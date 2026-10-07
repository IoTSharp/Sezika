using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Sezika;
using Sezika.IndependentModelTool;

namespace Sezika.Tests;

/// <summary>Only synthetic metadata/features. Accepted strings simulate declarations and are never written into real datasets.</summary>
internal static class GpuTrainingReportChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string, string> expectCode, bool tiny = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var clock = Stopwatch.StartNew();
        void Guard() { token.ThrowIfCancellationRequested(); if (clock.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("GPU metadata checks deadline."); }
        var report = FixtureReport();
        var bytes = report.SerializeBounded(token);
        var parsed = GpuTrainingReport.ParseBounded(bytes, token);
        check(parsed.SchemaVersion == 3 && parsed.Training.SchemaVersion == 2 && !parsed.Training.UsesRealIndependentEncoder &&
            parsed.Execution.DataMode == "synthetic_fixture" && !parsed.Training.RepresentsQualityAcceptance,
            "tiny GPU metadata roundtrip retains full v2 and explicit synthetic fixture provenance; no GPU operation");
        if (tiny) return;
        Guard();
        expectCode(() => WorkflowTrainingReport.ParseBounded(bytes, token), "training_report_invalid", "CPU v2 reader refuses the outer GPU v3 report");
        var json = Encoding.UTF8.GetString(bytes);
        void InvalidJson(string original, string replacement, string name, string code = "training_gpu_report_invalid")
        {
            Guard();
            var changed = json.Replace(original, replacement, StringComparison.Ordinal);
            if (changed == json) throw new InvalidOperationException("Synthetic report mutation missed its field.");
            expectCode(() => GpuTrainingReport.ParseBounded(Encoding.UTF8.GetBytes(changed), token), code, name);
        }
        InvalidJson("\"schema_version\": 3", "\"schema_version\": 2", "GPU reader rejects old outer schema");
        InvalidJson("\"schema_version\": 2", "\"schema_version\": 1", "GPU reader validates complete inner v2 version", "training_report_invalid");
        InvalidJson("\"schema_version\": 3,", "\"schema_version\": 3, \"unknown\": 1,", "GPU report rejects unknown fields");
        InvalidJson("\"contract\":", "\"contract\": \"duplicate\", \"contract\":", "GPU report rejects duplicate fields");
        InvalidJson("\"execution\":", "\"unexpected_execution\":", "GPU report rejects missing execution identity");
        InvalidJson("\"kernels\":", "\"unknown_kernels\":", "GPU report rejects nested unknown or missing fields");
        InvalidJson("\"uuid\":", "\"unknown_uuid\":", "GPU device identity requires physical UUID");
        InvalidJson("\"count\": 1", "\"count\": 2", "GPU report retains inner development denominator validation", "training_report_invalid");
        InvalidJson("\"resume\": null", "\"unexpected_resume\": null", "GPU report retains inner required null-resume field");
        InvalidJson("\"training_backend\": \"cuda-independent-linear-marker-head-sgd-fp32\"", "\"training_backend\": \"cpu\"", "GPU report cannot declare CPU training");
        InvalidJson("\"numeric_contract\": \"cuda-linear-ce-gradient-sgd-fp32-v1\"", "\"numeric_contract\": \"fp64\"", "GPU report rejects a different numeric contract");
        InvalidJson("\"order_contract\": \"input-order-xorshift32-fixed-cycle-v1\"", "\"order_contract\": \"epoch_shuffle\"", "GPU report rejects different ordering semantics");
        InvalidJson("\"feature_backend\": \"synthetic-marker-features-v1\"", "\"feature_backend\": \"cuda-modernbert-fp32-v1\"", "Fixture cannot claim real GPU encoder features");
        InvalidJson("\"parameter_count\": 9", "\"parameter_count\": 8", "GPU kernel ABI parameter count is fixed");
        InvalidJson("\"represents_quality_acceptance\": false", "\"represents_quality_acceptance\": true", "GPU report rejects quality acceptance claims", "training_report_invalid");
        expectCode(() => (report with { Execution = null! }).SerializeBounded(token), "training_gpu_report_invalid", "GPU report rejects null execution");
        expectCode(() => (report with { Training = null! }).SerializeBounded(token), "training_gpu_report_invalid", "GPU report rejects null inner training");
        expectCode(() => report.BindApprovedIdentity(report.Training.Identity.ToIdentity(), token), "training_resume_backend_mismatch", "Formal resume and evaluation reject synthetic engineering fixtures");
        report.RequireExecutionCompatible(report.Execution with { Device = report.Execution.Device with { DeviceIndex = 1, Uuid = report.Execution.Device.Uuid.ToLowerInvariant() } }, token);
        check(true, "Physical UUID is stable across enumeration position and hex spelling");
        var changedExecutions = new[]
        {
            report.Execution with { KernelSourceSha256 = new string('1', 64) },
            report.Execution with { KernelManifestSha256 = new string('2', 64) },
            report.Execution with { Device = report.Execution.Device with { Uuid = new string('1', 32) } },
            report.Execution with { Device = report.Execution.Device with { DriverVersion = report.Execution.Device.DriverVersion + 1 } },
            report.Execution with { Device = report.Execution.Device with { Name = "other-device" } },
            report.Execution with { Device = report.Execution.Device with { ComputeCapabilityMinor = 8 } },
            report.Execution with { Device = report.Execution.Device with { TotalMemoryBytes = report.Execution.Device.TotalMemoryBytes + 1 } }
        };
        for (var index = 0; index < changedExecutions.Length && index < 7; index++)
        {
            Guard(); var changed = changedExecutions[index];
            expectCode(() => report.RequireExecutionCompatible(changed, token), "training_resume_backend_mismatch", $"GPU resume rejects execution identity mutation {index + 1}/7");
        }
        var kernels = (GpuTrainingKernelIdentity[])report.Execution.Kernels.Clone();
        kernels[14] = kernels[14] with { Sha256 = new string('1', 64) };
        expectCode(() => report.RequireExecutionCompatible(report.Execution with { Kernels = kernels }, token), "training_resume_backend_mismatch", "GPU resume binds actual individual PTX hashes");
        expectCode(() => (report with { Execution = report.Execution with { Kernels = kernels[..16] } }).SerializeBounded(token), "training_gpu_report_invalid", "GPU report requires all seventeen kernels");
        (kernels[0], kernels[1]) = (kernels[1], kernels[0]);
        expectCode(() => (report with { Execution = report.Execution with { Kernels = kernels } }).SerializeBounded(token), "training_gpu_report_invalid", "GPU report rejects reordered or duplicated kernel identities");
        var approved = report with { Training = report.Training with { UsesRealIndependentEncoder = true, FeatureSource = "original_encoder", Status = "development_smoke_only" },
            Execution = report.Execution with { DataMode = "approved_original", FeatureBackend = "cuda-modernbert-fp32-v1" } };
        check(GpuTrainingReport.ParseBounded(approved.SerializeBounded(token), token).BindApprovedIdentity(approved.Training.Identity.ToIdentity(), token) == approved.Training.Identity.ToIdentity(),
            "Synthetic metadata can exercise formal schema identity binding without creating human review evidence");
        expectCode(() => (approved with { Execution = report.Execution }).SerializeBounded(token), "training_gpu_report_invalid", "Real feature declaration cannot be paired with fixture execution mode");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { _ = GpuTrainingReport.ParseBounded(bytes, cancelled.Token); throw new InvalidOperationException("Pre-cancelled report parsed."); }
            catch (OperationCanceledException) { check(true, "GPU report pre-cancellation needs no device"); }
        }

        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sezika-gpu-contract-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var recordsPath = Path.Combine(directory, "records.jsonl"); var manifestPath = Path.Combine(directory, "manifest.json"); var reportPath = Path.Combine(directory, "report.json");
        var owned = new Dictionary<string, string>(StringComparer.Ordinal);
        void WriteOwned(string path, byte[] content) { Guard(); File.WriteAllBytes(path, content); owned[path] = Convert.ToHexString(SHA256.HashData(content)); }
        string train = Record("train", "train", "train-family", "choice", 1);
        string development = Record("development", "development", "development-family", "boolean", 0);
        byte[] Records(string trainRow, string developmentRow) => Encoding.UTF8.GetBytes(trainRow + "\n" + developmentRow + "\n");
        void Inputs(byte[] content, Func<string, string>? changeManifest = null)
        {
            WriteOwned(recordsPath, content);
            var manifest = Manifest(Convert.ToHexString(SHA256.HashData(content)), content.Length);
            WriteOwned(manifestPath, Encoding.UTF8.GetBytes(changeManifest?.Invoke(manifest) ?? manifest));
        }
        void RejectInputs(byte[] content, Func<string, string>? changeManifest, string name)
        {
            Inputs(content, changeManifest);
            expectCode(() => { using var rejected = OriginalTrainingAdmission.ReadBounded(recordsPath, manifestPath, cancellationToken: token); }, "training_admission_invalid", name);
        }
        try
        {
            WriteOwned(reportPath, bytes);
            var reportHash = Convert.ToHexString(SHA256.HashData(bytes));
            check(GpuTrainingReport.ReadBounded(reportPath, reportHash.ToLowerInvariant(), token).Training.FeatureSha256 == report.Training.FeatureSha256,
                "GPU report reads one hash-bound file snapshot");
            expectCode(() => GpuTrainingReport.ReadBounded(reportPath, new string('0', 64), token), "training_report_hash_invalid", "GPU report rejects wrong expected file SHA");
            Inputs(Records(train, development));
            var progress = 0;
            using (var input = OriginalTrainingAdmission.ReadBounded(recordsPath, manifestPath, (_, _) => { Guard(); progress++; }, token))
            {
                var choice = (IndependentChoiceQuestion)input.Records[0].Question;
                check(input.Records.Count == 2 && input.Records[0].RecordId == "train" && input.Records[1].RecordId == "development" &&
                    choice.Candidates[0].Id == "b" && choice.Candidates[1].Id == "a" && input.Records[0].TargetIndex == 1 && progress == 2,
                    "Admission preserves source record/candidate order and hard-label index in synthetic declarations");
                check(input.RecordsSha256.Equals(input.Manifest.RecordsSha256, StringComparison.OrdinalIgnoreCase) && input.ManifestSha256.Length == 64 &&
                    input.Manifest.HumanReviewId == "synthetic-parser-test-not-human-evidence", "Admission binds snapshots without generating human approval");
                if (OperatingSystem.IsWindows())
                {
                    try { using var writer = new FileStream(recordsPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); throw new InvalidOperationException("Read lease allowed writer."); }
                    catch (IOException) { check(true, "Formal input holds records read lease during consumption"); }
                    try { using var writer = new FileStream(manifestPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); throw new InvalidOperationException("Manifest read lease allowed writer."); }
                    catch (IOException) { check(true, "Formal input holds manifest read lease during consumption"); }
                }
            }
            RejectInputs(Records(train, development), value => value.Replace("\"human_acceptance\":\"accepted\"", "\"human_acceptance\":\"pending\"", StringComparison.Ordinal), "Formal admission rejects pending human review");
            RejectInputs(Records(train, development), value => value.Replace("\"training_admission\":true", "\"training_admission\":false", StringComparison.Ordinal), "Formal admission rejects missing approval to train");
            RejectInputs(Records(train, development), value => value.Replace("\"origin\":\"Original\"", "\"origin\":\"AI drafted\"", StringComparison.Ordinal), "Formal admission rejects AI pilot provenance");
            RejectInputs(Records(train, development), value => value.Replace("\"purpose\":\"training\"", "\"purpose\":\"pilot\"", StringComparison.Ordinal), "Formal admission rejects pilot purpose");
            RejectInputs(Records(train, development), value => value.Replace("\"license_review\":\"accepted\"", "\"license_review\":\"pending\"", StringComparison.Ordinal), "Formal admission rejects pending license review");
            RejectInputs(Records(train, development), value => value.Replace("\"allowed_splits\":[\"Train\",\"Development\"]", "\"allowed_splits\":[\"Train\",\"Audit\"]", StringComparison.Ordinal), "Formal admission rejects audit split authorization");
            var originalBytes = Records(train, development);
            var originalHash = Convert.ToHexString(SHA256.HashData(originalBytes));
            RejectInputs(originalBytes, value => value.Replace(originalHash, new string('0', 64), StringComparison.Ordinal), "Formal admission rejects a valid SHA-256 that differs from consumed records");
            RejectInputs(originalBytes, value => value.Replace($"\"records_bytes\":{originalBytes.Length}", $"\"records_bytes\":{originalBytes.Length + 1}", StringComparison.Ordinal), "Formal admission rejects records byte count mismatch");
            RejectInputs(Records(train, development), value => value.Replace("\"records_bytes\":", "\"unexpected_records_bytes\":", StringComparison.Ordinal), "Formal admission rejects unknown or missing manifest fields");
            RejectInputs(Records(train, development), value => value.Replace("\"train\":1", "\"train\":2", StringComparison.Ordinal), "Formal admission checks exact train/development denominators");
            RejectInputs(Records(train, development), value => value.Replace("\"schema_version\":2", "\"schema_version\":2,\"schema_version\":2", StringComparison.Ordinal), "Formal admission rejects duplicate manifest properties");
            RejectInputs(Records(train.Replace("\"review_status\":\"accepted\"", "\"review_status\":\"pending\"", StringComparison.Ordinal), development), null, "Formal admission rejects a pending record even with matching manifest SHA");
            RejectInputs(Records(train.Replace("\"training_admission\":true", "\"training_admission\":false", StringComparison.Ordinal), development), null, "Formal admission rejects an unadmitted record");
            RejectInputs(Records(train.Replace("\"human_review_id\":", "\"unexpected_human_review_id\":", StringComparison.Ordinal), development), null, "Formal admission requires record human review declaration");
            RejectInputs(Records(train, development.Replace("development-family", "train-family", StringComparison.Ordinal)), null, "Family isolation cannot be replaced by a correct whole-file hash");
            RejectInputs(Records(train, development.Replace("\"split\":\"development\"", "\"split\":\"audit\"", StringComparison.Ordinal)), null, "Formal admission rejects record audit split");
            RejectInputs(Records(train.Replace("synthetic-contract-source", "other-source", StringComparison.Ordinal), development), null, "Formal admission binds record source to reviewed manifest source");
            RejectInputs(Records(train.Replace("\"target_index\":1", "\"target_index\":2", StringComparison.Ordinal), development), null, "Formal admission checks ordered candidate label bounds");
            RejectInputs(Records(train.Replace("\"record_id\":\"train\"", "\"record_id\":\"train\",\"record_id\":\"other\"", StringComparison.Ordinal), development), null, "Formal admission rejects duplicate record properties");
            RejectInputs(Records(train.Replace("\"state\":{\"text\":\"fixture\"}", "\"state\":{\"text\":\"fixture\",\"text\":\"other\"}", StringComparison.Ordinal), development), null, "Formal admission checks duplicates inside arbitrary state JSON");
            RejectInputs(Records(train, development.Replace("\"record_id\":\"development\"", "\"record_id\":\"train\"", StringComparison.Ordinal)), null, "Formal admission requires globally unique record IDs");
            RejectInputs(Records(train.Replace("\"id\":\"a\"", "\"id\":\"b\"", StringComparison.Ordinal), development), null, "Formal admission rejects duplicate candidate IDs");
            Inputs(Records(Record("train", "train", "train-family", "score", 1), development));
            using (var scoreInput = OriginalTrainingAdmission.ReadBounded(recordsPath, manifestPath, cancellationToken: token))
                check(scoreInput.Records[0].Question is IndependentScoreQuestion score && score.Levels.SequenceEqual(["low", "high"]), "Formal reader preserves score level mapping");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { using var ignored = OriginalTrainingAdmission.ReadBounded(recordsPath, manifestPath, cancellationToken: cancelled.Token); throw new InvalidOperationException("Cancelled admission succeeded."); }
            catch (OperationCanceledException) { check(true, "Admission pre-cancellation opens no model or GPU"); }
            Guard();
        }
        finally
        {
            var cleanupClock = Stopwatch.StartNew();
            foreach (var item in owned.Take(3))
            {
                if (cleanupClock.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Synthetic metadata cleanup deadline.");
                var path = Path.GetFullPath(item.Key);
                if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup escaped owned test directory.");
                if (File.Exists(path) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == item.Value) File.Delete(path);
            }
            Directory.Delete(directory, recursive: false);
        }
    }

    private static GpuTrainingReport FixtureReport()
    {
        var names = new[] { "VectorAdd", "Linear", "Embedding", "Norm", "SplitQkv", "Rope", "AttentionScores", "Softmax", "AttentionContext", "Add", "Activate", "GatedGelu", "RowBias", "TypeEmbeddingAdd", "MarkerCrossEntropy", "MarkerGradientSgd", "MarkerUpdateStatus" };
        var counts = new[] { 4, 7, 5, 8, 6, 6, 7, 3, 6, 3, 4, 4, 4, 5, 6, 9, 5 };
        var clock = Stopwatch.StartNew();
        var kernels = new GpuTrainingKernelIdentity[17];
        for (var index = 0; index < 17; index++)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(2)) throw new TimeoutException("Fixture metadata setup deadline.");
            kernels[index] = new() { Name = names[index], Entry = "Kernel_" + names[index], BlockSize = 256, ParameterCount = counts[index], Sha256 = new string('D', 64) };
        }
        return new()
        {
            SchemaVersion = 3, Contract = GpuTrainingReport.ContractName,
            Training = new WorkflowTrainingReport
            {
                SchemaVersion = 2, Status = "fixture_contract_only", Identity = WorkflowTrainingIdentity.From(new MarkerFeatureIdentity("sezika/gpu-metadata-fixture", new string('A', 64), new string('B', 64), new string('C', 64), 2)),
                RecordsSha256 = new string('D', 64), FeatureSha256 = new string('E', 64), HeadSha256 = new string('F', 64), CheckpointSha256 = new string('1', 64),
                Seed = 17, LearningRate = 0.05f, StepSemantics = "total_completed_steps", InitialSteps = 0, MaxSteps = 4, CompletedSteps = 4,
                LastLoss = 0.5, TrainCount = 1, DevelopmentCount = 1, UsesRealIndependentEncoder = false, RepresentsQualityAcceptance = false,
                Optimizer = "sgd_v1_no_momentum", FeatureSource = "synthetic_fixture", TrainingTimeoutSeconds = 5, ElapsedMilliseconds = 1, Resume = null,
                DevelopmentGroups = [new() { Language = "en", QuestionType = "boolean", Count = 1, Correct = 1, Accuracy = 1 }]
            },
            Execution = new()
            {
                DataMode = "synthetic_fixture", FeatureBackend = "synthetic-marker-features-v1", TrainingBackend = "cuda-independent-linear-marker-head-sgd-fp32",
                NumericContract = "cuda-linear-ce-gradient-sgd-fp32-v1", OrderContract = "input-order-xorshift32-fixed-cycle-v1", DevelopmentBackend = "scalar-linear-marker-head-fp64",
                KernelSourceSha256 = new string('A', 64), KernelManifestSha256 = new string('B', 64), Compiler = "ILGPU", CompilerVersion = "1.5.3", Target = "sm_70", PtxVersion = "7.0", Kernels = kernels,
                Device = new() { Uuid = new string('E', 32), Name = "synthetic-device-metadata-not-hardware", DriverVersion = 13000, ComputeCapabilityMajor = 8, ComputeCapabilityMinor = 9, TotalMemoryBytes = 8_000_000_000, DeviceIndex = 0 }
            }
        };
    }

    private static string Record(string id, string split, string family, string type, int target)
    {
        var shape = type switch
        {
            "choice" => "\"candidates\":[{\"id\":\"b\",\"text\":\"second\"},{\"id\":\"a\",\"text\":\"first\"}]",
            "score" => "\"levels\":[\"low\",\"high\"]",
            _ => "\"statement\":\"fixture\",\"when_false\":\"false\",\"when_true\":\"true\""
        };
        return $$"""{"record_id":"{{id}}","split":"{{split}}","language":"en","family_id":"{{family}}","source":"synthetic-contract-source","review_status":"accepted","training_admission":true,"human_review_id":"synthetic-parser-test-not-human-evidence","type":"{{type}}","state":{"text":"fixture"},"instruction":"fixture","target_index":{{target}},{{shape}}}""";
    }

    private static string Manifest(string hash, int size) => $$"""{"schema_version":2,"training_contract":"reviewed-original-records-v1","dataset_id":"synthetic-contract-only","records_file":"records.jsonl","records_sha256":"{{hash}}","records_bytes":{{size}},"source":"synthetic-contract-source","origin":"Original","purpose":"training","license_expression":"Apache-2.0","license_review":"accepted","allowed_uses":["Research","Development"],"allowed_splits":["Train","Development"],"split_counts":{"train":1,"development":1},"human_acceptance":"accepted","training_admission":true,"human_review_id":"synthetic-parser-test-not-human-evidence","represents_model_quality":false}""";
}
