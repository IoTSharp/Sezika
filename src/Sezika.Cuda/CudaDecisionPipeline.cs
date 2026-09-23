using Sezika;

namespace Sezika.Cuda;

/// <summary>Runs the checkpoint's type embedding, Transformer head and marker scorer on the CUDA device.</summary>
public sealed class CudaDecisionPipeline : IDisposable
{
    private readonly CudaDevice _device;
    private readonly CudaModernBertEncoder _encoder;
    private readonly CudaKernels _kernels;
    private readonly List<CudaDeviceBuffer> _weights = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CudaDeviceBuffer _typeEmbeddings;
    private readonly Layer[] _layers;
    private readonly CudaDeviceBuffer _scorerNorm;
    private readonly CudaDeviceBuffer _scorerNormBias;
    private readonly CudaDeviceBuffer _scorerDense;
    private readonly CudaDeviceBuffer _scorerDenseBias;
    private readonly CudaDeviceBuffer _scorerOutput;
    private readonly CudaDeviceBuffer _scorerOutputBias;
    private bool _disposed;

    /// <remarks>The caller retains ownership of the encoder and device.</remarks>
    public CudaDecisionPipeline(CudaDevice device, CudaModernBertEncoder encoder, DecisionHeadWeights head, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(head);
        _device = device;
        _encoder = encoder;
        _kernels = new CudaKernels(device);
        _layers = new Layer[head.Layers.Length];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            var hidden = encoder.Config.HiddenSize;
            if (head.TypeEmbeddings.Length != checked(hidden * 3) || head.Layers.Length != 2)
                throw new DecisionException("model_tensor_shape_invalid", "Decision head type embedding or layer count is invalid.");
            _typeEmbeddings = Upload(head.TypeEmbeddings, 3 * hidden, deadline.Token);
            for (var index = 0; index < _layers.Length; index++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var source = head.Layers[index];
                _layers[index] = new Layer(
                    Upload(source.Qkv, 3 * hidden * hidden, deadline.Token),
                    Upload(source.QkvBias, 3 * hidden, deadline.Token),
                    Upload(source.AttentionOutput, hidden * hidden, deadline.Token),
                    Upload(source.AttentionOutputBias, hidden, deadline.Token),
                    Upload(source.AttentionNorm, hidden, deadline.Token),
                    Upload(source.AttentionNormBias, hidden, deadline.Token),
                    Upload(source.MlpUp, 4 * hidden * hidden, deadline.Token),
                    Upload(source.MlpUpBias, 4 * hidden, deadline.Token),
                    Upload(source.MlpDown, 4 * hidden * hidden, deadline.Token),
                    Upload(source.MlpDownBias, hidden, deadline.Token),
                    Upload(source.MlpNorm, hidden, deadline.Token),
                    Upload(source.MlpNormBias, hidden, deadline.Token));
            }
            _scorerNorm = Upload(head.ScorerNorm, hidden, deadline.Token);
            _scorerNormBias = Upload(head.ScorerNormBias, hidden, deadline.Token);
            _scorerDense = Upload(head.ScorerDense, hidden * hidden, deadline.Token);
            _scorerDenseBias = Upload(head.ScorerDenseBias, hidden, deadline.Token);
            _scorerOutput = Upload(head.ScorerOutput, hidden, deadline.Token);
            _scorerOutputBias = Upload(head.ScorerOutputBias, 1, deadline.Token);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Action<string, float[]>? Trace { get; set; }

    public float[] Score(ReadOnlySpan<int> tokenIds, int typeId, ReadOnlySpan<int> markerPositions, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)typeId >= 3 || markerPositions.Length is < 2 or > 32)
            throw new DecisionException("decision_head_input_invalid", "The checkpoint expects a known question type and between 2 and 32 markers.");
        foreach (var position in markerPositions)
            if ((uint)position >= (uint)tokenIds.Length)
                throw new DecisionException("decision_head_input_invalid", "A candidate marker position is outside the input sequence.");
        if (!_gate.Wait(0, cancellationToken))
            throw new DecisionException("decision_session_busy", "The CUDA decision head session is busy.");
        var markers = markerPositions.ToArray();
        try
        {
            return _encoder.RunOnDevice(tokenIds,
                (hidden, rows, token) => ScoreOnDevice(hidden, rows, typeId, markers, token), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private float[] ScoreOnDevice(CudaDeviceBuffer hidden, int rows, int typeId, int[] markers, CancellationToken cancellationToken)
    {
        var width = _encoder.Config.HiddenSize;
        var heads = Math.Max(1, width / 64);
        var count = checked(rows * width);
        using var normalized = Allocate(count);
        using var qkv = Allocate(checked(count * 3));
        using var q = Allocate(count);
        using var k = Allocate(count);
        using var v = Allocate(count);
        using var attentionScores = Allocate(checked(rows * rows * heads));
        using var attention = Allocate(count);
        using var update = Allocate(count);
        using var up = Allocate(checked(count * 4));
        using var activated = Allocate(checked(count * 4));
        using var logits = Allocate(rows);
        _kernels.TypeEmbeddingAdd(hidden, _typeEmbeddings, rows, width, typeId);
        Observe("head/type", hidden, count, cancellationToken);
        for (var index = 0; index < _layers.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layer = _layers[index];
            var prefix = $"head/{index}/";
            _kernels.Norm(hidden, layer.AttentionNorm, layer.AttentionNormBias, normalized, rows, width, _encoder.Config.NormEpsilon);
            Observe(prefix + "norm1", normalized, count, cancellationToken);
            _kernels.Linear(normalized, layer.Qkv, qkv, rows, width, 3 * width);
            _kernels.RowBias(qkv, layer.QkvBias, rows, 3 * width);
            Observe(prefix + "qkv", qkv, 3 * count, cancellationToken);
            _kernels.SplitQkv(qkv, q, k, v, rows, width);
            _kernels.AttentionScores(q, k, attentionScores, rows, heads, width / heads, 0);
            Complete(cancellationToken);
            _kernels.Softmax(attentionScores, checked(rows * heads), rows);
            Complete(cancellationToken);
            _kernels.AttentionContext(attentionScores, v, attention, rows, heads, width / heads);
            Observe(prefix + "attention", attention, count, cancellationToken);
            _kernels.Linear(attention, layer.AttentionOutput, update, rows, width, width);
            _kernels.RowBias(update, layer.AttentionOutputBias, rows, width);
            Observe(prefix + "projected", update, count, cancellationToken);
            _kernels.Add(hidden, update, count);
            Observe(prefix + "residual", hidden, count, cancellationToken);
            _kernels.Norm(hidden, layer.MlpNorm, layer.MlpNormBias, normalized, rows, width, _encoder.Config.NormEpsilon);
            Observe(prefix + "norm2", normalized, count, cancellationToken);
            _kernels.Linear(normalized, layer.MlpUp, up, rows, width, 4 * width);
            _kernels.RowBias(up, layer.MlpUpBias, rows, 4 * width);
            Observe(prefix + "up", up, 4 * count, cancellationToken);
            _kernels.Activate(up, activated, 4 * count, relu: true);
            Observe(prefix + "relu", activated, 4 * count, cancellationToken);
            _kernels.Linear(activated, layer.MlpDown, update, rows, 4 * width, width);
            _kernels.RowBias(update, layer.MlpDownBias, rows, width);
            Observe(prefix + "mlp", update, count, cancellationToken);
            _kernels.Add(hidden, update, count);
            Observe(prefix + "hidden", hidden, count, cancellationToken);
        }
        _kernels.Norm(hidden, _scorerNorm, _scorerNormBias, normalized, rows, width, _encoder.Config.NormEpsilon);
        Observe("scorer/norm", normalized, count, cancellationToken);
        _kernels.Linear(normalized, _scorerDense, update, rows, width, width);
        _kernels.RowBias(update, _scorerDenseBias, rows, width);
        Observe("scorer/dense", update, count, cancellationToken);
        _kernels.Activate(update, attention, count);
        Observe("scorer/gelu", attention, count, cancellationToken);
        _kernels.Linear(attention, _scorerOutput, logits, rows, width, 1);
        _kernels.RowBias(logits, _scorerOutputBias, rows, 1);
        Observe("scorer/logits", logits, rows, cancellationToken);
        var allLogits = new float[rows];
        _device.CopyFromDevice(allLogits, logits);
        var result = new float[markers.Length];
        for (var index = 0; index < markers.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[index] = allLogits[markers[index]];
            if (!float.IsFinite(result[index]))
                throw new DecisionException("decision_nonfinite_logits", "The CUDA checkpoint produced a non-finite marker logit.");
        }
        return result;
    }

    private CudaDeviceBuffer Upload(float[] values, int expectedLength, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (values.Length != expectedLength)
            throw new DecisionException("model_tensor_shape_invalid", "A decision head tensor has an unexpected shape.");
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
            throw new DecisionException("decision_session_busy", "Cannot unload the CUDA decision head while inference is running.");
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

    private sealed record Layer(CudaDeviceBuffer Qkv, CudaDeviceBuffer QkvBias,
        CudaDeviceBuffer AttentionOutput, CudaDeviceBuffer AttentionOutputBias,
        CudaDeviceBuffer AttentionNorm, CudaDeviceBuffer AttentionNormBias,
        CudaDeviceBuffer MlpUp, CudaDeviceBuffer MlpUpBias, CudaDeviceBuffer MlpDown,
        CudaDeviceBuffer MlpDownBias, CudaDeviceBuffer MlpNorm, CudaDeviceBuffer MlpNormBias);
}
