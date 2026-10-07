using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Sezika;
using Sezika.Cuda;
using Sezika.IndependentModelTool;

namespace Sezika.Cuda.Diagnostics.Tests;

internal static class IndependentCudaWorkflowChecks
{
    public static void Prepare(Action<bool, string> check, CancellationToken cancellationToken)
    {
        var artifacts = CudaTrainingExecutionIdentity.Read(cancellationToken);
        check(artifacts.Kernels.Count == 17 && artifacts.SourceSha256 == "3743158624364474AE920D4863C48D322168954B8FB992B5868D7DB0660E2FBD" &&
            artifacts.ManifestSha256 == "8CF83A3B5145345D92D50D4695B4DD0200AE690C9E1FD70E910B399241B18D79", "CPU-only static CUDA identity matches unchanged source and complete manifest");
        check(artifacts.Kernels[14].Sha256 == "311091D74C816E06401373B005FD2A7CD1CB40D9A083B0495452EAF434BF653B" &&
            artifacts.Kernels[15].Sha256 == "20D6588D9E3E20C24703CC250483CDD51FC2E13CAE29BEE93989CC92BE160285" &&
            artifacts.Kernels[16].Sha256 == "623D6EBE5C1435DC59C2CC229DD5A4685C39E1733521F248416B6ABB5543D9A4", "CPU-only identity hashes exact static training PTX bytes including terminal NUL");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { _ = CudaTrainingExecutionIdentity.Read(cancelled.Token); throw new InvalidOperationException("Cancelled identity read succeeded."); }
        catch (OperationCanceledException) { check(true, "Static identity supports cancellation without opening CUDA"); }
    }

