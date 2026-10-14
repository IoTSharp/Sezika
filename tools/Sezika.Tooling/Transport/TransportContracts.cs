using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record AssetFile(string Path, long Bytes, string Sha256);
internal sealed record DownloadSpec(string Url, long Bytes, string Sha256, string? ETag = null, long RangeBytes = 16 * 1024 * 1024);
internal sealed record DownloadReport(string Status, string Path, long Bytes, string Sha256, string ETag, long ResumedBytes, int Requests, bool IndependentlyReadBack);
internal sealed record DownloadState(int SchemaVersion, string Url, long Bytes, string Sha256, string? ETag);
// Expected content identity is supplied by a frozen upstream plan. A Git blob
// digest includes its Git header and is never a content SHA-256.
internal sealed record ContentDigest(string Kind, string Value);
internal sealed record DigestDownloadSpec(string Url, long Bytes, ContentDigest ExpectedDigest, string? ETag = null, long RangeBytes = 16 * 1024 * 1024);
internal sealed record DigestDownloadReport(string Status, string Path, long Bytes, ContentDigest ExpectedDigest, string ContentSha256,
    string ETag, long ResumedBytes, int Requests, bool IndependentlyReadBack);
internal sealed record DigestDownloadState(int SchemaVersion, string Url, long Bytes, ContentDigest ExpectedDigest, string? ETag);
internal sealed class DownloadHttpException(int statusCode) : IOException("Server did not honor bounded HTTP Range with 206.")
{
    internal int StatusCode { get; } = statusCode;
}
internal sealed record CompletionGates(bool FullCorpusEpochCoverage, bool GlobalUpdatesComplete, bool SourceEofVerifiedByTraining,
    bool CompleteCheckpointVerified, bool AllAdamwMomentsVerified, bool IndependentValidationVerified, bool SourceEofIndividuallyReported = false);
internal sealed record LogicalManifest(int SchemaVersion, string Kind, string RunId, string Status, string OwnedPrefix, AssetFile[] Files,
    long TotalBytes, string PackageSha256, long GlobalUpdates, string PublishedDatasetCommit, string OriginalDatasetCommit,
    CompletionGates Completion, bool ExperimentalOnly, bool FormalQuality, bool TestOnly = false);
internal sealed record TransportMapping(AssetFile Logical, AssetFile[] Parts);
internal sealed record TransportManifest(int SchemaVersion, string Kind, string RunId, bool FixtureOnly, string LogicalManifestSha256,
    long MaximumPhysicalObjectBytes, long LogicalTotalBytes, TransportMapping[] Mappings, AssetFile[] PhysicalFiles);
internal sealed record TransportPreparationReport(string Status, string RunId, string Directory, int LogicalFiles, long LogicalBytes,
    int PhysicalFiles, string TransportManifestSha256, string AttributesSha256, string PublicationManifestSha256,
    string ServerPublish = "NOT_RUN", string AuthenticatedReadback = "NOT_RUN");
internal sealed record TransportReadbackReport(string Status, string RunId, string Directory, int Files, long Bytes, bool CompleteOriginalBytesSha256,
    string PublicationState = "NOT_INFERRED", string FormalQuality = "NOT_ACCEPTED", string Calibration = "NOT_ACCEPTED", string NewWeightsAot = "NOT_RUN");
internal sealed record PublicationPlan(int SchemaVersion, string Provider, string Repository, string Branch, string Visibility,
    string Kind, string RunId, string SourceCommit, string ExpectedRef, long MaximumObjectBytes, AssetFile[] Files);
internal sealed record PublicationPreview(string Status, PublicationPlan Plan, int Files, long Bytes, string PlanSha256,
    bool PublicContentReviewed, string Upload = "NOT_RUN", string RefPublication = "NOT_RUN", string IndependentReadback = "NOT_RUN");
internal sealed record PublicationAuthorization(string PlanSha256, string Repository, string ExpectedRef, string Visibility,
    string ApprovedBy, DateTimeOffset ApprovedUtc);
