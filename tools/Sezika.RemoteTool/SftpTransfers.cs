using System.Security.Cryptography;
using System.Text.Json;
using Renci.SshNet.Sftp;
using Sezika.Tooling;

namespace Sezika.RemoteTool;

internal static class SftpTransfers
{
    private const int BufferBytes = 65536;
    internal static async Task<RemoteTransferReport> DownloadAsync(PinnedSession session, TransferRequest request, string destination,
        Budget budget, CancellationToken cancellation)
    {
        Validate(session.Configuration, request); var remote = RemotePaths.Within(session.Configuration.Root, request.Path);
        var initial = await RequiredFile(session, remote, request.Bytes, budget, cancellation);
        destination = Path.GetFullPath(destination); SafePaths.NoLinks(destination, budget);
        if (Path.Exists(destination)) throw new IOException("Completed local destination already exists.");
        if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new DirectoryNotFoundException("Local destination parent must exist.");
        var partial = destination + ".sezika-part"; var statePath = destination + ".sezika-sftp.json";
        SafePaths.NoLinks(partial, budget); SafePaths.NoLinks(statePath, budget);
        var expected = new RemoteTransferState(1, request.OperationId, session.Configuration.HostName, session.Configuration.Port,
            session.Configuration.HostKeySha256, remote, request.Bytes, request.Sha256, initial.LastWriteTimeUtc);
        long resumed = 0;
        if (Path.Exists(partial) || Path.Exists(statePath))
        {
            RegularFiles.Require(partial, budget);
            var actual = AssetTransport.ReadJson(statePath, RemoteJsonContext.Default.RemoteTransferState, budget);
            if (actual != expected) throw new InvalidDataException("SFTP resume host/key/path/length/SHA/mtime identity differs.");
            resumed = new FileInfo(partial).Length;
            if (resumed > request.Bytes) throw new InvalidDataException("Local partial exceeds the trusted length.");
        }
        else AssetTransport.WriteJson(statePath, expected, RemoteJsonContext.Default.RemoteTransferState, budget);
        using (var deadline = PinnedSession.Deadline(budget, cancellation, session.Configuration.MaximumSeconds))
        await using (var source = await session.Sftp.OpenAsync(remote, FileMode.Open, FileAccess.Read, deadline.Token))
        {
            source.Position = resumed;
            using var target = new FileStream(partial, resumed > 0 || File.Exists(partial) ? FileMode.Open : FileMode.CreateNew, FileAccess.Write, FileShare.None);
            target.Position = resumed;
            await CopyExactAsync(source, target, request.Bytes - resumed, session, budget, deadline.Token); target.Flush(flushToDisk: true);
        }
        var after = await RequiredFile(session, remote, request.Bytes, budget, cancellation);
        if (after.LastWriteTimeUtc != initial.LastWriteTimeUtc || new FileInfo(partial).Length != request.Bytes || AssetTransport.FileHash(partial, budget) != request.Sha256)
            throw new InvalidDataException("SFTP source changed or complete local bytes/SHA differ.");
        File.Move(partial, destination, overwrite: false);
        if (AssetTransport.FileHash(destination, budget) != request.Sha256) throw new InvalidDataException("Independent local disk readback differs.");
        File.Delete(statePath);
        return new("PASS_sftp_download_complete_bytes_sha", request.OperationId, session.Configuration.HostName, session.Configuration.Port,
            session.Configuration.HostKeySha256, remote, destination, request.Bytes, request.Sha256, resumed, true);
    }

