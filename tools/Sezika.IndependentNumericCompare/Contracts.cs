using System.Text.Json.Serialization;

namespace Sezika.IndependentNumericCompare;

internal sealed record Artifact(string Path, string Sha256, long Bytes, string? Verification = null, string? SourcePath = null);
internal sealed record TensorArtifact(string Name, string Path, string Dtype, int[] Shape, string Layout, long Bytes, string Sha256);
internal sealed record ProgramArtifact(string Path, string Sha256, long Bytes, string? Package = null, string? Version = null);
internal sealed record Execution
{
    public required string Python { get; init; }
    public required string Torch { get; init; }
    public required string Transformers { get; init; }
    public string? Numpy { get; init; }
    public required string ModernbertSourceSha256 { get; init; }
    public required string Backend { get; init; }
    public required string Attention { get; init; }
    public required int Threads { get; init; }
    public required bool Compile { get; init; }
    public required bool Autocast { get; init; }
    public required string Network { get; init; }
    public string? OriginalPickleLoad { get; init; }
    public string? InputOrigin { get; init; }
    public string? UpstreamSourceIdentity { get; init; }
}
internal sealed record ActualCapture
{
    public required int SchemaVersion { get; init; }
    public required string Status { get; init; }
    public required string Kind { get; init; }
    public required string ModelId { get; init; }
    public required string SourceModelId { get; init; }
    public required string SourceRevision { get; init; }
    public required string Protocol { get; init; }
    public required string LengthPolicy { get; init; }
    public required string Backend { get; init; }
    public required string CaseId { get; init; }
    public required bool FixtureOnly { get; init; }
    public required bool RepresentsUpstreamParity { get; init; }
    public required int ExpectedCheckpoints { get; init; }
    public required string IntegerDtype { get; init; }
    public required string IntegerLayout { get; init; }
    public required int[] TokenIds { get; init; }
    public required int[] MarkerPositions { get; init; }
    public required Artifact TokenIdsArtifact { get; init; }
    public required Artifact MarkerPositionsArtifact { get; init; }
    public required Artifact Request { get; init; }
    public required Artifact[] Assets { get; init; }
    public required TensorArtifact[] Tensors { get; init; }
    public required TensorArtifact MarkerVectors { get; init; }
    public required string FinalHiddenSha256 { get; init; }
}
internal sealed record ReferenceCapture
{
    public required string SchemaVersion { get; init; }
    public required string Status { get; init; }
    public required string Kind { get; init; }
    public required bool FixtureOnly { get; init; }
    public required string CaseId { get; init; }
    public required string SourceModelId { get; init; }
    public required string SourceRevision { get; init; }
    public required string SourceWeightsSha256 { get; init; }
    public required long SourceWeightsBytes { get; init; }
    public required bool OriginalWeightsVerified { get; init; }
    public required string ConfigSha256 { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required string ContractSha256 { get; init; }
    public required string Protocol { get; init; }
    public required string LengthPolicy { get; init; }
    public required string IntegerDtype { get; init; }
    public required string IntegerLayout { get; init; }
    public required int[] TokenIds { get; init; }
    public required int[] MarkerPositions { get; init; }
    public required Artifact TokenIdsArtifact { get; init; }
    public required Artifact MarkerPositionsArtifact { get; init; }
    public required Artifact Request { get; init; }
    public required ProgramArtifact OracleProgram { get; init; }
    public required ProgramArtifact UpstreamSource { get; init; }
    public required Execution Execution { get; init; }
    public required TensorArtifact[] Tensors { get; init; }
    public required TensorArtifact MarkerVectors { get; init; }
}
internal sealed record Tolerance(double Absolute, double Relative);
internal sealed record FrozenContract
{
    public required string SchemaVersion { get; init; }
    public required string Status { get; init; }
    public required string SourceModelId { get; init; }
    public required string SourceRevision { get; init; }
    public required string SourceWeightsSha256 { get; init; }
    public required long SourceWeightsBytes { get; init; }
    public required string ConfigSha256 { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required string ConvertedEncoderSha256 { get; init; }
    public required string CaseId { get; init; }
    public required string RequestSha256 { get; init; }
    public required string TokenIdsSha256 { get; init; }
    public required string MarkerPositionsSha256 { get; init; }
    public required int TokenCount { get; init; }
    public required int[] MarkerPositions { get; init; }
    public required int HiddenSize { get; init; }
    public required int LayerCount { get; init; }
    public required string Protocol { get; init; }
    public required string LengthPolicy { get; init; }
    public required string IntegerDtype { get; init; }
    public required string IntegerLayout { get; init; }
    public required string TensorDtype { get; init; }
    public required string TensorLayout { get; init; }
    public required int CheckpointCount { get; init; }
    public required int ComparisonCount { get; init; }
    public required string[] Checkpoints { get; init; }
    public required string MarkerVectorsName { get; init; }
    public required Execution Execution { get; init; }
    public required Dictionary<string, Tolerance> Tolerances { get; init; }
}
internal sealed record PackageManifest
{
    public required int SchemaVersion { get; init; }
    public required string ModelId { get; init; }
    public required string SourceModelId { get; init; }
    public required string SourceRevision { get; init; }
    public required string SourceWeightsSha256 { get; init; }
    public required string WeightsSha256 { get; init; }
    public required string TokenizerSha256 { get; init; }
}
internal sealed record TensorResult(string Name, int[] Shape, long ElementCount, long FailedElements,
    double MaximumAbsoluteError, double MaximumRelativeError, long MaximumAbsoluteErrorIndex, bool Passed);
internal sealed record ProcessIdentity(int Pid, DateTime StartedUtc, string CommandLine, string ParentEvidence);
internal sealed record ComparisonReport
{
    public string SchemaVersion { get; init; } = "sezika.independent-numeric-comparison.v1";
    public required string Status { get; init; }
    public required string CaseId { get; init; }
    public required string ActualSha256 { get; init; }
    public required string ReferenceSha256 { get; init; }
    public required string ContractSha256 { get; init; }
    public required string OracleProgramSha256 { get; init; }
    public required string UpstreamSourceSha256 { get; init; }
    public required bool FixtureOnly { get; init; }
    public required bool Passed { get; init; }
    public required bool AcceptedIndependentReference { get; init; }
    public bool AcceptedPortDerivedReference { get; init; }
    public string ReferenceKind { get; init; } = "upstream_original_weights_capture";
    public bool UpstreamExecuted { get; init; }
    public int ComparisonDenominator { get; init; } = 26;
    public required int ComparedCount { get; init; }
    public required int FailedComparisons { get; init; }
    public required long ElementDenominator { get; init; }
    public required long FailedElements { get; init; }
    public required TensorResult[] Results { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required ProcessIdentity Process { get; init; }
    public double RelativeErrorDenominatorFloor => 1e-12;
    public bool RepresentsFullOperatorParity => false;
    public bool RepresentsTokenizerOracle => false;
    public bool RepresentsModelQuality => false;
    public bool RepresentsTraining => false;
    public bool RepresentsCalibration => false;
    public bool RepresentsAot => false;
    public bool RepresentsBenchmark => false;
    public string Scope => ReferenceKind == "independently_ported_original_weights_capture"
        ? "One realized 46-token independently ported C# reference; 25 checkpoints plus marker vectors; no upstream Python execution or old upstream-reference gate acceptance."
        : "One realized 46-token input; 25 selected layer checkpoints and marker vectors. Source independence additionally requires supervisor review of actual original-weight execution evidence. Environment versions/source hashes do not claim full wheel/transitive hash locking.";
}
internal sealed record SelfTestCase(int Number, string Name, bool Passed, int AssertionVariants);
internal sealed record SelfTestReport(string SchemaVersion, bool FixtureOnly, bool AcceptedIndependentReference, int Count, int AssertionVariants, SelfTestCase[] Cases);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ActualCapture))]
[JsonSerializable(typeof(ReferenceCapture))]
[JsonSerializable(typeof(FrozenContract))]
[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(ComparisonReport))]
[JsonSerializable(typeof(SelfTestReport))]
internal partial class CompareJsonContext : JsonSerializerContext;

