using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

namespace Sezika.IndependentSmoke;

internal sealed record Artifact(string Path, string Sha256, long Bytes);
internal sealed record NativeIdentity(bool DynamicCodeSupported, bool DynamicCodeCompiled, bool TrustedPlatformAssembliesNull)
{
    internal static NativeIdentity Current() => new(RuntimeFeature.IsDynamicCodeSupported, RuntimeFeature.IsDynamicCodeCompiled,
        AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is null);
    internal bool IsNative => !DynamicCodeSupported && !DynamicCodeCompiled && TrustedPlatformAssembliesNull;
}
internal sealed record DiagnosticDescriptor(string Schema, string ModelId, string EncoderSha256, string TokenizerSha256,
    string RequestSha256, string TokenIdsSha256, string MarkerPositionsSha256, int Rows, int HiddenSize,
    int Seed, string Algorithm, string AlgorithmSha256, string MemorySha256, int MemoryBytes,
    string IdentityTokenSemantics = "Descriptor hashes only: not training manifest, feature cache or head file.");
internal sealed record Answer(string QuestionId, string Type, string[] CandidateIds, double[] Probabilities,
    int SelectedIndex, double Concentration, string Status);
internal sealed record ResourceClosure(bool ModelDisposed, long WorkspaceBytes, int ActiveWorkspaces,
    CudaMemorySnapshot? Before, CudaMemorySnapshot? After, long PrecancelLaunchDelta,
    bool CallerDeviceDisposed, string[] CleanupErrors)
{
    public CudaMemorySnapshot? PrecancelBefore { get; init; }
    public CudaMemorySnapshot? PrecancelAfter { get; init; }
    public CudaMemorySnapshot? ActiveSnapshot { get; init; }
}
internal sealed record SmokeReport(string Schema, string Status, string Backend, string ModelId,
    string SourceModelId, string Revision, string SourceWeightsSha256, string EncoderSha256, string TokenizerSha256,
    Artifact Capture, Artifact Request, Artifact SourceConfig, Artifact SourceLock, Artifact SourceWeights,
    Artifact SourceFreeze, Artifact ProcessExecutable, DiagnosticDescriptor DiagnosticHead, string DescriptorSha256,
    NativeIdentity Native, int TokenCount, int[] MarkerPositions, Answer[] Answers, CudaDeviceInfo? Device,
    CudaTelemetrySnapshot? Telemetry, ResourceClosure Closure, int Pid, DateTimeOffset StartedUtc, double ElapsedSeconds)
{
    public string HeadOrigin => "diagnostic_initialization";
    public bool HeadFilePresent => false;
    public bool HeadTraining => false;
    public bool FormalTraining => false;
    public bool TrainingDataPresent => false;
    public bool FeatureCacheLoaded => false;
    public bool Quality => false;
    public bool Parity => false;
    public bool Calibration => false;
    public bool Benchmark => false;
    public bool LicenseApproval => false;
    public bool SourceFreezeContentsValidatedByProgram => false;
    public string SourceFreezeScope => "File bytes/hash recorded only; root separately verifies contents, current source and published binary.";
    public string NativeScope => "Actual flags; require-aot rejects managed execution. This diagnostic head is not a trained deployment binding.";
    public string MemoryPolicy => "Checkpoint observations <=4GiB host/3GiB owned CUDA; external timeout remains required for native calls.";
}
internal sealed record ProbeReport(string Status, NativeIdentity Native, CudaDeviceInfo Device,
    float[] Result, CudaMemorySnapshot After, CudaTelemetrySnapshot Telemetry, bool CallerDeviceDisposed);
internal sealed record FailureReport(string Status, string Error, string Detail, ResourceClosure? Closure,
    int Pid, DateTimeOffset StartedUtc);
