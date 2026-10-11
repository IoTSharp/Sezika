using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Sezika;

namespace Sezika.Cuda;

/// <summary>Shared resident GPU encoder and three-row marker-logit forward, without asset provenance.</summary>
internal sealed class CudaMarkerLogitScorer
{
    private readonly CudaDevice _device;
    private readonly NativeOwner _owner;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _hiddenSize;
    private readonly string _inputErrorCode, _valueErrorCode, _scope;
    private bool _disposed;
    private Exception? _disposalError;

    internal bool IsDisposed => Volatile.Read(ref _disposed);
    internal int MaxTokens { get; }

    internal CudaMarkerLogitScorer(CudaDevice device, IndependentEncoderModelPackage model, LinearMarkerHead head,
        string inputErrorCode, string valueErrorCode, string scope, CancellationToken cancellationToken)
        : this(device, model.Encoder.Config, model.Encoder.Weights, head, inputErrorCode, valueErrorCode, scope, cancellationToken,
            model.Encoder.Config.MaxTokens > 1024 ? 6UL * 1024 * 1024 * 1024 : 3UL * 1024 * 1024 * 1024) { }

    internal CudaMarkerLogitScorer(CudaDevice device, ModernBertConfig config, ModernBertWeights weights, LinearMarkerHead head,
        string inputErrorCode, string valueErrorCode, string scope, CancellationToken cancellationToken, ulong maximumOwnedBytes)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (head.HiddenSize != config.HiddenSize) throw new DecisionException(inputErrorCode, "Marker head width differs from encoder.");
        _device = device;
        _hiddenSize = head.HiddenSize;
        MaxTokens = config.MaxTokens;
        _inputErrorCode = inputErrorCode; _valueErrorCode = valueErrorCode; _scope = scope;
        // Owner storage, cleanup clock and exception slots exist before acquiring any native resource.
        _owner = new NativeOwner();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var values = head.CopyWeights();
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            _owner.Encoder = new CudaModernBertEncoder(device, config, weights, deadline.Token, maximumOwnedBytes);
            _owner.Kernels = new CudaKernels(device, deadline.Token);
            _owner.Weights = device.Allocate(checked((nuint)values.Length * sizeof(float)));
            device.CopyToDevice(_owner.Weights, values);
            deadline.Token.ThrowIfCancellationRequested();
        }
        catch (Exception error)
        {
            if (_owner.Release() is { } cleanupError)
                throw new AggregateException("CUDA marker scorer construction and owned-resource cleanup failed.", error, cleanupError);
            throw;
        }
        finally { Array.Clear(values); }
    }

    internal double[] Score(ReadOnlySpan<int> tokenIds, MarkerQuestionType type, ReadOnlySpan<int> markerPositions,
        object caller, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, caller);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        token.ThrowIfCancellationRequested();
        if (tokenIds.Length < 2 || tokenIds.Length > MaxTokens || (uint)type > 2 || markerPositions.Length is < 2 or > 32 ||
            (type == MarkerQuestionType.Boolean && markerPositions.Length != 2) || (type == MarkerQuestionType.Score && markerPositions.Length > 10))
            throw new DecisionException(_inputErrorCode, "CUDA token/type/candidate budget is invalid.");
        for (var index = 0; index < markerPositions.Length && index < 32; index++)
        {
            token.ThrowIfCancellationRequested();
            if ((uint)markerPositions[index] >= (uint)tokenIds.Length || (index > 0 && markerPositions[index] <= markerPositions[index - 1]))
                throw new DecisionException(_inputErrorCode, "CUDA markers must be ordered and inside the complete sequence.");
        }
        if (!_gate.Wait(0, token)) throw new DecisionException("decision_session_busy", $"The {_scope} CUDA pipeline is busy.");
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, caller);
            var markers = markerPositions.ToArray();
            return _owner.Encoder!.RunOnDevice(tokenIds, (hidden, rows, ct) =>
            {
                CudaDeviceBuffer? logits = null;
                Exception? executionError = null;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    logits = _device.Allocate(checked((nuint)rows * 3 * sizeof(float)));
                    _owner.Kernels!.Linear(hidden, _owner.Weights!, logits, rows, _hiddenSize, 3);
                    _device.Synchronize();
                    ct.ThrowIfCancellationRequested();
                    var all = new float[checked(rows * 3)];
                    _device.CopyFromDevice(all, logits);
                    ct.ThrowIfCancellationRequested();
                    var result = new double[markers.Length];
                    for (var index = 0; index < result.Length && index < 32; index++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var value = all[checked(markers[index] * 3 + (int)type)];
                        if (!float.IsFinite(value)) throw new DecisionException(_valueErrorCode, "CUDA marker logit is non-finite.");
                        result[index] = value;
                    }
                    return result;
                }
                catch (Exception error) { executionError = error; throw; }
                finally
                {
                    try { logits?.Dispose(); }
                    catch (Exception cleanupError)
                    {
                        if (executionError is not null)
                            throw new AggregateException("CUDA marker forward and request-buffer cleanup failed.", executionError, cleanupError);
                        throw;
                    }
                }
            }, token);
        }
        finally { _gate.Release(); }
    }

    internal void Dispose()
    {
        if (IsDisposed) { RethrowDisposalError(); return; }
        if (!_gate.Wait(0)) throw new DecisionException("decision_session_busy", $"Cannot unload an active {_scope} CUDA pipeline.");
        try
        {
            if (IsDisposed) { RethrowDisposalError(); return; }
            Volatile.Write(ref _disposed, true);
            _disposalError = _owner.Release();
            RethrowDisposalError();
        }
        finally { _gate.Release(); }
    }

    private void RethrowDisposalError()
    {
        if (_disposalError is { } error) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class NativeOwner
    {
        private static readonly TimeSpan MaxCleanupDuration = TimeSpan.FromSeconds(15);
        private readonly Stopwatch _clock = new();
        private readonly List<Exception> _errors = new(4);
        private bool _released;
        private Exception? _error;
        internal CudaModernBertEncoder? Encoder;
        internal CudaKernels? Kernels;
        internal CudaDeviceBuffer? Weights;

        internal Exception? Release()
        {
            if (_released) return _error;
            _released = true;
            _clock.Start();
            // Exactly three owners. Cancellation does not skip independent recovery.
            ReleaseOne(Weights);
            ReleaseOne(Kernels);
            ReleaseOne(Encoder);
            if (_clock.Elapsed >= MaxCleanupDuration && _errors.Count == 0)
                _errors.Add(new DecisionException("cuda_pipeline_cleanup_incomplete", "CUDA marker cleanup exceeded its cooperative deadline; caller retains the device."));
            _error = _errors.Count switch
            {
                0 => null,
                1 => _errors[0],
                _ => new AggregateException("CUDA marker owned-resource cleanup failed or is incomplete.", _errors)
            };
            return _error;
        }

        private void ReleaseOne(IDisposable? resource)
        {
            if (resource is null) return;
            if (_clock.Elapsed >= MaxCleanupDuration)
            {
                _errors.Add(new DecisionException("cuda_pipeline_cleanup_incomplete", "A CUDA marker owner was retained after the recovery deadline; caller retains the device."));
                return;
            }
            try { resource.Dispose(); }
            catch (Exception error) { _errors.Add(error); }
        }
    }
}
