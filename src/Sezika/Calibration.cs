using System.Diagnostics;

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
    public int CoveredCount { get; init; }
    public double? SelectiveAccuracy { get; init; }
    public double? NegativeClassRecall { get; init; }
    public double? ScoreMae { get; init; }
}

/// <summary>Reproducible metrics for a frozen evaluation split.</summary>
public static class CalibrationEvaluator
{
    private const int MaxExamples = 1_000_000;

    public static CalibrationMetrics Evaluate(IReadOnlyList<CalibrationExample> examples, double abstainBelow = 0d, int bins = 10,
        string primitive = "choice", CancellationToken cancellationToken = default, TimeSpan? maxDuration = null,
        Action<int, int>? progress = null)
    {
        if (examples is null || examples.Count is < 1 or > MaxExamples || bins is < 2 or > 100 || !double.IsFinite(abstainBelow) || abstainBelow < 0 || abstainBelow > 1)
            throw new ArgumentOutOfRangeException(nameof(examples));
        if (primitive is not ("choice" or "score" or "boolean")) throw new ArgumentOutOfRangeException(nameof(primitive));
        var duration = Duration(maxDuration);
        var watch = Stopwatch.StartNew();
        CheckBudget(watch, duration, cancellationToken);
        var classCount = ClassCount(examples[0]);
        if (primitive == "boolean" && classCount != 2)
            throw new DecisionException("calibration_input_invalid", "Boolean evaluation requires false/true class order.");
        var confusion = new int[classCount, classCount];
        var nll = 0d;
        var brier = 0d;
        var covered = 0;
        var errors = 0;
        var correct = 0;
        var scoreMae = 0d;
        var binCount = new int[bins];
        var binConfidence = new double[bins];
        var binAccuracy = new double[bins];
        var completed = 0;
        progress?.Invoke(0, examples.Count);
        foreach (var example in examples)
        {
            CheckBudget(watch, duration, cancellationToken);
            ValidateExample(example, classCount);
            var probabilities = example.Probabilities;
            var predicted = DecisionMath.ArgMax(probabilities);
            var confidence = probabilities[predicted];
            if (predicted == example.Label) correct++;
            confusion[example.Label, predicted]++;
            nll -= Math.Log(Math.Max(probabilities[example.Label], 1e-15));
            var expectedScore = 0d;
            for (var i = 0; i < classCount; i++)
            {
                brier += Math.Pow(probabilities[i] - (i == example.Label ? 1 : 0), 2);
                expectedScore += i * probabilities[i];
            }
            scoreMae += Math.Abs(expectedScore - example.Label);
            var bin = Math.Min(bins - 1, (int)(confidence * bins));
            binCount[bin]++;
            binConfidence[bin] += confidence;
            binAccuracy[bin] += predicted == example.Label ? 1 : 0;
            if (confidence >= abstainBelow)
            {
                covered++;
                if (predicted != example.Label) errors++;
            }
            if (++completed % 1024 == 0) progress?.Invoke(completed, examples.Count);
        }
        var ece = 0d;
        for (var i = 0; i < bins; i++)
        {
            if (binCount[i] == 0) continue;
            ece += (double)binCount[i] / examples.Count * Math.Abs(binConfidence[i] / binCount[i] - binAccuracy[i] / binCount[i]);
        }
        var macroF1 = 0d;
        var negatives = 0;
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
            if (c == 0) negatives = truePositive + falseNegative;
        }
        CheckBudget(watch, duration, cancellationToken);
        progress?.Invoke(examples.Count, examples.Count);
        return new CalibrationMetrics
        {
            Count = examples.Count,
            Accuracy = (double)correct / examples.Count,
            MacroF1 = macroF1 / classCount,
            NegativeLogLikelihood = nll / examples.Count,
            Brier = brier / examples.Count,
            ExpectedCalibrationError = ece,
            Coverage = (double)covered / examples.Count,
            SelectiveRisk = covered == 0 ? 0 : (double)errors / covered,
            CoveredCount = covered,
            SelectiveAccuracy = covered == 0 ? null : (double)(covered - errors) / covered,
            NegativeClassRecall = primitive == "boolean" && negatives > 0 ? (double)confusion[0, 0] / negatives : null,
            ScoreMae = primitive == "score" ? scoreMae / examples.Count : null,
        };
    }

    public static double FitTemperature(IReadOnlyList<CalibrationExample> examples, double minimum = 0.05, double maximum = 5d, int steps = 200,
        CancellationToken cancellationToken = default, TimeSpan? maxDuration = null, Action<int, int>? progress = null)
    {
        if (examples is null || examples.Count is < 1 or > MaxExamples) throw new ArgumentException("Calibration examples are outside the bounded range.", nameof(examples));
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum <= 0 || maximum < minimum || steps is < 2 or > 10_000 ||
            (long)examples.Count * (steps + 1) > 20_000_000)
            throw new ArgumentOutOfRangeException(nameof(steps));
        var duration = Duration(maxDuration);
        var watch = Stopwatch.StartNew();
        CheckBudget(watch, duration, cancellationToken);
        var classCount = ClassCount(examples[0]);
        foreach (var example in examples)
        {
            CheckBudget(watch, duration, cancellationToken);
            ValidateExample(example, classCount);
        }
        var bestTemperature = minimum;
        var bestLoss = double.PositiveInfinity;
        // Include the identity candidate to avoid grid-only NLL regression.
        var total = steps + (minimum <= 1d && maximum >= 1d ? 1 : 0);
        progress?.Invoke(0, total);
        for (var step = 0; step < total; step++)
        {
            CheckBudget(watch, duration, cancellationToken);
            var temperature = step == steps ? 1d : minimum + (maximum - minimum) * (step / (steps - 1d));
            var loss = 0d;
            foreach (var example in examples)
            {
                CheckBudget(watch, duration, cancellationToken);
                var maximumLog = Math.Log(example.Probabilities.Max());
                var sum = 0d;
                for (var i = 0; i < classCount; i++)
                    sum += example.Probabilities[i] == 0 ? 0 : Math.Exp((Math.Log(example.Probabilities[i]) - maximumLog) / temperature);
                var labelProbability = example.Probabilities[example.Label] == 0 ? 0 :
                    Math.Exp((Math.Log(example.Probabilities[example.Label]) - maximumLog) / temperature) / sum;
                loss -= Math.Log(Math.Max(labelProbability, 1e-15));
            }
            if (loss < bestLoss)
            {
                bestLoss = loss;
                bestTemperature = temperature;
            }
            progress?.Invoke(step + 1, total);
        }
        return bestTemperature;
    }

    private static int ClassCount(CalibrationExample? example)
    {
        if (example?.Probabilities is null || example.Probabilities.Length is < 2 or > 32)
            throw new DecisionException("calibration_input_invalid", "Calibration requires 2..32 classes.");
        return example.Probabilities.Length;
    }

    private static void ValidateExample(CalibrationExample? example, int classCount)
    {
        if (example?.Probabilities is null || example.Label < 0 || example.Label >= classCount || example.Probabilities.Length != classCount)
            throw new DecisionException("calibration_input_invalid", "Calibration label or probability dimensions are invalid.");
        var sum = example.Probabilities.Sum();
        if (!double.IsFinite(sum) || Math.Abs(sum - 1d) > 1e-6 || example.Probabilities.Any(value => !double.IsFinite(value) || value < 0d || value > 1d))
            throw new DecisionException("calibration_input_invalid", "Calibration probabilities must be finite and normalized.");
    }

    private static TimeSpan Duration(TimeSpan? requested)
    {
        var duration = requested ?? TimeSpan.FromSeconds(60);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(requested));
        return duration;
    }

    private static void CheckBudget(Stopwatch watch, TimeSpan duration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (watch.Elapsed >= duration) throw new TimeoutException("Calibration wall-clock budget exceeded.");
    }
}
