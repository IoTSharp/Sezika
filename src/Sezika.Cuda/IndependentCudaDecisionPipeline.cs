using Sezika;

namespace Sezika.Cuda;

/// <summary>Independent frozen encoder and three-row linear marker head using static generated CUDA Driver kernels.</summary>
/// <remarks>Owns its CUDA encoder, head weights and modules. The caller retains the CudaDevice and CPU model package.</remarks>
public sealed class IndependentCudaDecisionPipeline : IIndependentDecisionPipeline
{
    private readonly CudaMarkerLogitScorer _scorer;
    public MarkerFeatureIdentity Identity { get; }
    public string FeatureSha256 { get; }
    public string HeadSha256 { get; }
    public string Backend => "cuda-independent-linear-marker-head";
    public int MaxTokens => _scorer.MaxTokens;

    public IndependentCudaDecisionPipeline(CudaDevice device, IndependentEncoderModelPackage model,
        IndependentMarkerHeadAsset head, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(head);
        cancellationToken.ThrowIfCancellationRequested();
        var id = head.Identity;
        if (model.IsDisposed || id.ModelId != model.ModelId || id.HiddenSize != model.Encoder.Config.HiddenSize ||
            id.HiddenSize != head.Head.HiddenSize || id.Protocol != "decision-v1" || id.LengthPolicy != "strict" ||
            !id.ModelId.StartsWith("sezika/", StringComparison.Ordinal) ||
            !SameHash(id.EncoderSha256, model.EncoderSha256) || !SameHash(id.TokenizerSha256, model.TokenizerSha256) ||
            !IsHash(id.DataManifestSha256) || !IsHash(head.FeatureSha256) || !IsHash(head.FileSha256))
            throw new DecisionException("independent_runtime_identity_mismatch", "CUDA independent head differs from the loaded encoder or complete asset contract.");
        Identity = id; FeatureSha256 = head.FeatureSha256; HeadSha256 = head.FileSha256;
        _scorer = new CudaMarkerLogitScorer(device, model, head.Head, "independent_pipeline_input_invalid",
            "independent_runtime_value_invalid", "independent", cancellationToken);
    }

    public double[] Score(ReadOnlySpan<int> tokenIds, MarkerQuestionType type, ReadOnlySpan<int> markerPositions,
        CancellationToken cancellationToken = default) => _scorer.Score(tokenIds, type, markerPositions, this, cancellationToken);

    public void Dispose() => _scorer.Dispose();

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool SameHash(string left, string right) => IsHash(left) && IsHash(right) && left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
