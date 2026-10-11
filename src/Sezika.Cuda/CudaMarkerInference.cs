using Sezika;

namespace Sezika.Cuda;

/// <summary>Inference only: resident encoder and linear marker head with static CUDA Driver kernels.</summary>
/// <remarks>The caller must independently verify package provenance and owns the supplied device and host weights.</remarks>
public sealed class CudaMarkerInference : IDisposable
{
    private readonly CudaMarkerLogitScorer _scorer;

    public CudaMarkerInference(CudaDevice device, ModernBertConfig config, ModernBertWeights weights,
        LinearMarkerHead head, ulong maximumOwnedBytes, CancellationToken cancellationToken = default)
    {
        _scorer = new(device, config, weights, head, "repository_inference_input_invalid",
            "repository_inference_value_invalid", "repository", cancellationToken, maximumOwnedBytes);
    }

    public double[] Score(ReadOnlySpan<int> tokenIds, MarkerQuestionType type, ReadOnlySpan<int> markerPositions,
        CancellationToken cancellationToken = default) => _scorer.Score(tokenIds, type, markerPositions, this, cancellationToken);

    public void Dispose() => _scorer.Dispose();
}
