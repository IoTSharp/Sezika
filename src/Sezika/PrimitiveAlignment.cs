using System.Text.Json;

namespace Sezika;

/// <summary>A frozen reference row used to compare a typed primitive without treating confidence as accuracy.</summary>
public sealed record PrimitiveReferenceCase
{
    public required string CaseId { get; init; }
    public required string Language { get; init; }
    public required string Primitive { get; init; }
    public required PrimitiveReferenceInput Input { get; init; }
    public required string ModelRevision { get; init; }
    public required string TokenizerRevision { get; init; }
    public required Dictionary<string, double> Logits { get; init; }
    public required Dictionary<string, double> Probabilities { get; init; }
    public Dictionary<string, JsonElement>? Legend { get; init; }
    public required string Status { get; init; }
    public string? AbstentionReason { get; init; }
}

public sealed record PrimitiveReferenceInput
{
    public required JsonElement State { get; init; }
    public required JsonElement Instructions { get; init; }
    public required JsonElement Criteria { get; init; }
}

/// <summary>Versioned fixture envelope for fixed Chinese/English primitive alignment cases.</summary>
public sealed record PrimitiveReferenceFixture
{
    public required string SchemaVersion { get; init; }
    public required string ModelRevision { get; init; }
    public required string TokenizerRevision { get; init; }
    public required string PromptSchema { get; init; }
    public required IReadOnlyList<PrimitiveReferenceCase> Cases { get; init; }
}

public sealed record PrimitiveAlignmentIssue
{
    public required string CaseId { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public sealed record PrimitiveAlignmentReport
{
    public required int CaseCount { get; init; }
    public required int ComparedCount { get; init; }
    public required double MaxLogitError { get; init; }
    public required double MaxProbabilityError { get; init; }
    public required bool Passed { get; init; }
    public required IReadOnlyList<PrimitiveAlignmentIssue> Issues { get; init; }
}

/// <summary>
/// Compares logits, probabilities, score legends and abstention semantics to a
/// separately captured reference implementation. It performs no model inference
/// and therefore cannot turn a probability concentration into an accuracy claim.
/// </summary>
public static class PrimitiveAlignment
{
    public static PrimitiveReferenceFixture LoadFixture(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize(utf8Json, DecisionJsonContext.Default.PrimitiveReferenceFixture)
                ?? throw new DecisionException("decision_alignment_fixture_invalid", "The primitive alignment fixture is null.");
        }
        catch (JsonException exception)
        {
            throw new DecisionException("decision_alignment_fixture_invalid", exception.Message, exception);
        }
    }

    public static PrimitiveAlignmentReport Compare(
        PrimitiveReferenceFixture fixture,
        DecisionResponse response,
        double absoluteTolerance = 1e-5)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(response);
        if (!double.IsFinite(absoluteTolerance) || absoluteTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(absoluteTolerance));

        ValidateFixture(fixture);

        var issues = new List<PrimitiveAlignmentIssue>();
        var compared = 0;
        var maxLogitError = 0d;
        var maxProbabilityError = 0d;
        if (fixture.Cases.Count == 0)
            issues.Add(Issue("<fixture>", "cases", "The fixture contains no reference cases."));

        foreach (var expected in fixture.Cases)
        {
            if (!response.Answers.TryGetValue(expected.CaseId, out var actual))
            {
                issues.Add(Issue(expected.CaseId, "answer", "The response does not contain this reference case."));
                continue;
            }

            compared++;
            if (!string.Equals(response.ModelRevision, expected.ModelRevision, StringComparison.Ordinal))
                issues.Add(Issue(expected.CaseId, "model_revision", $"Expected '{expected.ModelRevision}', got '{response.ModelRevision}'."));
            if (!string.Equals(response.TokenizerRevision, expected.TokenizerRevision, StringComparison.Ordinal))
                issues.Add(Issue(expected.CaseId, "tokenizer_revision", $"Expected '{expected.TokenizerRevision}', got '{response.TokenizerRevision ?? "<missing>"}'."));
            CompareAnswer(expected, actual, absoluteTolerance, issues, ref maxLogitError, ref maxProbabilityError);
        }

        return new PrimitiveAlignmentReport
        {
            CaseCount = fixture.Cases.Count,
            ComparedCount = compared,
            MaxLogitError = maxLogitError,
            MaxProbabilityError = maxProbabilityError,
            Passed = issues.Count == 0 && compared == fixture.Cases.Count,
            Issues = issues,
        };
    }

