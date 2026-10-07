using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.Cuda.Diagnostics.Tests;

internal static class IndependentCudaCleanupChecks
{
    private static MarkerFeatureSet Fixture() => new(new("sezika/cleanup-fixture", new string('A', 64),
        new string('B', 64), new string('C', 64), 3),
        [new("cleanup", MarkerQuestionType.Choice, "en", [1, 2], [0, 1],
            [[0.3f, -0.2f, 0.1f], [-0.1f, 0.4f, 0.2f]], 1)]);

    internal static void RunCpu(Action<bool, string> check, CancellationToken token, bool tiny)
    {
        var clock = Stopwatch.StartNew();
        var maximum = tiny ? 1 : 3;
        for (var index = 0; index < maximum && index < 3; index++)
        {
            token.ThrowIfCancellationRequested();
            if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Cleanup fault contract deadline.");
            bool rejected = false;
            try
            {
                _ = index switch
                {
                    0 => new TrainingCleanupFaults(bufferResult: 0),
                    1 => new TrainingCleanupFaults(moduleResult: 0),
                    _ => new TrainingCleanupFaults(expireAfterOwners: 41),
                };
            }
            catch (ArgumentException) { rejected = true; }
            check(rejected, $"cleanup fault admission {index + 1}/{maximum}: success CUresult or unbounded owner input rejected");
        }
        Console.WriteLine($"Cleanup CPU contracts complete: cases={maximum}; gpu=false; encoder_forward=false");
    }

    internal static void RunGpu(Action<bool, string> check, CancellationToken token, bool tiny)
    {
        var clock = Stopwatch.StartNew();
        var maximum = tiny ? 1 : 8;
        for (var index = 0; index < maximum && index < 8; index++)
        {
            token.ThrowIfCancellationRequested();
            if (clock.Elapsed >= TimeSpan.FromSeconds(60)) throw new TimeoutException("Cleanup GPU checks deadline.");
            RunCase(tiny ? 0 : index + 1, check, token);
            Console.WriteLine($"Cleanup GPU progress: cases={index + 1}/{maximum}; injected_faults=true; aot_verified=false");
        }
    }

