using System.Diagnostics;
using System.Text.Json;
using Sezika.Tooling;

internal static class CorpusResearchIdentityChecks
{
    internal const int CheckCount = 8;
    private const string Revision = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string BinaryRow = "{\"instance_id\":\"binary-one\",\"outcomes\":[\"B\",\"A\"],\"gold_distribution\":{\"B\":0.4,\"A\":0.6}}\n";
    private const string ParallelRow = "{\"instance_id\":\"parallel-one\",\"gold_distribution\":{\"True\":0.4,\"False\":0.6},\"generator_metadata\":{\"parallel_primitives\":{\"source_instance_id\":\"binary-one\"}},\"queries\":{\"choice\":{},\"noul\":{},\"score\":{}}}\n";

    internal static void Run(string root, Action<string, Action> check)
    {
        var fixtures = Fresh(Path.Combine(root, "research-identity-fixtures"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var clock = Stopwatch.StartNew();
        Budget Limit() => new(5, null, cancellation.Token);
        void Gate()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 15) throw new TimeoutException("Research identity fixture group deadline15s.");
        }
        void Case(string name, Action action) => check(name, () => { Gate(); action(); Gate(); });

        Case("research identity golden retains revision metadata SHA license and selected URL", () =>
        {
            var source = Fresh(Path.Combine(fixtures, "hf-golden"));
            var seeds = Write(source, "seeds.json", "[{\"id\":\"owner/data\"}]");
            var metadata = Hf(source, "owner/data", "owner/data"); var output = Path.Combine(source, "plan");
            var report = CorpusOperations.BuildResearchPlan(new(seeds, source, output, 1, 8), Limit());
            var catalog = CorpusJson.Read(Path.Combine(output, "source-catalog.json"), CorpusJsonContext.Default.CorpusResearchSourceArray, Limit());
            var plan = CorpusJson.Read(Path.Combine(output, "raw-download-plan.json"), CorpusJsonContext.Default.CorpusResearchAssetArray, Limit());
            var training = plan.Single(asset => asset.Path.EndsWith("data/train.jsonl", StringComparison.Ordinal));
            Assert(catalog.Length == 1 && catalog[0].Id == "owner/data" && catalog[0].RequestedId == "owner/data" && catalog[0].Revision == Revision &&
                catalog[0].MetadataSha256 == CorpusJson.FileHash(metadata, Limit()) && catalog[0].License.ValueKind == JsonValueKind.String &&
                catalog[0].License.GetString() == "cc-by-4.0" && catalog[0].SourceUrl == "https://huggingface.co/datasets/owner/data",
                "Frozen research provenance no longer matches source metadata.");
            Assert(plan.Length == 2 && training.Dataset == "owner/data" && training.Revision == Revision && training.Split == "source-train" &&
                training.ExpectedBytes == 6 && training.ExpectedSha256 == new string('c', 64) &&
                training.Url == "https://huggingface.co/datasets/owner/data/resolve/" + Revision + "/data/train.jsonl" &&
                plan.All(asset => !asset.Path.Contains("predictions/", StringComparison.Ordinal)), "Selected source identity, URL or original split changed.");
            Assert(report.Status == "RESEARCH_PLAN_FROZEN_ONLY" && !report.FormalTrainingAdmitted && !report.ActualTeacherCalled &&
                catalog[0].TrainingAdmission == "pending_original_split_and_license_review", "Research planning granted admission or teacher claims.");
        });
        Case("research identity publisher alias keeps requested ID canonical ID and missing identity", () =>
        {
            var source = Fresh(Path.Combine(fixtures, "alias"));
            var seeds = Write(source, "seeds.json", "[{\"id\":\"requested/alias\"},{\"id\":\"canonical/data\"},{\"id\":\"owner/missing\"}]");
            var metadata = Hf(source, "requested/alias", "canonical/data"); _ = Hf(source, "canonical/data", "canonical/data");
            var output = Path.Combine(source, "plan"); var report = CorpusOperations.BuildResearchPlan(new(seeds, source, output, 3, 8), Limit());
            var catalog = CorpusJson.Read(Path.Combine(output, "source-catalog.json"), CorpusJsonContext.Default.CorpusResearchSourceArray, Limit());
            var plan = CorpusJson.Read(Path.Combine(output, "raw-download-plan.json"), CorpusJsonContext.Default.CorpusResearchAssetArray, Limit());
            var actual = catalog.Single(item => item.Verification == "metadata_frozen"); var missing = catalog.Single(item => item.Verification != "metadata_frozen");
            Assert(actual.Id == "canonical/data" && actual.RequestedId == "requested/alias" && actual.Id != actual.RequestedId &&
                actual.MetadataSha256 == CorpusJson.FileHash(metadata, Limit()) && actual.SourceUrl == "https://huggingface.co/datasets/canonical/data" &&
                plan.Length == 2 && plan.All(asset => asset.Dataset == "canonical/data" && asset.Url.StartsWith("https://huggingface.co/datasets/canonical/data/resolve/" + Revision + "/", StringComparison.Ordinal)),
                "Historical publisher alias was silently relabelled, duplicated or rejected.");
            Assert(report.Requested == 3 && report.Completed == 2 && report.Failed == 1 && missing.Id == "owner/missing" && missing.RequestedId == "owner/missing" &&
                missing.Revision is null && missing.License.ValueKind == JsonValueKind.Null && missing.MetadataSha256 == "",
                "Missing source or alias deduplication changed the original denominator.");
            var invalid = new[] { "../escape", "canonical//data", "canonical/da\nta" };
            for (var index = 0; index < invalid.Length && index < 3; index++)
            {
                Gate(); var bad = Fresh(Path.Combine(fixtures, "canonical-invalid-" + index));
                var badSeeds = Write(bad, "seeds.json", "[{\"id\":\"requested/alias\"}]"); _ = Hf(bad, "requested/alias", invalid[index]);
                var badOutput = Path.Combine(bad, "plan");
                Reject(() => CorpusOperations.BuildResearchPlan(new(badSeeds, bad, badOutput, 1, 8), Limit()));
                Assert(!Directory.Exists(badOutput), "Unsafe canonical metadata ID created a plan.");
            }
        });
        Case("research identity GitHub URLs require full lowercase fixed revision", () =>
        {
            var source = Fresh(Path.Combine(fixtures, "github-golden")); var seeds = Write(source, "seeds.json", "[]");
            GitHub(source, Revision); var output = Path.Combine(source, "plan");
            _ = CorpusOperations.BuildResearchPlan(new(seeds, source, output, 1, 8), Limit());
            var plan = CorpusJson.Read(Path.Combine(output, "raw-download-plan.json"), CorpusJsonContext.Default.CorpusResearchAssetArray, Limit());
            Assert(plan.Length == 3 && plan.All(asset => asset.Dataset == "little-g-ai/Sys1Cal-v1" && asset.Revision == Revision &&
                asset.Url.StartsWith("https://raw.githubusercontent.com/little-g-ai/Sys1Cal-v1/" + Revision + "/", StringComparison.Ordinal) &&
                asset.ExpectedSha256 is null && asset.Split == "evaluation-calibration-only"), "Fixed GitHub revision or unknown digest semantics changed.");
            var invalid = new[] { "main", "abcdef0", new string('B', 40), new string('z', 40) };
            for (var index = 0; index < invalid.Length && index < 4; index++)
            {
                Gate(); var bad = Fresh(Path.Combine(fixtures, "github-invalid-" + index)); var badSeeds = Write(bad, "seeds.json", "[]");
                GitHub(bad, invalid[index]); var badOutput = Path.Combine(bad, "plan");
                Reject(() => CorpusOperations.BuildResearchPlan(new(badSeeds, bad, badOutput, 1, 8), Limit()));
                Assert(!Directory.Exists(badOutput), "Unpinned GitHub revision created a frozen plan.");
            }
        });
        Case("research identity independent seed duplicate IDs reject without a report", () =>
        {
            var seed = Seeds(Fresh(Path.Combine(fixtures, "duplicate")));
            File.WriteAllText(seed.Binary, BinaryRow + BinaryRow, CorpusJson.Utf8);
            var binary = Request(seed, "binary-duplicate.json", Limit()); Reject(() => CorpusOperations.CheckSeeds(binary, Limit()));
            Assert(!File.Exists(binary.Output), "Duplicate binary seed produced a successful report.");
            File.WriteAllText(seed.Binary, BinaryRow, CorpusJson.Utf8); File.WriteAllText(seed.Parallel, ParallelRow + ParallelRow, CorpusJson.Utf8);
            var parallel = Request(seed, "parallel-duplicate.json", Limit()); Reject(() => CorpusOperations.CheckSeeds(parallel, Limit()));
            Assert(!File.Exists(parallel.Output), "Duplicate parallel seed produced a successful report.");
        });
        Case("research identity independent seed golden and missing evaluation retain evidence scope", () =>
        {
            var seed = Seeds(Fresh(Path.Combine(fixtures, "evaluation"))); var request = Request(seed, "golden.json", Limit());
            var golden = CorpusOperations.CheckSeeds(request, Limit());
            Assert(golden.Status == "PASS_SEED_CONTRACT_ONLY" && golden.BinaryRows == 1 && golden.ParallelRows == 1 && golden.MatchedPairs == 1 &&
                golden.GoldNormalizationErrors == 0 && golden.CrossPrimitiveConsistencyErrors == 0 && golden.ParallelQueryErrors == 0 &&
                golden.DeclaredEvaluationFiles == 1 && !golden.GoldConstructionRecomputed && !golden.ModelCalibrationValidated && !golden.FormalTrainingAdmitted,
                "Reference outcome mapping or acceptance boundary changed.");
            var missingRoot = Fresh(Path.Combine(seed.Root, "missing-evaluation-root"));
            var missing = CorpusOperations.CheckSeeds(request with { EvaluationRoot = missingRoot, Output = Path.Combine(seed.Root, "missing-evaluation.json") }, Limit());
            Assert(missing.Status == "FAIL_SEED_CONTRACT" && missing.DeclaredEvaluationFiles == 1 && missing.MissingEvaluationFiles.SequenceEqual(new[] { "eval.jsonl" }) &&
                !missing.GoldConstructionRecomputed && !missing.ModelCalibrationValidated, "Missing evaluation file was hidden or reference gold was invented.");
        });
        Case("research identity independent seed nonnumeric reference rejects", () =>
        {
            var seed = Seeds(Fresh(Path.Combine(fixtures, "nonnumeric")));
            File.WriteAllText(seed.Binary, BinaryRow.Replace("\"B\":0.4", "\"B\":\"not-a-number\"", StringComparison.Ordinal), CorpusJson.Utf8);
            var request = Request(seed, "nonnumeric.json", Limit()); Reject(() => CorpusOperations.CheckSeeds(request, Limit()), format: true);
            Assert(!File.Exists(request.Output), "Nonnumeric reference distribution became numeric output.");
        });
        Case("research identity independent seed each external pin mismatch rejects before output", () =>
        {
            var seed = Seeds(Fresh(Path.Combine(fixtures, "pin-mismatch"))); var request = Request(seed, "binary-pin.json", Limit());
            Reject(() => CorpusOperations.CheckSeeds(request with { BinarySha256 = new string('0', 64) }, Limit()));
            var parallel = request with { Output = Path.Combine(seed.Root, "parallel-pin.json"), ParallelSha256 = new string('0', 64) };
            Reject(() => CorpusOperations.CheckSeeds(parallel, Limit()));
            var evaluation = request with { Output = Path.Combine(seed.Root, "evaluation-pin.json"), EvaluationManifestSha256 = new string('0', 64) };
            Reject(() => CorpusOperations.CheckSeeds(evaluation, Limit()));
            Assert(!File.Exists(request.Output) && !File.Exists(parallel.Output) && !File.Exists(evaluation.Output), "A changed input pin produced a report.");
        });
        Case("research identity independent reference disagreement and query failures remain counters", () =>
        {
            var seed = Seeds(Fresh(Path.Combine(fixtures, "disagreement")));
            var parallel = ParallelRow.Replace("\"True\":0.4,\"False\":0.6", "\"True\":0.6,\"False\":0.4", StringComparison.Ordinal)
                .Replace("\"choice\":{},\"noul\":{},\"score\":{}", "\"choice\":{}", StringComparison.Ordinal);
            File.WriteAllText(seed.Parallel, parallel, CorpusJson.Utf8);
            var report = CorpusOperations.CheckSeeds(Request(seed, "disagreement.json", Limit()), Limit());
            Assert(report.Status == "FAIL_SEED_CONTRACT" && report.MatchedPairs == 1 && report.GoldNormalizationErrors == 0 &&
                report.CrossPrimitiveConsistencyErrors == 1 && report.ParallelQueryErrors == 1 && !report.GoldConstructionRecomputed && !report.ModelCalibrationValidated,
                "Reference disagreement was rewritten or failed query coverage disappeared.");
        });
    }

