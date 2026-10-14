using System.Text.Json.Serialization;

namespace Sezika.ReferenceTool;

internal sealed record FilePin(string Path, long Bytes, string Sha256);
internal sealed record ExportRequest(FilePin OriginalWeights, FilePin Config, FilePin Tokenizer, FilePin ActualCapture,
    FilePin Contract, FilePin[] UpstreamSources, FilePin[] ProgramSources, FilePin RuntimeExecutable,
    FilePin? RuntimeAssembly, FilePin BuildReceipt, string OutputDirectory,
    string ProcessEvidenceDirectory, int TimeoutSeconds = 1800, long MaximumResidentBytes = 2147483648,
    int MaximumOutputBytes = 20971520, string? CancelFile = null);
internal sealed record ReferenceContract(string SchemaVersion, string Status, string SourceModelId, string SourceRevision,
    string CaseId, string Protocol, string LengthPolicy, int TokenCount, int[] MarkerPositions,
    string TokenIdsSha256, string MarkerPositionsSha256, string RequestSha256,
    [property: JsonPropertyName("source_weights_sha256")] string WeightsSha256,
    [property: JsonPropertyName("source_weights_bytes")] long WeightsBytes, string ConfigSha256, string TokenizerSha256, string UpstreamVersion,
    string[] UpstreamSourceSha256, string[] ProgramSourceSha256);
internal sealed class ActualInput
{
    public int SchemaVersion { get; init; }
    public string Status { get; init; } = "";
    public string Kind { get; init; } = "";
    public required bool FixtureOnly { get; init; }
    public string CaseId { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string SourceModelId { get; init; } = "";
    public string SourceRevision { get; init; } = "";
    public string Protocol { get; init; } = "";
    public string LengthPolicy { get; init; } = "";
    public string IntegerDtype { get; init; } = "";
    public string IntegerLayout { get; init; } = "";
    public int ExpectedCheckpoints { get; init; }
    public int[] TokenIds { get; init; } = [];
    public int[] MarkerPositions { get; init; } = [];
    public InputArtifact TokenIdsArtifact { get; init; } = new();
    public InputArtifact MarkerPositionsArtifact { get; init; } = new();
    public InputArtifact Request { get; init; } = new();
    public InputArtifact[] Tensors { get; init; } = [];
}
internal sealed class InputArtifact
{
    public string Path { get; init; } = "";
    public long Bytes { get; init; }
    public string Sha256 { get; init; } = "";
}
internal sealed record TensorOutput(string Name, string Path, string Dtype, int[] Shape, string Layout, long Bytes, string Sha256);
internal sealed record WeightTensorProof(string Name, string StorageKey, long StorageOffset, long[] Shape, long Bytes, string Sha256);
internal sealed record ProcessProof(int Pid, int ParentPid, int[] ParentChain, DateTimeOffset? Started, ulong? StartTicks,
    string? BootId, string CommandLine, string? ScopeId, string Ownership,
    string? ExpectedExecCommandLine = null, string? ActualCommandLine = null);
internal sealed record ReferenceExecution(string Backend, int VectorLanes, string DotArithmetic, string NormStatistics,
    string Gelu, string Runtime, string OperatingSystem, string Architecture, string[] ActualArguments, int NumericalThreads);
internal sealed record Capture(string SchemaVersion, string Status, string Kind, bool FixtureOnly, bool UpstreamExecuted,
    string Implementation, string SourceModelId, string SourceRevision, string CaseId, string Protocol, string LengthPolicy,
    string ModelId, FilePin OriginalWeights, FilePin Config, FilePin Tokenizer, FilePin ActualCapture, FilePin Contract,
    FilePin[] UpstreamSources, FilePin[] ProgramSources, FilePin Executable, FilePin? ManagedAssembly,
    FilePin BuildReceipt, FilePin BuildExecution, FilePin[] RuntimeFiles, FilePin ExportRequest,
    FilePin ProcessEvidence, ProcessProof Process, InputArtifact TokenIdsArtifact, InputArtifact MarkerPositionsArtifact,
    InputArtifact Request, int[] TokenIds, int[] MarkerPositions, TensorOutput[] Tensors, TensorOutput? MarkerVectors,
    WeightTensorProof[] OriginalTensors, ReferenceExecution Execution, DateTimeOffset StartedUtc, double ElapsedSeconds, long ObservedPeakBytes,
    string? TerminalError, bool OriginalWeightsVerified, bool RepresentsParity = false, bool RepresentsQuality = false,
    bool RepresentsCalibration = false, string Scope = "One fixed 46-token C# independently ported numerical reference; actual upstream Python execution and parity remain separate");
internal sealed record ExportReceipt([property: JsonPropertyName("Status")] string Status, string OutputDirectory, string? CaptureSha256, int Checkpoints,
    int OriginalTensors, double ElapsedSeconds, long ObservedPeakBytes, string? Error,
    bool UpstreamExecuted = false, bool RepresentsParity = false);
internal sealed record CheckResult(string Name, bool Passed, string? Error);
internal sealed record CheckReport([property: JsonPropertyName("Status")] string Status, int Planned, int Completed, CheckResult[] Cases, bool FixtureOnly = true,
    bool OriginalWeightsVerified = false, bool UpstreamExecuted = false, bool RepresentsParity = false);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ExportRequest))]
[JsonSerializable(typeof(ReferenceContract))]
[JsonSerializable(typeof(ActualInput))]
[JsonSerializable(typeof(ProcessProof[]))]
[JsonSerializable(typeof(Capture))]
[JsonSerializable(typeof(ExportReceipt))]
[JsonSerializable(typeof(CheckReport))]
internal partial class ReferenceJson : JsonSerializerContext;

[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ProcessProof[]))]
internal partial class RegisteredProcessJson : JsonSerializerContext;
