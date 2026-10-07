using Sezika;

namespace Sezika.Cuda;

/// <summary>Independent frozen encoder and three-row linear marker head using static generated CUDA Driver kernels.</summary>
/// <remarks>Owns its CUDA encoder, head weights and modules. The caller retains the CudaDevice and CPU model package.</remarks>
public sealed class IndependentCudaDecisionPipeline : IIndependentDecisionPipeline
{
    private readonly CudaDevice _device;
    private readonly CudaModernBertEncoder _encoder;
    private readonly CudaKernels _kernels;
    private readonly CudaDeviceBuffer _weights;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    public MarkerFeatureIdentity Identity { get; }
    public string FeatureSha256 { get; }
    public string HeadSha256 { get; }
    public string Backend => "cuda-independent-linear-marker-head";
    public int MaxTokens => _encoder.Config.MaxTokens;

    public IndependentCudaDecisionPipeline(CudaDevice device, IndependentEncoderModelPackage model,
        IndependentMarkerHeadAsset head, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(head);
        cancellationToken.ThrowIfCancellationRequested();
        var id = head.Identity;
        if (model.IsDisposed || id.ModelId != model.ModelId || id.HiddenSize != model.Encoder.Config.HiddenSize ||
            id.HiddenSize != head.Head.HiddenSize || id.Protocol != "decision-v1" || id.LengthPolicy != "strict" ||
            !id.ModelId.StartsWith("sezika/", StringComparison.Ordinal) ||
            !SameHash(id.EncoderSha256, model.EncoderSha256) || !SameHash(id.TokenizerSha256, model.TokenizerSha256) ||
            !IsHash(id.DataManifestSha256) || !IsHash(head.FeatureSha256) || !IsHash(head.FileSha256))
            throw new DecisionException("independent_runtime_identity_mismatch", "CUDA independent head differs from the loaded encoder or complete asset contract.");
        _device = device; Identity = id; FeatureSha256 = head.FeatureSha256; HeadSha256 = head.FileSha256;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var values = head.Head.CopyWeights();
        try
        {
            _encoder = new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, deadline.Token);
            _kernels = new CudaKernels(device, deadline.Token);
            _weights = device.Allocate(checked((nuint)values.Length * sizeof(float)));
            device.CopyToDevice(_weights, values);
            deadline.Token.ThrowIfCancellationRequested();
        }
        catch
        {
            Dispose();
            throw;
        }
        finally { Array.Clear(values); }
    }

    public double[] Score(ReadOnlySpan<int> tokenIds, MarkerQuestionType type, ReadOnlySpan<int> markerPositions,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        token.ThrowIfCancellationRequested();
        if (tokenIds.Length < 2 || tokenIds.Length > MaxTokens || (uint)type > 2 || markerPositions.Length is < 2 or > 32 ||
            (type == MarkerQuestionType.Boolean && markerPositions.Length != 2) || (type == MarkerQuestionType.Score && markerPositions.Length > 10))
            throw new DecisionException("independent_pipeline_input_invalid", "Independent CUDA token/type/candidate budget is invalid.");
        for (var index = 0; index < markerPositions.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if ((uint)markerPositions[index] >= (uint)tokenIds.Length || (index > 0 && markerPositions[index] <= markerPositions[index - 1]))
                throw new DecisionException("independent_pipeline_input_invalid", "Independent CUDA markers must be ordered and inside the complete sequence.");
        }
        if (!_gate.Wait(0, token)) throw new DecisionException("decision_session_busy", "The independent CUDA pipeline is busy.");
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var markers = markerPositions.ToArray();
            return _encoder.RunOnDevice(tokenIds, (hidden, rows, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                using var logits = _device.Allocate(checked((nuint)rows * 3 * sizeof(float)));
                // [3,hidden] is the independently trained head. No legacy type
                // embedding, Transformer head or scorer weight enters this path.
                _kernels.Linear(hidden, _weights, logits, rows, Identity.HiddenSize, 3);
                _device.Synchronize();
                ct.ThrowIfCancellationRequested();
                var all = new float[checked(rows * 3)];
                _device.CopyFromDevice(all, logits);
                ct.ThrowIfCancellationRequested();
                var result = new double[markers.Length];
                for (var index = 0; index < result.Length; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var value = all[checked(markers[index] * 3 + (int)type)];
                    if (!float.IsFinite(value))
                        throw new DecisionException("independent_runtime_value_invalid", "Independent CUDA marker logit is non-finite.");
                    result[index] = value;
                }
                return result;
            }, token);
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed)) return;
        if (!_gate.Wait(0)) throw new DecisionException("decision_session_busy", "Cannot unload an active independent CUDA pipeline.");
        try
        {
            if (_disposed) return;
            Volatile.Write(ref _disposed, true);
            try { _weights?.Dispose(); }
            finally
            {
                try { _kernels?.Dispose(); }
                finally { _encoder?.Dispose(); }
            }
        }
        finally { _gate.Release(); }
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool SameHash(string left, string right) => IsHash(left) && IsHash(right) && left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
