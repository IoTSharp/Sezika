namespace Sezika;

/// <summary>
/// Owns one verified ModernBERT model package and its typed decision session.
/// This is the small public entry point for hosts that do not need to manage
/// the loader, request parser and engine as separate objects.
/// </summary>
public sealed class DecisionModelRuntime : IDecisionEngine, IDisposable
{
    private readonly ModernBertDecisionEngine _engine;
    private readonly DecisionLimits _limits;
    private int _disposed;

    /// <summary>
    /// Creates a runtime that takes ownership of <paramref name="model"/>.
    /// Dispose this runtime to unload the model and release its workspace.
    /// </summary>
    public DecisionModelRuntime(
        ModernBertModelPackage model,
        DecisionLimits? limits = null,
        DecisionResourceBudget? budget = null,
        double minimumConcentration = 0d)
    {
        ArgumentNullException.ThrowIfNull(model);
        _limits = limits ?? DecisionLimits.Default;
        try
        {
            _limits.Validate();
            _engine = new ModernBertDecisionEngine(model, _limits, budget, minimumConcentration);
        }
        catch
        {
            // Construction transfers ownership immediately; a rejected budget
            // or malformed head must not leave the loaded tensors resident.
            model.Dispose();
            throw;
        }

        ModelId = model.ModelId;
        Revision = model.Revision;
        TokenizerRevision = model.TokenizerRevision;
        EstimatedResidentBytes = model.EstimatedResidentBytes;
    }

    /// <summary>Loads a verified model package and creates a runtime that owns its resources.</summary>
    public static DecisionModelRuntime Load(
        string packageDirectory,
        DecisionLimits? limits = null,
        DecisionResourceBudget? budget = null,
        double minimumConcentration = 0d,
        CancellationToken cancellationToken = default)
    {
        var model = ModernBertModelLoader.Load(packageDirectory, cancellationToken);
        try
        {
            return new DecisionModelRuntime(model, limits, budget, minimumConcentration);
        }
        catch
        {
            // The constructor normally performs this cleanup. Keep the static
            // factory exception-safe if construction changes in the future.
            model.Dispose();
            throw;
        }
    }

    public string ModelId { get; }
    public string Revision { get; }
    public string TokenizerRevision { get; }
    public long EstimatedResidentBytes { get; }
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Evaluates an already parsed, structurally typed request.</summary>
    public DecisionResponse Evaluate(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _engine.Evaluate(request, cancellationToken);
    }

    /// <summary>
    /// Parses and evaluates one UTF-8 JSON request using the same limits that
    /// were applied when the session was created.
    /// </summary>
    public DecisionResponse Evaluate(ReadOnlySpan<byte> utf8Json, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _engine.Evaluate(DecisionRequestParser.Parse(utf8Json, _limits), cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _engine.Dispose();
    }
}
