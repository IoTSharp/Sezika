using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Sezika;
using Sezika.Cuda;
using Sezika.IndependentModelTool;

namespace Sezika.Cuda.Diagnostics.Tests;

/// <summary>CPU-only declaration/argument fixtures. Accepted test strings are never written to real datasets.</summary>
internal static class OriginalCudaCommandChecks
{
    public static void Run(Action<bool, string> check, CancellationToken cancellationToken, bool tiny = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token; var clock = Stopwatch.StartNew();
        void Guard() { token.ThrowIfCancellationRequested(); if (clock.Elapsed >= TimeSpan.FromSeconds(30)) throw new TimeoutException("GPU CLI contract deadline."); }
        void Expect(Action action, string code, string name)
        { Guard(); try { action(); throw new InvalidOperationException("Expected " + code); } catch (DecisionException error) when (error.Code == code) { check(true, name); } }
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sezika-gpu-cli-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        string[] Args() => ["train-original-gpu", "--package", Path.Combine(directory, "missing-package"), "--records", Path.Combine(directory, "records.jsonl"),
            "--checkpoint", Path.Combine(directory, "output.checkpoint"), "--head", Path.Combine(directory, "output.head"), "--report", Path.Combine(directory, "output.json")];
        try
        {
            var basic = OriginalCudaTrainingCommand.Parse(Args(), token);
            check(basic.DeviceIndex == 0 && basic.Resume is null && basic.Training.Options.MaxSteps == 24, "GPU CLI defaults parse without opening CUDA");
            Expect(() => OriginalCudaTrainingCommand.Parse([.. Args(), "--device", "16"], token), "training_arguments_invalid", "GPU CLI refuses out-of-range device before CUDA");
            if (tiny) return;
            var invalid = new[] { new[] { "--device", "-1" }, new[] { "--device", "NaN" }, new[] { "--device", "0", "--device", "1" },
                new[] { "--unknown", "x" }, new[] { "--learning-rate", "NaN" }, new[] { "--max-steps", "0" }, new[] { "--timeout-seconds", "1801" },
                new[] { "--resume-head", "head" }, new[] { "--device" } };
            for (var index = 0; index < invalid.Length && index < 9; index++)
            { Guard(); var options = invalid[index]; Expect(() => OriginalCudaTrainingCommand.Parse([.. Args(), .. options], token), "training_arguments_invalid", $"GPU CLI invalid option contract {index + 1}/9"); }
            var cpu = OriginalTrainingCommand.Parse(["train-original", .. Args()[1..], "--resume-checkpoint", "old.checkpoint", "--resume-sha256", new string('A', 64),
                "--resume-report", "old.json", "--resume-report-sha256", new string('B', 64)], token);
            check(cpu.Resume is not null, "CPU command retains its historical four-input resume contract");
            var records = Encoding.UTF8.GetBytes(Record("train", "train", "train-family") + "\n" + Record("development", "development", "development-family") + "\n");
            File.WriteAllBytes(basic.Training.Records, records);
            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifest = Manifest(Convert.ToHexString(SHA256.HashData(records)), records.Length);
            File.WriteAllText(manifestPath, manifest.Replace("\"human_acceptance\":\"accepted\"", "\"human_acceptance\":\"pending\"", StringComparison.Ordinal));
            Expect(() => basic.Run(cancellationToken: token), "training_admission_invalid", "Pending human admission fails before missing package or CUDA can be consumed");
            File.WriteAllText(manifestPath, manifest);
            using var admitted = basic.Admit(cancellationToken: token);
            var report = Report(admitted.ManifestSha256, admitted.RecordsSha256, token);
            var reportPath = Path.Combine(directory, "prior.json"); var reportBytes = report.SerializeBounded(token); File.WriteAllBytes(reportPath, reportBytes);
            string[] arguments = [.. Args(), "--max-steps", "4", "--seed", "17", "--learning-rate", "0.05", "--training-timeout-seconds", "5", "--timeout-seconds", "30", "--device", "0",
                "--resume-checkpoint", Path.Combine(directory, "prior.checkpoint"), "--resume-sha256", report.Training.CheckpointSha256,
                "--resume-head", Path.Combine(directory, "prior.head"), "--resume-report", reportPath, "--resume-report-sha256", Convert.ToHexString(SHA256.HashData(reportBytes))];
            check(arguments.Length == 33, "Complete GPU argument contract has exactly 16 pairs and explicit restored head");
            var resumed = OriginalCudaTrainingCommand.Parse(arguments, token);
            check(resumed.ReadResumeReport(admitted, token)!.Training.CompletedSteps == 2, "Formal schema3 resume preflight binds declaration and optimizer without opening CUDA");
            Expect(() => (resumed with { Resume = resumed.Resume! with { ReportSha256 = new string('0', 64) } }).ReadResumeReport(admitted, token),
                "training_report_hash_invalid", "GPU CLI rejects report SHA before device or encoder");
            using var reportDocument = System.Text.Json.JsonDocument.Parse(reportBytes);
            var cpuBytes = Encoding.UTF8.GetBytes(reportDocument.RootElement.GetProperty("training").GetRawText());
            var cpuPath = Path.Combine(directory, "cpu.json"); File.WriteAllBytes(cpuPath, cpuBytes);
            Expect(() => (resumed with { Resume = resumed.Resume! with { Report = cpuPath, ReportSha256 = Convert.ToHexString(SHA256.HashData(cpuBytes)) } }).ReadResumeReport(admitted, token),
                "training_gpu_report_invalid", "GPU CLI rejects old CPU schema2 before device or encoder");
            var gpuAdapted = EvaluationTrainingReportAdapter.ParseBounded(reportBytes, token);
            var cpuAdapted = EvaluationTrainingReportAdapter.ParseBounded(cpuBytes, token);
            check(gpuAdapted.Training.FeatureSha256 == report.Training.FeatureSha256 && gpuAdapted.TrainingBackend.StartsWith("cuda-", StringComparison.Ordinal) &&
                cpuAdapted.TrainingBackend.StartsWith("scalar-", StringComparison.Ordinal) && EvaluationTrainingReportAdapter.EvaluationBackend.StartsWith("scalar-", StringComparison.Ordinal),
                "Evaluation adapter supports complete schema2/schema3 and identifies scalar evaluation separately");
            var fixture = report with { Training = report.Training with { UsesRealIndependentEncoder = false, Status = "fixture_contract_only", FeatureSource = "synthetic_fixture" },
                Execution = report.Execution with { DataMode = "synthetic_fixture", FeatureBackend = "synthetic-marker-features-v1" } };
            Expect(() => EvaluationTrainingReportAdapter.ParseBounded(fixture.SerializeBounded(token), token), "training_resume_backend_mismatch", "Evaluation adapter refuses GPU engineering fixture provenance");
            var json = Encoding.UTF8.GetString(reportBytes);
            Expect(() => EvaluationTrainingReportAdapter.ParseBounded(Encoding.UTF8.GetBytes(json.Replace("\"contract\":", "\"unknown\": 1, \"contract\":", StringComparison.Ordinal)), token),
                "training_gpu_report_invalid", "Evaluation adapter preserves strict unknown-field rejection");
            Expect(() => EvaluationTrainingReportAdapter.ParseBounded(Encoding.UTF8.GetBytes(json.Replace("\"schema_version\": 3,", "\"schema_version\": 3, \"schema_version\": 3,", StringComparison.Ordinal)), token),
                "training_report_invalid", "Evaluation adapter rejects duplicated schema rather than choosing one");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { _ = OriginalCudaTrainingCommand.Parse(Args(), cancelled.Token); throw new InvalidOperationException("Cancelled parser returned."); }
            catch (OperationCanceledException) { check(true, "GPU CLI pre-cancellation is honored before all work"); }
            Guard();
        }
        finally
        {
            var owned = Path.GetFullPath(directory);
            if (!owned.StartsWith(Path.GetFullPath(Path.GetTempPath()) + "sezika-gpu-cli-", StringComparison.Ordinal)) throw new InvalidOperationException("CLI fixture cleanup escaped its GUID root.");
            Directory.Delete(owned, recursive: true);
        }
    }

