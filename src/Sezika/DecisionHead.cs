namespace Sezika;

public sealed class DecisionHeadLayerWeights
{
    public required float[] Qkv { get; init; }
    public required float[] QkvBias { get; init; }
    public required float[] AttentionOutput { get; init; }
    public required float[] AttentionOutputBias { get; init; }
    public required float[] AttentionNorm { get; init; }
    public required float[] AttentionNormBias { get; init; }
    public required float[] MlpUp { get; init; }
    public required float[] MlpUpBias { get; init; }
    public required float[] MlpDown { get; init; }
    public required float[] MlpDownBias { get; init; }
    public required float[] MlpNorm { get; init; }
    public required float[] MlpNormBias { get; init; }
}

public sealed class DecisionHeadWeights
{
    public required float[] TypeEmbeddings { get; init; }
    public required DecisionHeadLayerWeights[] Layers { get; init; }
    public required float[] ScorerNorm { get; init; }
    public required float[] ScorerNormBias { get; init; }
    public required float[] ScorerDense { get; init; }
    public required float[] ScorerDenseBias { get; init; }
    public required float[] ScorerOutput { get; init; }
    public required float[] ScorerOutputBias { get; init; }
}

/// <summary>CPU decision head with scalar, SIMD and W8A32 linear kernels for the Laya marker contract.</summary>
public sealed class ModernBertDecisionPipeline : IMarkerDecisionPipeline, IDisposable
{
    private readonly ModernBertEncoder _encoder;
    private readonly DecisionHeadWeights _weights;
    private readonly int _headCount;
    private Int8WeightCache? _quantizedWeights;
    private int _disposed;
    public Action<string, float[]>? Trace { get; set; }
    /// <summary>Head/scorer int8 and scales payload, additional to retained float32 tensors.</summary>
    public long QuantizedWeightBytes => Volatile.Read(ref _quantizedWeights)?.StorageBytes ?? 0;

    public ModernBertDecisionPipeline(ModernBertEncoder encoder, DecisionHeadWeights weights)
        : this(encoder, weights, CancellationToken.None)
    {
    }