    private sealed record SeedFixture(string Root, string Binary, string Parallel, string Manifest, string EvaluationFile);
    private static SeedFixture Seeds(string root) => new(root, Write(root, "binary.jsonl", BinaryRow), Write(root, "parallel.jsonl", ParallelRow),
        Write(root, "manifest.json", "{\"evaluation\":[{\"data_files\":[\"eval.jsonl\"]}]}"), Write(root, "eval.jsonl", "{}\n"));
    private static CorpusSeedCheckRequest Request(SeedFixture fixture, string output, Budget budget) => new(fixture.Binary, CorpusJson.FileHash(fixture.Binary, budget),
        fixture.Parallel, CorpusJson.FileHash(fixture.Parallel, budget), fixture.Manifest, CorpusJson.FileHash(fixture.Manifest, budget), fixture.Root, Path.Combine(fixture.Root, output), 4);
    private static string Hf(string root, string requested, string actual) => Write(root, "metadata/hf/" + requested.Replace("/", "--", StringComparison.Ordinal) + ".json",
        "{\"id\":" + Quote(actual) + ",\"sha\":" + Quote(Revision) + ",\"cardData\":{\"license\":\"cc-by-4.0\"},\"siblings\":[{\"rfilename\":\"README.md\",\"size\":3},{\"rfilename\":\"data/train.jsonl\",\"size\":6,\"lfs\":{\"sha256\":\"" + new string('c', 64) + "\"}},{\"rfilename\":\"predictions/results.json\",\"size\":9}]}");
    private static void GitHub(string root, string revision) => Write(root, "metadata/github/sys1cal-tree.json", "{\"sha\":" + Quote(revision) + ",\"tree\":[{\"path\":\"README.md\",\"size\":3},{\"path\":\"LICENSE\",\"size\":4},{\"path\":\"datasets/generated/v0.1.0_tiny_cleanvars_binary.jsonl\",\"size\":6},{\"path\":\"not-selected.bin\",\"size\":8}]}");
    private static string Quote(string value) => JsonSerializer.Serialize(value, CorpusJsonContext.Default.String);
    private static string Fresh(string path)
    {
        path = Path.GetFullPath(path);
        if (Path.Exists(path)) throw new IOException("Preserve existing research identity fixture.");
        Directory.CreateDirectory(path); return path;
    }
    private static string Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.Write(CorpusJson.Utf8.GetBytes(text)); return path;
    }
    private static void Reject(Action action, bool format = false)
    {
        try { action(); }
        catch (InvalidDataException) when (!format) { return; }
        catch (FormatException) when (format) { return; }
        throw new InvalidOperationException("Expected research identity rejection was absent.");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
