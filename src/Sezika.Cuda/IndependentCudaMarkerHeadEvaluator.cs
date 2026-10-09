using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Sezika;

namespace Sezika.Cuda;

public sealed record MarkerHeadEvaluationRow(string RecordId, MarkerQuestionType Type,
    int CandidateCount, int TargetIndex, double Loss);

public sealed record MarkerHeadEvaluationProgress(int CompletedExamples, int ExampleCount,
    string RecordId, double LastLoss);

/// <summary>Complete, read-only GPU CE observations on explicitly supplied frozen features.</summary>
public sealed record MarkerHeadEvaluationResult(int ExampleCount, int CompletedExamples, double MeanLoss,
    string FeatureSha256, string ParameterSha256, string DeviceParameterSha256, MarkerHeadEvaluationRow[] Rows);

/// <summary>GPU linear logits and cross-entropy only. The caller owns the CUDA context; no weight update is executed.</summary>
/// <remarks>Caller serializes all operations on its device. Synchronous driver calls require an external bounded runner.</remarks>
public static class IndependentCudaMarkerHeadEvaluator
{
    public const string Backend = "cuda-frozen-marker-head-cross-entropy-fp32-v1";
    private const ulong MaxDeviceOwnedBytes = 3UL * 1024 * 1024 * 1024;

    public static MarkerHeadEvaluationResult Evaluate(CudaDevice device, MarkerFeatureSet set,
        LinearMarkerHead head, TimeSpan maxDuration, Action<MarkerHeadEvaluationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(head);
        if (maxDuration <= TimeSpan.Zero || maxDuration > TimeSpan.FromMinutes(5))
            throw new DecisionException("evaluation_budget_invalid", "GPU evaluation duration must be in (0, five minutes].");
        if (!BitConverter.IsLittleEndian)
            throw new DecisionException("evaluation_parameter_encoding_invalid", "Parameter SHA requires little-endian IEEE F32 encoding.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(maxDuration);
        var clock = Stopwatch.StartNew();
        void Check()
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed >= maxDuration)
                throw new DecisionException("evaluation_deadline_exceeded", "GPU evaluation wall-clock budget expired.");
        }

