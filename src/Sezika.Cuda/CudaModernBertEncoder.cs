using Sezika;

namespace Sezika.Cuda;

/// <summary>ModernBERT inference with resident FP32 weights and generated C# CUDA kernels.</summary>
public sealed class CudaModernBertEncoder : IEncoder, IDisposable
{
    private readonly CudaDevice _device;
    private readonly CudaKernels _kernels;
    private readonly List<CudaDeviceBuffer> _weights = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CudaDeviceBuffer _embeddings;
    private readonly CudaDeviceBuffer _embeddingNorm;
    private readonly CudaDeviceBuffer _finalNorm;
    private readonly Layer[] _layers;
    private bool _disposed;

    public CudaModernBertEncoder(CudaDevice device, ModernBertConfig config, ModernBertWeights weights, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(weights);
        cancellationToken.ThrowIfCancellationRequested();
        // The scalar encoder validates the shared tensor contract without running inference.
        using (var validatedEncoder = new ModernBertEncoder(config, weights))
        {
            // Construction validates the shared tensor contract before any
            // device allocation. Its bounded workspace is immediately
            // released because CUDA owns the resident execution buffers.
        }
        Config = config;
        Weights = weights;
        _device = device;
        _layers = new Layer[config.LayerCount];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        _kernels = new CudaKernels(device, deadline.Token);
        try
        {
            _embeddings = Upload(weights.TokenEmbeddings, deadline.Token);
            _embeddingNorm = Upload(weights.EmbeddingNorm, deadline.Token);
            _finalNorm = Upload(weights.FinalNorm, deadline.Token);
            for (var index = 0; index < _layers.Length; index++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var source = weights.Layers[index];
                _layers[index] = new Layer(
                    source.AttentionNorm is null ? null : Upload(source.AttentionNorm, deadline.Token),
                    Upload(source.Qkv, deadline.Token), Upload(source.AttentionOutput, deadline.Token),
                    Upload(source.MlpNorm, deadline.Token), Upload(source.MlpUp, deadline.Token),
                    Upload(source.MlpDown, deadline.Token));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public ModernBertConfig Config { get; }
    public ModernBertWeights Weights { get; }
    internal CudaDevice Device => _device;

    /// <summary>Optional diagnostic snapshots; enabling this copies each named operator output to the host.</summary>
    public Action<string, float[]>? Trace { get; set; }

    public float[] Encode(ReadOnlySpan<int> tokenIds, CancellationToken cancellationToken = default) =>
        RunOnDevice(tokenIds, (hidden, rows, _) =>
        {
            var result = new float[checked(rows * Config.HiddenSize)];
            _device.CopyFromDevice(result, hidden);
            return result;
        }, cancellationToken);

    internal T RunOnDevice<T>(ReadOnlySpan<int> tokenIds, Func<CudaDeviceBuffer, int, CancellationToken, T> continuation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (tokenIds.Length < 2 || tokenIds.Length > Config.MaxTokens || tokenIds.Length > 1024)
            throw new DecisionException("decision_token_limit_exceeded", $"Sequence length must be between 2 and {Math.Min(Config.MaxTokens, 1024)}.");
        foreach (var value in tokenIds)
            if ((uint)value >= (uint)Config.VocabularySize)
                throw new DecisionException("tokenizer_token_out_of_range", $"Token ID {value} is outside the vocabulary.");
        if (!_gate.Wait(0, cancellationToken))
            throw new DecisionException("decision_session_busy", "The CUDA encoder session is busy.");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            var ct = deadline.Token;
            ct.ThrowIfCancellationRequested();
            _device.ThrowIfDisposed();
            var rows = tokenIds.Length;
            var hiddenSize = Config.HiddenSize;
            var intermediateSize = Config.IntermediateSize;
            var hiddenCount = checked(rows * hiddenSize);
            using var ids = _device.Allocate(checked((nuint)rows * sizeof(int)));
            using var hidden = Allocate(hiddenCount);
            using var normalized = Allocate(hiddenCount);
            using var qkv = Allocate(checked(hiddenCount * 3));
            using var q = Allocate(hiddenCount);
            using var k = Allocate(hiddenCount);
            using var v = Allocate(hiddenCount);
            using var scores = Allocate(checked(rows * rows * Config.HeadCount));
            using var attention = Allocate(hiddenCount);
            using var update = Allocate(hiddenCount);
            using var up = Allocate(checked(rows * intermediateSize * 2));
            using var activated = Allocate(checked(rows * intermediateSize));
            _device.CopyToDevice(ids, tokenIds);
            _kernels.Embedding(ids, _embeddings, normalized, rows, hiddenSize);
            Observe("embedding/raw", normalized, hiddenCount, ct);
            _kernels.Norm(normalized, _embeddingNorm, null, hidden, rows, hiddenSize, Config.NormEpsilon);
            Observe("embedding/norm", hidden, hiddenCount, ct);
            for (var index = 0; index < _layers.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                var layer = _layers[index];
                var prefix = $"layer/{index}/";
                var source = hidden;
                if (layer.AttentionNorm is not null)
                {
                    _kernels.Norm(hidden, layer.AttentionNorm, null, normalized, rows, hiddenSize, Config.NormEpsilon);
                    source = normalized;
                }
                Observe(prefix + "norm1", source, hiddenCount, ct);
                _kernels.Linear(source, layer.Qkv, qkv, rows, hiddenSize, 3 * hiddenSize);
                Observe(prefix + "qkv", qkv, 3 * hiddenCount, ct);
                _kernels.SplitQkv(qkv, q, k, v, rows, hiddenSize);
                var global = index % Config.GlobalAttentionEvery == 0;
                _kernels.Rope(q, k, rows, Config.HeadCount, hiddenSize / Config.HeadCount,
                    global ? Config.GlobalRopeTheta : Config.LocalRopeTheta);
                Observe(prefix + "rope_q", q, hiddenCount, ct);
                Observe(prefix + "rope_k", k, hiddenCount, ct);
                _kernels.AttentionScores(q, k, scores, rows, Config.HeadCount, hiddenSize / Config.HeadCount,
                    global ? 0 : Config.LocalAttention / 2);
                Complete(ct);
                _kernels.Softmax(scores, checked(rows * Config.HeadCount), rows);
                Complete(ct);
                _kernels.AttentionContext(scores, v, attention, rows, Config.HeadCount, hiddenSize / Config.HeadCount);
                Observe(prefix + "attention", attention, hiddenCount, ct);
                _kernels.Linear(attention, layer.AttentionOutput, update, rows, hiddenSize, hiddenSize);
                Observe(prefix + "projected", update, hiddenCount, ct);
                _kernels.Add(hidden, update, hiddenCount);
                Observe(prefix + "residual", hidden, hiddenCount, ct);
                _kernels.Norm(hidden, layer.MlpNorm, null, normalized, rows, hiddenSize, Config.NormEpsilon);
                Observe(prefix + "norm2", normalized, hiddenCount, ct);
                _kernels.Linear(normalized, layer.MlpUp, up, rows, hiddenSize, 2 * intermediateSize);
                Observe(prefix + "up", up, checked(rows * intermediateSize * 2), ct);
                _kernels.GatedGelu(up, activated, rows, intermediateSize);
                Observe(prefix + "gelu", activated, checked(rows * intermediateSize), ct);
                _kernels.Linear(activated, layer.MlpDown, update, rows, intermediateSize, hiddenSize);
                Observe(prefix + "mlp", update, hiddenCount, ct);
                _kernels.Add(hidden, update, hiddenCount);
                Observe(prefix + "hidden", hidden, hiddenCount, ct);
            }
            _kernels.Norm(hidden, _finalNorm, null, normalized, rows, hiddenSize, Config.NormEpsilon);
            Observe("encoder/final", normalized, hiddenCount, ct);
            ct.ThrowIfCancellationRequested();
            return continuation(normalized, rows, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private CudaDeviceBuffer Upload(float[] values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = Allocate(values.Length);
        _weights.Add(buffer);
        _device.CopyToDevice(buffer, values);
        cancellationToken.ThrowIfCancellationRequested();
        return buffer;
    }

    private CudaDeviceBuffer Allocate(int count) => _device.Allocate(checked((nuint)count * sizeof(float)));

    private void Complete(CancellationToken cancellationToken)
    {
        _device.Synchronize();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Observe(string name, CudaDeviceBuffer buffer, int count, CancellationToken cancellationToken)
    {
        Complete(cancellationToken);
        if (Trace is not { } trace) return;
        var values = new float[count];
        _device.CopyFromDevice(values, buffer);
        trace(name, values);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!_gate.Wait(0))
            throw new DecisionException("decision_session_busy", "Cannot unload the CUDA encoder while inference is running.");
        try
        {
            _disposed = true;
            foreach (var buffer in _weights) buffer.Dispose();
            _weights.Clear();
            _kernels.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record Layer(CudaDeviceBuffer? AttentionNorm, CudaDeviceBuffer Qkv,
        CudaDeviceBuffer AttentionOutput, CudaDeviceBuffer MlpNorm, CudaDeviceBuffer MlpUp, CudaDeviceBuffer MlpDown);
}
