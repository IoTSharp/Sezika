using System.Diagnostics;
using Sezika;

namespace Sezika.Cuda;

/// <summary>Three tiny synthetic GPU checks; never encoder, training, quality or formal acceptance evidence.</summary>
public sealed record MarkerHeadEvaluationChecksResult(int PassedChecks, double MaximumLossAbsoluteError)
{
    public bool UsesSyntheticFeatures => true;
    public bool RepresentsFormalAcceptance => false;
}

public static class IndependentCudaMarkerHeadEvaluationChecks
{
    public static MarkerHeadEvaluationChecksResult Run(CudaDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var clock = Stopwatch.StartNew();
        void Check()
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed >= TimeSpan.FromSeconds(30))
                throw new DecisionException("evaluation_checks_deadline_exceeded", "Tiny synthetic GPU CE checks exceeded thirty seconds.");
        }
        void Require(bool condition, string message)
        {
            if (!condition) throw new DecisionException("evaluation_checks_failed", message);
        }
        var identity = new MarkerFeatureIdentity("sezika/gpu-ce-numeric-fixture", new string('A', 64),
            new string('B', 64), new string('C', 64), 2);
        MarkerFeatureExample[] examples =
        [
            new("tiny-choice", MarkerQuestionType.Choice, "en", [1, 2, 3], [0, 2], [[1, 0], [0, 1]], 0),
            new("tiny-boolean", MarkerQuestionType.Boolean, "zh", [1, 2, 3], [0, 2], [[0.5f, -1], [-0.25f, 2]], 1),
            new("tiny-score", MarkerQuestionType.Score, "en", [1, 2, 3], [0, 1, 2], [[1, 2], [-1, 0.5f], [0.25f, -0.5f]], 2),
        ];
        var set = new MarkerFeatureSet(identity, examples);
        var baseline = device.GetMemorySnapshot();
        void RequireReleased()
        {
            Check();
            var after = device.GetMemorySnapshot();
            Require(after.OwnedBytes == baseline.OwnedBytes && after.OwnedAllocationCount == baseline.OwnedAllocationCount &&
                after.LoadedModuleCount == baseline.LoadedModuleCount && after.ReleaseFailureCount == baseline.ReleaseFailureCount,
                "Tiny GPU evaluator did not restore caller resource accounting.");
        }

        Check();
        Console.WriteLine("GPU CE numerical checks: 1/3 zero head, all three question types");
        var zero = new LinearMarkerHead(2, new float[6]);
        var initial = IndependentCudaMarkerHeadEvaluator.Evaluate(device, set, zero, TimeSpan.FromSeconds(10), cancellationToken: deadline.Token);
        Require(initial.ExampleCount == 3 && initial.CompletedExamples == 3 && initial.Rows.Length == 3 &&
            initial.ParameterSha256 == initial.DeviceParameterSha256, "Zero-head result denominator or actual device parameter identity differs.");
        double maximumError = 0;
        for (var index = 0; index < 3; index++)
        {
            Check();
            var expected = Math.Log(examples[index].Features.Length);
            var error = Math.Abs(initial.Rows[index].Loss - expected);
            maximumError = Math.Max(maximumError, error);
            Require(initial.Rows[index].RecordId == examples[index].RecordId && initial.Rows[index].Type == examples[index].Type &&
                initial.Rows[index].CandidateCount == examples[index].Features.Length && initial.Rows[index].TargetIndex == examples[index].TargetIndex &&
                error <= 2e-6, "Actual GPU zero-head CE or candidate/type mapping differs.");
        }
        RequireReleased();

        Console.WriteLine("GPU CE numerical checks: 2/3 nonzero head against tiny scalar numeric reference");
        var head = new LinearMarkerHead(2, [0.1f, -0.2f, 0.3f, 0.25f, -0.4f, 0.6f]);
        var actual = IndependentCudaMarkerHeadEvaluator.Evaluate(device, set, head, TimeSpan.FromSeconds(10), cancellationToken: deadline.Token);
        Require(actual.FeatureSha256 == initial.FeatureSha256 && actual.ParameterSha256 != initial.ParameterSha256 &&
            actual.ParameterSha256 == actual.DeviceParameterSha256 && actual.CompletedExamples == 3, "Nonzero-head feature/parameter identity differs.");
        double expectedTotal = 0;
        for (var index = 0; index < 3; index++)
        {
            Check();
            // Exactly three tiny numeric references; no CPU forward, gradient, SGD or fallback.
            var logits = head.Score(examples[index].Type, examples[index].Features, Check);
            var max = logits.Max(); double sum = 0;
            for (var candidate = 0; candidate < logits.Length && candidate < 3; candidate++) { Check(); sum += Math.Exp(logits[candidate] - max); }
            var expected = max + Math.Log(sum) - logits[examples[index].TargetIndex];
            expectedTotal += expected;
            var error = Math.Abs(actual.Rows[index].Loss - expected);
            maximumError = Math.Max(maximumError, error);
            Require(error <= 2e-6, "Actual GPU nonzero-head CE differs from the tiny numeric reference.");
        }
        Require(Math.Abs(actual.MeanLoss - expectedTotal / 3) <= 2e-6, "Complete GPU CE mean denominator differs.");
        RequireReleased();

        Console.WriteLine("GPU CE numerical checks: 3/3 cancellation after actual first CE and resource recovery");
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var observed = false;
        try
        {
            _ = IndependentCudaMarkerHeadEvaluator.Evaluate(device, set, head, TimeSpan.FromSeconds(10), row =>
            {
                observed = row.CompletedExamples == 1 && double.IsFinite(row.LastLoss);
                cancelled.Cancel();
            }, cancelled.Token);
            throw new DecisionException("evaluation_checks_failed", "Cancelled GPU CE returned a complete result.");
        }
        catch (OperationCanceledException) { Require(observed, "Cancellation test never reached its actual GPU CE callback."); }
        RequireReleased();
        Check();
        return new(3, maximumError);
    }
}
