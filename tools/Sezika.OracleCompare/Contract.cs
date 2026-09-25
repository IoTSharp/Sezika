using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.OracleCompare;

internal sealed record Capture
{
    public required string SchemaVersion { get; init; }
    public required Identity Provenance { get; init; }
    public required CapturedCase[] Cases { get; init; }
    public string? MeasurementStatus { get; init; }
}

// Runtime/backend/dependency diagnostics intentionally do not participate in identity equality.
internal sealed record Identity
{
    public required string ModelId { get; init; }
    public required string ModelRevision { get; init; }
    public required string WeightsSha256 { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required string UpstreamSourceRevision { get; init; }
    public required string CasesSha256 { get; init; }
    public required string ContractSha256 { get; init; }
    public required int MaxLen { get; init; }
    public required int HeadMaxLen { get; init; }
    public required string TemperaturePolicy { get; init; }
}

internal sealed record CapturedCase
{
    public required string Id { get; init; }
    public required string Primitive { get; init; }
    public required string Status { get; init; }
    public required JsonElement Input { get; init; }
    public int[]? TokenIds { get; init; }
    public int[]? MarkerPositions { get; init; }
    public string[]? CandidateLabels { get; init; }
    public double[]? RawLogits { get; init; }
    public double[]? Probabilities { get; init; }
    public Prediction? Prediction { get; init; }
    public Failure? Failure { get; init; }
}

internal sealed record Prediction
{
    public string? ChoiceLabel { get; init; }
    public double? Score { get; init; }
    public bool? Boolean { get; init; }
    public double? ProbabilityTrue { get; init; }
}

internal sealed record Failure
{
    public required string Stage { get; init; }
    public required string Type { get; init; }
    public required string Message { get; init; }
}

internal sealed record FrozenContract
{
    public required string SchemaVersion { get; init; }
    public required string OutputSchemaVersion { get; init; }
    public required Tolerances Tolerances { get; init; }
}

internal sealed record InputManifest
{
    public required string SchemaVersion { get; init; }
    public required InputCase[] Cases { get; init; }
}

internal sealed record InputCase
{
    public required string Id { get; init; }
    public required string Primitive { get; init; }
}

internal sealed record Tolerances
{
    public required Tolerance RawLogits { get; init; }
    public required Tolerance Probabilities { get; init; }
    public required Tolerance Score { get; init; }
    public required double NearTieLogitMargin { get; init; }
}

internal sealed record Tolerance
{
    public required double Absolute { get; init; }
    public required double Relative { get; init; }
    public bool Accepts(double expected, double actual) =>
        double.IsFinite(expected) && double.IsFinite(actual) &&
        Math.Abs(expected - actual) <= Absolute + Relative * Math.Abs(expected);
}

internal sealed record Issue(string CaseId, string Field, string Message);
internal sealed record ComparisonReport
{
    public string SchemaVersion { get; init; } = "sezika.oracle-comparison.v1";
    public required DateTimeOffset StartedUtc { get; init; }
    public required string ReferenceSha256 { get; init; }
    public required string ActualSha256 { get; init; }
    public required string ContractSha256 { get; init; }
    public required string CasesSha256 { get; init; }
    public required Identity Identity { get; init; }
    public required Tolerances Tolerances { get; init; }
    public required int ManifestCount { get; init; }
    public required string[] SelectedCaseIds { get; init; }
    public required string[] MissingCaseIds { get; init; }
    public required double InputCoverage { get; init; }
    public required bool CompleteInputCoverage { get; init; }
    public required int ReferenceCount { get; init; }
    public required int ActualCount { get; init; }
    public string? ReferenceMeasurementStatus { get; init; }
    public string? ActualMeasurementStatus { get; init; }
    public required string SelectionMode { get; init; }
    public string[]? RequestedCaseIds { get; init; }
    public required string[] ExcludedReferenceCaseIds { get; init; }
    public required string[] ExcludedActualCaseIds { get; init; }
    public required int ComparedCount { get; init; }
    public required int AnsweredPairs { get; init; }
    public required int FailurePairs { get; init; }
    public required int NearTieCases { get; init; }
    public required double MaxLogitError { get; init; }
    public required double MaxProbabilityError { get; init; }
    public required bool Passed { get; init; }
    public required bool FullManifestPassed { get; init; }
    public required List<Issue> Issues { get; init; }
    public required double ElapsedMilliseconds { get; init; }
    public string Scope { get; init; } = "Offline comparison of the explicitly reported selection only. Original complete file hashes and row counts remain bound to the report. A selected-subset pass is not a full-manifest pass. No inference, dataset quality, Native AOT or performance acceptance. Near ties never waive prediction mismatch. Failed pairs do not establish numerical parity.";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(Capture))]
[JsonSerializable(typeof(FrozenContract))]
[JsonSerializable(typeof(InputManifest))]
[JsonSerializable(typeof(ComparisonReport))]
internal partial class CompareJsonContext : JsonSerializerContext;
