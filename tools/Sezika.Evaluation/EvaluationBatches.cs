using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed record OracleBatch(string Manifest, string ManifestSha256, int Offset, string[] CaseIds);
internal sealed record OracleBatchPlan(string SchemaVersion, string DatasetSha256, int DatasetTotal,
    string MeasurementStatus, List<OracleBatch> Batches);
internal sealed record CaptureBatchInput(string Capture, string CaptureSha256, string Manifest, string ManifestSha256);
internal sealed record CaptureBatchIndex(string SchemaVersion, List<CaptureBatchInput> Batches);
internal sealed record CaptureBatchEvidence(string Capture, string CaptureSha256, string ManifestSha256,
    int Processed, JsonElement? Provenance, JsonElement? Implementation);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(OracleBatchPlan))]
[JsonSerializable(typeof(CaptureBatchIndex))]
internal partial class BatchJsonContext : JsonSerializerContext;

internal static class EvaluationBatches
{
    internal static (int Offset, int Count)[] Ranges(int total, int batchSize)
    {
        if (total is < 1 or > 10000 || batchSize is < 1 or > 46)
            throw new InvalidDataException("Batch bounds must be total 1..10000 and size 1..46.");
        var count = (total + batchSize - 1) / batchSize;
        if (count > 256) throw new InvalidDataException("At most 256 batches are permitted; increase the batch size.");
        return Enumerable.Range(0, count).Select(index => (index * batchSize, Math.Min(batchSize, total - index * batchSize))).ToArray();
    }

    internal static void Prepare(string directory, Dictionary<string, JsonElement> source, string hash, int batchSize, CancellationToken token)
    {
        var ranges = Ranges(source.Count, batchSize);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Batch output must be a new directory.");
        Directory.CreateDirectory(directory);
        var batches = new List<OracleBatch>();
        var ids = source.Keys.ToArray();
        for (var index = 0; index < ranges.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var (offset, count) = ranges[index];
            var name = $"cases-{index + 1:D3}.json";
            var path = Path.Combine(directory, name);
            EvaluationOracle.WriteManifest(path, source, hash, count, token, offset);
            batches.Add(new(name, EvaluationInputs.FileHash(path, token), offset, ids.Skip(offset).Take(count).ToArray()));
            Console.WriteLine($"Prepared oracle batch {index + 1}/{ranges.Length}: rows {offset + 1}..{offset + count}/{source.Count}.");
        }
        token.ThrowIfCancellationRequested();
        // Only a fully prepared set receives an index. Partial files remain evidence of interrupted preparation.
        using var output = new FileStream(Path.Combine(directory, "batches.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, new OracleBatchPlan("sezika.evaluation-oracle-batches.v1", hash, source.Count,
            "not_executed", batches), BatchJsonContext.Default.OracleBatchPlan);
    }

