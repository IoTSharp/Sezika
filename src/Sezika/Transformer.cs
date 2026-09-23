namespace Sezika;

public sealed record TransformerConfig
{
    public required int VocabularySize { get; init; }
    public required int HiddenSize { get; init; }
    public required int IntermediateSize { get; init; }
    public required int LayerCount { get; init; }
    public required int HeadCount { get; init; }
    public required int MaxTokens { get; init; }
    public float RopeTheta { get; init; } = 10_000f;
    public float NormEpsilon { get; init; } = 1e-5f;

    public void Validate()
    {
        if (VocabularySize <= 0 || HiddenSize <= 0 || IntermediateSize <= 0 || LayerCount <= 0 ||
            HeadCount <= 0 || MaxTokens < 2 || HiddenSize % HeadCount != 0 || RopeTheta <= 0 || NormEpsilon <= 0)
        {
            throw new DecisionException("model_architecture_unsupported", "Transformer dimensions or numerical settings are invalid.");
        }
    }
}

public sealed class TransformerLayerWeights
{
    public required float[] Query { get; init; }
    public required float[] Key { get; init; }
    public required float[] Value { get; init; }
    public required float[] Output { get; init; }
    public required float[] FeedForwardUp { get; init; }
    public required float[] FeedForwardDown { get; init; }
    public required float[] AttentionNorm { get; init; }
    public required float[] FeedForwardNorm { get; init; }

    internal void Validate(TransformerConfig config)
    {
        var h = config.HiddenSize;
        var i = config.IntermediateSize;
        if (Query.Length != h * h || Key.Length != h * h || Value.Length != h * h || Output.Length != h * h ||
            FeedForwardUp.Length != i * h || FeedForwardDown.Length != h * i ||
            AttentionNorm.Length != h || FeedForwardNorm.Length != h)
        {
            throw new DecisionException("model_tensor_shape_invalid", "Transformer tensor dimensions do not match the model config.");
        }
    }
}

public sealed class TransformerWeights
{
    public required float[] TokenEmbeddings { get; init; }
    public required TransformerLayerWeights[] Layers { get; init; }
    public float[]? FinalNorm { get; init; }

    internal void Validate(TransformerConfig config)
    {
        config.Validate();
        if (TokenEmbeddings.Length != config.VocabularySize * config.HiddenSize || Layers.Length != config.LayerCount ||
            (FinalNorm is not null && FinalNorm.Length != config.HiddenSize))
        {
            throw new DecisionException("model_tensor_shape_invalid", "Embedding or layer tensor dimensions do not match the model config.");
        }
        foreach (var layer in Layers)
        {
            layer.Validate(config);
        }
    }
}

/// <summary>Scalar FP32 reference encoder. It is intentionally simple and deterministic.</summary>
public sealed class TransformerEncoder
{
    private readonly TransformerConfig _config;
    private readonly TransformerWeights _weights;
    private readonly int _headDimension;

