using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record CorpusPrepareRequest(string DatasetRoot, string AssetManifest, string RightsManifest, string Output,
    int MaximumRecords = 50_000_000, int MaximumSources = 96, int RowsPerShard = 50_000, int PilotPerFile = 0,
    bool Resume = false, string? SourceFilter = null, string? RecoverLedger = null, string? RecoverLedgerSha256 = null);
internal sealed record CorpusComposeRequest(CorpusInput[] Inputs, string Output, int MaximumRecords = 50_000_000, int RowsPerShard = 50_000);
internal sealed record CorpusInput(string Root, string ManifestSha256);
internal sealed record CorpusQueueRequest(string[] Corpora, string Output, int MaximumQuestions = 20_000, int SamplesPerSource = 64, int Seeds = 0, bool PilotOnly = false);
internal sealed record CorpusSyntheticState(string Case, string Scenario, string Scope, string Rule, string[] Facts);
internal sealed record CorpusTeacherRequest(string QueueRoot, string Output, string Endpoint, string Model, string TeacherId,
    string ModelRevision, string License, string ModelSha256, int MaximumRecords = 24, int MaximumHttpAttempts = 24,
    int RequestTimeoutSeconds = 60, string Protocol = "responses", bool Resume = false, string? CredentialEnvironmentVariable = null);
internal sealed record CorpusAdmissionRequest(string Records, string Output, int MaximumRecords = 20_000);
internal sealed record CorpusDatasetBuildRequest(string SourceRoot, string Output, string Mode, string ManifestSha256,
    string CandidatesSha256, int MaximumRecords = 1024, string? RecordsFile = null, string? ManifestFile = null);
internal sealed record CorpusResearchPlanRequest(string Seeds, string MetadataRoot, string Output, int MaximumSources = 96, int MaximumFiles = 50_000);
internal sealed record CorpusBindRequest(string Root, string SourceRoot, string Output, string ManifestSha256);
internal sealed record CorpusBinding(string ManifestSha256, string ExclusionsSha256, string SourceRightsSha256, string SourceAssetsSha256,
    string SourceAssetsPath, string SourceRoot, bool FormalTrainingAdmitted = false);
internal sealed record CorpusResearchCheckRequest(string Root, string[] Plans, string[] Ledgers, string Output, int MaximumFiles = 50_000, int MaximumLedgerRows = 200_000, bool VerifyHashes = true);
internal sealed record CorpusSeedCheckRequest(string Binary, string BinarySha256, string Parallel, string ParallelSha256,
    string EvaluationManifest, string EvaluationManifestSha256, string EvaluationRoot, string Output, int MaximumRecords = 200_000);
internal sealed record CorpusInspectRequest(string DatasetRoot, string AssetManifest, string Output, int MaximumSources = 96, int RowsPerSource = 2);
internal sealed record CorpusEstimateRequest(string TrainingReport, string TrainingReportSha256, string Profile,
    string ProfileSha256, string Plan, string PlanSha256, long[] Updates, string Output);
internal sealed record CorpusEstimateScenario(long Updates, double HistoricalAmortizedSeconds, double HistoricalMaximumStepSeconds,
    double HistoricalConservativeFormulaSeconds, long HypotheticalCheckpointEvery);
internal sealed record CorpusEstimateReport(string Status, string RunId, long HistoricalUpdates, double HistoricalSecondsPerUpdate,
    double HistoricalMaximumStepSeconds, CorpusEstimateScenario[] Scenarios, string TrainingReportSha256,
    string ProfileSha256, string PlanSha256, bool CurrentThroughputVerified = false, bool GpuTrainingRun = false,
    bool RentalAuthorizedOrPurchased = false, string Meaning = "Pinned historical scenario arithmetic only; no current-corpus throughput, convergence, cost or quality prediction");
internal sealed record CorpusInspectionItem(string Source, string Path, string OriginalSplit, string FileSha256, long FileBytes,
    string Status, int SampleRows, string? SamplePath, string? SampleSha256, bool SampleAdapterValidated, string? Failure);
internal sealed record CorpusInspectionReport(string Status, CorpusInspectionItem[] Sources, bool PrivateRawSamples,
    bool TeacherCalled = false, bool FormalTrainingAdmitted = false, bool LabelQualityAssessed = false);
internal sealed record CorpusSourceCompleteness(string Dataset, int PlannedFiles, int DownloadedFiles, long DownloadedBytes,
    int MissingFiles, long MissingWholeBytes, long ResumablePartialBytes, bool Complete, bool FormalTrainingAdmitted = false);
internal sealed record CorpusSourceInventory(string Dataset, string Path, string SplitHint, string? SourceRevision, string SourceUrl,
    long Bytes, string Sha256, long? Rows, string? RowCountSource, string? RowCountError, bool RowsAreUniqueQuestions = false,
    string TrainingAdmission = "pending", string LabelQuality = "not_assessed");
