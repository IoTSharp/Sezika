namespace Sezika;

/// <summary>Explicit local execution of an untrained diagnostic head, with no training identity.</summary>
/// <remarks>The runtime owns the supplied pipeline. Implementations honor cancellation and candidate order.</remarks>
public interface IDiagnosticDecisionPipeline : IDisposable
{
    DiagnosticHeadIdentity Identity { get; }
    string DiagnosticAssetSha256 { get; }
    string InitializationDescriptorSha256 { get; }
    string Backend { get; }
    int MaxTokens { get; }
    double[] Score(ReadOnlySpan<int> tokenIds, MarkerQuestionType type, ReadOnlySpan<int> markerPositions,
        CancellationToken cancellationToken = default);
}
