using Sezika;

internal static class QuantizedChecks
{
    public static void Run(Action<bool, string> check)
    {
        // A dense, asymmetric tail-width case exercises actual rounding,
        // negative weights, zero rows, bias, and more than one activation row.
        const int inputSize = 13;
        const int outputSize = 5;
        var source = Values(inputSize * outputSize, 0.37f, 0.2f);
        Array.Clear(source, 2 * inputSize, inputSize);
        var input = Values(3 * inputSize, 0.8f, 0.6f);
        var bias = Values(outputSize, 0.2f, 0.3f);
        var matrix = Int8WeightMatrix.Create(source, inputSize, outputSize);
        var expected = ScalarOps.Linear(input, source, bias, 3, inputSize, outputSize, CancellationToken.None);
        var actual = QuantizedOps.Linear(input, matrix, bias, 3);
        var boundSatisfied = true;
        for (var row = 0; row < 3; row++)
        {
            var absoluteActivationSum = 0f;
            for (var index = 0; index < inputSize; index++) absoluteActivationSum += MathF.Abs(input[row * inputSize + index]);
            for (var column = 0; column < outputSize; column++)
            {
                var maximum = 0f;
                for (var index = 0; index < inputSize; index++) maximum = MathF.Max(maximum, MathF.Abs(source[column * inputSize + index]));
                // Symmetric rounding contributes at most half a scale per
                // weight. A small FP32 allowance covers the reduction itself.
                var bound = maximum / 254f * absoluteActivationSum + 2e-6f;
                boundSatisfied &= MathF.Abs(expected[row * outputSize + column] - actual[row * outputSize + column]) <= bound;
            }
        }
        check(boundSatisfied && Error(expected, actual) > 0f, "W8A32 dense dot-product obeys rounding error bound");
        check(actual[2] == bias[2] && actual[outputSize + 2] == bias[2], "W8A32 zero rows preserve exact bias");
        check(matrix.StorageBytes == inputSize * outputSize + outputSize * sizeof(float), "W8A32 storage includes int8 values and per-row scales");
        Array.Clear(source);
        check(actual.SequenceEqual(QuantizedOps.Linear(input, matrix, bias, 3)), "W8A32 executes immutable int8 snapshot after FP32 source mutation");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        ExpectCancellation(() => Int8WeightMatrix.Create(source, inputSize, outputSize, cancelled.Token), check, "W8A32 preparation observes cancellation");
        ExpectCancellation(() => QuantizedOps.Linear(input, matrix, bias, 3, cancelled.Token), check, "W8A32 matmul observes cancellation");
        try
        {
            _ = Int8WeightMatrix.Create([float.NaN], 1, 1);
            throw new InvalidOperationException("Non-finite weights must be rejected.");
        }
        catch (DecisionException exception) when (exception.Code == "model_quantization_non_finite")
        {
            check(true, "W8A32 rejects non-finite source weights");
        }
        var subnormal = Int8WeightMatrix.Create([float.Epsilon], 1, 1);
        check(QuantizedOps.Linear([1f], subnormal, null, 1)[0] == float.Epsilon, "W8A32 subnormal scale cannot underflow to zero");

        var config = new ModernBertConfig
        {
            VocabularySize = 16, HiddenSize = 8, IntermediateSize = 12, LayerCount = 2, HeadCount = 2,
            MaxTokens = 16, GlobalAttentionEvery = 2, LocalAttention = 4,
        };
        var weights = EncoderWeights(config);
        var options = new EncoderExecutionOptions { Deadline = TimeSpan.FromSeconds(5), MaxWorkspaceBytes = 4 * 1024 * 1024 };
        using var scalar = new ModernBertEncoder(config, weights, options);
        using var quantized = new ModernBertEncoder(config, weights, options with { Kernel = EncoderKernelMode.QuantizedInt8 });
        using var simd = new ModernBertEncoder(config, weights, options with { Kernel = EncoderKernelMode.Simd });
        int[] tokens = [1, 3, 5, 7, 9, 11];
        var referenceTrace = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var quantizedTrace = new Dictionary<string, float[]>(StringComparer.Ordinal);
        scalar.Trace = (name, values) => referenceTrace[name] = values;
        quantized.Trace = (name, values) => quantizedTrace[name] = values;
        var encoded = scalar.Encode(tokens);
        var quantizedEncoded = quantized.Encode(tokens);
        var encoderTraceError = TraceError(referenceTrace, quantizedTrace);
        check(encoderTraceError > 0f && encoderTraceError < 0.025f && Error(encoded, quantizedEncoded) < 0.01f,
            $"W8A32 encoder dense trace alignment max_abs={encoderTraceError:G9}");
        check(quantized.QuantizedWeightBytes > 0 && quantized.WorkspacePool.ActiveCount == 0 && quantized.WorkspacePool.OutstandingBytes == 0,
            "W8A32 encoder accounts preparation bytes and releases request workspace");

        var head = HeadWeights(config.HiddenSize);
        using var scalarHead = new ModernBertDecisionPipeline(scalar, head);
        using var quantizedHead = new ModernBertDecisionPipeline(quantized, head);
        using var simdHead = new ModernBertDecisionPipeline(simd, head);
        var referenceHeadTrace = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var quantizedHeadTrace = new Dictionary<string, float[]>(StringComparer.Ordinal);
        scalarHead.Trace = (name, values) => referenceHeadTrace[name] = values;
        quantizedHead.Trace = (name, values) => quantizedHeadTrace[name] = values;
        var inputSnapshot = encoded.ToArray();
        var scalarLogits = scalarHead.ScoreEncoded(encoded, 1, [1, 3, 5]);
        var quantizedLogits = quantizedHead.ScoreEncoded(encoded, 1, [1, 3, 5]);
        var headTraceError = TraceError(referenceHeadTrace, quantizedHeadTrace);
        check(headTraceError > 0f && headTraceError < 0.025f && Error(scalarLogits, quantizedLogits) < 0.005f,
            $"W8A32 head-only dense trace alignment max_abs={headTraceError:G9}");
        check(encoded.SequenceEqual(inputSnapshot), "head-only comparison leaves shared encoder output unchanged");
        check(Error(scalarLogits, simdHead.ScoreEncoded(encoded, 1, [1, 3, 5])) < 2e-5f, "SIMD head-only alignment uses same encoded input");
        check(Error(scalarHead.Score(tokens, 1, [1, 3, 5]), quantizedHead.Score(tokens, 1, [1, 3, 5])) < 0.01f,
            "W8A32 encoder and head end-to-end marker logits align");
        check(quantizedHead.QuantizedWeightBytes > 0 && scalarHead.QuantizedWeightBytes == 0, "W8A32 head cache accounted separately from encoder");
        ExpectCancellation(() => quantizedHead.ScoreEncoded(encoded, 1, [1], cancelled.Token), check, "W8A32 head-only pre-cancel observed");

        using var midRequest = new CancellationTokenSource();
        quantized.Trace = (name, _) => { if (name == "layer/0/qkv") midRequest.Cancel(); };
        ExpectCancellation(() => quantized.Encode(tokens, midRequest.Token), check, "W8A32 cancellation during encoder request observed");
        check(quantized.WorkspacePool.ActiveCount == 0 && quantized.WorkspacePool.OutstandingBytes == 0, "W8A32 cancelled request returns workspace");
        quantized.Trace = null;
        check(quantized.Encode(tokens).Length == encoded.Length, "W8A32 session reusable after cancellation");

        quantizedHead.Dispose();
        check(quantizedHead.QuantizedWeightBytes == 0, "W8A32 head unload drops prepared buffer ownership");
        try { quantizedHead.ScoreEncoded(encoded, 1, [1]); throw new InvalidOperationException("Disposed head accepted request."); }
        catch (ObjectDisposedException) { check(true, "unloaded W8A32 head rejects new requests"); }
        quantized.Dispose();
        check(quantized.QuantizedWeightBytes == 0, "W8A32 encoder unload drops prepared buffer ownership");
        try { quantized.Encode(tokens); throw new InvalidOperationException("Disposed encoder accepted request."); }
        catch (ObjectDisposedException) { check(true, "unloaded W8A32 encoder rejects new requests"); }
    }

