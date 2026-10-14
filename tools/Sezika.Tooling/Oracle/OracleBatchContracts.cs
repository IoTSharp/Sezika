using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record OracleFilePin(string Path, long Bytes, string Sha256);
internal sealed record OraclePackageFile(string RelativePath, long Bytes, string Sha256);
internal sealed record OraclePackagePin(string Root, OraclePackageFile[] Files);
internal sealed record OracleProgramPin(string Executable, OraclePackagePin RuntimeAndCode, OraclePackagePin Source);
internal sealed record OracleReferenceInputs(OracleFilePin Weights, OracleFilePin EncoderConfig, OracleFilePin AgentConfig,
    OracleFilePin Tokenizer, OracleFilePin TokenizerConfig, OracleFilePin SourceLock, OracleFilePin CommonSource, OracleFilePin AgentSource,
    OracleFilePin[] ProgramSources, OracleFilePin RuntimeExecutable, OracleFilePin BuildReceipt, OracleFilePin? RuntimeAssembly = null,
    long MaximumResidentBytes = 2147483648);
internal sealed record OracleBatchRequest(string TaskId, OracleFilePin Plan, OracleFilePin Contract,
    OraclePackagePin Model, OracleProgramPin? ReferenceTool, OracleProgramPin ActualTool, OracleProgramPin CompareTool,
    OracleReferenceInputs? ReferenceInputs, string OutputDirectory, int MaximumBatches = 1, int BatchOffset = 0,
    int TimeoutSeconds = 900, string? CancelFile = null, OracleFilePin? RecoveryRunIndex = null,
    OracleProgramPin? BrokerTool = null, bool RequireActualAot = false, OracleFilePin? LegacyFrozenReferenceIndex = null);
internal sealed record OraclePlan(string SchemaVersion, string DatasetSha256, int DatasetTotal, OraclePlanBatch[] Batches);
internal sealed record OraclePlanBatch(string Manifest, string ManifestSha256, int Offset, string[] CaseIds);
internal sealed record OracleLayaRequest(OracleFilePin Weights, OracleFilePin EncoderConfig, OracleFilePin AgentConfig,
    OracleFilePin Tokenizer, OracleFilePin TokenizerConfig, OracleFilePin SourceLock, OracleFilePin CommonSource, OracleFilePin AgentSource,
    OracleFilePin Cases, OracleFilePin Contract, OracleFilePin[] ProgramSources, OracleFilePin RuntimeExecutable,
    OracleFilePin? RuntimeAssembly, string OutputDirectory, string ProcessEvidenceDirectory, OracleFilePin BuildReceipt, int MaximumCases, int TimeoutSeconds,
    long MaximumResidentBytes, string? CancelFile);
internal sealed record OracleStageResult(string Stage, string Status, int? ExitCode, string ProcessReceipt,
    string? ProcessReceiptSha256, OracleFilePin? Output, bool SimulationOnly, OracleFilePin? InputRequest = null, OracleFilePin? ProducerReceipt = null);
internal sealed record OracleBatchResult(int PlanIndex, int Offset, string[] CaseIds, string Status,
    OracleFilePin Manifest, OracleFilePin? Reference, OracleFilePin? Actual, OracleFilePin? Comparison,
    bool? FullManifestPassed, OracleStageResult[] Stages, string? Error, string? RecoveredFrom = null);
internal sealed record OracleCaptureIndexEntry(string Capture, string CaptureSha256, string Manifest, string ManifestSha256);
internal sealed record OracleLegacyCaptureIndex(string SchemaVersion, OracleCaptureIndexEntry[] Batches);
internal sealed record OracleCaptureIndex(string SchemaVersion, OracleCaptureIndexEntry[] Batches, string ReferenceKind,
    bool SimulationOnly, string Scope = "Frozen completed captures for separate scoring; no quality, calibration, AOT or training acceptance");
internal sealed record OracleBatchReport(string SchemaVersion, string TaskId, string TaskFingerprint, string Status,
    OracleFilePin Plan, OracleFilePin Contract, string DeclaredDatasetSha256, int DatasetTotal,
    int BatchOffset, int PlannedBatches, int CompletedBatches, int RecoveredBatches, int NumericalDifferences,
    int RemainingBatches, OracleBatchResult[] Batches, double ElapsedSeconds, int TimeoutSeconds,
    double RemainingSeconds, bool SimulationOnly, string? Error,
    string Scoring = "NOT_RUN_SEPARATE_STAGE", string ModelInferenceAcceptance = "NOT_ACCEPTED",
    string NativeAotAcceptance = "NOT_ACCEPTED_BY_BATCH_ORCHESTRATION");
internal sealed record OracleTaskFingerprint(string TaskId, OracleFilePin Plan, OracleFilePin Contract,
    OraclePackagePin Model, OracleProgramPin? ReferenceTool, OracleProgramPin ActualTool, OracleProgramPin CompareTool,
    OracleReferenceInputs? ReferenceInputs, int MaximumBatches, int BatchOffset, bool RequireActualAot,
    OracleProgramPin? BrokerTool, OracleFilePin? LegacyFrozenReferenceIndex);
internal sealed record OracleStageInvocation(string Stage, int PlanIndex, string OutputPath, string ReferencePath,
    string ActualPath, OracleFilePin Manifest, ExecutionRequest Request, bool SimulationOnly);

[JsonSerializable(typeof(OracleBatchRequest))]
[JsonSerializable(typeof(OraclePlan))]
[JsonSerializable(typeof(OracleLayaRequest))]
[JsonSerializable(typeof(OracleStageResult))]
[JsonSerializable(typeof(OracleBatchResult))]
[JsonSerializable(typeof(OracleCaptureIndex))]
[JsonSerializable(typeof(OracleLegacyCaptureIndex))]
[JsonSerializable(typeof(OracleBatchReport))]
[JsonSerializable(typeof(OracleTaskFingerprint))]
[JsonSerializable(typeof(OracleFilePin))]
[JsonSerializable(typeof(OracleFilePin[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class OracleJsonContext : JsonSerializerContext;
