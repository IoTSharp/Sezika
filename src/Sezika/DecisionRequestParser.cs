using System.Text;
using System.Text.Json;

namespace Sezika;

/// <summary>Parses requests while rejecting duplicate JSON properties before DTO materialization.</summary>
public static class DecisionRequestParser
{
    public static DecisionRequest Parse(ReadOnlySpan<byte> utf8Json, DecisionLimits? limits = null)
    {
        var effectiveLimits = limits ?? DecisionLimits.Default;
        effectiveLimits.Validate();
        if (utf8Json.Length > effectiveLimits.MaxRequestBytes)
        {
            throw new DecisionException("decision_input_limit_exceeded", "The request body exceeds the configured byte limit.");
        }

        RejectDuplicateProperties(utf8Json, effectiveLimits.MaxJsonDepth);
        DecisionRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(utf8Json, DecisionJsonContext.Default.DecisionRequest);
        }
        catch (JsonException exception)
        {
            throw new DecisionException("decision_invalid_json", exception.Message, exception);
        }

        if (request is null)
        {
            throw new DecisionException("decision_invalid_request", "The request body is null.");
        }

        DecisionRequestValidator.Validate(request, effectiveLimits, utf8Json.Length);
        return request;
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8Json, int maxDepth)
    {
        // Permit one extra level so the explicit check below can produce a
        // stable depth diagnostic instead of an implementation-specific reader
        // exception. The input byte limit still bounds the stack allocation.
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
        {
            MaxDepth = maxDepth == int.MaxValue ? maxDepth : maxDepth + 1,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
        var scopes = new Stack<HashSet<string>>(Math.Min(maxDepth, 64));
        try
        {
            while (reader.Read())
            {
                if (reader.CurrentDepth > maxDepth)
                {
                    throw new DecisionException("decision_input_depth_exceeded",
                        $"JSON nesting exceeds the configured depth limit ({maxDepth}).");
                }

                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        if (scopes.Count == 0)
                        {
                            throw new JsonException("Unexpected object end.");
                        }
                        scopes.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        if (scopes.Count == 0)
                        {
                            throw new JsonException("Property name is outside an object.");
                        }

                        var propertyName = reader.GetString();
                        if (propertyName is null || !scopes.Peek().Add(propertyName))
                        {
                            throw new DecisionException("decision_duplicate_property",
                                $"Duplicate JSON property '{propertyName ?? "<null>"}' is not accepted.");
                        }
                        break;
                }
            }

            if (scopes.Count != 0 || reader.BytesConsumed != utf8Json.Length)
            {
                throw new JsonException("Incomplete JSON document.");
            }
        }
        catch (DecisionException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new DecisionException("decision_invalid_json", exception.Message, exception);
        }
    }
}

/// <summary>Values observed while validating a request.</summary>
public readonly record struct DecisionValidationResult(
    int RequestBytes,
    int EstimatedTokens,
    int QuestionCount);

