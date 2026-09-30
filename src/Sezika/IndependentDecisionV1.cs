using System.Text;
using System.Text.Json;

namespace Sezika;

/// <summary>Typed input for the independent decision-v1 protocol.</summary>
public sealed record IndependentDecisionRequest(
    int SchemaVersion,
    string ModelId,
    JsonElement State,
    IReadOnlyList<IndependentDecisionQuestion> Questions);

public abstract record IndependentDecisionQuestion(string Id, string Instruction);

public sealed record IndependentChoiceCandidate(string Id, string Text);

public sealed record IndependentChoiceQuestion(
    string Id,
    string Instruction,
    IReadOnlyList<IndependentChoiceCandidate> Candidates)
    : IndependentDecisionQuestion(Id, Instruction);

public sealed record IndependentScoreQuestion(
    string Id,
    string Instruction,
    IReadOnlyList<string> Levels)
    : IndependentDecisionQuestion(Id, Instruction);

public sealed record IndependentBooleanQuestion(
    string Id,
    string Instruction,
    string Statement,
    string WhenFalse,
    string WhenTrue)
    : IndependentDecisionQuestion(Id, Instruction);

public sealed record IndependentDecisionSequenceOptions
{
    public int MaxTokens { get; init; } = 1024;
    public int MaxCharacters { get; init; } = 262_144;
    public TimeSpan Deadline { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>One complete decision-v1 sequence and marker positions used by training and inference.</summary>
public sealed record IndependentDecisionSequence
{
    public required int[] TokenIds { get; init; }
    public required int[] MarkerPositions { get; init; }
    public required string[] CandidateIds { get; init; }
    public required MarkerQuestionType Type { get; init; }
    public required string SerializedState { get; init; }
    public required string SerializedQuestion { get; init; }
}

/// <summary>Parses raw decision-v1 JSON with duplicate and unknown-property rejection.</summary>
public static class IndependentDecisionV1Parser
{
    private const int MaxBytes = 1 * 1024 * 1024;
    private static readonly HashSet<string> RequestProperties = ["schema_version", "model_id", "state", "questions"];
    private static readonly HashSet<string> QuestionProperties = ["id", "type", "instruction", "candidates", "levels", "statement", "when_false", "when_true"];
    private static readonly HashSet<string> CandidateProperties = ["id", "text"];

    public static IndependentDecisionRequest Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MaxBytes) throw new DecisionException("decision_v1_input_limit_exceeded", "decision-v1 JSON exceeds its byte limit.");
        RejectDuplicateProperties(utf8Json);
        JsonDocument document;
        try { document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException exception) { throw new DecisionException("decision_v1_invalid_json", exception.Message, exception); }
        using (document)
        {
            var root = document.RootElement; RequireObject(root, "request"); RejectUnknown(root, RequestProperties);
            var questionsElement = root.GetProperty("questions");
            if (questionsElement.ValueKind != JsonValueKind.Array || questionsElement.GetArrayLength() is < 1 or > 32)
                throw new DecisionException("decision_v1_question_invalid", "decision-v1 questions must contain 1 to 32 items.");
            var questions = new List<IndependentDecisionQuestion>(questionsElement.GetArrayLength()); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in questionsElement.EnumerateArray())
            {
                RequireObject(item, "question"); RejectUnknown(item, QuestionProperties);
                var id = Text(item, "id"); if (!ids.Add(id)) throw new DecisionException("decision_v1_question_invalid", "Question IDs must be unique.");
                var instruction = Text(item, "instruction"); var type = Text(item, "type");
                questions.Add(type switch
                {
                    "choice" => ParseChoice(id, instruction, item),
                    "score" => ParseScore(id, instruction, item),
                    "boolean" => new IndependentBooleanQuestion(id, instruction, Text(item, "statement"), Text(item, "when_false"), Text(item, "when_true")),
                    _ => throw new DecisionException("decision_v1_question_invalid", $"Unsupported decision-v1 type '{type}'."),
                });
            }
            return new IndependentDecisionRequest(root.GetProperty("schema_version").GetInt32(), Text(item: root, property: "model_id"),
                root.GetProperty("state").Clone(), questions);
        }
    }

    private static IndependentChoiceQuestion ParseChoice(string id, string instruction, JsonElement item)
    {
        var candidatesElement = item.GetProperty("candidates");
        if (candidatesElement.ValueKind != JsonValueKind.Array || candidatesElement.GetArrayLength() is < 2 or > 32)
            throw new DecisionException("decision_v1_candidate_invalid", "Choice candidates must contain 2 to 32 items.");
        var candidates = new List<IndependentChoiceCandidate>(candidatesElement.GetArrayLength()); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidatesElement.EnumerateArray())
        {
            RequireObject(candidate, "candidate"); RejectUnknown(candidate, CandidateProperties);
            var candidateId = Text(candidate, "id"); if (!ids.Add(candidateId)) throw new DecisionException("decision_v1_candidate_invalid", "Candidate IDs must be unique.");
            candidates.Add(new IndependentChoiceCandidate(candidateId, Text(candidate, "text")));
        }
        return new IndependentChoiceQuestion(id, instruction, candidates);
    }

    private static IndependentScoreQuestion ParseScore(string id, string instruction, JsonElement item)
    {
        var levels = item.GetProperty("levels");
        if (levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() is < 2 or > 10)
            throw new DecisionException("decision_v1_candidate_invalid", "Score levels must contain 2 to 10 items.");
        return new IndependentScoreQuestion(id, instruction, levels.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new DecisionException("decision_v1_text_invalid", "Score levels must be strings.")).ToArray());
    }

    private static string Text(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 65_536)
            throw new DecisionException("decision_v1_text_invalid", $"decision-v1 property '{property}' must be a bounded non-empty string.");
        return value.GetString()!;
    }

    private static void RequireObject(JsonElement value, string kind)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new DecisionException("decision_v1_invalid_json", $"decision-v1 {kind} must be an object.");
    }

    private static void RejectUnknown(JsonElement value, HashSet<string> allowed)
    {
        foreach (var property in value.EnumerateObject()) if (!allowed.Contains(property.Name)) throw new DecisionException("decision_v1_unknown_property", $"Unknown decision-v1 property '{property.Name}'.");
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions { MaxDepth = 33, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        var scopes = new Stack<HashSet<string>>();
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                else if (reader.TokenType == JsonTokenType.EndObject) { if (scopes.Count == 0) throw new JsonException("Unexpected object end."); scopes.Pop(); }
                else if (reader.TokenType == JsonTokenType.PropertyName && (scopes.Count == 0 || !scopes.Peek().Add(reader.GetString()!)))
                    throw new DecisionException("decision_duplicate_property", "Duplicate decision-v1 JSON property is not accepted.");
                if (reader.CurrentDepth > 32) throw new DecisionException("decision_v1_depth_exceeded", "decision-v1 JSON nesting exceeds its bound.");
            }
            if (scopes.Count != 0 || reader.BytesConsumed != utf8Json.Length) throw new JsonException("Incomplete JSON document.");
        }
        catch (DecisionException) { throw; }
        catch (JsonException exception) { throw new DecisionException("decision_v1_invalid_json", exception.Message, exception); }
    }
}

