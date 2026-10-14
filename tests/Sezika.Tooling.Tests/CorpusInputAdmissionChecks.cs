using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class CorpusInputAdmissionChecks
{
    internal const int CheckCount = 14;

    internal static void Run(string root, Budget budget, Action<string, Action> check)
    {
        // Initialization is inside every callback. maximumChecks=1 therefore
        // executes exactly the first fixture, without preparing later fixtures.
        check("input admission verifies the complete literal preparation graph", () =>
        {
            var fixture = Fixture.Create(root, "complete", budget);
            var report = CorpusInputAdmission.Validate(fixture.Request, budget);
            Require(report.Status == "PASS_full_corpus_inputs_only" && report.InventoryComplete && report.Failure is null &&
                report.PlannedFiles == 21 && report.VerifiedFiles == 21 && report.PlannedBytes == fixture.ExpectedBytes && report.VerifiedBytes == fixture.ExpectedBytes &&
                report.PlannedShards == 2 && report.VerifiedShards == 2 && report.PlannedSources == 1 && report.VerifiedSources == 1 &&
                !report.FormalTrainingAdmitted && !report.GpuExecutionAccepted && !report.LicenseAccepted && !report.QualityAccepted && !report.PublicationAccepted,
                "Literal full inventory/acceptance semantics differ.");
            Require(report.BaseEncoderSha256 == fixture.ExpectedEncoderSha && report.TokenizerSha256 == fixture.ExpectedTokenizerSha &&
                report.CorpusManifestSha256 == fixture.ExpectedManifestSha && report.CorpusBindingsSha256 == fixture.ExpectedBindingsSha &&
                report.ExclusionsSha256 == fixture.ExpectedExclusionsSha && report.LengthReportSha256 == fixture.ExpectedLengthsSha &&
                report.RecordsSha256 == fixture.ExpectedRecordsSha, "Independent literal input/profile identity differs.");
            var encoded = JsonSerializer.Serialize(report, CorpusInputAdmissionJsonContext.Default.CorpusInputAdmissionReport);
            using var json = JsonDocument.Parse(encoded);
            Require(json.RootElement.GetProperty("VerifiedFiles").GetInt32() == 21 && !json.RootElement.GetProperty("FormalTrainingAdmitted").GetBoolean(),
                "Source-generated report serialization differs.");
        });
        check("input admission accepts legacy omitted false and zero defaults", () =>
        {
            var fixture = Fixture.Create(root, "omitted-defaults", budget, Variant.OmitDefaults);
            var report = CorpusInputAdmission.Validate(fixture.Request, budget);
            Require(report.Status == "PASS_full_corpus_inputs_only" && report.VerifiedFiles == 21, "Legal original omitted defaults rejected.");
        });
        check("input admission requires actual requested policy bytes", () =>
        {
            var fixture = Fixture.Create(root, "missing-policy", budget);
            var report = CorpusInputAdmission.Validate(fixture.Request with { SourcePolicyPath = Path.Combine(fixture.Directory, "absent-policy.json") }, budget);
            Failed(report, "policy");
        });
        check("input admission rejects policy pin and policy schema changes", () =>
        {
            var fixture = Fixture.Create(root, "wrong-policy-pin", budget);
            Failed(CorpusInputAdmission.Validate(fixture.Request with { SourcePolicySha256 = new string('0', 64) }, budget), "policy");
            var schema = Fixture.Create(root, "wrong-policy-schema", budget, Variant.WrongPolicySchema);
            Failed(CorpusInputAdmission.Validate(schema.Request, budget), "SchemaVersion");
        });
        check("input admission rejects policy-less complete-scan bypass", () =>
        {
            var fixture = Fixture.Create(root, "no-complete-scan", budget, Variant.NoCompleteScan);
            Failed(CorpusInputAdmission.Validate(fixture.Request, budget), "Complete scan");
        });
        check("input admission binds scan inputs to this tokenizer and parent", () =>
        {
            var fixture = Fixture.Create(root, "wrong-scan-input", budget, Variant.WrongScanTokenizer);
            Failed(CorpusInputAdmission.Validate(fixture.Request, budget), "scan input");
            var parent = Fixture.Create(root, "wrong-parent", budget, Variant.WrongParentShard);
            Failed(CorpusInputAdmission.Validate(parent.Request, budget), "original shard");
        });
        check("input admission retains complete denominator for a missing shard", () =>
        {
            var fixture = Fixture.Create(root, "missing-shard", budget);
            File.Delete(fixture.TrainShard); // Exact file created by this callback.
            var report = CorpusInputAdmission.Validate(fixture.Request, budget);
            Require(report.Status == "FAIL" && report.InventoryComplete && report.PlannedFiles == 21 && report.PlannedShards == 2 &&
                report.PlannedBytes == fixture.ExpectedBytes && report.VerifiedFiles < report.PlannedFiles && report.VerifiedShards < 2,
                "Leaf failure lost its complete known inventory.");
        });
        check("input admission rejects changed base bytes and model commit", () =>
        {
            var fixture = Fixture.Create(root, "wrong-base-sha", budget);
            File.WriteAllText(fixture.Encoder, "changed base bytes", new UTF8Encoding(false));
            Failed(CorpusInputAdmission.Validate(fixture.Request, budget), "SHA");
            var commit = Fixture.Create(root, "wrong-model-commit", budget, Variant.WrongModelCommit);
            Failed(CorpusInputAdmission.Validate(commit.Request, budget), "model commit");
        });
        check("input admission is read only with an occupied cache directory", () =>
        {
            var fixture = Fixture.Create(root, "cache-conflict", budget);
            var cache = Path.Combine(fixture.Directory, "corpus", ".input-admission-cache");
            Directory.CreateDirectory(cache);
            var sentinel = Path.Combine(cache, "result.json"); File.WriteAllText(sentinel, "preserve existing cache", new UTF8Encoding(false));
            var before = fixture.Inputs.Select(path => (Path: path, Sha: Hash(File.ReadAllBytes(path)))).ToArray();
            Require(CorpusInputAdmission.Validate(fixture.Request, budget).Status == "PASS_full_corpus_inputs_only", "Read-only occupied-cache fixture rejected.");
            foreach (var file in before) { budget.Check(); Require(Hash(File.ReadAllBytes(file.Path)) == file.Sha, "Admission mutated an input."); }
            Require(File.ReadAllText(sentinel) == "preserve existing cache" && Directory.EnumerateFileSystemEntries(cache).Take(2).Count() == 1,
                "Admission wrote or replaced cache objects.");
        });
        check("input admission propagates parent token and cancellation file", () =>
        {
            var fixture = Fixture.Create(root, "cancellation", budget);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var parent = new Budget(20, null, cancelled.Token, budget);
            Throws<OperationCanceledException>(() => CorpusInputAdmission.Validate(fixture.Request, new Budget(20, null, CancellationToken.None, parent)));
            var marker = Path.Combine(fixture.Directory, "CANCEL"); File.WriteAllText(marker, "owned cancellation fixture");
            Throws<OperationCanceledException>(() => CorpusInputAdmission.Validate(fixture.Request, new Budget(20, marker, CancellationToken.None, budget)));
        });
        check("input admission preserves deadline and explicit resource bounds", () =>
        {
            var fixture = Fixture.Create(root, "bounds", budget);
            var deadline = new Budget(20, null, CancellationToken.None, budget, () => throw new TimeoutException("owned parent deadline fixture"));
            Throws<TimeoutException>(() => CorpusInputAdmission.Validate(fixture.Request, new Budget(20, null, CancellationToken.None, deadline)));
            Failed(CorpusInputAdmission.Validate(fixture.Request with { MaximumFiles = 20 }, budget), "bound");
            Failed(CorpusInputAdmission.Validate(fixture.Request with { MaximumBytes = 1 }, budget), "bound");
        });
        check("input admission rejects duplicate unsafe absolute and falsely typed metadata", () =>
        {
            var duplicate = Fixture.Create(root, "duplicate-shard", budget, Variant.DuplicateShard);
            Failed(CorpusInputAdmission.Validate(duplicate.Request, budget), "Duplicate");
            var unsafePath = Fixture.Create(root, "unsafe-shard", budget, Variant.UnsafeShardPath);
            Failed(CorpusInputAdmission.Validate(unsafePath.Request, budget), "portable ASCII");
            var wrongType = Fixture.Create(root, "wrong-boolean", budget, Variant.StringBoolean);
            Failed(CorpusInputAdmission.Validate(wrongType.Request, budget), "Boolean");
            var wrongCount = Fixture.Create(root, "wrong-zero-type", budget, Variant.StringZero);
            Failed(CorpusInputAdmission.Validate(wrongCount.Request, budget), "Integer");
            var paths = Fixture.Create(root, "absolute-paths", budget);
            Failed(CorpusInputAdmission.Validate(paths.Request with { PlanPath = "relative-plan.json" }, budget), "absolute");
            Failed(CorpusInputAdmission.Validate(paths.Request with { SourcePolicyPath = @"\\host\share\policy.json" }, budget), "absolute");
            Failed(CorpusInputAdmission.Validate(paths.Request with { ModelReadbackReceiptPath = paths.Request.ModelReadbackReceiptPath + "\t" }, budget), "absolute");
        });
        check("input admission CLI writes typed success and protects original inputs", () =>
        {
            budget.Check();
            var caseRoot = SafePaths.Within(root, "corpus-input-admission-cli-caller", budget);
            Require(!Path.Exists(caseRoot), "Fresh CLI admission caller fixture required.");
            Directory.CreateDirectory(caseRoot);
            var fixture = Fixture.Create(caseRoot, "cli-inputs", budget);
            var original = new List<(string Path, string Sha)>();
            foreach (var path in fixture.Inputs) { budget.Check(); original.Add((path, Hash(File.ReadAllBytes(path)))); }
            Require(original.Count == 21, "CLI original literal input denominator differs.");
            // Plan/policy/readback parents are protected. Keep this request and
            // all successful command output outside the fixture input tree.
            var requestPath = Path.Combine(caseRoot, "request.json");
            CorpusJson.Write(requestPath, fixture.Request, CorpusInputAdmissionJsonContext.Default.CorpusInputAdmissionRequest, budget);
            var resultRoot = Path.Combine(caseRoot, "command-result"); Directory.CreateDirectory(resultRoot);
            var output = Path.Combine(resultRoot, "admission.json");
            var invocations = 0;
            int Invoke(string target)
            {
                budget.Check();
                Require(++invocations <= 3, "Admission CLI caller invocation bound3 exceeded.");
                var exit = ToolingCommand.Run(["task", "validate-inputs", "--request", requestPath, "--output", target, "--timeout-seconds", "10"]);
                budget.Check(); return exit;
            }
            Require(Invoke(output) == 0, "Actual typed task validate-inputs CLI did not return0.");
            using (var document = TaskOperations.ReadDocument(output, budget))
            {
                var report = document.RootElement;
                Require(report.GetProperty("Status").GetString() == "PASS_full_corpus_inputs_only" && report.GetProperty("InventoryComplete").GetBoolean() &&
                    report.GetProperty("PlannedFiles").GetInt32() == 21 && report.GetProperty("VerifiedFiles").GetInt32() == 21 &&
                    report.GetProperty("PlannedBytes").GetInt64() == fixture.ExpectedBytes && report.GetProperty("VerifiedBytes").GetInt64() == fixture.ExpectedBytes &&
                    report.GetProperty("RecordsSha256").GetString() == fixture.ExpectedRecordsSha,
                    "Actual typed admission CLI report or independent identity differs.");
                foreach (var flag in new[] { "FormalTrainingAdmitted", "GpuExecutionAccepted", "LicenseAccepted", "QualityAccepted", "PublicationAccepted" })
                {
                    budget.Check(); Require(!report.GetProperty(flag).GetBoolean(), "CLI input admission acquired downstream permission: " + flag);
                }
            }
            var forbiddenCorpus = Path.Combine(fixture.Directory, "corpus", "must-not-write-command-failure.json");
            var forbiddenPlanParent = Path.Combine(fixture.Directory, "must-not-write-command-failure.json");
            Require(Invoke(forbiddenCorpus) != 0 && !Path.Exists(forbiddenCorpus), "Protected corpus output created a generic failure receipt.");
            Require(Invoke(forbiddenPlanParent) != 0 && !Path.Exists(forbiddenPlanParent), "Protected plan parent created a generic failure receipt.");
            foreach (var file in original) { budget.Check(); Require(Hash(File.ReadAllBytes(file.Path)) == file.Sha, "CLI changed an original literal input."); }
        });
        check("resident admission caller binds independently pinned corpus and base identities", () =>
        {
            budget.Check();
            var caseRoot = SafePaths.Within(root, "corpus-input-admission-resident-caller", budget);
            Require(!Path.Exists(caseRoot), "Fresh resident admission helper fixture required.");
            Directory.CreateDirectory(caseRoot);
            var fixture = Fixture.Create(caseRoot, "resident-inputs", budget);
            // Only the exact RunCore admission helper is exercised. These inert
            // paths are never opened; no real profile, RunCore or GPU is claimed.
            var inertTask = Path.Combine(caseRoot, "inert-task-not-created");
            var trainer = Path.Combine(caseRoot, "inert-trainer-not-opened");
            var profile = Path.Combine(caseRoot, "inert-profile-not-opened.json");
            var nvidiaSmi = Path.Combine(caseRoot, "inert-nvidia-smi-not-opened");
            var resident = new ResidentRequest(inertTask, trainer, new string('1', 64), fixture.Request.PlanPath, fixture.Request.PlanSha256,
                profile, new string('2', 64), new string('0', 32), nvidiaSmi, new string('3', 64), 1, 60, 1)
            { InputAdmission = fixture.Request };
            // Golden inputs come from the literal fixture builder, not from the
            // helper report. Order/Backward are synthetic unused helper values.
            var independent = new CheckpointInputContract(fixture.Request.PlanSha256, fixture.ExpectedManifestSha, fixture.ExpectedRecordsSha,
                fixture.ExpectedEncoderSha, fixture.ExpectedTokenizerSha, new string('b', 64), new string('c', 64));
            var accepted = ResidentController.ValidateInputAdmission(resident, independent, budget);
            Require(accepted.Status == "PASS_full_corpus_inputs_only" && accepted.VerifiedFiles == 21 &&
                accepted.RecordsSha256 == fixture.ExpectedRecordsSha && accepted.BaseEncoderSha256 == fixture.ExpectedEncoderSha,
                "Real resident admission helper rejected independently fixed inputs.");
            RejectData(() => ResidentController.ValidateInputAdmission(resident with { InputAdmission = null }, independent, budget), "explicit full corpus input admission");
            RejectData(() => ResidentController.ValidateInputAdmission(resident with
            {
                InputAdmission = fixture.Request with { PlanPath = Path.Combine(caseRoot, "different-plan.json") }
            }, independent, budget), "same resident plan and SHA");
            RejectData(() => ResidentController.ValidateInputAdmission(resident with
            {
                InputAdmission = fixture.Request with { PlanSha256 = new string('0', 64) }
            }, independent, budget), "same resident plan and SHA");
            RejectData(() => ResidentController.ValidateInputAdmission(resident, independent with { RecordsSha256 = new string('0', 64) }, budget), "independently pinned profile");
            RejectData(() => ResidentController.ValidateInputAdmission(resident, independent with { BaseEncoderSha256 = new string('0', 64) }, budget), "independently pinned profile");
            Require(!Path.Exists(inertTask) && !Path.Exists(trainer) && !Path.Exists(profile) && !Path.Exists(nvidiaSmi),
                "Input helper opened a controller/native preflight execution path.");
        });
    }

    private enum Variant { Complete, OmitDefaults, WrongPolicySchema, NoCompleteScan, WrongScanTokenizer, WrongParentShard, WrongModelCommit, DuplicateShard, UnsafeShardPath, StringBoolean, StringZero }

    private sealed record Fixture(string Directory, CorpusInputAdmissionRequest Request, string TrainShard, string Encoder, string[] Inputs, long ExpectedBytes,
        string ExpectedEncoderSha, string ExpectedTokenizerSha, string ExpectedManifestSha, string ExpectedBindingsSha, string ExpectedExclusionsSha, string ExpectedLengthsSha, string ExpectedRecordsSha)
    {
        internal static Fixture Create(string root, string name, Budget budget, Variant variant = Variant.Complete)
        {
            budget.Check();
            var directory = Path.Combine(root, "corpus-input-admission-" + name);
            SafePaths.NoLinks(directory, budget);
            Require(!Path.Exists(directory), "Fresh isolated admission fixture required.");
            System.IO.Directory.CreateDirectory(directory);
            var corpus = Path.Combine(directory, "corpus"); var modelRoot = Path.Combine(directory, "base");
            System.IO.Directory.CreateDirectory(corpus); System.IO.Directory.CreateDirectory(modelRoot);
            var inputs = new List<string>(); long expectedBytes = 0;
            string Put(string relative, string content) => Bytes(relative, Encoding.UTF8.GetBytes(content));
            string Bytes(string relative, byte[] bytes)
            {
                budget.Check(); Require(inputs.Count < 32 && expectedBytes + bytes.Length <= 1_048_576, "Literal fixture32 files/1MiB bound.");
                var path = SafePaths.Within(directory, relative, budget); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
                inputs.Add(path); expectedBytes += bytes.Length; return Hash(bytes);
            }
            var omit = variant == Variant.OmitDefaults;
            var flags = omit ? "" : ",\"GpuFeaturesComputed\":false,\"FormalTrainingAdmitted\":false";
            var formal = omit ? "" : ",\"FormalTrainingAdmitted\":false";
            var manifestFlags = omit ? "" : ",\"pilot\":false,\"formal_training_admitted\":false";
            var zeros = omit ? "" : ",\"OriginalExcluded\":0,\"SourceScopeExcluded\":0,\"TokenLimitExcluded\":0";
            var lengthZeros = omit ? "" : ",\"OriginalExcludedRows\":0,\"SourceScopeExcludedRows\":0,\"TokenLimitExcludedRows\":0";
            var trainBytes = Zip("{\"record_id\":\"train-1\",\"split\":\"train\"}\n{\"record_id\":\"train-2\",\"split\":\"train\"}\n");
            var developmentBytes = Zip("{\"record_id\":\"development-1\",\"split\":\"development\"}\n");
            var trainSha = Bytes("corpus/shards/train.jsonl.gz", trainBytes);
            var developmentSha = Bytes("corpus/shards/development.jsonl.gz", developmentBytes);
            var trainRelative = variant == Variant.UnsafeShardPath ? "../escape.gz" : "shards/train.jsonl.gz";
            var developmentRelative = variant == Variant.DuplicateShard ? trainRelative : "shards/development.jsonl.gz";
            var trainShard = $$"""{"path":{{Q(trainRelative)}},"source":"fixture","split":"train","records":2,"bytes":{{N(trainBytes.Length)}},"sha256":{{Q(trainSha)}}}""";
            var developmentShard = $$"""{"path":{{Q(developmentRelative)}},"source":"fixture","split":"development","records":1,"bytes":{{N(developmentBytes.Length)}},"sha256":{{Q(developmentSha)}}}""";
            var shards = "[" + trainShard + "," + developmentShard + "]";
            var parentShards = variant == Variant.WrongParentShard ? "[" + trainShard.Replace("\"records\":2", "\"records\":1", StringComparison.Ordinal) + "," + developmentShard + "]" : shards;
            var emptySha = Put("corpus/exclusions.jsonl", "");
            _ = Put("corpus/components/length-parent/exclusions.jsonl", "");
            var rightsSha = Put("corpus/source-rights.json", "[{\"dataset\":\"fixture\",\"decision\":\"private_fixture_only\",\"license\":\"MIT\"}]");
            var assetsSha = Put("corpus/source-assets.json", "[{\"dataset\":\"fixture\",\"path\":\"source.jsonl\",\"split\":\"train\"}]");
            var noticeSha = Put("corpus/THIRD_PARTY_NOTICES.md", "Literal test source notice. No model or data license acceptance.\n");
            var policySchema = variant == Variant.WrongPolicySchema ? 2 : 1;
            var policy = $$"""{"SchemaVersion":{{N(policySchema)}},"Status":"PRIVATE_EXPERIMENT_SCOPE_FIXED_METADATA_ONLY","DefaultAction":"deny","SourceAllowlist":["fixture"],"SourceDenylist":[],"SourceAssetAllowlist":{"fixture":[{"Path":"source.jsonl","FileSha256":{{Q(Hash(Encoding.UTF8.GetBytes("literal original source\n")))}},"SourceRevision":"fixture-v1","OriginalSplit":"train"}]}{{formal}}{{(omit ? "" : ",\"FullTokenLengthAccepted\":false")}}}""";
            var policySha = Put("corpus/source-scope-policy.json", policy);
            var parentManifest = $$"""{"schema_version":1,"status":"COMPLETE_PREPARATION_ONLY","records_written":3,"shards":{{parentShards}},"source_notice_bindings":{"THIRD_PARTY_NOTICES.md":{{Q(noticeSha)}}}{{manifestFlags}}}""";
            var parentManifestSha = Put("corpus/components/length-parent/manifest.json", parentManifest);
            var parentBindings = $$"""{"ManifestSha256":{{Q(parentManifestSha)}},"ExclusionsSha256":{{Q(emptySha)}},"SourceRightsSha256":{{Q(rightsSha)}},"SourceAssetsSha256":{{Q(assetsSha)}},"SourceAssetsPath":"source-assets.json","SourceRoot":null{{formal}}}""";
            var parentBindingsSha = Put("corpus/components/length-parent/corpus-bindings.json", parentBindings);
            var encoderSha = Put("base/encoder.bin", "literal encoder identity bytes\n");
            var tokenizerSha = Put("base/tokenizer.json", "{\"fixture\":\"literal tokenizer identity only\"}");
            var licenseSha = Put("base/LICENSE", "MIT\nLiteral fixture assets only.\n");
            var sourceNoticeSha = Put("base/SOURCE_NOTICE.md", "Literal source identity notice. No inference is performed.\n");
            var model = $$"""{"schema_version":1,"license":"MIT","weights_file":"encoder.bin","weights_sha256":{{Q(encoderSha)}},"tokenizer_file":"tokenizer.json","tokenizer_sha256":{{Q(tokenizerSha)}}}""";
            var modelSha = Put("base/model.json", model);
            var baseRows = new[]
            {
                (Path: "model.json", Bytes: Encoding.UTF8.GetByteCount(model), Sha: modelSha),
                (Path: "encoder.bin", Bytes: Encoding.UTF8.GetByteCount("literal encoder identity bytes\n"), Sha: encoderSha),
                (Path: "tokenizer.json", Bytes: Encoding.UTF8.GetByteCount("{\"fixture\":\"literal tokenizer identity only\"}"), Sha: tokenizerSha),
                (Path: "LICENSE", Bytes: Encoding.UTF8.GetByteCount("MIT\nLiteral fixture assets only.\n"), Sha: licenseSha),
                (Path: "SOURCE_NOTICE.md", Bytes: Encoding.UTF8.GetByteCount("Literal source identity notice. No inference is performed.\n"), Sha: sourceNoticeSha)
            };
            var commit = new string('a', 40);
            var baseJson = string.Join(",", baseRows.Select(row => $$"""{"path":{{Q(row.Path)}},"bytes":{{N(row.Bytes)}},"sha256":{{Q(row.Sha)}}}"""));
            var readback = $$"""{"status":"PASS_local_complete_base_and_run_full_bytes_sha256","source_model_commit":{{Q(variant == Variant.WrongModelCommit ? new string('b', 40) : commit)}},"source_checkout":{{Q(modelRoot)}},"base_files":5,"files":[{{baseJson}}]}""";
            var readbackSha = Put("model-readback.json", readback);
            var scanTokenizer = variant == Variant.WrongScanTokenizer ? new string('0', 64) : tokenizerSha;
            var scanInputs = $$"""{"SchemaVersion":1,"Algorithm":"strict_decision_v1_choice_rotation_chunk4096_explicit_source_and_token_exclusions_v1","ManifestSha256":{{Q(parentManifestSha)}},"BindingsSha256":{{Q(parentBindingsSha)}},"OriginalExclusionsSha256":{{Q(emptySha)}},"TokenizerSha256":{{Q(scanTokenizer)}},"SourcePolicySha256":{{Q(policySha)}},"MaximumTokens":8,"ChunkRows":4096,"MaximumExclusions":100,"PhysicalRows":3,"Shards":2,"Chunks":2}""";
            // Admit transports these canonical references, not the chunk payloads.
            var zeroChunkSha = Hash(Encoding.UTF8.GetBytes("literal chunk zero"));
            var oneChunkSha = Hash(Encoding.UTF8.GetBytes("literal chunk one"));
            var chunkReferences = $$"""[{"Index":0,"Path":"chunks/chunk-00000.json","Sha256":{{Q(zeroChunkSha)}}},{"Index":1,"Path":"chunks/chunk-00001.json","Sha256":{{Q(oneChunkSha)}}}]""";
            var scan = $$"""{"Status":"PASS_complete_strict_scan_explicit_exclusions_private_only","Inputs":{{scanInputs}},"ScanPlanSha256":{{Q(Hash(Encoding.UTF8.GetBytes(scanInputs)))}},"Chunks":{{chunkReferences}},"PhysicalRows":3,"Accepted":3,"Train":2,"Development":1,"LongestTokens":8,"LongestTrainTokens":8,"LongestTrainId":"train-1","LongestTrainSource":"fixture","TrainingBySource":{"fixture":2},"Families":3,"FamilyIsolationChecked":true,"ExclusionIdentities":0,"ExclusionsPath":"scan-exclusions.jsonl","ExclusionsSha256":{{Q(emptySha)}}{{zeros}}{{flags}}}""";
            var scanSha = Put("corpus/length-scan-complete.json", scan);
            var notices = $$"""{"THIRD_PARTY_NOTICES.md":{{Q(noticeSha)}},"source-scope-policy.json":{{Q(policySha)}},"length-scan-complete.json":{{Q(scanSha)}},"components/length-parent/manifest.json":{{Q(parentManifestSha)}},"components/length-parent/corpus-bindings.json":{{Q(parentBindingsSha)}},"components/length-parent/exclusions.jsonl":{{Q(emptySha)}}}""";
            var admission = $$"""{"SchemaVersion":1,"Status":"COMPLETE_SCAN_APPLIED_EXPLICIT_EXCLUSIONS","ParentManifestSha256":{{Q(parentManifestSha)}},"ParentBindingsSha256":{{Q(parentBindingsSha)}},"ScanCompletionSha256":{{Q(scanSha)}},"SourcePolicySha256":{{Q(policySha)}},"ExclusionsSha256":{{Q(emptySha)}}{{zeros}}}""";
            var manifest = $$"""{"schema_version":1,"status":"COMPLETE_PREPARATION_ONLY","records_written":3,"shards":{{shards}},"source_notice_bindings":{{notices}},"length_admission":{{admission}}{{manifestFlags}}}""";
            var manifestSha = Put("corpus/manifest.json", manifest);
            var bindings = $$"""{"ManifestSha256":{{Q(manifestSha)}},"ExclusionsSha256":{{Q(emptySha)}},"SourceRightsSha256":{{Q(rightsSha)}},"SourceAssetsSha256":{{Q(assetsSha)}},"SourceAssetsPath":"source-assets.json","SourceRoot":null,"SourcePolicySha256":{{Q(policySha)}},"LengthScanSha256":{{Q(scanSha)}}{{formal}}}""";
            var bindingsSha = Put("corpus/corpus-bindings.json", bindings);
            var lengthFlags = variant == Variant.StringBoolean ? ",\"GpuFeaturesComputed\":\"false\",\"FormalTrainingAdmitted\":false" : flags;
            var zeroFields = variant == Variant.StringZero ? ",\"OriginalExcludedRows\":\"0\",\"SourceScopeExcludedRows\":0,\"TokenLimitExcludedRows\":0" : lengthZeros;
            var lengths = $$"""{"Status":"PASS_full_corpus_strict_lengths_only","CorpusManifestSha256":{{Q(manifestSha)}},"CorpusBindingsSha256":{{Q(bindingsSha)}},"TokenizerSha256":{{Q(tokenizerSha)}},"MaximumTokens":8,"PhysicalRows":3,"ExcludedRows":0,"Accepted":3,"Rejected":0,"Train":2,"Development":1,"LongestTokens":8,"LongestTrainingRecordId":"train-1","LongestTrainingTokens":8,"LongestTrainingSource":"fixture","TrainingBySource":{"fixture":2},"FailureLedgerSha256":{{Q(emptySha)}},"CompleteScan":{{(variant == Variant.NoCompleteScan ? "false" : "true")}},"SourcePolicySha256":{{Q(policySha)}},"ScanCompletionSha256":{{Q(scanSha)}},"ParentCorpusManifestSha256":{{Q(parentManifestSha)}}{{zeroFields}}{{lengthFlags}}}""";
            var lengthsSha = Put("lengths.json", lengths);
            var plan = $$"""{"SchemaVersion":1,"CorpusRoot":{{Q(corpus)}},"CorpusManifestSha256":{{Q(manifestSha)}},"CorpusBindingsSha256":{{Q(bindingsSha)}},"LengthReportPath":{{Q(Path.Combine(directory, "lengths.json"))}},"LengthReportSha256":{{Q(lengthsSha)}},"BaseModelRoot":{{Q(modelRoot)}},"ModelCommit":{{Q(commit)}},"TrainRecords":2,"MaximumTokens":8}""";
            var planSha = Put("plan.json", plan);
            Require(inputs.Count == 21, "Independent literal fixture file denominator changed.");
            return new(directory, new(Path.Combine(directory, "plan.json"), planSha, Path.Combine(corpus, "source-scope-policy.json"), policySha,
                Path.Combine(directory, "model-readback.json"), readbackSha), Path.Combine(corpus, "shards", "train.jsonl.gz"),
                Path.Combine(modelRoot, "encoder.bin"), inputs.ToArray(), expectedBytes, encoderSha, tokenizerSha, manifestSha, bindingsSha, emptySha, lengthsSha,
                Hash(Encoding.UTF8.GetBytes(manifestSha + "\n" + bindingsSha + "\n" + emptySha + "\n" + lengthsSha)));
        }
    }

    private static byte[] Zip(string literal)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(Encoding.UTF8.GetBytes(literal));
        return buffer.ToArray();
    }
    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Q(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static void Failed(CorpusInputAdmissionReport report, string evidence)
    {
        Require(report.Status == "FAIL" && report.Failure is not null && report.Failure.Contains(evidence, StringComparison.OrdinalIgnoreCase) &&
            report.VerifiedFiles <= report.PlannedFiles && report.VerifiedBytes <= report.PlannedBytes && !report.FormalTrainingAdmitted,
            "Expected exact failed admission evidence: " + evidence + "; actual: " + report.Failure);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidDataException("Expected preserved exception " + typeof(T).Name + ".");
    }
    private static void RejectData(Action action, string expected)
    {
        try { action(); }
        catch (InvalidDataException error)
        {
            Require(error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), "Resident helper rejected for a different reason: " + error.Message);
            return;
        }
        throw new InvalidDataException("Expected resident input admission rejection: " + expected);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
