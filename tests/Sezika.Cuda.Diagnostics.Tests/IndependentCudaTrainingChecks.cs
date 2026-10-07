using Sezika;
using Sezika.Cuda;

namespace Sezika.Cuda.Diagnostics.Tests;

internal static class IndependentCudaTrainingChecks
{
    private static MarkerFeatureSet Fixture(MarkerQuestionType type)
    {
        float[][] features = type == MarkerQuestionType.Score
            ? [[0.3f, -0.2f, 0.1f], [-0.1f, 0.4f, 0.2f], [0.2f, 0.1f, -0.3f]]
            : [[0.3f, -0.2f, 0.1f], [-0.1f, 0.4f, 0.2f]];
        return new(new("sezika/gpu-training-fixture", new string('A', 64), new string('B', 64), new string('C', 64), 3),
            [new("fixture", type, "en", [2, 3, 4, 5, 6, 1], features.Length == 3 ? [1, 2, 4] : [1, 4], features, 1)]);
    }

    public static void Prepare(Action<bool, string> check, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        // Three types, nine parameters each, central differences against scalar FP64 loss.
        for (var type = 0; type < 3; type++)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var set = Fixture((MarkerQuestionType)type);
            var weights = new float[9];
            var gradient = new LinearMarkerHead(3, weights).Gradient(set.Examples[0], deadline.Token.ThrowIfCancellationRequested);
            for (var index = 0; index < 9; index++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var plus = (float[])weights.Clone(); var minus = (float[])weights.Clone();
                plus[index] += 0.001f; minus[index] -= 0.001f;
                var numerical = (new LinearMarkerHead(3, plus).Gradient(set.Examples[0]).Loss -
                    new LinearMarkerHead(3, minus).Gradient(set.Examples[0]).Loss) / 0.002d;
                check(Math.Abs(numerical - gradient.WeightGradient[index]) <= 1e-5, $"{(MarkerQuestionType)type} scalar finite-difference parameter {index}");
            }
        }
    }

    public static void Run(CudaDevice device, Action<bool, string> check, CancellationToken cancellationToken, int maximumTypes)
    {
        if (maximumTypes is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(maximumTypes));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var initialResources = device.GetMemorySnapshot();
        bool Near(double left, double right) => Math.Abs(left - right) <= 2e-5 + 2e-5 * Math.Abs(right);
        for (var type = 0; type < maximumTypes && type < 3; type++)
        {
            token.ThrowIfCancellationRequested();
            var set = Fixture((MarkerQuestionType)type);
            var options = new MarkerTrainingOptions(17, 1, 0.1f, TimeSpan.FromSeconds(5));
            var seedState = IndependentMarkerHeadTrainer.Train(set, options, cancellationToken: token);
            var seedWeights = seedState.Head.CopyWeights();
            var expectedGradient = seedState.Head.Gradient(set.Examples[0], token.ThrowIfCancellationRequested);
            var actual = IndependentCudaMarkerHeadTrainer.Train(device, set, options with { MaxSteps = 2 }, seedState, cancellationToken: token);
            var weights = actual.Head.CopyWeights();
            check(Near(actual.LastLoss, expectedGradient.Loss), $"{(MarkerQuestionType)type} GPU nonzero-weight cross entropy matches scalar FP64");
            for (var parameter = 0; parameter < 9; parameter++)
            {
                token.ThrowIfCancellationRequested();
                var effectiveGradient = ((double)seedWeights[parameter] - weights[parameter]) / options.LearningRate;
                check(Near(effectiveGradient, expectedGradient.WeightGradient[parameter]), $"{(MarkerQuestionType)type} GPU SGD gradient parameter {parameter}");
                if (parameter / 3 != type) check(weights[parameter] == seedWeights[parameter], "unselected head row remains exact");
            }
            var two = IndependentCudaMarkerHeadTrainer.Train(device, set, options with { MaxSteps = 2 }, cancellationToken: token);
            var resumed = IndependentCudaMarkerHeadTrainer.Train(device, set, options with { MaxSteps = 4 }, two, cancellationToken: token);
            var continuous = IndependentCudaMarkerHeadTrainer.Train(device, set, options with { MaxSteps = 4 }, cancellationToken: token);
            check(resumed.Head.CopyWeights().SequenceEqual(continuous.Head.CopyWeights()) && resumed.LastLoss == continuous.LastLoss &&
                resumed.CompletedSteps == 4 && resumed.FeatureSha256 == continuous.FeatureSha256, "GPU 2+2 resume equals GPU four continuous updates");
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            try
            {
                _ = IndependentCudaMarkerHeadTrainer.Train(device, set, options with { MaxSteps = 3 }, seedState,
                    item => { if (item.CompletedSteps > seedState.CompletedSteps) cancel.Cancel(); }, cancel.Token);
                check(false, "cancel after one committed GPU update must fail");
            }
            catch (OperationCanceledException) { check(seedState.Head.CopyWeights().SequenceEqual(seedWeights), "cancel leaves supplied resume state immutable"); }
            var released = device.GetMemorySnapshot();
            check(released.OwnedBytes == initialResources.OwnedBytes && released.OwnedAllocationCount == initialResources.OwnedAllocationCount &&
                released.LoadedModuleCount == initialResources.LoadedModuleCount && released.ReleaseFailureCount == initialResources.ReleaseFailureCount,
                "GPU training success/cancel release all own buffers/modules and retain caller device");
        }
        using var preCancelled = new CancellationTokenSource(); preCancelled.Cancel();
        device.ResetTelemetry();
        try { _ = IndependentCudaMarkerHeadTrainer.Train(device, Fixture(MarkerQuestionType.Boolean), new(17, 1, 0.1f, TimeSpan.FromSeconds(3)), cancellationToken: preCancelled.Token); check(false, "pre-cancelled trainer must fail"); }
        catch (OperationCanceledException) { check(device.GetTelemetry().KernelLaunchCount == 0, "pre-cancelled training launches nothing"); }
        if (maximumTypes == 3)
        {
            var overflow = new MarkerFeatureSet(Fixture(MarkerQuestionType.Choice).Identity with { HiddenSize = 2 },
                [new("overflow", MarkerQuestionType.Choice, "en", [1, 2], [0, 1], [[float.MaxValue, float.MaxValue], [-float.MaxValue, -float.MaxValue]], 1)]);
            try { _ = IndependentCudaMarkerHeadTrainer.Train(device, overflow, new(17, 2, 1, TimeSpan.FromSeconds(3)), cancellationToken: token); check(false, "GPU FP32 overflow must fail before publishing state"); }
            catch (DecisionException error) { check(error.Code == "training_value_invalid", "GPU status rejects non-finite proposed training state"); }
            var released = device.GetMemorySnapshot();
            check(released.OwnedBytes == initialResources.OwnedBytes && released.LoadedModuleCount == initialResources.LoadedModuleCount &&
                released.ReleaseFailureCount == initialResources.ReleaseFailureCount, "GPU non-finite failure releases training resources");
        }
    }
}
