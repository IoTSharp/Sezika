using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record ReceiptSnapshotRequest(string SourceRoot, string[] Paths, ReviewFilePin[]? ExpectedFiles = null,
    int MaximumFiles = 32, int MaximumFileBytes = 65536, int MaximumTotalBytes = 1048576, int MaximumSeconds = 20);
internal sealed record ReceiptSnapshotFile(string Path, long Bytes, string Sha256, string Base64);
internal sealed record ReceiptSnapshotReport(string Status, int RequestedFiles, int CompletedFiles, long TotalBytes,
    ReceiptSnapshotFile[] Files, string? Error, bool ReadOnly = true, bool AtomicProducerSnapshot = false,
    bool WeightsOrCorpusDownloaded = false, bool TrainingAccepted = false, bool PublicReleaseAccepted = false);

internal static class ReceiptSnapshot
{
    internal static ReceiptSnapshotReport Capture(ReceiptSnapshotRequest request, Budget parent)
    {
        parent.Check();
        if (request.MaximumFiles is < 1 or > 32 || request.MaximumFileBytes is < 1 or > 65536 ||
            request.MaximumTotalBytes is < 1 or > 1048576 || request.MaximumSeconds is < 1 or > 20 ||
            request.Paths.Length < 1 || request.Paths.Length > request.MaximumFiles ||
            !Path.IsPathFullyQualified(request.SourceRoot))
            throw new InvalidDataException("Named receipt snapshot bounds or absolute source root differ.");
        var budget = new Budget(request.MaximumSeconds, null, parent.Cancellation, parent);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.SourceRoot));
        if (root == Path.GetPathRoot(root) || root.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidDataException("Receipt snapshot requires an existing local task directory.");
        SafePaths.NoLinks(root, budget);
        if (!Directory.Exists(root)) throw new InvalidDataException("Receipt snapshot requires an existing local task directory.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in request.Paths)
        {
            budget.Check();
            if (SafePaths.Relative(path) != path || Path.GetExtension(path).ToLowerInvariant() is not
                (".json" or ".jsonl" or ".txt" or ".log" or ".tsv" or ".csv" or ".sha256" or ".md") || !names.Add(path))
                throw new InvalidDataException("Unique portable declared metadata receipt paths required.");
        }
        var pins = new Dictionary<string, ReviewFilePin>(StringComparer.Ordinal);
        if (request.ExpectedFiles is not null)
        {
            if (request.ExpectedFiles.Length != request.Paths.Length) throw new InvalidDataException("Complete optional receipt pins required.");
            foreach (var pin in request.ExpectedFiles)
            {
                budget.Check(); TaskOperations.Sha(pin.Sha256);
                if (!request.Paths.Contains(pin.Path, StringComparer.Ordinal) || pin.Bytes < 0 || pin.Bytes > request.MaximumFileBytes ||
                    !pins.TryAdd(pin.Path, pin)) throw new InvalidDataException("Optional receipt pin scope/bytes differ.");
            }
        }
        var files = new List<ReceiptSnapshotFile>(request.Paths.Length); long total = 0;
        ReceiptSnapshotReport Report(string status, string? error = null) => new(status, request.Paths.Length, files.Count, total, files.ToArray(), error);
        try
        {
            foreach (var name in request.Paths)
            {
                budget.Check();
                var path = SafePaths.Within(root, name, budget); RegularFiles.Require(path, budget);
                var length = new FileInfo(path).Length;
                if (length > request.MaximumFileBytes || length > request.MaximumTotalBytes - total)
                    throw new InvalidDataException("Receipt snapshot byte bound exceeded.");
                if (pins.TryGetValue(name, out var expected) && expected.Bytes != length)
                    throw new InvalidDataException("Receipt expected length changed.");
                byte[] bytes;
                using (var input = File.OpenRead(path))
                using (var memory = new MemoryStream(checked((int)length)))
                {
                    var buffer = new byte[1024]; var eof = false;
                    for (var block = 0; block < 65; block++)
                    {
                        budget.Check(); var count = input.Read(buffer); budget.Check();
                        if (count == 0) { eof = true; break; }
                        if (memory.Length + count > length) throw new InvalidDataException("Receipt changed during snapshot read.");
                        memory.Write(buffer, 0, count);
                    }
                    if (!eof || memory.Length != length) throw new InvalidDataException("Receipt snapshot EOF/length differs.");
                    bytes = memory.ToArray();
                }
                var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (expected is not null && sha != expected.Sha256) throw new InvalidDataException("Receipt expected SHA changed.");
                // Reopen the path as well as the original stream, detecting a
                // replacement between reads. This is not a multi-file atomic save.
                TaskOperations.VerifyFile(path, length, sha, budget);
                files.Add(new(name, length, sha, Convert.ToBase64String(bytes))); total += length;
            }
            budget.Check();
            return Report("PASS_named_small_receipt_snapshot_only");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or OperationCanceledException or TimeoutException)
        {
            return Report(error is OperationCanceledException or TimeoutException
                ? "FAIL_cancelled_or_timed_out_receipt_snapshot_partial" : "FAIL_named_receipt_snapshot_partial", error.GetType().Name);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ReceiptSnapshotRequest))]
[JsonSerializable(typeof(ReceiptSnapshotReport))]
internal partial class ReceiptSnapshotJson : JsonSerializerContext;
