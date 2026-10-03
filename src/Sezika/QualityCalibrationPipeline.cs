using System.Diagnostics;

namespace Sezika;

/// <summary>
/// One already materialized model prediction.  The split is part of the
/// record so the evaluator cannot silently fit on sealed-test rows.
/// </summary>
public sealed record QualityCalibrationExample(
    string Id,
    string Split,
    int Label,
    double[] Probabilities,
    string Language = "und",
    string Domain = "default");

/// <summary>Hash and count commitments for the exact prediction artifact.</summary>
public sealed record QualityCalibrationManifest
{
    public required int SchemaVersion { get; init; }
    public required string DatasetId { get; init; }
    public required string Status { get; init; }
    public required bool RepresentsModelQuality { get; init; }
    public required int RecordCount { get; init; }
    public required int CalibrationCount { get; init; }
    public required int SealedTestCount { get; init; }
    public required double AbstainBelow { get; init; }
    /// <summary>Optional external binding. A manifest cannot self-hash its own bytes.</summary>
    public string? ManifestSha256 { get; init; }
    public required string RecordsSha256 { get; init; }
    public required string CalibrationSplitSha256 { get; init; }
    public required string SealedTestSplitSha256 { get; init; }
}

/// <summary>Identity of the model and prompt used to produce predictions.</summary>
public sealed record QualityCalibrationIdentity
{
    public required string ModelId { get; init; }
    public required string ModelRevision { get; init; }
    public required string ModelWeightsSha256 { get; init; }
    public required string TokenizerRevision { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required string PromptSchemaId { get; init; }
    public required string PromptSchemaSha256 { get; init; }
}

public sealed record QualityCalibrationIntervals
{
    public required CalibrationConfidenceInterval Accuracy { get; init; }
    public required CalibrationConfidenceInterval Coverage { get; init; }
}

public sealed record QualityCalibrationGateResult
{
    public required string Status { get; init; }
    public required string[] Failures { get; init; }
}

/// <summary>
/// Deterministic report produced from one hash-bound prediction artifact.
/// A report whose input manifest is not accepted for quality is explicitly
/// measurement-only and cannot be interpreted as model-quality evidence.
/// </summary>
public sealed record QualityCalibrationReport
{
    public required int SchemaVersion { get; init; }
    public required string DatasetId { get; init; }
    public required string Primitive { get; init; }
    public required QualityCalibrationIdentity Identity { get; init; }
    public required QualityCalibrationManifest Manifest { get; init; }
    public required string ObservedManifestSha256 { get; init; }
    public required int RecordCount { get; init; }
    public required int CalibrationCount { get; init; }
    public required int SealedTestCount { get; init; }
    public required double AbstainBelow { get; init; }
    public required double Temperature { get; init; }
    public required CalibrationMetrics Uncalibrated { get; init; }
    public required CalibrationMetrics Calibrated { get; init; }
    public required double RandomBaselineAccuracy { get; init; }
    public required double MajorityBaselineAccuracy { get; init; }
    public required double AccuracyDeltaOverRandom { get; init; }
    public required double AccuracyDeltaOverMajority { get; init; }
    public required double NegativeLogLikelihoodDeltaOverUncalibrated { get; init; }
    public required double BrierDeltaOverUncalibrated { get; init; }
    public required QualityCalibrationIntervals Intervals { get; init; }
    public required QualityCalibrationGateResult Gate { get; init; }
    public required DateTimeOffset GeneratedUtc { get; init; }
}

/// <summary>Hash-bound calibration fitting and sealed-test evaluation.</summary>
public static class QualityCalibrationPipeline
{
    private const int MaxExamples = 1_000_000;
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(10);