    internal static async Task<RemoteTransferReport> UploadAsync(PinnedSession session, TransferRequest request, string sourcePath,
        Budget budget, CancellationToken cancellation)
    {
        Validate(session.Configuration, request); sourcePath = Path.GetFullPath(sourcePath); RegularFiles.Require(sourcePath, budget);
        var sourceInfo = new FileInfo(sourcePath);
        if (sourceInfo.Length != request.Bytes || AssetTransport.FileHash(sourcePath, budget) != request.Sha256) throw new InvalidDataException("Local upload source bytes/SHA differ.");
        var remote = RemotePaths.Within(session.Configuration.Root, request.Path);
        if (request.Executable && !session.Configuration.AllowedExecutables.Any(p => p.Path == remote && p.Sha256 == request.Sha256) &&
            !(session.Configuration.WorkerPath == remote && session.Configuration.WorkerSha256 == request.Sha256))
            throw new InvalidDataException("Executable upload is outside the frozen native allowlist.");
        var parent = remote[..remote.LastIndexOf('/')]; await session.CreateDirectoriesAsync(parent, budget, cancellation);
        var partial = remote + "." + request.OperationId + ".sezika-part"; var statePath = remote + "." + request.OperationId + ".sezika-sftp.json";
        var preflight = await session.UploadAttributesAsync(remote, partial, statePath, budget, cancellation);
        if (preflight.Destination is not null) throw new IOException("Remote completed destination exists; preserve it.");
        var expected = new RemoteTransferState(1, request.OperationId, session.Configuration.HostName, session.Configuration.Port,
            session.Configuration.HostKeySha256, remote, request.Bytes, request.Sha256, sourceInfo.LastWriteTimeUtc);
        var partAttributes = preflight.Partial; var stateAttributes = preflight.State; long resumed = 0;
        if (partAttributes is not null || stateAttributes is not null)
        {
            if (partAttributes is null || stateAttributes is null || !partAttributes.IsRegularFile || !stateAttributes.IsRegularFile || stateAttributes.Size > 65536)
                throw new InvalidDataException("Remote partial has no valid owned resume state.");
            var actual = await ReadStateAsync(session, statePath, stateAttributes.Size, budget, cancellation);
            if (actual != expected || partAttributes.Size > request.Bytes) throw new InvalidDataException("Remote SFTP resume source/key/path identity differs.");
            resumed = partAttributes.Size;
        }
        else
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(expected, RemoteJsonContext.Default.RemoteTransferState);
            using var deadline = PinnedSession.Deadline(budget, cancellation, 30);
            await using var state = await session.Sftp.OpenAsync(statePath, FileMode.CreateNew, FileAccess.Write, deadline.Token);
            await state.WriteAsync(bytes, deadline.Token); await state.FlushAsync(deadline.Token);
        }
        using (var deadline = PinnedSession.Deadline(budget, cancellation, session.Configuration.MaximumSeconds))
        await using (var target = await session.Sftp.OpenAsync(partial, partAttributes is null ? FileMode.CreateNew : FileMode.Open, FileAccess.Write, deadline.Token))
        using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            target.Position = resumed; source.Position = resumed;
            await CopyExactAsync(source, target, request.Bytes - resumed, session, budget, deadline.Token); await target.FlushAsync(deadline.Token);
        }
        if (AssetTransport.FileHash(sourcePath, budget) != request.Sha256 || await HashRemoteAsync(session, partial, request.Bytes, budget, cancellation) != request.Sha256)
            throw new InvalidDataException("Full upload source/remote readback SHA differs; retain partial.");
        if (await session.AttributesAsync(remote, true, budget, cancellation) is not null) throw new IOException("Remote target appeared during upload; no overwrite allowed.");
        if (request.Executable) session.Sftp.ChangePermissions(partial, 0x1ed); // 0755, only a frozen native executable.
        using (var deadline = PinnedSession.Deadline(budget, cancellation, 30)) await session.Sftp.RenameFileAsync(partial, remote, deadline.Token);
        if (await HashRemoteAsync(session, remote, request.Bytes, budget, cancellation) != request.Sha256) throw new InvalidDataException("Renamed remote asset independent readback differs.");
        // This exact state was either created here or read and matched above; no other remote objects are deleted.
        using (var deadline = PinnedSession.Deadline(budget, cancellation, 30)) await session.Sftp.DeleteFileAsync(statePath, deadline.Token);
        return new("PASS_sftp_upload_complete_readback_bytes_sha", request.OperationId, session.Configuration.HostName, session.Configuration.Port,
            session.Configuration.HostKeySha256, remote, sourcePath, request.Bytes, request.Sha256, resumed, true);
    }

    internal static void Validate(RemoteConfiguration config, TransferRequest request)
    {
        RemotePaths.Validate(config); RemotePaths.Identity(request.OperationId); RemotePaths.Sha(request.Sha256);
        _ = RemotePaths.Within(config.Root, request.Path);
        if (request.SchemaVersion != 1 || request.Bytes < 0 || request.Bytes > config.MaximumFileBytes) throw new InvalidDataException("SFTP request byte/schema bound differs.");
    }
    private static async Task<SftpFileAttributes> RequiredFile(PinnedSession session, string remote, long bytes, Budget budget, CancellationToken cancellation)
    {
        var attributes = await session.AttributesAsync(remote, false, budget, cancellation);
        if (attributes is null || !attributes.IsRegularFile || attributes.Size != bytes) throw new InvalidDataException("Remote regular file/trusted size differs."); return attributes;
    }
    internal static async Task<string> HashRemoteAsync(PinnedSession session, string path, long bytes, Budget budget, CancellationToken cancellation)
    {
        _ = await RequiredFile(session, path, bytes, budget, cancellation);
        return await HashOpenedRemoteAsync(session, path, bytes, budget, cancellation);
    }
    internal static async Task<(long Bytes, string Sha256)> InspectAndHashRemoteAsync(PinnedSession session, string path, long maximumBytes, Budget budget, CancellationToken cancellation)
    {
        var attributes = await session.AttributesAsync(path, false, budget, cancellation);
        if (attributes is null || !attributes.IsRegularFile || attributes.Size < 0 || attributes.Size > maximumBytes)
            throw new InvalidDataException("Remote inspected regular file size differs from its explicit byte cap.");
        return (attributes.Size, await HashOpenedRemoteAsync(session, path, attributes.Size, budget, cancellation));
    }
    private static async Task<string> HashOpenedRemoteAsync(PinnedSession session, string path, long bytes, Budget budget, CancellationToken cancellation)
    {
        using var deadline = PinnedSession.Deadline(budget, cancellation, session.Configuration.MaximumSeconds);
        await using var stream = await session.Sftp.OpenAsync(path, FileMode.Open, FileAccess.Read, deadline.Token);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[BufferBytes]; long total = 0;
        for (long readIndex = 0; readIndex < bytes + 1; readIndex++)
        {
            budget.Check(); using var operation = PinnedSession.Deadline(budget, deadline.Token, 30);
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, bytes - total + 1)), operation.Token);
            if (read == 0) { if (total != bytes) throw new EndOfStreamException("Remote object truncated during SHA."); return Convert.ToHexStringLower(hash.GetHashAndReset()); }
            total += read; if (total > bytes) throw new InvalidDataException("Remote object exceeded trusted length."); hash.AppendData(buffer, 0, read);
        }
        throw new InvalidDataException("Remote SHA read iteration cap exceeded.");
    }
    private static async Task<RemoteTransferState> ReadStateAsync(PinnedSession session, string path, long bytes, Budget budget, CancellationToken cancellation)
    {
        using var memory = new MemoryStream(); using var deadline = PinnedSession.Deadline(budget, cancellation, 30);
        await using var stream = await session.Sftp.OpenAsync(path, FileMode.Open, FileAccess.Read, deadline.Token);
        await CopyExactAsync(stream, memory, bytes, session, budget, deadline.Token);
        using var document = JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 16 }); AssetTransport.ValidateJson(document.RootElement, budget);
        return document.RootElement.Deserialize(RemoteJsonContext.Default.RemoteTransferState) ?? throw new InvalidDataException("Remote resume state missing.");
    }
    private static async Task CopyExactAsync(Stream source, Stream target, long bytes, PinnedSession session, Budget budget, CancellationToken cancellation)
    {
        var buffer = new byte[BufferBytes]; long remaining = bytes;
        for (long readIndex = 0; readIndex < bytes + 1; readIndex++)
        {
            budget.Check(); using var operation = PinnedSession.Deadline(budget, cancellation, 30);
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining + 1)), operation.Token);
            if (read == 0) { if (remaining != 0) throw new EndOfStreamException("SFTP copy truncated; owned partial retained."); return; }
            if (read > remaining) throw new InvalidDataException("SFTP copy exceeded trusted byte length.");
            await target.WriteAsync(buffer.AsMemory(0, read), operation.Token); remaining -= read;
        }
        throw new InvalidDataException("SFTP copy read cap exceeded.");
    }
}
