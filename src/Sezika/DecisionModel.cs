namespace Sezika;

/// <summary>One statically loaded model. Weights are supplied by the host's verified asset loader.</summary>
public sealed class DecisionModel
{
    public DecisionModel(
        string modelId,
        string revision,
        string tokenizerRevision,
        Tokenizer tokenizer,
        IEncoder encoder,
        ReadOnlySpan<float> headWeights,
        float headBias,
        string backend = "cpu")
    {
        if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(tokenizerRevision))
            throw new ArgumentException("Model identity is required.");
        if (headWeights.Length == 0) throw new ArgumentException("Head weights are required.", nameof(headWeights));
        ModelId = modelId;
        Revision = revision;
        TokenizerRevision = tokenizerRevision;
        Tokenizer = tokenizer;
        Encoder = encoder;
        HeadWeights = headWeights.ToArray();
        HeadBias = headBias;
        Backend = backend;
    }

    public string ModelId { get; }
    public string Revision { get; }
    public string TokenizerRevision { get; }
    public Tokenizer Tokenizer { get; }
    public IEncoder Encoder { get; }
    public float[] HeadWeights { get; }
    public float HeadBias { get; }
    public string Backend { get; }

    public float Score(ReadOnlySpan<float> hidden)
    {
        if (hidden.Length != HeadWeights.Length)
            throw new DecisionException("model_tensor_shape_invalid", "Pooled hidden state does not match the decision head.");
        var sum = 0f;
        for (var i = 0; i < HeadWeights.Length; i++) sum += hidden[i] * HeadWeights[i];
        return sum + HeadBias;
    }
}
