namespace Sezika;

/// <summary>
/// A finite-sample interval for a scalar metric. The interval is an estimate
/// from the supplied split; it is not a release decision or a quality claim.
/// </summary>
public sealed record CalibrationConfidenceInterval
{
    public required int Count { get; init; }
    public required double Lower { get; init; }
    public required double Upper { get; init; }
    public required double ConfidenceLevel { get; init; }
}

/// <summary>Deterministic intervals used by calibration and quality reports.</summary>
public static class CalibrationConfidenceIntervals
{
    private const int MaxValues = 1_000_000;

    /// <summary>
    /// Wilson score interval for a binomial metric such as accuracy or
    /// execution coverage. This remains defined for zero successes and zero
    /// failures, unlike a normal approximation.
    /// </summary>
    public static CalibrationConfidenceInterval Wilson(int successes, int trials, double confidenceLevel = 0.95)
    {
        ValidateCounts(successes, trials);
        ValidateConfidence(confidenceLevel);
        var z = StandardNormalQuantile((1d + confidenceLevel) / 2d);
        var n = (double)trials;
        var p = trials == 0 ? 0d : successes / n;
        var z2 = z * z;
        var denominator = 1d + z2 / n;
        var centre = (p + z2 / (2d * n)) / denominator;
        var margin = z * Math.Sqrt((p * (1d - p) / n) + z2 / (4d * n * n)) / denominator;
        return new CalibrationConfidenceInterval
        {
            Count = trials,
            Lower = Math.Max(0d, centre - margin),
            Upper = Math.Min(1d, centre + margin),
            ConfidenceLevel = confidenceLevel,
        };
    }

    /// <summary>
    /// Normal-approximation interval for a finite metric mean. The sample
    /// standard deviation is used when at least two values are available;
    /// one value produces a zero-width interval and remains explicitly small.
    /// </summary>
    public static CalibrationConfidenceInterval Mean(IReadOnlyList<double> values, double confidenceLevel = 0.95)
    {
        if (values is null || values.Count is < 1 or > MaxValues) throw new ArgumentException("Metric values are outside the bounded range.", nameof(values));
        ValidateConfidence(confidenceLevel);
        var z = StandardNormalQuantile((1d + confidenceLevel) / 2d);
        var mean = 0d;
        var sumSquares = 0d;
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (!double.IsFinite(value)) throw new ArgumentException("Metric values must be finite.", nameof(values));
            var delta = value - mean;
            mean += delta / (index + 1d);
            sumSquares += delta * (value - mean);
            if (!double.IsFinite(mean) || !double.IsFinite(sumSquares))
                throw new ArgumentException("Metric mean or variance exceeds the finite numerical range.", nameof(values));
        }
        var standardError = values.Count < 2 ? 0d : Math.Sqrt(sumSquares / (values.Count - 1d) / values.Count);
        var margin = z * standardError;
        if (!double.IsFinite(margin) || !double.IsFinite(mean - margin) || !double.IsFinite(mean + margin))
            throw new ArgumentException("Metric confidence interval exceeds the finite numerical range.", nameof(values));
        return new CalibrationConfidenceInterval
        {
            Count = values.Count,
            Lower = mean - margin,
            Upper = mean + margin,
            ConfidenceLevel = confidenceLevel,
        };
    }

    private static void ValidateCounts(int successes, int trials)
    {
        if (trials < 1 || successes < 0 || successes > trials)
            throw new ArgumentOutOfRangeException(nameof(successes), "Successes must be within a non-empty trial count.");
    }

    private static void ValidateConfidence(double confidenceLevel)
    {
        if (!double.IsFinite(confidenceLevel) || confidenceLevel <= 0d || confidenceLevel >= 1d)
            throw new ArgumentOutOfRangeException(nameof(confidenceLevel));
    }

    private static double StandardNormalQuantile(double probability)
    {
        if (!double.IsFinite(probability) || probability <= 0d || probability >= 1d)
            throw new ArgumentOutOfRangeException(nameof(probability));
        // Acklam's rational approximation, with absolute error below 4.5e-4.
        ReadOnlySpan<double> a = [-39.6968302866538, 220.946098424521, -275.928510446969, 138.357751867269, -30.6647980661472, 2.50662827745924];
        ReadOnlySpan<double> b = [-54.4760987982241, 161.585836858041, -155.698979859887, 66.8013118877197, -13.2806815528857];
        ReadOnlySpan<double> c = [-0.00778489400243029, -0.322396458041136, -2.40075827716184, -2.54973253934373, 4.37466414146497, 2.93816398269878];
        ReadOnlySpan<double> d = [0.00778469570904146, 0.32246712907004, 2.445134137143, 3.75440866190742];
        const double low = 0.02425;
        const double high = 1d - low;
        if (probability < low)
        {
            var q = Math.Sqrt(-2d * Math.Log(probability));
            return Evaluate(c, q) / (Evaluate(d, q) * q + 1d);
        }
        if (probability > high)
        {
            var q = Math.Sqrt(-2d * Math.Log(1d - probability));
            return -Evaluate(c, q) / (Evaluate(d, q) * q + 1d);
        }
        var qCentral = probability - 0.5;
        var r = qCentral * qCentral;
        return Evaluate(a, r) * qCentral / (Evaluate(b, r) * r + 1d);
    }

    private static double Evaluate(ReadOnlySpan<double> coefficients, double value)
    {
        var result = 0d;
        for (var index = 0; index < coefficients.Length; index++) result = result * value + coefficients[index];
        return result;
    }
}
