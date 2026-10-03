using System.Text.Json;
using Sezika;

namespace Sezika.Tests;

internal static class CalibrationContinuationChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string, string> expectCode)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CalibrationExample[] examples = [new(0, [0.9, 0.1]), new(1, [0.6, 0.4])];
        var full = CalibrationEvaluator.Evaluate(examples, primitive: "boolean", cancellationToken: deadline.Token);
        var selected = CalibrationEvaluator.Evaluate(examples, 0.8, primitive: "boolean", cancellationToken: deadline.Token);
        check(full.Accuracy == 0.5 && selected.Accuracy == full.Accuracy && selected.MacroF1 == full.MacroF1,
            "calibration abstention preserves full-split accuracy and macro F1");
        check(Math.Abs(full.ExpectedCalibrationError - 0.35) < 1e-12 && full.ExpectedCalibrationError == selected.ExpectedCalibrationError,
            "calibration ECE includes abstained examples in frozen denominator");
        check(selected.CoveredCount == 1 && selected.Coverage == 0.5 && selected.SelectiveAccuracy == 1 && selected.SelectiveRisk == 0,
            "calibration covered count and selective metrics use answered denominator");
        check(selected.NegativeClassRecall == 1,
            "Boolean negative recall uses all false gold examples");
        var none = CalibrationEvaluator.Evaluate(examples, 1, cancellationToken: deadline.Token);
        check(none.CoveredCount == 0 && none.SelectiveAccuracy is null && none.Accuracy == 0.5,
            "zero coverage exposes undefined selective accuracy");
        var missingNegative = CalibrationEvaluator.Evaluate([new(1, [0.2, 0.8])], primitive: "boolean", cancellationToken: deadline.Token);
        check(missingNegative.NegativeClassRecall is null,
            "Boolean no-negative slice exposes undefined negative recall");
        var ordinal = CalibrationEvaluator.Evaluate([new(2, [0.1, 0.2, 0.7])], primitive: "score", cancellationToken: deadline.Token);
        check(Math.Abs(ordinal.ScoreMae!.Value - 0.4) < 1e-12,
            "Score MAE uses ordinal expected value rather than argmax");
        expectCode(() => CalibrationEvaluator.Evaluate([new(0, null!)]), "calibration_input_invalid", "null probability array is structured calibration error");
        expectCode(() => CalibrationEvaluator.Evaluate([null!]), "calibration_input_invalid", "null example is structured calibration error");
        expectCode(() => CalibrationEvaluator.Evaluate([new(0, [0.4, 0.3, 0.3])], primitive: "boolean"),
            "calibration_input_invalid", "Boolean rejects nonbinary class count");
        expectCode(() => CalibrationEvaluator.Evaluate([new(0, [double.NaN, 1])]),
            "calibration_input_invalid", "nonfinite probability is structured calibration error");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Expect<OperationCanceledException>(() => CalibrationEvaluator.Evaluate(examples, cancellationToken: cancelled.Token), check, "calibration evaluation cancellation");
        Expect<TimeoutException>(() => CalibrationEvaluator.Evaluate(examples, maxDuration: TimeSpan.FromTicks(1)), check, "calibration evaluation wall-clock limit");
        Expect<ArgumentOutOfRangeException>(() => CalibrationEvaluator.Evaluate(examples, maxDuration: TimeSpan.FromMinutes(11)), check, "calibration evaluation rejects unbounded duration");

        var progress = new List<(int Done, int Total)>();
        // Tiny bounded run first: 2 examples x (2 grid candidates + identity).
        var temperature = CalibrationEvaluator.FitTemperature([new(0, [0.9, 0.1]), new(1, [0.9, 0.1])],
            minimum: 0.05, maximum: 1.01, steps: 2, cancellationToken: deadline.Token,
            progress: (done, total) => progress.Add((done, total)));
        check(temperature == 1.01 && progress.Count == 4 && progress[^1] == (3, 3),
            "temperature search bounded tiny trial reports complete progress");
        var identity = CalibrationEvaluator.FitTemperature([new(0, [0.75, 0.25]), new(0, [0.75, 0.25]),
            new(0, [0.75, 0.25]), new(1, [0.75, 0.25])],
            minimum: 0.8, maximum: 1.2, steps: 2, cancellationToken: deadline.Token);
        check(identity == 1, "temperature search includes identity omitted by coarse grid");
        var zeroTemperature = CalibrationEvaluator.FitTemperature([new(0, [1, 0])],
            minimum: double.Epsilon, maximum: double.Epsilon, steps: 2, cancellationToken: deadline.Token);
        check(zeroTemperature == double.Epsilon, "temperature search preserves exact-zero support at finite extreme temperature");
        Expect<OperationCanceledException>(() => CalibrationEvaluator.FitTemperature(examples, cancellationToken: cancelled.Token), check, "temperature fitting cancellation");
        Expect<TimeoutException>(() => CalibrationEvaluator.FitTemperature(examples, maxDuration: TimeSpan.FromTicks(1)), check, "temperature fitting wall-clock limit");
        Expect<ArgumentException>(() => CalibrationConfidenceIntervals.Mean([double.MaxValue, -double.MaxValue]), check,
            "confidence interval rejects overflowing finite aggregate");

        var manifest = JsonSerializer.Deserialize(File.ReadAllBytes("data/s4-03/calibration-profiles.json"),
            DecisionJsonContext.Default.CalibrationProfileManifest)!;
        var entry = manifest.Profiles[0];
        var binding = new CalibrationProfileBinding
        {
            ModelId = entry.ModelId, ModelRevision = entry.ModelRevision, ModelWeightsSha256 = entry.ModelWeightsSha256,
            TokenizerRevision = entry.TokenizerRevision, TokenizerSha256 = entry.TokenizerSha256,
            PromptSchemaId = entry.PromptSchemaId, PromptSchemaSha256 = entry.PromptSchemaSha256,
            DatasetManifestSha256 = entry.DatasetManifestSha256,
        };
        var passing = new CalibrationMetricSnapshot
        {
            Count = 100, Accuracy = 0.9, MacroF1 = 0.9, NegativeLogLikelihood = 0.1,
            Brier = 0.1, ExpectedCalibrationError = 0.01, Coverage = 1, SelectiveRisk = 0.1,
            AccuracyDeltaOverRandom = 0.4, AccuracyDeltaOverMajority = 0.1,
            NegativeLogLikelihoodDeltaOverUncalibrated = -0.01, BrierDeltaOverUncalibrated = -0.01,
            EvaluationSplit = "sealed_test", ScoreMae = 0.2,
        };
        var verified = entry with { Status = "verified", FitStatus = "fitted", ObservedMetrics = passing };
        verified.Validate(binding, manifest.FrozenGates);
        check(true, "complete synthetic verified snapshot exercises gates without certifying real model");
        expectCode(() => (verified with { ObservedMetrics = passing with { AccuracyDeltaOverRandom = null } }).Validate(binding, manifest.FrozenGates),
            "decision_calibration_not_ready", "verified profile requires random baseline delta");
        expectCode(() => (verified with { ObservedMetrics = passing with { AccuracyDeltaOverMajority = null } }).Validate(binding, manifest.FrozenGates),
            "decision_calibration_not_ready", "verified profile requires majority baseline delta");
        expectCode(() => (verified with { ObservedMetrics = passing with { NegativeLogLikelihoodDeltaOverUncalibrated = double.NaN } }).Validate(binding, manifest.FrozenGates),
            "decision_calibration_metrics_invalid", "nonfinite delta cannot bypass profile quality gate");
        expectCode(() => (verified with { ObservedMetrics = passing with { EvaluationSplit = "calibration" } }).Validate(binding, manifest.FrozenGates),
            "decision_calibration_not_ready", "fit split cannot replace sealed evaluation split");
        expectCode(() => (verified with { Primitive = "score", ObservedMetrics = passing with { ScoreMae = null } }).Validate(binding, manifest.FrozenGates),
            "decision_calibration_not_ready", "verified Score requires MAE");
        expectCode(() => (verified with { ObservedMetrics = passing with { AccuracyDeltaOverMajority = 0 } }).Validate(binding, manifest.FrozenGates),
            "decision_calibration_quality_gate_failed", "majority baseline delta must meet frozen gate");
        (verified with { Status = "rejected", ObservedMetrics = passing with { Count = 1, AccuracyDeltaOverRandom = -0.4 } }).Validate(binding, manifest.FrozenGates);
        check(true, "rejected profile retains finite failed measurements for audit");
        expectCode(() => (manifest with { Status = "verified" }).Validate(binding),
            "decision_calibration_not_ready", "verified manifest cannot contain pending profiles");

        var qualityRows = new QualityCalibrationExample[]
        {
            new("cal-1", "calibration", 0, [0.9, 0.1], "en", "general"),
            new("cal-2", "calibration", 1, [0.1, 0.9], "en", "general"),
            new("seal-1", "sealed_test", 0, [0.8, 0.2], "en", "general"),
            new("seal-2", "sealed_test", 1, [0.2, 0.8], "en", "general"),
        };
        var qualityManifest = new QualityCalibrationManifest
        {
            SchemaVersion = 1, DatasetId = "quality-contract", Status = "fixture_only", RepresentsModelQuality = false,
            RecordCount = 4, CalibrationCount = 2, SealedTestCount = 2,
            AbstainBelow = 0,
            RecordsSha256 = new string('a', 64), CalibrationSplitSha256 = new string('b', 64), SealedTestSplitSha256 = new string('c', 64),
        };
        var qualityIdentity = new QualityCalibrationIdentity
        {
            ModelId = "contract-model", ModelRevision = "r1", ModelWeightsSha256 = new string('d', 64),
            TokenizerRevision = "r1", TokenizerSha256 = new string('e', 64), PromptSchemaId = "prompt.v1", PromptSchemaSha256 = new string('f', 64),
        };
        var qualityGates = new CalibrationGates
        {
            MinimumTestExamples = 2, MinimumAccuracyDeltaOverRandom = 0, MaximumNllIncreaseOverUncalibrated = 1,
            MaximumBrierIncreaseOverUncalibrated = 1, MaximumExpectedCalibrationError = 1, MinimumCoverage = 0,
            MaximumSelectiveRisk = 1, MaximumScoreMae = 1, ConfidenceLevel = 0.95,
        };
        var quality = QualityCalibrationPipeline.Run(qualityManifest, qualityRows, qualityIdentity,
            new string('1', 64), qualityManifest.RecordsSha256, qualityManifest.CalibrationSplitSha256,
            qualityManifest.SealedTestSplitSha256, "boolean", qualityGates, temperatureSteps: 2,
            maxDuration: TimeSpan.FromSeconds(10), cancellationToken: deadline.Token);
        check(quality.Gate.Status == "not_eligible" && quality.Calibrated.Count == 2 &&
            double.IsFinite(quality.Temperature) && quality.Temperature > 0 && quality.Intervals.Accuracy.Count == 2,
            "quality pipeline keeps fixture gate explicitly ineligible and emits finite sealed metrics");
        var acceptedQuality = QualityCalibrationPipeline.Run(qualityManifest with
            { Status = "accepted_for_quality", RepresentsModelQuality = true }, qualityRows, qualityIdentity,
            new string('1', 64), qualityManifest.RecordsSha256, qualityManifest.CalibrationSplitSha256,
            qualityManifest.SealedTestSplitSha256, "boolean", qualityGates, temperatureSteps: 2,
            maxDuration: TimeSpan.FromSeconds(10), cancellationToken: deadline.Token);
        check(acceptedQuality.Gate.Status == "passed" && acceptedQuality.Gate.Failures.Length == 0,
            "accepted quality manifest evaluates frozen gates separately from fixture status");
        expectCode(() => QualityCalibrationPipeline.Run(qualityManifest, qualityRows, qualityIdentity,
            new string('1', 64), new string('2', 64), qualityManifest.CalibrationSplitSha256,
            qualityManifest.SealedTestSplitSha256, "boolean", qualityGates, temperatureSteps: 2,
            maxDuration: TimeSpan.FromSeconds(10), cancellationToken: deadline.Token),
            "quality_hash_mismatch", "quality pipeline rejects manifest hash mismatch");
        expectCode(() => QualityCalibrationPipeline.Run(qualityManifest with { ManifestSha256 = new string('0', 64) }, qualityRows, qualityIdentity,
            new string('1', 64), qualityManifest.RecordsSha256, qualityManifest.CalibrationSplitSha256,
            qualityManifest.SealedTestSplitSha256, "boolean", qualityGates, temperatureSteps: 2,
            maxDuration: TimeSpan.FromSeconds(10), cancellationToken: deadline.Token),
            "quality_hash_mismatch", "quality pipeline rejects embedded manifest hash mismatch");
        var badSplitRows = qualityRows.ToArray();
        badSplitRows[0] = badSplitRows[0] with { Split = "sealed_test" };
        expectCode(() => QualityCalibrationPipeline.Run(qualityManifest, badSplitRows, qualityIdentity,
            new string('1', 64), qualityManifest.RecordsSha256, qualityManifest.CalibrationSplitSha256,
            qualityManifest.SealedTestSplitSha256, "boolean", qualityGates, temperatureSteps: 2,
            maxDuration: TimeSpan.FromSeconds(10), cancellationToken: deadline.Token),
            "quality_split_count_mismatch", "quality pipeline rejects fixed split count mismatch");
    }

    private static void Expect<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); }
        catch (T) { check(true, name); return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}: {name}");
    }
}
