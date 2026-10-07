namespace Sezika;

/// <summary>Local independent encoder/linear-head execution with complete immutable asset binding.</summary>
/// <remarks>The runtime owns an injected pipeline. Implementations must honor cancellation and retain candidate order.</remarks>
public interface IIndependentDecisionPipeline : IDisposable
{
    MarkerFeatureIdentity Identity { get; }
    string FeatureSha256 { get; }
    string HeadSha256 { get; }
    string Backend { get; }
    int MaxTokens { get; }
    double[] Score(ReadOnlySpan<int> tokenIds, MarkerQuestionType type, ReadOnlySpan<int> markerPositions,
        CancellationToken cancellationToken = default);
}

/// <summary>Reference CPU execution; the caller retains ownership of the encoder.</summary>
public sealed class IndependentCpuDecisionPipeline : IIndependentDecisionPipeline
{
    private readonly ModernBertEncoder _encoder;
    private readonly LinearMarkerHead _head;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    public MarkerFeatureIdentity Identity { get; }
    public string FeatureSha256 { get; }
    public string HeadSha256 { get; }
    public string Backend => "cpu-independent-linear-marker-head";
    public int MaxTokens => _encoder.Config.MaxTokens;

    public IndependentCpuDecisionPipeline(ModernBertEncoder encoder, IndependentMarkerHeadAsset head)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(head);
        IndependentMarkerHeadTrainer.ValidateIdentity(head.Identity, "independent_pipeline_identity_invalid");
        if (!IndependentMarkerHeadTrainer.IsHash(head.FeatureSha256) || !IndependentMarkerHeadTrainer.IsHash(head.FileSha256) ||
            head.Identity.HiddenSize != encoder.Config.HiddenSize || head.Head.HiddenSize != head.Identity.HiddenSize)
            throw new DecisionException("independent_pipeline_identity_invalid", "CPU pipeline head identity or hidden width is invalid.");
        _encoder = encoder;
        _head = new LinearMarkerHead(head.Identity.HiddenSize, head.Head.CopyWeights());
        Identity = head.Identity; FeatureSha256 = head.FeatureSha256; HeadSha256 = head.FileSha256;
    }

    public double[] Score(ReadOnlySpan<int> tokenIds, MarkerQuestionType type, ReadOnlySpan<int> markerPositions,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        token.ThrowIfCancellationRequested();
        ValidateMarkers(tokenIds.Length, MaxTokens, type, markerPositions, token);
        if (!_gate.Wait(0, token)) throw new DecisionException("decision_session_busy", "The independent CPU pipeline is busy.");
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var hidden = _encoder.Encode(tokenIds, token);
            var width = Identity.HiddenSize;
            if (hidden.Length != checked(tokenIds.Length * width))
                throw new DecisionException("independent_runtime_shape_invalid", "Independent encoder output does not contain a complete sequence.");
            var features = new float[markerPositions.Length][];
            for (var candidate = 0; candidate < features.Length; candidate++)
            {
                token.ThrowIfCancellationRequested();
                features[candidate] = hidden.AsSpan(checked(markerPositions[candidate] * width), width).ToArray();
            }
            return _head.Score(type, features, token.ThrowIfCancellationRequested);
        }
        finally { _gate.Release(); }
    }

    internal static void ValidateMarkers(int tokenCount, int maximum, MarkerQuestionType type,
        ReadOnlySpan<int> markers, CancellationToken token)
    {
        if (tokenCount < 2 || tokenCount > maximum || (uint)type > 2 || markers.Length is < 2 or > 32 ||
            (type == MarkerQuestionType.Boolean && markers.Length != 2) || (type == MarkerQuestionType.Score && markers.Length > 10))
            throw new DecisionException("independent_pipeline_input_invalid", "Independent token/type/candidate budget is invalid.");
        for (var index = 0; index < markers.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if ((uint)markers[index] >= (uint)tokenCount || (index > 0 && markers[index] <= markers[index - 1]))
                throw new DecisionException("independent_pipeline_input_invalid", "Independent marker positions must be ordered and inside the sequence.");
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed)) return;
        if (!_gate.Wait(0)) throw new DecisionException("decision_session_busy", "Cannot unload an active independent CPU pipeline.");
        try { if (!_disposed) Volatile.Write(ref _disposed, true); }
        finally { _gate.Release(); }
    }
}