internal sealed record ProofPin(string Path, long Bytes, string Sha256);
internal sealed record NumericView(string SourceModelId, string SourceRevision, string SourceWeightsSha256, long SourceWeightsBytes,
    string ConfigSha256, string TokenizerSha256, string ConvertedEncoderSha256, string CaseId, string RequestSha256,
    string TokenIdsSha256, string MarkerPositionsSha256, string Protocol, string LengthPolicy, int[] MarkerPositions,
    string[] Checkpoints, string MarkerVectorsName, Dictionary<string, Tolerance> Tolerances);
internal sealed record PortContract
{
    public required string SchemaVersion { get; init; }
    public required string Status { get; init; }
    public required string SourceModelId { get; init; }
    public required string SourceRevision { get; init; }
    public required string SourceWeightsSha256 { get; init; }
    public required long SourceWeightsBytes { get; init; }
    public required string ConfigSha256 { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required string ConvertedEncoderSha256 { get; init; }
    public required string CaseId { get; init; }
    public required string RequestSha256 { get; init; }
    public required string TokenIdsSha256 { get; init; }
    public required string MarkerPositionsSha256 { get; init; }
    public required int TokenCount { get; init; }
    public required int[] MarkerPositions { get; init; }
    public required int HiddenSize { get; init; }
    public required int LayerCount { get; init; }
    public required string Protocol { get; init; }
    public required string LengthPolicy { get; init; }
    public required string IntegerDtype { get; init; }
    public required string IntegerLayout { get; init; }
    public required string TensorDtype { get; init; }
    public required string TensorLayout { get; init; }
    public required int CheckpointCount { get; init; }
    public required int ComparisonCount { get; init; }
    public required string[] Checkpoints { get; init; }
    public required string MarkerVectorsName { get; init; }
    public required Dictionary<string, Tolerance> Tolerances { get; init; }
    public required string UpstreamVersion { get; init; }
    public required string[] UpstreamSourceSha256 { get; init; }
    public required string[] ProgramSourceSha256 { get; init; }
    public required string ProofPolicy { get; init; }
    [JsonIgnore] public NumericView Numeric => new(SourceModelId, SourceRevision, SourceWeightsSha256, SourceWeightsBytes, ConfigSha256,
        TokenizerSha256, ConvertedEncoderSha256, CaseId, RequestSha256, TokenIdsSha256, MarkerPositionsSha256, Protocol,
        LengthPolicy, MarkerPositions, Checkpoints, MarkerVectorsName, Tolerances);
}
internal sealed record PortProcess(int Pid, int ParentPid, int[] ParentChain, DateTimeOffset? Started, ulong? StartTicks,
    string? BootId, string CommandLine, string? ScopeId, string Ownership,
    string? ExpectedExecCommandLine = null, string? ActualCommandLine = null);
internal sealed record PortExecution(string Backend, int VectorLanes, string DotArithmetic, string NormStatistics,
    string Gelu, string Runtime, string OperatingSystem, string Architecture, string[] ActualArguments, int NumericalThreads);
internal sealed record OriginalTensorProof(string Name, string StorageKey, long StorageOffset, long[] Shape, long Bytes, string Sha256);
internal sealed record PortCapture
{
    public required string SchemaVersion { get; init; }
    public required string Status { get; init; }
    public required string Kind { get; init; }
    public required bool FixtureOnly { get; init; }
    public required bool UpstreamExecuted { get; init; }
    public required string Implementation { get; init; }
    public required string SourceModelId { get; init; }
    public required string SourceRevision { get; init; }
    public required string CaseId { get; init; }
    public required string Protocol { get; init; }
    public required string LengthPolicy { get; init; }
    public required string ModelId { get; init; }
    public required ProofPin OriginalWeights { get; init; }
    public required ProofPin Config { get; init; }
    public required ProofPin Tokenizer { get; init; }
    public required ProofPin ActualCapture { get; init; }
    public required ProofPin Contract { get; init; }
    public required ProofPin[] UpstreamSources { get; init; }
    public required ProofPin[] ProgramSources { get; init; }
    public required ProofPin Executable { get; init; }
    public ProofPin? ManagedAssembly { get; init; }
    public required ProofPin BuildReceipt { get; init; }
    public required ProofPin BuildExecution { get; init; }
    public required ProofPin[] RuntimeFiles { get; init; }
    public required ProofPin ExportRequest { get; init; }
    public required ProofPin ProcessEvidence { get; init; }
    public required PortProcess Process { get; init; }
    public required Artifact TokenIdsArtifact { get; init; }
    public required Artifact MarkerPositionsArtifact { get; init; }
    public required Artifact Request { get; init; }
    public required int[] TokenIds { get; init; }
    public required int[] MarkerPositions { get; init; }
    public required TensorArtifact[] Tensors { get; init; }
    public required TensorArtifact MarkerVectors { get; init; }
    public required OriginalTensorProof[] OriginalTensors { get; init; }
    public required PortExecution Execution { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required long ObservedPeakBytes { get; init; }
    public string? TerminalError { get; init; }
    public required bool OriginalWeightsVerified { get; init; }
    public required bool RepresentsParity { get; init; }
    public required bool RepresentsQuality { get; init; }
    public required bool RepresentsCalibration { get; init; }
}
internal sealed record PortProofRequest(ProofPin TemplateContract, ProofPin RunContract, ProofPin Reference, ProofPin Actual,
    ProofPin OriginalWeights, ProofPin Config, ProofPin Tokenizer, ProofPin[] UpstreamSources, ProofPin[] ProgramSources,
    ProofPin Executable, ProofPin? ManagedAssembly, ProofPin ProcessRegistration, ProofPin ExecutorReceipt,
    ProofPin ExportReceipt, ProofPin BuildReceipt, ProofPin ExportRequest, ProofPin BuildExecution, ProofPin[] RuntimeFiles);
internal sealed record PortExportRequest(ProofPin OriginalWeights, ProofPin Config, ProofPin Tokenizer, ProofPin ActualCapture,
    ProofPin Contract, ProofPin[] UpstreamSources, ProofPin[] ProgramSources, ProofPin RuntimeExecutable,
    ProofPin? RuntimeAssembly, ProofPin BuildReceipt, string OutputDirectory, string ProcessEvidenceDirectory,
    int TimeoutSeconds = 1800, long MaximumResidentBytes = 2147483648, int MaximumOutputBytes = 20971520,
    string? CancelFile = null);
internal sealed record PortBuildProof([property: JsonPropertyName("Status")] string Status, bool IndependentlyVerified, ProofPin[] Sources, ProofPin Executable, ProofPin? ManagedAssembly);
internal sealed record PortExportProof([property: JsonPropertyName("Status")] string Status, string OutputDirectory,
    string? CaptureSha256, int Checkpoints, int OriginalTensors, double ElapsedSeconds, long ObservedPeakBytes,
    string? Error, bool UpstreamExecuted, bool RepresentsParity);
internal sealed record ExecutorProcess(int Pid, int ParentPid, int[] ParentChain, DateTimeOffset? Started, ulong? StartTicks,
    string? BootId, string CommandLine, string? AfterExecCommandLine, string? ExpectedExecCommandLine, string State,
    string Ownership, string? ScopeId, string? ExitProof, string? PendingObservationError);
internal sealed record ExecutorProof(string Status, string ExecutionStatus, string CleanupStatus, int? ExitCode, int? RootPid,
    string Platform, string Executable, string[] ArgumentList, string OutputDirectory, string OwnershipId,
    DateTimeOffset StartedUtc, double ElapsedSeconds, bool InputComplete, bool OutputComplete,
    bool OutputLimitExceeded, int RemainingProcesses, ExecutorProcess[] Processes, string[] Errors);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(PortContract))]
[JsonSerializable(typeof(PortCapture))]
[JsonSerializable(typeof(PortProofRequest))]
[JsonSerializable(typeof(PortExportRequest))]
[JsonSerializable(typeof(PortBuildProof))]
[JsonSerializable(typeof(PortExportProof))]
[JsonSerializable(typeof(ProofPin[]))]
internal partial class PortJson : JsonSerializerContext;

[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ExecutorProof))]
[JsonSerializable(typeof(ExecutorProcess[]))]
internal partial class ExecutorJson : JsonSerializerContext;
