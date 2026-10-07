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
    public string Scope => "One realized 46-token input; 25 selected layer checkpoints and marker vectors. Source independence additionally requires supervisor review of actual original-weight execution evidence. Environment versions/source hashes do not claim full wheel/transitive hash locking.";
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
