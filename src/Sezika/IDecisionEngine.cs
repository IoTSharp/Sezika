namespace Sezika;

/// <summary>
/// Draft contract for bounded inference over a loaded decision model.
/// This repository does not yet contain an implementation.
/// </summary>
public interface IDecisionEngine
{
    DecisionResponse Evaluate(
        DecisionRequest request,
        CancellationToken cancellationToken = default);
}
