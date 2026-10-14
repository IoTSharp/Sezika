using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record ProfileValidationRequest(string[] ReportPaths, string Output);
internal sealed record S5SummaryRequest(string[] ReportPaths, string Output, bool VerifyMatrix = false);
internal sealed record DataAdmissionRequest(string Manifest, string Output, int MaximumRecords = 1000, bool RequireAdmission = false);
internal sealed record LanguageAuditRequest(string Root, string[] Sources, string Output, int MaximumRecords = 12);
internal sealed record ValidationInput(string Source, string RelativePath, string Sha256);
internal sealed record ValidationMutation(string RelativePath, string[] JsonPath, string Kind, JsonElement Value, string? ReferencedFile = null);
internal sealed record ValidationOutput(string RelativePath, bool MustExist, string? JsonStatusProperty = null,
    string? JsonStatusValue = null, Dictionary<string, JsonElement>? JsonEquals = null);
internal sealed record ValidationCase(string Name, string Executable, string ExecutableSha256, string[] Arguments,
    ValidationInput[] Inputs, ValidationMutation[] Mutations, ValidationOutput[] Outputs, int ExpectedExitCode,
    string? ExpectedStandardError = null, int TimeoutSeconds = 30, string? ManagedAssembly = null,
    string? ManagedAssemblySha256 = null);
internal sealed record ValidationBatchRequest(ValidationCase[] Cases, string Output, int MaximumCases = 64,
    int MaximumCopiedBytes = 268_435_456, string? BrokerExecutable = null, string[]? BrokerArgumentPrefix = null, bool StopOnFailure = true,
    bool AssetPreflightOnly = false);
internal sealed record ValidationCaseResult(string Name, string Status, int? ExitCode, string CleanupStatus,
    string? ProcessReceipt, string? Failure, string[] OutputSha256);
internal sealed record ValidationBatchReport(string Status, int Planned, int Completed, int Passed, int Failed,
    int Unprocessed, ValidationCaseResult[] Cases, bool SyntheticTransport, bool NewInference = false,
    string ReferenceGeneration = "RETIRED; independently generated frozen references must be supplied",
    string Scope = "Native validation scheduling and independent output/cleanup readback; no new model quality or calibration acceptance");
internal sealed record NearTieRequest(string Plan, string PlanSha256, string InputRoot, string?[] RunDirectories,
    string Output, int MaximumObservedRows = 574);
internal sealed record NearTieRow(string Source, int Ordinal, string Id, string Primitive, string Status, double? Margin,
    bool NearTie, string Reference, string ReferenceSha256);
internal sealed record NearTieSource(string Name, int Planned, int Observed, string Status, string? IndexSha256);
internal sealed record NearTieReport(string Status, string PlanSha256, int Planned, int Observed, int Unprocessed,
    int NearTies, double Threshold, NearTieSource[] Sources, NearTieRow[] Rows, NearTieRow[] SelectedFollowup,
    string Scope = "Read-only margin observation from supplied hash-frozen independent historical reference logits; provenance is a frozen input declaration, not new upstream attestation; no model run, quality/prompt/training selection or calibration");
internal sealed record ValidatedProfileCase(string Id, string Status, int Questions, string InputSha256, int Samples,
    double? P50Milliseconds, double? P95Milliseconds, double? P99Milliseconds, double? RequestsPerSecond,
    double? QuestionsPerSecond, string? Stage, double? StageMilliseconds, string? ErrorCode);
internal sealed record ValidatedProfile(string SourcePath, string SourceSha256, string Backend, string Rid, bool NativeAot,
    string ModelRevision, string WeightsSha256, string TokenizerSha256, string ManifestSha256, int PlannedCases,
    int SuccessfulCases, int RejectedCases, double RequestCoverage, double QuestionCoverage, ValidatedProfileCase[] Cases);
internal sealed record ProfileValidationReport(string Status, ValidatedProfile[] Reports,
    string Scope = "Read-only schema/timing consistency of supplied historical reports; raw samples are never pooled; no new inference, speedup, quality, calibration or AOT acceptance");
internal sealed record ValidationDiagnostic(string Code, string Subject, string Detail, string? RelatedId = null);
internal sealed record DataAdmissionReport(string Status, string DatasetId, string ManifestSha256, string RecordsSha256,
    int RecordCount, long ComparedPairs, Dictionary<string, int> Splits, ValidationDiagnostic[] Issues,
    bool AdmissionChecksPassed, bool RequiresHumanAcceptance = true, bool RepresentsModelQuality = false);
internal sealed record LanguageAuditSource(string Path, string Sha256, int PhysicalRows);
internal sealed record LanguageAuditReport(string Status, int Records, string DatasetSha256, LanguageAuditSource[] Sources,
    string License = "Apache-2.0", string Transformation = "Original test input/labels/candidate order preserved; Boolean when_false/when_true maps false/true; no quality, calibration or model-output acceptance");
internal sealed record S5ValidatedRow(string SourcePath, string SourceSha256, string Rid, string Backend, bool NativeAot,
    int Samples, string ModelRevision, string WeightsSha256, string TokenizerSha256, string ManifestSha256,
    JsonElement OriginalReport);
internal sealed record S5SummaryReport(string Status, bool MatrixVerified, S5ValidatedRow[] Rows,
    string Scope = "Each historical RID/backend/executable/run remains separate; no pooled samples, cache flush, new inference, language quality or calibration acceptance");

[JsonSourceGenerationOptions(WriteIndented = false, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ProfileValidationRequest))]
[JsonSerializable(typeof(S5SummaryRequest))]
[JsonSerializable(typeof(DataAdmissionRequest))]
[JsonSerializable(typeof(LanguageAuditRequest))]
[JsonSerializable(typeof(ValidationBatchRequest))]
[JsonSerializable(typeof(ValidationBatchReport))]
[JsonSerializable(typeof(NearTieRequest))]
[JsonSerializable(typeof(NearTieReport))]
[JsonSerializable(typeof(ProfileValidationReport))]
[JsonSerializable(typeof(DataAdmissionReport))]
[JsonSerializable(typeof(LanguageAuditReport))]
[JsonSerializable(typeof(S5SummaryReport))]
internal partial class ValidationJsonContext : JsonSerializerContext;
