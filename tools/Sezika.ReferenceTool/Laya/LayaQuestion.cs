using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.ReferenceTool.Laya;

// Port of agent._check_question/_to_internal and common.render_options at 4066d5d5, Apache-2.0.
internal sealed class InvalidQuestionException(string message) : Exception(message);
internal sealed record LayaQuestion(string Type, int TypeIndex, string Instructions, string[] Labels,
    string[] Options, string FalseDisplay, string TrueDisplay)
{
    internal static LayaQuestion Convert(JsonElement value, Budget budget)
    {
        budget.Check(); if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String)
            throw new InvalidQuestionException("Question requires an explicit type.");
        var type = typeValue.GetString()!; var index = type switch { "choice" => 0, "score" => 1, "noul" => 2, _ => throw new InvalidQuestionException("Unknown question type.") };
        if (!value.TryGetProperty("instructions", out var instruction)) throw new InvalidQuestionException("Question has no instructions.");
        var text = instruction.ValueKind == JsonValueKind.String ? instruction.GetString()! : PythonJson.Render(instruction, budget);
        if (text.Length > 131072) throw new InvalidQuestionException("Instruction character bound131072.");
        value.TryGetProperty("criteria", out var criteria); var labels = new List<string>(); var options = new List<string>();
        if (type == "choice")
        {
            var count = 0; var keys = new HashSet<string>(StringComparer.Ordinal);
            if (criteria.ValueKind == JsonValueKind.Object)
                foreach (var item in criteria.EnumerateObject())
                {
                    budget.Check(); if (++count > 64) throw new InvalidQuestionException("Candidate bound64.");
                    labels.Add(item.Name); var description = item.Value.ValueKind == JsonValueKind.Null || item.Value.ValueKind == JsonValueKind.String && item.Value.GetString() == "" ? "" : PythonJson.Criterion(item.Value, budget);
                    options.Add(item.Name + (description.Length == 0 ? "" : ": " + description));
                }
            else if (criteria.ValueKind == JsonValueKind.Array)
                foreach (var item in criteria.EnumerateArray())
                {
                    budget.Check(); if (++count > 64) throw new InvalidQuestionException("Candidate bound64.");
                    if (item.ValueKind is JsonValueKind.Array or JsonValueKind.Object) throw new InvalidQuestionException("Choice labels must be scalar.");
                    var key = item.ValueKind switch
                    {
                        JsonValueKind.Number => "number:" + item.GetDouble().ToString("R", CultureInfo.InvariantCulture),
                        JsonValueKind.True => "number:1", JsonValueKind.False => "number:0",
                        _ => item.ValueKind + ":" + item.GetRawText()
                    };
                    if (!keys.Add(key)) continue; var label = PythonJson.Scalar(item); labels.Add(label); options.Add(label);
                }
            else throw new InvalidQuestionException("Choice criteria must be object or array.");
            if (labels.Count == 0) throw new InvalidQuestionException("Choice needs at least one criterion.");
        }
        else if (type == "score")
        {
            if (criteria.ValueKind != JsonValueKind.Array || criteria.GetArrayLength() is < 1 or > 64) throw new InvalidQuestionException("Score needs1..64 ordered levels.");
            var level = 0;
            foreach (var item in criteria.EnumerateArray())
            {
                budget.Check(); if (item.ValueKind == JsonValueKind.Null) throw new InvalidQuestionException("Score level is null.");
                var label = (level++).ToString(CultureInfo.InvariantCulture); labels.Add(label); options.Add("level " + label + ": " + PythonJson.Criterion(item, budget));
            }
        }
        else
        {
            if (criteria.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object)) throw new InvalidQuestionException("Noul criteria must be object or omitted.");
            var descriptions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (criteria.ValueKind == JsonValueKind.Object)
                foreach (var item in criteria.EnumerateObject())
                {
                    budget.Check(); var key = item.Name.ToLowerInvariant(); if (key is not ("false" or "true")) throw new InvalidQuestionException("Noul criterion keys must be false/true."); descriptions[key] = item.Value;
                }
            foreach (var label in new[] { "false", "true" })
            {
                var description = descriptions.TryGetValue(label, out var item) && item.ValueKind != JsonValueKind.Null && !(item.ValueKind == JsonValueKind.String && item.GetString() == "") ? PythonJson.Criterion(item, budget) : "";
                labels.Add(label); options.Add(label + ": " + (description.Length == 0 ? label == "false" ? "no, the statement does not hold" : "yes, the statement holds" : description));
            }
        }
        var display = new[] { "false", "true" };
        if (value.TryGetProperty("labels", out var supplied) && supplied.ValueKind != JsonValueKind.Null)
        {
            if (type != "noul") throw new InvalidQuestionException("Display labels require noul.");
            if (supplied.ValueKind == JsonValueKind.Object)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in supplied.EnumerateObject()) { budget.Check(); if (item.Value.ValueKind != JsonValueKind.String) throw new InvalidQuestionException("Noul display values must be strings."); values[item.Name] = item.Value.GetString()!.Trim(); }
                if (values.Count != 2 || !values.TryGetValue("false", out var falseLabel) || !values.TryGetValue("true", out var trueLabel)) throw new InvalidQuestionException("Noul display labels require exactly false/true.");
                display = [falseLabel, trueLabel];
            }
            else throw new InvalidQuestionException("Noul display labels require two labels.");
            if (display[0].Length == 0 || display[1].Length == 0 || display[0] == display[1]) throw new InvalidQuestionException("Noul display labels must be distinct nonempty strings.");
        }
        if (type == "noul") { options[0] = display[0] + options[0][5..]; options[1] = display[1] + options[1][4..]; }
        return new(type, index, text, labels.ToArray(), options.ToArray(), display[0], display[1]);
    }
}
internal static class PythonJson
{
    internal static bool Truthy(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.String => value.GetString()!.Length != 0, JsonValueKind.Number => value.GetDouble() != 0,
        JsonValueKind.Array => value.GetArrayLength() != 0, JsonValueKind.Object => value.EnumerateObject().MoveNext(), _ => true
    };
    internal static string Criterion(JsonElement value, Budget budget) => value.ValueKind == JsonValueKind.String ? value.GetString()! :
        value.ValueKind == JsonValueKind.Undefined ? "" : Render(value, budget);
    internal static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!, JsonValueKind.True => "True", JsonValueKind.False => "False",
        JsonValueKind.Null or JsonValueKind.Undefined => "None", JsonValueKind.Number => Number(value),
        _ => throw new InvalidQuestionException("Scalar value expected.")
    };
    private static string Number(JsonElement value)
    {
        var raw = value.GetRawText(); if (raw.Length > 128) throw new InvalidDataException("Number text bound128.");
        if (!raw.Contains('.') && !raw.Contains('e') && !raw.Contains('E')) return System.Numerics.BigInteger.Parse(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        var number = value.GetDouble(); if (!double.IsFinite(number)) throw new InvalidDataException("Non-finite input number.");
        if (number == 0) return BitConverter.DoubleToInt64Bits(number) < 0 ? "-0.0" : "0.0";
        var text = number.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant(); var scientific = Math.Abs(number) < 1e-4 || Math.Abs(number) >= 1e16;
        if (!text.Contains('e') && scientific)
        {
            var negative = text[0] == '-'; var digits = negative ? text[1..] : text; var dot = digits.IndexOf('.'); if (dot < 0) dot = digits.Length;
            digits = digits.Replace(".", "", StringComparison.Ordinal); var first = 0; for (; first < digits.Length && digits[first] == '0'; first++) { }
            var exponent = dot - first - 1; digits = digits[first..].TrimEnd('0');
            text = (negative ? "-" : "") + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
        }
        return text.Contains('e') || text.Contains('.') ? text : text + ".0";
    }
    internal static string Render(JsonElement value, Budget budget)
    {
        var output = new StringBuilder(); var nodes = 0;
        void Write(JsonElement item, int depth)
        {
            budget.Check(); if (++nodes > 16384 || depth > 16 || output.Length > 131072) throw new InvalidDataException("Structured input depth/node/character bound.");
            switch (item.ValueKind)
            {
                case JsonValueKind.Object:
                    output.Append('{'); var properties = 0;
                    foreach (var property in item.EnumerateObject()) { if (++properties > 1024) throw new InvalidDataException("Object property bound1024."); if (properties > 1) output.Append(", "); output.Append(JsonSerializer.Serialize(property.Name, PythonStringJson.Unicode.String)); output.Append(": "); Write(property.Value, depth + 1); }
                    output.Append('}'); break;
                case JsonValueKind.Array:
                    if (item.GetArrayLength() > 1024) throw new InvalidDataException("Array item bound1024."); output.Append('['); var entries = 0;
                    foreach (var child in item.EnumerateArray()) { if (entries++ > 0) output.Append(", "); Write(child, depth + 1); } output.Append(']'); break;
                case JsonValueKind.String: output.Append(JsonSerializer.Serialize(item.GetString(), PythonStringJson.Unicode.String)); break;
                case JsonValueKind.Number: output.Append(Number(item)); break;
                case JsonValueKind.True: output.Append("true"); break;
                case JsonValueKind.False: output.Append("false"); break;
                case JsonValueKind.Null: output.Append("null"); break;
                default: throw new InvalidDataException("Undefined JSON input.");
            }
        }
        Write(value, 0); if (output.Length > 131072) throw new InvalidDataException("Structured text bound131072."); return output.ToString();
    }
}
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(string))]
internal partial class PythonStringJson : JsonSerializerContext
{
    internal static readonly PythonStringJson Unicode = new(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
