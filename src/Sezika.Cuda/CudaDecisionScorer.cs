using Sezika;

namespace Sezika.Cuda;

/// <summary>
/// Uses the CUDA Driver GEMM probe as a real GPU decision-head scorer. The
/// encoder can remain on the CPU until the full GPU encoder kernels are loaded;
/// pooled candidate states and the linear head are transferred once per request.
/// </summary>
public sealed class CudaDecisionScorer : IDecisionScorer, IDisposable
{
    private readonly CudaGemm _gemm;
    private bool _disposed;

    public CudaDecisionScorer(CudaDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _gemm = device.CreateGemm();
    }

    public void Score(ReadOnlySpan<float> pooledStates, int candidateCount, ReadOnlySpan<float> weights, float bias, Span<float> logits, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (candidateCount <= 0 || weights.Length == 0 || pooledStates.Length != checked(candidateCount * weights.Length) || logits.Length != candidateCount)
            throw new DecisionException("model_tensor_shape_invalid", "CUDA decision scorer dimensions are invalid.");
        cancellationToken.ThrowIfCancellationRequested();
        var right = weights.ToArray();
        var gpuOutput = new float[candidateCount];
        _gemm.Execute(pooledStates, candidateCount, weights.Length, right, 1, gpuOutput);
        for (var i = 0; i < logits.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logits[i] = gpuOutput[i] + bias;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _gemm.Dispose();
        }
    }
}
