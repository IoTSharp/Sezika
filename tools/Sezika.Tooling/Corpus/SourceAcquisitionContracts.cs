using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record SourceAcquisitionRequest(string PlanPath, string PlanSha256, string MetadataRoot, string OutputRoot,
    string ReceiptDirectory, string[]? SelectedAssetIds = null);
internal sealed record SourceAcquisitionPlan(int SchemaVersion, SourceAcquisitionSource[] Sources, SourceAcquisitionAsset[] Assets);
internal sealed record SourceResearchPlanEntry(string Dataset, string Revision, string Split, string Url, string Path, long ExpectedBytes,
    string? ExpectedSha256, ContentDigest ExpectedDigest, string SourceAcquisitionPlanSha256);
internal sealed record SourceAcquisitionSource(string Id, string Provider, string Revision, string DeclaredLicense, AssetFile[] MetadataPins);
internal sealed record SourceAcquisitionAsset(string Id, string SourceId, string Path, string RemotePath, string Split, long Bytes,
    ContentDigest ExpectedDigest, string OfficialUrl, bool PreferMirror = false, long RangeBytes = 16 * 1024 * 1024);
internal sealed record SourceAcquisitionAttempt(string Route, string Status, string? FailureType, int? Requests, long? ResumedBytes, int? HttpStatusCode = null);
internal sealed record SourceAcquisitionEntry(string Id, string SourceId, string Revision, string DeclaredLicense, string Path, string Split,
    string OfficialUrl, string PlanSha256, long ExpectedBytes, ContentDigest ExpectedDigest, string Status, long Bytes, string? ContentSha256, long RetainedPartialBytes,
    SourceAcquisitionAttempt[] Attempts, string? FailureType = null, bool FormalTrainingAdmitted = false, string MirrorDecision = "NOT_REQUESTED");
internal sealed record SourceAcquisitionSourceResult(string Id, string Revision, string DeclaredLicense, AssetFile[] MetadataPins,
    int PlannedAssets, int SelectedAssets, int CompleteAssets, int FailedAssets, bool AllPlannedAssetsComplete, bool FormalTrainingAdmitted = false);
internal sealed record SourceAcquisitionReport(string Status, string PlanSha256, int PlannedSources, int PlannedAssets, int SelectedAssets,
    int CompleteAssets, int FailedAssets, int NotAttemptedAssets, int UnselectedAssets, bool AllSelectedAssetsComplete,
    bool AllPlannedAssetsComplete, SourceAcquisitionSourceResult[] Sources, SourceAcquisitionEntry[] Entries,
    bool FormalTrainingAdmitted = false, bool TeacherCalled = false, bool ModelTrainingRun = false);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = false,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SourceAcquisitionRequest))]
[JsonSerializable(typeof(SourceAcquisitionPlan))]
[JsonSerializable(typeof(SourceAcquisitionReport))]
[JsonSerializable(typeof(SourceAcquisitionEntry))]
[JsonSerializable(typeof(SourceResearchPlanEntry))]
[JsonSerializable(typeof(SourceResearchPlanEntry[]))]
internal partial class SourceAcquisitionJsonContext : JsonSerializerContext;