    private static GpuTrainingReport Report(string manifestSha, string recordsSha, CancellationToken token)
    {
        var artifacts = CudaTrainingExecutionIdentity.Read(token);
        return new() { SchemaVersion = 3, Contract = GpuTrainingReport.ContractName,
            Training = new() { SchemaVersion = 2, Status = "development_smoke_only", Identity = WorkflowTrainingIdentity.From(new(IndependentModelLoader.ModelId,
                IndependentModelLoader.ConvertedEncoderSha256, IndependentModelLoader.TokenizerSha256, manifestSha, 8)), RecordsSha256 = recordsSha,
                FeatureSha256 = new string('E', 64), HeadSha256 = new string('F', 64), CheckpointSha256 = new string('A', 64), Seed = 17, LearningRate = 0.05f,
                StepSemantics = "total_completed_steps", InitialSteps = 0, MaxSteps = 2, CompletedSteps = 2, LastLoss = 0.5, TrainCount = 1, DevelopmentCount = 1,
                UsesRealIndependentEncoder = true, RepresentsQualityAcceptance = false, Optimizer = "sgd_v1_no_momentum", FeatureSource = "original_encoder",
                TrainingTimeoutSeconds = 5, ElapsedMilliseconds = 1, Resume = null, DevelopmentGroups = [new() { Language = "en", QuestionType = "boolean", Count = 1, Correct = 1, Accuracy = 1 }] },
            Execution = new() { DataMode = "approved_original", FeatureBackend = "cuda-modernbert-fp32-v1", TrainingBackend = "cuda-independent-linear-marker-head-sgd-fp32",
                NumericContract = "cuda-linear-ce-gradient-sgd-fp32-v1", OrderContract = "input-order-xorshift32-fixed-cycle-v1", DevelopmentBackend = "scalar-linear-marker-head-fp64",
                KernelSourceSha256 = artifacts.SourceSha256, KernelManifestSha256 = artifacts.ManifestSha256, Compiler = artifacts.Compiler, CompilerVersion = artifacts.CompilerVersion,
                Target = artifacts.Target, PtxVersion = artifacts.PtxVersion, Kernels = artifacts.Kernels.Select(k => new GpuTrainingKernelIdentity { Name = k.Name, Entry = k.Entry,
                    BlockSize = k.BlockSize, ParameterCount = k.ParameterCount, Sha256 = k.Sha256 }).ToArray(),
                Device = new() { Uuid = new string('E', 32), Name = "synthetic-contract-device-no-GPU", DriverVersion = 13000, ComputeCapabilityMajor = 8,
                    ComputeCapabilityMinor = 9, TotalMemoryBytes = 8_000_000_000, DeviceIndex = 0 } } };
    }
    private static string Record(string id, string split, string family) => $$"""{"record_id":"{{id}}","split":"{{split}}","language":"en","family_id":"{{family}}","source":"synthetic-cli-contract","review_status":"accepted","training_admission":true,"human_review_id":"synthetic-parser-test-not-human-evidence","type":"boolean","state":"x","instruction":"judge","target_index":0,"statement":"x","when_false":"no","when_true":"yes"}""";
    private static string Manifest(string hash, int size) => $$"""{"schema_version":2,"training_contract":"reviewed-original-records-v1","dataset_id":"synthetic-cli-contract-only","records_file":"records.jsonl","records_sha256":"{{hash}}","records_bytes":{{size}},"source":"synthetic-cli-contract","origin":"Original","purpose":"training","license_expression":"Apache-2.0","license_review":"accepted","allowed_uses":["Research","Development"],"allowed_splits":["Train","Development"],"split_counts":{"train":1,"development":1},"human_acceptance":"accepted","training_admission":true,"human_review_id":"synthetic-parser-test-not-human-evidence","represents_model_quality":false}""";
}
