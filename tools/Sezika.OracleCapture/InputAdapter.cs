using System.Text.Json;

namespace Sezika.OracleCapture;

internal static class InputAdapter
{
    public static DecisionRequest Request(ReferenceCase source, PromptLengthPolicy policy, List<ContractDifference> differences,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = source.Input;
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("state", out var state) ||
            !input.TryGetProperty("question", out var question) || question.ValueKind != JsonValueKind.Object ||
            !input.TryGetProperty("max_len", out var maxLen) || !maxLen.TryGetInt32(out var total) || total != 1024 ||
            !input.TryGetProperty("head_max_len", out var headMaxLen) || !headMaxLen.TryGetInt32(out var prefix) || prefix != 256)
            throw new DecisionException("capture_expanded_input_invalid", "Expanded input must contain state, question, max_len=1024 and head_max_len=256.");
        if (!question.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            !question.TryGetProperty("instructions", out var instructions))
            throw new DecisionException("decision_question_invalid", "Question type and instructions are required.");
        var typeName = type.GetString();
        var primitive = typeName switch { "choice" => "choice", "score" => "score", "noul" => "boolean", _ => null };
        if (primitive != source.Primitive)
            throw new DecisionException("decision_question_type_invalid", "Expanded upstream question type does not match the fixture primitive.");
        var hasCriteria = question.TryGetProperty("criteria", out var criteria);
        Question typed;
        switch (typeName)
        {
            case "choice":
                if (!hasCriteria || criteria.ValueKind != JsonValueKind.Object)
                    throw new DecisionException("decision_criteria_invalid", "Choice criteria must be an object.");
                var choices = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var item in criteria.EnumerateObject()) // Input reader caps all JSON nodes and candidates below.
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (choices.Count >= 64) throw new DecisionException("decision_candidate_limit_exceeded", "Capture accepts at most 64 source candidates.");
                    choices.Add(item.Name, item.Value.Clone());
                }
                if (choices.Count is < 2 or > 32)
                    differences.Add(new("question.criteria", "choice_candidate_count_2_to_32", source.Status,
                        $"Source has {choices.Count} candidates; Sezika retains its public 2..32 limit and exports the rejection."));
                typed = new ChoiceQuestion { Instructions = instructions.Clone(), Criteria = choices };
                break;
            case "score":
                if (!hasCriteria || criteria.ValueKind != JsonValueKind.Array || criteria.GetArrayLength() > 64)
                    throw new DecisionException("decision_criteria_invalid", "Score criteria must be an array with at most 64 levels.");
                var levels = criteria.EnumerateArray().Select(value => value.Clone()).ToArray();
                if (levels.Length is < 2 or > 10)
                    differences.Add(new("question.criteria", "score_level_count_2_to_10", source.Status,
                        $"Source has {levels.Length} levels; Sezika retains its public 2..10 limit and exports the rejection."));
                typed = new ScoreQuestion { Instructions = instructions.Clone(), Criteria = levels };
                break;
            case "noul":
                BooleanCriteria? booleanCriteria = null;
                if (hasCriteria && criteria.ValueKind != JsonValueKind.Null)
                {
                    RequireBooleanKeys(criteria, "criteria", cancellationToken);
                    booleanCriteria = new BooleanCriteria
                    {
                        WhenFalse = criteria.TryGetProperty("false", out var whenFalse) ? whenFalse.Clone() : default,
                        WhenTrue = criteria.TryGetProperty("true", out var whenTrue) ? whenTrue.Clone() : default,
                    };
                }
                BooleanLabels? labels = null;
                if (question.TryGetProperty("labels", out var displayLabels) && displayLabels.ValueKind != JsonValueKind.Null)
                {
                    RequireBooleanKeys(displayLabels, "labels", cancellationToken);
                    if (!displayLabels.TryGetProperty("false", out var falseLabel) || falseLabel.ValueKind != JsonValueKind.String ||
                        !displayLabels.TryGetProperty("true", out var trueLabel) || trueLabel.ValueKind != JsonValueKind.String)
                        throw new DecisionException("decision_boolean_labels_invalid", "Boolean display labels require two string values.");
                    labels = new BooleanLabels { WhenFalse = falseLabel.GetString()!, WhenTrue = trueLabel.GetString()! };
                }
                typed = new BooleanQuestion { Instructions = instructions.Clone(), Criteria = booleanCriteria, Labels = labels };
                break;
            default: throw new DecisionException("decision_question_type_invalid", "Unsupported upstream primitive.");
        }
        return new DecisionRequest
        {
            Model = ModernBertModelLoader.PinnedModelId,
            State = state.Clone(),
            LengthPolicy = policy,
            Questions = new(StringComparer.Ordinal) { ["q"] = typed },
        };
    }

    private static void RequireBooleanKeys(JsonElement value, string field, CancellationToken cancellationToken)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new DecisionException("decision_criteria_invalid", $"Boolean {field} must be an object.");
        foreach (var property in value.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property.Name is not ("false" or "true"))
                throw new DecisionException("decision_criteria_invalid", $"Unknown Boolean {field} key: {property.Name}.");
        }
    }
}
