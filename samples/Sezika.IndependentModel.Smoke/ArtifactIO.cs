using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sezika.IndependentSmoke;

internal sealed class Budget : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _cancel = new();
    private readonly double _seconds;
    private readonly bool _observeMemory;
    private long _nextMemorySample;
    internal Budget(int seconds, bool observeMemory = true) { ArtifactIO.Require(seconds is >= 1 and <= 100, "Invalid internal deadline"); _seconds = seconds; _observeMemory = observeMemory; _cancel.CancelAfter(TimeSpan.FromSeconds(seconds)); }
    internal CancellationToken Token => _cancel.Token;
    internal double Elapsed => _clock.Elapsed.TotalSeconds;
    internal void Cancel() => _cancel.Cancel();
    internal void Check()
    {
        Token.ThrowIfCancellationRequested();
        if (_clock.Elapsed.TotalSeconds >= _seconds) throw new TimeoutException("Internal deadline exceeded");
        if (_observeMemory && _clock.ElapsedMilliseconds >= _nextMemorySample)
        {
            _nextMemorySample = _clock.ElapsedMilliseconds + 250;
            using var process = Process.GetCurrentProcess();
            ArtifactIO.Require(Math.Max(process.WorkingSet64, process.PeakWorkingSet64) <= 4L * 1024 * 1024 * 1024, "Observed host peak exceeds 4GiB");
        }
    }
    public void Dispose() => _cancel.Dispose();
}
internal readonly record struct FileId(ulong Device, ulong Node);
internal static partial class ArtifactIO
{
    internal const int MaxMetadata = 1_048_576;
    internal static void Require([DoesNotReturnIf(false)] bool value, string message) { if (!value) throw new InvalidDataException(message); }
    internal static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));
    internal static string Safe(string path)
    {
        var full = Path.GetFullPath(path); var clock = Stopwatch.StartNew(); string? component = full;
        for (var depth = 0; depth < 64 && component is not null; depth++)
        {
            Require(clock.Elapsed < TimeSpan.FromSeconds(5), "Path inspection deadline exceeded");
            if (File.Exists(component) || Directory.Exists(component)) Require((File.GetAttributes(component) & FileAttributes.ReparsePoint) == 0, "Links/junctions unsupported");
            component = Path.GetDirectoryName(component);
        }
        Require(component is null, "Path ancestry exceeds 64"); return full;
    }
    internal static string Relative(string directory, string name)
    {
        Require(!string.IsNullOrWhiteSpace(name) && !Path.IsPathRooted(name), "Relative artifact required");
        var root = Safe(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Safe(Path.Combine(root, name));
        Require(result.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Artifact escaped input root"); return result;
    }
    internal static FileStream Lease(string path) => new(Safe(path), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
    internal static byte[] Small(FileStream stream, Budget budget)
    {
        budget.Check(); Require(stream.Length is > 0 and <= MaxMetadata, "Metadata byte bound exceeded");
        stream.Position = 0; var result = new byte[(int)stream.Length]; stream.ReadExactly(result); budget.Check(); return result;
    }
    internal static Artifact Identity(string path, FileStream stream, Budget budget, long maximum, string? expected = null, long? extent = null)
    {
        budget.Check(); var size = stream.Length; Require(size > 0 && size <= maximum && (extent is null || size == extent), "Asset extent differs");
        stream.Position = 0; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var block = new byte[1024 * 1024];
        var count = (size + block.Length - 1) / block.Length; Require(count <= 2048, "Hash block bound exceeded"); long left = size;
        for (var i = 0; i < count; i++) { budget.Check(); var n = (int)Math.Min(left, block.Length); stream.ReadExactly(block.AsSpan(0, n)); hash.AppendData(block, 0, n); left -= n; }
        Require(left == 0 && stream.ReadByte() == -1, "Asset changed during hash"); var digest = Convert.ToHexString(hash.GetHashAndReset());
        Require(expected is null || digest.Equals(expected, StringComparison.OrdinalIgnoreCase), "Asset SHA differs: " + path); return new(Safe(path), digest, size);
    }
    internal static void JsonGuard(byte[] bytes, Budget budget)
    {
        Require(bytes.Length is > 0 and <= MaxMetadata, "JSON byte bound exceeded");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var pending = new Stack<JsonElement>(); pending.Push(doc.RootElement);
        for (var n = 0; n < 100_000 && pending.Count > 0; n++)
        {
            budget.Check(); var item = pending.Pop();
            if (item.ValueKind == JsonValueKind.Object)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in item.EnumerateObject()) { budget.Check(); Require(seen.Add(property.Name), "Duplicate JSON property"); pending.Push(property.Value); }
            }
            else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) { budget.Check(); pending.Push(child); }
            else if (item.ValueKind == JsonValueKind.Number) Require(item.TryGetDouble(out var number) && double.IsFinite(number), "Non-finite JSON number");
        }
        Require(pending.Count == 0, "JSON node bound exceeded");
    }
    internal static Artifact Publish(string destination, byte[] bytes, Budget budget, Action? afterMove = null)
    {
        var path = Safe(destination); Require(bytes.Length <= MaxMetadata && !File.Exists(path) && !Directory.Exists(path), "Output exists or exceeds 1MiB");
        Require(Directory.Exists(Path.GetDirectoryName(path)), "Output parent must exist");
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N"); FileId? owned = null; bool moved = false;
        try
        {
            budget.Check();
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { owned = Id(stream); stream.Write(bytes); stream.Flush(true); }
            budget.Check(); File.Move(temp, path, overwrite: false); moved = true; afterMove?.Invoke(); budget.Check();
            return new(path, Hash(bytes), bytes.Length);
        }
        catch
        {
            if (moved) RemoveOwned(path, owned, Hash(bytes));
            throw;
        }
        finally { if (File.Exists(temp)) RemoveOwned(temp, owned, null); }
    }
    internal static void RemoveOwned(string path, FileId? owned, string? digest)
    {
        path = Safe(path); Require(owned is not null, "Unknown temporary ownership; retained");
        using (var stream = Lease(path))
        {
            Require(Id(stream) == owned, "File identity changed; retained");
            using var cleanup = new Budget(5, observeMemory: false); cleanup.Check();
            Require(stream.Length is >= 0 and <= MaxMetadata, "Owned cleanup extent exceeds bound");
            var actualHash = stream.Length == 0 ? Hash([]) : Identity(path, stream, cleanup, MaxMetadata).Sha256;
            Require(digest is null || actualHash == digest, "Owned output bytes changed; retained");
        }
        File.Delete(path);
    }
    internal static FileId Id(FileStream stream)
    {
        if (OperatingSystem.IsWindows())
        {
            Require(GetFileInformationByHandle(stream.SafeFileHandle.DangerousGetHandle(), out var info), "Cannot obtain owned file ID");
            return new(info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow);
        }
        Require(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64, "Owned file IDs support Windows or Linux x64");
        Require(FStat(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), out var linux) == 0, "Cannot obtain Linux owned file ID"); return new(linux.Device, linux.Inode);
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowsInfo
    { internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [StructLayout(LayoutKind.Sequential)] private struct LinuxInfo
    {
        internal ulong Device, Inode, Links;
        internal uint Mode, Uid, Gid, Padding;
        internal ulong RDevice;
        internal long Size, BlockSize, Blocks, AccessSeconds, AccessNanos, ModifySeconds, ModifyNanos, ChangeSeconds, ChangeNanos, Reserved1, Reserved2, Reserved3;
    }
    [LibraryImport("kernel32", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(nint file, out WindowsInfo information);
    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static partial int FStat(int file, out LinuxInfo information);
}
