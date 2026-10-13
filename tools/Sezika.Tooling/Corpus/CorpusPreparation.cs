using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using static Sezika.Tooling.CorpusJson;

namespace Sezika.Tooling;

internal static partial class CorpusOperations
{
    internal static CorpusManifest Prepare(CorpusPrepareRequest request, Budget budget, Func<string, int, IEnumerable<JsonElement>>? parquetReader = null)
    {
        Require(request.MaximumRecords is >= 1 and <= 50_000_000 && request.MaximumSources is >= 1 and <= 96 && request.RowsPerShard is >= 1 and <= 50_000 && request.PilotPerFile is >= 0 and <= 1000, "Preparation bounds.");
        var root = Path.GetFullPath(request.DatasetRoot); SafePaths.NoLinks(root, budget);
        var assetsHash = FileHash(request.AssetManifest, budget); var rightsHash = FileHash(request.RightsManifest, budget);
        HashSet<(string Path, long Row)>? recoveryRows = null;
        if (request.RecoverLedger is not null)
        {
            Require(request.SourceFilter == "dwidlee/systemone-lite-phase2" && IsSha(request.RecoverLedgerSha256) && FileHash(request.RecoverLedger, budget) == request.RecoverLedgerSha256, "Pinned standalone phase2 recovery ledger required.");
            recoveryRows = [];
            foreach (var item in Lines(request.RecoverLedger, 2_000_000, budget))
            { budget.Check(); if (Optional(item, "source") == request.SourceFilter && Str(item, "reason").EndsWith("phase2_non_choice_or_label", StringComparison.Ordinal)) { var number = item.GetProperty("row").GetInt64(); Require(number is >= 1 and <= 5_000_000, "Recovery source row bound."); recoveryRows.Add((Str(item, "path"), number)); } }
            Require(recoveryRows.Count > 0, "No selected recoverable source rows.");
        }
        else Require(request.RecoverLedgerSha256 is null, "Recovery SHA supplied without ledger.");
        var assets = Read(request.AssetManifest, CorpusJsonContext.Default.CorpusAssetArray, budget);
        var rights = Read(request.RightsManifest, CorpusJsonContext.Default.CorpusRightsArray, budget);
        Require(assets.Length <= 50_000 && rights.Length <= 96 && rights.Select(r => r.Dataset).Distinct(StringComparer.Ordinal).Count() == rights.Length, "Asset/rights bounds or duplicate source.");
        var selected = assets.Where(asset => (request.SourceFilter is null || asset.Dataset == request.SourceFilter) && rights.Any(r => r.Dataset == asset.Dataset && r.Decision == "eligible_under_declared_terms_with_attribution") && Included(asset))
            .OrderBy(asset => asset.Dataset, StringComparer.Ordinal).ThenBy(asset => asset.Path, StringComparer.Ordinal).ToArray();
        Require(selected.Length > 0 && selected.Select(a => a.Dataset).Distinct(StringComparer.Ordinal).Count() <= request.MaximumSources, "No selected assets or source bound exceeded.");
        var output = Path.GetFullPath(request.Output); var stage = 1; CorpusManifest? prior = null;
        if (request.Resume)
        {
            SafePaths.NoLinks(output, budget); Require(File.ReadAllText(Path.Combine(output, "OWNED_BY_SEZIKA_CORPUS")) == "csharp-corpus-v1\n", "Owned C# corpus required.");
            prior = Read(Path.Combine(output, "manifest.json"), CorpusJsonContext.Default.CorpusManifest, budget);
            Require(prior.Status == "PARTIAL_FAILED_PRESERVE_OUTPUTS" && prior.ToolingVersion == "csharp-corpus-v1" && prior.Stage < 4 && prior.AssetsSha256 == assetsHash && prior.RightsSha256 == rightsHash && prior.RecoveryLedgerSha256 == request.RecoverLedgerSha256 && !File.Exists(Path.Combine(output, "corpus-bindings.json")), "Resume identity/status/stage differs.");
            stage = prior.Stage + 1; File.Move(Path.Combine(output, "manifest.json"), Path.Combine(output, $"manifest-stage-{prior.Stage:0000}.json"));
        }
        else
        {
            output = NewOutput(output, budget); File.WriteAllText(Path.Combine(output, "OWNED_BY_SEZIKA_CORPUS"), "csharp-corpus-v1\n", Utf8);
            File.Copy(request.AssetManifest, Path.Combine(output, "source-assets.json")); File.Copy(request.RightsManifest, Path.Combine(output, "source-rights.json"));
        }
        using var writer = new CorpusWriter(output, request.MaximumRecords, request.RowsPerShard, stage, budget, prior);
        var coverage = prior?.SourceCoverage.ToList() ?? []; string? failure = null;
        var activeCoverage = -1; var pendingBefore = writer.Records; long activeRowsRead = 0;
        try
        {
            foreach (var asset in selected)
            {
                budget.Check(); var existing = coverage.FindIndex(entry => entry.Source == asset.Dataset && entry.Path == asset.Path);
                if (existing >= 0 && coverage[existing].Complete) continue;
                Require(IsSha(asset.Sha256), "Source SHA required."); var path = SafePaths.Within(root, asset.Path, budget);
                Require(new FileInfo(path).Length == asset.Bytes && FileHash(path, budget) == asset.Sha256, "Immutable source SHA/size differs.");
                var rightsEntry = rights.Single(entry => entry.Dataset == asset.Dataset);
                if (!string.IsNullOrWhiteSpace(rightsEntry.Evidence))
                {
                    var source = SafePaths.Within(root, rightsEntry.Evidence, budget); var target = SafePaths.Within(output, "source-notices/" + rightsEntry.Evidence, budget);
                    if (!File.Exists(target)) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target); }
                    Require(FileHash(source, budget) == FileHash(target, budget), "Archived license evidence bytes differ.");
                }
                var maximum = request.PilotPerFile == 0 ? 5_000_000 : request.PilotPerFile;
                string[]? names = null; string[]? scenarios = null;
                if (asset.Dataset is "PolyAI/banking77" or "clinc/clinc_oos" or "AmazonScience/massive")
                {
                    var nameSet = new HashSet<string>(StringComparer.Ordinal); var scenarioSet = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var row in Rows(path, asset.Dataset, 5_000_000, budget, parquetReader, false))
                    {
                        budget.Check(); nameSet.Add(asset.Dataset == "clinc/clinc_oos" ? Text(row[1]) : Str(row, asset.Dataset == "PolyAI/banking77" ? "category" : "intent"));
                        if (asset.Dataset == "AmazonScience/massive") scenarioSet.Add(Str(row, "scenario"));
                        Require(nameSet.Count <= 4096 && scenarioSet.Count <= 32, "Class inventory bounds.");
                    }
                    names = nameSet.Order(StringComparer.Ordinal).ToArray(); scenarios = scenarioSet.Order(StringComparer.Ordinal).ToArray();
                }
                var resumeRow = existing < 0 ? 0 : coverage[existing].RowsFullyProcessed; var before = writer.Records; long number = 0;
                if (existing < 0) { coverage.Add(new(asset.Dataset, asset.Path, 0, 0, 0, false, request.PilotPerFile > 0)); existing = coverage.Count - 1; }
                activeCoverage = existing; pendingBefore = writer.Records; activeRowsRead = 0;
                foreach (var row in Rows(path, asset.Dataset, maximum, budget, parquetReader, request.PilotPerFile > 0))
                {
                    budget.Check(); number++; activeRowsRead = number; if (number <= resumeRow || recoveryRows is not null && !recoveryRows.Contains((asset.Path, number))) continue;
                    try
                    {
                        var questions = CorpusAdapters.Adapt(asset.Dataset, row, names, scenarios); Require(questions.Length <= 256, "Per-row question bound.");
                        foreach (var question in questions) writer.Add(question, new(asset.Dataset, asset.SourceRevision, asset.Path, asset.Sha256, asset.Dataset == "Team-ACE/ToolACE" ? "unsplit" : "train", number,
                            rightsEntry.License, rightsEntry.Evidence ?? asset.LicenseEvidence, question.SourceMember), Sha(Canonical(row)));
                    }
                    catch (Exception error) when (error is InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
                    { writer.Reject(asset.Dataset, asset.Path, number, error.GetType().Name + ":" + error.Message); }
                    coverage[existing] = coverage[existing] with { RowsRead = number, RecordsWritten = coverage[existing].RecordsWritten + writer.Records - before, RowsFullyProcessed = number };
                    before = writer.Records;
                    pendingBefore = writer.Records;
                }
                coverage[existing] = coverage[existing] with { RowsRead = number, RowsFullyProcessed = number, Complete = true };
                activeCoverage = -1;
            }
        }
        catch (Exception error)
        {
            if (activeCoverage >= 0) coverage[activeCoverage] = coverage[activeCoverage] with
            { RowsRead = activeRowsRead, RecordsWritten = coverage[activeCoverage].RecordsWritten + writer.Records - pendingBefore };
            failure = error.GetType().Name + ": " + error.Message; throw;
        }
        finally
        {
            writer.Close(); var finalBudget = new Budget(90, null, CancellationToken.None);
            var manifest = new CorpusManifest(1, failure is null ? "COMPLETE_PREPARATION_ONLY" : "PARTIAL_FAILED_PRESERVE_OUTPUTS", stage,
                writer.Records, writer.Duplicates, writer.Conflicts, writer.Quarantined, writer.Shards(finalBudget), coverage.ToArray(), assetsHash, rightsHash, Failure: failure, RecoveryLedgerSha256: request.RecoverLedgerSha256, Pilot: request.PilotPerFile > 0);
            Write(Path.Combine(output, "manifest.json"), manifest, CorpusJsonContext.Default.CorpusManifest, finalBudget);
        }
        return Read(Path.Combine(output, "manifest.json"), CorpusJsonContext.Default.CorpusManifest, budget);
    }
    private static bool Included(CorpusAsset asset)
    {
        if (asset.Split is not null && asset.Split is not "train" and not "source-train" and not "unsplit" and not "unclassified-review-required") return false;
        var name = asset.Path; var include = Path.GetFileName(name).Contains("train", StringComparison.OrdinalIgnoreCase) && (name.EndsWith(".parquet", StringComparison.Ordinal) || name.EndsWith(".jsonl", StringComparison.Ordinal) || name.EndsWith(".jsonl.gz", StringComparison.Ordinal) || name.EndsWith(".jsonl.xz", StringComparison.Ordinal) || name.EndsWith(".csv", StringComparison.Ordinal));
        return asset.Dataset switch
        {
            "LocalLLaMA/typed-decisions" => include && !name.Contains("/all/", StringComparison.Ordinal),
            "google-research-datasets/go_emotions" => include && name.Contains("/simplified/", StringComparison.Ordinal),
            "clinc/clinc_oos" => name.EndsWith("/data_full.json", StringComparison.Ordinal),
            "samatv256/jev-decisions-v1" => include && name.Contains("/clean50k/", StringComparison.Ordinal),
            "ZefanCai/Open-Jev" => include && name.EndsWith(".parquet", StringComparison.Ordinal),
            "AmazonScience/massive" => name.EndsWith("/amazon-massive-dataset-1.1.tar.gz", StringComparison.Ordinal),
            "Team-ACE/ToolACE" => name.EndsWith("/data.json", StringComparison.Ordinal), _ => include
        };
    }
    internal static IEnumerable<JsonElement> Rows(string path, string source, int maximum, Budget budget, Func<string, int, IEnumerable<JsonElement>>? parquet, bool pilot)
    {
        if (path.EndsWith(".parquet", StringComparison.Ordinal) || path.EndsWith(".xz", StringComparison.Ordinal))
        {
            Require(parquet is not null, "Parquet/XZ requires DatasetTool corpus prepare or inspect; no downloaded loader is executed.");
            var count = 0; foreach (var row in parquet!(path, pilot ? maximum : maximum + 1)) { budget.Check(); if (count++ >= maximum) { Require(pilot, "Parquet row bound exceeded."); yield break; } yield return row; } yield break;
        }
        if (source == "AmazonScience/massive")
        {
            using var file = File.OpenRead(path); using var gzip = new GZipStream(file, CompressionMode.Decompress); using var tar = new TarReader(gzip);
            var count = 0;
            for (var member = 0; member < 512; member++)
            {
                budget.Check(); var entry = tar.GetNextEntry(); if (entry is null) yield break;
                Require(entry.Length <= 64 * 1024 * 1024 && entry.EntryType is not TarEntryType.SymbolicLink and not TarEntryType.HardLink, "MASSIVE archive member bounds.");
                if (!entry.Name.EndsWith(".jsonl", StringComparison.Ordinal) || entry.DataStream is null) continue;
                using var reader = new StreamReader(entry.DataStream, Utf8, true, 4096, true);
                for (var index = 0; index < 100_000; index++)
                {
                    budget.Check(); var text = ReadLine(reader, MaximumRowBytes, budget); if (text is null) break;
                    using var doc = JsonDocument.Parse(text); if (Str(doc.RootElement, "partition") != "train") continue;
                    if (count++ >= maximum) { Require(pilot, "MASSIVE train rows exceeded."); yield break; }
                    using var memory = new MemoryStream(); using (var writer = new Utf8JsonWriter(memory)) { writer.WriteStartObject(); foreach (var p in doc.RootElement.EnumerateObject()) p.WriteTo(writer); writer.WriteString("_member", entry.Name); writer.WriteEndObject(); }
                    using var expanded = JsonDocument.Parse(memory.ToArray()); yield return expanded.RootElement.Clone();
                    if (index == 99_999 && reader.Read() != -1) throw new InvalidDataException("MASSIVE member rows exceeded.");
                }
            }
            Require(tar.GetNextEntry() is null, "MASSIVE member count exceeded."); yield break;
        }
        if (path.EndsWith(".json", StringComparison.Ordinal) || path.EndsWith(".json.gz", StringComparison.Ordinal))
        {
            JsonElement json;
            if (path.EndsWith(".gz", StringComparison.Ordinal))
            {
                using var file = File.OpenRead(path); using var gzip = new GZipStream(file, CompressionMode.Decompress); using var buffer = new MemoryStream(); var bytes = new byte[8192];
                for (var block = 0; block < 8193; block++) { budget.Check(); var count = gzip.Read(bytes); if (count == 0) break; buffer.Write(bytes, 0, count); Require(buffer.Length <= 64 * 1024 * 1024, "Decoded JSON carrier byte bound."); }
                using var document = JsonDocument.Parse(buffer.ToArray()); json = document.RootElement.Clone();
            }
            else json = Read(path, budget);
            JsonElement[] rows;
            if (source == "clinc/clinc_oos")
            { var train = json.GetProperty("train"); var oos = json.GetProperty("oos_train"); Require(train.GetArrayLength() + oos.GetArrayLength() <= 5_000_000, "CLINC rows bound."); rows = train.EnumerateArray().Concat(oos.EnumerateArray()).ToArray(); }
            else if (json.ValueKind == JsonValueKind.Array) { Require(json.GetArrayLength() <= 5_000_000, "JSON array rows bound."); rows = json.EnumerateArray().ToArray(); }
            else if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array) { Require(data.GetArrayLength() <= 5_000_000, "JSON data rows bound."); rows = data.EnumerateArray().ToArray(); }
            else if (json.ValueKind == JsonValueKind.Object)
            {
                var properties = json.EnumerateObject().Take(4097).ToArray(); Require(properties.Length <= 4096, "JSON column count bound.");
                if (properties.Length > 0 && properties.All(property => property.Value.ValueKind == JsonValueKind.Array))
                {
                    var count = properties[0].Value.GetArrayLength(); Require(count <= 5_000_000 && properties.All(property => property.Value.GetArrayLength() == count), "Columnar JSON row dimensions differ."); Require(count <= maximum || pilot, "Columnar row bound.");
                    for (var row = 0; row < Math.Min(count, maximum); row++)
                    {
                        budget.Check(); using var memory = new MemoryStream(); using (var writer = new Utf8JsonWriter(memory)) { writer.WriteStartObject(); foreach (var property in properties) { writer.WritePropertyName(property.Name); property.Value[row].WriteTo(writer); } writer.WriteEndObject(); }
                        using var document = JsonDocument.Parse(memory.ToArray()); yield return document.RootElement.Clone();
                    }
                    yield break;
                }
                rows = [json];
            }
            else throw new InvalidDataException("JSON carrier must be record array, data array, columnar object or single object.");
            Require(rows.Length <= maximum || pilot, "Source rows item bound."); foreach (var row in rows.Take(maximum)) { budget.Check(); yield return row; } yield break;
        }
        if (path.EndsWith(".csv", StringComparison.Ordinal))
        { foreach (var row in Csv(path, maximum, budget, pilot)) yield return row; yield break; }
        var read = 0;
        foreach (var row in Lines(path, pilot ? maximum + 1 : maximum, budget))
        { if (read++ >= maximum) yield break; yield return row; }
    }
    private static IEnumerable<JsonElement> Csv(string path, int maximum, Budget budget, bool pilot)
    {
        using var reader = new StreamReader(path, Utf8, true); var header = CsvRow(reader, budget); Require(header is not null && header.Length is >= 1 and <= 4096 && header.Distinct(StringComparer.Ordinal).Count() == header.Length, "CSV header invalid.");
        for (var index = 0; index < maximum; index++)
        {
            budget.Check(); var fields = CsvRow(reader, budget); if (fields is null) yield break; Require(fields.Length == header!.Length, "CSV field dimension.");
            using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); for (var i = 0; i < fields.Length; i++) writer.WriteString(header[i], fields[i]); writer.WriteEndObject(); }
            using var doc = JsonDocument.Parse(stream.ToArray()); yield return doc.RootElement.Clone();
        }
        Require(pilot || reader.Read() == -1, "CSV item bound.");
    }
    private static string[]? CsvRow(TextReader reader, Budget budget)
    {
        var fields = new List<string>(); var field = new StringBuilder(); var quoted = false; var any = false;
        for (var index = 0; index <= MaximumRowBytes; index++)
        {
            if ((index & 4095) == 0) budget.Check(); var c = reader.Read();
            if (c == -1) { Require(!quoted, "Unclosed CSV quote."); if (!any) return null; fields.Add(field.ToString()); return fields.ToArray(); }
            any = true;
            if (c == '"') { if (quoted && reader.Peek() == '"') { reader.Read(); field.Append('"'); } else { Require(quoted || field.Length == 0, "CSV quote inside unquoted field."); quoted = !quoted; } }
            else if (!quoted && c is ',' or '\n' or '\r')
            { fields.Add(field.ToString()); Require(fields.Count <= 4096, "CSV column bound."); field.Clear(); if (c != ',') { if (c == '\r' && reader.Peek() == '\n') reader.Read(); return fields.ToArray(); } }
            else field.Append((char)c);
        }
        throw new InvalidDataException("CSV row byte bound.");
    }
}

