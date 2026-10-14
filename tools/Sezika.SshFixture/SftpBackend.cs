using System.Diagnostics;
using System.Text;
using Sezika.RemoteTool;

namespace Sezika.SshFixture;

internal sealed record SftpOperationFailure(byte Operation, string Target, string ErrorType, string Message);
internal sealed record SftpOperationCount(byte Operation, int Count);

internal sealed class SftpBackend(string root, CancellationToken cancellation, string virtualRoot = "/fixture", int maximumRequests = 256,
    int maximumDirectoryEntries = 128, int maximumLifetimeHandles = 64, string? nativeImagePath = null, long nativeImageBytes = 1048576) : IDisposable
{
    private const int MaximumBytes = 1048576;
    private readonly Dictionary<string, FileStream> files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Path, bool Sent)> directories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> links = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> permissions = new(StringComparer.Ordinal);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<SftpOperationFailure> failures = [];
    private readonly int[] operationCounts = new int[256];
    private string lastTarget = "";
    private int handles;
    internal int Requests { get; private set; }
    internal int OpenHandles => files.Count + directories.Count;
    internal SftpOperationFailure[] Failures => failures.ToArray();
    internal SftpOperationCount[] OperationCounts => Enumerable.Range(0, 256).Where(i => operationCounts[i] != 0).Select(i => new SftpOperationCount((byte)i, operationCounts[i])).ToArray();

    internal async Task ServeAsync(Stream stream)
    {
        if (maximumRequests is < 1 or > 512 || maximumDirectoryEntries is < 1 or > 256 || maximumLifetimeHandles is < 1 or > 128 ||
            nativeImageBytes is < 1 or > 33554432 || nativeImagePath is not null && maximumRequests != 512)
            throw new ArgumentOutOfRangeException(nameof(maximumRequests));
        for (var packetIndex = 0; packetIndex < maximumRequests; packetIndex++)
        {
            cancellation.ThrowIfCancellationRequested(); if (clock.Elapsed.TotalSeconds >= 60) throw new TimeoutException("SFTP fixture deadline60s.");
            var prefix = new byte[4];
            var first = await stream.ReadAsync(prefix.AsMemory(0, 1), cancellation); if (first == 0) return;
            await stream.ReadExactlyAsync(prefix.AsMemory(1), cancellation);
            var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(prefix);
            if (size is < 1 or > 131072) throw new InvalidDataException("SFTP fixture packet byte cap exceeded.");
            var payload = new byte[(int)size]; await stream.ReadExactlyAsync(payload, cancellation);
            var reader = new WireReader(payload); var type = reader.Byte();
            using var reply = new WireWriter();
            if (type == 1) { if (reader.UInt32() != 3) throw new InvalidDataException("Only SFTP v3 is admitted by the fixture."); reply.Byte(2); reply.UInt32(3); }
            else
            {
                Requests++; operationCounts[type]++; var id = reader.UInt32();
                try { Dispatch(type, id, reader, reply); }
                catch (FileNotFoundException) { reply.Clear(); Status(reply, id, 2); }
                catch (DirectoryNotFoundException) { reply.Clear(); Status(reply, id, 2); }
                catch (UnauthorizedAccessException error) { RecordFailure(type, error); reply.Clear(); Status(reply, id, 3); }
                catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or KeyNotFoundException) { RecordFailure(type, error); reply.Clear(); Status(reply, id, 4); }
            }
            var result = reply.ToArray(); var length = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, (uint)result.Length);
            await stream.WriteAsync(length, cancellation); await stream.WriteAsync(result, cancellation); await stream.FlushAsync(cancellation);
        }
        throw new InvalidDataException("SFTP fixture configured request cap exceeded.");
    }

    private void Dispatch(byte type, uint id, WireReader reader, WireWriter reply)
    {
        cancellation.ThrowIfCancellationRequested(); lastTarget = "";
        switch (type)
        {
            case 3:
            {
                var path = Normalize(reader.Text()); var flags = reader.UInt32(); ReadAttributes(reader);
                if (++handles > maximumLifetimeHandles || files.Count + directories.Count >= 32) throw new InvalidDataException("Fixture file handle cap exceeded.");
                var mode = (flags & 8) == 0 ? FileMode.Open : (flags & 32) != 0 ? FileMode.CreateNew : (flags & 16) != 0 ? FileMode.Create : FileMode.OpenOrCreate;
                var access = (flags & 2) != 0 ? (flags & 1) != 0 ? FileAccess.ReadWrite : FileAccess.Write : FileAccess.Read;
                var file = new FileStream(Map(path, true), mode, access, FileShare.ReadWrite); var handle = "file-" + handles;
                if (file.Length > ReadLimit(path)) { file.Dispose(); throw new InvalidDataException("Fixture file byte cap exceeded."); }
                files.Add(handle, file); reply.Byte(102); reply.UInt32(id); reply.Text(handle); break;
            }
            case 4:
            {
                var handle = reader.Text(); if (files.Remove(handle, out var file)) file.Dispose(); else if (!directories.Remove(handle)) throw new IOException("Unknown fixture handle."); Status(reply, id, 0); break;
            }
            case 5:
            {
                var handle = reader.Text(); var file = files[handle]; lastTarget = PathForFileHandle(handle); var offset = reader.UInt64(); var count = reader.UInt32();
                if (file.Length > ReadLimit(lastTarget) || count > 65536) throw new InvalidDataException("Fixture read range cap exceeded.");
                // SSH.NET pipelines reads past the last chunk. Beyond EOF returns no bytes and must not seek or allocate a buffer.
                if (offset >= (ulong)file.Length) { Status(reply, id, 1); break; }
                file.Position = (long)offset; var data = new byte[count]; var read = file.Read(data);
                if (read == 0) Status(reply, id, 1); else { reply.Byte(103); reply.UInt32(id); reply.Bytes(data.AsSpan(0, read)); } break;
            }
            case 6:
            {
                var file = files[reader.Text()]; var offset = reader.UInt64(); var data = reader.Bytes(65536);
                if (offset + (ulong)data.Length > MaximumBytes) throw new InvalidDataException("Fixture write byte cap exceeded.");
                file.Position = (long)offset; file.Write(data); file.Flush(flushToDisk: true); Status(reply, id, 0); break;
            }
            case 7: case 17:
            {
                var path = Normalize(reader.Text()); reply.Byte(105); reply.UInt32(id); Attributes(reply, path, type == 17); break;
            }
            case 8:
            {
                var file = files[reader.Text()]; reply.Byte(105); reply.UInt32(id); FileAttributes(reply, file.Length, 0x8000 | 0x180, DateTime.UtcNow); break;
            }
            case 9: case 10:
            {
                var path = type == 9 ? Normalize(reader.Text()) : PathForFileHandle(reader.Text()); var attrs = ReadAttributes(reader);
                if (attrs.Mode is { } mode) { permissions[path] = mode & 0x1ff; if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Map(path, true), (UnixFileMode)(mode & 0x1ff)); }
                Status(reply, id, 0); break;
            }
            case 11:
            {
                var path = Normalize(reader.Text()); if (!Ancestor(path) && !Directory.Exists(Map(path, true))) throw new DirectoryNotFoundException();
                if (++handles > maximumLifetimeHandles || directories.Count + files.Count >= 32) throw new InvalidDataException("Fixture directory handle cap exceeded.");
                var handle = "directory-" + handles; directories.Add(handle, (path, false)); reply.Byte(102); reply.UInt32(id); reply.Text(handle); break;
            }
            case 12:
            {
                var handle = reader.Text(); var directory = directories[handle];
                lastTarget = directory.Path;
                if (directory.Sent) { Status(reply, id, 1); break; }
                directories[handle] = directory with { Sent = true }; var entries = Entries(directory.Path);
                reply.Byte(104); reply.UInt32(id); reply.UInt32((uint)entries.Length);
                foreach (var path in entries) { cancellation.ThrowIfCancellationRequested(); var name = path[(path.LastIndexOf('/') + 1)..]; reply.Text(name); reply.Text(name); Attributes(reply, path, false); } break;
            }
            case 13:
            {
                var path = Normalize(reader.Text());
                if (!links.Remove(path))
                {
                    var local = Map(path, false); if (Directory.Exists(local)) throw new InvalidDataException("Fixture REMOVE cannot remove a directory.");
                    if (!File.Exists(local)) throw new FileNotFoundException(); File.Delete(local);
                }
                Status(reply, id, 0); break;
            }
            case 14:
            {
                var path = Normalize(reader.Text()); ReadAttributes(reader); var local = Map(path, false); if (Path.Exists(local)) throw new IOException("Fixture mkdir target exists."); Directory.CreateDirectory(local); Status(reply, id, 0); break;
            }
            case 15: { var path = Normalize(reader.Text()); Directory.Delete(Map(path, false), recursive: false); Status(reply, id, 0); break; }
            case 16:
            {
                var path = Normalize(reader.Text()); path = Resolve(path); reply.Byte(104); reply.UInt32(id); reply.UInt32(1); reply.Text(path); reply.Text(path); Attributes(reply, path, true); break;
            }
            case 18:
            {
                var source = Normalize(reader.Text()); var target = Normalize(reader.Text());
                if (links.ContainsKey(source) || links.ContainsKey(target) || Path.Exists(Map(target, false))) throw new IOException("Fixture normal rename refuses existing/link targets.");
                File.Move(Map(source, false), Map(target, false), overwrite: false); Status(reply, id, 0); break;
            }
            case 19:
            {
                var path = Normalize(reader.Text()); if (!links.TryGetValue(path, out var target)) throw new IOException("Fixture readlink is not a link.");
                reply.Byte(104); reply.UInt32(id); reply.UInt32(1); reply.Text(target); reply.Text(target); Attributes(reply, target, true); break;
            }
            case 20:
            {
                var first = Normalize(reader.Text()); var second = Normalize(reader.Text());
                // OpenSSH historically reversed these fields. The fixture source must exist and the new link must be absent.
                var firstExists = Exists(first); var source = firstExists ? first : second; var link = firstExists ? second : first;
                if (!Exists(source) || Exists(link)) throw new IOException("Fixture symbolic link source/target differs."); links.Add(link, source); Status(reply, id, 0); break;
            }
            default: Status(reply, id, 8); break;
        }
    }

    private string Normalize(string path)
    {
        lastTarget = path;
        if (path == "." || path == "") return virtualRoot; if (path == "/") return path;
        if (!path.StartsWith('/')) path = virtualRoot + "/" + path; RemotePaths.Absolute(path);
        if (path != virtualRoot && !path.StartsWith(virtualRoot + "/", StringComparison.Ordinal) && !Ancestor(path)) throw new InvalidDataException("Fixture SFTP path escaped its one virtual root."); return path;
    }
    private string Resolve(string path)
    {
        for (var pass = 0; pass < 16; pass++)
        {
            var changed = false;
            foreach (var link in links)
            {
                if (path == link.Key || path.StartsWith(link.Key + "/", StringComparison.Ordinal)) { path = link.Value + path[link.Key.Length..]; changed = true; break; }
            }
            if (!changed) return path;
        }
        throw new InvalidDataException("Fixture link cycle/depth exceeded.");
    }
    private string Map(string path, bool follow)
    {
        if (follow) path = Resolve(path); if (path == virtualRoot) return root; if (!path.StartsWith(virtualRoot + "/", StringComparison.Ordinal)) throw new InvalidDataException("Fixture root mapping refused.");
        var relative = path[(virtualRoot.Length + 1)..]; var local = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!local.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Fixture physical path escaped."); return local;
    }
    private bool Ancestor(string path) => path == "/" || virtualRoot.StartsWith(path + "/", StringComparison.Ordinal);
    private bool Exists(string path) => path == virtualRoot || Ancestor(path) || links.ContainsKey(path) || Path.Exists(Map(path, false));
    private string[] Entries(string path)
    {
        if (Ancestor(path))
        {
            var prefix = path == "/" ? "/" : path + "/"; return [prefix + virtualRoot[prefix.Length..].Split('/')[0]];
        }
        var local = Map(path, true); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Directory.EnumerateFileSystemEntries(local)) { cancellation.ThrowIfCancellationRequested(); if (names.Count >= maximumDirectoryEntries) throw new InvalidDataException("Fixture directory entry cap exceeded."); names.Add(path + "/" + Path.GetFileName(item)); }
        foreach (var link in links.Keys) if (link[..link.LastIndexOf('/')] == path) { if (names.Count >= maximumDirectoryEntries) throw new InvalidDataException("Fixture link entry cap exceeded."); names.Add(link); }
        return names.Order(StringComparer.Ordinal).ToArray();
    }
    private void Attributes(WireWriter writer, string path, bool follow)
    {
        if (!follow && links.ContainsKey(path)) { FileAttributes(writer, 0, 0xa000 | 0x1ff, DateTime.UtcNow); return; }
        if (Ancestor(path)) { FileAttributes(writer, 0, 0x4000 | 0x1ed, DateTime.UtcNow); return; }
        var local = Map(path, follow); var mode = permissions.GetValueOrDefault(path, 0x180u);
        if (Directory.Exists(local)) FileAttributes(writer, 0, 0x4000 | 0x1ed, Directory.GetLastWriteTimeUtc(local));
        else { var info = new FileInfo(local); if (!info.Exists) throw new FileNotFoundException(); FileAttributes(writer, info.Length, 0x8000 | mode, info.LastWriteTimeUtc); }
    }
    private static void FileAttributes(WireWriter writer, long length, uint mode, DateTime stamp)
    {
        writer.UInt32(0xd); writer.UInt64((ulong)length); writer.UInt32(mode); var time = (uint)new DateTimeOffset(DateTime.SpecifyKind(stamp, DateTimeKind.Utc)).ToUnixTimeSeconds(); writer.UInt32(time); writer.UInt32(time);
    }
    private string PathForFileHandle(string handle) => files[handle].Name == root ? virtualRoot : virtualRoot + "/" + Path.GetRelativePath(root, files[handle].Name).Replace('\\', '/');
    private static (uint? Mode, long? Bytes) ReadAttributes(WireReader reader)
    {
        var flags = reader.UInt32(); long? bytes = (flags & 1) != 0 ? (long)reader.UInt64() : null;
        if ((flags & 2) != 0) { reader.UInt32(); reader.UInt32(); }
        uint? mode = (flags & 4) != 0 ? reader.UInt32() : null;
        if ((flags & 8) != 0) { reader.UInt32(); reader.UInt32(); }
        if ((flags & ~15u) != 0) throw new InvalidDataException("Fixture attributes extension unsupported."); return (mode, bytes);
    }
    private static void Status(WireWriter writer, uint id, uint code) { writer.Byte(101); writer.UInt32(id); writer.UInt32(code); writer.Text("fixture status"); writer.Text(""); }
    private long ReadLimit(string path) => path == nativeImagePath ? nativeImageBytes : MaximumBytes;
    private void RecordFailure(byte operation, Exception error)
    {
        if (failures.Count >= 8) return;
        var target = lastTarget.Length > 512 ? lastTarget[..512] : lastTarget;
        var message = error.Message.Length > 512 ? error.Message[..512] : error.Message;
        failures.Add(new(operation, target, error.GetType().Name, message));
    }
    public void Dispose() { foreach (var file in files.Values) file.Dispose(); files.Clear(); directories.Clear(); links.Clear(); permissions.Clear(); }
}