    internal static EvaluationReport Score(string indexPath, Dictionary<string, JsonElement> source, string datasetHash,
        int total, DateTimeOffset started, Stopwatch watch, CancellationToken token)
    {
        var indexHash = EvaluationInputs.FileHash(indexPath, token);
        if (new FileInfo(indexPath).Length > 1_048_576) throw new InvalidDataException("Capture index exceeds 1 MiB.");
        using var indexDocument = EvaluationInputs.ParseJson(File.ReadAllBytes(indexPath), 10000, 16, token);
        var index = JsonSerializer.Deserialize(indexDocument.RootElement, BatchJsonContext.Default.CaptureBatchIndex)
            ?? throw new InvalidDataException("Missing capture index.");
        if (index.SchemaVersion != "sezika.evaluation-captures.v1" || index.Batches is null || index.Batches.Count is < 1 or > 256)
            throw new InvalidDataException("Expected 1..256 indexed captures.");
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(indexPath))!;
        var rows = new List<EvaluationRow>();
        var evidence = new List<CaptureBatchEvidence>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? identity = null, backend = null, policy = null;
        long bytes = 0;
        for (var batch = 0; batch < index.Batches.Count; batch++)
        {
            token.ThrowIfCancellationRequested();
            var item = index.Batches[batch];
            var capturePath = Path.GetFullPath(item.Capture, baseDirectory);
            var manifestPath = Path.GetFullPath(item.Manifest, baseDirectory);
            bytes = checked(bytes + new FileInfo(capturePath).Length + new FileInfo(manifestPath).Length);
            if (bytes > 256L * 1024 * 1024) throw new InvalidDataException("Indexed captures exceed the 256 MiB batch bound.");
            RequireHash(capturePath, item.CaptureSha256, token);
            RequireHash(manifestPath, item.ManifestSha256, token);
            if (new FileInfo(manifestPath).Length > 1_048_576) throw new InvalidDataException("Manifest exceeds 1 MiB.");
            using var manifest = EvaluationInputs.ParseJson(File.ReadAllBytes(manifestPath), 65536, 32, token);
            var root = manifest.RootElement;
            if (root.GetProperty("schema_version").GetString() != "sezika.laya-oracle-inputs.v1" ||
                root.GetProperty("dataset_sha256").GetString() != datasetHash || root.GetProperty("dataset_total").GetInt32() != total)
                throw new InvalidDataException("Batch manifest dataset identity differs.");
            var ids = root.GetProperty("cases").EnumerateArray().Select(row => row.GetProperty("id").GetString()!).ToArray();
            if (ids.Length is < 1 or > 46 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                throw new InvalidDataException("Batch manifest IDs are invalid or duplicate.");
            var report = EvaluationOracle.ReadScore(capturePath, source, datasetHash, total, started, watch, token);
            if (report.CaptureCasesSha256 != item.ManifestSha256.ToLowerInvariant() ||
                report.CaptureSha256 != item.CaptureSha256.ToLowerInvariant() || report.CaptureManifestCases != ids.Length ||
                !ids.SequenceEqual(report.Rows.Select(row => row.Id)))
                throw new InvalidDataException("Capture does not cover its complete ordered batch manifest.");
            var currentIdentity = StableIdentity(report);
            if (identity is not null && (identity != currentIdentity || backend != report.Backend || policy != report.LengthPolicy))
                throw new InvalidDataException("Mixed capture implementation, backend, policy or reference provenance.");
            identity = currentIdentity; backend = report.Backend; policy = report.LengthPolicy;
            AddUnique(rows, seen, report.Rows);
            evidence.Add(new(capturePath, report.CaptureSha256!, item.ManifestSha256.ToLowerInvariant(), report.Processed,
                report.CaptureProvenance, report.CaptureImplementation));
            RequireHash(manifestPath, item.ManifestSha256, token);
            Console.WriteLine($"Scored batch {batch + 1}/{index.Batches.Count}: {rows.Count}/{total} dataset rows.");
        }
        // Restore source order before metrics, including counterfactual family grouping.
        var byId = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        rows = source.Keys.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var summary = Evaluation.Summarize(rows, backend!, Path.GetFileNameWithoutExtension(indexPath), total, datasetHash,
            started, watch.Elapsed.TotalMilliseconds, policy!, token);
        summary.MeasurementOrigin = "offline_scoring_of_existing_capture_batches_not_new_inference_or_parity_proof";
        summary.CaptureStatus = rows.Count == total ? "complete_dataset" : "partial_dataset";
        summary.CaptureSelectedCases = rows.Count;
        summary.CaptureBatches = evidence;
        RequireHash(indexPath, indexHash, token);
        return summary;
    }

    internal static void AddUnique(List<EvaluationRow> rows, HashSet<string> seen, List<EvaluationRow> addition)
    {
        foreach (var row in addition)
        {
            if (!seen.Add(row.Id)) throw new InvalidDataException("Duplicate case across capture batches.");
            rows.Add(row);
        }
    }

    private static string StableIdentity(EvaluationReport report)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("provenance");
            foreach (var property in report.CaptureProvenance!.Value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                if (property.Name != "cases_sha256") property.WriteTo(writer);
            writer.WriteEndObject();
            if (report.CaptureImplementation is { } implementation)
            {
                writer.WriteStartObject("implementation");
                foreach (var property in implementation.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                    if (property.Name is not ("process_id" or "process_started_utc" or "arguments" or "timeout_seconds" or "max_cases")) property.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return EvaluationInputs.TextHash(System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
    }

    private static void RequireHash(string path, string expected, CancellationToken token)
    {
        if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit) ||
            !EvaluationInputs.FileHash(path, token).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Indexed evidence SHA-256 mismatch.");
    }
}