    public TransformerEncoder(TransformerConfig config, TransformerWeights weights)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(weights);
        weights.Validate(config);
        _config = config;
        _weights = weights;
        _headDimension = config.HiddenSize / config.HeadCount;
    }

    public float[] Encode(ReadOnlySpan<int> tokenIds, CancellationToken cancellationToken = default)
    {
        if (tokenIds.Length is < 2 or > int.MaxValue || tokenIds.Length > _config.MaxTokens)
        {
            throw new DecisionException("decision_token_limit_exceeded", $"Sequence length must be between 2 and {_config.MaxTokens}.");
        }
        var length = tokenIds.Length;
        var hidden = new float[length * _config.HiddenSize];
        for (var t = 0; t < length; t++)
        {
            var token = tokenIds[t];
            if ((uint)token >= (uint)_config.VocabularySize)
            {
                throw new DecisionException("tokenizer_token_out_of_range", $"Token ID {token} is outside the vocabulary.");
            }
            Array.Copy(_weights.TokenEmbeddings, token * _config.HiddenSize, hidden, t * _config.HiddenSize, _config.HiddenSize);
        }

        for (var layerIndex = 0; layerIndex < _config.LayerCount; layerIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layer = _weights.Layers[layerIndex];
            var normalized = new float[hidden.Length];
            for (var t = 0; t < length; t++)
            {
                LayerNorm(hidden.AsSpan(t * _config.HiddenSize, _config.HiddenSize), normalized.AsSpan(t * _config.HiddenSize, _config.HiddenSize), layer.AttentionNorm, _config.NormEpsilon);
            }
            var query = new float[hidden.Length];
            var key = new float[hidden.Length];
            var value = new float[hidden.Length];
            Project(normalized, query, layer.Query, length, _config.HiddenSize);
            Project(normalized, key, layer.Key, length, _config.HiddenSize);
            Project(normalized, value, layer.Value, length, _config.HiddenSize);
            ApplyRope(query, key, length);
            var attention = new float[hidden.Length];
            Attend(query, key, value, attention, length, cancellationToken);
            var projected = new float[hidden.Length];
            Project(attention, projected, layer.Output, length, _config.HiddenSize);
            for (var index = 0; index < hidden.Length; index++)
            {
                hidden[index] += projected[index];
            }

            normalized = new float[hidden.Length];
            for (var t = 0; t < length; t++)
            {
                LayerNorm(hidden.AsSpan(t * _config.HiddenSize, _config.HiddenSize), normalized.AsSpan(t * _config.HiddenSize, _config.HiddenSize), layer.FeedForwardNorm, _config.NormEpsilon);
            }
            var up = new float[length * _config.IntermediateSize];
            for (var t = 0; t < length; t++)
            {
                Multiply(normalized.AsSpan(t * _config.HiddenSize, _config.HiddenSize), layer.FeedForwardUp, up.AsSpan(t * _config.IntermediateSize), _config.HiddenSize, _config.IntermediateSize);
                for (var j = 0; j < _config.IntermediateSize; j++)
                {
                    up[t * _config.IntermediateSize + j] = Gelu(up[t * _config.IntermediateSize + j]);
                }
            }
            for (var t = 0; t < length; t++)
            {
                var output = hidden.AsSpan(t * _config.HiddenSize, _config.HiddenSize);
                var update = new float[_config.HiddenSize];
                Multiply(up.AsSpan(t * _config.IntermediateSize, _config.IntermediateSize), layer.FeedForwardDown, update, _config.IntermediateSize, _config.HiddenSize);
                for (var j = 0; j < output.Length; j++)
                {
                    output[j] += update[j];
                }
            }
        }
        if (_weights.FinalNorm is not null)
        {
            for (var t = 0; t < length; t++)
            {
                var source = hidden.AsSpan(t * _config.HiddenSize, _config.HiddenSize);
                var scratch = source.ToArray();
                LayerNorm(scratch, source, _weights.FinalNorm, _config.NormEpsilon);
            }
        }
        return hidden;
    }

    private void Attend(float[] query, float[] key, float[] value, float[] output, int length, CancellationToken cancellationToken)
    {
        var scale = 1f / MathF.Sqrt(_headDimension);
        var scores = new float[length];
        for (var head = 0; head < _config.HeadCount; head++)
        {
            var offset = head * _headDimension;
            for (var row = 0; row < length; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var max = float.NegativeInfinity;
                for (var column = 0; column < length; column++)
                {
                    var score = 0f;
                    for (var d = 0; d < _headDimension; d++)
                    {
                        score += query[row * _config.HiddenSize + offset + d] * key[column * _config.HiddenSize + offset + d];
                    }
                    score *= scale;
                    scores[column] = score;
                    max = MathF.Max(max, score);
                }
                var sum = 0f;
                for (var column = 0; column < length; column++)
                {
                    scores[column] = MathF.Exp(scores[column] - max);
                    sum += scores[column];
                }
                var denominator = sum > 0 && float.IsFinite(sum) ? sum : 1f;
                for (var column = 0; column < length; column++)
                {
                    var probability = scores[column] / denominator;
                    for (var d = 0; d < _headDimension; d++)
                    {
                        output[row * _config.HiddenSize + offset + d] += probability * value[column * _config.HiddenSize + offset + d];
                    }
                }
            }
        }
    }

    private void ApplyRope(float[] query, float[] key, int length)
    {
        for (var position = 0; position < length; position++)
        {
            for (var headOffset = 0; headOffset < _config.HiddenSize; headOffset += _headDimension)
            {
                for (var pair = 0; pair + 1 < _headDimension; pair += 2)
                {
                    var frequency = MathF.Pow(_config.RopeTheta, -((float)pair / _headDimension));
                    var angle = position * frequency;
                    var cosine = MathF.Cos(angle);
                    var sine = MathF.Sin(angle);
                    Rotate(query, key, position * _config.HiddenSize + headOffset + pair, cosine, sine);
                }
            }
        }
    }

    private static void Rotate(float[] query, float[] key, int index, float cosine, float sine)
    {
        var query0 = query[index];
        var query1 = query[index + 1];
        query[index] = query0 * cosine - query1 * sine;
        query[index + 1] = query0 * sine + query1 * cosine;
        var key0 = key[index];
        var key1 = key[index + 1];
        key[index] = key0 * cosine - key1 * sine;
        key[index + 1] = key0 * sine + key1 * cosine;
    }

    private static void Project(float[] input, float[] output, float[] matrix, int length, int hidden)
    {
        for (var row = 0; row < length; row++)
        {
            Multiply(input.AsSpan(row * hidden, hidden), matrix, output.AsSpan(row * hidden, hidden), hidden, hidden);
        }
    }

    private static void Multiply(ReadOnlySpan<float> input, float[] matrix, Span<float> output, int inputSize, int outputSize)
    {
        for (var column = 0; column < outputSize; column++)
        {
            var sum = 0f;
            for (var row = 0; row < inputSize; row++)
            {
                sum += input[row] * matrix[row * outputSize + column];
            }
            output[column] = sum;
        }
    }

    private static void LayerNorm(ReadOnlySpan<float> input, Span<float> output, float[] gamma, float epsilon)
    {
        var mean = 0f;
        for (var i = 0; i < input.Length; i++) mean += input[i];
        mean /= input.Length;
        var variance = 0f;
        for (var i = 0; i < input.Length; i++)
        {
            var delta = input[i] - mean;
            variance += delta * delta;
        }
        var inverse = 1f / MathF.Sqrt(variance / input.Length + epsilon);
        for (var i = 0; i < input.Length; i++) output[i] = (input[i] - mean) * inverse * gamma[i];
    }

    private static float Gelu(float value) => 0.5f * value * (1f + MathF.Tanh(0.79788456f * (value + 0.044715f * value * value * value)));
}
