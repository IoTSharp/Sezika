using System.Globalization;
using System.Text.Json;
using Sezika.ReferenceTool.Tokenization;

namespace Sezika.ReferenceTool.Laya;

internal static class LayaCases
{
    internal static JsonElement Question(LayaCaseSpec spec, LayaManifest manifest, Budget budget)
    {
        if (spec.Question.ValueKind != JsonValueKind.Undefined) return spec.Question;
        if (spec.QuestionRef is not null) return manifest.QuestionTemplates.TryGetValue(spec.QuestionRef, out var value) ? value : throw new InvalidDataException("Unknown question template.");
        var recipe = spec.QuestionRecipe; if (recipe.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Question/input recipe missing.");
        var count = recipe.GetProperty("count").GetInt32(); var repetitions = recipe.TryGetProperty("description_repeat", out var d) ? d.GetInt32() : 1;
        var instructions = recipe.TryGetProperty("instruction_repeat", out var ins) ? ins.GetInt32() : 1; var type = recipe.GetProperty("type").GetString();
        if (count is < 0 or > 64 || repetitions is < 1 or > 512 || instructions is < 1 or > 512 || type is not ("choice" or "score" or "noul")) throw new InvalidDataException("Question recipe bounds differ.");
        using var memory = new MemoryStream(); using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject(); writer.WriteString("type", type); writer.WriteString("instructions", Repeat(" Evaluate the record.", instructions, budget)); writer.WritePropertyName("criteria");
            if (type == "choice") writer.WriteStartObject(); else writer.WriteStartArray();
            for (var index = 0; index < count; index++) { budget.Check(); if (type == "choice") writer.WritePropertyName("label_" + index.ToString("D2", CultureInfo.InvariantCulture)); writer.WriteStringValue(Repeat(" evidence", repetitions, budget)); }
            if (type == "choice") writer.WriteEndObject(); else writer.WriteEndArray(); writer.WriteEndObject();
        }
        return Parse(memory.ToArray());
    }
    internal static JsonElement State(LayaCaseSpec spec, LayaManifest manifest, JsonElement question, FrozenTokenizer tokenizer, Budget budget)
    {
        var state = spec.StateRef is not null ? manifest.StateTemplates.TryGetValue(spec.StateRef, out var value) ? value : throw new InvalidDataException("Unknown state template.") : spec.State;
        if (state.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("State missing.");
        if (state.ValueKind != JsonValueKind.Object || !state.TryGetProperty("recipe", out var recipe)) return state;
        if (recipe.GetString() == "conversation_repeat")
        {
            var count = state.GetProperty("count").GetInt32(); if (count is < 1 or > 512) throw new InvalidDataException("Conversation recipe bound512.");
            using var memory = new MemoryStream(); using (var writer = new Utf8JsonWriter(memory))
            {
                writer.WriteStartArray(); for (var turn = 0; turn <= count; turn++) { budget.Check(); writer.WriteStartObject(); writer.WriteString("role", "user"); writer.WriteString("content", state.GetProperty(turn == count ? "latest" : "old").GetString()); writer.WriteEndObject(); } writer.WriteEndArray();
            }
            return Parse(memory.ToArray());
        }
        if (recipe.GetString() != "target_total_tokens") throw new InvalidDataException("Unknown state recipe.");
        var target = state.GetProperty("target").GetInt32(); if (target is < 1 or > 2048) throw new InvalidDataException("Exact-token target bound2048.");
        var internalQuestion = LayaQuestion.Convert(question, budget); var lower = 0; var upper = 4096;
        for (var probe = 0; probe < 32 && lower <= upper; probe++)
        {
            budget.Check(); var middle = lower + (upper - lower) / 2; var candidate = state.GetProperty("prefix").GetString() + Repeat(state.GetProperty("repeat_text").GetString()!, middle, budget) + state.GetProperty("suffix").GetString();
            if (candidate.Length > 131072) throw new InvalidDataException("Materialized state character bound131072.");
            var scalar = JsonSerializer.SerializeToElement(candidate, PythonStringJson.Unicode.String); var sequence = LayaSequences.Build(tokenizer, scalar, internalQuestion, budget, 16384);
            if (sequence.Ids.Length == target) return scalar;
            if (sequence.Ids.Length < target) lower = middle + 1; else upper = middle - 1;
        }
        throw new InvalidDataException("Exact independent-tokenized boundary unavailable; no approximate input substituted.");
    }
    private static string Repeat(string text, int count, Budget budget)
    {
        if (count is < 0 or > 4096 || (long)text.Length * count > 131072) throw new InvalidDataException("Recipe repeat count/character bound."); var result = new System.Text.StringBuilder(text.Length * count);
        for (var index = 0; index < count; index++) { if ((index & 63) == 0) budget.Check(); result.Append(text); } return result.ToString();
    }
    private static JsonElement Parse(byte[] bytes) { using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }); return document.RootElement.Clone(); }
}
