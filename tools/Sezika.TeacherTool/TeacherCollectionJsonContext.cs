using System.Text.Json.Serialization;

namespace Sezika.TeacherTool;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(TeacherChatRequest))]
[JsonSerializable(typeof(TeacherCollectedRecord))]
[JsonSerializable(typeof(TeacherCollectionReport))]
[JsonSerializable(typeof(TeacherFailureAudit))]
internal sealed partial class TeacherCollectionJsonContext : JsonSerializerContext;