    public static void Run(CudaDevice device, Action<bool, string> check, CancellationToken cancellationToken, bool tiny = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(80));
        var token = deadline.Token; var clock = Stopwatch.StartNew();
        void Guard() { token.ThrowIfCancellationRequested(); if (clock.Elapsed > TimeSpan.FromSeconds(80)) throw new TimeoutException("GPU workflow diagnostic deadline."); }
        void ExpectCode(Action action, string code, string name) { Guard(); try { action(); throw new InvalidOperationException("Expected " + code); } catch (DecisionException exception) when (exception.Code == code) { check(true, name); } }
        var identity = new MarkerFeatureIdentity("sezika/cuda-workflow-fixture", new string('A', 64), new string('B', 64), new string('C', 64), 2);
        var train = new MarkerFeatureSet(identity, [Example("train", MarkerQuestionType.Choice, [[1f, 0f], [0f, 1f]], 0)]);
        var development = new[] { Example("development", MarkerQuestionType.Boolean, [[-1f, 0f], [1f, 0f]], 0) };
        var options = new MarkerTrainingOptions(17, 2, 0.05f, TimeSpan.FromSeconds(10)); var four = options with { MaxSteps = 4 };
        var recordsHash = new string('D', 64);
        var baseline = device.GetMemorySnapshot();
        void Clean(string name) { var snapshot = device.GetMemorySnapshot(); check(snapshot.OwnedBytes == baseline.OwnedBytes && snapshot.OwnedAllocationCount == baseline.OwnedAllocationCount &&
            snapshot.LoadedModuleCount == baseline.LoadedModuleCount && snapshot.ReleaseFailureCount == baseline.ReleaseFailureCount, name); }
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sezika-cuda-workflow-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(directory);
        TrainingOutputPaths Paths(string name) => new(Path.Combine(directory, name + ".checkpoint"), Path.Combine(directory, name + ".head"), Path.Combine(directory, name + ".json"));
        CudaTrainingResumePaths Resume(TrainingOutputPaths paths, CudaTrainingWorkflowResult result) => new(paths.Checkpoint, result.Report.Training.CheckpointSha256, paths.Head, paths.Report, result.ReportSha256);
        bool Absent(TrainingOutputPaths paths) => !File.Exists(paths.Checkpoint) && !File.Exists(paths.Head) && !File.Exists(paths.Report);
        try
        {
            check(device.Info.Uuid.Length == 32 && device.Info.Uuid.All(char.IsAsciiHexDigit) && device.Info.Uuid.Any(character => character != '0'), "CUDA Driver reports a real 16-byte physical UUID");
            var firstPaths = Paths("first"); var first = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, options, firstPaths, cancellationToken: token);
            var report = GpuTrainingReport.ReadBounded(firstPaths.Report, first.ReportSha256, token);
            check(report.SchemaVersion == 3 && report.Execution.Device.Uuid == device.Info.Uuid && report.Execution.Device.DriverVersion == device.Info.DriverVersion &&
                report.Execution.DataMode == "synthetic_fixture" && !report.Training.UsesRealIndependentEncoder && !report.Training.RepresentsQualityAcceptance,
                "GPU workflow publishes truthful fixture schema3 with actual Driver and kernel identity");
            var checkpoint = MarkerTrainingCheckpoint.Load(firstPaths.Checkpoint, train, report.Training.CheckpointSha256, token);
            var head = IndependentMarkerHeadAssetStore.Load(firstPaths.Head, identity, checkpoint.FeatureSha256, report.Training.HeadSha256, token);
            check(checkpoint.CompletedSteps == 2 && head.Head.CopyWeights().SequenceEqual(checkpoint.Head.CopyWeights()), "Published GPU checkpoint and inference head pass production loaders");
            Clean("GPU workflow tiny success restores caller-owned resources");
            if (tiny) return;
            var resume = Resume(firstPaths, first);
            var resumedPaths = Paths("resumed"); var resumed = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four, resumedPaths, resume, cancellationToken: token);
            var fullPaths = Paths("full"); var full = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four, fullPaths, cancellationToken: token);
            check(resumed.State.Head.CopyWeights().SequenceEqual(full.State.Head.CopyWeights()) && resumed.State.LastLoss == full.State.LastLoss &&
                resumed.Report.Training.CheckpointSha256 == full.Report.Training.CheckpointSha256 && resumed.Report.Training.HeadSha256 == full.Report.Training.HeadSha256 &&
                resumed.Report.Training.InitialSteps == 2 && resumed.Report.Training.Resume!.ReportSha256 == first.ReportSha256, "GPU schema3 2+2 restoration equals uninterrupted four-step weights, loss, checkpoint and head bytes");
            Clean("GPU workflow restored and uninterrupted execution release owned resources");
            var before = device.GetTelemetry().KernelLaunchCount;
            void RejectHeadMetadata(string name, int field)
            {
                Guard();
                var alteredHeadPath = Path.Combine(directory, name + ".head");
                var alteredHeadHash = RewriteHeadMetadata(firstPaths.Head, alteredHeadPath, field);
                var alteredHead = IndependentMarkerHeadAssetStore.Load(alteredHeadPath, identity, checkpoint.FeatureSha256, alteredHeadHash, token);
                var validFixture = alteredHead.Head.CopyWeights().SequenceEqual(checkpoint.Head.CopyWeights()) &&
                    (alteredHead.Seed != checkpoint.Seed || alteredHead.LearningRate != checkpoint.LearningRate ||
                        alteredHead.CompletedSteps != checkpoint.CompletedSteps || alteredHead.LastLoss != checkpoint.LastLoss);
                var alteredReport = first.Report with { Training = first.Report.Training with { HeadSha256 = alteredHeadHash } };
                var alteredReportPath = Path.Combine(directory, name + ".json");
                var alteredReportBytes = alteredReport.SerializeBounded(token); File.WriteAllBytes(alteredReportPath, alteredReportBytes);
                ExpectCode(() => IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four, Paths(name + "-output"),
                    resume with { Head = alteredHeadPath, Report = alteredReportPath, ReportSha256 = Convert.ToHexString(SHA256.HashData(alteredReportBytes)) }, cancellationToken: token),
                    "training_resume_mismatch", "GPU restoration rejects correctly hashed head with changed " + name);
                check(validFixture && device.GetTelemetry().KernelLaunchCount == before,
                    "Changed " + name + " fixture passes production hash/checksum validation with identical weights and launches zero kernels");
            }
            RejectHeadMetadata("head-seed", 0);
            RejectHeadMetadata("head-learning-rate", 1);
            RejectHeadMetadata("head-completed-steps", 2);
            RejectHeadMetadata("head-last-loss", 3);
            ExpectCode(() => IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four, Paths("bad-report-sha"), resume with { ReportSha256 = new string('0', 64) }, cancellationToken: token), "training_report_hash_invalid", "GPU restoration rejects wrong external report SHA before launching kernels");
            ExpectCode(() => IndependentCudaTrainingWorkflow.RunFixture(device, train, development, new string('E', 64), four, Paths("changed-records"), resume, cancellationToken: token), "training_resume_mismatch", "GPU restoration binds full records identity");
            ExpectCode(() => IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four with { Seed = 18 }, Paths("changed-seed"), resume, cancellationToken: token), "training_resume_mismatch", "GPU restoration binds optimizer seed");
            ExpectCode(() => IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four, Paths("bad-head"), resume with { Head = fullPaths.Head }, cancellationToken: token), "head_asset_hash_invalid", "GPU restoration verifies explicit prior head SHA");
            ExpectCode(() => IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four, firstPaths, cancellationToken: token), "training_output_exists", "GPU workflow preserves existing three-file outputs");
            ExpectCode(() => IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four, Paths("colliding-input") with { Report = firstPaths.Head }, resume, cancellationToken: token), "training_output_invalid", "GPU workflow outputs cannot overwrite a restored head input");
            var changedExecution = first.Report with { Execution = first.Report.Execution with { Device = first.Report.Execution.Device with { DriverVersion = first.Report.Execution.Device.DriverVersion + 1 } } };
            var changedPath = Path.Combine(directory, "changed-execution.json"); var changedBytes = changedExecution.SerializeBounded(token); File.WriteAllBytes(changedPath, changedBytes);
            ExpectCode(() => IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four, Paths("changed-driver"), resume with { Report = changedPath, ReportSha256 = Convert.ToHexString(SHA256.HashData(changedBytes)) }, cancellationToken: token), "training_resume_backend_mismatch", "GPU restoration compares saved execution against actual current Driver identity");
            var cpuPath = Path.Combine(directory, "cpu-v2.json"); var cpuBytes = Encoding.UTF8.GetBytes(ExtractTraining(firstPaths.Report)); File.WriteAllBytes(cpuPath, cpuBytes);
            ExpectCode(() => IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four, Paths("old-v2"), resume with { Report = cpuPath, ReportSha256 = Convert.ToHexString(SHA256.HashData(cpuBytes)) }, cancellationToken: token), "training_gpu_report_invalid", "GPU workflow refuses old CPU report without execution identity");
            check(device.GetTelemetry().KernelLaunchCount == before, "All resume/output rejection paths launch zero training kernels");
            ExpectCode(() => first.Report.BindApprovedIdentity(identity, token), "training_resume_backend_mismatch", "Formal restoration rejects GPU engineering fixture provenance");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); var cancelledPaths = Paths("pre-cancel");
                try { _ = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, options, cancelledPaths, cancellationToken: cancelled.Token); throw new InvalidOperationException("Pre-cancelled workflow returned state."); }
                catch (OperationCanceledException) { check(Absent(cancelledPaths), "Pre-cancelled GPU workflow publishes no artifacts"); }
            }
            using (var cancelAfterStep = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var cancelledPaths = Paths("after-step");
                try { _ = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, four, cancelledPaths, resume,
                    progress: item => { if (item.CompletedSteps == 3) cancelAfterStep.Cancel(); }, cancellationToken: cancelAfterStep.Token); throw new InvalidOperationException("Cancelled workflow returned state."); }
                catch (OperationCanceledException) { check(Absent(cancelledPaths) && GpuTrainingReport.ReadBounded(firstPaths.Report, first.ReportSha256, token).Training.CompletedSteps == 2, "Cancellation after a committed GPU update leaves prior resume and output paths intact"); }
            }
            var overflow = train with { Examples = [Example("train", MarkerQuestionType.Choice, [[float.MaxValue / 2, 0f], [0f, 0f]], 0)] };
            var overflowPaths = Paths("overflow");
            ExpectCode(() => IndependentCudaTrainingWorkflow.RunFixture(device, overflow, development, recordsHash, options, overflowPaths, cancellationToken: token), "training_value_invalid", "GPU workflow finite failure rejects the second FP32 update");
            check(Absent(overflowPaths), "Finite failure publishes no complete or partial outputs");
            var collisionPaths = Paths("publish-race");
            try
            {
                _ = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, recordsHash, options, collisionPaths,
                    progress: item => { if (item.CompletedSteps == 2) Directory.CreateDirectory(collisionPaths.Report); }, cancellationToken: token);
                throw new InvalidOperationException("Publication collision unexpectedly succeeded.");
            }
            catch (IOException) { check(!File.Exists(collisionPaths.Checkpoint) && !File.Exists(collisionPaths.Head) && Directory.Exists(collisionPaths.Report), "Report-last publication failure rolls back only owned checkpoint/head and preserves the competing path"); }
            Clean("GPU workflow cancel, finite and publication failures restore caller-owned resources");
            Guard();
        }
        finally
        {
            var ownedRoot = Path.GetFullPath(directory);
            if (!ownedRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()) + "sezika-cuda-workflow-", StringComparison.Ordinal)) throw new InvalidOperationException("Test cleanup escaped its unique task directory.");
            // This GUID directory and every child was created exclusively by this bounded fixture; no user inputs are stored here.
            Directory.Delete(ownedRoot, recursive: true);
        }
    }

    private static MarkerFeatureExample Example(string id, MarkerQuestionType type, float[][] features, int target) => new(id, type, "en", [2, 4, 4, 1], [1, 2], features, target);
    private static string RewriteHeadMetadata(string source, string destination, int field)
    {
        var bytes = File.ReadAllBytes(source);
        using (var stream = new MemoryStream(bytes, writable: true))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            void SkipText()
            {
                var length = reader.ReadInt32();
                if (length is < 0 or > 4096 || stream.Position + length > bytes.Length - 32) throw new InvalidDataException("Fixture text extent is invalid.");
                stream.Position += length;
            }
            stream.Position = 5; _ = reader.ReadInt32();
            SkipText(); SkipText(); SkipText(); SkipText(); _ = reader.ReadInt32();
            SkipText(); SkipText(); SkipText();
            var seedOffset = stream.Position; var seed = reader.ReadInt32();
            var rateOffset = stream.Position; var rate = reader.ReadSingle();
            var stepsOffset = stream.Position; var steps = reader.ReadInt32();
            var lossOffset = stream.Position; var loss = reader.ReadDouble();
            switch (field)
            {
                case 0: stream.Position = seedOffset; writer.Write(seed + 1); break;
                case 1: stream.Position = rateOffset; writer.Write(rate * 2); break;
                case 2: stream.Position = stepsOffset; writer.Write(steps + 1); break;
                case 3: stream.Position = lossOffset; writer.Write(loss + 0.25); break;
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }
        }
        SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
        File.WriteAllBytes(destination, bytes);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    private static string ExtractTraining(string path) { using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path)); return document.RootElement.GetProperty("training").GetRawText(); }
}
