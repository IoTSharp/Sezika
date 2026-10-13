using System.Text.Json;
using static Sezika.Tooling.CorpusJson;

namespace Sezika.Tooling;

internal static class ValidationJson
{
    internal static string Mutate(JsonElement source, string[] path, JsonElement replacement, Budget budget)
    {
        Require(path.Length is >= 1 and <= 64, "Mutation path depth."); using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory)) Copy(writer, source, 0);
        return Utf8.GetString(memory.ToArray());
        void Copy(Utf8JsonWriter writer, JsonElement value, int depth)
        {
            budget.Check(); if (depth == path.Length) { replacement.WriteTo(writer); return; }
            var found = false;
            if (value.ValueKind == JsonValueKind.Object)
            {
                var properties = value.EnumerateObject().Take(4097).ToArray(); Require(properties.Length <= 4096, "Mutation object count."); writer.WriteStartObject();
                foreach (var property in properties) { writer.WritePropertyName(property.Name); if (property.Name == path[depth]) { found = true; Copy(writer, property.Value, depth + 1); } else property.Value.WriteTo(writer); } writer.WriteEndObject();
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                var array = Array(value, 131_072); Require(int.TryParse(path[depth], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) && index >= 0 && index < array.Length, "Mutation array index."); writer.WriteStartArray();
                for (var item = 0; item < array.Length; item++) { budget.Check(); if (item == index) { found = true; Copy(writer, array[item], depth + 1); } else array[item].WriteTo(writer); } writer.WriteEndArray();
            }
            else throw new InvalidDataException("Mutation traversed a scalar.");
            Require(found, "Mutation target property missing.");
        }
    }
    internal static string Text(JsonElement value)
    { Require(value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString()!.Length <= 4 * 1024 * 1024, "Missing or invalid JSON string."); return value.GetString()!; }
    internal static string Text(JsonElement value, string name) => Text(value.GetProperty(name));
    internal static double Number(JsonElement value, string name = "number")
    { Require(value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= 0, "Invalid nonnegative number: " + name); return value.GetDouble(); }
    internal static long Integer(JsonElement value, long minimum, long maximum, string name = "integer")
    { var number = Number(value, name); Require(number == Math.Truncate(number) && number >= minimum && number <= maximum, "Invalid bounded integer: " + name); return checked((long)number); }
    internal static bool Boolean(JsonElement value) { Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False, "Expected JSON boolean."); return value.GetBoolean(); }
    internal static string Hash(JsonElement value) { var hash = Text(value); Require(IsSha(hash), "Invalid lowercase SHA256."); return hash; }
    internal static JsonElement[] Array(JsonElement value, int maximum, int minimum = 0)
    { Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() >= minimum && value.GetArrayLength() <= maximum, "JSON array item bound."); return value.EnumerateArray().ToArray(); }
    internal static bool Null(JsonElement value, string name) => !value.TryGetProperty(name, out var member) || member.ValueKind == JsonValueKind.Null;
    internal static void Near(JsonElement actual, double expected, string name)
    { var value = Number(actual, name); Require(double.IsFinite(expected) && Math.Abs(value - expected) <= 1e-12 * Math.Max(1, Math.Abs(expected)), "Derived number mismatch: " + name); }
    internal static double Percentile(double[] values, double quantile)
    { Require(values.Length is >= 1 and <= 30, "Percentile sample count."); var sorted = values.Order().ToArray(); return sorted[(int)Math.Ceiling(quantile * values.Length) - 1]; }
    internal static void Properties(JsonElement value, string[] allowed)
    { Require(value.ValueKind == JsonValueKind.Object, "Expected JSON object."); var properties = value.EnumerateObject().Take(4097).ToArray(); Require(properties.Length <= 4096 && properties.All(p => allowed.Contains(p.Name, StringComparer.Ordinal)), "Unknown JSON property."); }
}
