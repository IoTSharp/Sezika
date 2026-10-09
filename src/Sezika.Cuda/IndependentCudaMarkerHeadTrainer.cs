using System.Diagnostics;
using Sezika;

namespace Sezika.Cuda;

/// <summary>Bounded GPU SGD over frozen complete-sequence marker features. Only the three linear rows are trained.</summary>
/// <remarks>The caller owns and serializes the CUDA device. No encoder or host gradient/update is executed here.</remarks>
public static class IndependentCudaMarkerHeadTrainer
{
    public const string Backend = "cuda-independent-linear-marker-head-sgd-fp32";

    public static MarkerTrainingState Train(CudaDevice device, MarkerFeatureSet set, MarkerTrainingOptions options,
        MarkerTrainingState? resume = null, Action<MarkerTrainingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        TrainingCleanup? recovery = null;
        List<CudaDeviceBuffer>? owned = null;
        CudaKernels? kernels = null;
        float[]? initial = null;
        Exception? primary = null;
        try
        {
            ArgumentNullException.ThrowIfNull(set);
            var maximumBuffers = set.Examples?.Count is >= 1 and <= IndependentMarkerHeadTrainer.MaximumExamples
                ? set.Examples.Count + 7 : 39;
            recovery = new TrainingCleanup(device, maximumBuffers: maximumBuffers);
            ArgumentNullException.ThrowIfNull(options);
            if (options.MaxSteps is < 1 or > IndependentMarkerHeadTrainer.MaximumSteps || !float.IsFinite(options.LearningRate) ||
                options.LearningRate <= 0 || options.LearningRate > 1 ||
                options.MaxDuration <= TimeSpan.Zero || options.MaxDuration > TimeSpan.FromMinutes(10))
                throw new DecisionException("training_budget_invalid", "Step, learning rate or wall-clock budget is invalid.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(options.MaxDuration);
            var clock = Stopwatch.StartNew();
            void Check()
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (clock.Elapsed >= options.MaxDuration)
                    throw new DecisionException("training_deadline_exceeded", "GPU training wall-clock budget expired.");
            }
            Check();
            var prepared = IndependentMarkerHeadTrainer.Prepare(set, Check);
            if (resume is not null && (!IndependentMarkerHeadTrainer.IdentityMatches(resume.Identity, prepared.Identity) ||
                !resume.FeatureSha256.Equals(prepared.Sha256, StringComparison.OrdinalIgnoreCase) ||
                resume.Seed != options.Seed || resume.LearningRate != options.LearningRate ||
                resume.CompletedSteps < 0 || resume.CompletedSteps > options.MaxSteps ||
                !double.IsFinite(resume.LastLoss) || resume.LastLoss < 0 ||
                resume.Weights.Length != checked(3 * prepared.Identity.HiddenSize) ||
                Array.Exists(resume.Weights, value => !float.IsFinite(value))))
                throw new DecisionException("training_resume_mismatch", "Checkpoint differs from features or GPU training configuration.");
            initial = resume?.Head.CopyWeights() ?? new float[checked(3 * prepared.Identity.HiddenSize)];
            device.TrainingFaults?.Reach(TrainingFaultPoint.InitialCreated, initial);
            var completed = resume?.CompletedSteps ?? 0;
            var lastLoss = resume?.LastLoss ?? 0d;
            var order = IndependentMarkerHeadTrainer.MakeOrder(prepared.Examples.Length, options.Seed);
            // <=256 feature buffers plus seven fixed working buffers; all are task-owned.
            owned = new List<CudaDeviceBuffer>(maximumBuffers);
            device.ThrowIfDisposed();
            Check();
            kernels = new CudaKernels(device, deadline.Token, recovery);
            CudaDeviceBuffer Allocate(int floats)
            {
                Check();
                if (owned.Count >= maximumBuffers) throw new DecisionException("training_budget_invalid", "GPU allocation count limit.");
                var buffer = device.Allocate(checked((nuint)floats * sizeof(float)));
                owned.Add(buffer);
                return buffer;
            }
            var features = new CudaDeviceBuffer[prepared.Examples.Length];
            for (var item = 0; item < features.Length && item < IndependentMarkerHeadTrainer.MaximumExamples; item++)
            {
                Check();
                var example = prepared.Examples[item];
                var packed = new float[checked(example.Features.Length * prepared.Identity.HiddenSize)];
                try
                {
                    for (var candidate = 0; candidate < example.Features.Length && candidate < 32; candidate++)
                    {
                        Check();
                        example.Features[candidate].CopyTo(packed, checked(candidate * prepared.Identity.HiddenSize));
                    }
                    device.TrainingFaults?.Reach(TrainingFaultPoint.PackedBeforeAllocate, packed);
                    features[item] = Allocate(packed.Length);
                    device.CopyToDevice(features[item], packed);
                }
                finally { Array.Clear(packed); }
            }
            var current = Allocate(initial.Length);
            var next = Allocate(initial.Length);
            var gradient = Allocate(initial.Length);
            var logits = Allocate(32 * 3);
            var delta = Allocate(32);
            var loss = Allocate(1);
            var status = Allocate(2);
            device.CopyToDevice(current, initial);
            var metrics = new float[2];
            progress?.Invoke(new MarkerTrainingProgress(completed, options.MaxSteps, lastLoss));
            // Checked finite: <=24576 updates, <=32 candidates, H<=2048, caller deadline.
            for (var step = completed; step < options.MaxSteps && step < IndependentMarkerHeadTrainer.MaximumSteps; step++)
            {
                Check();
                var selected = order[step % order.Length];
                var example = prepared.Examples[selected];
                kernels.Linear(features[selected], current, logits, example.Features.Length, prepared.Identity.HiddenSize, 3);
                Check();
                kernels.MarkerCrossEntropy(logits, delta, loss, example.Features.Length, (int)example.Type, example.TargetIndex);
                Check();
                kernels.MarkerGradientSgd(features[selected], current, delta, gradient, next,
                    example.Features.Length, prepared.Identity.HiddenSize, (int)example.Type, options.LearningRate);
                Check();
                kernels.MarkerUpdateStatus(gradient, next, loss, status, initial.Length);
                Check();
                device.CopyFromDevice(metrics, status);
                Check();
                if (metrics[1] != 1 || !float.IsFinite(metrics[0]) || metrics[0] < 0)
                    throw new DecisionException("training_value_invalid", "GPU loss, gradient or proposed weight is non-finite.");
                // Only a fully validated, uncancelled update commits. Resume/initial host state is immutable.
                (current, next) = (next, current);
                completed = step + 1;
                lastLoss = metrics[0];
                progress?.Invoke(new MarkerTrainingProgress(completed, options.MaxSteps, lastLoss));
            }
            Check();
            var weights = new float[initial.Length];
            device.CopyFromDevice(weights, current);
            Check();
            if (Array.Exists(weights, value => !float.IsFinite(value)))
                throw new DecisionException("training_value_invalid", "GPU final weights are non-finite.");
            return new MarkerTrainingState(prepared.Identity, prepared.Sha256, options.Seed,
                options.LearningRate, completed, lastLoss, weights);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            // Driver calls cannot be interrupted in-flight: an outer bounded runner remains mandatory.
            try
            {
                if (recovery is not null)
                {
                    try { recovery.Buffers(owned); }
                    catch (Exception error) { recovery.Record(error); }
                    try { recovery.Kernels(kernels); }
                    catch (Exception error) { recovery.Record(error); }
                    recovery.Publish();
                }
            }
            finally
            {
                if (initial is not null) Array.Clear(initial);
                device.ResetTrainingFaults();
            }
            if (primary is null && recovery?.Result.Incomplete == true)
                throw new DecisionException("training_cleanup_incomplete", "Training cleanup retained resources, exceeded its recovery budget or recorded a release failure; the caller still owns the device.");
        }
    }
}
