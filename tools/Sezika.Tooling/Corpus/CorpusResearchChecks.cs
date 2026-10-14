using System.Buffers.Binary;
using System.Text.Json;
using static Sezika.Tooling.CorpusJson;

namespace Sezika.Tooling;

internal static partial class CorpusOperations
{
    internal static CorpusResearchCheckReport CheckResearch(CorpusResearchCheckRequest request, Budget budget)
    {
        Require(request.Plans.Length is >= 1 and <= 16 && request.Ledgers.Length is >= 1 and <= 32 && request.MaximumFiles is >= 1 and <= 50_000 && request.MaximumLedgerRows is >= 1 and <= 200_000, "Research completeness bounds.");
        var root = Path.GetFullPath(request.Root); SafePaths.NoLinks(root, budget); var plans = new Dictionary<string, JsonElement>(StringComparer.Ordinal); var latest = new Dictionary<string, JsonElement>(StringComparer.Ordinal); var downloaded = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var sourcePlans = new Dictionary<string, SourceResearchPlanEntry>(StringComparer.Ordinal);
        var sourceDownloads = new Dictionary<string, SourceAcquisitionEntry>(StringComparer.Ordinal);
        static string Field(JsonElement row, string lower, string upper) => row.TryGetProperty(lower, out var value) ? Text(value) : Str(row, upper);
        foreach (var path in request.Plans)
        {
            budget.Check(); var plan = Read(path, budget); Require(plan.ValueKind == JsonValueKind.Array && plan.GetArrayLength() <= request.MaximumFiles, "Research plan row bound.");
            foreach (var row in plan.EnumerateArray())
            {
                budget.Check(); var url = Field(row, "url", "Url"); Require(Uri.TryCreate(url, UriKind.Absolute, out _), "Research plan absolute URL required.");
                var isSourcePlan = row.TryGetProperty("source_acquisition_plan_sha256", out _) || row.TryGetProperty("expected_digest", out _);
                if (plans.TryGetValue(url, out var existing))
                {
                    Require(Field(existing, "dataset", "Dataset") == Field(row, "dataset", "Dataset") && Field(existing, "path", "Path") == Field(row, "path", "Path"), "Conflicting research plan URL identity.");
                    Require(sourcePlans.ContainsKey(url) == isSourcePlan, "Legacy/source acquisition plan schemas cannot replace one another.");
                }
                if (isSourcePlan)
                {
                    var sourcePlan = row.Deserialize(SourceAcquisitionJsonContext.Default.SourceResearchPlanEntry) ?? throw new InvalidDataException("Source research plan entry missing.");
                    AssetTransport.ValidateSha(sourcePlan.SourceAcquisitionPlanSha256); AssetTransport.ValidateContentDigest(sourcePlan.ExpectedDigest);
                    Require(sourcePlan.ExpectedBytes is > 0 and <= AssetTransport.MaximumFileBytes && sourcePlan.Url == url &&
                        sourcePlan.ExpectedSha256 == (sourcePlan.ExpectedDigest.Kind == "sha256" ? sourcePlan.ExpectedDigest.Value : null), "Source research trusted expected digest/length differs.");
                    if (sourcePlans.TryGetValue(url, out var previous)) Require(previous == sourcePlan, "Conflicting source research plan identity.");
                    sourcePlans[url] = sourcePlan;
                }
                plans[url] = row; Require(plans.Count <= request.MaximumFiles, "Combined plan row bound.");
            }
        }
        var ledgerCount = 0;
        foreach (var path in request.Ledgers)
            foreach (var row in Lines(path, request.MaximumLedgerRows, budget))
            {
                budget.Check(); Require(++ledgerCount <= request.MaximumLedgerRows, "Combined ledger row bound.");
                if (row.TryGetProperty("official_url", out _) || row.TryGetProperty("source_id", out _) || row.TryGetProperty("plan_sha256", out _) || row.TryGetProperty("expected_digest", out _))
                {
                    var source = row.Deserialize(SourceAcquisitionJsonContext.Default.SourceAcquisitionEntry) ?? throw new InvalidDataException("Source acquisition receipt missing.");
                    if (!sourcePlans.TryGetValue(source.OfficialUrl, out var sourcePlan)) throw new InvalidDataException("Source acquisition receipt requires its complete typed research plan.");
                    Require(source.PlanSha256 == sourcePlan.SourceAcquisitionPlanSha256 && source.Path == sourcePlan.Path && source.SourceId == sourcePlan.Dataset &&
                        source.Revision == sourcePlan.Revision && source.Split == sourcePlan.Split && source.ExpectedBytes == sourcePlan.ExpectedBytes && source.ExpectedDigest == sourcePlan.ExpectedDigest &&
                        !source.FormalTrainingAdmitted, "Source acquisition plan SHA/path/source/revision/digest identity differs.");
                    Require(source.Status is "DOWNLOADED_VERIFIED" or "CACHED_VERIFIED" or "NOT_SELECTED" or "NOT_ATTEMPTED" or "FAILED" or "CANCELLED" or "TIMED_OUT", "Unknown source acquisition receipt status.");
                    latest[source.OfficialUrl] = row;
                    if (source.Status is "DOWNLOADED_VERIFIED" or "CACHED_VERIFIED")
                    {
                        Require(IsSha(source.ContentSha256) && source.Bytes == sourcePlan.ExpectedBytes, "Completed source acquisition content SHA/bytes differ.");
                        sourceDownloads[source.OfficialUrl] = source; downloaded[source.OfficialUrl] = row;
                    }
                }
                else
                {
                    var url = Field(row, "url", "Url"); Require(!sourcePlans.ContainsKey(url), "Typed source plans require typed source receipt identity."); latest[url] = row;
                    if (Field(row, "status", "Status") is "downloaded" or "PASS_VERIFIED_DOWNLOAD") downloaded[url] = row;
                }
            }
        var inventories = new List<CorpusSourceInventory>(); var missing = new List<CorpusSourceMissing>();
        foreach (var item in plans.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            budget.Check(); var plan = item.Value; var dataset = Field(plan, "dataset", "Dataset"); var relative = Field(plan, "path", "Path"); var path = SafePaths.Within(root, relative, budget);
            var expectedBytesValue = plan.TryGetProperty("expected_bytes", out var lowerBytes) ? lowerBytes : plan.TryGetProperty("ExpectedBytes", out var upperBytes) ? upperBytes : default;
            long? expectedBytes = expectedBytesValue.ValueKind == JsonValueKind.Number ? expectedBytesValue.GetInt64() : null;
            var expectedHash = Optional(plan, "expected_sha256") ?? Optional(plan, "ExpectedSha256");
            if (downloaded.TryGetValue(item.Key, out var receipt) && File.Exists(path))
            {
                sourceDownloads.TryGetValue(item.Key, out var sourceReceipt);
                var sha = sourceReceipt is not null ? sourceReceipt.ContentSha256! : Field(receipt, "sha256", "Sha256");
                var receiptBytes = sourceReceipt is not null ? sourceReceipt.Bytes : receipt.TryGetProperty("bytes", out var b) ? b.GetInt64() : receipt.GetProperty("Bytes").GetInt64();
                Require(IsSha(sha) && receiptBytes >= 0 && new FileInfo(path).Length == receiptBytes && (!expectedBytes.HasValue || expectedBytes == receiptBytes) && (expectedHash is null || sha == expectedHash), "Downloaded source declared SHA/bytes differs.");
                if (request.VerifyHashes)
                {
                    Require(FileHash(path, budget) == sha, "Downloaded source independent SHA differs.");
                    if (sourceReceipt is not null) Require(AssetTransport.VerifyDownloadedFile(path, sourceReceipt.ExpectedBytes, sourceReceipt.ExpectedDigest, budget) == sha, "Downloaded source original expected digest differs.");
                }
                long? rows = null; string? countSource = null; string? countError = null;
                try
                {
                    if (path.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase)) { rows = CorpusParquetFooter.ReadRows(path, budget); countSource = "Parquet FileMetaData.num_rows footer only"; }
                    else if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                    {
                        using var reader = new StreamReader(path, Utf8, true); long count = 0;
                        for (var line = 0; line < 50_000_000; line++) { budget.Check(); var text = ReadLine(reader, MaximumRowBytes, budget); if (text is null) break; if (!string.IsNullOrWhiteSpace(text)) count++; if (line == 49_999_999) Require(reader.Read() == -1, "Physical row count bound."); }
                        rows = count; countSource = "nonblank JSONL physical lines; no schema validity, uniqueness or decision-count inference";
                    }
                }
                catch (Exception error) when (error is InvalidDataException or EndOfStreamException or OverflowException) { countError = error.GetType().Name + ": " + error.Message; }
                inventories.Add(new(dataset, relative, Optional(plan, "split") ?? Optional(plan, "Split") ?? "unclassified-review-required", Optional(plan, "revision") ?? Optional(plan, "Revision"), item.Key, receiptBytes, sha, rows, countSource, countError));
            }
            else
            {
                long bytes;
                if (sourcePlans.ContainsKey(item.Key)) bytes = SourceResearchPartialBytes(path, budget);
                else { var partial = path + ".range-partial"; SafePaths.NoLinks(partial, budget); bytes = File.Exists(partial) ? new FileInfo(partial).Length : 0; }
                latest.TryGetValue(item.Key, out var attempt); missing.Add(new(dataset, item.Key, relative, expectedBytes, expectedHash, bytes,
                    attempt.ValueKind == JsonValueKind.Object ? Optional(attempt, "status") ?? Optional(attempt, "Status") : null, attempt.ValueKind == JsonValueKind.Object ? Optional(attempt, "error") ?? Optional(attempt, "Error") ?? Optional(attempt, "failure_type") : null));
            }
        }
        var groups = plans.Values.GroupBy(row => Field(row, "dataset", "Dataset"), StringComparer.Ordinal).Take(97).ToArray(); Require(groups.Length <= 96, "Research source count.");
        var sources = groups.Select(group =>
        { var complete = inventories.Where(i => i.Dataset == group.Key).ToArray(); var absent = missing.Where(i => i.Dataset == group.Key).ToArray(); return new CorpusSourceCompleteness(group.Key, group.Count(), complete.Length, complete.Sum(i => i.Bytes), absent.Length, absent.Sum(i => i.ExpectedBytes ?? 0), absent.Sum(i => i.PartialBytes), absent.Length == 0); }).ToArray();
        var report = new CorpusResearchCheckReport(missing.Count == 0 ? "COMPLETE_SOURCE_FILES_ONLY" : "PARTIAL_SOURCE_FILES_PRESERVE_LEDGER", plans.Count, inventories.Count, inventories.Sum(i => i.Bytes), inventories.Sum(i => i.Rows ?? 0), inventories.Count(i => i.Rows.HasValue), sources.Count(s => s.Complete), missing.Count, sources, inventories.ToArray(), missing.ToArray(), missing.Count == 0);
        Write(request.Output, report, CorpusJsonContext.Default.CorpusResearchCheckReport, budget); return report;
    }
    private static long SourceResearchPartialBytes(string path, Budget budget)
    {
        long bytes = 0;
        foreach (var partial in new[] { path + ".sezika-source-mirror.sezika-part", path + ".sezika-source-official.sezika-part" })
        {
            budget.Check(); SafePaths.NoLinks(partial, budget);
            if (!File.Exists(partial)) continue;
            RegularFiles.Require(partial, budget); bytes = checked(bytes + new FileInfo(partial).Length);
        }
        return bytes;
    }
    internal static CorpusSeedCheckReport CheckSeeds(CorpusSeedCheckRequest request, Budget budget)
    {
        Require(request.MaximumRecords is >= 1 and <= 200_000 && IsSha(request.BinarySha256) && IsSha(request.ParallelSha256) && IsSha(request.EvaluationManifestSha256), "Seed check bounds/identity.");
        Require(FileHash(request.Binary, budget) == request.BinarySha256 && FileHash(request.Parallel, budget) == request.ParallelSha256 && FileHash(request.EvaluationManifest, budget) == request.EvaluationManifestSha256, "Pinned independent seed bytes differ.");
        var binary = new Dictionary<string, double>(StringComparer.Ordinal); var instances = new HashSet<string>(StringComparer.Ordinal); long binaryRows = 0; long parallelRows = 0; long pairs = 0; long probabilityErrors = 0; long consistencyErrors = 0; long queryErrors = 0;
        foreach (var row in Lines(request.Binary, request.MaximumRecords, budget))
        {
            budget.Check(); var id = Str(row, "instance_id"); Require(instances.Add(id), "Duplicate binary seed ID."); var outcomes = Strings(row.GetProperty("outcomes")); Require(outcomes.Length >= 2, "Source outcomes missing."); var distribution = row.GetProperty("gold_distribution"); if (!Normalized(distribution)) probabilityErrors++; binary.Add(id, Number(distribution.GetProperty(outcomes[0]))); binaryRows++;
        }
        instances.Clear();
        foreach (var row in Lines(request.Parallel, request.MaximumRecords, budget))
        {
            budget.Check(); Require(instances.Add(Str(row, "instance_id")), "Duplicate parallel seed ID."); var distribution = row.GetProperty("gold_distribution"); if (!Normalized(distribution)) probabilityErrors++; var positive = Number(distribution.GetProperty("True"));
            var id = Str(row.GetProperty("generator_metadata").GetProperty("parallel_primitives"), "source_instance_id"); if (binary.TryGetValue(id, out var expected)) { pairs++; if (Math.Abs(expected - positive) > 1e-10) consistencyErrors++; } else consistencyErrors++;
            var names = row.GetProperty("queries").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray(); if (!names.SequenceEqual(new[] { "choice", "noul", "score" })) queryErrors++; parallelRows++;
        }
        var manifest = Read(request.EvaluationManifest, budget); var evaluation = manifest.GetProperty("evaluation"); Require(evaluation.GetArrayLength() <= 4096, "Evaluation subset count."); var missing = new List<string>(); var fileCount = 0;
        foreach (var subset in evaluation.EnumerateArray())
            foreach (var file in Strings(subset.GetProperty("data_files"))) { budget.Check(); Require(++fileCount <= 50_000, "Evaluation declared file count."); var path = SafePaths.Within(request.EvaluationRoot, file, budget); if (!File.Exists(path)) missing.Add(file); }
        var result = new CorpusSeedCheckReport(probabilityErrors + consistencyErrors + queryErrors + missing.Count == 0 && binaryRows > 0 && parallelRows > 0 ? "PASS_SEED_CONTRACT_ONLY" : "FAIL_SEED_CONTRACT", binaryRows, parallelRows, pairs, probabilityErrors, consistencyErrors, queryErrors, evaluation.GetArrayLength(), fileCount, missing.ToArray(), "Official frozen parallel primitive contract: outcomes[0] maps to True; source_instance_id binds paired rows; reference distributions preserved, not recomputed");
        Write(request.Output, result, CorpusJsonContext.Default.CorpusSeedCheckReport, budget); return result;
        static bool Normalized(JsonElement distribution)
        { var fields = distribution.EnumerateObject().Take(33).ToArray(); Require(fields.Length is >= 2 and <= 32, "Independent reference distribution dimension."); var values = fields.Select(p => Number(p.Value)).ToArray(); return values.All(v => double.IsFinite(v) && v is >= 0 and <= 1) && Math.Abs(values.Sum() - 1) <= 1e-10; }
    }
}

