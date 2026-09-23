namespace Sezika;

public interface IEncoder
{
    float[] Encode(ReadOnlySpan<int> tokenIds, CancellationToken cancellationToken = default);
}

public sealed record ModernBertConfig
{
    public required int VocabularySize { get; init; }
    public required int HiddenSize { get; init; }
    public required int IntermediateSize { get; init; }
    public required int LayerCount { get; init; }
    public required int HeadCount { get; init; }
    public int MaxTokens { get; init; } = 1024;
    public int GlobalAttentionEvery { get; init; } = 3;
    public int LocalAttention { get; init; } = 128;
    public float GlobalRopeTheta { get; init; } = 160000;
    public float LocalRopeTheta { get; init; } = 160000;
    public float NormEpsilon { get; init; } = 1e-5f;

    public void Validate()
    {
        if (VocabularySize is < 1 or > 300000 || HiddenSize is < 2 or > 2048 || IntermediateSize is < 2 or > 8192 ||
            LayerCount is < 1 or > 64 || HeadCount < 1 || HiddenSize % HeadCount != 0 || (HiddenSize / HeadCount) % 2 != 0 ||
            MaxTokens is < 2 or > 1024 || GlobalAttentionEvery < 1 || LocalAttention is < 2 or > 1024 ||
            !float.IsFinite(GlobalRopeTheta) || GlobalRopeTheta <= 0 || !float.IsFinite(LocalRopeTheta) || LocalRopeTheta <= 0 ||
            !float.IsFinite(NormEpsilon) || NormEpsilon <= 0)
            throw new DecisionException("model_architecture_unsupported", "ModernBERT dimensions or numerical settings exceed the validated runtime bounds.");
    }
}

/// <summary>All matrices retain SafeTensors/PyTorch [output,input] row-major layout.</summary>
public sealed class ModernBertLayerWeights
{
    public float[]? AttentionNorm { get; init; }
    public required float[] Qkv { get; init; }
    public required float[] AttentionOutput { get; init; }
    public required float[] MlpNorm { get; init; }
    public required float[] MlpUp { get; init; }
    public required float[] MlpDown { get; init; }
}

public sealed class ModernBertWeights
{
    public required float[] TokenEmbeddings { get; init; }
    public required float[] EmbeddingNorm { get; init; }
    public required float[] FinalNorm { get; init; }
    public required ModernBertLayerWeights[] Layers { get; init; }

    public void Validate(ModernBertConfig config)
    {
        config.Validate();
        var h = config.HiddenSize;
        ScalarOps.Shape(TokenEmbeddings, checked(config.VocabularySize * h));
        ScalarOps.Shape(EmbeddingNorm, h); ScalarOps.Shape(FinalNorm, h);
        if (Layers.Length != config.LayerCount) throw new DecisionException("model_tensor_shape_invalid", "ModernBERT layer count differs from configuration.");
        for (var i = 0; i < Layers.Length; i++)
        {
            var l = Layers[i];
            if (i == 0 && l.AttentionNorm is not null) throw new DecisionException("model_tensor_shape_invalid", "The first ModernBERT attention norm must be identity.");
            if (i != 0) ScalarOps.Shape(l.AttentionNorm, h);
            ScalarOps.Shape(l.Qkv, 3 * h * h); ScalarOps.Shape(l.AttentionOutput, h * h);
            ScalarOps.Shape(l.MlpNorm, h); ScalarOps.Shape(l.MlpUp, 2 * config.IntermediateSize * h);
            ScalarOps.Shape(l.MlpDown, h * config.IntermediateSize);
        }
    }
}

/// <summary>Fixed ModernBERT encoder with scalar reference and explicitly selected CPU SIMD kernels.</summary>
public sealed class ModernBertEncoder : IEncoder, IDisposable
{
    public ModernBertConfig Config { get; }
    public ModernBertWeights Weights { get; }
    public Action<string, float[]>? Trace { get; set; }
    public EncoderExecutionOptions ExecutionOptions { get; }
    public EncoderWorkspacePool WorkspacePool { get; }