    public ModernBertDecisionPipeline(ModernBertEncoder encoder, DecisionHeadWeights weights, CancellationToken cancellationToken)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder)); _weights = weights ?? throw new ArgumentNullException(nameof(weights));
        var h = encoder.Config.HiddenSize; _headCount = Math.Max(1, h / 64);
        ValidateWeights(encoder.Config, weights);
        if (encoder.ExecutionOptions.Kernel == EncoderKernelMode.QuantizedInt8)
        {
            var cache = new Int8WeightCache();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(encoder.ExecutionOptions.Deadline);
            foreach (var layer in weights.Layers)
            {
                cache.Add(layer.Qkv, h, 3 * h, deadline.Token);
                cache.Add(layer.AttentionOutput, h, h, deadline.Token);
                cache.Add(layer.MlpUp, h, 4 * h, deadline.Token);
                cache.Add(layer.MlpDown, 4 * h, h, deadline.Token);
            }
            cache.Add(weights.ScorerDense, h, h, deadline.Token);
            cache.Add(weights.ScorerOutput, h, 1, deadline.Token);
            _quantizedWeights = cache;
        }
    }

    public float[] Score(ReadOnlySpan<int> tokenIds, int typeId, ReadOnlySpan<int> markerPositions, CancellationToken cancellationToken = default)
    {
        var quantizedWeights = Volatile.Read(ref _quantizedWeights);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ValidateMarkers(typeId, markerPositions, tokenIds.Length);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_encoder.ExecutionOptions.Deadline);
        var hidden = _encoder.Encode(tokenIds, deadline.Token);
        return ScoreCore(hidden, typeId, markerPositions, quantizedWeights, deadline.Token);
    }

    /// <summary>Runs only the head on a copied encoder output, allowing independent head error measurements.</summary>
    public float[] ScoreEncoded(ReadOnlySpan<float> encodedHidden, int typeId, ReadOnlySpan<int> markerPositions, CancellationToken cancellationToken = default)
    {
        var quantizedWeights = Volatile.Read(ref _quantizedWeights);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_encoder.ExecutionOptions.Deadline);
        deadline.Token.ThrowIfCancellationRequested();
        var h = _encoder.Config.HiddenSize;
        if (encodedHidden.Length % h != 0 || encodedHidden.Length / h < 2 || encodedHidden.Length / h > _encoder.Config.MaxTokens)
            throw new DecisionException("decision_head_input_invalid", "Encoded hidden dimensions are outside the head bounds.");
        ValidateMarkers(typeId, markerPositions, encodedHidden.Length / h);
        return ScoreCore(encodedHidden.ToArray(), typeId, markerPositions, quantizedWeights, deadline.Token);
    }

    private float[] ScoreCore(float[] hidden, int typeId, ReadOnlySpan<int> markerPositions, Int8WeightCache? quantizedWeights, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var h = _encoder.Config.HiddenSize; var n = hidden.Length / h;
        for (var row = 0; row < n; row++) for (var j = 0; j < h; j++) hidden[row * h + j] += _weights.TypeEmbeddings[typeId * h + j];
        Emit("head/type", hidden);
        for (var index = 0; index < _weights.Layers.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var l = _weights.Layers[index]; var p = $"head/{index}/";
            var norm = Norm(hidden, n, h, l.AttentionNorm, l.AttentionNormBias, cancellationToken); Emit(p + "norm1", norm);
            var qkv = Linear(norm, l.Qkv, l.QkvBias, n, h, 3 * h, quantizedWeights, cancellationToken); Emit(p + "qkv", qkv);
            ScalarOps.SplitQkv(qkv, n, h, out var q, out var k, out var v);
            var attention = ScalarOps.Attention(q, k, v, n, h, _headCount, -1, cancellationToken); Emit(p + "attention", attention);
            var projected = Linear(attention, l.AttentionOutput, l.AttentionOutputBias, n, h, h, quantizedWeights, cancellationToken); Emit(p + "projected", projected);
            hidden = Add(hidden, projected, cancellationToken); Emit(p + "residual", hidden);
            norm = Norm(hidden, n, h, l.MlpNorm, l.MlpNormBias, cancellationToken); Emit(p + "norm2", norm);
            var up = Linear(norm, l.MlpUp, l.MlpUpBias, n, h, 4 * h, quantizedWeights, cancellationToken); Emit(p + "up", up);
            for (var i = 0; i < up.Length; i++)
            {
                if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                up[i] = MathF.Max(0f, up[i]);
            }
            Emit(p + "relu", up);
            var mlp = Linear(up, l.MlpDown, l.MlpDownBias, n, 4 * h, h, quantizedWeights, cancellationToken); Emit(p + "mlp", mlp);
            hidden = Add(hidden, mlp, cancellationToken); Emit(p + "hidden", hidden);
        }
        var normalized = Norm(hidden, n, h, _weights.ScorerNorm, _weights.ScorerNormBias, cancellationToken); Emit("scorer/norm", normalized);
        var dense = Linear(normalized, _weights.ScorerDense, _weights.ScorerDenseBias, n, h, h, quantizedWeights, cancellationToken);
        for (var i = 0; i < dense.Length; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            dense[i] = ScalarOps.Gelu(dense[i]);
        }
        Emit("scorer/dense", dense);
        var logits = Linear(dense, _weights.ScorerOutput, _weights.ScorerOutputBias, n, h, 1, quantizedWeights, cancellationToken); Emit("scorer/logits", logits);
        var selected = new float[markerPositions.Length];
        for (var i = 0; i < selected.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var marker = markerPositions[i];
            if ((uint)marker >= (uint)n) throw new DecisionException("decision_head_input_invalid", "Marker position is outside the sequence.");
            selected[i] = logits[marker];
        }
        return selected;
    }

    private float[] Linear(float[] input, float[] weights, float[]? bias, int rows, int inputSize, int outputSize, Int8WeightCache? quantizedWeights, CancellationToken cancellationToken) =>
        _encoder.ExecutionOptions.Kernel switch
        {
            EncoderKernelMode.Simd => SimdOps.Linear(input, weights, bias, rows, inputSize, outputSize, cancellationToken),
            EncoderKernelMode.QuantizedInt8 => QuantizedOps.Linear(input, quantizedWeights!.Get(weights), bias, rows, cancellationToken),
            _ => ScalarOps.Linear(input, weights, bias, rows, inputSize, outputSize, cancellationToken),
        };

    private float[] Norm(float[] input, int rows, int width, float[] gamma, float[]? beta, CancellationToken cancellationToken) =>
        _encoder.ExecutionOptions.Kernel == EncoderKernelMode.Simd
            ? SimdOps.Norm(input, rows, width, gamma, beta, _encoder.Config.NormEpsilon, cancellationToken)
            : ScalarOps.Norm(input, rows, width, gamma, beta, _encoder.Config.NormEpsilon, cancellationToken);

    private float[] Add(float[] left, float[] right, CancellationToken cancellationToken) =>
        _encoder.ExecutionOptions.Kernel == EncoderKernelMode.Simd
            ? SimdOps.Add(left, right, cancellationToken)
            : ScalarOps.Add(left, right);

    /// <summary>Releases prepared head weights; an in-flight request retains its immutable snapshot until completion.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Interlocked.Exchange(ref _quantizedWeights, null);
    }

    private void Emit(string name, float[] values) => Trace?.Invoke(name, (float[])values.Clone());

    private static void ValidateMarkers(int typeId, ReadOnlySpan<int> markerPositions, int tokenCount)
    {
        if ((uint)typeId >= 3 || markerPositions.Length is < 1 or > 1024)
            throw new DecisionException("decision_head_input_invalid", "A valid type and bounded marker position set are required.");
        foreach (var marker in markerPositions)
            if ((uint)marker >= (uint)tokenCount) throw new DecisionException("decision_head_input_invalid", "Marker position is outside the sequence.");
    }

    internal static void ValidateWeights(ModernBertConfig config, DecisionHeadWeights weights)
    {
        var h = config.HiddenSize;
        ScalarOps.Shape(weights.TypeEmbeddings, checked(3 * h));
        if (weights.Layers.Length != 2) throw new DecisionException("model_tensor_shape_invalid", "Decision head must contain exactly two transformer layers.");
        foreach (var layer in weights.Layers) ValidateLayer(layer, h, h * 4);
        ScalarOps.Shape(weights.ScorerNorm, h); ScalarOps.Shape(weights.ScorerNormBias, h);
        ScalarOps.Shape(weights.ScorerDense, h * h); ScalarOps.Shape(weights.ScorerDenseBias, h);
        ScalarOps.Shape(weights.ScorerOutput, h); ScalarOps.Shape(weights.ScorerOutputBias, 1);
    }

    private static void ValidateLayer(DecisionHeadLayerWeights layer, int h, int intermediate)
    {
        ScalarOps.Shape(layer.Qkv, 3 * h * h); ScalarOps.Shape(layer.QkvBias, 3 * h);
        ScalarOps.Shape(layer.AttentionOutput, h * h); ScalarOps.Shape(layer.AttentionOutputBias, h);
        ScalarOps.Shape(layer.AttentionNorm, h); ScalarOps.Shape(layer.AttentionNormBias, h);
        ScalarOps.Shape(layer.MlpUp, intermediate * h); ScalarOps.Shape(layer.MlpUpBias, intermediate);
        ScalarOps.Shape(layer.MlpDown, h * intermediate); ScalarOps.Shape(layer.MlpDownBias, h);
        ScalarOps.Shape(layer.MlpNorm, h); ScalarOps.Shape(layer.MlpNormBias, h);
    }
}
