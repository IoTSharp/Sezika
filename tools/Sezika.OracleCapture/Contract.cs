using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.OracleCapture;

// Deliberately excludes reference tokens, marker positions, logits and probabilities.
// Only expanded user inputs enter the C# implementation.
internal sealed record ReferenceCapture
{
    public required string SchemaVersion { get; init; }
    public required Identity Provenance { get; init; }
    public required ReferenceCase[] Cases { get; init; }
}

internal sealed record ReferenceCase
{
    public required string Id { get; init; }
    public required string Primitive { get; init; }
    public required string Status { get; init; }
    public required JsonElement Input { get; init; }
}

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

internal sealed record Capture
{
    public string SchemaVersion { get; init; } = "sezika.laya-oracle.v1";
    public required Identity Provenance { get; init; }
    public required ExecutionIdentity Implementation { get; init; }
    public required string ReferenceSha256 { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? EndedUtc { get; set; }
    public required string[] SelectedCaseIds { get; init; }
    public required string[] MissingManifestCaseIds { get; init; }
    public required int TotalManifestCases { get; init; }
    public required int ReferenceCaseCount { get; init; }
    public string MeasurementStatus { get; set; } = "in_progress";
    public string[] UnprocessedCaseIds { get; set; } = [];
    public List<CapturedCase> Cases { get; init; } = [];
    public string? FatalError { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public string Scope { get; init; } = "Actual Sezika typed engine inputs and FP32 outputs. Rejected or unsupported rows are retained. Selection is explicit; a subset cannot establish full-suite parity, calibration, language quality, AOT or performance acceptance.";
}

internal sealed record ExecutionIdentity
{
    public string Name { get; init; } = "sezika_typed_modernbert_marker_engine";
    public required string Backend { get; init; }
    public required PromptLengthPolicy LengthPolicy { get; init; }
    public required string Framework { get; init; }
    public required string OperatingSystem { get; init; }
    public required string Architecture { get; init; }
    public required string RuntimeIdentifier { get; init; }
    public required bool RequireAot { get; init; }
    public required bool IsDynamicCodeSupported { get; init; }
    public required bool IsDynamicCodeCompiled { get; init; }
    public required bool ManagedHostDetected { get; init; }
    public required Dictionary<string, string> BinarySha256 { get; init; }
    public required int ProcessId { get; init; }
    public required DateTimeOffset ProcessStartedUtc { get; init; }
    public required string[] Arguments { get; init; }
    public required int TimeoutSeconds { get; init; }
    public required int MaxCases { get; init; }
    public string ProcessTreeEvidence { get; init; } = "Run through tools/Invoke-BoundedProcess.ps1 to record the parent chain and reclaim this task's process subtree.";
}

internal sealed record CapturedCase
{
    public required string Id { get; init; }
    public required string Primitive { get; init; }
    public required JsonElement Input { get; init; }
    public string Status { get; set; } = "failed";
    public int[]? TokenIds { get; set; }
    public int[]? MarkerPositions { get; set; }
    public string[]? CandidateLabels { get; set; }
    public double[]? RawLogits { get; set; }
    public double[]? Probabilities { get; set; }
    public Prediction? Prediction { get; set; }
    public Failure? Failure { get; set; }
    public PromptSequenceDiagnostics? SequenceDiagnostics { get; set; }
    public List<ContractDifference> ContractDifferences { get; init; } = [];
    public string? CalibrationStatus { get; set; }
    public string? ActualBackend { get; set; }
    public double ElapsedMilliseconds { get; set; }
}

internal sealed record Prediction
{
    public string? ChoiceLabel { get; init; }
    public double? Score { get; init; }
    public bool? Boolean { get; init; }
    public double? ProbabilityTrue { get; init; }
}

internal sealed record Failure(string Stage, string Type, string Message, string RuntimeCode);
internal sealed record ContractDifference(string Field, string SezikaPolicy, string ReferenceStatus, string Detail);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(ReferenceCapture))]
[JsonSerializable(typeof(InputManifest))]
[JsonSerializable(typeof(Capture))]
internal partial class CaptureJsonContext : JsonSerializerContext;