    public ModernBertEncoder(ModernBertConfig config, ModernBertWeights weights, EncoderExecutionOptions? executionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(weights);
        weights.Validate(config);
        ExecutionOptions = executionOptions ?? EncoderExecutionOptions.Default;
        ExecutionOptions.Validate();
        Config = config; Weights = weights;
        WorkspacePool = new EncoderWorkspacePool(ExecutionOptions);
    }

    public float[] Encode(ReadOnlySpan<int> tokenIds, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ExecutionOptions.Deadline);
        var ct = deadline.Token;
        var deadlineStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var deadlineTicks = Math.Max(1L, checked((long)Math.Ceiling(ExecutionOptions.Deadline.TotalSeconds * System.Diagnostics.Stopwatch.Frequency)));
        void CheckDeadline()
        {
            if (System.Diagnostics.Stopwatch.GetTimestamp() - deadlineStart >= deadlineTicks)
            {
                deadline.Cancel();
            }
            ct.ThrowIfCancellationRequested();
        }
        CheckDeadline();
        if (tokenIds.Length < 2 || tokenIds.Length > Config.MaxTokens) throw new DecisionException("decision_token_limit_exceeded", "ModernBERT sequence exceeds its token budget.");
        using var workspace = WorkspacePool.Acquire(tokenIds.Length, Config, ct);
        CheckDeadline();
        var n = tokenIds.Length; var h = Config.HiddenSize; var width = Config.IntermediateSize;
        var hidden = new float[n * h];
        for (var row = 0; row < n; row++)
        {
            if ((uint)tokenIds[row] >= (uint)Config.VocabularySize) throw new DecisionException("tokenizer_token_out_of_range", "Token ID is outside the model vocabulary.");
            Weights.TokenEmbeddings.AsSpan(tokenIds[row] * h, h).CopyTo(hidden.AsSpan(row * h));
        }
        Emit("embedding/raw", hidden);
        hidden = Norm(hidden, n, h, Weights.EmbeddingNorm, null, Config.NormEpsilon, ct);
        Emit("embedding/norm", hidden);
        for (var index = 0; index < Weights.Layers.Length; index++)
        {
            CheckDeadline();
            var l = Weights.Layers[index]; var prefix = $"layer/{index}/";
            var normalized = l.AttentionNorm is null ? hidden : Norm(hidden, n, h, l.AttentionNorm, null, Config.NormEpsilon, ct);
            Emit(prefix + "norm1", normalized);
            var qkv = Linear(normalized, l.Qkv, null, n, h, 3 * h, ct);
            Emit(prefix + "qkv", qkv);
            ScalarOps.SplitQkv(qkv, n, h, out var q, out var k, out var v);
            var global = index % Config.GlobalAttentionEvery == 0;
            ScalarOps.Rope(q, k, n, h, Config.HeadCount, global ? Config.GlobalRopeTheta : Config.LocalRopeTheta, ct);
            Emit(prefix + "rope_q", q); Emit(prefix + "rope_k", k);
            var attention = ScalarOps.Attention(q, k, v, n, h, Config.HeadCount, global ? -1 : Config.LocalAttention / 2, ct);
            Emit(prefix + "attention", attention);
            var projected = Linear(attention, l.AttentionOutput, null, n, h, h, ct);
            Emit(prefix + "projected", projected);
            hidden = Add(hidden, projected, ct); Emit(prefix + "residual", hidden);
            normalized = Norm(hidden, n, h, l.MlpNorm, null, Config.NormEpsilon, ct); Emit(prefix + "norm2", normalized);
            var up = Linear(normalized, l.MlpUp, null, n, h, 2 * width, ct); Emit(prefix + "up", up);
            var gated = ExecutionOptions.Kernel == EncoderKernelMode.Simd
                ? SimdOps.GatedGelu(up, n, width, ct)
                : ScalarGatedGelu(up, n, width, ct);
            Emit(prefix + "gelu", gated);
            var mlp = Linear(gated, l.MlpDown, null, n, width, h, ct); Emit(prefix + "mlp", mlp);
            hidden = Add(hidden, mlp, ct); Emit(prefix + "hidden", hidden);
            CheckDeadline();
        }
        hidden = Norm(hidden, n, h, Weights.FinalNorm, null, Config.NormEpsilon, ct);
        Emit("encoder/final", hidden); ct.ThrowIfCancellationRequested(); return hidden;
    }

    private float[] Linear(float[] input, float[] weights, float[]? bias, int rows, int inputSize, int outputSize, CancellationToken cancellationToken) =>
        ExecutionOptions.Kernel == EncoderKernelMode.Simd
            ? SimdOps.Linear(input, weights, bias, rows, inputSize, outputSize, cancellationToken)
            : ScalarOps.Linear(input, weights, bias, rows, inputSize, outputSize, cancellationToken);

    private float[] Norm(float[] input, int rows, int width, float[] gamma, float[]? beta, float epsilon, CancellationToken cancellationToken) =>
        ExecutionOptions.Kernel == EncoderKernelMode.Simd
            ? SimdOps.Norm(input, rows, width, gamma, beta, epsilon, cancellationToken)
            : ScalarOps.Norm(input, rows, width, gamma, beta, epsilon, cancellationToken);

    private float[] Add(float[] a, float[] b, CancellationToken cancellationToken) =>
        ExecutionOptions.Kernel == EncoderKernelMode.Simd
            ? SimdOps.Add(a, b, cancellationToken)
            : ScalarOps.Add(a, b);

    private void Emit(string name, float[] values) => Trace?.Invoke(name, (float[])values.Clone());

    private static float[] ScalarGatedGelu(float[] up, int rows, int width, CancellationToken cancellationToken)
    {
        var output = new float[checked(rows * width)];
        for (var row = 0; row < rows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = 0; j < width; j++)
            {
                if ((j & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                output[row * width + j] = ScalarOps.Gelu(up[row * 2 * width + j]) * up[row * 2 * width + width + j];
            }
        }
        return output;
    }

    public void Dispose() => WorkspacePool.Dispose();
}

