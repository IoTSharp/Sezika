namespace Sezika;

public sealed record CalibrationExample(int Label, double[] Probabilities, string Language = "und", string Domain = "default");

public sealed record CalibrationMetrics
{
    public required int Count { get; init; }
    public required double Accuracy { get; init; }
    public required double MacroF1 { get; init; }
    public required double NegativeLogLikelihood { get; init; }
    public required double Brier { get; init; }
    public required double ExpectedCalibrationError { get; init; }
    public required double Coverage { get; init; }
    public required double SelectiveRisk { get; init; }
}

/// <summary>Reproducible metrics for a frozen evaluation split.</summary>
public static class CalibrationEvaluator
{
    public static CalibrationMetrics Evaluate(IReadOnlyList<CalibrationExample> examples, double abstainBelow = 0d, int bins = 10)
    {
        if (examples is null || examples.Count == 0 || bins is < 2 or > 100 || !double.IsFinite(abstainBelow) || abstainBelow < 0 || abstainBelow > 1)
            throw new ArgumentOutOfRangeException(nameof(examples));
        var classCount = examples[0].Probabilities.Length;
        if (classCount < 2) throw new DecisionException("calibration_input_invalid", "Calibration examples require at least two classes.");
        var confusion = new int[classCount, classCount];
        var nll = 0d;
        var brier = 0d;
        var covered = 0;
        var errors = 0;
        var binCount = new int[bins];
        var binConfidence = new double[bins];
        var binAccuracy = new double[bins];
        foreach (var example in examples)
        {
            if (example.Label < 0 || example.Label >= classCount || example.Probabilities.Length != classCount)
                throw new DecisionException("calibration_input_invalid", "Calibration label or probability dimensions are invalid.");
            var probabilities = example.Probabilities;
            var sum = probabilities.Sum();
            if (!double.IsFinite(sum) || Math.Abs(sum - 1) > 1e-6 || probabilities.Any(value => !double.IsFinite(value) || value < 0))
                throw new DecisionException("calibration_input_invalid", "Calibration probabilities must be finite and normalized.");
            var predicted = DecisionMath.ArgMax(probabilities);
            var confidence = probabilities[predicted];
            nll -= Math.Log(Math.Max(probabilities[example.Label], 1e-15));
            for (var i = 0; i < classCount; i++) brier += Math.Pow(probabilities[i] - (i == example.Label ? 1 : 0), 2);
            if (confidence >= abstainBelow)
            {
                covered++;
                if (predicted != example.Label) errors++;
                confusion[example.Label, predicted]++;
                var bin = Math.Min(bins - 1, (int)(confidence * bins));
                binCount[bin]++;
                binConfidence[bin] += confidence;
                binAccuracy[bin] += predicted == example.Label ? 1 : 0;
            }
        }
        var ece = 0d;
        for (var i = 0; i < bins; i++)
        {
            if (binCount[i] == 0) continue;
            ece += (double)binCount[i] / examples.Count * Math.Abs(binConfidence[i] / binCount[i] - binAccuracy[i] / binCount[i]);
        }
        var macroF1 = 0d;
        for (var c = 0; c < classCount; c++)
        {
            var truePositive = confusion[c, c];
            var falsePositive = 0;
            var falseNegative = 0;
            for (var row = 0; row < classCount; row++) if (row != c) falsePositive += confusion[row, c];
            for (var column = 0; column < classCount; column++) if (column != c) falseNegative += confusion[c, column];
            var precision = truePositive + falsePositive == 0 ? 0 : (double)truePositive / (truePositive + falsePositive);
            var recall = truePositive + falseNegative == 0 ? 0 : (double)truePositive / (truePositive + falseNegative);
            macroF1 += precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
        }
        return new CalibrationMetrics
        {
            Count = examples.Count,
            Accuracy = covered == 0 ? 0 : (double)(covered - errors) / covered,
            MacroF1 = macroF1 / classCount,
            NegativeLogLikelihood = nll / examples.Count,
            Brier = brier / examples.Count,
            ExpectedCalibrationError = ece,
            Coverage = (double)covered / examples.Count,
            SelectiveRisk = covered == 0 ? 0 : (double)errors / covered,
        };
    }

    public static double FitTemperature(IReadOnlyList<CalibrationExample> examples, double minimum = 0.05, double maximum = 5d, int steps = 200)
    {
        if (minimum <= 0 || maximum < minimum || steps < 2) throw new ArgumentOutOfRangeException(nameof(steps));
        var bestTemperature = 1d;
        var bestLoss = double.PositiveInfinity;
        for (var step = 0; step < steps; step++)
        {
            var temperature = minimum + (maximum - minimum) * step / (steps - 1d);
            var loss = 0d;
            foreach (var example in examples)
            {
                var logits = example.Probabilities.Select(value => (float)Math.Log(Math.Max(value, 1e-12))).ToArray();
                var probabilities = DecisionMath.Softmax(logits, temperature);
                loss -= Math.Log(Math.Max(probabilities[example.Label], 1e-15));
            }
            if (loss < bestLoss)
            {
                bestLoss = loss;
                bestTemperature = temperature;
            }
        }
        return bestTemperature;
    }
}