    public static QualityCalibrationReport Run(
        QualityCalibrationManifest manifest,
        IReadOnlyList<QualityCalibrationExample> examples,
        QualityCalibrationIdentity identity,
        string actualManifestSha256,
        string actualRecordsSha256,
        string actualCalibrationSplitSha256,
        string actualSealedTestSplitSha256,
        string primitive,
        CalibrationGates gates,
        double minimumTemperature = 0.05,
        double maximumTemperature = 5d,
        int temperatureSteps = 200,
        CancellationToken cancellationToken = default,
        TimeSpan? maxDuration = null,
        Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(examples);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(gates);
        if (examples.Count is < 2 or > MaxExamples) throw Failure("quality_input_invalid", "Prediction count is outside the bounded range.");
        if (primitive is not ("choice" or "score" or "boolean")) throw Failure("quality_input_invalid", "Primitive is unsupported.");
        ValidateHash(actualManifestSha256, nameof(actualManifestSha256));
        ValidateHash(actualRecordsSha256, nameof(actualRecordsSha256));
        ValidateHash(actualCalibrationSplitSha256, nameof(actualCalibrationSplitSha256));
        ValidateHash(actualSealedTestSplitSha256, nameof(actualSealedTestSplitSha256));
        ValidateIdentity(identity);
        ValidateManifest(manifest, actualManifestSha256, actualRecordsSha256,
            actualCalibrationSplitSha256, actualSealedTestSplitSha256);
        gates.Validate();
        var duration = maxDuration ?? DefaultDuration;
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(maxDuration));
        var watch = Stopwatch.StartNew();
        CheckBudget(watch, duration, cancellationToken);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var calibration = new List<CalibrationExample>(manifest.CalibrationCount);
        var sealedTest = new List<CalibrationExample>(manifest.SealedTestCount);
        var expectedClassCount = 0;
        for (var index = 0; index < examples.Count; index++)
        {
            CheckBudget(watch, duration, cancellationToken);
            var example = examples[index] ?? throw Failure("quality_input_invalid", "Prediction row is null.");
            if (string.IsNullOrWhiteSpace(example.Id) || example.Id.Length > 256 || !ids.Add(example.Id))
                throw Failure("quality_input_invalid", "Prediction IDs must be non-empty and unique.");
            if (example.Split is not ("calibration" or "sealed_test"))
                throw Failure("quality_split_invalid", "Only calibration and sealed_test rows are accepted.");
            if (string.IsNullOrWhiteSpace(example.Language) || example.Language.Length > 16 ||
                string.IsNullOrWhiteSpace(example.Domain) || example.Domain.Length > 128)
                throw Failure("quality_input_invalid", "Language and domain labels are bounded and required.");
            expectedClassCount = expectedClassCount == 0 ? ClassCount(example.Probabilities) : expectedClassCount;
            ValidateExample(example, expectedClassCount);
            var converted = new CalibrationExample(example.Label, example.Probabilities, example.Language, example.Domain);
            if (example.Split == "calibration") calibration.Add(converted); else sealedTest.Add(converted);
            progress?.Invoke(index + 1, examples.Count);
        }
        if (calibration.Count != manifest.CalibrationCount || sealedTest.Count != manifest.SealedTestCount)
            throw Failure("quality_split_count_mismatch", "Observed split counts do not match the manifest.");
        if (calibration.Count == 0 || sealedTest.Count == 0)
            throw Failure("quality_split_invalid", "Both calibration and sealed_test must be non-empty.");

