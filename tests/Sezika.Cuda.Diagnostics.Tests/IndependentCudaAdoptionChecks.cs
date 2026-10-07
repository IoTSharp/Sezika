using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.Cuda.Diagnostics.Tests;

internal static class IndependentCudaAdoptionChecks
{
    internal static void RunCpu(Action<bool, string> check, CancellationToken token, bool tiny)
    {
        var clock = Stopwatch.StartNew();
        var maximum = tiny ? 1 : 7;
        for (var index = 0; index < maximum && index < 7; index++)
        {
            CheckBudget(clock, TimeSpan.FromSeconds(5), token);
            var name = $"adoption CPU contract {index + 1}/{maximum}";
            switch (tiny ? 0 : index + 1)
            {
                case 0:
                    var defaults = new ResourceAdoptionFaults();
                    check(!defaults.FaultConsumed && !defaults.BufferFailureConsumed &&
                        !defaults.ModuleFailureConsumed && !defaults.EventFailureConsumed &&
                        ResourceAdoption.CheckedOwnedBytes(0, (nuint)sizeof(float)) == sizeof(float),
                        name + ": default fault scope is inert and byte accounting is exact");
                    break;
                case 1:
                    Reject(() => _ = new ResourceAdoptionFaults((ResourceAdoptionPoint)int.MaxValue), check,
                        name + ": unknown fault point rejected");
                    break;
                case 2:
                    Reject(() => _ = new ResourceAdoptionFaults(ResourceAdoptionPoint.BufferAdopted), check,
                        name + ": fault point requires a prebuilt exception");
                    break;
                case 3:
                    Reject(() => _ = new ResourceAdoptionFaults(failure: new OutOfMemoryException("unused adoption fault")), check,
                        name + ": exception without a fault point rejected");
                    Reject(() => _ = new ResourceAdoptionFaults(ResourceAdoptionPoint.ReleaseDiagnostic,
                        new InvalidOperationException("not an allocation fault")), check,
                        name + ": release diagnostic injection accepts only a prebuilt OOM");
                    break;
                case 4:
                    Reject(() => _ = new ResourceAdoptionFaults(bufferResult: 0), check,
                        name + ": successful buffer CUresult cannot be injected as failure");
                    break;
                case 5:
                    Reject(() => _ = new ResourceAdoptionFaults(moduleResult: 0), check,
                        name + ": successful module CUresult cannot be injected as failure");
                    break;
                case 6:
                    Reject(() => _ = new ResourceAdoptionFaults(eventResult: 0), check,
                        name + ": successful event CUresult cannot be injected as failure");
                    break;
                case 7:
                    var overflowRejected = false;
                    try { _ = ResourceAdoption.CheckedOwnedBytes(ulong.MaxValue, (nuint)1); }
                    catch (OverflowException) { overflowRejected = true; }
                    check(overflowRejected &&
                        ResourceAdoption.CheckedOwnedBytes(ulong.MaxValue - sizeof(float), (nuint)sizeof(float)) == ulong.MaxValue,
                        name + ": byte accounting rejects overflow and accepts exact upper boundary");
                    break;
            }
            CheckBudget(clock, TimeSpan.FromSeconds(5), token);
            Console.WriteLine($"Adoption CPU progress: cases={index + 1}/{maximum}; gpu=false; encoder_forward=false");
            Pause(token);
        }
        Console.WriteLine($"Adoption CPU contracts complete: cases={maximum}; gpu=false; actual_gpu_training=false; aot_verified=false");
    }

