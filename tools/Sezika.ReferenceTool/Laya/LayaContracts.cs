using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.ReferenceTool.Laya;

internal sealed record LayaExportRequest(FilePin Weights, FilePin EncoderConfig, FilePin AgentConfig, FilePin Tokenizer, FilePin TokenizerConfig,
    FilePin SourceLock, FilePin CommonSource, FilePin AgentSource, FilePin Cases, FilePin Contract,
    FilePin[] ProgramSources, FilePin RuntimeExecutable, FilePin? RuntimeAssembly, string OutputDirectory,
    string ProcessEvidenceDirectory, FilePin BuildReceipt, int MaximumCases = 46, int TimeoutSeconds = 1800,
    long MaximumResidentBytes = 2147483648, string? CancelFile = null);
internal sealed record LayaManifest(string SchemaVersion, Dictionary<string, JsonElement> QuestionTemplates,
    Dictionary<string, JsonElement> StateTemplates, LayaCaseSpec[] Cases);
internal sealed class LayaCaseSpec
{
    public string Id { get; init; } = "";
    public string Primitive { get; init; } = "";
    public string Language { get; init; } = "";
    public JsonElement Question { get; init; }
    public string? QuestionRef { get; init; }
    public JsonElement QuestionRecipe { get; init; }
    public JsonElement State { get; init; }
    public string? StateRef { get; init; }
    public string[] Tags { get; init; } = [];
}
internal sealed record LayaCaseInput(JsonElement State, JsonElement Question, string Language, int MaxLen = 1024, int HeadMaxLen = 256);
internal sealed record LayaPrediction(string? ChoiceLabel, double? Score, bool? Boolean, double? ProbabilityTrue);
internal sealed record LayaFailure(string Stage, string Type, string Message, string PortType);
internal sealed class LayaCaseRecord
{
    public string Id { get; init; } = "";
    public string Primitive { get; init; } = "";
    public string Status { get; set; } = "failed";
    public LayaCaseInput Input { get; init; } = null!;
    public string[] Tags { get; init; } = [];
    public int[]? TokenIds { get; set; }
    public int[]? MarkerPositions { get; set; }
    public string[]? CandidateLabels { get; set; }
    public float[]? RawLogits { get; set; }
    public float[]? Probabilities { get; set; }
    public float[]? ActProbabilities { get; set; }
    public LayaPrediction? Prediction { get; set; }
    public LayaFailure? Failure { get; set; }
    public SequenceDetails? SequenceDiagnostics { get; set; }
}
internal sealed record SequenceDetails(string SerializedState, string SanitizedState, string RenderedInstruction,
    string[] RenderedOptions, int OriginalInstructionTokens, int RetainedInstructionTokens, int[] OriginalOptionTokens,
    int[] RetainedOptionTokens, int PrefixTokensIncludingSpecials, int UntruncatedTotalTokens, int OriginalStateTokens,
    int RetainedStateTokens, int DroppedStateTokens, int StateRetainedStart, string StateTruncationDirection, bool MaskTextRemoved);
internal sealed record PortProcess(int Pid, int ParentPid, int[] ParentChain, DateTimeOffset? Started, ulong? StartTicks,
    string? BootId, string CommandLine, string? ExpectedExecCommandLine, string? ScopeId, string Ownership,
    string? ActualCommandLine = null);
internal sealed record TokenizerRoles(string ClsToken, int ClsId, string SepToken, int SepId, string MaskToken, int MaskId,
    string PadToken, int PadId);
internal sealed record LayaProvenance(string ModelId, string ModelRevision, string WeightsSha256, string TokenizerSha256,
    string TokenizerConfigSha256, TokenizerRoles TokenizerRoles, string EncoderConfigSha256, string AgentConfigSha256, string UpstreamSourceRevision, string UpstreamVersion,
    string CasesSha256, string ContractSha256, string SourceLockSha256, string UpstreamAgentSha256, string UpstreamCommonSha256,
    FilePin[] ProgramSources, FilePin RuntimeExecutable, FilePin? RuntimeAssembly, FilePin BuildReceipt, FilePin BuildExecution,
    FilePin ProcessEvidence, PortProcess Process, FilePin ExportRequest, FilePin[] RuntimeFiles,
    string Implementation = "laya-4066d5-transformers-4.57.1-csharp-fp32-v2",
    string Backend = "independent_csharp_cpu_fp32", int MaxLen = 1024, int HeadMaxLen = 256, int BatchSize = 1,
    string TemperaturePolicy = "checkpoint_fixed_1_no_overrides", bool UpstreamExecuted = false,
    float EffectiveGlobalRopeTheta = 160000, float EffectiveLocalRopeTheta = 160000,
    bool RepresentsParity = false, bool RepresentsQuality = false, bool RepresentsCalibration = false);
internal sealed record LayaCapture(string SchemaVersion, string Kind, bool FixtureOnly, bool UpstreamExecuted,
    LayaProvenance Provenance, string MeasurementStatus, int TotalManifestCases, int SelectedCaseCount,
    LayaCaseRecord[] Cases);
internal sealed record LayaExportReceipt(string Status, string OutputDirectory, FilePin? Capture, int Planned, int Completed,
    int Answered, int Failed, double ElapsedSeconds, long ObservedPeakBytes, string? Error, bool UpstreamExecuted = false,
    bool RepresentsParity = false);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(LayaExportRequest))]
[JsonSerializable(typeof(LayaManifest))]
[JsonSerializable(typeof(LayaCapture))]
[JsonSerializable(typeof(LayaExportReceipt))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string))]
internal partial class LayaJson : JsonSerializerContext;

[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(PortProcess[]))]
internal partial class PortRegistrationJson : JsonSerializerContext;
