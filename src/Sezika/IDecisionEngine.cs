namespace Sezika;

/// <summary>Bounded inference over a verified, loaded decision model.</summary>
public interface IDecisionEngine
{
    DecisionResponse Evaluate(
        DecisionRequest request,
        CancellationToken cancellationToken = default);
}