internal sealed record InputArtifact
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public required long Bytes { get; init; }
}
internal sealed record CaptureInput
{
    public required int SchemaVersion { get; init; }
    public required string Status { get; init; }
    public required string Kind { get; init; }
    public required bool FixtureOnly { get; init; }
    public required string CaseId { get; init; }
    public required string ModelId { get; init; }
    public required string SourceModelId { get; init; }
    public required string SourceRevision { get; init; }
    public required string Protocol { get; init; }
    public required string LengthPolicy { get; init; }
    public required string IntegerDtype { get; init; }
    public required string IntegerLayout { get; init; }
    public required int[] TokenIds { get; init; }
    public required int[] MarkerPositions { get; init; }
    public required InputArtifact Request { get; init; }
    public required InputArtifact TokenIdsArtifact { get; init; }
    public required InputArtifact MarkerPositionsArtifact { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(CaptureInput))]
[JsonSerializable(typeof(DiagnosticDescriptor))]
[JsonSerializable(typeof(SmokeReport))]
[JsonSerializable(typeof(ProbeReport))]
[JsonSerializable(typeof(FailureReport))]
internal partial class SmokeJson : JsonSerializerContext;

internal static class Diagnostic
{
    internal const string RequestHash = "75B7055B38C78E7563D1D14DE9BC5F4EE9D63A7FF31E637B8C77F3EBF41D9917";
    internal const string TokensHash = "BAB3A41CDB62FC4D5E3353D3D88F6BBDCBD0D3CEC9C3DB537841F09604D2D80B";
    internal const string MarkersHash = "6E45294D69ECA0660E53BB653811E6AEC1BD269B164EE6174D1961D9D5B94396";
    internal const string ConfigHash = "47B40FD2E1DF8299426DD5F4BB18C28F028CFAFCB51B73645F83E596D187EB37";
    internal const string LockHash = "034E7ADC8F223962C5C914136860CBE869A0B5F9E29917FD1CE5DF59C7F4D00F";
    internal const string Algorithm = "diagnostic-linear-v1: seed=0; i=0..2303; ((i*17+11)%101-50)/10000f; row-major F32 little-endian";
    internal static (DiagnosticDescriptor Descriptor, IndependentMarkerHeadAsset Asset) Create(Budget budget)
    {
        var weights = new float[3 * 768]; var bytes = new byte[weights.Length * 4];
        for (var i = 0; i < weights.Length; i++)
        {
            budget.Check(); weights[i] = ((i * 17 + 11) % 101 - 50) / 10000f;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), weights[i]);
        }
        var descriptor = new DiagnosticDescriptor("sezika.diagnostic-head.v1", IndependentModelLoader.ModelId,
            IndependentModelLoader.ConvertedEncoderSha256, IndependentModelLoader.TokenizerSha256,
            RequestHash, TokensHash, MarkersHash, 3, 768, 0, Algorithm, ArtifactIO.Hash(Encoding.UTF8.GetBytes(Algorithm)),
            ArtifactIO.Hash(bytes), bytes.Length);
        var descriptorHash = ArtifactIO.Hash(JsonSerializer.SerializeToUtf8Bytes(descriptor, SmokeJson.Default.DiagnosticDescriptor));
        var identity = new MarkerFeatureIdentity(descriptor.ModelId, descriptor.EncoderSha256, descriptor.TokenizerSha256,
            descriptorHash, 768);
        // These tokens deliberately identify the diagnostic descriptor, not nonexistent training/files.
        var asset = new IndependentMarkerHeadAsset(identity, descriptorHash, 0, 0, 0, 0,
            new LinearMarkerHead(768, weights), descriptorHash);
        var actual = asset.Head.CopyWeights(); var actualBytes = new byte[actual.Length * 4];
        for (var i = 0; i < actual.Length; i++) { budget.Check(); ArtifactIO.Require(float.IsFinite(actual[i]), "Actual diagnostic head is not finite"); BinaryPrimitives.WriteSingleLittleEndian(actualBytes.AsSpan(i * 4, 4), actual[i]); }
        ArtifactIO.Require(actual.Length == 2304 && ArtifactIO.Hash(actualBytes) == descriptor.MemorySha256, "Actual head memory differs from descriptor");
        Array.Clear(actual); Array.Clear(actualBytes);
        Array.Clear(weights); Array.Clear(bytes);
        return (descriptor, asset);
    }
}
