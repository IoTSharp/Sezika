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
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxSteps is < 1 or > 10_000 || !float.IsFinite(options.LearningRate) ||
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
        var initial = resume?.Head.CopyWeights() ?? new float[checked(3 * prepared.Identity.HiddenSize)];
        var completed = resume?.CompletedSteps ?? 0;
        var lastLoss = resume?.LastLoss ?? 0d;
        var order = IndependentMarkerHeadTrainer.MakeOrder(prepared.Examples.Length, options.Seed);
        // <=32 feature buffers plus seven fixed working buffers; all are task-owned.
        var owned = new List<CudaDeviceBuffer>(39);
        CudaKernels? kernels = null;
        try
        {
            device.ThrowIfDisposed();
            Check();
            kernels = new CudaKernels(device, deadline.Token);
            CudaDeviceBuffer Allocate(int floats)
            {
                Check();
                if (owned.Count >= 39) throw new DecisionException("training_budget_invalid", "GPU allocation count limit.");
                var buffer = device.Allocate(checked((nuint)floats * sizeof(float)));
                owned.Add(buffer);
                return buffer;
            }
            var features = new CudaDeviceBuffer[prepared.Examples.Length];
            for (var item = 0; item < features.Length && item < 32; item++)
            {
                Check();
                var example = prepared.Examples[item];
                var packed = new float[checked(example.Features.Length * prepared.Identity.HiddenSize)];
                for (var candidate = 0; candidate < example.Features.Length && candidate < 32; candidate++)
                {
                    Check();
                    example.Features[candidate].CopyTo(packed, checked(candidate * prepared.Identity.HiddenSize));
                }
                features[item] = Allocate(packed.Length);
                try { device.CopyToDevice(features[item], packed); }
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
            // Checked finite: <=10000 updates, <=32 candidates, H<=2048, caller deadline.
            for (var step = completed; step < options.MaxSteps && step < 10_000; step++)
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
        finally
        {
            // Driver calls cannot be interrupted in-flight: an outer bounded runner remains mandatory.
            for (var item = owned.Count - 1; item >= 0 && item < 39; item--) owned[item].Dispose();
            kernels?.Dispose();
            Array.Clear(initial);
        }
    }
}
