using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika;

internal static class EvaluationInputs
{
    public const int MaxDatasetBytes = 64 * 1024 * 1024;
    public const string RenderingVersion = "sezika.prompt.laya-4066d5d5.v2";

    public static string Language(JsonElement row)
    {
        if (!row.TryGetProperty("language", out var language)) return "unspecified";
        if (language.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(language.GetString()) || language.GetString()!.Length > 80)
            throw new InvalidDataException("Invalid explicit language metadata.");
        return language.GetString()!;
    }

    public static DecisionRequest Request(JsonElement input, string modelId, PromptLengthPolicy policy)
    {
        var questions = input.GetProperty("questions");
        if (questions.ValueKind != JsonValueKind.Object || questions.EnumerateObject().Count() != 1 ||
            !questions.TryGetProperty("decision", out var question))
            throw new InvalidDataException("Evaluation requires exactly one question named decision.");
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            writer.WriteString("model", modelId);
            writer.WriteString("length_policy", policy == PromptLengthPolicy.Strict ? "strict" : "laya_compatible");
            writer.WritePropertyName("state");
            input.GetProperty("state").WriteTo(writer);
            writer.WriteStartObject("questions");
            writer.WriteStartObject("decision");
            var kind = question.GetProperty("type").GetString();
            writer.WriteString("type", kind == "noul" ? "boolean" : kind);
            foreach (var property in question.EnumerateObject())
            {
                if (property.Name != "type") property.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return DecisionRequestParser.Parse(bytes.ToArray());
    }

    public static JsonDocument ParseRow(string line, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(line) || Encoding.UTF8.GetByteCount(line) > 1_048_576)
            throw new InvalidDataException("Blank or oversized evaluation row.");
        return ParseJson(Encoding.UTF8.GetBytes(line), 65536, 32, token);
    }

    public static JsonDocument ParseJson(byte[] bytes, int maxNodes, int maxDepth, CancellationToken token)
    {
        if (bytes.Length > MaxDatasetBytes) throw new InvalidDataException("JSON input exceeds 64 MiB.");
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = maxDepth });
        var scopes = new Stack<HashSet<string>>();
        for (var nodes = 0; reader.Read(); nodes++)
        {
            token.ThrowIfCancellationRequested();
            if (nodes >= maxNodes) throw new InvalidDataException("JSON node count exceeds its bound.");
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject: scopes.Push(new(StringComparer.Ordinal)); break;
                case JsonTokenType.EndObject: scopes.Pop(); break;
                case JsonTokenType.PropertyName:
                    if (!scopes.Peek().Add(reader.GetString()!)) throw new InvalidDataException("Duplicate evaluation JSON property.");
                    break;
            }
        }
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = maxDepth });
    }

    public static string TextHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static Dictionary<string, string> CodeHashes(CancellationToken token)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var executable = Environment.ProcessPath ?? throw new InvalidDataException("Process image path is unavailable.");
        result.Add(Path.GetFileName(executable), FileHash(executable, token));
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not null)
        {
            foreach (var name in new[] { "Sezika.Evaluation.dll", "Sezika.dll", "Sezika.Cuda.dll" })
                result.Add(name, FileHash(Path.Combine(AppContext.BaseDirectory, name), token));
        }
        return result;
    }

    public static string FileHash(string path, CancellationToken token)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxDatasetBytes) throw new InvalidDataException("Evaluation input exceeds 64 MiB.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        for (var chunk = 0; chunk <= MaxDatasetBytes / buffer.Length; chunk++)
        {
            token.ThrowIfCancellationRequested();
            var count = stream.Read(buffer);
            if (count == 0) return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            hash.AppendData(buffer, 0, count);
        }
        throw new InvalidDataException("Evaluation input changed or exceeded its read bound.");
    }

    public static string TokenHash(ReadOnlySpan<int> tokens)
    {
        if (tokens.Length > 1024) throw new InvalidDataException("Observed sequence exceeds 1024 tokens.");
        Span<byte> bytes = stackalloc byte[tokens.Length * sizeof(int)];
        for (var index = 0; index < tokens.Length; index++) BinaryPrimitives.WriteInt32LittleEndian(bytes[(index * 4)..], tokens[index]);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

internal sealed class EvaluationPipeline(IMarkerDecisionPipeline inner) : IMarkerDecisionPipeline
{
    public int Calls { get; private set; }
    public string? TokenIdsSha256 { get; private set; }
    public int[]? MarkerPositions { get; private set; }
    public float[]? RawLogits { get; private set; }

    public void Begin() { Calls = 0; TokenIdsSha256 = null; MarkerPositions = null; RawLogits = null; }

    public float[] Score(ReadOnlySpan<int> tokenIds, int typeId, ReadOnlySpan<int> markerPositions, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++Calls != 1) throw new InvalidDataException("Evaluation must execute one forward per row.");
        TokenIdsSha256 = EvaluationInputs.TokenHash(tokenIds);
        MarkerPositions = markerPositions.ToArray();
        var logits = inner.Score(tokenIds, typeId, markerPositions, cancellationToken);
        if (logits.Length != markerPositions.Length || logits.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Nonfinite or incorrectly shaped marker output.");
        RawLogits = (float[])logits.Clone();
        return logits;
    }

    public void Dispose() => inner.Dispose();
}
