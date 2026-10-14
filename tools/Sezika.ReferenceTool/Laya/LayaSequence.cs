using System.Text;
using System.Text.Json;
using Sezika.ReferenceTool.Tokenization;

namespace Sezika.ReferenceTool.Laya;

internal sealed class SequenceBudgetException(string message) : Exception(message);
internal sealed record LayaSequence(int[] Ids, int[] Markers, LayaQuestion Question, SequenceDetails Details);
internal sealed record LayaCollation(int[][] InputIds, int[][] AttentionMask, int[][] MarkerPositions, bool[][] MarkerMask, int[] QuestionTypes);
internal static class LayaSequences
{
    internal static string SerializeState(JsonElement state, Budget budget)
    {
        if (state.ValueKind == JsonValueKind.String) return state.GetString()!;
        return PythonJson.Render(state, budget);
    }
    internal static LayaSequence Build(FrozenTokenizer tokenizer, JsonElement state, LayaQuestion question, Budget budget, int maximumLength = 1024)
    {
        if (maximumLength is < 1 or > 16384) throw new SequenceBudgetException("Explicit sequence length bound16384.");
        var instruction = question.Type + " question: " + question.Instructions.Replace("<mask>", " ", StringComparison.Ordinal);
        var instructionIds = tokenizer.Encode(instruction, false, budget); var optionIds = new int[question.Options.Length][]; var original = new int[optionIds.Length];
        if (optionIds.Length is < 1 or > 64) throw new SequenceBudgetException("Marker count bound1..64.");
        for (var index = 0; index < optionIds.Length; index++)
        {
            budget.Check(); var tokens = tokenizer.Encode(" " + question.Options[index].Replace("<mask>", " ", StringComparison.Ordinal), false, budget);
            original[index] = tokens.Length; optionIds[index] = new[] { FrozenTokenizer.Mask }.Concat(tokens.Take(48)).ToArray();
        }
        var optionBudget = 256 - optionIds.Sum(tokens => tokens.Length);
        if (optionBudget < 16)
        {
            var cap = Math.Max(4, (256 - 16) / optionIds.Length);
            for (var index = 0; index < optionIds.Length; index++) { budget.Check(); optionIds[index] = optionIds[index][..Math.Min(cap, optionIds[index].Length)]; }
            optionBudget = 256 - optionIds.Sum(tokens => tokens.Length);
        }
        var retainedInstruction = Math.Min(Math.Max(8, optionBudget), instructionIds.Length); var ids = new List<int> { FrozenTokenizer.Bos }; ids.AddRange(instructionIds.AsSpan(0, retainedInstruction).ToArray()); ids.Add(FrozenTokenizer.Eos);
        var markers = new List<int>(); for (var index = 0; index < optionIds.Length; index++) { budget.Check(); markers.Add(ids.Count); ids.AddRange(optionIds[index]); }
        ids.Add(FrozenTokenizer.Eos);
        var serialized = SerializeState(state, budget); if (serialized.Length > 131072) throw new SequenceBudgetException("State character bound131072.");
        var sanitized = serialized.Replace("<mask>", " ", StringComparison.Ordinal); var tokensState = tokenizer.Encode(sanitized, false, budget);
        if (tokensState.Length > 16384) throw new SequenceBudgetException("State token bound16384."); var retained = Math.Min(tokensState.Length, Math.Max(0, maximumLength - ids.Count - 1));
        var prefixCount = ids.Count; var start = state.ValueKind == JsonValueKind.Array ? tokensState.Length - retained : 0; ids.AddRange(tokensState.AsSpan(start, retained).ToArray()); ids.Add(FrozenTokenizer.Eos);
        var finalIds = ids.Take(maximumLength).ToArray(); var retainedMarkers = markers.Where(position => position < maximumLength).ToArray();
        var actualMarkers = finalIds.Select((id, position) => (id, position)).Where(pair => pair.id == FrozenTokenizer.Mask).Select(pair => pair.position).ToArray();
        if (retainedMarkers.Length != question.Options.Length || !actualMarkers.SequenceEqual(retainedMarkers)) throw new SequenceBudgetException("Constructed marker positions differ after cleaning/cropping mask text.");
        return new(finalIds, retainedMarkers, question, new(serialized, sanitized, instruction, question.Options, instructionIds.Length,
            retainedInstruction, original, optionIds.Select(item => item.Length - 1).ToArray(), prefixCount, prefixCount + tokensState.Length + 1,
            tokensState.Length, retained, tokensState.Length - retained, start, state.ValueKind == JsonValueKind.Array ? "left" : "right", serialized != sanitized));
    }
    internal static LayaCollation Collate(LayaSequence[] items, Budget budget)
    {
        if (items.Length is < 1 or > 46) throw new SequenceBudgetException("Collation row bound1..46.");
        var length = items.Max(item => item.Ids.Length); var markers = items.Max(item => item.Markers.Length);
        if (length > 1024 || markers > 64) throw new SequenceBudgetException("Collation sequence/marker bound1024/64.");
        var ids = new int[items.Length][]; var attention = new int[items.Length][]; var positions = new int[items.Length][]; var mask = new bool[items.Length][]; var types = new int[items.Length];
        for (var row = 0; row < items.Length; row++)
        {
            budget.Check(); ids[row] = new int[length]; attention[row] = new int[length]; positions[row] = new int[markers]; mask[row] = new bool[markers];
            items[row].Ids.CopyTo(ids[row], 0); Array.Fill(attention[row], 1, 0, items[row].Ids.Length); items[row].Markers.CopyTo(positions[row], 0); Array.Fill(mask[row], true, 0, items[row].Markers.Length); types[row] = items[row].Question.TypeIndex;
        }
        return new(ids, attention, positions, mask, types);
    }
}
