using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sezika.Tooling;

internal static class CorpusJson
{
    internal const int MaximumRowBytes = 4 * 1024 * 1024;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string Sha(string value) => Sha(Utf8.GetBytes(value));
    internal static string FileHash(string path, Budget budget)
    {
        SafePaths.NoLinks(path, budget);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return budget.Hash(file, file.Length);
    }
    internal static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    internal static bool IsSha(string? value) => value is { Length: 64 } && value.All(c => char.IsAsciiHexDigit(c) && !char.IsUpper(c));
    internal static JsonElement Read(string path, Budget budget, int maximum = 64 * 1024 * 1024)
    {
        budget.Check(); SafePaths.NoLinks(path, budget);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length <= maximum, "JSON file byte bound.");
        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 64, AllowDuplicateProperties = false });
        budget.Check(); return json.RootElement.Clone();
    }
    internal static T Read<T>(string path, JsonTypeInfo<T> type, Budget budget) =>
        JsonSerializer.Deserialize(Read(path, budget), type) ?? throw new InvalidDataException("Missing JSON value.");
    internal static void Write<T>(string path, T value, JsonTypeInfo<T> type, Budget budget)
    {
        budget.Check(); SafePaths.NoLinks(path, budget);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, type); stream.WriteByte(10); stream.Flush(true);
    }
    internal static string Canonical(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            Canonical(writer, element, 0);
        return Utf8.GetString(buffer.ToArray());
    }
    private static void Canonical(Utf8JsonWriter writer, JsonElement element, int depth)
    {
        Require(depth <= 64, "Canonical JSON depth.");
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var fields = element.EnumerateObject().Take(4097).OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
                Require(fields.Length <= 4096 && fields.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == fields.Length, "Object field count/duplicate.");
                writer.WriteStartObject();
                foreach (var field in fields) { writer.WritePropertyName(field.Name); Canonical(writer, field.Value, depth + 1); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                Require(element.GetArrayLength() <= 200_000, "Canonical array count.");
                writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Canonical(writer, item, depth + 1); writer.WriteEndArray(); break;
            default: element.WriteTo(writer); break;
        }
    }
    internal static JsonElement Parsed(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String) return element;
        var text = element.GetString() ?? throw new InvalidDataException("Null encoded JSON.");
        Require(text.Length <= MaximumRowBytes, "Encoded JSON bounds.");
        using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone();
    }
    internal static string Text(JsonElement element) => element.ValueKind == JsonValueKind.String ? element.GetString()! : Canonical(element);
    internal static string Str(JsonElement element, string property) => Text(element.GetProperty(property));
    internal static string? Optional(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? Text(value) : null;
    internal static string[] Strings(JsonElement value)
    {
        value = Parsed(value); Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= 4096, "String array bound.");
        return value.EnumerateArray().Select(Text).ToArray();
    }
    internal static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
    internal static double[] Probabilities(JsonElement value) => Probability(Parsed(value).EnumerateArray().Select(Number).ToArray());
    internal static double[] Probability(double[] values)
    {
        Require(values.Length is >= 2 and <= 32 && values.All(v => double.IsFinite(v) && v >= 0) && Math.Abs(values.Sum() - 1) <= 2e-5, "Invalid target distribution.");
        return values;
    }
    internal static double[] OneHot(int count, int index)
    {
        Require(count is >= 2 and <= 32 && index >= 0 && index < count, "Invalid label index.");
        var result = new double[count]; result[index] = 1; return result;
    }
    internal static string NewOutput(string path, Budget budget)
    {
        var result = Path.GetFullPath(path); SafePaths.NoLinks(result, budget);
        Require(!Path.Exists(result), "Preserve existing output; a new version is required.");
        Directory.CreateDirectory(result); return result;
    }
    internal static StreamWriter Writer(string path, bool append = false) => new(new FileStream(path, append ? FileMode.Append : FileMode.CreateNew, FileAccess.Write, FileShare.Read), Utf8);
    internal static void Line<T>(StreamWriter writer, T value, JsonTypeInfo<T> type) => writer.WriteLine(JsonSerializer.Serialize(value, type));
    internal static IEnumerable<JsonElement> Lines(string path, int maximum, Budget budget)
    {
        Require(maximum is >= 1 and <= 50_000_000, "Row maximum."); SafePaths.NoLinks(path, budget);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var gzip = path.EndsWith(".gz", StringComparison.Ordinal) ? new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress, true) : null;
        using var reader = new StreamReader(gzip ?? (Stream)file, Utf8, true);
        for (var row = 0; row < maximum; row++)
        {
            budget.Check(); var line = ReadLine(reader, MaximumRowBytes, budget); if (line is null) yield break;
            using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64, AllowDuplicateProperties = false }); yield return doc.RootElement.Clone();
        }
        Require(reader.Read() == -1, "Source row item bound exceeded.");
    }
    internal static string? ReadLine(TextReader reader, int maximum, Budget budget)
    {
        var buffer = new StringBuilder();
        for (var index = 0; index <= maximum; index++)
        {
            if ((index & 4095) == 0) budget.Check();
            var value = reader.Read(); if (value == -1) { if (buffer.Length == 0) return null; var tail = buffer.ToString(); Require(Utf8.GetByteCount(tail) <= maximum, "Row UTF8 byte bound."); return tail; }
            if (value == 10) { var line = buffer.ToString().TrimEnd('\r'); Require(Utf8.GetByteCount(line) <= maximum, "Row UTF8 byte bound."); return line; } buffer.Append((char)value);
        }
        throw new InvalidDataException("Row character bound exceeded.");
    }
}
