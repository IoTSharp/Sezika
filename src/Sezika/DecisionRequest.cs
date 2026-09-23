using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika;

/// <summary>Draft input contract. A runtime validator must enforce semantic limits.</summary>
public sealed record DecisionRequest
{
    public required string Model { get; init; }
    public required JsonElement State { get; init; }
    public required Dictionary<string, Question> Questions { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ChoiceQuestion), "choice")]
[JsonDerivedType(typeof(ScoreQuestion), "score")]
[JsonDerivedType(typeof(BooleanQuestion), "boolean")]
public abstract record Question
{
    public required JsonElement Instructions { get; init; }
}

public sealed record ChoiceQuestion : Question
{
    public required Dictionary<string, JsonElement> Criteria { get; init; }
}

public sealed record ScoreQuestion : Question
{
    public required JsonElement[] Criteria { get; init; }
}

public sealed record BooleanQuestion : Question
{
    public BooleanCriteria? Criteria { get; init; }
}

public sealed record BooleanCriteria
{
    [JsonPropertyName("true")]
    public required JsonElement WhenTrue { get; init; }

    [JsonPropertyName("false")]
    public required JsonElement WhenFalse { get; init; }
}
