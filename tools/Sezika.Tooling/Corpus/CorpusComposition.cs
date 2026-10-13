using System.Text.Json;
using static Sezika.Tooling.CorpusJson;

namespace Sezika.Tooling;

internal static partial class CorpusOperations
{
    internal static CorpusManifest Compose(CorpusComposeRequest request, Budget budget)
    {
        Require(request.Inputs.Length is >= 1 and <= 8 && request.MaximumRecords is >= 1 and <= 50_000_000 && request.RowsPerShard is >= 1 and <= 50_000, "Composition bounds.");
        var frozen = request.Inputs.Select(input => Frozen(input, budget)).ToArray();
        Require(frozen.Sum(item => item.Manifest.RecordsWritten) <= request.MaximumRecords, "Composition physical input denominator exceeds bound.");
        var output = NewOutput(request.Output, budget); File.WriteAllText(Path.Combine(output, "OWNED_BY_SEZIKA_CORPUS"), "csharp-corpus-v1\n", Utf8);
        var assets = new List<CorpusAsset>(); var rights = new List<CorpusRights>();
        for (var component = 0; component < frozen.Length; component++)
        {
            budget.Check(); var input = frozen[component]; var directory = Path.Combine(output, $"components/root-{component:00}"); Directory.CreateDirectory(directory);
            foreach (var name in new[] { "manifest.json", "source-assets.json", "source-rights.json", "exclusions.jsonl", "corpus-bindings.json" })
            { var source = SafePaths.Within(input.Root, name, budget); if (File.Exists(source)) { var target = Path.Combine(directory, name); File.Copy(source, target); Require(FileHash(source, budget) == FileHash(target, budget), "Archived component metadata differs."); } }
            var notices = Path.Combine(input.Root, "source-notices");
            if (Directory.Exists(notices)) ArchiveNotices(notices, Path.Combine(directory, "source-notices"), budget);
            assets.AddRange(Read(Path.Combine(input.Root, "source-assets.json"), CorpusJsonContext.Default.CorpusAssetArray, budget));
            rights.AddRange(Read(Path.Combine(input.Root, "source-rights.json"), CorpusJsonContext.Default.CorpusRightsArray, budget));
            Require(assets.Count <= 50_000 && rights.Count <= 768, "Composition metadata bounds.");
        }
        var assetRows = assets.Distinct().ToArray(); var rightsRows = rights.Distinct().ToArray();
        Require(rightsRows.GroupBy(r => r.Dataset, StringComparer.Ordinal).All(g => g.Select(r => (r.Decision, r.License)).Distinct().Count() == 1), "Conflicting source rights cannot be composed.");
        Write(Path.Combine(output, "source-assets.json"), assetRows, CorpusJsonContext.Default.CorpusAssetArray, budget);
        Write(Path.Combine(output, "source-rights.json"), rightsRows, CorpusJsonContext.Default.CorpusRightsArray, budget);
        using var writer = new CorpusWriter(output, request.MaximumRecords, request.RowsPerShard, 1, budget); var coverage = new List<CorpusCoverage>(); string? failure = null;
        try
        {
            foreach (var input in frozen)
            {
                foreach (var shard in input.Manifest.Shards)
                {
                    long count = 0; var before = writer.Records;
                    foreach (var element in Lines(SafePaths.Within(input.Root, shard.Path, budget), (int)shard.Records, budget))
                    { budget.Check(); var record = JsonSerializer.Deserialize(element, CorpusJsonContext.Default.CorpusRecord) ?? throw new InvalidDataException("Record missing."); count++; Require(record.Split == shard.Split && record.Source.Dataset == shard.Source, "Shard row source/split differs."); if (!input.Excluded.Contains(record.RecordId)) writer.AddRecord(record); }
                    Require(count == shard.Records, "Physical shard record denominator differs."); coverage.Add(new(shard.Source, shard.Path, count, writer.Records - before, count, true, false));
                }
            }
        }
        catch (Exception error) { failure = error.GetType().Name + ": " + error.Message; throw; }
        finally
        {
            writer.Close(); var finalBudget = new Budget(90, null, CancellationToken.None);
            var manifest = new CorpusManifest(1, failure is null ? "COMPLETE_PREPARATION_ONLY" : "PARTIAL_FAILED_PRESERVE_OUTPUTS", 1, writer.Records, writer.Duplicates, writer.Conflicts, writer.Quarantined,
                writer.Shards(finalBudget), coverage.ToArray(), FileHash(Path.Combine(output, "source-assets.json"), finalBudget), FileHash(Path.Combine(output, "source-rights.json"), finalBudget), Failure: failure, Pilot: frozen.Any(input => input.Manifest.Pilot));
            Write(Path.Combine(output, "manifest.json"), manifest, CorpusJsonContext.Default.CorpusManifest, finalBudget);
        }
        return Read(Path.Combine(output, "manifest.json"), CorpusJsonContext.Default.CorpusManifest, budget);
    }
    private static (string Root, CorpusManifest Manifest, HashSet<string> Excluded, string Hash) Frozen(CorpusInput input, Budget budget)
    {
        var root = Path.GetFullPath(input.Root); var path = SafePaths.Within(root, "manifest.json", budget);
        Require(IsSha(input.ManifestSha256) && FileHash(path, budget) == input.ManifestSha256, "Pinned corpus manifest identity differs.");
        var original = Read(path, budget);
        CorpusManifest manifest;
        if (original.TryGetProperty("assets_sha256", out _)) manifest = JsonSerializer.Deserialize(original, CorpusJsonContext.Default.CorpusManifest) ?? throw new InvalidDataException("Corpus manifest missing.");
        else
        {
            // Historical manifests are read-only. Their original binding owns the
            // rights/assets identities; importing never rewrites the old bytes or
            // pretends the old guardian or failed conversion was completed.
            var binding = Read(Path.Combine(root, "corpus-bindings.json"), CorpusJsonContext.Default.CorpusBinding, budget);
            Require(binding.ManifestSha256 == input.ManifestSha256 && binding.ExclusionsSha256 == FileHash(Path.Combine(root, "exclusions.jsonl"), budget) && !binding.FormalTrainingAdmitted, "Historical immutable bindings differ.");
            var shards = JsonSerializer.Deserialize(original.GetProperty("shards"), CorpusJsonContext.Default.CorpusShardArray) ?? throw new InvalidDataException("Historical shard array missing.");
            Require(shards.Length <= 4096, "Historical shard count."); var coverage = new List<CorpusCoverage>();
            if (original.TryGetProperty("source_coverage", out var sources))
            {
                Require(sources.GetArrayLength() <= 96, "Historical source coverage count.");
                foreach (var source in sources.EnumerateArray())
                {
                    budget.Check(); var name = Str(source, "source");
                    if (!source.TryGetProperty("files", out var files)) continue; Require(files.GetArrayLength() <= 50_000, "Historical carrier count.");
                    foreach (var carrier in files.EnumerateArray()) { var read = carrier.GetProperty("rows_read").GetInt64(); var written = carrier.GetProperty("records_written").GetInt64(); Require(read is >= 0 and <= 5_000_000 && written is >= 0 and <= 50_000_000, "Historical physical carrier denominator."); coverage.Add(new(name, Str(carrier, "path"), read, written, read, true, carrier.TryGetProperty("pilot_prefix", out var pilot) && pilot.GetBoolean())); }
                }
            }
            long Counter(string name) => original.TryGetProperty("counts", out var counts) && counts.TryGetProperty(name, out var value) ? value.GetInt64() : 0;
            manifest = new(original.GetProperty("schema_version").GetInt32(), Str(original, "status"), original.TryGetProperty("stage", out var stage) ? stage.GetInt32() : 1,
                original.GetProperty("records_written").GetInt64(), Counter("duplicates"), Counter("conflicts"), Counter("quarantined"), shards, coverage.ToArray(), binding.SourceAssetsSha256, binding.SourceRightsSha256,
                original.GetProperty("formal_training_admitted").GetBoolean(), ToolingVersion: "legacy-python-frozen-v1", Pilot: original.TryGetProperty("pilot", out var prefix) && prefix.GetBoolean());
        }
        Require(manifest.Status == "COMPLETE_PREPARATION_ONLY" && !manifest.FormalTrainingAdmitted && manifest.Shards.Length is >= 1 and <= 4096 && manifest.RecordsWritten is >= 1 and <= 50_000_000 && manifest.Shards.Sum(s => s.Records) == manifest.RecordsWritten, "Closed preparation corpus/denominator required.");
        Require(FileHash(Path.Combine(root, "source-assets.json"), budget) == manifest.AssetsSha256 && FileHash(Path.Combine(root, "source-rights.json"), budget) == manifest.RightsSha256, "Corpus metadata identity differs.");
        foreach (var shard in manifest.Shards) { var file = SafePaths.Within(root, shard.Path, budget); Require(shard.Records is >= 1 and <= 50_000 && shard.Split is "train" or "development" && new FileInfo(file).Length == shard.Bytes && FileHash(file, budget) == shard.Sha256, "Closed shard identity/denominator differs."); }
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in Lines(Path.Combine(root, "exclusions.jsonl"), 1_000_000, budget)) Require(excluded.Add(Str(element, "record_id")), "Duplicate exclusion.");
        return (root, manifest, excluded, input.ManifestSha256);
    }
    private static void ArchiveNotices(string source, string destination, Budget budget)
    {
        var pending = new Queue<(string Source, string Target, int Depth)>(); pending.Enqueue((source, destination, 0)); var files = 0;
        for (var directory = 0; directory < 257 && pending.Count > 0; directory++)
        {
            budget.Check(); var current = pending.Dequeue(); SafePaths.NoLinks(current.Source, budget); Directory.CreateDirectory(current.Target);
            var entries = Directory.EnumerateFileSystemEntries(current.Source).Take(257).ToArray(); Require(entries.Length <= 256, "Notice directory entry bound.");
            foreach (var path in entries)
            {
                budget.Check(); SafePaths.NoLinks(path, budget); var target = Path.Combine(current.Target, Path.GetFileName(path));
                if (Directory.Exists(path)) { Require(current.Depth < 6, "Notice directory depth bound."); pending.Enqueue((path, target, current.Depth + 1)); }
                else { Require(++files <= 256 && new FileInfo(path).Length <= 16 * 1024 * 1024, "Notice file/byte bound."); File.Copy(path, target); Require(FileHash(path, budget) == FileHash(target, budget), "Notice byte identity differs."); }
            }
        }
        Require(pending.Count == 0, "Notice traversal bound.");
    }
    internal static CorpusOperationReport BuildQueue(CorpusQueueRequest request, Budget budget)
    {
        Require(request.Corpora.Length is >= 0 and <= 8 && request.MaximumQuestions is >= 1 and <= 20_000 && request.SamplesPerSource is >= 1 and <= 1024 && request.Seeds is >= 0 and <= 40 && (request.Corpora.Length > 0 || request.Seeds > 0 || request.PilotOnly), "Queue bounds.");
        var inputs = request.Corpora.Select(root => Frozen(new(root, FileHash(Path.Combine(root, "manifest.json"), budget)), budget)).ToArray();
        var output = NewOutput(request.Output, budget); var samples = new List<(CorpusQueueRecord Question, CorpusReference Reference)>(); var sourceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (request.Seeds > 0 || request.PilotOnly) samples.AddRange(CorpusSyntheticTasks.Rows(request.Seeds, request.PilotOnly, request.MaximumQuestions, budget));
        foreach (var input in inputs)
        {
            foreach (var shard in input.Manifest.Shards)
            {
                if (shard.Split != "train") continue; long count = 0;
                foreach (var json in Lines(SafePaths.Within(input.Root, shard.Path, budget), (int)shard.Records, budget))
                {
                    budget.Check(); var row = JsonSerializer.Deserialize(json, CorpusJsonContext.Default.CorpusRecord)!; CorpusWriter.CanonicalRecord(row); count++;
                    Require(row.Split == shard.Split && row.Source.Dataset == shard.Source, "Queue shard source/split differs.");
                    if (input.Excluded.Contains(row.RecordId) || row.Source.OriginalSplit is not "train" and not "unsplit" || sourceCounts.GetValueOrDefault(shard.Source) >= request.SamplesPerSource) continue;
                    Require(samples.Count < request.MaximumQuestions, "Queue total question bound."); sourceCounts[shard.Source] = sourceCounts.GetValueOrDefault(shard.Source) + 1;
                    var visibleKey = CorpusWriter.ContentKey(row.State, row.Instruction, row.Type, row.Candidates); var id = "teacher-queue-" + visibleKey;
                    samples.Add((new(id, row.Family, row.Split, row.State, row.Instruction, row.Type, row.Candidates, row.Language, row.Source, input.Hash), new(id, true, row.Target)));
                }
                Require(count == shard.Records, "Queue physical shard denominator differs.");
            }
        }
        Require(samples.Count > 0, "Queue has no eligible source records.");
        var families = samples.Select(s => s.Question.Family).Distinct(StringComparer.Ordinal).ToDictionary(f => f, f => f, StringComparer.Ordinal); var states = new Dictionary<string, string>(StringComparer.Ordinal);
        string Find(string family) { for (var step = 0; step < request.MaximumQuestions; step++) { budget.Check(); if (families[family] == family) return family; family = families[family]; } throw new InvalidDataException("Family traversal bound."); }
        foreach (var sample in samples)
        { var stateHash = Sha(sample.Question.State.Normalize(System.Text.NormalizationForm.FormC).Replace("\r\n", "\n", StringComparison.Ordinal)); if (states.TryGetValue(stateHash, out var prior)) { var left = Find(prior); var right = Find(sample.Question.Family); families[StringComparer.Ordinal.Compare(left, right) > 0 ? left : right] = StringComparer.Ordinal.Compare(left, right) > 0 ? right : left; } else states[stateHash] = sample.Question.Family; }
        var conflicts = samples.GroupBy(s => s.Question.RecordId, StringComparer.Ordinal).Where(g => g.Select(s => JsonSerializer.Serialize(s.Reference, CorpusJsonContext.Default.CorpusReference)).Distinct(StringComparer.Ordinal).Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        using var queue = Writer(Path.Combine(output, "questions.jsonl")); using var gold = Writer(Path.Combine(output, "reference-gold.jsonl")); using var conflict = Writer(Path.Combine(output, "source-reference-conflicts.jsonl"));
        var seen = new HashSet<string>(StringComparer.Ordinal); var completed = 0; var duplicates = 0;
        foreach (var sample in samples)
        {
            budget.Check(); if (conflicts.Contains(sample.Question.RecordId)) { Line(conflict, sample.Reference, CorpusJsonContext.Default.CorpusReference); continue; }
            if (!seen.Add(sample.Question.RecordId)) { duplicates++; continue; }
            Line(queue, sample.Question with { Family = Find(sample.Question.Family), OriginalFamily = sample.Question.Family, OriginalDerivedSplit = sample.Question.Split }, CorpusJsonContext.Default.CorpusQueueRecord); Line(gold, sample.Reference, CorpusJsonContext.Default.CorpusReference); completed++;
        }
        queue.Flush(); gold.Flush(); conflict.Flush(); queue.Dispose(); gold.Dispose(); conflict.Dispose();
        var report = new CorpusOperationReport("QUESTION_QUEUE_READY_TEACHER_NOT_CALLED", output, samples.Count, completed, conflicts.Count, 0, duplicates, false, false);
        var queueFile = Path.Combine(output, "questions.jsonl"); var referenceFile = Path.Combine(output, "reference-gold.jsonl");
        Write(Path.Combine(output, "manifest.json"), new CorpusQueueManifest(report, FileHash(queueFile, budget), FileHash(referenceFile, budget), new FileInfo(queueFile).Length, new FileInfo(referenceFile).Length), CorpusJsonContext.Default.CorpusQueueManifest, budget); return report;
    }
}