    private static void RunCase(int index, Action<bool, string> check, CancellationToken token)
    {
        var names = new[] { "initial_progress_sentinel_caller_baseline", "first_step_sentinel", "first_step_cancellation",
            "packed_before_allocate", "initial_before_order", "injected_mem_free", "injected_module_unload_no_primary",
            "injected_recovery_deadline", "partial_kernel_constructor" };
        var name = names[index];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var device = CudaDevice.Open(enableProfiling: true); // Driver/device absence is a failure.
        CudaDeviceBuffer? callerBuffer = null;
        CudaVectorAdd? callerModule = null;
        var sentinel = new InvalidOperationException("cleanup-sentinel-" + name);
        TrainingCleanupFaults? faults = null;
        Exception? primary = null, shutdown = null;
        MarkerTrainingState? returned = null;
        var before = device.OwnedResources;
        TrainingCleanupResult summary = default;
        CudaTelemetrySnapshot? telemetry = null;
        try
        {
            if (index == 0)
            {
                callerBuffer = device.Allocate(sizeof(float));
                device.CopyToDevice(callerBuffer, new float[] { 7 });
                callerModule = device.CreateVectorAdd();
            }
            before = device.OwnedResources;
            var set = Fixture();
            var options = new MarkerTrainingOptions(17, 2, 0.1f, TimeSpan.FromSeconds(5));
            MarkerTrainingState? resume = null;
            float[]? resumeBytes = null;
            if (index == 4)
            {
                resume = IndependentCudaMarkerHeadTrainer.Train(device, set, options with { MaxSteps = 1 }, cancellationToken: deadline.Token);
                resumeBytes = resume.Head.CopyWeights();
                check(resumeBytes.Any(value => value != 0), name + ": real GPU warmup produced nonzero resume weights");
            }
            faults = index switch
            {
                3 => new(TrainingFaultPoint.PackedBeforeAllocate, sentinel),
                4 => new(TrainingFaultPoint.InitialCreated, sentinel),
                5 => new(bufferResult: 1),
                6 => new(moduleResult: 1),
                7 => new(expireAfterOwners: 1),
                8 => new(TrainingFaultPoint.ModuleLoaded, sentinel, hit: 3),
                _ => new(),
            };
            device.InstallTrainingFaults(faults);
            device.ResetTelemetry();
            void ThrowProgress(MarkerTrainingProgress progress)
            {
                if (index == 2 && progress.CompletedSteps == 1) cancelled.Cancel();
                if ((index is 0 or 5 or 7 && progress.CompletedSteps == 0) ||
                    (index == 1 && progress.CompletedSteps == 1)) throw sentinel;
            }
            try
            {
                returned = IndependentCudaMarkerHeadTrainer.Train(device, set, options,
                    resume, ThrowProgress, index == 2 ? cancelled.Token : deadline.Token);
            }
            catch (Exception error) { primary = error; }
            check(returned is null, name + ": failure returns no successful training state");
            check(device.TrainingFaults is null, name + ": fault scope automatically reset");
            summary = device.LastTrainingCleanup ?? throw new InvalidOperationException("Missing managed cleanup evidence.");
            telemetry = device.GetTelemetry();
            check(summary.AttemptedOwners <= 40 && summary.AttemptedModules <= 17,
                name + ": owner/module limits hold");
            check(summary.Before == before, name + ": cleanup captures caller resource baseline");
            if (index == 2) check(primary is OperationCanceledException, name + ": cancellation remains the training failure");
            else if (index == 6)
                check(primary is DecisionException { Code: "training_cleanup_incomplete" }, name + ": silent native false-release prevents success");
            else
                check(ReferenceEquals(primary, sentinel) && primary!.StackTrace?.Contains("IndependentCudaMarkerHeadTrainer.Train", StringComparison.Ordinal) == true &&
                    primary.StackTrace.Contains(index is 3 or 4 or 8 ? "TrainingCleanupFaults.Reach" : "ThrowProgress", StringComparison.Ordinal),
                    name + ": exact primary object/type/stack retained");
            check(faults.ObservedInitial is { } initial && initial.All(value => value == 0), name + ": initial copy cleared independently");
            if (index is not (4 or 8))
                check(faults.ObservedPacked is { } packed && packed.All(value => value == 0), name + ": packed copy cleared independently");
            if (resume is not null)
                check(resume.Head.CopyWeights().SequenceEqual(resumeBytes!), name + ": input resume weights remain immutable");
            if (index == 5)
                check(faults.BufferFailureConsumed && summary.After.Buffers == before.Buffers + 1 &&
                    summary.After.ReleaseFailures == before.ReleaseFailures + 1 && summary.After.Modules == before.Modules &&
                    summary.AttemptedOwners == 9 && summary.AttemptedModules == 17 && summary.FirstException is null,
                    name + ": injected nonzero MemFree records false, retains one buffer and continues other owners");
            else if (index == 6)
                check(faults.ModuleFailureConsumed && summary.After.Modules == before.Modules + 1 &&
                    summary.After.Buffers == before.Buffers && summary.After.ReleaseFailures == before.ReleaseFailures + 1 &&
                    summary.AttemptedModules == 17 && summary.FirstException is null,
                    name + ": closed SafeHandle with false release retains module ownership");
            else if (index == 7)
                check(summary.DeadlineReached && summary.DeadlineInjected && summary.AttemptedOwners == 1 &&
                    summary.AttemptedModules == 0 && summary.After.Buffers > before.Buffers && summary.After.Modules > before.Modules,
                    name + ": deadline stops new native attempts and reports real residual owners");
            else
                check(!summary.Incomplete && summary.After == before, name + ": recovery restores caller baseline");
            if (index == 8) check(summary.AttemptedOwners == 1 && summary.AttemptedModules == 3, name + ": partial constructor unloads three actual modules");
            if (index is 1 or 2)
                check(telemetry.KernelLaunchCount == 4 && telemetry.DeviceToHostBytes == 8,
                    name + ": first real SGD step has four launches and two-float status readback");
            if (index == 6)
                check(telemetry.KernelLaunchCount == 8 && telemetry.DeviceToHostBytes == 52,
                    name + ": successful training computations precede cleanup rejection");
            if (index == 0)
            {
                float[] retained = new float[1];
                device.CopyFromDevice(retained, callerBuffer!);
                check(retained[0] == 7, name + ": caller buffer contents preserved");
                callerModule!.Execute([2f], [3f], retained);
                check(retained[0] == 5, name + ": caller device/module remain usable");
            }
        }
        finally
        {
            device.ResetTrainingFaults();
            try { callerBuffer?.Dispose(); }
            finally
            {
                try { callerModule?.Dispose(); }
                finally
                {
                    try { device.Dispose(); }
                    catch (Exception error) { shutdown = error; }
                }
            }
        }
        check(device.ContextDestroySucceeded && device.OwnedResources.Bytes == 0 &&
            device.OwnedResources.Buffers == 0 && device.OwnedResources.Modules == 0,
            name + ": caller actually destroyed context and cleared ownership");
        if (index is 5 or 6)
            check(shutdown is CudaException { Code: "cuda_resource_release_failed", DriverCode: 1 } &&
                device.OwnedResources.ReleaseFailures == 1,
                name + ": historical injected release error remains visible after successful context destruction");
        else check(shutdown is null, name + ": caller context shutdown has no release error");
        Console.WriteLine($"Observed training failure ({name}): {primary}");
        if (shutdown is not null) Console.WriteLine($"Observed historical release failure ({name}): {shutdown}");
        Console.WriteLine(JsonSerializer.Serialize(new CleanupCaseEvidence(name, index is 3 or 4 or 5 or 6 or 7 or 8,
            device.Info.Uuid, telemetry!.ProfilingEnabled, telemetry.KernelLaunchCount, telemetry.DeviceToHostBytes,
            before.Bytes, before.Buffers, before.Modules, summary.After.Bytes, summary.After.Buffers, summary.After.Modules,
            summary.After.ReleaseFailures - before.ReleaseFailures, summary.AttemptedOwners, summary.AttemptedModules, summary.OwnerLimitReached,
            summary.DeadlineReached, summary.DeadlineInjected, primary!.GetType().Name,
            primary is DecisionException decision ? decision.Code : null, ReferenceEquals(primary, sentinel),
            device.ContextDestroySucceeded, shutdown is CudaException cuda ? cuda.DriverCode : null,
            shutdown is CudaException release ? release.Operation : null, device.OwnedResources.ReleaseFailures,
            device.OwnedResources.Bytes, device.OwnedResources.Buffers, device.OwnedResources.Modules,
            false, "synthetic_fixture"), CleanupJson.Default.CleanupCaseEvidence));
    }
}

internal sealed record CleanupCaseEvidence(string Case, bool FaultInjected, string DeviceUuid, bool ProfilingEnabled,
    long KernelLaunchCount, ulong DeviceToHostBytes, ulong BeforeBytes, int BeforeBuffers, int BeforeModules,
    ulong AfterTrainingBytes, int AfterTrainingBuffers, int AfterTrainingModules, long ReleaseFailureDelta,
    int AttemptedOwners, int AttemptedModules, bool OwnerLimitReached, bool DeadlineReached, bool DeadlineInjected, string PrimaryType,
    string? PrimaryCode, bool PrimarySentinelPreserved, bool ContextDestroySucceeded, int? ContextDisposeDriverCode,
    string? ContextDisposeOperation, long ReleaseFailuresAfterContext,
    ulong AfterContextBytes, int AfterContextBuffers, int AfterContextModules, bool AotVerified, string DataMode);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(CleanupCaseEvidence))]
internal partial class CleanupJson : JsonSerializerContext;
