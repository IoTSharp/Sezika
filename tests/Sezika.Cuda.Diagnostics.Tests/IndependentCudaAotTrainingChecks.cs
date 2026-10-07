using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.IndependentModelTool;

namespace Sezika.Cuda.Diagnostics.Tests;

internal sealed record AotRuntimeIdentity(bool DynamicCodeSupported, bool DynamicCodeCompiled, bool TpaIsNull,
    string? ProcessPath, string? ProcessSha256, string RuntimeIdentifier)
{
    internal static AotRuntimeIdentity Read()
    {
        var path = Environment.ProcessPath;
        string? hash = null;
        if (path is not null)
        {
            var clock = Stopwatch.StartNew();
            var info = new FileInfo(path);
            if (info.Length is < 1 or > 134_217_728 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Runtime binary identity size/link bound.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            hash = Convert.ToHexString(SHA256.HashData(stream));
            if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Runtime binary identity deadline.");
        }
        return new(RuntimeFeature.IsDynamicCodeSupported, RuntimeFeature.IsDynamicCodeCompiled,
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is null, path, hash, RuntimeInformation.RuntimeIdentifier);
    }

    public bool AotVerified => !DynamicCodeSupported && !DynamicCodeCompiled && TpaIsNull;
}

internal static class IndependentCudaAotTrainingChecks
{
    private const string RecordsSha256 = "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";
    private static MarkerFeatureIdentity Identity => new("sezika/native-training-h4-synthetic-fixture",
        new string('A', 64), new string('B', 64), new string('C', 64), 4);

    private static MarkerFeatureSet Fixture(bool tiny) => new(Identity, tiny
        ? [Example("train-choice", MarkerQuestionType.Choice, [[0.3f, -0.2f, 0.1f, 0.4f], [-0.1f, 0.4f, 0.2f, -0.3f]], 1)]
        : [Example("train-choice", MarkerQuestionType.Choice, [[0.3f, -0.2f, 0.1f, 0.4f], [-0.1f, 0.4f, 0.2f, -0.3f]], 1),
           Example("train-score", MarkerQuestionType.Score, [[0.3f, 0.1f, -0.2f, 0.4f], [-0.2f, 0.4f, 0.3f, -0.1f], [0.1f, -0.3f, 0.2f, -0.4f]], 2),
           Example("train-boolean", MarkerQuestionType.Boolean, [[-0.3f, 0.2f, 0.4f, -0.1f], [0.2f, -0.1f, -0.3f, 0.4f]], 0)]);

    private static MarkerFeatureExample Example(string id, MarkerQuestionType type, float[][] features, int target) =>
        new(id, type, "en", [2, 3, 4, 5, 6, 1], features.Length == 3 ? [1, 2, 4] : [1, 4], features, target);

    private static MarkerFeatureExample[] Development() =>
        [Example("development-boolean", MarkerQuestionType.Boolean, [[0.2f, -0.1f, 0.3f, -0.4f], [-0.1f, 0.2f, -0.4f, 0.3f]], 1)];

    internal static void Prepare(Action<bool, string> check, CancellationToken cancellationToken, bool tiny)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var clock = Stopwatch.StartNew();
        var maximum = tiny ? 1 : 3;
        for (var index = 0; index < maximum && index < 3; index++)
        {
            Guard(clock, TimeSpan.FromSeconds(5), deadline.Token);
            if (index == 0)
            {
                var set = Fixture(false);
                check(set.Identity.HiddenSize == 4 && set.Examples.Count == 3 && Development()[0].RecordId != set.Examples[0].RecordId &&
                    set.Examples[0].Type == MarkerQuestionType.Choice && set.Examples[1].Type == MarkerQuestionType.Score &&
                    set.Examples[2].Type == MarkerQuestionType.Boolean && set.Examples.Sum(item => item.Features.Length * 4) == 28,
                    "AOT H4 synthetic fixture has three distinct types and 28 feature floats; no CPU training");
            }
            else if (index == 1)
            {
                var identity = CudaTrainingExecutionIdentity.Read(deadline.Token);
                check(identity.Kernels.Count == 17 && identity.Kernels[14].Name == "MarkerCrossEntropy" &&
                    identity.Kernels[15].Name == "MarkerGradientSgd" && identity.Kernels[16].Name == "MarkerUpdateStatus",
                    "AOT preparation binds seventeen static PTX artifacts without opening CUDA");
            }
            else
            {
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                var rejected = false;
                try { _ = GpuTrainingReport.ParseBounded("{}"u8, cancelled.Token); }
                catch (OperationCanceledException) { rejected = true; }
                check(rejected, "AOT schema3 report pre-cancellation works before CUDA and JSON loading");
            }
            Console.WriteLine($"AOT preparation progress: cases={index + 1}/{maximum}; actual_gpu_training=false; encoder_forward=false; diagnostic_only=true");
        }
        Console.WriteLine(JsonSerializer.Serialize(new AotPreparationEvidence(maximum, AotRuntimeIdentity.Read(), false, false, false),
            AotTrainingJson.Default.AotPreparationEvidence));
    }

    internal static void Run(string outputDirectory, bool tiny, bool requireNative,
        Action<bool, string> check, CancellationToken cancellationToken)
    {
        var runtime = AotRuntimeIdentity.Read();
        // Emit observed flags even when managed invocation must fail. No CUDA has been opened here.
        Console.WriteLine(JsonSerializer.Serialize(runtime, AotTrainingJson.Default.AotRuntimeIdentity));
        if (requireNative) check(runtime.AotVerified, "GPU training executes with dynamic code disabled and TPA null before any CUDA Open");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var limit = TimeSpan.FromSeconds(tiny ? 30 : 100);
        deadline.CancelAfter(limit);
        var clock = Stopwatch.StartNew();
        Guard(clock, limit, deadline.Token);
        var root = Path.GetFullPath(outputDirectory);
        var parent = Path.GetDirectoryName(root) ?? throw new ArgumentException("A named output directory is required.");
        if (!Directory.Exists(parent) || Directory.Exists(root) || File.Exists(root))
            throw new IOException("Diagnostic output requires an existing parent and a new named directory.");
        // At most sixteen explicit ancestors; never follow links or discover source directories.
        DirectoryInfo? ancestor = new(parent);
        for (var depth = 0; depth < 16 && ancestor is not null; depth++)
        {
            Guard(clock, limit, deadline.Token);
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked diagnostic output parent is rejected.");
            ancestor = ancestor.Parent;
        }
        if (ancestor is not null) throw new IOException("Diagnostic output ancestor limit exceeded.");
        Directory.CreateDirectory(root); // Retain successful and failed evidence; no TEMP or input deletion.
        RunWorkflow(root, tiny, runtime, check, deadline.Token);
        Console.WriteLine($"GPU AOT training progress: cases=1/{(tiny ? 1 : 11)}; updates={(tiny ? 1 : 12)}; diagnostic_only=true");
        if (tiny) return;
        Guard(clock, limit, deadline.Token);
        RunPreCancelled(root, runtime, check, deadline.Token);
        Console.WriteLine("GPU AOT training progress: cases=2/11; pre_cancelled=true; updates=0");
        IndependentCudaCleanupChecks.RunAotSlice(check, deadline.Token);
        Guard(clock, limit, deadline.Token);
        IndependentCudaAdoptionChecks.RunAotSlice(check, deadline.Token);
        Guard(clock, limit, deadline.Token);
        Console.WriteLine($"GPU AOT training complete: cases=11; training_updates=15; aot_verified={runtime.AotVerified}; data_mode=synthetic_fixture; training_admission=false; quality_evidence=false");
    }

    private static void RunWorkflow(string root, bool tiny, AotRuntimeIdentity runtime,
        Action<bool, string> check, CancellationToken token)
    {
        var train = Fixture(tiny);
        var development = Development();
        var options = new MarkerTrainingOptions(17, tiny ? 1 : 3, 0.05f, TimeSpan.FromSeconds(10));
        var device = CudaDevice.Open(enableProfiling: true);
        var calls = new List<AotWorkflowCallEvidence>(3);
        GpuTrainingExecution? execution = null;
        bool? exactWeights = null, exactLoss = null, exactCheckpoint = null, exactHead = null, inputsUnchanged = null;
        string featureHash = string.Empty;
        try
        {
            TrainingOutputPaths Paths(string name) => new(Path.Combine(root, name + ".checkpoint"), Path.Combine(root, name + ".head"), Path.Combine(root, name + ".json"));
            AotWorkflowCallEvidence Capture(string name, int initialSteps, int updates, TrainingOutputPaths paths, CudaTrainingWorkflowResult result)
            {
                token.ThrowIfCancellationRequested();
                var report = GpuTrainingReport.ReadBounded(paths.Report, result.ReportSha256, token);
                var loaded = MarkerTrainingCheckpoint.Load(paths.Checkpoint, train, report.Training.CheckpointSha256, token);
                var head = IndependentMarkerHeadAssetStore.Load(paths.Head, train.Identity, loaded.FeatureSha256, report.Training.HeadSha256, token);
                var telemetry = device.GetTelemetry();
                var memory = device.GetMemorySnapshot();
                // From frozen trainer source: four launches/update, two status floats/update, 12 final FP32 weights.
                var expectedH2D = (ulong)(train.Examples.Sum(item => item.Features.Length * 4) * sizeof(float) + 12 * sizeof(float));
                var expectedD2H = (ulong)(updates * 2 * sizeof(float) + 12 * sizeof(float));
                check(telemetry.ProfilingEnabled && telemetry.KernelLaunchCount == updates * 4 &&
                    telemetry.HostToDeviceBytes == expectedH2D && telemetry.DeviceToHostBytes == expectedD2H,
                    name + ": actual GPU linear/CE/gradient/SGD/status launches and readback match frozen counts");
                check(memory.OwnedBytes == 0 && memory.OwnedAllocationCount == 0 && memory.LoadedModuleCount == 0 &&
                    memory.ReleaseFailureCount == 0 && memory.PeakOwnedBytes == (tiny ? 700UL : 780UL) && memory.PeakOwnedBytes <= 1_048_576,
                    name + ": own buffers/modules close with the exact tiny/full working-buffer peak below one MiB");
                check(report.SchemaVersion == 3 && report.Execution.DataMode == "synthetic_fixture" &&
                    !report.Training.UsesRealIndependentEncoder && !report.Training.RepresentsQualityAcceptance &&
                    report.Training.Status == "fixture_contract_only" && report.Training.InitialSteps == initialSteps &&
                    report.Training.CompletedSteps == initialSteps + updates && report.Training.TrainCount == train.Examples.Count &&
                    report.Training.DevelopmentCount == 1 && report.Execution.Device.Uuid == device.Info.Uuid &&
                    report.Execution.Device.DriverVersion == device.Info.DriverVersion && report.Execution.Kernels.Length == 17,
                    name + ": real loaders preserve truthful schema3 fixture and actual UUID/Driver/PTX identity");
                check(MemoryMarshal.AsBytes(loaded.Head.CopyWeights().AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(head.Head.CopyWeights().AsSpan())) &&
                    loaded.Head.CopyWeights().SequenceEqual(result.State.Head.CopyWeights()) && double.IsFinite(loaded.LastLoss) && loaded.LastLoss >= 0,
                    name + ": saved checkpoint/head loaders bind all twelve actual finite GPU weights and loss");
                var weights = loaded.Head.CopyWeights();
                for (var row = 0; row < (tiny ? 1 : 3) && row < 3; row++)
                {
                    token.ThrowIfCancellationRequested();
                    check(weights.AsSpan(row * 4, 4).ContainsAnyExcept(0f), name + ": selected head row has a real nonzero GPU update");
                }
                execution ??= report.Execution;
                featureHash = loaded.FeatureSha256;
                return new(name, initialSteps, updates, loaded.CompletedSteps, loaded.LastLoss, weights, telemetry,
                    memory.PeakOwnedBytes, FileIdentity(paths.Checkpoint), FileIdentity(paths.Head), FileIdentity(paths.Report),
                    report.Training.Resume?.CheckpointSha256, report.Training.Resume?.ReportSha256);
            }
            device.ResetTelemetry();
            var firstPaths = Paths("first");
            var first = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, RecordsSha256, options, firstPaths, cancellationToken: token);
            calls.Add(Capture("first", 0, options.MaxSteps, firstPaths, first));
            if (!tiny)
            {
                var before = new[] { FileIdentity(firstPaths.Checkpoint), FileIdentity(firstPaths.Head), FileIdentity(firstPaths.Report) };
                var resume = new CudaTrainingResumePaths(firstPaths.Checkpoint, first.Report.Training.CheckpointSha256,
                    firstPaths.Head, firstPaths.Report, first.ReportSha256);
                var six = options with { MaxSteps = 6 };
                var resumedPaths = Paths("resumed");
                device.ResetTelemetry();
                var resumed = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, RecordsSha256, six,
                    resumedPaths, resume, cancellationToken: token);
                calls.Add(Capture("resumed", 3, 3, resumedPaths, resumed));
                var continuousPaths = Paths("continuous");
                device.ResetTelemetry();
                var continuous = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, RecordsSha256, six,
                    continuousPaths, cancellationToken: token);
                calls.Add(Capture("continuous", 0, 6, continuousPaths, continuous));
                exactWeights = MemoryMarshal.AsBytes(resumed.State.Head.CopyWeights().AsSpan()).SequenceEqual(
                    MemoryMarshal.AsBytes(continuous.State.Head.CopyWeights().AsSpan()));
                exactLoss = resumed.State.LastLoss == continuous.State.LastLoss;
                exactCheckpoint = calls[1].Checkpoint.Sha256 == calls[2].Checkpoint.Sha256;
                exactHead = calls[1].Head.Sha256 == calls[2].Head.Sha256;
                var after = new[] { FileIdentity(firstPaths.Checkpoint), FileIdentity(firstPaths.Head), FileIdentity(firstPaths.Report) };
                inputsUnchanged = before.SequenceEqual(after);
                check(exactWeights == true && exactLoss == true && exactCheckpoint == true && exactHead == true && inputsUnchanged == true &&
                    resumed.Report.Training.Resume?.ReportSha256 == first.ReportSha256 && resumed.Report.Training.Resume?.InitialSteps == 3,
                    "GPU schema3 saved three-file 3→6 restoration equals continuous six; prior input bytes remain unchanged");
            }
        }
        finally { device.Dispose(); }
        check(device.ContextDestroySucceeded && device.OwnedResources.Bytes == 0 && device.OwnedResources.Buffers == 0 &&
            device.OwnedResources.Modules == 0 && device.AdoptionSnapshot.OwnedEvents == 0,
            "GPU training context actually destroyed with no final owned resources");
        var evidence = new AotWorkflowEvidence("saved_resume_workflow", runtime, "synthetic_fixture", true, false, false, false,
            tiny, !tiny, train.Identity.HiddenSize, train.Examples.Count, 1, 17, 0.05f, execution!, featureHash, calls.ToArray(),
            exactWeights, exactLoss, exactCheckpoint, exactHead, inputsUnchanged, device.ContextDestroySucceeded,
            calls.Sum(item => item.Updates), calls.Sum(item => item.Telemetry.KernelLaunchCount),
            calls.Aggregate(0UL, (sum, item) => sum + item.Telemetry.DeviceToHostBytes));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(evidence, AotTrainingJson.Default.AotWorkflowEvidence);
        _ = JsonSerializer.Deserialize(bytes, AotTrainingJson.Default.AotWorkflowEvidence) ?? throw new InvalidDataException("Sourcegen evidence roundtrip failed.");
        var evidencePath = Path.Combine(root, "workflow-evidence.json");
        using (var stream = new FileStream(evidencePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
    }

    private static void RunPreCancelled(string root, AotRuntimeIdentity runtime, Action<bool, string> check, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var device = CudaDevice.Open(enableProfiling: true);
        var rejected = false;
        var paths = new TrainingOutputPaths(Path.Combine(root, "pre-cancel.checkpoint"), Path.Combine(root, "pre-cancel.head"), Path.Combine(root, "pre-cancel.json"));
        CudaTelemetrySnapshot telemetry;
        try
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            device.ResetTelemetry();
            try { _ = IndependentCudaTrainingWorkflow.RunFixture(device, Fixture(false), Development(), RecordsSha256,
                new(17, 3, 0.05f, TimeSpan.FromSeconds(10)), paths, cancellationToken: cancelled.Token); }
            catch (OperationCanceledException) { rejected = true; }
            telemetry = device.GetTelemetry();
            check(rejected && telemetry.KernelLaunchCount == 0 && telemetry.HostToDeviceBytes == 0 && telemetry.DeviceToHostBytes == 0 &&
                device.OwnedResources.Bytes == 0 && device.OwnedResources.Buffers == 0 && device.OwnedResources.Modules == 0 &&
                !File.Exists(paths.Checkpoint) && !File.Exists(paths.Head) && !File.Exists(paths.Report),
                "Pre-cancelled GPU workflow creates no training work or publication artifacts");
        }
        finally { device.Dispose(); }
        check(device.ContextDestroySucceeded, "Pre-cancelled workflow caller context actually closes");
        Console.WriteLine(JsonSerializer.Serialize(new AotPreCancelledEvidence("pre_cancelled_workflow", runtime,
            device.Info.Uuid, rejected, telemetry, device.ContextDestroySucceeded, false, false, false),
            AotTrainingJson.Default.AotPreCancelledEvidence));
    }

    private static AotFileIdentity FileIdentity(string path)
    {
        var info = new FileInfo(path);
        if (info.Length is < 1 or > 1_048_576 || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Evidence asset size/link limit.");
        return new(Path.GetFullPath(path), info.Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    private static void Guard(Stopwatch clock, TimeSpan limit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (clock.Elapsed >= limit) throw new TimeoutException("GPU AOT diagnostic deadline.");
    }
}

internal sealed record AotPreparationEvidence(int Cases, AotRuntimeIdentity Runtime, bool ActualGpuTraining, bool EncoderForward, bool TrainingAdmission);
internal sealed record AotFileIdentity(string Path, long Bytes, string Sha256);
internal sealed record AotWorkflowCallEvidence(string Name, int InitialSteps, int Updates, int CompletedSteps, double LastLoss,
    float[] Weights, CudaTelemetrySnapshot Telemetry, ulong PeakOwnedBytes, AotFileIdentity Checkpoint, AotFileIdentity Head,
    AotFileIdentity Report, string? ResumedCheckpointSha256, string? ResumedReportSha256);
internal sealed record AotWorkflowEvidence(string Case, AotRuntimeIdentity Runtime, string DataMode, bool SyntheticFixture,
    bool TrainingAdmission, bool QualityEvidence, bool UsesRealIndependentEncoder, bool Tiny, bool ResumeComparisonPerformed, int HiddenSize,
    int TrainCount, int DevelopmentCount, int Seed, float LearningRate, GpuTrainingExecution Execution, string FeatureSha256,
    AotWorkflowCallEvidence[] Calls, bool? ExactResumedWeightBytes, bool? ExactResumedLoss, bool? ExactCheckpointHash,
    bool? ExactHeadHash, bool? ResumeInputsUnchanged, bool ContextDestroySucceeded, int TrainingUpdates,
    long KernelLaunchCount, ulong DeviceToHostBytes);
internal sealed record AotPreCancelledEvidence(string Case, AotRuntimeIdentity Runtime, string DeviceUuid,
    bool CancellationRejected, CudaTelemetrySnapshot Telemetry, bool ContextDestroySucceeded,
    bool ActualGpuTraining, bool TrainingAdmission, bool QualityEvidence);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(AotRuntimeIdentity))]
[JsonSerializable(typeof(AotPreparationEvidence))]
[JsonSerializable(typeof(AotWorkflowEvidence))]
[JsonSerializable(typeof(AotPreCancelledEvidence))]
internal partial class AotTrainingJson : JsonSerializerContext;
