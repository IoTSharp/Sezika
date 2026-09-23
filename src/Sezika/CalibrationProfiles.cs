using System.Security.Cryptography;
using System.Text.Json;

namespace Sezika;

/// <summary>
/// Frozen quality gates for a calibration release. The gates are policy, not
/// measured results; a profile cannot be marked verified until every observed
/// metric is present and satisfies these values.
/// </summary>
public sealed record CalibrationGates
{
    public required int MinimumTestExamples { get; init; }
    public required double MinimumAccuracyDeltaOverRandom { get; init; }
    public required double MaximumNllIncreaseOverUncalibrated { get; init; }
    public required double MaximumBrierIncreaseOverUncalibrated { get; init; }
    public required double MaximumExpectedCalibrationError { get; init; }
    public required double MinimumCoverage { get; init; }
    public required double MaximumSelectiveRisk { get; init; }
    public required double MaximumScoreMae { get; init; }
    public required double ConfidenceLevel { get; init; }

    public void Validate()
    {
        if (MinimumTestExamples < 1 || !double.IsFinite(MinimumAccuracyDeltaOverRandom) ||
            !double.IsFinite(MaximumNllIncreaseOverUncalibrated) ||
            !double.IsFinite(MaximumBrierIncreaseOverUncalibrated) ||
            !double.IsFinite(MaximumExpectedCalibrationError) ||
            !double.IsFinite(MinimumCoverage) || !double.IsFinite(MaximumSelectiveRisk) ||
            !double.IsFinite(MaximumScoreMae) || !double.IsFinite(ConfidenceLevel) ||
            MinimumAccuracyDeltaOverRandom < 0 || MaximumNllIncreaseOverUncalibrated < 0 ||
            MaximumBrierIncreaseOverUncalibrated < 0 || MaximumExpectedCalibrationError is < 0 or > 1 ||
            MinimumCoverage is < 0 or > 1 || MaximumSelectiveRisk is < 0 or > 1 ||
            MaximumScoreMae < 0 || ConfidenceLevel is <= 0 or >= 1)
        {
            throw new DecisionException("decision_calibration_gates_invalid", "Calibration quality gates are invalid.");
        }
    }
}

public sealed record CalibrationMetricSnapshot
{
    public required int Count { get; init; }
    public required double Accuracy { get; init; }
    public required double MacroF1 { get; init; }
    public required double NegativeLogLikelihood { get; init; }
    public required double Brier { get; init; }
    public required double ExpectedCalibrationError { get; init; }
    public required double Coverage { get; init; }
    public required double SelectiveRisk { get; init; }
    public double? ScoreMae { get; init; }
    public double? AccuracyDeltaOverRandom { get; init; }
    public double? NegativeLogLikelihoodDeltaOverUncalibrated { get; init; }
    public double? BrierDeltaOverUncalibrated { get; init; }
}