public static class ScalarOps
{
    public static void Shape(float[]? values, int count)
    {
        if (values is null || values.Length != count) throw new DecisionException("model_tensor_shape_invalid", $"Expected {count} tensor elements.");
    }

    public static float[] Linear(float[] input, float[] weights, float[]? bias, int rows, int inputSize, int outputSize, CancellationToken ct)
    {
        Shape(input, checked(rows * inputSize)); Shape(weights, checked(inputSize * outputSize));
        if (bias is not null) Shape(bias, outputSize);
        var output = new float[checked(rows * outputSize)];
        for (var row = 0; row < rows; row++)
        {
            ct.ThrowIfCancellationRequested();
            for (var col = 0; col < outputSize; col++)
            {
                if ((col & 63) == 0) ct.ThrowIfCancellationRequested();
                var sum = 0f;
                for (var k = 0; k < inputSize; k++) sum += input[row * inputSize + k] * weights[col * inputSize + k];
                output[row * outputSize + col] = sum + (bias is null ? 0 : bias[col]);
            }
        }
        return output;
    }

    public static float[] Norm(float[] input, int rows, int width, float[] gamma, float[]? beta, float epsilon, CancellationToken ct)
    {
        var output = new float[input.Length];
        for (var row = 0; row < rows; row++)
        {
            ct.ThrowIfCancellationRequested(); var start = row * width; var mean = 0f;
            for (var j = 0; j < width; j++) mean += input[start + j];
            mean /= width; var variance = 0f;
            for (var j = 0; j < width; j++) { var d = input[start + j] - mean; variance += d * d; }
            var inverse = 1f / MathF.Sqrt(variance / width + epsilon);
            for (var j = 0; j < width; j++) output[start + j] = (input[start + j] - mean) * inverse * gamma[j] + (beta is null ? 0 : beta[j]);
        }
        return output;
    }

