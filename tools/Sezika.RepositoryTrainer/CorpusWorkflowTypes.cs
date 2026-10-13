using System.Text.Json.Serialization;

namespace Sezika.RepositoryTrainer;

// Global identities/epochs are immutable. A stage's update budget is a CLI argument,
// so ending a rental phase does not change the training plan or optimizer identity.
internal sealed record CorpusRunPlan(
    int SchemaVersion,string RunId,string CorpusRoot,string CorpusManifestSha256,string CorpusBindingsSha256,
    string LengthReportPath,string LengthReportSha256,string BaseModelRoot,string SourceCommit,string DatasetCommit,string ModelCommit,
    int Seed,int Epochs,long TrainRecords,int MaximumTokens,int CheckpointEvery,int ValidationPerSource,
    float EncoderRate,float HeadRate,float WeightDecay,ulong MaximumOwnedBytes,string ExpectedGpuUuid,bool ExperimentalOnly);
internal sealed record CorpusLengthFailure(string RecordId,string Source,string Split,string Failure);
internal sealed record CorpusLengthReceipt(
    string Status,string CorpusManifestSha256,string CorpusBindingsSha256,string TokenizerSha256,int MaximumTokens,
    long PhysicalRows,long ExcludedRows,long Accepted,long Rejected,long Train,long Development,
    int LongestTokens,string? LongestTrainingRecordId,int LongestTrainingTokens,string? LongestTrainingSource,
    Dictionary<string,long> TrainingBySource,string FailureLedgerSha256,bool GpuFeaturesComputed,bool FormalTrainingAdmitted)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]public bool CompleteScan {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string? SourcePolicySha256 {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string? ScanCompletionSha256 {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string? ParentCorpusManifestSha256 {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]public long OriginalExcludedRows {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]public long SourceScopeExcludedRows {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]public long TokenLimitExcludedRows {get;init;}
}
internal sealed record CorpusProfileReceipt(
    string Status,string RunId,InputIdentity Inputs,string GpuUuid,int MaximumTokensTested,double MaximumStepSeconds,
    double ElapsedSeconds,ulong PeakOwnedBytes,bool SyntheticMaximumLengthProbe,bool QualityAccepted)
{
    public double CheckpointSeconds {get;init;}
}
internal sealed record CorpusCursorReceipt(
    int SchemaVersion,string RunId,InputIdentity Inputs,long CompletedUpdates,long Epoch,long WithinEpoch,
    long TrainingRecordsPerEpoch,int PlannedEpochs,string CheckpointReceiptSha256,string ScheduleAlgorithm);
internal sealed record CorpusStageReceipt(
    string Status,string RunId,InputIdentity Inputs,string GpuUuid,int ProcessId,DateTime ProcessStartUtc,
    long InitialUpdates,long CompletedUpdates,long StageRequestedUpdates,long GlobalUpdates,long CompletedEpochs,
    long WithinEpoch,double MeanObservedLoss,double ElapsedSeconds,ulong PeakOwnedBytes,
    string CheckpointReceiptSha256,string CursorReceiptSha256,bool FullCorpusEpochCoverage,bool QualityAccepted,bool Calibrated)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string? StopReason {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]public int CheckpointEvery {get;init;}
}
internal sealed record CorpusPackageReceipt(
    int SchemaVersion,string RunId,string Method,string SourceCommit,string DatasetCommit,string ModelCommit,
    InputIdentity Inputs,Sezika.ModernBertConfig Config,long CompletedUpdates,string WeightsSha256,string TokenizerSha256,
    string CheckpointReceiptSha256,string TrainingReportSha256,string CorpusManifestSha256,string CorpusBindingsSha256,
    string License,string CalibrationStatus,bool QualityAccepted,bool AotAccepted)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string? SourceProvenanceSha256 {get;init;}
}
internal sealed record CorpusSchedulerReceipt(string Status,int Checks,string Algorithm,bool GpuTrainingRun,bool RealCorpusValidated);
internal sealed record CorpusValidationReceipt(string Status,string RunId,string PackageSha256,InputIdentity Inputs,
    string GpuUuid,int ProcessId,DateTime ProcessStartUtc,int OriginalRows,int RotatedRows,
    Dictionary<string,int> OriginalRowsBySourceAndSplit,ValidationRow[] Rows,ulong PeakOwnedBytes,bool QualityAccepted,bool Calibrated);

[JsonSerializable(typeof(CorpusRunPlan))]
[JsonSerializable(typeof(CorpusLengthFailure))]
[JsonSerializable(typeof(CorpusLengthReceipt))]
[JsonSerializable(typeof(CorpusProfileReceipt))]
[JsonSerializable(typeof(CorpusCursorReceipt))]
[JsonSerializable(typeof(CorpusStageReceipt))]
[JsonSerializable(typeof(CorpusPackageReceipt))]
[JsonSerializable(typeof(CorpusSchedulerReceipt))]
[JsonSerializable(typeof(CorpusValidationReceipt))]
[JsonSourceGenerationOptions(WriteIndented=false,UseStringEnumConverter=true)]
internal partial class CorpusWorkflowJson:JsonSerializerContext;
