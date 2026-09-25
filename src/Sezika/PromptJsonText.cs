using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sezika;

/// <summary>JSON text matching Python json.dumps(..., ensure_ascii=False) for JSON input values.</summary>
internal static class PromptJsonText
{
    internal static string Render(JsonElement value, int maxCharacters, CancellationToken cancellationToken)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? string.Empty;
            if (text.Length > maxCharacters) Limit();
            cancellationToken.ThrowIfCancellationRequested();
            return text;
        }
        var builder = new StringBuilder();
        Write(value, builder, maxCharacters, 0, cancellationToken);
        return builder.ToString();
    }

    private static void Write(JsonElement value, StringBuilder builder, int limit, int depth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > 32) throw new DecisionException("decision_input_depth_exceeded", "Prompt JSON exceeds 32 levels.");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var firstProperty = true;
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!names.Add(property.Name))
                        throw new DecisionException("decision_duplicate_property", "Prompt JSON contains duplicate property names.");
                    if (!firstProperty) builder.Append(", ");
                    firstProperty = false;
                    WriteString(property.Name, builder, limit, cancellationToken);
                    builder.Append(": ");
                    Write(property.Value, builder, limit, depth + 1, cancellationToken);
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in value.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!firstItem) builder.Append(", ");
                    firstItem = false;
                    Write(item, builder, limit, depth + 1, cancellationToken);
                }
                builder.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(value.GetString() ?? string.Empty, builder, limit, cancellationToken);
                break;
            case JsonValueKind.Number:
                builder.Append(RenderNumber(value));
                break;
            case JsonValueKind.True: builder.Append("true"); break;
            case JsonValueKind.False: builder.Append("false"); break;
            case JsonValueKind.Null: builder.Append("null"); break;
            default: throw new DecisionException("decision_prompt_value_invalid", "Undefined JSON cannot be rendered as prompt text.");
        }
        if (builder.Length > limit) Limit();
    }

    private static void WriteString(string text, StringBuilder builder, int limit, CancellationToken cancellationToken)
    {
        if (text.Length > limit) Limit();
        builder.Append('"');
        foreach (var value in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (value)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (value < 0x20) builder.Append("\\u").Append(((int)value).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(value);
                    break;
            }
            if (builder.Length > limit) Limit();
        }
        builder.Append('"');
    }

    private static string RenderNumber(JsonElement value)
    {
        var raw = value.GetRawText();
        // json.loads preserves arbitrary-precision integer tokens; do not round
        // them through double, decimal, or Int64 before rendering.
        if (raw.IndexOfAny(['.', 'e', 'E']) < 0) return raw == "-0" ? "0" : raw;
        var number = value.GetDouble();
        if (double.IsPositiveInfinity(number)) return "Infinity";
        if (double.IsNegativeInfinity(number)) return "-Infinity";
        if (number == 0) return double.IsNegative(number) ? "-0.0" : "0.0";
        var negative = number < 0;
        var shortest = Math.Abs(number).ToString("R", CultureInfo.InvariantCulture);
        var exponentOffset = shortest.IndexOf('E');
        var explicitExponent = exponentOffset < 0 ? 0 : int.Parse(shortest.AsSpan(exponentOffset + 1), CultureInfo.InvariantCulture);
        var mantissa = exponentOffset < 0 ? shortest : shortest[..exponentOffset];
        var point = mantissa.IndexOf('.');
        if (point < 0) point = mantissa.Length;
        var digits = mantissa.Replace(".", string.Empty, StringComparison.Ordinal);
        var leading = 0;
        while (leading < digits.Length - 1 && digits[leading] == '0') leading++;
        var exponent = explicitExponent + point - leading - 1;
        digits = digits[leading..].TrimEnd('0');
        var sign = negative ? "-" : string.Empty;
        if (exponent < -4 || exponent >= 16)
        {
            var fraction = digits.Length > 1 ? "." + digits[1..] : string.Empty;
            return sign + digits[0] + fraction + "e" + (exponent < 0 ? "-" : "+") +
                Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
        }
        var decimalPoint = exponent + 1;
        if (decimalPoint <= 0) return sign + "0." + new string('0', -decimalPoint) + digits;
        if (decimalPoint >= digits.Length) return sign + digits + new string('0', decimalPoint - digits.Length) + ".0";
        return sign + digits[..decimalPoint] + "." + digits[decimalPoint..];
    }

    private static void Limit() => throw new DecisionException("decision_input_limit_exceeded", "Rendered prompt exceeds its character limit.");
}
