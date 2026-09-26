using Sezika;

namespace Sezika.Benchmarks;

internal static class ProfileChecks
{
    internal static void Run()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        void Check(bool condition, string message)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (!condition) throw new InvalidOperationException(message);
            Console.WriteLine("PASS: " + message);
        }
        var inner = new SyntheticPipeline();
        using var pipeline = new ProfiledPipeline(inner);
        // Diagnostic text is not read by the pipeline observer; these are synthetic mechanics only.
        PreparedProfileSequence sequence = new("q", 0, [2, 4, 4, 1], [1, 2], ["a", "b"], null!);
        pipeline.Reset([sequence, sequence]);
        _ = pipeline.Score(sequence.Tokens, 0, sequence.Markers, deadline.Token);
        inner.Fail = true;
        try { pipeline.Score(sequence.Tokens, 0, sequence.Markers, deadline.Token); throw new InvalidOperationException("Expected synthetic failure."); }
        catch (DecisionException exception) when (exception.Code == "synthetic_failure") { }
        Check(pipeline.Actual.Count == 2 && pipeline.CompletedForwards == 1, "interrupted request records two entries and only one completed forward");
        pipeline.Reset([sequence]);
        Check(pipeline.Actual.Count == 0 && pipeline.CompletedForwards == 0, "independent instrumentation resets forward counts");
        inner.Fail = false; inner.Nonfinite = true;
        try { pipeline.Score(sequence.Tokens, 0, sequence.Markers, deadline.Token); throw new InvalidOperationException("Invalid output accepted."); }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("profile_output_invalid:", StringComparison.Ordinal)) { }
        Check(pipeline.CompletedForwards == 0, "invalid logits do not count as a completed forward");
        new DecisionResourceBudget { Deadline = TimeSpan.FromMinutes(30) }.Validate();
        try { new DecisionResourceBudget { Deadline = TimeSpan.FromMinutes(30) + TimeSpan.FromTicks(1) }.Validate(); throw new InvalidOperationException("Unbounded deadline accepted."); }
        catch (DecisionException exception) when (exception.Code == "decision_budget_invalid") { }
        Check(new DecisionResourceBudget().Deadline == TimeSpan.FromSeconds(30), "explicit longer budget preserves default and hard upper bound");
    }

    private sealed class SyntheticPipeline : IMarkerDecisionPipeline
    {
        public bool Fail { get; set; }
        public bool Nonfinite { get; set; }
        public float[] Score(ReadOnlySpan<int> tokenIds, int typeId, ReadOnlySpan<int> markerPositions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new DecisionException("synthetic_failure", "Synthetic software check only.");
            return Nonfinite ? [float.NaN, 0] : [1, 0];
        }
        public void Dispose() { }
    }
}
