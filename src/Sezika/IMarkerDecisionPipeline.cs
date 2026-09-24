namespace Sezika;

/// <summary>Local encoder/head backend for the shared marker prompt and typed decision contract.</summary>
public interface IMarkerDecisionPipeline : IDisposable
{
    float[] Score(ReadOnlySpan<int> tokenIds, int typeId, ReadOnlySpan<int> markerPositions,
        CancellationToken cancellationToken = default);
}