    public static void SplitQkv(float[] input, int rows, int width, out float[] q, out float[] k, out float[] v)
    {
        q = new float[rows * width]; k = new float[q.Length]; v = new float[q.Length];
        for (var row = 0; row < rows; row++)
        {
            input.AsSpan(row * 3 * width, width).CopyTo(q.AsSpan(row * width));
            input.AsSpan(row * 3 * width + width, width).CopyTo(k.AsSpan(row * width));
            input.AsSpan(row * 3 * width + 2 * width, width).CopyTo(v.AsSpan(row * width));
        }
    }

    public static void Rope(float[] q, float[] k, int rows, int width, int heads, float theta, CancellationToken ct)
    {
        var d = width / heads; var half = d / 2;
        for (var row = 0; row < rows; row++)
        {
            ct.ThrowIfCancellationRequested();
            for (var head = 0; head < heads; head++)
                for (var j = 0; j < half; j++)
                {
                    var angle = row * MathF.Pow(theta, -2f * j / d); var sin = MathF.Sin(angle); var cos = MathF.Cos(angle);
                    var a = row * width + head * d + j; var b = a + half;
                    var qa = q[a]; var qb = q[b]; var ka = k[a]; var kb = k[b];
                    q[a] = qa * cos - qb * sin; q[b] = qb * cos + qa * sin;
                    k[a] = ka * cos - kb * sin; k[b] = kb * cos + ka * sin;
                }
        }
    }

    public static float[] Attention(float[] q, float[] k, float[] v, int rows, int width, int heads, int window, CancellationToken ct)
    {
        var result = new float[rows * width]; var scores = new float[rows]; var d = width / heads; var scale = 1f / MathF.Sqrt(d);
        for (var head = 0; head < heads; head++)
            for (var row = 0; row < rows; row++)
            {
                ct.ThrowIfCancellationRequested(); var max = float.NegativeInfinity;
                for (var col = 0; col < rows; col++)
                {
                    var value = 0f;
                    for (var j = 0; j < d; j++) value += q[row * width + head * d + j] * k[col * width + head * d + j];
                    scores[col] = window >= 0 && Math.Abs(row - col) > window ? float.NegativeInfinity : value * scale;
                    max = MathF.Max(max, scores[col]);
                }
                var sum = 0f;
                for (var col = 0; col < rows; col++) { scores[col] = MathF.Exp(scores[col] - max); sum += scores[col]; }
                for (var col = 0; col < rows; col++) scores[col] /= sum;
                for (var j = 0; j < d; j++)
                {
                    var sumValue = 0f;
                    for (var col = 0; col < rows; col++) sumValue += scores[col] * v[col * width + head * d + j];
                    result[row * width + head * d + j] = sumValue;
                }
            }
        return result;
    }

    public static float[] Add(float[] a, float[] b)
    {
        Shape(b, a.Length); var output = new float[a.Length];
        for (var i = 0; i < a.Length; i++) output[i] = a[i] + b[i];
        return output;
    }

    // Abramowitz/Stegun 7.1.26: erf approximation error < 1.5e-7.
    // This approximates exact-erf GELU, not the separate tanh GELU variant.
    public static float Gelu(float x)
    {
        var z = x * 0.7071067811865475f; var a = MathF.Abs(z); var t = 1f / (1f + 0.3275911f * a);
        var p = (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t;
        var erf = (1f - p * MathF.Exp(-a * a)) * (z < 0 ? -1 : 1);
        return 0.5f * x * (1f + erf);
    }
}
