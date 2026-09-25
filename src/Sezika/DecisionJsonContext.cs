using System.Text.Json.Serialization;

namespace Sezika;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DecisionRequest))]
[JsonSerializable(typeof(DecisionResponse))]
[JsonSerializable(typeof(DecisionUsage))]
[JsonSerializable(typeof(PromptSequence))]
[JsonSerializable(typeof(PromptSequenceOptions))]
[JsonSerializable(typeof(PromptSequenceDiagnostics))]
[JsonSerializable(typeof(PromptInputDiagnostics))]
[JsonSerializable(typeof(PromptLengthPolicy))]
[JsonSerializable(typeof(ModelManifest))]
[JsonSerializable(typeof(TensorManifest))]
[JsonSerializable(typeof(ModelAssetFile))]
[JsonSerializable(typeof(ModelDownloadFile))]
[JsonSerializable(typeof(ModelPackageSpec))]
[JsonSerializable(typeof(InstalledModelRecord))]
[JsonSerializable(typeof(TokenizerSpec))]
[JsonSerializable(typeof(TransformerConfig))]
[JsonSerializable(typeof(Question))]
[JsonSerializable(typeof(ChoiceQuestion))]
[JsonSerializable(typeof(ScoreQuestion))]
[JsonSerializable(typeof(BooleanQuestion))]
[JsonSerializable(typeof(BooleanCriteria))]
[JsonSerializable(typeof(BooleanLabels))]
[JsonSerializable(typeof(Answer))]
[JsonSerializable(typeof(ChoiceAnswer))]
[JsonSerializable(typeof(ScoreAnswer))]
[JsonSerializable(typeof(BooleanAnswer))]
[JsonSerializable(typeof(CalibrationGates))]
[JsonSerializable(typeof(CalibrationMetricSnapshot))]
[JsonSerializable(typeof(CalibrationProfileEntry))]
[JsonSerializable(typeof(CalibrationProfileManifest))]
[JsonSerializable(typeof(CalibrationProfileBinding))]
[JsonSerializable(typeof(PrimitiveReferenceFixture))]
[JsonSerializable(typeof(PrimitiveReferenceCase))]
[JsonSerializable(typeof(PrimitiveReferenceInput))]
[JsonSerializable(typeof(PrimitiveAlignmentReport))]
public partial class DecisionJsonContext : JsonSerializerContext
{
}