/// <summary>Builds the independent sequence; legacy Laya prompt rules are deliberately not used.</summary>
public static class IndependentDecisionV1SequenceBuilder
{
    public static IndependentDecisionSequence Build(
        TokenizerJson tokenizer,
        IndependentDecisionRequest request,
        IndependentDecisionQuestion question,
        IndependentDecisionSequenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(question);
        var effective = options ?? new IndependentDecisionSequenceOptions();
        ValidateOptions(effective);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(effective.Deadline);
        try { return BuildCore(tokenizer, request, question, effective, deadline.Token); }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DecisionException("decision_deadline_exceeded", "decision-v1 sequence construction exceeded its deadline.", exception);
        }
    }

    private static IndependentDecisionSequence BuildCore(
        TokenizerJson tokenizer,
        IndependentDecisionRequest request,
        IndependentDecisionQuestion question,
        IndependentDecisionSequenceOptions options,
        CancellationToken cancellationToken)
    {
        if (request.SchemaVersion != 1 || string.IsNullOrWhiteSpace(request.ModelId) ||
            !request.ModelId.StartsWith("sezika/", StringComparison.Ordinal) || request.ModelId.Length > 128)
            throw new DecisionException("decision_v1_identity_invalid", "decision-v1 requires a bounded sezika model identity.");
        if (request.State.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new DecisionException("decision_v1_state_invalid", "decision-v1 state must be a string, object or array.");
        if (request.State.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
            throw new DecisionException("decision_v1_state_invalid", "decision-v1 state JSON type is unsupported.");
        if (request.Questions is null || request.Questions.Count is < 1 or > 32 ||
            !request.Questions.Contains(question))
            throw new DecisionException("decision_v1_question_invalid", "The question must belong to a bounded request.");

        var serializedState = JsonSerializer.Serialize(request.State, DecisionJsonContext.Default.JsonElement);
        var serializedQuestion = RenderQuestion(question, cancellationToken);
        var candidateTexts = RenderCandidates(question, cancellationToken, out var candidateIds, out var type);
        var totalCharacters = checked(serializedState.Length + serializedQuestion.Length + candidateTexts.Sum(value => value.Length));
        if (totalCharacters > options.MaxCharacters)
            throw new DecisionException("decision_v1_input_limit_exceeded", "decision-v1 rendered input exceeds its character limit.");

        var tokens = new List<int>(Math.Min(options.MaxTokens, 256)) { tokenizer.BosId };
        AppendUserSegment(tokenizer, serializedQuestion, tokens, options.MaxTokens, cancellationToken);
        tokens.Add(tokenizer.EosId);
        AppendUserSegment(tokenizer, serializedState, tokens, options.MaxTokens, cancellationToken);
        tokens.Add(tokenizer.EosId);
        var markers = new int[candidateTexts.Length];
        for (var index = 0; index < candidateTexts.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            markers[index] = tokens.Count;
            tokens.Add(tokenizer.MaskId);
            AppendUserSegment(tokenizer, candidateTexts[index], tokens, options.MaxTokens, cancellationToken);
        }
        tokens.Add(tokenizer.EosId);
        if (tokens.Count > options.MaxTokens)
            throw new PromptTruncationException("decision_v1_token_budget_exceeded",
                "decision-v1 strict policy rejects a sequence over the verified token budget.",
                new PromptSequenceDiagnostics
                {
                    LengthPolicy = PromptLengthPolicy.Strict, SerializedState = serializedState,
                    SanitizedState = serializedState, RenderedInstruction = serializedQuestion,
                    RenderedOptions = candidateTexts, OriginalInstructionTokens = 0,
                    RetainedInstructionTokens = 0, OriginalOptionTokens = [], RetainedOptionTokens = [],
                    PrefixTokensIncludingSpecials = tokens.Count, UntruncatedTotalTokens = tokens.Count,
                    OriginalTotalTokens = tokens.Count, OriginalStateTokens = 0, RetainedStateTokens = 0,
                    DroppedStateTokens = 0, StateRetainedStart = 0, StateTruncationDirection = "none",
                    MaskTextRemoved = false, InstructionTruncated = false, OptionsTruncated = false,
                    StateTruncated = false, FinalSequenceTruncated = true,
                });
        return new IndependentDecisionSequence
        {
            TokenIds = tokens.ToArray(), MarkerPositions = markers, CandidateIds = candidateIds,
            Type = type, SerializedState = serializedState, SerializedQuestion = serializedQuestion,
        };
    }

    private static void AppendUserSegment(TokenizerJson tokenizer, string text, List<int> destination,
        int maxTokens, CancellationToken cancellationToken)
    {
        int[] encoded;
        try
        {
            encoded = tokenizer.Encode(text, maxTokens, addSpecialTokens: false, cancellationToken);
        }
        catch (DecisionException exception) when (exception.Code == "decision_token_limit_exceeded")
        {
            throw new DecisionException("decision_v1_token_budget_exceeded", "decision-v1 strict policy rejects a segment over the token budget.", exception);
        }
        foreach (var token in encoded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token is var id && (id == tokenizer.BosId || id == tokenizer.EosId || id == tokenizer.MaskId))
                throw new DecisionException("decision_v1_control_token", "User text produced a reserved control token.");
            destination.Add(token);
            if (destination.Count >= maxTokens)
                throw new DecisionException("decision_v1_token_budget_exceeded", "decision-v1 sequence exceeds its strict token budget.");
        }
    }

    private static string RenderQuestion(IndependentDecisionQuestion question, CancellationToken cancellationToken)
    {
        ValidateText(question.Id, "question id"); ValidateText(question.Instruction, "instruction");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            switch (question)
            {
                case IndependentChoiceQuestion: writer.WriteString("type", "choice"); break;
                case IndependentScoreQuestion: writer.WriteString("type", "score"); break;
                case IndependentBooleanQuestion boolean:
                    ValidateText(boolean.Statement, "statement"); ValidateText(boolean.WhenFalse, "when_false");
                    ValidateText(boolean.WhenTrue, "when_true");
                    writer.WriteString("type", "boolean"); break;
                default: throw new DecisionException("decision_v1_question_invalid", "Unknown decision-v1 question type.");
            }
            writer.WriteString("id", question.Id);
            writer.WriteString("instruction", question.Instruction);
            if (question is IndependentBooleanQuestion value) writer.WriteString("statement", value.Statement);
            writer.WriteEndObject(); writer.Flush();
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string[] RenderCandidates(IndependentDecisionQuestion question, CancellationToken cancellationToken,
        out string[] candidateIds, out MarkerQuestionType type)
    {
        var values = new List<string>(); var ids = new List<string>();
        switch (question)
        {
            case IndependentChoiceQuestion choice:
                type = MarkerQuestionType.Choice;
                if (choice.Candidates is null || choice.Candidates.Count is < 2 or > 32) CandidateError();
                var candidates = choice.Candidates!;
                foreach (var candidate in candidates)
                {
                    ValidateText(candidate.Id, "candidate id"); ValidateText(candidate.Text, "candidate text");
                    if (!ids.Contains(candidate.Id, StringComparer.Ordinal)) CandidateAdd(candidate.Id, candidate.Text);
                    else throw new DecisionException("decision_v1_candidate_invalid", "Candidate IDs must be unique.");
                }
                break;
            case IndependentScoreQuestion score:
                type = MarkerQuestionType.Score;
                if (score.Levels is null || score.Levels.Count is < 2 or > 10) CandidateError();
                var levels = score.Levels!;
                for (var index = 0; index < levels.Count; index++)
                {
                    ValidateText(levels[index], "score level"); CandidateAdd(index.ToString(), levels[index]);
                }
                break;
            case IndependentBooleanQuestion boolean:
                type = MarkerQuestionType.Boolean;
                CandidateAdd("false", boolean.WhenFalse); CandidateAdd("true", boolean.WhenTrue); break;
            default: throw new DecisionException("decision_v1_question_invalid", "Unknown decision-v1 question type.");
        }
        candidateIds = ids.ToArray(); return values.ToArray();

        void CandidateAdd(string id, string text)
        {
            cancellationToken.ThrowIfCancellationRequested(); ids.Add(id);
            using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject(); writer.WriteString("id", id); writer.WriteString("text", text);
                writer.WriteEndObject(); writer.Flush();
            }
            values.Add(Encoding.UTF8.GetString(stream.ToArray()));
        }
        static void CandidateError() => throw new DecisionException("decision_v1_candidate_invalid", "Candidate count is outside the decision-v1 contract.");
    }

    private static void ValidateText(string value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 65_536)
            throw new DecisionException("decision_v1_text_invalid", $"decision-v1 {kind} must be non-empty and bounded.");
    }

    private static void ValidateOptions(IndependentDecisionSequenceOptions options)
    {
        if (options.MaxTokens is < 2 or > 8192 || options.MaxCharacters is < 1 or > 1_048_576 ||
            options.Deadline <= TimeSpan.Zero || options.Deadline > TimeSpan.FromMinutes(5))
            throw new DecisionException("decision_v1_options_invalid", "decision-v1 limits are outside the bounded range.");
    }
}