internal sealed record CorpusSourceMissing(string Dataset, string Url, string Path, long? ExpectedBytes, string? ExpectedSha256,
    long PartialBytes, string? LatestAttemptStatus, string? LatestAttemptError);
internal sealed record CorpusResearchCheckReport(string Status, int PlannedFiles, int DownloadedFiles, long DownloadedBytes,
    long PhysicalRows, int RowCountedFiles, int CompleteSources, int MissingFiles, CorpusSourceCompleteness[] Sources,
    CorpusSourceInventory[] Inventory, CorpusSourceMissing[] Missing, bool AllSelectedFilesDownloaded,
    bool RowsAreUniqueQuestions = false, bool FormalTrainingAdmitted = false, bool PublicationAccepted = false);
internal sealed record CorpusSeedCheckReport(string Status, long BinaryRows, long ParallelRows, long MatchedPairs,
    long GoldNormalizationErrors, long CrossPrimitiveConsistencyErrors, long ParallelQueryErrors, int EvaluationSubsets,
    int DeclaredEvaluationFiles, string[] MissingEvaluationFiles, string PositiveEventMapping,
    bool GoldConstructionRecomputed = false, bool ModelCalibrationValidated = false, bool FormalTrainingAdmitted = false);
internal sealed record CorpusCandidate(string Id, string Text);
internal sealed record CorpusQuestion(string State, string Instruction, string Type, CorpusCandidate[] Candidates,
    double[] Target, string Language, string LabelKind, string LabelSemantics, string? SourceFamily = null,
    string? Suffix = null, string? SourceMember = null);
internal sealed record CorpusProvenance(string Dataset, string? Revision, string Path, string FileSha256, string OriginalSplit, long Row,
    string? License, string? LicenseEvidence, string? SourceMember = null);
internal sealed record CorpusRecord(int SchemaVersion, string RecordId, string Family, string OriginalStateId, string Split,
    string State, string Instruction, string Type, CorpusCandidate[] Candidates, double[] Target, string Language,
    string LabelKind, string LabelSemantics, string SourceLineSha256, long SourceRow, CorpusProvenance Source,
    string TrainingAdmission, string HumanReview, string? CandidateTransform = null);
internal sealed record CorpusShard(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("records")] long Records,
    [property: JsonPropertyName("bytes")] long Bytes,
    [property: JsonPropertyName("sha256")] string Sha256);
internal sealed record CorpusAsset(
    [property: JsonPropertyName("dataset")] string Dataset,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("bytes")] long Bytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("source_revision")] string? SourceRevision = null,
    [property: JsonPropertyName("split")] string? Split = null,
    [property: JsonPropertyName("license_evidence")] string? LicenseEvidence = null);
internal sealed record CorpusRights(
    [property: JsonPropertyName("dataset")] string Dataset,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("license")] string? License = null,
    [property: JsonPropertyName("evidence")] string? Evidence = null);
internal sealed record CorpusCoverage(string Source, string Path, long RowsRead, long RecordsWritten, long RowsFullyProcessed, bool Complete, bool Pilot);
internal sealed record CorpusManifest(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("stage")] int Stage,
    [property: JsonPropertyName("records_written")] long RecordsWritten,
    [property: JsonPropertyName("duplicates")] long Duplicates,
    [property: JsonPropertyName("conflicts")] long Conflicts,
    [property: JsonPropertyName("quarantined")] long Quarantined,
    [property: JsonPropertyName("shards")] CorpusShard[] Shards,
    [property: JsonPropertyName("source_coverage")] CorpusCoverage[] SourceCoverage,
    [property: JsonPropertyName("assets_sha256")] string AssetsSha256,
    [property: JsonPropertyName("rights_sha256")] string RightsSha256,
    [property: JsonPropertyName("formal_training_admitted")] bool FormalTrainingAdmitted = false,
    [property: JsonPropertyName("failure")] string? Failure = null,
    [property: JsonPropertyName("tooling_version")] string ToolingVersion = "csharp-corpus-v1",
    [property: JsonPropertyName("source_row_hash_semantics")] string SourceRowHashSemantics = "SHA256 canonical sorted UTF8 decoded JSON; not raw line bytes; raw file SHA retained",
    [property: JsonPropertyName("exclusions_mandatory")] string ExclusionsMandatory = "exclusions.jsonl",
    [property: JsonPropertyName("recovery_ledger_sha256")] string? RecoveryLedgerSha256 = null,
    [property: JsonPropertyName("pilot")] bool Pilot = false);
internal sealed record CorpusLedgerRow(string RecordId, string Status, string? Failure, string? RequestSha256, string? ResponseSha256,
    int? HttpStatus, string? ResponsePath, bool HttpAttempted, string QueueManifestSha256, int Attempt);
