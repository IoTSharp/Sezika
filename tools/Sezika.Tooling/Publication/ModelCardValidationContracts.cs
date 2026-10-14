using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record ModelCardValidationRequest(
    int SchemaVersion,
    string CardPath,
    long ExpectedBytes,
    string ExpectedSha256,
    string RepositoryType = "model",
    int TimeoutSeconds = 50,
    string ServiceRoot = "https://huggingface.co/");

// No provider message, card text or credential is included in the durable result.
internal sealed record ModelCardValidationReport(
    string Status,
    string Outcome,
    long CardBytes,
    string CardSha256,
    int Requests,
    int? HttpStatusCode,
    int? Warnings,
    int? Errors,
    string? ResponseSha256,
    bool InputUnchanged,
    bool FixtureOnly,
    string ValidationScope = "MODEL_CARD_YAML_ONLY",
    bool ProviderMessagesIncluded = false);

internal sealed record ModelCardValidationBody(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("repoType")] string RepositoryType);

[JsonSourceGenerationOptions(WriteIndented = false, RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ModelCardValidationRequest))]
[JsonSerializable(typeof(ModelCardValidationReport))]
[JsonSerializable(typeof(ModelCardValidationBody))]
internal partial class ModelCardValidationJsonContext : JsonSerializerContext;
