namespace Sezika;

/// <summary>Computes one logit per candidate from pooled encoder states.</summary>
public interface IDecisionScorer
{
    void Score(ReadOnlySpan<float> pooledStates, int candidateCount, ReadOnlySpan<float> weights, float bias, Span<float> logits, CancellationToken cancellationToken = default);
}

public sealed class CpuDecisionScorer : IDecisionScorer
{
    public void Score(ReadOnlySpan<float> pooledStates, int candidateCount, ReadOnlySpan<float> weights, float bias, Span<float> logits, CancellationToken cancellationToken = default)
    {
        if (candidateCount <= 0 || weights.Length == 0 || pooledStates.Length != checked(candidateCount * weights.Length) || logits.Length != candidateCount)
            throw new DecisionException("model_tensor_shape_invalid", "Decision scorer dimensions are invalid.");
        for (var candidate = 0; candidate < candidateCount; candidate++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = pooledStates.Slice(candidate * weights.Length, weights.Length);
            var sum = bias;
            for (var i = 0; i < weights.Length; i++) sum += state[i] * weights[i];
            logits[candidate] = sum;
        }
    }
}