    private static float[] Values(int count, float amplitude, float phase) =>
        Enumerable.Range(0, count).Select(index => amplitude * MathF.Sin((index + 1) * 0.173f + phase)).ToArray();

    private static ModernBertWeights EncoderWeights(ModernBertConfig config)
    {
        var h = config.HiddenSize;
        var width = config.IntermediateSize;
        return new ModernBertWeights
        {
            TokenEmbeddings = Values(config.VocabularySize * h, 0.7f, 0.1f),
            EmbeddingNorm = Enumerable.Repeat(1f, h).ToArray(), FinalNorm = Enumerable.Repeat(1f, h).ToArray(),
            Layers = Enumerable.Range(0, config.LayerCount).Select(index => new ModernBertLayerWeights
            {
                AttentionNorm = index == 0 ? null : Enumerable.Repeat(1f, h).ToArray(),
                Qkv = Values(3 * h * h, 0.08f, index), AttentionOutput = Values(h * h, 0.08f, 0.4f + index),
                MlpNorm = Enumerable.Repeat(1f, h).ToArray(), MlpUp = Values(2 * width * h, 0.08f, 0.2f + index),
                MlpDown = Values(h * width, 0.08f, 0.7f + index),
            }).ToArray(),
        };
    }

    private static DecisionHeadWeights HeadWeights(int h) => new()
    {
        TypeEmbeddings = Values(3 * h, 0.04f, 0.5f),
        Layers = Enumerable.Range(0, 2).Select(index => new DecisionHeadLayerWeights
        {
            Qkv = Values(3 * h * h, 0.08f, index), QkvBias = Values(3 * h, 0.02f, 0.3f),
            AttentionOutput = Values(h * h, 0.08f, 0.4f + index), AttentionOutputBias = Values(h, 0.02f, 0.5f),
            AttentionNorm = Enumerable.Repeat(1f, h).ToArray(), AttentionNormBias = Values(h, 0.02f, 0.2f),
            MlpUp = Values(4 * h * h, 0.08f, 0.2f + index), MlpUpBias = Values(4 * h, 0.02f, 0.5f),
            MlpDown = Values(4 * h * h, 0.08f, 0.7f + index), MlpDownBias = Values(h, 0.02f, 0.8f),
            MlpNorm = Enumerable.Repeat(1f, h).ToArray(), MlpNormBias = Values(h, 0.02f, 0.4f),
        }).ToArray(),
        ScorerNorm = Enumerable.Repeat(1f, h).ToArray(), ScorerNormBias = Values(h, 0.02f, 0.4f),
        ScorerDense = Values(h * h, 0.18f, 0.8f), ScorerDenseBias = Values(h, 0.02f, 0.4f),
        ScorerOutput = Values(h, 0.18f, 0.2f), ScorerOutputBias = [0.03f],
    };

    private static float Error(float[] expected, float[] actual) =>
        expected.Length != actual.Length ? float.PositiveInfinity : expected.Zip(actual, (left, right) => MathF.Abs(left - right)).Max();

    private static float TraceError(Dictionary<string, float[]> expected, Dictionary<string, float[]> actual) =>
        !expected.Keys.SequenceEqual(actual.Keys) ? float.PositiveInfinity : expected.Max(pair => Error(pair.Value, actual[pair.Key]));

    private static void ExpectCancellation(Action action, Action<bool, string> check, string name)
    {
        try { action(); throw new InvalidOperationException("Expected cancellation."); }
        catch (OperationCanceledException) { check(true, name); }
    }
}
