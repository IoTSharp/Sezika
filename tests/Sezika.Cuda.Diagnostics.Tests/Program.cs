using Sezika;
using Sezika.Cuda;

// Fixed-size fixtures only (at most 65 vector elements and one tiny encoder layer).
// CUDA Driver calls cannot be interrupted in-flight; the caller should also enforce a process timeout.
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
ConsoleCancelEventHandler cancelHandler = (_, args) => { args.Cancel = true; deadline.Cancel(); };
Console.CancelKeyPress += cancelHandler;
var passed = 0;

void Check(bool condition, string name)
{
    deadline.Token.ThrowIfCancellationRequested();
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}

void Expect<TException>(Action action, string name) where TException : Exception
{
    deadline.Token.ThrowIfCancellationRequested();
    try { action(); }
    catch (TException) { Check(true, name); return; }
    throw new InvalidOperationException($"FAIL: {name}: expected {typeof(TException).Name}.");
}

void CheckNoResources(CudaDevice device, string name)
{
    var memory = device.GetMemorySnapshot();
    // Device-wide free memory also contains driver caches and other clients, so it is not an ownership assertion.
    Check(memory.OwnedBytes == 0 && memory.OwnedAllocationCount == 0 &&
        memory.LoadedModuleCount == 0 && memory.ReleaseFailureCount == 0, name);
}

static bool NoTelemetry(CudaTelemetrySnapshot snapshot) =>
    snapshot.HostToDeviceBytes == 0 && snapshot.DeviceToHostBytes == 0 && snapshot.KernelLaunchCount == 0 &&
    snapshot.HostToDeviceMilliseconds == 0 && snapshot.DeviceToHostMilliseconds == 0 &&
    snapshot.ModuleLoadMilliseconds == 0 && snapshot.KernelMilliseconds == 0;

static bool ValidTime(double milliseconds) => double.IsFinite(milliseconds) && milliseconds >= 0;

