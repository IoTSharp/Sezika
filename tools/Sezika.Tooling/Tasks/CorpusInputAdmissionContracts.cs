using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record CorpusInputAdmissionRequest(
    string PlanPath, string PlanSha256, string SourcePolicyPath, string SourcePolicySha256,
    string ModelReadbackReceiptPath, string ModelReadbackReceiptSha256,
    int MaximumFiles = 8192, long MaximumBytes = 137438953472);

// This receipt proves input bytes and their supplied preparation graph only.
// It does not load a model, run a tokenizer, or grant any downstream acceptance.
internal sealed record CorpusInputAdmissionReport
{
    public string Status { get; init; } = "FAIL";
    public string PlanPath { get; init; } = "";
    public string PlanSha256 { get; init; } = "";
    public string SourcePolicyPath { get; init; } = "";
    public string SourcePolicySha256 { get; init; } = "";
    public string ModelReadbackReceiptPath { get; init; } = "";
    public string ModelReadbackReceiptSha256 { get; init; } = "";
    public string CorpusRoot { get; init; } = "";
    public string BaseModelRoot { get; init; } = "";
    public string ModelCommit { get; init; } = "";
    public string? BaseEncoderSha256 { get; init; }
    public string? TokenizerSha256 { get; init; }
    public string? CorpusManifestSha256 { get; init; }
    public string? CorpusBindingsSha256 { get; init; }
    public string? ExclusionsSha256 { get; init; }
    public string? LengthReportSha256 { get; init; }
    public string? RecordsSha256 { get; init; }
    public bool InventoryComplete { get; init; }
    public int PlannedFiles { get; init; }
    public int VerifiedFiles { get; init; }
    public long PlannedBytes { get; init; }
    public long VerifiedBytes { get; init; }
    public int PlannedShards { get; init; }
    public int VerifiedShards { get; init; }
    public int PlannedSources { get; init; }
    public int VerifiedSources { get; init; }
    public string? Failure { get; init; }
    public bool FormalTrainingAdmitted => false;
    public bool GpuExecutionAccepted => false;
    public bool LicenseAccepted => false;
    public bool QualityAccepted => false;
    public bool PublicationAccepted => false;
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(CorpusInputAdmissionRequest))]
[JsonSerializable(typeof(CorpusInputAdmissionReport))]
internal partial class CorpusInputAdmissionJsonContext : JsonSerializerContext;