internal sealed class CorpusDiskIndex : IDisposable
{
    private const int EntryBytes = 65;
    private readonly FileStream file; private readonly long slots; private readonly Budget budget;
    internal CorpusDiskIndex(string path, int maximum, Budget budget)
    {
        this.budget = budget; slots = 16;
        for (var iteration = 0; iteration < 28 && slots < (long)maximum * 2; iteration++) slots *= 2;
        Require(slots >= (long)maximum * 2 && slots <= 134_217_728, "Disk dedup table capacity.");
        file = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.RandomAccess | FileOptions.DeleteOnClose); file.SetLength(slots * EntryBytes);
    }
    internal string Add(string key, string target, bool forceConflict = false)
    {
        var keyBytes = Convert.FromHexString(key); var targetBytes = Convert.FromHexString(target); var slot = BinaryPrimitives.ReadUInt64LittleEndian(keyBytes) % (ulong)slots;
        var entry = new byte[EntryBytes];
        for (var probe = 0; probe < 4096; probe++)
        {
            budget.Check(); file.Position = (long)((slot + (ulong)probe) % (ulong)slots) * EntryBytes; file.ReadExactly(entry);
            if (entry[64] == 0) { keyBytes.CopyTo(entry, 0); targetBytes.CopyTo(entry, 32); entry[64] = forceConflict ? (byte)2 : (byte)1; file.Position -= EntryBytes; file.Write(entry); return forceConflict ? "excluded" : "new"; }
            if (!entry.AsSpan(0, 32).SequenceEqual(keyBytes)) continue;
            if (entry[64] == 2) return "excluded";
            if (forceConflict || !entry.AsSpan(32, 32).SequenceEqual(targetBytes)) { entry[64] = 2; file.Position -= EntryBytes; file.Write(entry); return "conflict"; }
            return "duplicate";
        }
        throw new InvalidDataException("Disk dedup probe bound exceeded.");
    }
    public void Dispose() => file.Dispose();
}