    internal static void RunGpu(Action<bool, string> check, CancellationToken token, bool tiny)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var clock = Stopwatch.StartNew();
        var maximum = tiny ? 1 : 11;
        for (var index = 0; index < maximum && index < 11; index++)
        {
            CheckBudget(clock, TimeSpan.FromSeconds(60), deadline.Token);
            RunCase(tiny ? 0 : index + 1, check, deadline.Token);
            CheckBudget(clock, TimeSpan.FromSeconds(60), deadline.Token);
            Console.WriteLine($"Adoption GPU progress: cases={index + 1}/{maximum}; prebuilt_faults=true; actual_gpu_training=false; aot_verified=false");
            Pause(deadline.Token);
        }
    }

    private static void RunCase(int index, Action<bool, string> check, CancellationToken token)
    {
        string[] names = ["buffer_pending", "buffer_adopted", "buffer_adopted_primary_cancellation", "module_pending",
            "module_adopted", "context_pending", "context_adopted", "timing_events_pending", "start_event_adopted",
            "end_event_adopted", "buffer_false_release_diagnostic_oom", "module_and_event_false_release"];
        var name = names[index];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var clock = Stopwatch.StartNew();
        void Assert(bool condition, string detail)
        {
            CheckBudget(clock, TimeSpan.FromSeconds(10), deadline.Token);
            check(condition, name + ": " + detail);
        }
        Exception sentinel = index == 2
            ? new OperationCanceledException("adoption-primary-" + name, null, cancelled.Token)
            : new OutOfMemoryException("adoption-primary-" + name);
        // Separate objects preserve the original primary stack when diagnostic construction also fails.
        var diagnosticFailure = new OutOfMemoryException("adoption-release-diagnostic-" + name);
        var point = index switch
        {
            0 => ResourceAdoptionPoint.BufferPending,
            1 or 2 => ResourceAdoptionPoint.BufferAdopted,
            3 => ResourceAdoptionPoint.ModulePending,
            4 => ResourceAdoptionPoint.ModuleAdopted,
            5 => ResourceAdoptionPoint.ContextPending,
            6 => ResourceAdoptionPoint.ContextAdopted,
            7 => ResourceAdoptionPoint.TimingEventsPending,
            8 => ResourceAdoptionPoint.StartEventAdopted,
            9 => ResourceAdoptionPoint.EndEventAdopted,
            10 => ResourceAdoptionPoint.ReleaseDiagnostic,
            _ => ResourceAdoptionPoint.EndEventAdopted,
        };
        var faults = new ResourceAdoptionFaults(point, point == ResourceAdoptionPoint.None ? null :
            index == 10 ? diagnosticFailure : sentinel, bufferResult: index == 10 ? 1 : null,
            moduleResult: index == 11 ? 1 : null, eventResult: index == 11 ? 1 : null);
        CudaDevice? device = null;
        CudaDeviceBuffer? buffer = null;
        CudaVectorAdd? vector = null;
        CudaVectorAdd? secondVector = null;
        Exception? primary = null, childShutdown = null, shutdown = null;
        CudaException? nativeFailure = null;
        var returned = false;
        float[] output = [0];
        ulong beforeContextBytes = 0;
        var beforeContextBuffers = 0;
        var beforeContextModules = 0;
        long beforeContextFailures = 0;
        var pendingBuffers = 0;
        var pendingModules = 0;
        var beforeContextEvents = 0;
        var diagnosticUnavailable = false;
        var lastResult = 0;
        var lastOperation = ResourceReleaseOperation.None;
        CudaTelemetrySnapshot? telemetry = null;
        try
        {
            try
            {
                if (index is 5 or 6)
                    device = CudaDevice.OpenForAdoptionDiagnostics(0, true, faults);
                else
                {
                    device = CudaDevice.Open(enableProfiling: true); // Missing driver/device must fail, never SKIP.
                    if (index == 10)
                    {
                        var priorFaults = new ResourceAdoptionFaults(moduleResult: 2);
                        device.InstallAdoptionFaults(priorFaults);
                        vector = device.CreateVectorAdd();
                        vector.Dispose();
                        var prior = device.AdoptionSnapshot;
                        Assert(priorFaults.ModuleFailureConsumed && device.OwnedResources.Modules == 1 &&
                            device.OwnedResources.ReleaseFailures == 1 && prior.LastReleaseResult == 2 &&
                            prior.LastReleaseOperation == ResourceReleaseOperation.Module && !prior.ReleaseDiagnosticUnavailable,
                            "earlier false module unload establishes a cached historical diagnostic");
                        device.ResetAdoptionFaults();
                    }
                    device.InstallAdoptionFaults(faults);
                    device.ResetTelemetry();
                    if (index is 0 or 1 or 2 or 10)
                    {
                        buffer = device.Allocate(sizeof(float));
                        if (index == 10) ThrowPrimary(sentinel);
                    }
                    else
                    {
                        vector = device.CreateVectorAdd();
                        if (index == 11)
                        {
                            secondVector = device.CreateVectorAdd();
                            vector.Dispose();
                            secondVector.Execute([2f], [3f], output);
                        }
                        else if (index is 7 or 8 or 9)
                            vector.Execute([2f], [3f], output);
                    }
                }
                returned = true;
            }
            catch (CudaException) when (device is null && faults.ObservedDevice is null) { throw; }
            catch (Exception error) { primary = error; }
            device ??= faults.ObservedDevice;
            Assert(device is not null, "real driver device is observable even when Open fails");
            if (index is 0 or 3)
            {
                // A real Driver rejection independently exercises the non-success output branch.
                device!.ResetAdoptionFaults();
                try
                {
                    if (index == 0)
                    {
                        using var invalid = device.Allocate(nuint.MaxValue);
                    }
                    else
                    {
                        using var invalid = device.LoadModule("invalid\0"u8);
                    }
                }
                catch (CudaException error) { nativeFailure = error; }
                var rejectedResources = device.OwnedResources;
                Assert(nativeFailure is { DriverCode: > 0 } && rejectedResources.Bytes == 0 &&
                    rejectedResources.Buffers == 0 && rejectedResources.Modules == 0 &&
                    rejectedResources.ReleaseFailures == 0 && device.GetMemorySnapshot().PeakOwnedBytes == 0 &&
                    device.AdoptionSnapshot.PendingBuffers == 0 && device.AdoptionSnapshot.PendingModules == 0,
                    "real non-success Driver result leaves no adopted owner, pending reservation or false release");
            }
        }
        finally
        {
            device ??= faults.ObservedDevice;
            try { buffer?.Dispose(); }
            catch (Exception error) { childShutdown = error; }
            try { vector?.Dispose(); }
            catch (Exception error) { childShutdown ??= error; }
            try { secondVector?.Dispose(); }
            catch (Exception error) { childShutdown ??= error; }
            if (device is not null)
            {
                var resources = device.OwnedResources;
                beforeContextBytes = resources.Bytes;
                beforeContextBuffers = resources.Buffers;
                beforeContextModules = resources.Modules;
                beforeContextFailures = resources.ReleaseFailures;
                var adoption = device.AdoptionSnapshot;
                pendingBuffers = adoption.PendingBuffers;
                pendingModules = adoption.PendingModules;
                beforeContextEvents = adoption.OwnedEvents;
                diagnosticUnavailable = adoption.ReleaseDiagnosticUnavailable;
                telemetry = device.GetTelemetry();
                try { device.Dispose(); }
                catch (Exception error) { shutdown = error; }
                finally { device.ResetAdoptionFaults(); }
                adoption = device.AdoptionSnapshot;
                lastResult = adoption.LastReleaseResult;
                lastOperation = adoption.LastReleaseOperation;
                diagnosticUnavailable |= adoption.ReleaseDiagnosticUnavailable;
            }
        }
        Assert(device is not null && telemetry is not null, "managed ownership and telemetry evidence available");
        Assert(childShutdown is null, "child cleanup preserves the primary failure");
        Assert(pendingBuffers == 0 && pendingModules == 0 && device!.AdoptionSnapshot.PendingBuffers == 0 &&
            device.AdoptionSnapshot.PendingModules == 0, "pending reservations are removed before and after context cleanup");
        var primaryPreserved = ReferenceEquals(primary, sentinel);
        var throwSitePreserved = primary?.StackTrace?.Contains(index == 10 ? "ThrowPrimary" :
            "ResourceAdoptionFaults", StringComparison.Ordinal) == true;
        if (index == 11)
        {
            Assert(!returned && primaryPreserved && throwSitePreserved && output[0] == 0,
                "event adoption failure preserves the exact primary before any vector output");
            Assert(faults.FaultConsumed && faults.ModuleFailureConsumed && faults.EventFailureConsumed,
                "module and event failures are each consumed once");
            Assert(beforeContextBytes == 0 && beforeContextBuffers == 0 && beforeContextModules == 1 &&
                beforeContextEvents == 1 && beforeContextFailures == 2,
                "closed false-release module and event remain tracked before context cleanup");
            Assert(telemetry!.KernelLaunchCount == 0, "failed timing-event adoption launches no vector kernel");
        }
        else
        {
            Assert(!returned && primaryPreserved && throwSitePreserved,
                "exact prebuilt primary object and original throw-site stack preserved");
            Assert(faults.FaultConsumed, "configured fault is consumed once");
            if (index == 2)
                Assert(primary is OperationCanceledException cancellation && cancellation.CancellationToken == cancelled.Token,
                    "primary cancellation identity and token remain unchanged");
            if (index == 10)
                Assert(faults.BufferFailureConsumed && beforeContextBytes == sizeof(float) && beforeContextBuffers == 1 &&
                    beforeContextModules == 1 && beforeContextFailures == 2 && diagnosticUnavailable,
                    "false buffer release retains ownership even when optional diagnostic allocation fails");
            else
                Assert(beforeContextBytes == 0 && beforeContextBuffers == 0 && beforeContextModules == 0 &&
                    beforeContextEvents == 0 && beforeContextFailures == 0,
                    "failed native adoption restores zero caller-owned resources");
            Assert(telemetry!.KernelLaunchCount == 0, "failed adoption launches no kernels");
        }
        var after = device!.OwnedResources;
        Assert(after.Bytes == 0 && after.Buffers == 0 && after.Modules == 0 && device.AdoptionSnapshot.OwnedEvents == 0,
            "final context cleanup clears buffers, modules and timing events");
        Assert(index == 5 ? !device.ContextDestroySucceeded : device.ContextDestroySucceeded,
            index == 5 ? "no native context is destroyed for a pre-creation fault" : "native context destruction actually succeeds");
        if (index is 10 or 11)
        {
            Assert(shutdown is CudaException { Code: "cuda_resource_release_failed", DriverCode: 1 } &&
                after.ReleaseFailures == 2 && lastResult == 1 &&
                lastOperation == (index == 10 ? ResourceReleaseOperation.Buffer : ResourceReleaseOperation.Event),
                "primitive failure evidence survives successful context destruction");
        }
        else Assert(shutdown is null && after.ReleaseFailures == 0, "context cleanup has no hidden release failure");
        Console.WriteLine(JsonSerializer.Serialize(new AdoptionCaseEvidence(name, index == 5 ? null : device.Info.Uuid,
            point.ToString(), primary?.GetType().Name, primaryPreserved, throwSitePreserved, returned,
            faults.FaultConsumed, faults.BufferFailureConsumed, faults.ModuleFailureConsumed, faults.EventFailureConsumed,
            beforeContextBytes, beforeContextBuffers, beforeContextModules, beforeContextEvents, beforeContextFailures,
            pendingBuffers, pendingModules, lastResult, lastOperation.ToString(), diagnosticUnavailable,
            device.ContextDestroySucceeded, shutdown is CudaException cuda ? cuda.DriverCode : null,
            after.Bytes, after.Buffers, after.Modules, device.AdoptionSnapshot.OwnedEvents, after.ReleaseFailures,
            telemetry!.KernelLaunchCount, telemetry.HostToDeviceBytes, telemetry.DeviceToHostBytes, nativeFailure?.DriverCode,
            false, false, false, "prebuilt_exception_injection"), AdoptionJson.Default.AdoptionCaseEvidence));
        CheckBudget(clock, TimeSpan.FromSeconds(10), deadline.Token);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowPrimary(Exception failure) => throw failure;

    private static void Reject(Action action, Action<bool, string> check, string name)
    {
        var rejected = false;
        try { action(); }
        catch (ArgumentException) { rejected = true; }
        check(rejected, name);
    }

    private static void CheckBudget(Stopwatch clock, TimeSpan limit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (clock.Elapsed >= limit) throw new TimeoutException("Adoption diagnostic wall-clock limit.");
    }

    private static void Pause(CancellationToken token)
    {
        if (token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(5))) token.ThrowIfCancellationRequested();
    }
}

internal sealed record AdoptionCaseEvidence(string Case, string? DeviceUuid, string FaultPoint, string? PrimaryType,
    bool PrimarySentinelPreserved, bool PrimaryThrowSitePreserved, bool OperationReturned,
    bool FaultConsumed, bool BufferFailureConsumed, bool ModuleFailureConsumed, bool EventFailureConsumed,
    ulong BeforeContextBytes, int BeforeContextBuffers, int BeforeContextModules, int BeforeContextEvents,
    long BeforeContextReleaseFailures, int PendingBuffers, int PendingModules, int LastReleaseResult,
    string LastReleaseOperation, bool ReleaseDiagnosticUnavailable, bool ContextDestroySucceeded,
    int? ContextDisposeDriverCode, ulong AfterContextBytes, int AfterContextBuffers, int AfterContextModules,
    int AfterContextEvents, long AfterContextReleaseFailures, long KernelLaunchCount, ulong HostToDeviceBytes,
    ulong DeviceToHostBytes, int? NativeFailureDriverCode, bool ActualGpuTraining, bool EncoderForward, bool AotVerified, string FaultMode);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(AdoptionCaseEvidence))]
internal partial class AdoptionJson : JsonSerializerContext;