internal sealed record CorpusReference(string RecordId, bool Answerable, double[]? Target);
internal sealed record CorpusQueueRecord(string RecordId, string Family, string Split, string State, string Instruction, string Type,
    CorpusCandidate[] Candidates, string Language, CorpusProvenance Source, string SourceCorpusManifestSha256,
    string? OriginalFamily = null, string? OriginalDerivedSplit = null);
internal sealed record CorpusQueueManifest(CorpusOperationReport Report, string QuestionsSha256, string ReferenceSha256,
    long QuestionsBytes, long ReferenceBytes);
internal sealed record CorpusTeacherLabel(string RecordId, bool Answerable, double[]? Probabilities, string Evidence);
internal sealed record CorpusAnnotatedRecord(CorpusQueueRecord Question, CorpusTeacherLabel Label, string AnnotationUse,
    string TrainingAdmission, bool ReferenceAgreement, string TeacherId, string Model, string Revision, string License,
    string ModelSha256, string RequestSha256, string ResponseSha256, bool FormalTrainingAdmitted = false,
    string HumanReview = "pending", string TeacherEvidenceSemantics = "teacher_explanation_only_not_gold_or_execution_authorization",
    string LabelKind = "teacher_distribution", string LabelSemantics = "uncalibrated_teacher_annotation",
    string TeacherIdentitySemantics = "request configuration and gateway declaration; not independent upstream weight attestation");
internal sealed record CorpusOperationReport(string Status, string Output, long Requested, long Completed, long Failed,
    long Abstained, long Duplicates, bool FormalTrainingAdmitted, bool ActualTeacherCalled, string? Failure = null,
    bool SyntheticTransport = false, int HttpRequestsIssued = 0);
internal sealed record CorpusResearchAsset(string Dataset, string Revision, string Split, string Url, string Path, long? ExpectedBytes, string? ExpectedSha256);
internal sealed record CorpusDatasetAsset([property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256, [property: JsonPropertyName("bytes")] long Bytes);
internal sealed record CorpusResearchSource(string Id, string Verification, string? Revision, JsonElement License, string MetadataSha256,
    string SourceUrl, string TrainingAdmission = "pending_original_split_and_license_review", string? RequestedId = null);

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(CorpusPrepareRequest))]
[JsonSerializable(typeof(CorpusComposeRequest))]
[JsonSerializable(typeof(CorpusQueueRequest))]
[JsonSerializable(typeof(CorpusSyntheticState))]
[JsonSerializable(typeof(CorpusTeacherRequest))]
[JsonSerializable(typeof(CorpusAdmissionRequest))]
[JsonSerializable(typeof(CorpusDatasetBuildRequest))]
[JsonSerializable(typeof(CorpusResearchPlanRequest))]
[JsonSerializable(typeof(CorpusBindRequest))]
[JsonSerializable(typeof(CorpusBinding))]
[JsonSerializable(typeof(CorpusResearchCheckRequest))]
[JsonSerializable(typeof(CorpusSeedCheckRequest))]
[JsonSerializable(typeof(CorpusInspectRequest))]
[JsonSerializable(typeof(CorpusEstimateRequest))]
[JsonSerializable(typeof(CorpusEstimateReport))]
[JsonSerializable(typeof(CorpusInspectionReport))]
[JsonSerializable(typeof(CorpusResearchCheckReport))]
[JsonSerializable(typeof(CorpusSeedCheckReport))]
[JsonSerializable(typeof(CorpusAsset[]))]
[JsonSerializable(typeof(CorpusRights[]))]
[JsonSerializable(typeof(CorpusQuestion))]
[JsonSerializable(typeof(CorpusRecord))]
[JsonSerializable(typeof(CorpusShard[]))]
[JsonSerializable(typeof(CorpusManifest))]
[JsonSerializable(typeof(CorpusLedgerRow))]
[JsonSerializable(typeof(CorpusReference))]
[JsonSerializable(typeof(CorpusQueueRecord))]
[JsonSerializable(typeof(CorpusQueueManifest))]
[JsonSerializable(typeof(CorpusTeacherLabel))]
[JsonSerializable(typeof(CorpusAnnotatedRecord))]
[JsonSerializable(typeof(CorpusOperationReport))]
[JsonSerializable(typeof(CorpusResearchAsset[]))]
[JsonSerializable(typeof(CorpusResearchSource[]))]
[JsonSerializable(typeof(CorpusDatasetAsset[]))]
[JsonSerializable(typeof(CorpusDatasetAsset))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(double[]))]
[JsonSerializable(typeof(CorpusCandidate[]))]
internal partial class CorpusJsonContext : JsonSerializerContext;