        // Storage and shared independent recovery exist before any native acquisition.
        var owned = new List<CudaDeviceBuffer>(5);
        var recovery = new TrainingCleanup(device);
        CudaKernels? kernels = null;
        float[]? weights = null, copiedWeights = null, packed = null;
        MarkerHeadEvaluationResult? result = null;
        Exception? primary = null, cleanupFailure = null;
        try
        {
            Check(); device.ThrowIfDisposed();
            var prepared = IndependentMarkerHeadTrainer.Prepare(set, Check);
            if (head.HiddenSize != prepared.Identity.HiddenSize)
                throw new DecisionException("evaluation_shape_invalid", "Head width differs from the complete frozen feature identity.");
            if (device.OwnedResources.Bytes > MaxDeviceOwnedBytes)
                throw new DecisionException("evaluation_device_budget_exceeded", "Caller-owned device bytes already exceed three GiB.");

            weights = head.CopyWeights();
            copiedWeights = new float[weights.Length];
            packed = new float[checked(32 * prepared.Identity.HiddenSize)];
            var rows = new MarkerHeadEvaluationRow[prepared.Examples.Length];
            var hostLoss = new float[1];
            var parameterHash = ParameterHash(weights);
            Check();
            kernels = new CudaKernels(device, deadline.Token, recovery);

            CudaDeviceBuffer Allocate(int floats)
            {
                Check();
                if (owned.Count >= 5)
                    throw new DecisionException("evaluation_budget_invalid", "GPU evaluation exceeds its five-buffer bound.");
                var bytes = checked((ulong)floats * sizeof(float));
                var callerBytes = device.OwnedResources.Bytes;
                if (callerBytes > MaxDeviceOwnedBytes || bytes > MaxDeviceOwnedBytes - callerBytes)
                    throw new DecisionException("evaluation_device_budget_exceeded", "GPU evaluation allocation would exceed three GiB owned.");
                var buffer = device.Allocate(checked((nuint)bytes));
                owned.Add(buffer); // Fixed capacity: no allocation after successful acquisition.
                Check();
                if (device.OwnedResources.Bytes > MaxDeviceOwnedBytes)
                    throw new DecisionException("evaluation_device_budget_exceeded", "GPU evaluation owned bytes exceed three GiB.");
                return buffer;
            }

            var features = Allocate(packed.Length);
            var parameters = Allocate(weights.Length);
            var logits = Allocate(32 * 3);
            var delta = Allocate(32);
            var loss = Allocate(1);
            device.CopyToDevice(parameters, weights);
            Check();
            device.CopyFromDevice(copiedWeights, parameters);
            Check();
            RequireSameParameters(weights, copiedWeights);
            var deviceParameterHash = ParameterHash(copiedWeights);
            double totalLoss = 0;
            var completed = 0;

            // Prepare enforces 1..256 records, 2..32 candidates, H<=2048 and finite values.
            for (var item = 0; item < prepared.Examples.Length && item < IndependentMarkerHeadTrainer.MaximumExamples; item++)
            {
                Check();
                var example = prepared.Examples[item];
                for (var candidate = 0; candidate < example.Features.Length && candidate < 32; candidate++)
                {
                    Check();
                    example.Features[candidate].CopyTo(packed, checked(candidate * prepared.Identity.HiddenSize));
                }
                device.CopyToDevice(features, packed);
                Check();
                kernels.Linear(features, parameters, logits, example.Features.Length, prepared.Identity.HiddenSize, 3);
                Check();
                kernels.MarkerCrossEntropy(logits, delta, loss, example.Features.Length, (int)example.Type, example.TargetIndex);
                Check();
                device.CopyFromDevice(hostLoss, loss);
                Check();
                if (!float.IsFinite(hostLoss[0]) || hostLoss[0] < 0)
                    throw new DecisionException("evaluation_value_invalid", "GPU CE returned a nonfinite or negative loss.");
                rows[item] = new(example.RecordId, example.Type, example.Features.Length, example.TargetIndex, hostLoss[0]);
                totalLoss += hostLoss[0];
                if (!double.IsFinite(totalLoss))
                    throw new DecisionException("evaluation_value_invalid", "Host aggregation of GPU CE losses is nonfinite.");
                completed++;
                progress?.Invoke(new(completed, prepared.Examples.Length, example.RecordId, hostLoss[0]));
                Check();
            }
            if (completed != prepared.Examples.Length)
                throw new DecisionException("evaluation_denominator_invalid", "GPU CE did not complete every frozen example.");

            // Verify the actual device parameter tensor again after all CE calls. No SGD kernel is called.
            device.CopyFromDevice(copiedWeights, parameters);
            Check();
            RequireSameParameters(weights, copiedWeights);
            if (!ParameterHash(copiedWeights).Equals(deviceParameterHash, StringComparison.Ordinal))
                throw new DecisionException("evaluation_parameter_changed", "GPU CE changed the uploaded parameter bytes.");
            var mean = totalLoss / completed;
            if (!double.IsFinite(mean) || mean < 0)
                throw new DecisionException("evaluation_value_invalid", "Complete GPU CE mean is invalid.");
            result = new(prepared.Examples.Length, completed, mean, prepared.Sha256, parameterHash, deviceParameterHash, rows);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            // One independent five-second recovery clock covers buffers and partial/nested modules.
            // Driver operations remain non-preemptible; a bounded external process is mandatory.
            try { recovery.Buffers(owned); }
            catch (Exception error) { recovery.Record(error); }
            try { recovery.Kernels(kernels); }
            catch (Exception error) { recovery.Record(error); }
            if (weights is not null) Array.Clear(weights);
            if (copiedWeights is not null) Array.Clear(copiedWeights);
            if (packed is not null) Array.Clear(packed);
            try
            {
                var cleanup = recovery.Result;
                if (cleanup.Incomplete)
                {
                    cleanupFailure = new DecisionException("evaluation_cleanup_incomplete",
                        "GPU CE cleanup exceeded its independent budget, retained owners, failed native release or did not restore caller accounting.");
                    if (cleanup.FirstException is not null)
                        cleanupFailure = new AggregateException("GPU CE cleanup and native owner release failed.", cleanupFailure, cleanup.FirstException);
                }
            }
            catch (Exception error) { cleanupFailure = error; }
        }

        if (primary is not null && cleanupFailure is not null)
            throw new AggregateException("GPU CE evaluation and owned-resource cleanup failed.", primary, cleanupFailure);
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        return result ?? throw new DecisionException("evaluation_result_missing", "GPU CE returned no complete result.");
    }

    private static string ParameterHash(float[] weights) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(weights.AsSpan())));

    private static void RequireSameParameters(float[] expected, float[] actual)
    {
        if (!MemoryMarshal.AsBytes(expected.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(actual.AsSpan())))
            throw new DecisionException("evaluation_parameter_mismatch", "Actual device parameter F32 bytes differ from the supplied head.");
    }
}
