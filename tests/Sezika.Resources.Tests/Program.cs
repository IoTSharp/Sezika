using Sezika;
using System.Numerics;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}

static ModernBertWeights CreateWeights(ModernBertConfig config)
{
    var hidden = new float[config.VocabularySize * config.HiddenSize];
    for (var token = 0; token < config.VocabularySize; token++)
        for (var dimension = 0; dimension < config.HiddenSize; dimension++)
            hidden[token * config.HiddenSize + dimension] = MathF.Sin((token + 1) * (dimension + 1) * 0.07f);

    var h = config.HiddenSize;
    var width = config.IntermediateSize;
    var qkv = new float[3 * h * h];
    var attentionOutput = new float[h * h];
    var mlpUp = new float[2 * width * h];
    var mlpDown = new float[h * width];
    for (var row = 0; row < h; row++)
    {
        qkv[row * h + row] = 1f;
        qkv[(h + row) * h + row] = 1f;
        qkv[(2 * h + row) * h + row] = 1f;
        attentionOutput[row * h + row] = 0.2f;
    }
    for (var row = 0; row < width; row++)
    {
        mlpUp[row * h + (row % h)] = 0.15f;
        mlpUp[(width + row) * h + (row % h)] = 0.1f;
        mlpDown[(row % h) * width + row] = 0.08f;
    }
    var ones = Enumerable.Repeat(1f, h).ToArray();
    var layers = new ModernBertLayerWeights[config.LayerCount];
    for (var layer = 0; layer < layers.Length; layer++)
    {
        layers[layer] = new ModernBertLayerWeights
        {
            AttentionNorm = layer == 0 ? null : ones.ToArray(),
            Qkv = qkv.ToArray(), AttentionOutput = attentionOutput.ToArray(), MlpNorm = ones.ToArray(),
            MlpUp = mlpUp.ToArray(), MlpDown = mlpDown.ToArray(),
        };
    }
    return new ModernBertWeights { TokenEmbeddings = hidden, EmbeddingNorm = ones.ToArray(), FinalNorm = ones.ToArray(), Layers = layers };
}

var config = new ModernBertConfig
{
    VocabularySize = 64, HiddenSize = 8, IntermediateSize = 12, LayerCount = 2, HeadCount = 2,
    MaxTokens = 64, GlobalAttentionEvery = 2, LocalAttention = 16,
};
var weights = CreateWeights(config);
var scalarOptions = new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(5), MaxConcurrentRequests = 2, MaxWorkspaceBytes = 64 * 1024 * 1024 };
var simdOptions = scalarOptions with { Kernel = EncoderKernelMode.Simd };
using var scalar = new ModernBertEncoder(config, weights, scalarOptions);
using var simd = new ModernBertEncoder(config, weights, simdOptions);
var tokens = Enumerable.Range(1, 32).Select(value => value % config.VocabularySize).ToArray();
var scalarTrace = new Dictionary<string, float[]>(StringComparer.Ordinal);
var simdTrace = new Dictionary<string, float[]>(StringComparer.Ordinal);
scalar.Trace = (name, values) => scalarTrace[name] = values;
simd.Trace = (name, values) => simdTrace[name] = values;
var scalarOutput = scalar.Encode(tokens);
var simdOutput = simd.Encode(tokens);
Check(scalarTrace.Count == simdTrace.Count && scalarTrace.Keys.SequenceEqual(simdTrace.Keys), "scalar/SIMD trace topology");
var maxDifference = 0f;
foreach (var name in scalarTrace.Keys)
{
    var expected = scalarTrace[name];
    var actual = simdTrace[name];
    for (var index = 0; index < expected.Length; index++) maxDifference = MathF.Max(maxDifference, MathF.Abs(expected[index] - actual[index]));
}
Check(maxDifference <= 2e-5f, $"scalar/SIMD per-layer alignment max_abs={maxDifference:G9}");
Check(scalarOutput.Length == simdOutput.Length, "scalar/SIMD output shape");
var directInput = Enumerable.Range(0, 13).Select(value => value * 0.031f).ToArray();
var directWeights = Enumerable.Range(0, 26).Select(value => MathF.Sin(value * 0.11f)).ToArray();
var directScalar = ScalarOps.Linear(directInput, directWeights, null, 1, 13, 2, CancellationToken.None);
var directSimd = SimdOps.Linear(directInput, directWeights, null, 1, 13, 2, CancellationToken.None);
Check(directScalar.Zip(directSimd, (left, right) => MathF.Abs(left - right)).Max() <= 2e-6f, $"SIMD tail linear alignment vector_width={Vector<float>.Count}");
Check(scalar.WorkspacePool.ActiveCount == 0 && simd.WorkspacePool.ActiveCount == 0, "workspace released after encode");

using var pool = new EncoderWorkspacePool(new EncoderExecutionOptions { MaxConcurrentRequests = 2, MaxWorkspaceBytes = 64 * 1024 * 1024 });
using var first = pool.Acquire(tokens.Length, config);
using var second = pool.Acquire(tokens.Length, config);
try
{
    pool.Acquire(tokens.Length, config);
    throw new InvalidOperationException("Expected bounded concurrency rejection.");
}
catch (DecisionException exception) when (exception.Code == "encoder_session_busy")
{
    Check(true, "bounded concurrency rejects third lease");
}
second.Dispose();
using var third = pool.Acquire(tokens.Length, config);
first.Dispose();
third.Dispose();
Check(pool.ActiveCount == 0 && pool.OutstandingBytes == 0, "workspace leases return all resources");

var unloadPool = new EncoderWorkspacePool(new EncoderExecutionOptions { MaxConcurrentRequests = 1, MaxWorkspaceBytes = 64 * 1024 * 1024 });
var unloadLease = unloadPool.Acquire(tokens.Length, config);
unloadPool.Dispose();
unloadLease.Dispose();
Check(unloadLease.IsDisposed && unloadPool.ActiveCount == 0, "unload defers semaphore disposal until lease release");

using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    scalar.Encode(tokens, cancelled.Token);
    throw new InvalidOperationException("Expected cancellation.");
}
catch (OperationCanceledException)
{
    Check(true, "pre-cancel observed before workspace allocation");
}
Check(scalar.WorkspacePool.ActiveCount == 0, "cancel leaves no active workspace");

using var deadlineEncoder = new ModernBertEncoder(config, weights, scalarOptions with { Deadline = TimeSpan.FromTicks(1) });
try
{
    deadlineEncoder.Encode(tokens);
    throw new InvalidOperationException("Expected encoder deadline.");
}
catch (OperationCanceledException)
{
    Check(true, "deadline observed at encoder boundary");
}
Check(deadlineEncoder.WorkspacePool.ActiveCount == 0, "deadline leaves no active workspace");

Console.WriteLine($"Sezika resource tests passed: {passed}; max_trace_abs_error={maxDifference:G9}");