internal sealed class CorpusWriter : IDisposable
{
    internal const long MaximumLogicalShardBytes = 512L * 1024 * 1024;
    private readonly string root; private readonly int maximum; private readonly int rowsPerShard; private readonly int stage; private readonly Budget budget;
    private readonly CorpusDiskIndex index; private readonly StreamWriter exclusions; private readonly StreamWriter quarantine;
    private readonly List<CorpusShard> shards; private StreamWriter? current; private GZipStream? compressed; private FileStream? shardFile;
    private string? currentSource; private string? currentSplit; private int currentRows; private int shardNumber; private bool closed;
    private readonly long maximumLogicalShardBytes; private long currentLogicalBytes;
    internal long Records { get; private set; } internal long Duplicates { get; private set; } internal long Conflicts { get; private set; } internal long Quarantined { get; private set; }
    internal CorpusWriter(string root, int maximum, int rowsPerShard, int stage, Budget budget, CorpusManifest? prior = null,
        long maximumLogicalShardBytes = MaximumLogicalShardBytes)
    {
        this.root = root; this.maximum = maximum; this.rowsPerShard = rowsPerShard; this.stage = stage; this.budget = budget;
        Require(maximumLogicalShardBytes is >= 256 and <= MaximumLogicalShardBytes, "Logical shard byte bound."); this.maximumLogicalShardBytes = maximumLogicalShardBytes;
        shards = prior?.Shards.ToList() ?? []; Records = prior?.RecordsWritten ?? 0; Duplicates = prior?.Duplicates ?? 0; Conflicts = prior?.Conflicts ?? 0; Quarantined = prior?.Quarantined ?? 0;
        index = new(Path.Combine(root, $"dedup-stage-{stage:0000}.bin"), maximum, budget);
        if (prior is not null)
        {
            Require(prior.Shards.Length <= 4096 && Records <= maximum, "Prior shard/record bound.");
            foreach (var shard in prior.Shards)
            {
                var path = SafePaths.Within(root, shard.Path, budget); Require(new FileInfo(path).Length == shard.Bytes && FileHash(path, budget) == shard.Sha256, "Closed resume shard identity differs.");
                long count = 0; foreach (var row in Lines(path, (int)shard.Records, budget)) { var record = JsonSerializer.Deserialize(row, CorpusJsonContext.Default.CorpusRecord)!; var pair = CanonicalRecord(record); index.Add(pair.Key, Sha(pair.Target)); count++; }
                Require(count == shard.Records, "Closed resume shard denominator differs.");
            }
            foreach (var row in Lines(Path.Combine(root, "exclusions.jsonl"), 1_000_000, budget))
            { var id = Str(row, "record_id"); Require(id.StartsWith("corpus-", StringComparison.Ordinal) && IsSha(id[7..]), "Exclusion identity invalid."); index.Add(id[7..], Sha("excluded"), true); }
        }
        exclusions = Writer(Path.Combine(root, "exclusions.jsonl"), prior is not null); quarantine = Writer(Path.Combine(root, "quarantine.jsonl"), prior is not null);
    }
    internal void Reject(string source, string path, long row, string reason)
    { budget.Check(); Quarantined++; quarantine.WriteLine("{\"source\":" + JsonSerializer.Serialize(source, CorpusJsonContext.Default.String) + ",\"path\":" + JsonSerializer.Serialize(path, CorpusJsonContext.Default.String) + ",\"row\":" + row + ",\"reason\":" + JsonSerializer.Serialize(reason, CorpusJsonContext.Default.String) + "}"); }
    internal void Add(CorpusQuestion question, CorpusProvenance source, string rowHash)
    {
        var normalized = question.State.Normalize(NormalizationForm.FormC).Replace("\r\n", "\n", StringComparison.Ordinal);
        Require(!string.IsNullOrWhiteSpace(normalized) && normalized.Length <= 262_144 && !string.IsNullOrWhiteSpace(question.Instruction) && question.Instruction.Length <= 65_536, "Input text bounds.");
        var key = ContentKey(normalized, question.Instruction, question.Type, question.Candidates); var family = Sha(source.Dataset + "|" + (question.SourceFamily ?? Sha(normalized)));
        var candidates = question.Candidates; var target = question.Target; string? transform = null;
        if (question.Type == "Choice") { var offset = (int)(Convert.ToUInt32(key[..8], 16) % candidates.Length); candidates = candidates.Skip(offset).Concat(candidates.Take(offset)).ToArray(); target = target.Skip(offset).Concat(target.Take(offset)).ToArray(); transform = "deterministic_cyclic_rotation_offset_" + offset; }
        AddRecord(new(1, "corpus-" + key, family, family, Convert.ToUInt32(family[..8], 16) % 10 == 0 ? "development" : "train", normalized, question.Instruction, question.Type, candidates, target,
            question.Language, question.LabelKind, question.LabelSemantics, rowHash, source.Row, source, "experimental_candidate_pending_length_and_source_review", "pending", transform));
    }
    internal void AddRecord(CorpusRecord record)
    {
        budget.Check(); var pair = CanonicalRecord(record);
        var serialized = JsonSerializer.Serialize(record, CorpusJsonContext.Default.CorpusRecord); var rowBytes = (long)Utf8.GetByteCount(serialized) + 1;
        Require(rowBytes <= MaximumRowBytes && rowBytes <= maximumLogicalShardBytes, "Derived corpus row UTF8 byte bound.");
        var state = index.Add(pair.Key, Sha(pair.Target));
        if (state == "duplicate") { Duplicates++; return; }
        if (state is "conflict" or "excluded")
        {
            if (state == "conflict") { Conflicts++; exclusions.WriteLine("{\"record_id\":\"" + record.RecordId + "\",\"reason\":\"conflicting_original_targets_all_excluded\"}"); }
            Reject(record.Source.Dataset, record.Source.Path, record.SourceRow, "conflicting_original_targets_all_excluded"); return;
        }
        if (Records >= maximum) throw new IOException("Output record bound exceeded; partial stage must resume with its complete denominator.");
        if (current is null || currentSource != record.Source.Dataset || currentSplit != record.Split || currentRows >= rowsPerShard || currentLogicalBytes + rowBytes > maximumLogicalShardBytes)
        {
            CloseShard(); Require(shards.Count < 4096, "Shard bound 4096.");
            var name = $"shards/stage-{stage:0000}-{shardNumber++:000000}.jsonl.gz"; var path = SafePaths.Within(root, name, budget); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            shardFile = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); compressed = new(shardFile, CompressionLevel.Fastest, true); current = new(compressed, Utf8, 4096, true);
            currentSource = record.Source.Dataset; currentSplit = record.Split; currentRows = 0; currentLogicalBytes = 0; shards.Add(new(name, currentSource, currentSplit, 0, 0, ""));
        }
        current.WriteLine(serialized); currentLogicalBytes += rowBytes; currentRows++; Records++; shards[^1] = shards[^1] with { Records = currentRows };
        if (Records % 10_000 == 0) Console.Error.WriteLine($"corpus records={Records}; duplicates={Duplicates}; conflicts={Conflicts}");
    }
    private void CloseShard() { current?.Dispose(); current = null; compressed?.Dispose(); compressed = null; shardFile?.Flush(true); shardFile?.Dispose(); shardFile = null; }
    internal void Close() { if (closed) return; closed = true; CloseShard(); exclusions.Dispose(); quarantine.Dispose(); index.Dispose(); }
    internal CorpusShard[] Shards(Budget finalBudget)
    { Require(closed, "Writer must close before hashing."); return shards.Select(s => { var path = SafePaths.Within(root, s.Path, finalBudget); return s with { Bytes = new FileInfo(path).Length, Sha256 = FileHash(path, finalBudget) }; }).ToArray(); }
    internal static string ContentKey(string state, string instruction, string kind, CorpusCandidate[] candidates)
    {
        using var memory = new MemoryStream(); using (var writer = new Utf8JsonWriter(memory)) { writer.WriteStartArray(); writer.WriteStringValue(state); writer.WriteStringValue(instruction); writer.WriteStringValue(kind); JsonSerializer.Serialize(writer, candidates, CorpusJsonContext.Default.CorpusCandidateArray); writer.WriteEndArray(); }
        using var doc = JsonDocument.Parse(memory.ToArray()); return Sha(Canonical(doc.RootElement));
    }
    internal static (string Key, string Target) CanonicalRecord(CorpusRecord record)
    {
        Require(record.Candidates.Length is >= 2 and <= 32 && record.Candidates.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == record.Candidates.Length && record.Target.Length == record.Candidates.Length, "Record candidate identity/dimension.");
        Probability(record.Target); Require(record.Type is "Choice" or "Boolean" or "Score" && (record.Type != "Score" || record.Candidates.Length <= 10) && record.Split is "train" or "development" && IsSha(record.Family) && IsSha(record.SourceLineSha256), "Record provenance/kind/split.");
        var candidates = record.Candidates; var target = record.Target;
        if (record.CandidateTransform is not null)
        {
            const string prefix = "deterministic_cyclic_rotation_offset_"; Require(record.Type == "Choice" && record.CandidateTransform.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(record.CandidateTransform[prefix.Length..], out _), "Unknown transform.");
            var offset = int.Parse(record.CandidateTransform[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture); Require(offset >= 0 && offset < candidates.Length, "Rotation offset.");
            if (offset > 0) { candidates = candidates.TakeLast(offset).Concat(candidates.SkipLast(offset)).ToArray(); target = target.TakeLast(offset).Concat(target.SkipLast(offset)).ToArray(); }
        }
        if (record.Type == "Boolean") Require(candidates.Select(c => c.Id).SequenceEqual(new[] { "false", "true" }), "Boolean false/true order.");
        if (record.Type == "Score") Require(candidates.Select(c => c.Id).SequenceEqual(Enumerable.Range(0, candidates.Length).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))), "Score order.");
        var key = ContentKey(record.State.Normalize(NormalizationForm.FormC).Replace("\r\n", "\n", StringComparison.Ordinal), record.Instruction, record.Type, candidates);
        Require(record.RecordId == "corpus-" + key, "Canonical unrotated record identity differs.");
        return (key, JsonSerializer.Serialize(target, CorpusJsonContext.Default.DoubleArray));
    }
    public void Dispose() => Close();
}