        var temperature = CalibrationEvaluator.FitTemperature(calibration, minimumTemperature, maximumTemperature,
            temperatureSteps, cancellationToken, duration - watch.Elapsed, progress: null);
        CheckBudget(watch, duration, cancellationToken);
        var calibratedExamples = new CalibrationExample[sealedTest.Count];
        var calibratedCorrect = 0;
        var labelCounts = new int[expectedClassCount];
        for (var index = 0; index < sealedTest.Count; index++)
        {
            CheckBudget(watch, duration, cancellationToken);
            var example = sealedTest[index];
            var calibratedProbabilities = CalibrationEvaluator.ApplyTemperature(example.Probabilities, temperature);
            calibratedExamples[index] = new CalibrationExample(example.Label, calibratedProbabilities, example.Language, example.Domain);
            if (DecisionMath.ArgMax(calibratedProbabilities) == example.Label) calibratedCorrect++;
            labelCounts[example.Label]++;
        }
        var uncalibrated = CalibrationEvaluator.Evaluate(sealedTest, abstainBelow: manifest.AbstainBelow, primitive: primitive,
            cancellationToken: cancellationToken, maxDuration: duration - watch.Elapsed);
        CheckBudget(watch, duration, cancellationToken);
        var calibrated = CalibrationEvaluator.Evaluate(calibratedExamples, abstainBelow: manifest.AbstainBelow, primitive: primitive,
            cancellationToken: cancellationToken, maxDuration: duration - watch.Elapsed);
        var random = 1d / expectedClassCount;
        var majority = labelCounts.Max() / (double)sealedTest.Count;
        var intervals = new QualityCalibrationIntervals
        {
            Accuracy = CalibrationConfidenceIntervals.Wilson(calibratedCorrect, sealedTest.Count, gates.ConfidenceLevel),
            Coverage = CalibrationConfidenceIntervals.Wilson(calibrated.CoveredCount, sealedTest.Count, gates.ConfidenceLevel),
        };
        var gate = EvaluateGate(manifest, gates, primitive, sealedTest.Count, calibrated,
            calibrated.Accuracy - random, calibrated.Accuracy - majority,
            calibrated.NegativeLogLikelihood - uncalibrated.NegativeLogLikelihood,
            calibrated.Brier - uncalibrated.Brier);
        return new QualityCalibrationReport
        {
            SchemaVersion = 1,
            DatasetId = manifest.DatasetId, Primitive = primitive, Identity = identity, Manifest = manifest,
            ObservedManifestSha256 = actualManifestSha256,
            RecordCount = examples.Count, CalibrationCount = calibration.Count, SealedTestCount = sealedTest.Count,
            AbstainBelow = manifest.AbstainBelow,
            Temperature = temperature, Uncalibrated = uncalibrated, Calibrated = calibrated,
            RandomBaselineAccuracy = random, MajorityBaselineAccuracy = majority,
            AccuracyDeltaOverRandom = calibrated.Accuracy - random, AccuracyDeltaOverMajority = calibrated.Accuracy - majority,
            NegativeLogLikelihoodDeltaOverUncalibrated = calibrated.NegativeLogLikelihood - uncalibrated.NegativeLogLikelihood,
            BrierDeltaOverUncalibrated = calibrated.Brier - uncalibrated.Brier, Intervals = intervals, Gate = gate,
            GeneratedUtc = DateTimeOffset.UtcNow,
        };
    }

    private static QualityCalibrationGateResult EvaluateGate(QualityCalibrationManifest manifest, CalibrationGates gates,
        string primitive, int count, CalibrationMetrics metrics, double randomDelta, double majorityDelta, double nllDelta, double brierDelta)
    {
        if (!manifest.RepresentsModelQuality)
            return new QualityCalibrationGateResult { Status = "not_eligible", Failures = ["manifest_not_eligible_for_quality"] };
        var failures = new List<string>();
        if (count < gates.MinimumTestExamples) failures.Add("minimum_test_examples");
        if (randomDelta < gates.MinimumAccuracyDeltaOverRandom) failures.Add("accuracy_delta_over_random");
        if (majorityDelta < gates.MinimumAccuracyDeltaOverRandom) failures.Add("accuracy_delta_over_majority");
        if (nllDelta > gates.MaximumNllIncreaseOverUncalibrated) failures.Add("nll_delta_over_uncalibrated");
        if (brierDelta > gates.MaximumBrierIncreaseOverUncalibrated) failures.Add("brier_delta_over_uncalibrated");
        if (metrics.ExpectedCalibrationError > gates.MaximumExpectedCalibrationError) failures.Add("expected_calibration_error");
        if (metrics.Coverage < gates.MinimumCoverage) failures.Add("coverage");
        if (metrics.SelectiveRisk > gates.MaximumSelectiveRisk) failures.Add("selective_risk");
        if (primitive == "score")
        {
            if (metrics.ScoreMae is null || metrics.ScoreMae.Value > gates.MaximumScoreMae) failures.Add("score_mae");
        }
        return new QualityCalibrationGateResult { Status = failures.Count == 0 ? "passed" : "failed", Failures = failures.ToArray() };
    }

