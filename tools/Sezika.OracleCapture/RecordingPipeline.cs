namespace Sezika.OracleCapture;

/// <summary>Records the actual backend call, and asserts that the public builder describes the engine call.</summary>
internal sealed class RecordingPipeline(IMarkerDecisionPipeline inner) : IMarkerDecisionPipeline
{
    private PromptSequence? _expected;
    public int Calls { get; private set; }
    public float[]? Logits { get; private set; }

    public void Begin(PromptSequence sequence)
    {
        _expected = sequence;
        Calls = 0;
        Logits = null;
    }

    public float[] Score(ReadOnlySpan<int> tokenIds, int typeId, ReadOnlySpan<int> markerPositions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_expected is null || ++Calls != 1 || typeId != _expected.TypeId ||
            !tokenIds.SequenceEqual(_expected.TokenIds) || !markerPositions.SequenceEqual(_expected.MarkerPositions))
            throw new InvalidDataException("The typed engine's actual backend input differs from PromptSequenceBuilder.");
        var logits = inner.Score(tokenIds, typeId, markerPositions, cancellationToken);
        Logits = (float[])logits.Clone();
        return logits;
    }

    public void Dispose() => inner.Dispose();
}
