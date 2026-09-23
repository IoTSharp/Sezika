namespace Sezika;

public static class DecisionMath
{
    public static double[] Softmax(ReadOnlySpan<float> logits, double temperature = 1d)
    {
        if (logits.Length < 2 || !double.IsFinite(temperature) || temperature <= 0)
        {
            throw new DecisionException("decision_numeric_invalid", "Softmax requires at least two finite logits and a positive temperature.");
        }
        var maximum = float.NegativeInfinity;
        for (var i = 0; i < logits.Length; i++)
        {
            if (!float.IsFinite(logits[i])) throw new DecisionException("decision_numeric_invalid", "Logits must be finite.");
            maximum = MathF.Max(maximum, logits[i]);
        }
        var probabilities = new double[logits.Length];
        var sum = 0d;
        for (var i = 0; i < logits.Length; i++)
        {
            var value = Math.Exp((logits[i] - maximum) / temperature);
            probabilities[i] = value;
            sum += value;
        }
        if (!double.IsFinite(sum) || sum <= 0) throw new DecisionException("decision_numeric_invalid", "Softmax normalization failed.");
        for (var i = 0; i < probabilities.Length; i++) probabilities[i] /= sum;
        return probabilities;
    }

    public static double Concentration(ReadOnlySpan<double> probabilities)
    {
        if (probabilities.Length < 2) return 1d;
        var entropy = 0d;
        var sum = 0d;
        for (var i = 0; i < probabilities.Length; i++)
        {
            var probability = probabilities[i];
            if (!double.IsFinite(probability) || probability < 0) throw new DecisionException("decision_numeric_invalid", "Probabilities must be finite and non-negative.");
            sum += probability;
            if (probability > 0) entropy -= probability * Math.Log(probability);
        }
        if (!double.IsFinite(sum) || Math.Abs(sum - 1d) > 1e-8) throw new DecisionException("decision_numeric_invalid", "Probabilities must sum to one.");
        return Math.Clamp(1d - entropy / Math.Log(probabilities.Length), 0d, 1d);
    }

    public static int ArgMax(ReadOnlySpan<double> values)
    {
        if (values.Length == 0) throw new ArgumentException("Values cannot be empty.", nameof(values));
        var index = 0;
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] > values[index]) index = i;
        }
        return index;
    }
}

public sealed record CalibrationProfile
{
    public required string ProfileId { get; init; }
    public required string ModelRevision { get; init; }
    public required string TokenizerRevision { get; init; }
    public required string Primitive { get; init; }
    public required string Language { get; init; }
    public required string Scope { get; init; }
    public required double Temperature { get; init; }

    public void Validate(string modelRevision, string tokenizerRevision, string primitive)
    {
        if (string.IsNullOrWhiteSpace(ProfileId) || !double.IsFinite(Temperature) || Temperature <= 0)
            throw new DecisionException("decision_calibration_invalid", "Calibration profile is invalid.");
        if (!string.Equals(ModelRevision, modelRevision, StringComparison.Ordinal) ||
            !string.Equals(TokenizerRevision, tokenizerRevision, StringComparison.Ordinal) ||
            !string.Equals(Primitive, primitive, StringComparison.Ordinal))
            throw new DecisionException("decision_calibration_out_of_scope", "Calibration profile does not match the loaded model.");
    }
}

public sealed record DecisionResourceBudget
{
    public int MaxQuestions { get; init; } = 32;
    public int MaxTokens { get; init; } = 4096;
    public TimeSpan Deadline { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (MaxQuestions <= 0 || MaxTokens <= 0 || Deadline <= TimeSpan.Zero || Deadline > TimeSpan.FromMinutes(5))
            throw new DecisionException("decision_budget_invalid", "Decision resource budget is invalid.");
    }
}