    private static void ValidateManifest(QualityCalibrationManifest manifest, string actualManifest, string actualRecords,
        string actualCalibration, string actualSealed)
    {
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.DatasetId) || manifest.DatasetId.Length > 128 ||
            manifest.Status is not ("fixture_only" or "accepted_for_quality") || manifest.RecordCount < 2 ||
            manifest.CalibrationCount < 1 || manifest.SealedTestCount < 1 || manifest.RecordCount != manifest.CalibrationCount + manifest.SealedTestCount ||
            !double.IsFinite(manifest.AbstainBelow) || manifest.AbstainBelow < 0d || manifest.AbstainBelow > 1d)
            throw Failure("quality_manifest_invalid", "Manifest schema, status or fixed counts are invalid.");
        if (manifest.RepresentsModelQuality && manifest.Status != "accepted_for_quality")
            throw Failure("quality_manifest_invalid", "A quality claim requires accepted_for_quality status.");
        if (manifest.ManifestSha256 is not null && !actualManifest.Equals(manifest.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw Failure("quality_hash_mismatch", "The manifest SHA-256 does not match the manifest binding.");
        foreach (var (actual, expected, name) in new[]
        {
            (actualRecords, manifest.RecordsSha256, "records"),
            (actualCalibration, manifest.CalibrationSplitSha256, "calibration split"), (actualSealed, manifest.SealedTestSplitSha256, "sealed_test split"),
        })
        {
            ValidateHash(expected, name);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw Failure("quality_hash_mismatch", $"The {name} SHA-256 does not match the manifest.");
        }
    }

    private static void ValidateIdentity(QualityCalibrationIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.ModelId) || string.IsNullOrWhiteSpace(identity.ModelRevision) ||
            string.IsNullOrWhiteSpace(identity.TokenizerRevision) || string.IsNullOrWhiteSpace(identity.PromptSchemaId) ||
            identity.ModelId.Length > 128 || identity.ModelRevision.Length > 128 ||
            identity.TokenizerRevision.Length > 128 || identity.PromptSchemaId.Length > 128)
            throw Failure("quality_identity_invalid", "Model, tokenizer and prompt identities are required.");
        ValidateHash(identity.ModelWeightsSha256, nameof(identity.ModelWeightsSha256));
        ValidateHash(identity.TokenizerSha256, nameof(identity.TokenizerSha256));
        ValidateHash(identity.PromptSchemaSha256, nameof(identity.PromptSchemaSha256));
    }

    private static int ClassCount(double[]? probabilities)
    {
        if (probabilities is null || probabilities.Length is < 2 or > 32) throw Failure("quality_input_invalid", "Class count must be 2..32.");
        return probabilities.Length;
    }

    private static void ValidateExample(QualityCalibrationExample example, int classCount)
    {
        if (example.Label < 0 || example.Label >= classCount || example.Probabilities is null || example.Probabilities.Length != classCount)
            throw Failure("quality_input_invalid", "Labels and probability dimensions must match every row.");
        var sum = 0d;
        foreach (var probability in example.Probabilities)
        {
            if (!double.IsFinite(probability) || probability < 0d || probability > 1d)
                throw Failure("quality_input_invalid", "Probabilities must be finite and in [0,1].");
            sum += probability;
            if (!double.IsFinite(sum)) throw Failure("quality_input_invalid", "Probability sum is non-finite.");
        }
        if (Math.Abs(sum - 1d) > 1e-6) throw Failure("quality_input_invalid", "Probabilities must sum to one.");
    }

    private static void ValidateHash(string? hash, string name)
    {
        if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw Failure("quality_hash_invalid", $"{name} must be a SHA-256 hex digest.");
    }

    private static DecisionException Failure(string code, string message) => new(code, message);

    private static void CheckBudget(Stopwatch watch, TimeSpan duration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (watch.Elapsed >= duration) throw new TimeoutException("Quality calibration wall-clock budget exceeded.");
    }
}
