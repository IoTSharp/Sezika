using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika;

public sealed record DecisionResponse
{
    public required string Model { get; init; }
    public required string ModelRevision { get; init; }
    public string? TokenizerRevision { get; init; }
    public required string Backend { get; init; }
    public required Dictionary<string, Answer> Answers { get; init; }
    public DecisionUsage? Usage { get; init; }
}

public sealed record DecisionUsage
{
    public required int QuestionCount { get; init; }
    public required int TokenCount { get; init; }
    public required int MicroBatchCount { get; init; }
    /// <summary>Peak transient bytes reserved by the request workspace.</summary>
    public long WorkspaceBytes { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
[JsonDerivedType(typeof(BooleanAnswer), "boolean")]
public abstract record Answer
{
    /// <summary>Either answered or abstained. Execution failures use errors.</summary>
    public required string Status { get; init; }
    public string? AbstentionReason { get; init; }
    public required CalibrationInfo Calibration { get; init; }
}

public sealed record ChoiceAnswer : Answer
{
    /// <summary>Distribution argmax, including when abstained; not an accepted action.</summary>
    public required string Choice { get; init; }
    public required Dictionary<string, double> Probabilities { get; init; }

    /// <summary>Raw marker logits keyed by the same candidate IDs as <see cref="Probabilities"/>.</summary>
    public Dictionary<string, double>? Logits { get; init; }

    /// <summary>Normalized entropy concentration, not probability of correctness.</summary>
    public required double Concentration { get; init; }
}

public sealed record ScoreAnswer : Answer
{
    /// <summary>Expected zero-based level index, in the range [0, level count - 1].</summary>
    public required double Score { get; init; }
    public required Dictionary<string, JsonElement> Legend { get; init; }
    public required Dictionary<string, double> Probabilities { get; init; }

    /// <summary>Raw marker logits keyed by the zero-based legend IDs.</summary>
    public Dictionary<string, double>? Logits { get; init; }

    /// <summary>Normalized entropy concentration, not probability of correctness.</summary>
    public required double Concentration { get; init; }
}

public sealed record BooleanAnswer : Answer
{
    public required double ProbabilityTrue { get; init; }

    /// <summary>Raw logits keyed by true/false for reference alignment.</summary>
    public Dictionary<string, double>? Logits { get; init; }
}

public sealed record CalibrationInfo
{
    /// <summary>uncalibrated, calibrated or out_of_scope.</summary>
    public required string Status { get; init; }
    public string? ProfileId { get; init; }
    public string? Scope { get; init; }
}