try
{
    // Missing driver/device is a failure with its real diagnostic, never a skipped success.
    using var device = CudaDevice.Open(enableProfiling: true);
    Check(!string.IsNullOrWhiteSpace(device.Info.Name) && device.Info.DriverVersion > 0 &&
        device.Info.ComputeCapabilityMajor > 0 && device.Info.TotalMemoryBytes > 0, "CUDA driver/device identity");
    var initial = device.GetMemorySnapshot();
    Check(initial.TotalBytes > 0 && initial.FreeBytes <= initial.TotalBytes, "device-wide memory bounds");
    CheckNoResources(device, "fresh context owns no allocations/modules");

    using (var vector = device.CreateVectorAdd())
    {
        Check(device.GetMemorySnapshot().LoadedModuleCount == 1 &&
            ValidTime(device.GetTelemetry().ModuleLoadMilliseconds), "module load is tracked");
        float[] small = new float[1];
        vector.Execute([2f], [3f], small);
        Check(small[0] == 5f, "single-element vector trial");

        float[] left = Enumerable.Range(0, 65).Select(value => (float)value).ToArray();
        float[] right = Enumerable.Repeat(0.5f, 65).ToArray();
        float[] output = new float[65];
        device.ResetTelemetry();
        Check(NoTelemetry(device.GetTelemetry()) && device.GetMemorySnapshot().PeakOwnedBytes == 0,
            "reset clears telemetry and idle allocation peak");
        vector.Execute(left, right, output);
        Check(output.SequenceEqual(left.Select(value => value + 0.5f)), "non-aligned 65-element vector result");
        var profiled = device.GetTelemetry();
        Check(profiled.ProfilingEnabled && profiled.HostToDeviceBytes == 520 &&
            profiled.DeviceToHostBytes == 260 && profiled.KernelLaunchCount == 1,
            "vector H2D/D2H bytes and kernel count");
        Check(ValidTime(profiled.HostToDeviceMilliseconds) && ValidTime(profiled.DeviceToHostMilliseconds) &&
            ValidTime(profiled.KernelMilliseconds), "profiled timings are finite non-negative durations");
        var vectorMemory = device.GetMemorySnapshot();
        Check(vectorMemory.OwnedBytes == 0 && vectorMemory.OwnedAllocationCount == 0 &&
            vectorMemory.PeakOwnedBytes == 780 && vectorMemory.ReleaseFailureCount == 0,
            "vector working allocations released with exact peak");

        device.ResetTelemetry();
        Expect<ArgumentException>(() => vector.Execute(left, [1f], output), "invalid vector rejected");
        Check(NoTelemetry(device.GetTelemetry()) && device.GetMemorySnapshot().PeakOwnedBytes == 0,
            "invalid vector allocates and launches nothing");
        device.ProfilingEnabled = false;
        vector.Execute(left, right, output);
        Check(output[64] == 64.5f && !device.GetTelemetry().ProfilingEnabled && NoTelemetry(device.GetTelemetry()),
            "unprofiled execution recovers and creates no telemetry");
        Check(device.GetMemorySnapshot().OwnedBytes == 0, "unprofiled execution still tracks cleanup");
    }
    CheckNoResources(device, "vector unload releases its module");

    device.ProfilingEnabled = true;
    using (var gemm = device.CreateGemm())
    {
        float[] result = new float[4];
        device.ResetTelemetry();
        gemm.Execute([1, 2, 3, 4, 5, 6], 2, 3, [7, 8, 9, 10, 11, 12], 2, result);
        Check(result.SequenceEqual(new float[] { 58, 64, 139, 154 }), "GEMM reference result");
        var gemmTelemetry = device.GetTelemetry();
        Check(gemmTelemetry.HostToDeviceBytes == 48 && gemmTelemetry.DeviceToHostBytes == 16 &&
            gemmTelemetry.KernelLaunchCount == 1, "GEMM transfer bytes and kernel count");
        var gemmMemory = device.GetMemorySnapshot();
        Check(gemmMemory.OwnedBytes == 0 && gemmMemory.OwnedAllocationCount == 0 &&
            gemmMemory.PeakOwnedBytes == 64 && gemmMemory.ReleaseFailureCount == 0, "GEMM allocation cleanup and peak");
    }
    CheckNoResources(device, "GEMM unload releases all modules");

    var config = new ModernBertConfig
    {
        VocabularySize = 8, HiddenSize = 8, IntermediateSize = 8, LayerCount = 1, HeadCount = 2,
        MaxTokens = 8, GlobalAttentionEvery = 1, LocalAttention = 8,
    };
    var weights = CreateTinyWeights();
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    device.ResetTelemetry();
    Expect<OperationCanceledException>(() =>
    {
        using var encoder = new CudaModernBertEncoder(device, config, weights, cancelled.Token);
    }, "pre-cancelled encoder constructor");
    CheckNoResources(device, "pre-cancelled constructor leaves no CUDA resources");
    Check(NoTelemetry(device.GetTelemetry()), "pre-cancelled constructor performs no CUDA work");

    using (var encoder = new CudaModernBertEncoder(device, config, weights, deadline.Token))
    {
        var resident = device.GetMemorySnapshot();
        Check(resident.OwnedBytes > 0 && resident.OwnedAllocationCount > 0, "encoder owns resident weights");
        device.ResetTelemetry();
        Check(device.GetMemorySnapshot().PeakOwnedBytes == resident.OwnedBytes,
            "reset preserves the resident allocation baseline");
        Expect<DecisionException>(() => encoder.Encode([0, 8], deadline.Token), "out-of-vocabulary token rejected");
        Check(NoTelemetry(device.GetTelemetry()), "invalid encoder input launches nothing");
        Expect<OperationCanceledException>(() =>
        {
            using var pipeline = new CudaDecisionPipeline(device, encoder, CreateInvalidHead(), cancelled.Token);
        }, "pre-cancelled decision head constructor");
        Expect<DecisionException>(() =>
        {
            using var pipeline = new CudaDecisionPipeline(device, encoder, CreateInvalidHead(), deadline.Token);
        }, "partially uploaded malformed head constructor");
        var afterFailedHead = device.GetMemorySnapshot();
        Check(afterFailedHead.OwnedBytes == resident.OwnedBytes &&
            afterFailedHead.OwnedAllocationCount == resident.OwnedAllocationCount &&
            afterFailedHead.LoadedModuleCount == resident.LoadedModuleCount && afterFailedHead.ReleaseFailureCount == 0,
            "failed head constructor releases partial weights/modules");

        using (var otherDevice = CudaDevice.Open(device.DeviceIndex))
        {
            bool mismatchRejected = false;
            try
            {
                using var pipeline = new CudaDecisionPipeline(otherDevice, encoder, CreateInvalidHead(), deadline.Token);
            }
            catch (CudaException error) when (error.Code == "cuda_context_mismatch")
            {
                mismatchRejected = true;
            }
            Check(mismatchRejected, "cross-context head rejected with cuda_context_mismatch");
            CheckNoResources(otherDevice, "cross-context rejection creates no resources on the second context");
            var afterMismatch = device.GetMemorySnapshot();
            Check(afterMismatch.OwnedBytes == resident.OwnedBytes &&
                afterMismatch.OwnedAllocationCount == resident.OwnedAllocationCount &&
                afterMismatch.LoadedModuleCount == resident.LoadedModuleCount && afterMismatch.ReleaseFailureCount == 0,
                "cross-context rejection preserves the encoder context");
            using (var otherVector = otherDevice.CreateVectorAdd())
            {
                float[] result = new float[1];
                otherVector.Execute([4f], [7f], result);
                Check(result[0] == 11f, "second context remains usable after rejected head");
                float[] recovered = encoder.Encode([0, 1], deadline.Token);
                Check(recovered.Length == 16 && recovered.All(float.IsFinite),
                    "encoder context remains usable while the second context is alive");
                otherVector.Execute([8f], [3f], result);
                Check(result[0] == 11f, "second context rebinds after encoder context execution");
            }
            CheckNoResources(otherDevice, "second context probe releases its resources");
            using var retainedEncoder = new CudaModernBertEncoder(otherDevice, config, weights, deadline.Token);
            Check(otherDevice.GetMemorySnapshot().OwnedAllocationCount > 0,
                "device-first disposal fixture retains child allocations");
            otherDevice.Dispose();
            retainedEncoder.Dispose();
            Check(true, "device-first disposal cleans tracked allocations/modules and permits child disposal");
        }

        using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        encoder.Trace = (_, _) => interrupted.Cancel();
        Expect<OperationCanceledException>(() => encoder.Encode([0, 1], interrupted.Token), "cancel after first CUDA operator");
        encoder.Trace = null;
        var afterCancelled = device.GetMemorySnapshot();
        Check(afterCancelled.OwnedBytes == resident.OwnedBytes &&
            afterCancelled.OwnedAllocationCount == resident.OwnedAllocationCount && afterCancelled.ReleaseFailureCount == 0,
            "cancelled inference releases working allocations");
        float[] encoded = encoder.Encode([0, 1], deadline.Token);
        Check(encoded.Length == 16 && encoded.All(float.IsFinite), "encoder recovers after invalid input and cancellation");
    }
    CheckNoResources(device, "encoder unload returns all owned CUDA resources");
    // Explicit disposal is asserted inside the error reporting scope; a second Dispose is harmless.
    device.Dispose();
    Check(true, "context and profiling events unload without release failures");
    Console.WriteLine($"CUDA diagnostics: {passed} checks passed.");
    return 0;
}
catch (CudaException error)
{
    Console.Error.WriteLine($"CUDA diagnostics failed: code={error.Code}, operation={error.Operation}, driver_code={error.DriverCode}: {error.Message}");
    return 2;
}
catch (Exception error)
{
    Console.Error.WriteLine($"CUDA diagnostics failed: {error.GetType().Name}: {error.Message}");
    return 1;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

static ModernBertWeights CreateTinyWeights() => new()
{
    TokenEmbeddings = Enumerable.Range(0, 64).Select(value => (value % 8 - 3.5f) / 8).ToArray(),
    EmbeddingNorm = Enumerable.Repeat(1f, 8).ToArray(), FinalNorm = Enumerable.Repeat(1f, 8).ToArray(),
    Layers = [new ModernBertLayerWeights
    {
        Qkv = new float[192], AttentionOutput = new float[64], MlpNorm = Enumerable.Repeat(1f, 8).ToArray(),
        MlpUp = new float[128], MlpDown = new float[64],
    }],
};

static DecisionHeadWeights CreateInvalidHead()
{
    var invalidLayer = new DecisionHeadLayerWeights
    {
        Qkv = [], QkvBias = [], AttentionOutput = [], AttentionOutputBias = [], AttentionNorm = [],
        AttentionNormBias = [], MlpUp = [], MlpUpBias = [], MlpDown = [], MlpDownBias = [], MlpNorm = [], MlpNormBias = [],
    };
    return new DecisionHeadWeights
    {
        TypeEmbeddings = new float[24], Layers = [invalidLayer, invalidLayer], ScorerNorm = [], ScorerNormBias = [],
        ScorerDense = [], ScorerDenseBias = [], ScorerOutput = [], ScorerOutputBias = [],
    };
}