/// <summary>One immutable binding from a calibrated head to its exact inputs.</summary>
public sealed record CalibrationProfileEntry
{
    public required string ProfileId { get; init; }
    public required string Status { get; init; }
    public required string FitStatus { get; init; }
    public required string ModelId { get; init; }
    public required string ModelRevision { get; init; }
    public required string ModelWeightsSha256 { get; init; }
    public required string TokenizerRevision { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required string PromptSchemaId { get; init; }
    public required string PromptSchemaSha256 { get; init; }
    public required string Primitive { get; init; }
    public required string Language { get; init; }
    public required string Domain { get; init; }
    public required string Split { get; init; }
    public required string DatasetManifestSha256 { get; init; }
    public required string Scope { get; init; }
    public required double Temperature { get; init; }
    public CalibrationMetricSnapshot? ObservedMetrics { get; init; }
    public string? MetricsArtifact { get; init; }
    public string? Notes { get; init; }

    public void Validate(CalibrationProfileBinding expected, CalibrationGates gates)
    {
        if (string.IsNullOrWhiteSpace(ProfileId) ||
            Status is not ("pending_measurement" or "verified" or "rejected") ||
            FitStatus is not ("not_fitted" or "fitted" or "failed") ||
            Primitive is not ("choice" or "score" or "boolean") ||
            Language is not ("en" or "zh") || string.IsNullOrWhiteSpace(Domain) || string.IsNullOrWhiteSpace(Scope) ||
            Split != "calibration" || !double.IsFinite(Temperature) || Temperature <= 0 ||
            !IsSha256(ModelWeightsSha256) || !IsSha256(TokenizerSha256) ||
            !IsSha256(PromptSchemaSha256) || !IsSha256(DatasetManifestSha256))
        {
            throw new DecisionException("decision_calibration_profile_invalid", $"Calibration profile '{ProfileId}' is invalid.");
        }
        gates.Validate();
        if (!string.Equals(ModelId, expected.ModelId, StringComparison.Ordinal) ||
            !string.Equals(ModelRevision, expected.ModelRevision, StringComparison.Ordinal) ||
            !string.Equals(ModelWeightsSha256, expected.ModelWeightsSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(TokenizerRevision, expected.TokenizerRevision, StringComparison.Ordinal) ||
            !string.Equals(TokenizerSha256, expected.TokenizerSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(PromptSchemaId, expected.PromptSchemaId, StringComparison.Ordinal) ||
            !string.Equals(PromptSchemaSha256, expected.PromptSchemaSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(DatasetManifestSha256, expected.DatasetManifestSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new DecisionException("decision_calibration_out_of_scope", $"Calibration profile '{ProfileId}' is not bound to the expected assets.");
        }
        if (ObservedMetrics is not null) ValidateMetrics(ObservedMetrics, gates);
        if (Status == "verified" && (FitStatus != "fitted" || ObservedMetrics is null))
        {
            throw new DecisionException("decision_calibration_not_ready", $"Verified profile '{ProfileId}' has no fitted metrics.");
        }
    }

    private static void ValidateMetrics(CalibrationMetricSnapshot metrics, CalibrationGates gates)
    {
        if (metrics.Count < 0 || !double.IsFinite(metrics.Accuracy) || metrics.Accuracy is < 0 or > 1 ||
            !double.IsFinite(metrics.MacroF1) || metrics.MacroF1 is < 0 or > 1 ||
            !double.IsFinite(metrics.NegativeLogLikelihood) || metrics.NegativeLogLikelihood < 0 ||
            !double.IsFinite(metrics.Brier) || metrics.Brier < 0 ||
            !double.IsFinite(metrics.ExpectedCalibrationError) || metrics.ExpectedCalibrationError is < 0 or > 1 ||
            !double.IsFinite(metrics.Coverage) || metrics.Coverage is < 0 or > 1 ||
            !double.IsFinite(metrics.SelectiveRisk) || metrics.SelectiveRisk is < 0 or > 1 ||
            (metrics.ScoreMae is not null && (!double.IsFinite(metrics.ScoreMae.Value) || metrics.ScoreMae.Value < 0)))
        {
            throw new DecisionException("decision_calibration_metrics_invalid", "Calibration metrics are invalid.");
        }
        if (metrics.Count < gates.MinimumTestExamples ||
            (metrics.AccuracyDeltaOverRandom is not null && metrics.AccuracyDeltaOverRandom.Value < gates.MinimumAccuracyDeltaOverRandom) ||
            (metrics.NegativeLogLikelihoodDeltaOverUncalibrated is not null && metrics.NegativeLogLikelihoodDeltaOverUncalibrated.Value > gates.MaximumNllIncreaseOverUncalibrated) ||
            (metrics.BrierDeltaOverUncalibrated is not null && metrics.BrierDeltaOverUncalibrated.Value > gates.MaximumBrierIncreaseOverUncalibrated) ||
            metrics.ExpectedCalibrationError > gates.MaximumExpectedCalibrationError ||
            metrics.Coverage < gates.MinimumCoverage || metrics.SelectiveRisk > gates.MaximumSelectiveRisk ||
            (metrics.ScoreMae is not null && metrics.ScoreMae.Value > gates.MaximumScoreMae))
        {
            throw new DecisionException("decision_calibration_quality_gate_failed", "Calibration metrics do not satisfy the frozen quality gates.");
        }
    }

    private static bool IsSha256(string? value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);
}

public sealed record CalibrationProfileManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string ManifestId { get; init; }
    public required string Status { get; init; }
    public required string License { get; init; }
    public required string DatasetManifestSha256 { get; init; }
    public required string PromptSchemaId { get; init; }
    public required string PromptSchemaSha256 { get; init; }
    public required CalibrationGates FrozenGates { get; init; }
    public required CalibrationProfileEntry[] Profiles { get; init; }

    public void Validate(CalibrationProfileBinding expected)
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(ManifestId) ||
            Status is not ("pending_measurement" or "verified") ||
            !string.Equals(License, "Apache-2.0", StringComparison.Ordinal) ||
            !IsSha256(DatasetManifestSha256) || string.IsNullOrWhiteSpace(PromptSchemaId) ||
            !IsSha256(PromptSchemaSha256) || FrozenGates is null || Profiles is null || Profiles.Length == 0)
        {
            throw new DecisionException("decision_calibration_manifest_invalid", "Calibration manifest fields are incomplete or unsupported.");
        }
        FrozenGates.Validate();
        if (!string.Equals(DatasetManifestSha256, expected.DatasetManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(PromptSchemaId, expected.PromptSchemaId, StringComparison.Ordinal) ||
            !string.Equals(PromptSchemaSha256, expected.PromptSchemaSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new DecisionException("decision_calibration_out_of_scope", "Calibration manifest is not bound to the expected dataset or prompt schema.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in Profiles)
        {
            if (!ids.Add(profile.ProfileId)) throw new DecisionException("decision_calibration_manifest_invalid", "Calibration profile IDs must be unique.");
            profile.Validate(expected, FrozenGates);
        }
    }

    private static bool IsSha256(string? value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);
}

public sealed record CalibrationProfileBinding
{
    public required string ModelId { get; init; }
    public required string ModelRevision { get; init; }
    public required string ModelWeightsSha256 { get; init; }
    public required string TokenizerRevision { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required string PromptSchemaId { get; init; }
    public required string PromptSchemaSha256 { get; init; }
    public required string DatasetManifestSha256 { get; init; }
}

public static class CalibrationProfileManifestLoader
{
    public static CalibrationProfileManifest LoadAndValidate(
        string path,
        CalibrationProfileBinding expected,
        CancellationToken cancellationToken = default,
        long maxBytes = 4 * 1024 * 1024)
    {
        if (string.IsNullOrWhiteSpace(path) || expected is null) throw new ArgumentNullException(nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new DecisionException("decision_calibration_manifest_missing", "Calibration manifest is missing.");
        var info = new FileInfo(fullPath);
        if (info.Length <= 0 || info.Length > maxBytes) throw new DecisionException("decision_calibration_manifest_invalid", "Calibration manifest exceeds the size limit.");
        cancellationToken.ThrowIfCancellationRequested();
        CalibrationProfileManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(File.ReadAllBytes(fullPath), DecisionJsonContext.Default.CalibrationProfileManifest);
        }
        catch (JsonException exception)
        {
            throw new DecisionException("decision_calibration_manifest_invalid", exception.Message, exception);
        }
        if (manifest is null) throw new DecisionException("decision_calibration_manifest_invalid", "Calibration manifest is empty.");
        cancellationToken.ThrowIfCancellationRequested();
        manifest.Validate(expected);
        return manifest;
    }

    public static string Sha256File(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