/// <summary>Exports complete-sequence marker rows; the encoder remains frozen and externally owned.</summary>
public static class IndependentMarkerFeatureExporter
{
    public static MarkerFeatureExample Export(
        TokenizerJson tokenizer,
        IEncoder encoder,
        MarkerFeatureIdentity identity,
        IndependentDecisionRequest request,
        IndependentDecisionQuestion question,
        string recordId,
        string language,
        int targetIndex,
        IndependentDecisionSequenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        if (string.IsNullOrWhiteSpace(recordId) || recordId.Length > 128 || language is not ("zh" or "en"))
            throw new DecisionException("training_input_invalid", "Feature record identity or language is invalid.");
        var sequence = IndependentDecisionV1SequenceBuilder.Build(tokenizer, request, question, options, cancellationToken);
        if ((uint)targetIndex >= (uint)sequence.MarkerPositions.Length)
            throw new DecisionException("training_label_invalid", "Feature target is outside the candidate range.");
        var hidden = encoder.Encode(sequence.TokenIds, cancellationToken);
        if (hidden.Length % sequence.TokenIds.Length != 0)
            throw new DecisionException("training_shape_invalid", "Encoder output is not a complete sequence tensor.");
        var width = hidden.Length / sequence.TokenIds.Length;
        if (width != identity.HiddenSize)
            throw new DecisionException("training_shape_invalid", "Encoder output width differs from the feature identity.");
        var features = new float[sequence.MarkerPositions.Length][];
        for (var candidate = 0; candidate < features.Length; candidate++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = checked(sequence.MarkerPositions[candidate] * width);
            features[candidate] = hidden.AsSpan(offset, width).ToArray();
            if (Array.Exists(features[candidate], value => !float.IsFinite(value)))
                throw new DecisionException("training_value_invalid", "Encoder marker feature is non-finite.");
        }
        return new MarkerFeatureExample(recordId, sequence.Type, language, sequence.TokenIds,
            sequence.MarkerPositions, features, targetIndex);
    }
}
