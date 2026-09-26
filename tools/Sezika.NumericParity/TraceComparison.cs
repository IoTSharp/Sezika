using Sezika;

internal static class TraceComparison
{
    public static OutputDifference Compare(TraceReferenceCase reference, double[] actualLogits,
        double[] actualProbabilities, TraceTolerances tolerances)
    {
        var expectedLogits = reference.RawLogits ?? throw new InvalidDataException("Reference logits missing.");
        var expectedProbabilities = reference.Probabilities ?? throw new InvalidDataException("Reference probabilities missing.");
        var labels = reference.CandidateLabels ?? throw new InvalidDataException("Reference candidate labels missing.");
        if (expectedLogits.Length is < 2 or > 32 || expectedLogits.Length != labels.Length ||
            expectedLogits.Length != actualLogits.Length || expectedLogits.Length != expectedProbabilities.Length ||
            expectedLogits.Length != actualProbabilities.Length || labels.Distinct(StringComparer.Ordinal).Count() != labels.Length)
            throw new InvalidDataException("Reference or actual candidate arrays have inconsistent shape.");
        var issues = new List<string>();
        double maxLogit = 0, maxProbability = 0;
        for (var index = 0; index < expectedLogits.Length; index++)
        {
            if (!double.IsFinite(expectedLogits[index]) || !double.IsFinite(actualLogits[index]) ||
                !double.IsFinite(expectedProbabilities[index]) || !double.IsFinite(actualProbabilities[index]))
                throw new InvalidDataException("Output comparison requires finite values.");
            if (!tolerances.RawLogits.Accepts(expectedLogits[index], actualLogits[index])) issues.Add($"logit:{labels[index]}");
            if (!tolerances.Probabilities.Accepts(expectedProbabilities[index], actualProbabilities[index])) issues.Add($"probability:{labels[index]}");
            maxLogit = Math.Max(maxLogit, Math.Abs(expectedLogits[index] - actualLogits[index]));
            maxProbability = Math.Max(maxProbability, Math.Abs(expectedProbabilities[index] - actualProbabilities[index]));
        }
        if (expectedProbabilities.Any(value => value is < 0 or > 1) || actualProbabilities.Any(value => value is < 0 or > 1) ||
            Math.Abs(expectedProbabilities.Sum() - 1) > 0.000002 || Math.Abs(actualProbabilities.Sum() - 1) > 0.000002)
            throw new InvalidDataException("Probability distribution is invalid.");
        var sorted = expectedLogits.OrderDescending().ToArray();
        var margin = sorted[0] - sorted[1];
        switch (reference.Primitive)
        {
            case "choice":
                var expectedLabel = reference.Prediction.GetProperty("choice_label").GetString();
                if (expectedLabel != labels[DecisionMath.ArgMax(expectedProbabilities)]) throw new InvalidDataException("Reference Choice prediction is inconsistent.");
                if (expectedLabel != labels[DecisionMath.ArgMax(actualProbabilities)]) issues.Add("prediction:choice");
                break;
            case "boolean":
                if (!labels.SequenceEqual(new[] { "false", "true" })) throw new InvalidDataException("Boolean marker order is invalid.");
                var expectedBoolean = reference.Prediction.GetProperty("boolean").GetBoolean();
                if (expectedBoolean != (expectedProbabilities[1] >= 0.5)) throw new InvalidDataException("Reference Boolean prediction is inconsistent.");
                if (expectedBoolean != (actualProbabilities[1] >= 0.5)) issues.Add("prediction:boolean");
                break;
            case "score":
                var expectedScore = reference.Prediction.GetProperty("score").GetDouble();
                if (!tolerances.Score.Accepts(expectedScore, Score(expectedProbabilities))) throw new InvalidDataException("Reference Score prediction is inconsistent.");
                if (!tolerances.Score.Accepts(expectedScore, Score(actualProbabilities))) issues.Add("prediction:score");
                break;
            default: throw new InvalidDataException("Unknown reference primitive.");
        }
        return new(issues.Count == 0, margin <= tolerances.NearTieLogitMargin, margin, maxLogit, maxProbability, issues);
    }

    private static double Score(double[] values)
    {
        double result = 0;
        for (var index = 0; index < values.Length; index++) result += index * values[index];
        return result;
    }
}