internal sealed record LfsObject(string Oid, long Size);
internal sealed record LfsRef(string Name);
internal sealed record LfsBatchRequest(string Operation, string[] Transfers, LfsObject[] Objects, LfsRef Ref, string HashAlgo = "sha256");
internal sealed record LfsError(int Code, string Message);
internal sealed record LfsAction(string Href, Dictionary<string, string>? Header = null);
internal sealed record LfsActions(LfsAction? Upload = null, LfsAction? Download = null, LfsAction? Verify = null);
internal sealed record LfsResponseObject(string Oid, long Size, LfsActions? Actions = null, LfsError? Error = null);
internal sealed record LfsBatchResponse(LfsResponseObject[] Objects, string? Transfer = null);
internal sealed record MultipartPart([property: JsonPropertyName("partNumber")] int PartNumber, string Etag);
internal sealed record MultipartCompletion(string Oid, MultipartPart[] Parts);
internal sealed record ObjectUploadReport(string Status, string Oid, long Bytes, int Parts, bool ServerObjectReadBack,
    string RefPublication = "NOT_RUN", string FullContentReadback = "NOT_RUN", int PriorCandidateParts = 0, int ReplacedPriorParts = 0, string? ResumeCheckpoint = null);
internal sealed record MultipartPartReceipt([property: JsonPropertyName("partNumber")] int PartNumber, string Etag, long Bytes, string Sha256);
internal sealed record MultipartResumeState(int SchemaVersion, string Repository, string Oid, long Bytes, long ChunkSize,
    string SessionSha256, MultipartPartReceipt[] CompletedParts, string Status);
internal sealed record HubCommitHeader(string Summary, string Description, [property: JsonPropertyName("parentCommit")] string ParentCommit);
internal sealed record HubCommitLfs(string Path, string Algo, string Oid, long Size);
internal sealed record HubCommitReceipt(string Status, string Repository, string Commit, string Visibility, string IndependentReadback = "NOT_RUN");
internal sealed record CorpusPublicationBinding(int SchemaVersion, string RunId, string OriginalDatasetCommit, string PublishedDatasetCommit,
    string PackageSha256, string ValidationSha256, string ContractSha256, string CheckpointDirectory, long GlobalUpdates,
    string TrainingGuardianSha256, string ValidationGuardianSha256, bool ExperimentalOnly = true, bool FormalQuality = false,
    bool SourceEofIndividuallyReported = false, string Calibration = "uncalibrated", string AotAndPerformance = "NOT_ACCEPTED", string Publication = "PREPARED_NOT_PUSHED");

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(DownloadState))]
[JsonSerializable(typeof(DownloadReport))]
[JsonSerializable(typeof(DigestDownloadState))]
[JsonSerializable(typeof(DigestDownloadReport))]
[JsonSerializable(typeof(LogicalManifest))]
[JsonSerializable(typeof(TransportManifest))]
[JsonSerializable(typeof(TransportPreparationReport))]
[JsonSerializable(typeof(TransportReadbackReport))]
[JsonSerializable(typeof(PublicationPlan))]
[JsonSerializable(typeof(PublicationPreview))]
[JsonSerializable(typeof(PublicationAuthorization))]
[JsonSerializable(typeof(LfsBatchRequest))]
[JsonSerializable(typeof(LfsBatchResponse))]
[JsonSerializable(typeof(LfsObject))]
[JsonSerializable(typeof(MultipartCompletion))]
[JsonSerializable(typeof(ObjectUploadReport))]
[JsonSerializable(typeof(MultipartResumeState))]
[JsonSerializable(typeof(HubCommitHeader))]
[JsonSerializable(typeof(HubCommitLfs))]
[JsonSerializable(typeof(HubCommitReceipt))]
[JsonSerializable(typeof(CorpusPublicationBinding))]
internal partial class TransportJsonContext : JsonSerializerContext;