public static class DecisionRequestValidator
{
    public static DecisionValidationResult Validate(
        DecisionRequest request,
        DecisionLimits? limits = null,
        int? requestBytes = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var effectiveLimits = limits ?? DecisionLimits.Default;
        effectiveLimits.Validate();
        if (requestBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestBytes));
        }
        if (requestBytes > effectiveLimits.MaxRequestBytes)
        {
            throw new DecisionException("decision_input_limit_exceeded", "The request body exceeds the configured byte limit.");
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new DecisionException("decision_model_required", "Model is required.");
        }
        if (request.State.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new DecisionException("decision_state_required", "State is required.");
        }

        var stateBytes = GetJsonBytes(request.State);
        if (stateBytes > effectiveLimits.MaxStateBytes)
        {
            throw new DecisionException("decision_input_limit_exceeded", "State exceeds the configured byte limit.");
        }
        if (request.Questions is null || request.Questions.Count == 0 ||
            request.Questions.Count > effectiveLimits.MaxQuestions)
        {
            throw new DecisionException("decision_question_limit_exceeded",
                $"Question count must be between 1 and {effectiveLimits.MaxQuestions}.");
        }

        var totalBytes = requestBytes ?? Encoding.UTF8.GetByteCount(request.Model);
        totalBytes = checked(totalBytes + stateBytes);
        foreach (var (id, question) in request.Questions)
        {
            ValidateIdentifier(id, "question", effectiveLimits.MaxIdentifierLength);
            if (question is null)
            {
                throw new DecisionException("decision_question_invalid", $"Question '{id}' is null.");
            }

            var instructionBytes = GetJsonBytes(question.Instructions);
            if (question.Instructions.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
                instructionBytes > effectiveLimits.MaxInstructionBytes)
            {
                throw new DecisionException("decision_instruction_invalid",
                    $"Question '{id}' has invalid or oversized instructions.");
            }

            var questionBytes = checked(Encoding.UTF8.GetByteCount(id) + stateBytes + instructionBytes);
            switch (question)
            {
                case ChoiceQuestion choice:
                    ValidateChoice(id, choice, effectiveLimits, ref questionBytes);
                    break;
                case ScoreQuestion score:
                    ValidateScore(id, score, effectiveLimits, ref questionBytes);
                    break;
                case BooleanQuestion boolean:
                    ValidateBoolean(id, boolean, ref questionBytes);
                    break;
                default:
                    throw new DecisionException("decision_question_type_invalid",
                        $"Question '{id}' uses an unsupported question type.");
            }

            var estimatedTokens = EstimateTokens(questionBytes);
            EnsureTokenBudget(estimatedTokens, effectiveLimits);
            totalBytes = checked(totalBytes + questionBytes);
        }

        var requestEstimate = requestBytes ?? Math.Min(totalBytes, int.MaxValue);
        return new DecisionValidationResult(requestEstimate, EstimateTokens(requestEstimate), request.Questions.Count);
    }

    /// <summary>
    /// Applies an exact model-tokenizer count after the structural pass. The
    /// UTF-8 estimate above is only a pre-allocation guard.
    /// </summary>
    public static void EnsureTokenBudget(int tokenCount, DecisionLimits? limits = null)
    {
        var effectiveLimits = limits ?? DecisionLimits.Default;
        effectiveLimits.Validate();
        if (tokenCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenCount));
        }
        if (tokenCount > effectiveLimits.MaxTokensPerQuestion)
        {
            throw new DecisionException("decision_token_budget_exceeded",
                $"Token count {tokenCount} exceeds the configured per-question budget ({effectiveLimits.MaxTokensPerQuestion}).");
        }
    }

    private static void ValidateChoice(string id, ChoiceQuestion question, DecisionLimits limits, ref int questionBytes)
    {
        if (question.Criteria is null || question.Criteria.Count < 2)
        {
            throw new DecisionException("decision_candidate_limit_exceeded",
                $"Choice question '{id}' requires at least two candidates.");
        }
        if (question.Criteria.Count > limits.MaxCandidates)
        {
            throw new DecisionException("decision_candidate_limit_exceeded",
                $"Choice question '{id}' has too many candidates.");
        }

        foreach (var (criterionId, criterion) in question.Criteria)
        {
            ValidateIdentifier(criterionId, "criterion", limits.MaxIdentifierLength);
            var bytes = GetJsonBytes(criterion);
            if (criterion.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                throw new DecisionException("decision_criteria_invalid",
                    $"Choice criterion '{criterionId}' has no value.");
            }
            questionBytes = checked(questionBytes + Encoding.UTF8.GetByteCount(criterionId) + bytes);
        }
    }

    private static void ValidateScore(string id, ScoreQuestion question, DecisionLimits limits, ref int questionBytes)
    {
        if (question.Criteria is null || question.Criteria.Length < 2)
        {
            throw new DecisionException("decision_candidate_limit_exceeded",
                $"Score question '{id}' requires at least two levels.");
        }
        if (question.Criteria.Length > limits.MaxScoreCriteria)
        {
            throw new DecisionException("decision_candidate_limit_exceeded",
                $"Score question '{id}' has too many levels.");
        }

        for (var index = 0; index < question.Criteria.Length; index++)
        {
            var criterion = question.Criteria[index];
            if (criterion.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                throw new DecisionException("decision_criteria_invalid",
                    $"Score level {index} has no value.");
            }
            questionBytes = checked(questionBytes + GetJsonBytes(criterion));
        }
    }

    private static void ValidateBoolean(string id, BooleanQuestion question, ref int questionBytes)
    {
        if (question.Criteria is null ||
            question.Criteria.WhenTrue.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
            question.Criteria.WhenFalse.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new DecisionException("decision_criteria_required",
                $"Boolean question '{id}' requires true and false criteria.");
        }

        questionBytes = checked(questionBytes + GetJsonBytes(question.Criteria.WhenTrue) +
            GetJsonBytes(question.Criteria.WhenFalse));
    }

    private static void ValidateIdentifier(string value, string kind, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw new DecisionException("decision_identifier_invalid",
                $"{kind} identifiers must be non-empty and at most {maxLength} characters.");
        }
    }

    private static int GetJsonBytes(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return 0;
        }
        return Encoding.UTF8.GetByteCount(value.GetRawText());
    }

    private static int EstimateTokens(int bytes) =>
        bytes == 0 ? 0 : checked((bytes / 4) + (bytes % 4 == 0 ? 0 : 1));
}
