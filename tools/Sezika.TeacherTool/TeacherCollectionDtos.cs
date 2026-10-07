using System.Text.Json;

namespace Sezika.TeacherTool;

public sealed record TeacherChatMessage(string Role, string Content);
public sealed record TeacherChatRequest(string Model, TeacherChatMessage[] Messages, bool Stream, double Temperature, int MaxTokens);
public sealed record TeacherUsage(int PromptTokens, int CompletionTokens, int TotalTokens);
public sealed record TeacherAsset(string Path, string Sha256, int Bytes);
public sealed record TeacherIdentity(string Id, string Service, string Model, string Revision, string ModelSha256,
    string SourceUrl, string License, string Endpoint, string CatalogSha256, string TermsEvidenceSha256,
    string ModelEvidenceSha256, bool IndependentlyVerified, bool OutputTrainingPermitted);
public sealed record TeacherCollectedRecord(string RecordId, string Language, string Type, string FamilyId,
    string Source, string Origin, string Purpose, string Split, string ReviewStatus, string TeacherStatus,
    bool TrainingAdmission, string ModelId, string ModelRevision, string ModelSha256, string QuestionId,
    string[] CandidateIds, int? CandidateIndex, string? SelectedCandidateId,
    string RequestSha256, string PromptSha256, string HttpRequestSha256, string? ResponseSha256,
    TeacherAsset Request, TeacherAsset Prompt, TeacherAsset HttpRequest, TeacherAsset? Response,
    TeacherUsage? Usage, int? HttpStatus, bool HttpAttempted, string? FinishReason, string? FailureCode,
    double ElapsedMilliseconds, bool HumanReviewRequired);
public sealed record TeacherCollectionReport(int SchemaVersion, string Status, bool TrainingAdmission,
    string SourceManifestSha256, string CandidatesSha256, TeacherIdentity Teacher, int Total,
    int Pending, int Failed, int Abstained, int HttpAttempts, int MaximumRecords, int MaximumHttpAttempts,
    int RequestTimeoutSeconds, int TotalTimeoutSeconds, int MaximumOutputTokens, int MaximumResponseBytes,
    double ElapsedSeconds, bool Cancelled, bool QualityEvidence, bool SyntheticTransport,
    TeacherAsset Records, TeacherAsset[] EvidenceAssets);
public sealed record TeacherFailureAudit(int SchemaVersion, string Status, bool TrainingAdmission, bool QualityEvidence,
    bool SyntheticTransport, string FailureCode, string SuccessOutput, TeacherIdentity Teacher,
    string SourceManifestSha256, string CandidatesSha256, int Total, int Pending, int Failed, int Abstained,
    int HttpAttempts, int Attempted, int NotCalled, double WorkElapsedSeconds, TeacherAsset Records);
internal sealed record TeacherCandidate(string RecordId, string Language, string Type, string FamilyId,
    string Source, byte[] RequestBytes, string RequestSha256, byte[] PromptBytes, string PromptSha256,
    string QuestionId, string[] CandidateIds);
internal sealed record TeacherPreflight(TeacherIdentity Identity, TeacherCandidate[] Candidates,
    string SourceManifestSha256, string CandidatesSha256, byte[] CatalogBytes, byte[] ManifestBytes,
    byte[] TermsBytes, byte[] ModelEvidenceBytes);