internal sealed class CorpusParquetFooter
{
    private readonly byte[] bytes; private readonly Budget budget; private int position; private long operations;
    private CorpusParquetFooter(byte[] bytes, Budget budget) { this.bytes = bytes; this.budget = budget; }
    private void Check(int depth = 0) { budget.Check(); Require(depth <= 64 && ++operations <= bytes.LongLength * 4 + 1, "Parquet compact footer operation/depth bound."); }
    private byte Byte() { Check(); if (position >= bytes.Length) throw new EndOfStreamException("Parquet compact footer ended."); return bytes[position++]; }
    private ulong Varint()
    {
        ulong value = 0;
        for (var index = 0; index < 10; index++) { var current = Byte(); Require(index != 9 || current <= 1, "Parquet varint overflow."); value |= (ulong)(current & 127) << (7 * index); if ((current & 128) == 0) return value; }
        throw new InvalidDataException("Parquet varint length bound.");
    }
    private void Advance(ulong count) { Check(); Require(count <= (ulong)(bytes.Length - position), "Parquet metadata truncated."); position += checked((int)count); }
    private void Skip(int type, int depth, bool collection = false)
    {
        Check(depth);
        switch (type)
        {
            case 1: case 2: if (collection) Byte(); return;
            case 3: Byte(); return;
            case 4: case 5: case 6: Varint(); return;
            case 7: Advance(8); return;
            case 8: Advance(Varint()); return;
            case 9: case 10:
                var header = Byte(); var count = (ulong)(header >> 4); if (count == 15) count = Varint(); Require(count <= (ulong)bytes.Length, "Parquet list bound."); for (ulong i = 0; i < count; i++) Skip(header & 15, depth + 1, true); return;
            case 11:
                var pairs = Varint(); Require(pairs <= (ulong)bytes.Length, "Parquet map bound."); if (pairs == 0) return; var types = Byte(); for (ulong i = 0; i < pairs; i++) { Skip(types >> 4, depth + 1, true); Skip(types & 15, depth + 1, true); } return;
            case 12:
                for (var i = 0; i < bytes.Length; i++) { var field = Byte(); if (field == 0) return; if (field >> 4 == 0) Varint(); Skip(field & 15, depth + 1); } throw new InvalidDataException("Parquet struct bound.");
            default: throw new InvalidDataException("Unknown Parquet compact type.");
        }
    }
    private long Rows()
    {
        var fieldId = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            var field = Byte(); if (field == 0) break; var delta = field >> 4;
            if (delta == 0) { var encoded = Varint(); fieldId = checked((int)((long)(encoded >> 1) ^ -((long)encoded & 1))); } else fieldId += delta;
            if (fieldId == 3) { Require((field & 15) == 6, "Parquet num_rows must be I64."); var encoded = Varint(); var rows = (long)(encoded >> 1) ^ -((long)encoded & 1); Require(rows >= 0, "Negative Parquet row count."); return rows; }
            Skip(field & 15, 1);
        }
        throw new InvalidDataException("Parquet num_rows missing.");
    }
    internal static long ReadRows(string path, Budget budget)
    {
        budget.Check(); SafePaths.NoLinks(path, budget); using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); Require(file.Length >= 12, "Parquet minimum length.");
        Span<byte> head = stackalloc byte[4]; file.ReadExactly(head); Require(head.SequenceEqual("PAR1"u8), "Parquet header identity.");
        Span<byte> tail = stackalloc byte[8]; file.Position = file.Length - 8; file.ReadExactly(tail); Require(tail[4..].SequenceEqual("PAR1"u8), "Unsupported or encrypted Parquet footer.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(tail); Require(length is >= 1 and <= 64 * 1024 * 1024 && length <= file.Length - 12, "Parquet footer byte bound.");
        var data = new byte[length]; file.Position = file.Length - 8 - length; file.ReadExactly(data); return new CorpusParquetFooter(data, budget).Rows();
    }
}
