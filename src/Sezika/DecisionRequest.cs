using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika;

/// <summary>Draft input contract. A runtime validator must enforce semantic limits.</summary>
public sealed record DecisionRequest
{
    public required string Model { get; init; }
    public required JsonElement State { get; init; }
    public required Dictionary<string, Question> Questions { get; init; }
    /// <summary>Strict rejects token loss; compatibility truncation must be explicitly requested.</summary>
    public PromptLengthPolicy LengthPolicy { get; init; } = PromptLengthPolicy.Strict;
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
    public BooleanLabels? Labels { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BooleanCriteria
{
    [JsonPropertyName("true")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement WhenTrue { get; init; }

    [JsonPropertyName("false")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement WhenFalse { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BooleanLabels
{
    [JsonPropertyName("false")]
    public required string WhenFalse { get; init; }

    [JsonPropertyName("true")]
    public required string WhenTrue { get; init; }
}
