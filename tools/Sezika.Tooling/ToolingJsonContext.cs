using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record CommandFailure(string Status, string ErrorType, string Message, bool PartialOutputsPreserved = true);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true, MaxDepth = 32)]
[JsonSerializable(typeof(LegacyReceipt))]
[JsonSerializable(typeof(AuditReport))]
[JsonSerializable(typeof(ArchiveReport))]
[JsonSerializable(typeof(CommandFailure))]
internal partial class ToolingJsonContext : JsonSerializerContext;
