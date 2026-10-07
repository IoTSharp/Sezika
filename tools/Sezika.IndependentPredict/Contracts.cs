using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.IndependentPredict;

internal sealed record ExportManifest
{
    public required int SchemaVersion { get; init; }
    public required string DatasetId { get; init; }
    public required string Primitive { get; init; }
    public required double AbstainBelow { get; init; }
    public required ArtifactReference Requests { get; init; }
    public required ArtifactReference AdmissionManifest { get; init; }
    public required ArtifactReference AdmissionReport { get; init; }
    public required ArtifactReference HumanAcceptance { get; init; }
    public required ArtifactReference HeadBinding { get; init; }
}

// Produced by an actual reviewer/custodian outside this tool. The tool never
// writes acceptance or seal evidence, and hash agreement cannot prove a person.
internal sealed record HumanAcceptance
{
    public required int SchemaVersion { get; init; }
    public required string DatasetId { get; init; }
    public required string Status { get; init; }
    public required string IntendedUse { get; init; }
    public required string Reviewer { get; init; }
    public required DateTimeOffset AcceptedUtc { get; init; }
    public required string Note { get; init; }
    public required bool InputContractAndLabelsReviewed { get; init; }
    public required string AdmissionManifestSha256 { get; init; }
    public required string AdmissionReportSha256 { get; init; }
    public required string AdmissionRecordsSha256 { get; init; }
    public required string RequestsSha256 { get; init; }
    public required string SealSha256 { get; init; }
    public required string[] RecordIds { get; init; }
}

internal sealed record HeadBinding(int SchemaVersion, MarkerFeatureIdentity Identity, string FeatureSha256, string HeadSha256);
internal sealed record RequestRow(string Id, string Split, string Primitive, int Label, string Language, string Domain, JsonElement Request);
internal sealed record PredictionRow(string Id, string Split, string Primitive, int Label, string Language, string Domain, double[] Probabilities);
internal sealed record Outcome(string Id, string Split, string Status, string? ErrorCode, int? CandidateCount);
internal sealed record ReleaseIdentity(int SchemaVersion, string Algorithm, string ModelId, string SourceRevision,
    string EncoderSha256, string HeadSha256, string TokenizerSha256, string Protocol, string LengthPolicy, string ProtocolSha256);
internal sealed record ExportReport
{
    public int SchemaVersion { get; init; } = 1;
    public required string Status { get; init; }
    public required bool FixtureOnly { get; init; }
    public bool RepresentsQualityAcceptance => false;
    public required string DatasetId { get; init; }
    public required string Primitive { get; init; }
    public required int Total { get; init; }
    public required int Answered { get; init; }
    public required int Rejected { get; init; }
    public required int Failed { get; init; }
    public required int Unprocessed { get; init; }
    public required Outcome[] Outcomes { get; init; }
    public required QualityCalibrationIdentity Identity { get; init; }
    public required ReleaseIdentity Release { get; init; }
    public required string ExportManifestSha256 { get; init; }
    public required string RequestsSha256 { get; init; }
    public required string TrainingDataManifestSha256 { get; init; }
    public required string FeatureSha256 { get; init; }
    public required string HeadBindingSha256 { get; init; }
    public required string? AdmissionManifestSha256 { get; init; }
    public required string? AdmissionReportSha256 { get; init; }
    public required string? AdmissionRecordsSha256 { get; init; }
    public required string? HumanAcceptanceSha256 { get; init; }
    public required string? SealSha256 { get; init; }
    public required string PredictionsSha256 { get; init; }
    public required string? CalibrationManifestSha256 { get; init; }
    public required double ElapsedMilliseconds { get; init; }
    public string? TerminalError { get; init; }
    public string ConsumerLimitation => "CalibrationTool accepts answered rows only; any failed/rejected/unprocessed row prevents a calibration manifest.";
}

internal sealed record AdmissionBinding(string ManifestHash, string ReportHash, string RecordsHash, string AcceptanceHash, string SealHash);
internal sealed record ExportResult(ExportReport Report, string OutputDirectory);
internal delegate IndependentDecisionV1Runtime RuntimeLoader(string package, string head, HeadBinding binding,
    IndependentDecisionRuntimeOptions options, CancellationToken token);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ExportManifest))]
[JsonSerializable(typeof(HumanAcceptance))]
[JsonSerializable(typeof(HeadBinding))]
[JsonSerializable(typeof(RequestRow))]
[JsonSerializable(typeof(ReleaseIdentity))]
[JsonSerializable(typeof(ExportReport))]
[JsonSerializable(typeof(QualityCalibrationManifest))]
internal partial class ExportJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PredictionRow))]
internal partial class PredictionJsonContext : JsonSerializerContext;