    private static void CompareAnswer(
        PrimitiveReferenceCase expected,
        Answer actual,
        double tolerance,
        List<PrimitiveAlignmentIssue> issues,
        ref double maxLogitError,
        ref double maxProbabilityError)
    {
        if (!string.Equals(expected.Status, actual.Status, StringComparison.Ordinal))
            issues.Add(Issue(expected.CaseId, "status", $"Expected '{expected.Status}', got '{actual.Status}'."));
        var actualPrimitive = actual switch
        {
            ChoiceAnswer => "choice",
            ScoreAnswer => "score",
            BooleanAnswer => "boolean",
            _ => "unknown",
        };
        if (!string.Equals(expected.Primitive, actualPrimitive, StringComparison.Ordinal))
            issues.Add(Issue(expected.CaseId, "primitive", $"Expected '{expected.Primitive}', got '{actualPrimitive}'."));
        if (!string.Equals(expected.AbstentionReason, actual.AbstentionReason, StringComparison.Ordinal))
            issues.Add(Issue(expected.CaseId, "abstention_reason", $"Expected '{expected.AbstentionReason ?? "<none>"}', got '{actual.AbstentionReason ?? "<none>"}'."));

        var actualLogits = actual switch
        {
            ChoiceAnswer choice => choice.Logits,
            ScoreAnswer score => score.Logits,
            BooleanAnswer boolean => boolean.Logits,
            _ => null,
        };
        CompareNumbers(expected.CaseId, "logits", expected.Logits, actualLogits, tolerance, issues, ref maxLogitError);

        var actualProbabilities = actual switch
        {
            ChoiceAnswer choice => choice.Probabilities,
            ScoreAnswer score => score.Probabilities,
            BooleanAnswer boolean => new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["true"] = boolean.ProbabilityTrue,
                ["false"] = 1d - boolean.ProbabilityTrue,
            },
            _ => null,
        };
        CompareNumbers(expected.CaseId, "probabilities", expected.Probabilities, actualProbabilities, tolerance, issues, ref maxProbabilityError);

