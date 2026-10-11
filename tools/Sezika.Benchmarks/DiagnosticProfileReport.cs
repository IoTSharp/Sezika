using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

namespace Sezika.Benchmarks;

internal sealed record DiagnosticProfileExpectedIdentity(string ModelId, string EncoderSha256,
    string TokenizerSha256, int HiddenSize, string Protocol, string LengthPolicy)
{
    internal DiagnosticHeadIdentity ToIdentity()
    {
        if (string.IsNullOrWhiteSpace(ModelId) || !ModelId.StartsWith("sezika/", StringComparison.Ordinal) || ModelId.Length > 256 ||
            !IndependentProfile.IsHash(EncoderSha256) || !IndependentProfile.IsHash(TokenizerSha256) ||
            HiddenSize is < 1 or > 2048 || Protocol != "decision-v1" || LengthPolicy != "strict")
            throw new InvalidDataException("诊断期望身份必须完整并绑定decision-v1/strict。");
        return new(ModelId, EncoderSha256, TokenizerSha256, HiddenSize, Protocol, LengthPolicy);
    }
}

internal sealed record DiagnosticProfileArtifact(string Kind, string Path, string Sha256, long Bytes,
    DateTime CreationUtc, DateTime LastWriteUtc);

internal sealed class DiagnosticProfileReport
{
    public int SchemaVersion => 1;
    public string Contract => "untrained-independent-e2e-diagnostic-v1";
    public string Status { get; set; } = "failed";
    public string ObservationScope => "DiagnosticDecisionV1Runtime.Evaluate; includes sequence construction; excludes validation/serialization/output";
    public string ForwardObservationScope => "underlying_diagnostic_pipeline.Score_call; does not establish encoder/kernel entry";
    public bool FixtureOnly { get; init; }
    public bool PerformanceAccepted => false;
    public bool QualityAccepted => false;
    public bool Calibrated => false;
    public bool TrainingAccepted => false;
    public bool LicenseApproved => false;
    public bool SealedTestAccepted => false;
    public int FormalSamples => 0;
    public double? EncoderMilliseconds => null;
    public double? HeadMilliseconds => null;
    public int? EncoderForwardsEntered => null;
    public int? EncoderForwardsCompleted => null;
    public MarkerFeatureIdentity? HeadIdentity => null;
    public string? FeatureSha256 => null;
    public string HeadOrigin => "untrained-diagnostic";
    public int CompletedUpdates => 0;
    public double? ObservedLoss => null;
    public string Backend { get; set; } = "not_started";
    public string Protocol => "decision-v1";
    public string? ModelId { get; set; }
    public string? SourceModelId { get; set; }
    public string? Revision { get; set; }
    public DiagnosticHeadIdentity? DiagnosticHeadIdentity { get; set; }
    public string? DiagnosticAssetSha256 { get; set; }
    public string? InitializationDescriptorSha256 { get; set; }
    public int? Seed { get; set; }
    public string? InitializationAlgorithm { get; set; }
    public int? AlgorithmVersion { get; set; }
    public string? InitializationDescriptorDomain { get; set; }
    public int PlannedRequests { get; init; }
    public int? RequestQuestions { get; set; }
    public int? PlannedQuestions { get; set; }
    public bool GpuResourcesStarted { get; set; }
    public bool? ModelDisposed { get; set; }
    public int? ActiveWorkspaces { get; set; }
    public long? WorkspaceBytes { get; set; }
    public bool? DeviceDisposeReturned { get; set; }
    public CudaDeviceInfo? Device { get; set; }
    public CudaMemorySnapshot? CudaAfterRuntimeRelease { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public string? Error { get; set; }
    public List<string> CleanupErrors { get; } = [];
    public List<DiagnosticProfileArtifact> Artifacts { get; } = [];
    public List<IndependentProfileSequenceObservation> Sequences { get; } = [];
    public List<ProfileRequestObservation> Requests { get; } = [];
    public string Runtime => RuntimeInformation.FrameworkDescription;
    public string Rid => RuntimeInformation.RuntimeIdentifier;
    public bool ManagedHostDetected => AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not null;
    public bool DynamicCodeSupported => RuntimeFeature.IsDynamicCodeSupported;
    public bool DynamicCodeCompiled => RuntimeFeature.IsDynamicCodeCompiled;
    public int ProcessId => Environment.ProcessId;
    internal Stopwatch? CleanupClock { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32, WriteIndented = true)]
[JsonSerializable(typeof(DiagnosticProfileExpectedIdentity))]
[JsonSerializable(typeof(DiagnosticProfileReport))]
internal partial class DiagnosticProfileJson : System.Text.Json.Serialization.JsonSerializerContext;
