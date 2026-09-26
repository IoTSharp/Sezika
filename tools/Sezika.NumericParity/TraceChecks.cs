using System.Text.Json;

internal static class TraceChecks
{
    public static int Run()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var token = deadline.Token;
        var count = 0;
        void Check(bool value, string name)
        {
            token.ThrowIfCancellationRequested();
            if (!value) throw new InvalidOperationException(name);
            count++;
            Console.WriteLine($"PASS: {name}");
        }
        void Reject(Action action, string name)
        {
            try { action(); throw new InvalidOperationException(name + " unexpectedly accepted"); }
            catch (InvalidDataException) { Check(true, name); }
        }
        try
        {
            var tolerance = new NumericTolerance(0.0005, 0.0001);
            var tolerances = new TraceTolerances(tolerance, new(0.0001, 0.0001), new(0.001, 0.0001), 0.001);
            Check(tolerance.Accepts(100, 100.0104) && !tolerance.Accepts(100, 100.0106), "absolute plus relative threshold distinguishes the boundary");
            Check(!tolerance.Accepts(0, double.NaN) && !tolerance.Accepts(double.PositiveInfinity, 1), "non-finite values never satisfy a tolerance");
            var fixture = Fixture([0.00001, 0], [0.5000025, 0.4999975], "first");
            var equal = TraceComparison.Compare(fixture, fixture.RawLogits!, fixture.Probabilities!, tolerances);
            Check(equal.Passed && equal.NearTie, "near-tie reference is reported and can pass unchanged");
            var swapped = TraceComparison.Compare(fixture, [0, 0.00001], [0.4999975, 0.5000025], tolerances);
            Check(!swapped.Passed && swapped.NearTie && swapped.Issues.SequenceEqual(new[] { "prediction:choice" }),
                "near-tie prediction reversal fails even when every numeric tolerance passes");
            var far = TraceComparison.Compare(fixture, [0.1, 0], [0.5000025, 0.4999975], tolerances);
            Check(!far.Passed && far.Issues.Contains("logit:first"), "logit threshold failures affect acceptance");
            Reject(() => TraceComparison.Compare(fixture, [0, 0], [0.4, 0.4], tolerances), "invalid probability normalization rejects");
            Reject(() => TraceComparison.Compare(fixture, [0], [1], tolerances), "candidate count mismatch rejects");
            Reject(() => TraceComparison.Compare(fixture, [double.NaN, 0], fixture.Probabilities!, tolerances), "non-finite comparison rejects");
            var metrics = TraceCollector.Compare("layer/0/hidden", [1, 2, 3], [1, 2, 3.5f], token);
            Check(metrics.Elements == 3 && metrics.MaxAbsoluteError == 0.5 && metrics.WorstElement == 2 && metrics.ScalarSha256 != metrics.ActualSha256,
                "full tensor comparison records the worst coordinate and independent hashes");
            Reject(() => TraceCollector.Compare("x", [1], [1, 2], token), "tensor shape mismatch rejects");
            Reject(() => TraceCollector.Compare("x", [float.NaN], [0], token), "non-finite tensor rejects");
            var snapshots = new Dictionary<string, float[]>(StringComparer.Ordinal);
            var collector = new TraceCollector(2, 1, 1, 2, snapshots, true, token);
            Check(collector.Missing.Count == 6, "every expected encoder/head/scorer checkpoint is required");
            collector.Observe("embedding/norm", [1, 2]);
            Check(collector.Missing.Count == 5 && collector.RetainedBytes == 8, "partial trace remains explicitly incomplete");
            Reject(() => collector.Observe("embedding/norm", [1, 2]), "duplicate checkpoint rejects");
            Reject(() => new TraceCollector(1024, 4096, 64, 2, snapshots, true, token), "memory budget rejects before trace retention");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { TraceCollector.Compare("x", [0], [0], cancelled.Token); throw new InvalidOperationException("cancellation ignored"); }
            catch (OperationCanceledException) { Check(true, "tensor comparison observes cancellation"); }
            var json = JsonSerializer.Serialize(tolerances, TraceJsonContext.Default.TraceTolerances);
            Check(JsonSerializer.Deserialize(json, TraceJsonContext.Default.TraceTolerances) == tolerances, "source-generated contract JSON preserves thresholds");
            using var booleanInput = JsonDocument.Parse("{\"state\":\"x\",\"question\":{\"type\":\"noul\",\"instructions\":\"q\"},\"max_len\":1024,\"head_max_len\":256}");
            var booleanReference = fixture with { Primitive = "boolean", Input = booleanInput.RootElement.Clone() };
            var request = TraceDiagnostics.Request(booleanReference);
            Check(request.Questions["q"] is Sezika.BooleanQuestion { Criteria: null } && request.LengthPolicy == Sezika.PromptLengthPolicy.LayaCompatible,
                "expanded Boolean type maps to the production discriminator and preserves default criteria");
            Reject(() => TraceDiagnostics.Request(booleanReference with { Primitive = "choice" }), "reference primitive disagreement rejects");
            Console.WriteLine($"Trace diagnostic checks passed: {count}; synthetic values validate diagnostics only.");
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or JsonException)
        {
            Console.Error.WriteLine($"Trace checks failed: {exception.Message}");
            return 1;
        }
    }

    private static TraceReferenceCase Fixture(double[] logits, double[] probabilities, string label)
    {
        using var input = JsonDocument.Parse("{}");
        using var prediction = JsonDocument.Parse("{\"choice_label\":\"" + label + "\"}");
        return new TraceReferenceCase
        {
            Id = "synthetic-diagnostic-check", Primitive = "choice", Status = "answered", Input = input.RootElement.Clone(),
            TokenIds = [2, 4, 4, 1], MarkerPositions = [1, 2], CandidateLabels = ["first", "second"],
            RawLogits = logits, Probabilities = probabilities, Prediction = prediction.RootElement.Clone(),
        };
    }
}
