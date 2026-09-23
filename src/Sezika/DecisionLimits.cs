namespace Sezika;

/// <summary>Hard limits applied before model memory is allocated.</summary>
public sealed record DecisionLimits
{
    public static DecisionLimits Default { get; } = new();

    public int MaxRequestBytes { get; init; } = 1 * 1024 * 1024;
    public int MaxQuestions { get; init; } = 32;
    public int MaxCandidates { get; init; } = 32;
    public int MaxScoreCriteria { get; init; } = 10;
    public int MaxTokensPerQuestion { get; init; } = 1024;
    public int MaxJsonDepth { get; init; } = 32;
    public int MaxStateBytes { get; init; } = 256 * 1024;
    public int MaxInstructionBytes { get; init; } = 64 * 1024;
    public int MaxIdentifierLength { get; init; } = 128;

    public void Validate()
    {
        if (MaxRequestBytes <= 0 || MaxQuestions <= 0 || MaxCandidates < 2 || MaxScoreCriteria < 2 ||
            MaxTokensPerQuestion <= 0 || MaxJsonDepth <= 0 || MaxStateBytes <= 0 ||
            MaxInstructionBytes <= 0 || MaxIdentifierLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DecisionLimits), "Decision limits must be positive and candidates must be at least two.");
        }
    }
}
