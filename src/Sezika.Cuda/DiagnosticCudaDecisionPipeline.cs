using Sezika;

namespace Sezika.Cuda;

/// <summary>Explicit zero-update diagnostic asset, with both encoder and three-row head forward on CUDA.</summary>
/// <remarks>Owns its resident scorer. The caller retains the device and model package.</remarks>
public sealed class DiagnosticCudaDecisionPipeline : IDiagnosticDecisionPipeline
{
    private readonly CudaMarkerLogitScorer _scorer;
    public DiagnosticHeadIdentity Identity { get; }
    public string DiagnosticAssetSha256 { get; }
    public string InitializationDescriptorSha256 { get; }
    public string Backend => "cuda-diagnostic-linear-marker-head";
    public int MaxTokens => _scorer.MaxTokens;

    public DiagnosticCudaDecisionPipeline(CudaDevice device, IndependentEncoderModelPackage model,
        DiagnosticHeadAsset head, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device); ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(head);
        cancellationToken.ThrowIfCancellationRequested();
        var id = head.Identity;
        if (model.IsDisposed || id.ModelId != model.ModelId || id.HiddenSize != model.Encoder.Config.HiddenSize ||
            id.HiddenSize != head.Head.HiddenSize || id.Protocol != "decision-v1" || id.LengthPolicy != "strict" ||
            !id.ModelId.StartsWith("sezika/", StringComparison.Ordinal) ||
            !SameHash(id.EncoderSha256, model.EncoderSha256) || !SameHash(id.TokenizerSha256, model.TokenizerSha256) ||
            !IsHash(head.FileSha256) || !IsHash(head.InitializationDescriptorSha256) ||
            head.Origin != DiagnosticHeadOrigin.UntrainedDiagnostic || head.CompletedUpdates != 0 || head.ObservedLoss is not null ||
            head.InitializationDescriptorDomain != DiagnosticHeadAssetStore.DescriptorDomain ||
            head.InitializationAlgorithm != DiagnosticHeadAssetStore.Algorithm || head.AlgorithmVersion != DiagnosticHeadAssetStore.AlgorithmVersion)
            throw new DecisionException("diagnostic_runtime_identity_mismatch", "CUDA diagnostic head differs from the loaded encoder or zero-update asset contract.");
        Identity = id; DiagnosticAssetSha256 = head.FileSha256; InitializationDescriptorSha256 = head.InitializationDescriptorSha256;
        _scorer = new CudaMarkerLogitScorer(device, model, head.Head, "diagnostic_pipeline_input_invalid",
            "diagnostic_runtime_value_invalid", "diagnostic", cancellationToken);
    }

    public double[] Score(ReadOnlySpan<int> tokenIds, MarkerQuestionType type, ReadOnlySpan<int> markerPositions,
        CancellationToken cancellationToken = default) => _scorer.Score(tokenIds, type, markerPositions, this, cancellationToken);

    public void Dispose() => _scorer.Dispose();

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool SameHash(string left, string right) => IsHash(left) && IsHash(right) && left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
