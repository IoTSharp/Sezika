using System.Text.Json.Serialization;

namespace Sezika;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DecisionRequest))]
[JsonSerializable(typeof(DecisionResponse))]
[JsonSerializable(typeof(Question))]
[JsonSerializable(typeof(ChoiceQuestion))]
[JsonSerializable(typeof(ScoreQuestion))]
[JsonSerializable(typeof(BooleanQuestion))]
[JsonSerializable(typeof(Answer))]
[JsonSerializable(typeof(ChoiceAnswer))]
[JsonSerializable(typeof(ScoreAnswer))]
[JsonSerializable(typeof(BooleanAnswer))]
public partial class DecisionJsonContext : JsonSerializerContext
{
}
