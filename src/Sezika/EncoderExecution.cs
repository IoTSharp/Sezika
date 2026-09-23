namespace Sezika;

/// <summary>CPU kernel selected for an encoder request.</summary>
public enum EncoderKernelMode
{
    Scalar,
    Simd,
}

/// <summary>
/// Bounded execution policy for a CPU encoder. Scalar remains the numerical
/// reference; SIMD only changes the explicitly vectorized reductions and
/// elementwise operations.
/// </summary>
public sealed record EncoderExecutionOptions
{
    public EncoderKernelMode Kernel { get; init; } = EncoderKernelMode.Scalar;
    public TimeSpan Deadline { get; init; } = TimeSpan.FromMinutes(10);
    public int MaxConcurrentRequests { get; init; } = 1;
    public long MaxWorkspaceBytes { get; init; } = 512L * 1024 * 1024;

    public static EncoderExecutionOptions Default { get; } = new();

    public void Validate()
    {
        if (!Enum.IsDefined(Kernel) || Deadline <= TimeSpan.Zero || Deadline > TimeSpan.FromMinutes(10) ||
            MaxConcurrentRequests is < 1 or > 64 || MaxWorkspaceBytes <= 0)
        {
            throw new DecisionException("encoder_execution_options_invalid", "Encoder execution limits are invalid.");
        }
    }
}

/// <summary>One bounded request workspace lease.</summary>
public sealed class EncoderWorkspace : IDisposable
{
    private readonly Action<EncoderWorkspace> _release;
    private int _disposed;

    internal EncoderWorkspace(int tokenCount, long reservedBytes, Action<EncoderWorkspace> release)
    {
        TokenCount = tokenCount;
        ReservedBytes = reservedBytes;
        _release = release;
    }

    public int TokenCount { get; }
    public long ReservedBytes { get; }
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _release(this);
        }
    }
}

/// <summary>
/// Bounded workspace accounting for one encoder session. The model weights are
/// shared and read-only; each request receives an independent lease. The
/// arrays used by the current scalar/SIMD implementation are short lived, but
/// the lease makes their upper bound and lifecycle observable to the host.
/// </summary>
public sealed class EncoderWorkspacePool : IDisposable
{
    private readonly object _lifecycleSync = new();
    private readonly SemaphoreSlim _gate;
    private readonly long _maxBytes;
    private long _outstandingBytes;
    private int _activeCount;
    private int _disposed;

    public EncoderWorkspacePool(EncoderExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _gate = new SemaphoreSlim(options.MaxConcurrentRequests, options.MaxConcurrentRequests);
        _maxBytes = options.MaxWorkspaceBytes;
        Capacity = options.MaxConcurrentRequests;
    }

    public int Capacity { get; }
    public int ActiveCount => Volatile.Read(ref _activeCount);
    public long OutstandingBytes => Volatile.Read(ref _outstandingBytes);
    public long MaxBytes => _maxBytes;

    public EncoderWorkspace Acquire(int tokenCount, ModernBertConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (tokenCount < 2 || tokenCount > config.MaxTokens)
        {
            throw new DecisionException("encoder_token_limit_exceeded", "Sequence length is outside the encoder bounds.");
        }

        var estimate = EstimateBytes(config, tokenCount);
        if (estimate > _maxBytes)
        {
            throw new DecisionException("encoder_workspace_limit_exceeded", $"The request workspace estimate ({estimate} bytes) exceeds the configured limit ({_maxBytes} bytes).");
        }

        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            // Wait(0) gives callers a deterministic, bounded busy response. A
            // caller cancellation still wins and is observed by SemaphoreSlim.
            if (!_gate.Wait(0, cancellationToken))
            {
                throw new DecisionException("encoder_session_busy", "The encoder workspace is busy.");
            }

            var total = Interlocked.Add(ref _outstandingBytes, estimate);
            if (total > _maxBytes)
            {
                Interlocked.Add(ref _outstandingBytes, -estimate);
                _gate.Release();
                throw new DecisionException("encoder_workspace_limit_exceeded", "Concurrent request workspaces exceed the configured memory limit.");
            }

            Interlocked.Increment(ref _activeCount);
            return new EncoderWorkspace(tokenCount, estimate, Release);
        }
    }

    public static long EstimateBytes(ModernBertConfig config, int tokenCount)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (tokenCount < 0 || tokenCount > config.MaxTokens)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenCount));
        }

        // Hidden, q/k/v, attention, projection, MLP and residual stages may
        // overlap until the next GC safepoint. The conservative factors leave
        // room for those short-lived arrays instead of pretending that a
        // reference assignment immediately returns their memory.
        checked
        {
            var sequence = (long)tokenCount * config.HiddenSize;
            var intermediate = (long)tokenCount * config.IntermediateSize;
            var attentionScores = (long)tokenCount * tokenCount;
            return checked((sequence * 64 + intermediate * 16 + attentionScores) * sizeof(float));
        }
    }

    private void Release(EncoderWorkspace workspace)
    {
        lock (_lifecycleSync)
        {
            Interlocked.Add(ref _outstandingBytes, -workspace.ReservedBytes);
            var remaining = Interlocked.Decrement(ref _activeCount);
            _gate.Release();
            if (Volatile.Read(ref _disposed) != 0 && remaining == 0)
            {
                _gate.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_lifecycleSync)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && Volatile.Read(ref _activeCount) == 0)
            {
                _gate.Dispose();
            }
        }
    }
}
