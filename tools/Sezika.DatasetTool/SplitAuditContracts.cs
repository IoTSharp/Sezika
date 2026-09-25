using System.Text.Json.Serialization;

// These records describe data admission; they are not a model input or an execution authorization.
internal sealed record AuditManifest
{
    public required int SchemaVersion { get; init; }
    public required string DatasetId { get; init; }
    public required DatasetStage Stage { get; init; }
    public required IntendedUse IntendedUse { get; init; }
    public required ArtifactReference Records { get; init; }
    public required AuditSource[] Sources { get; init; }
    public required TestSeal TestSeal { get; init; }
}

internal sealed record ArtifactReference
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
}

internal sealed record AuditSource
{
    public required string Id { get; init; }
    public required string UpstreamId { get; init; }
    public required string UpstreamSplit { get; init; }
    public required string Revision { get; init; }
    public required SourceOrigin Origin { get; init; }
    public required ArtifactReference Raw { get; init; }
    public required string LicenseExpression { get; init; }
    public required ReviewStatus LicenseReview { get; init; }
    public string? LicenseReviewer { get; init; }
    public DateTimeOffset? LicenseReviewedUtc { get; init; }
    public ArtifactReference? LicenseEvidence { get; init; }
    public required IntendedUse[] AllowedUses { get; init; }
    public required DataSplit[] AllowedSplits { get; init; }
    public required Exposure Exposure { get; init; }
}

internal sealed record TestSeal
{
    public required SealStatus Status { get; init; }
    public string? Custodian { get; init; }
    public DateTimeOffset? SealedUtc { get; init; }
    public ArtifactReference? Evidence { get; init; }
}

internal sealed record SealEvidence
{
    public required string DatasetId { get; init; }
    public required string RecordsSha256 { get; init; }
    public required string Custodian { get; init; }
    public required DateTimeOffset SealedUtc { get; init; }
}

internal sealed record AuditRecord
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string Family { get; init; }
    public required string[] Entities { get; init; }
    public required string Language { get; init; }
    public required string Domain { get; init; }
    public required DataSplit Split { get; init; }
    public required string Text { get; init; }
    public required Derivation Derivation { get; init; }
    public string? ParentId { get; init; }
    public required ReviewStatus HumanReview { get; init; }
    public string? Reviewer { get; init; }
    public DateTimeOffset? ReviewedUtc { get; init; }
    public string? ReviewNote { get; init; }
    public required Exposure Exposure { get; init; }
}

internal sealed record AuditIssue(string Code, string Subject, string? RelatedId, string Detail);
internal sealed record SplitCount(DataSplit Split, int Count);
internal sealed record SourceAdmission(string SourceId, ReviewStatus LicenseReview, IntendedUse[] DeclaredAllowedUses,
    DataSplit[] DeclaredAllowedSplits, Exposure DeclaredExposure, bool AdmissionChecksPassed);
internal sealed record AuditReport(int SchemaVersion, string DatasetId, string Status, bool AdmissionChecksPassed,
    bool RequiresHumanAcceptance, bool RepresentsModelQuality, string ToolPolicyVersion, string ManifestSha256,
    string RecordsSha256, string FingerprintAlgorithm, double NearDuplicateThreshold, int RecordCount,
    long ComparedPairs, SplitCount[] Splits, SourceAdmission[] Sources, AuditIssue[] Issues,
    DateTimeOffset StartedUtc, double ElapsedSeconds);

[JsonConverter(typeof(JsonStringEnumConverter<DatasetStage>))]
internal enum DatasetStage { FixtureOnly, Candidate }
[JsonConverter(typeof(JsonStringEnumConverter<IntendedUse>))]
internal enum IntendedUse { Research, Commercial, OpenSourceRedistribution }
[JsonConverter(typeof(JsonStringEnumConverter<ReviewStatus>))]
internal enum ReviewStatus { Pending, Approved, Rejected }
[JsonConverter(typeof(JsonStringEnumConverter<DataSplit>))]
internal enum DataSplit { Training, Development, Calibration, SealedTest, Audit }
[JsonConverter(typeof(JsonStringEnumConverter<SourceOrigin>))]
internal enum SourceOrigin { Original, ThirdParty, PawsViewedTest250, NimbleViewedEval324 }
[JsonConverter(typeof(JsonStringEnumConverter<Exposure>))]
internal enum Exposure { Unseen, DevelopmentViewed, EvaluationViewed }
[JsonConverter(typeof(JsonStringEnumConverter<SealStatus>))]
internal enum SealStatus { Unsealed, Sealed }
[JsonConverter(typeof(JsonStringEnumConverter<Derivation>))]
internal enum Derivation { Original, Translation, Counterfactual, Paraphrase }

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AuditManifest))]
[JsonSerializable(typeof(AuditRecord))]
[JsonSerializable(typeof(AuditReport))]
[JsonSerializable(typeof(SealEvidence))]
internal partial class SplitAuditJsonContext : JsonSerializerContext { }
