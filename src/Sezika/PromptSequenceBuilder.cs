using System.Globalization;
using System.Text.Json;

namespace Sezika;

/// <summary>Builds an input sequence with ordered candidate markers.</summary>
public static class PromptSequenceBuilder
{
    public static PromptSequence Build(
        TokenizerJson tokenizer,
        JsonElement state,
        Question question,
        PromptSequenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(question);
        var effective = options ?? new PromptSequenceOptions();
        ValidateOptions(effective);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(effective.Deadline);
        try { return BuildCore(tokenizer, state, question, effective, deadline.Token); }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DecisionException("decision_deadline_exceeded", "Prompt construction exceeded its deadline.", exception);
        }
    }

    private static PromptSequence BuildCore(TokenizerJson tokenizer, JsonElement state, Question question,
        PromptSequenceOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (typeName, typeId) = question switch
        {
            ChoiceQuestion => ("choice", 0), ScoreQuestion => ("score", 1), BooleanQuestion => ("noul", 2),
            _ => throw new DecisionException("decision_question_type_unsupported", "Question type is unsupported."),
        };
        var (labels, renderedOptions) = RenderOptions(question, options, cancellationToken);
        var serialized = PromptJsonText.Render(state, options.MaxInputCharacters, cancellationToken);
        var instructions = PromptJsonText.Render(question.Instructions, options.MaxInputCharacters, cancellationToken);
        var sanitized = serialized.Replace("<mask>", " ", StringComparison.Ordinal);
        var renderedInstruction = typeName + " question: " + instructions.Replace("<mask>", " ", StringComparison.Ordinal);
        var characters = checked(serialized.Length + renderedInstruction.Length);
        foreach (var text in renderedOptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            characters = checked(characters + text.Length);
        }
        if (characters > options.MaxInputCharacters)
            throw new DecisionException("decision_input_limit_exceeded", "Rendered prompt exceeds its total character limit.");

        var head = Encode(renderedInstruction);
        var stateIds = Encode(sanitized);
        var optionIds = new int[renderedOptions.Length][];
        var originalOptionTokens = new int[optionIds.Length];
        var retainedOptionTokens = new int[optionIds.Length];
        var maskRemoved = serialized != sanitized || instructions.Contains("<mask>", StringComparison.Ordinal);
        var optionTotal = 0;
        for (var index = 0; index < optionIds.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = renderedOptions[index];
            maskRemoved |= text.Contains("<mask>", StringComparison.Ordinal);
            optionIds[index] = Encode(" " + text.Replace("<mask>", " ", StringComparison.Ordinal));
            originalOptionTokens[index] = optionIds[index].Length;
            retainedOptionTokens[index] = Math.Min(48, optionIds[index].Length);
            optionTotal = checked(optionTotal + 1 + retainedOptionTokens[index]);
        }
        var optionBudget = options.PrefixTokenBudget - optionTotal;
        if (optionBudget < 16)
        {
            // The per-option cap includes the marker itself.
            var per = Math.Max(4, FloorDivide(options.PrefixTokenBudget - 16, optionIds.Length));
            optionTotal = 0;
            for (var index = 0; index < optionIds.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                retainedOptionTokens[index] = Math.Min(retainedOptionTokens[index], per - 1);
                optionTotal = checked(optionTotal + 1 + retainedOptionTokens[index]);
            }
            optionBudget = options.PrefixTokenBudget - optionTotal;
        }
        var retainedHead = Math.Min(head.Length, Math.Max(8, optionBudget));
        var prefixTokens = checked(3 + retainedHead + optionTotal);
        var room = Math.Max(0, options.TotalTokenBudget - prefixTokens - 1);
        var retainedState = Math.Min(room, stateIds.Length);
        var truncateLeft = state.ValueKind == JsonValueKind.Array;
        var stateStart = truncateLeft ? stateIds.Length - retainedState : 0;
        var tokens = new List<int>(Math.Min(options.TotalTokenBudget, prefixTokens + retainedState + 1)) { tokenizer.BosId };
        tokens.AddRange(head.AsSpan(0, retainedHead));
        tokens.Add(tokenizer.EosId);
        var markers = new int[optionIds.Length];
        for (var index = 0; index < optionIds.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            markers[index] = tokens.Count;
            tokens.Add(tokenizer.MaskId);
            tokens.AddRange(optionIds[index].AsSpan(0, retainedOptionTokens[index]));
        }
        tokens.Add(tokenizer.EosId);
        tokens.AddRange(stateIds.AsSpan(stateStart, retainedState));
        tokens.Add(tokenizer.EosId);
        var originalTotal = checked(4 + head.Length + originalOptionTokens.Sum() + optionIds.Length + stateIds.Length);
        var finalSequenceTruncated = tokens.Count > options.TotalTokenBudget;
        if (finalSequenceTruncated)
        {
            retainedHead = Math.Min(retainedHead, options.TotalTokenBudget - 1);
            for (var index = 0; index < retainedOptionTokens.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                retainedOptionTokens[index] = Math.Min(retainedOptionTokens[index],
                    Math.Max(0, options.TotalTokenBudget - markers[index] - 1));
            }
        }
        var diagnostics = new PromptSequenceDiagnostics
        {
            LengthPolicy = options.LengthPolicy, SerializedState = serialized, SanitizedState = sanitized,
            RenderedInstruction = renderedInstruction, RenderedOptions = renderedOptions,
            OriginalInstructionTokens = head.Length, RetainedInstructionTokens = retainedHead,
            OriginalOptionTokens = originalOptionTokens, RetainedOptionTokens = retainedOptionTokens,
            PrefixTokensIncludingSpecials = prefixTokens, UntruncatedTotalTokens = prefixTokens + stateIds.Length + 1,
            OriginalTotalTokens = originalTotal, OriginalStateTokens = stateIds.Length,
            RetainedStateTokens = retainedState, DroppedStateTokens = stateIds.Length - retainedState,
            StateRetainedStart = stateStart, StateTruncationDirection = truncateLeft ? "left" : "right",
            MaskTextRemoved = maskRemoved, InstructionTruncated = retainedHead != head.Length,
            OptionsTruncated = !originalOptionTokens.SequenceEqual(retainedOptionTokens),
            StateTruncated = retainedState != stateIds.Length,
            FinalSequenceTruncated = finalSequenceTruncated,
        };
        if (options.LengthPolicy == PromptLengthPolicy.Strict && (diagnostics.WasTruncated || tokens.Count > options.TotalTokenBudget))
            throw new PromptTruncationException("decision_token_budget_exceeded",
                "Strict prompt policy rejects input that requires prefix or state truncation.", diagnostics);
        // Every candidate marker must survive the final sequence limit so that
        // the output still represents the complete candidate set.
        if (markers.Any(marker => marker >= options.TotalTokenBudget))
            throw new PromptTruncationException("decision_candidate_limit_exceeded",
                "The complete prompt budget cannot preserve every candidate marker.", diagnostics);
        if (tokens.Count > options.TotalTokenBudget)
            tokens.RemoveRange(options.TotalTokenBudget, tokens.Count - options.TotalTokenBudget);
        cancellationToken.ThrowIfCancellationRequested();
        return new PromptSequence { TokenIds = tokens.ToArray(), MarkerPositions = markers, CandidateLabels = labels, TypeId = typeId, Diagnostics = diagnostics };

        int[] Encode(string text) => tokenizer.Encode(text, options.MaxEncodedTokens, addSpecialTokens: false, cancellationToken);
    }

    private static (string[] Labels, string[] Options) RenderOptions(Question question, PromptSequenceOptions options,
        CancellationToken cancellationToken)
    {
        var labels = new List<string>();
        var rendered = new List<string>();
        var renderedCharacters = 0;
        string Text(JsonElement value) => PromptJsonText.Render(value, options.MaxInputCharacters, cancellationToken);
        void AddOption(string text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            renderedCharacters = checked(renderedCharacters + text.Length);
            if (renderedCharacters > options.MaxInputCharacters)
                throw new DecisionException("decision_input_limit_exceeded", "Rendered candidates exceed the total prompt character limit.");
            rendered.Add(text);
        }
        switch (question)
        {
            case ChoiceQuestion choice:
                if (choice.Criteria is null || choice.Criteria.Count is < 1 or > 64) CandidateLimit();
                foreach (var (label, value) in choice.Criteria!)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (label.Length > options.MaxInputCharacters)
                        throw new DecisionException("decision_input_limit_exceeded", "Candidate label exceeds the prompt character limit.");
                    labels.Add(label);
                    AddOption(IsEmpty(value) ? label : label + ": " + Text(value));
                }
                break;
            case ScoreQuestion score:
                if (score.Criteria is null || score.Criteria.Length is < 1 or > 64) CandidateLimit();
                for (var index = 0; index < score.Criteria!.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (score.Criteria[index].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                        throw new DecisionException("decision_criteria_invalid", "Score levels cannot be null.");
                    var label = index.ToString(CultureInfo.InvariantCulture);
                    labels.Add(label);
                    AddOption("level " + label + ": " + Text(score.Criteria[index]));
                }
                break;
            case BooleanQuestion boolean:
                if (boolean.Labels is { } displayLabels)
                {
                    if (displayLabels.WhenFalse is null || displayLabels.WhenTrue is null)
                        throw new DecisionException("decision_boolean_labels_invalid", "Boolean display labels must be distinct non-empty strings.");
                    if (displayLabels.WhenFalse.Length > options.MaxInputCharacters || displayLabels.WhenTrue.Length > options.MaxInputCharacters)
                        throw new DecisionException("decision_input_limit_exceeded", "Boolean display labels exceed the prompt character limit.");
                }
                var falseLabel = boolean.Labels?.WhenFalse?.Trim() ?? "false";
                var trueLabel = boolean.Labels?.WhenTrue?.Trim() ?? "true";
                if (falseLabel.Length == 0 || trueLabel.Length == 0 || falseLabel == trueLabel)
                    throw new DecisionException("decision_boolean_labels_invalid", "Boolean display labels must be distinct non-empty strings.");
                var whenFalse = boolean.Criteria?.WhenFalse ?? default;
                var whenTrue = boolean.Criteria?.WhenTrue ?? default;
                labels.Add("false"); labels.Add("true");
                AddOption(falseLabel + ": " + (IsEmpty(whenFalse) ? "no, the statement does not hold" : Text(whenFalse)));
                AddOption(trueLabel + ": " + (IsEmpty(whenTrue) ? "yes, the statement holds" : Text(whenTrue)));
                break;
            default: throw new DecisionException("decision_question_type_unsupported", "Question type is unsupported.");
        }
        return (labels.ToArray(), rendered.ToArray());
    }

    private static bool IsEmpty(JsonElement value) => value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
        value.ValueKind == JsonValueKind.String && value.GetString() == string.Empty;

    private static int FloorDivide(int value, int denominator) => value >= 0 ? value / denominator : -((-value + denominator - 1) / denominator);

    private static void CandidateLimit() => throw new DecisionException("decision_candidate_limit_exceeded", "Prompt capture requires 1 to 64 candidates.");

    private static void ValidateOptions(PromptSequenceOptions options)
    {
        if (options.PrefixTokenBudget is < 2 or > 16_384 || options.TotalTokenBudget is < 2 or > 16_384 ||
            options.MaxInputCharacters is < 1 or > 1_048_576 || options.MaxEncodedTokens is < 2 or > 4_194_304 ||
            options.Deadline <= TimeSpan.Zero || options.Deadline > TimeSpan.FromMinutes(5) || !Enum.IsDefined(options.LengthPolicy))
            throw new ArgumentOutOfRangeException(nameof(options), "Prompt options exceed the bounded input, token, or deadline limits.");
    }
}