        if (expected.Legend is not null)
        {
            if (actual is not ScoreAnswer score || score.Legend is null)
            {
                issues.Add(Issue(expected.CaseId, "legend", "Reference contains a score legend but response does not."));
            }
            else
            {
                CompareLegend(expected.CaseId, expected.Legend, score.Legend, issues);
            }
        }
    }

    private static void CompareNumbers(
        string caseId,
        string field,
        IReadOnlyDictionary<string, double> expected,
        IReadOnlyDictionary<string, double>? actual,
        double tolerance,
        List<PrimitiveAlignmentIssue> issues,
        ref double maximum)
    {
        if (actual is null)
        {
            issues.Add(Issue(caseId, field, "The response omitted raw alignment values."));
            return;
        }
        if (expected.Count != actual.Count)
            issues.Add(Issue(caseId, field, $"Expected {expected.Count} entries, got {actual.Count}."));
        foreach (var (key, expectedValue) in expected)
        {
            if (!actual.TryGetValue(key, out var actualValue))
            {
                issues.Add(Issue(caseId, field, $"Missing key '{key}'."));
                continue;
            }
            if (!double.IsFinite(expectedValue) || !double.IsFinite(actualValue))
            {
                issues.Add(Issue(caseId, field, $"Key '{key}' is non-finite."));
                continue;
            }
            var error = Math.Abs(expectedValue - actualValue);
            maximum = Math.Max(maximum, error);
            if (error > tolerance)
                issues.Add(Issue(caseId, field, $"Key '{key}' differs by {error:R}, above tolerance {tolerance:R}."));
        }
    }

    private static void CompareLegend(
        string caseId,
        IReadOnlyDictionary<string, JsonElement> expected,
        IReadOnlyDictionary<string, JsonElement> actual,
        List<PrimitiveAlignmentIssue> issues)
    {
        if (expected.Count != actual.Count)
            issues.Add(Issue(caseId, "legend", $"Expected {expected.Count} entries, got {actual.Count}."));
        foreach (var (key, expectedValue) in expected)
        {
            if (!actual.TryGetValue(key, out var actualValue))
            {
                issues.Add(Issue(caseId, "legend", $"Missing key '{key}'."));
                continue;
            }
            if (!JsonElement.DeepEquals(expectedValue, actualValue))
                issues.Add(Issue(caseId, "legend", $"Value for '{key}' differs."));
        }
    }

    private static PrimitiveAlignmentIssue Issue(string caseId, string field, string message) =>
        new() { CaseId = caseId, Field = field, Message = message };

    private static void ValidateFixture(PrimitiveReferenceFixture fixture)
    {
        if (!string.Equals(fixture.SchemaVersion, "sezika.primitive-reference.v1", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(fixture.ModelRevision) ||
            string.IsNullOrWhiteSpace(fixture.TokenizerRevision) ||
            !string.Equals(fixture.PromptSchema, "sezika.prompt.v1", StringComparison.Ordinal) ||
            fixture.Cases is null || fixture.Cases.Count == 0)
        {
            throw new DecisionException("decision_alignment_fixture_invalid", "The primitive alignment fixture envelope is invalid.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expected in fixture.Cases)
        {
            var caseId = expected?.CaseId ?? "<missing>";
            if (expected is null || expected.Input is null || expected.Logits is null || expected.Probabilities is null ||
                expected.Input.State.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
                expected.Input.Instructions.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
                expected.Input.Criteria.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
                string.IsNullOrWhiteSpace(expected.CaseId) || !ids.Add(expected.CaseId) ||
                expected.Language is not ("en" or "zh") ||
                expected.Primitive is not ("choice" or "score" or "boolean") ||
                string.IsNullOrWhiteSpace(expected.ModelRevision) ||
                string.IsNullOrWhiteSpace(expected.TokenizerRevision) ||
                !string.Equals(expected.ModelRevision, fixture.ModelRevision, StringComparison.Ordinal) ||
                !string.Equals(expected.TokenizerRevision, fixture.TokenizerRevision, StringComparison.Ordinal) ||
                expected.Status is not ("answered" or "abstained") ||
                (expected.Status == "abstained" && string.IsNullOrWhiteSpace(expected.AbstentionReason)) ||
                (expected.Status == "answered" && expected.AbstentionReason is not null))
            {
                throw new DecisionException("decision_alignment_fixture_invalid", $"Primitive reference case '{caseId}' is invalid.");
            }

            ValidateNumbers(caseId, "logits", expected.Logits, allowNegative: true);
            ValidateNumbers(caseId, "probabilities", expected.Probabilities, allowNegative: false);
            if (expected.Logits.Count != expected.Probabilities.Count ||
                expected.Logits.Keys.Except(expected.Probabilities.Keys, StringComparer.Ordinal).Any() ||
                expected.Probabilities.Keys.Except(expected.Logits.Keys, StringComparer.Ordinal).Any())
            {
                throw new DecisionException("decision_alignment_fixture_invalid", $"Case '{caseId}' logits and probabilities must have identical keys.");
            }
            var probabilitySum = expected.Probabilities.Values.Sum();
            if (Math.Abs(probabilitySum - 1d) > 1e-9)
                throw new DecisionException("decision_alignment_fixture_invalid", $"Case '{caseId}' probabilities are not normalized.");
            if (expected.Primitive == "boolean" &&
                (!expected.Probabilities.ContainsKey("true") || !expected.Probabilities.ContainsKey("false")))
                throw new DecisionException("decision_alignment_fixture_invalid", $"Boolean case '{caseId}' must contain true/false probabilities.");
            if (expected.Primitive == "score" && expected.Legend is null)
                throw new DecisionException("decision_alignment_fixture_invalid", $"Score case '{caseId}' must contain a legend.");
        }
    }

    private static void ValidateNumbers(
        string caseId,
        string field,
        IReadOnlyDictionary<string, double> values,
        bool allowNegative)
    {
        if (values.Count == 0 || values.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || !double.IsFinite(pair.Value) || (!allowNegative && pair.Value < 0)))
            throw new DecisionException("decision_alignment_fixture_invalid", $"Case '{caseId}' contains invalid {field}.");
    }
}
