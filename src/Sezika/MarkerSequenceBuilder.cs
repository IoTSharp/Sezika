using System.Text.Json;

namespace Sezika;

// Historical prompt-v1 fixture helper only. Runtime and new captures use
// PromptSequenceBuilder, which has the typed question and independent budgets.
internal static class MarkerSequenceBuilder
{
    internal static (int[] Tokens, int[] Markers) Build(
        TokenizerJson tokenizer,
        JsonElement state,
        JsonElement instructions,
        JsonElement[] criteria,
        int tokenBudget,
        CancellationToken cancellationToken)
    {
        if (tokenBudget < 2) throw new ArgumentOutOfRangeException(nameof(tokenBudget));
        var tokens = new List<int>(tokenBudget) { tokenizer.BosId };
        AddText(tokens, $"type question: {ToPromptText(instructions)}", tokenizer, tokenBudget, cancellationToken);
        tokens.Add(tokenizer.EosId);
        var markers = new int[criteria.Length];
        for (var index = 0; index < criteria.Length; index++)
        {
            markers[index] = tokens.Count;
            tokens.Add(tokenizer.MaskId);
            AddText(tokens, ToPromptText(criteria[index]), tokenizer, tokenBudget, cancellationToken);
        }
        tokens.Add(tokenizer.EosId);
        AddText(tokens, ToPromptText(state), tokenizer, tokenBudget, cancellationToken);
        tokens.Add(tokenizer.EosId);
        if (tokens.Count > tokenBudget)
            throw new DecisionException("decision_token_budget_exceeded", $"The encoded question exceeds the model head token budget ({tokenBudget}).");
        return (tokens.ToArray(), markers);
    }

    private static void AddText(List<int> tokens, string text, TokenizerJson tokenizer, int tokenBudget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var remaining = tokenBudget - tokens.Count;
        if (remaining < 2) throw new DecisionException("decision_token_budget_exceeded", "The encoded question exceeds the token budget.");
        tokens.AddRange(tokenizer.Encode(text, remaining, addSpecialTokens: false));
    }

    private static string ToPromptText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
}
