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

/// <summary>CPU scalar decision head oracle matching the Laya marker-based head contract.</summary>
public sealed class ModernBertDecisionPipeline
{
    private readonly ModernBertEncoder _encoder;
    private readonly DecisionHeadWeights _weights;
    private readonly int _headCount;
    private readonly int _headWidth;
    public Action<string, float[]>? Trace { get; set; }

    public ModernBertDecisionPipeline(ModernBertEncoder encoder, DecisionHeadWeights weights)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder)); _weights = weights ?? throw new ArgumentNullException(nameof(weights));
        var h = encoder.Config.HiddenSize; _headCount = Math.Max(1, h / 64); _headWidth = h / _headCount;
        ScalarOps.Shape(weights.TypeEmbeddings, checked(3 * h));
        if (weights.Layers.Length != 2) throw new DecisionException("model_tensor_shape_invalid", "Decision head must contain exactly two transformer layers.");
        foreach (var layer in weights.Layers) ValidateLayer(layer, h, h * 4);
        ScalarOps.Shape(weights.ScorerNorm, h); ScalarOps.Shape(weights.ScorerNormBias, h);
        ScalarOps.Shape(weights.ScorerDense, h * h); ScalarOps.Shape(weights.ScorerDenseBias, h);
        ScalarOps.Shape(weights.ScorerOutput, h); ScalarOps.Shape(weights.ScorerOutputBias, 1);
    }

    public float[] Score(ReadOnlySpan<int> tokenIds, int typeId, ReadOnlySpan<int> markerPositions, CancellationToken cancellationToken = default)
    {
        if ((uint)typeId >= 3 || markerPositions.Length == 0) throw new DecisionException("decision_head_input_invalid", "A valid type and marker position set are required.");
        var hidden = _encoder.Encode(tokenIds, cancellationToken); var n = tokenIds.Length; var h = _encoder.Config.HiddenSize;
        for (var row = 0; row < n; row++) for (var j = 0; j < h; j++) hidden[row * h + j] += _weights.TypeEmbeddings[typeId * h + j];
        Emit("head/type", hidden);
        for (var index = 0; index < _weights.Layers.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var l = _weights.Layers[index]; var p = $"head/{index}/";
            var norm = ScalarOps.Norm(hidden, n, h, l.AttentionNorm, l.AttentionNormBias, _encoder.Config.NormEpsilon, cancellationToken); Emit(p + "norm1", norm);
            var qkv = ScalarOps.Linear(norm, l.Qkv, l.QkvBias, n, h, 3 * h, cancellationToken); Emit(p + "qkv", qkv);
            ScalarOps.SplitQkv(qkv, n, h, out var q, out var k, out var v);
            var attention = ScalarOps.Attention(q, k, v, n, h, _headCount, -1, cancellationToken); Emit(p + "attention", attention);
            var projected = ScalarOps.Linear(attention, l.AttentionOutput, l.AttentionOutputBias, n, h, h, cancellationToken); Emit(p + "projected", projected);
            hidden = ScalarOps.Add(hidden, projected); Emit(p + "residual", hidden);
            norm = ScalarOps.Norm(hidden, n, h, l.MlpNorm, l.MlpNormBias, _encoder.Config.NormEpsilon, cancellationToken); Emit(p + "norm2", norm);
            var up = ScalarOps.Linear(norm, l.MlpUp, l.MlpUpBias, n, h, 4 * h, cancellationToken); Emit(p + "up", up);
            for (var i = 0; i < up.Length; i++) up[i] = MathF.Max(0f, up[i]); Emit(p + "relu", up);
            var mlp = ScalarOps.Linear(up, l.MlpDown, l.MlpDownBias, n, 4 * h, h, cancellationToken); Emit(p + "mlp", mlp);
            hidden = ScalarOps.Add(hidden, mlp); Emit(p + "hidden", hidden);
        }
        var normalized = ScalarOps.Norm(hidden, n, h, _weights.ScorerNorm, _weights.ScorerNormBias, _encoder.Config.NormEpsilon, cancellationToken); Emit("scorer/norm", normalized);
        var dense = ScalarOps.Linear(normalized, _weights.ScorerDense, _weights.ScorerDenseBias, n, h, h, cancellationToken);
        for (var i = 0; i < dense.Length; i++) dense[i] = ScalarOps.Gelu(dense[i]); Emit("scorer/dense", dense);
        var logits = ScalarOps.Linear(dense, _weights.ScorerOutput, _weights.ScorerOutputBias, n, h, 1, cancellationToken); Emit("scorer/logits", logits);
        var selected = new float[markerPositions.Length];
        for (var i = 0; i < selected.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var marker = markerPositions[i];
            if ((uint)marker >= (uint)n) throw new DecisionException("decision_head_input_invalid", "Marker position is outside the sequence.");
            selected[i] = logits[marker];
        }
        return selected;
    }

    private void Emit(string name, float[] values) => Trace?.Invoke(name, (float[])values.Clone());
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
