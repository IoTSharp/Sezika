using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Sezika.Tests;

/// <summary>Checks input sequences against captured fixtures without running model inference or training.</summary>
internal static class PromptContractChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        var tokenizer = new TokenizerJson(".artifacts/models/laya-mmbert/tokenizer/tokenizer.json", token);
        var fixturePath = "tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json";
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        var cases = fixture.RootElement.GetProperty("cases");
        if (cases.GetArrayLength() != 46) throw new InvalidDataException("Expected the frozen 46-case real reference capture.");
        var compared = 0;
        var policyDifferences = 0;
        var referenceFailures = 0;
        var strictRejections = 0;
        foreach (var row in cases.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var id = row.GetProperty("id").GetString()!;
            if (row.GetProperty("status").GetString() == "failed") { referenceFailures++; continue; }
            var input = row.GetProperty("input");
            var question = MapQuestion(input.GetProperty("question"));
            if (question is ChoiceQuestion choice && choice.Criteria.Count is < 2 or > 32 ||
                question is ScoreQuestion score && score.Criteria.Length is < 2 or > 10)
            {
                policyDifferences++;
                Console.WriteLine($"POLICY: {id} exceeds Sezika's explicit candidate-count contract.");
                continue;
            }
            var options = new PromptSequenceOptions { LengthPolicy = PromptLengthPolicy.LayaCompatible };
            var actual = PromptSequenceBuilder.Build(tokenizer, input.GetProperty("state"), question, options, token);
            var expectedTokens = row.GetProperty("token_ids").EnumerateArray().Select(item => item.GetInt32()).ToArray();
            var expectedMarkers = row.GetProperty("marker_positions").EnumerateArray().Select(item => item.GetInt32()).ToArray();
            var expectedLabels = row.GetProperty("candidate_labels").EnumerateArray().Select(item => item.GetString()!).ToArray();
            if (!actual.TokenIds.SequenceEqual(expectedTokens))
            {
                var index = Enumerable.Range(0, Math.Min(actual.TokenIds.Length, expectedTokens.Length))
                    .FirstOrDefault(index => actual.TokenIds[index] != expectedTokens[index], -1);
                throw new InvalidOperationException($"{id}: token mismatch at {index}; expected count {expectedTokens.Length}, actual {actual.TokenIds.Length}.");
            }
            check(actual.MarkerPositions.SequenceEqual(expectedMarkers) && actual.CandidateLabels.SequenceEqual(expectedLabels),
                $"real Laya prompt tokens/markers/labels: {id}");
            var referenceDiagnostic = row.GetProperty("sequence_diagnostics");
            check(actual.Diagnostics.SerializedState == referenceDiagnostic.GetProperty("serialized_state").GetString() &&
                actual.Diagnostics.RenderedInstruction == referenceDiagnostic.GetProperty("rendered_instruction").GetString() &&
                actual.Diagnostics.OriginalStateTokens == referenceDiagnostic.GetProperty("original_state_tokens").GetInt32() &&
                actual.Diagnostics.RetainedStateTokens == referenceDiagnostic.GetProperty("retained_state_tokens").GetInt32() &&
                actual.Diagnostics.StateRetainedStart == referenceDiagnostic.GetProperty("state_retained_start").GetInt32(),
                $"real Laya state rendering and retained range: {id}");
            if (actual.Diagnostics.WasTruncated)
            {
                try
                {
                    _ = PromptSequenceBuilder.Build(tokenizer, input.GetProperty("state"), question,
                        options with { LengthPolicy = PromptLengthPolicy.Strict }, token);
                    throw new InvalidOperationException($"{id}: strict mode silently accepted input token loss.");
                }
                catch (PromptTruncationException exception)
                {
                    check(exception.Diagnostics.WasTruncated, $"strict mode retains truncation evidence: {id}");
                    strictRejections++;
                }
            }
            compared++;
        }
        check(compared == 38 && policyDifferences == 4 && referenceFailures == 4 && strictRejections > 0,
            "full input inventory accounts supported inputs, policy differences and reference failures");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { _ = tokenizer.Encode("cancel before encoding", 1024, cancellationToken: cancelled.Token); throw new InvalidOperationException("Tokenizer ignored pre-cancellation."); }
        catch (OperationCanceledException) { check(true, "tokenizer observes cancellation before allocating a prompt"); }

        const string compatibleRequest = """
            {"model":"fixture","length_policy":"laya_compatible","state":"small","questions":{"decision":{"type":"boolean","instructions":"ready"}}}
            """;
        var parsed = DecisionRequestParser.Parse(Encoding.UTF8.GetBytes(compatibleRequest));
        check(parsed.LengthPolicy == PromptLengthPolicy.LayaCompatible && parsed.Questions["decision"] is BooleanQuestion { Criteria: null },
            "source-generated request JSON retains explicit length policy and default boolean criteria");
        var serialized = JsonSerializer.Serialize(parsed, DecisionJsonContext.Default.DecisionRequest);
        check(serialized.Contains("laya_compatible", StringComparison.Ordinal), "length policy round-trips with the frozen JSON name");

        using var demo = new DecisionEngine(DemoModelFactory.CreateTiny());
        var demoRequest = parsed with { Model = "sezika-demo-tiny", LengthPolicy = PromptLengthPolicy.Strict };
        var demoAnswer = demo.Evaluate(demoRequest).Answers["decision"];
        check(demoAnswer is BooleanAnswer boolean && double.IsFinite(boolean.ProbabilityTrue), "demonstration engine accepts optional Boolean criteria");
        try
        {
            _ = demo.Evaluate(demoRequest with { LengthPolicy = PromptLengthPolicy.LayaCompatible });
            throw new InvalidOperationException("Demonstration engine silently ignored a Laya compatibility request.");
        }
        catch (DecisionException exception) when (exception.Code == "decision_length_policy_unsupported")
        {
            check(true, "demonstration engine explicitly rejects unsupported Laya length policy");
        }
    }

    private static Question MapQuestion(JsonElement value)
    {
        var instructions = value.GetProperty("instructions").Clone();
        var hasCriteria = value.TryGetProperty("criteria", out var criteria);
        return value.GetProperty("type").GetString() switch
        {
            "choice" => new ChoiceQuestion { Instructions = instructions,
                Criteria = criteria.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal) },
            "score" => new ScoreQuestion { Instructions = instructions,
                Criteria = criteria.EnumerateArray().Select(item => item.Clone()).ToArray() },
            "noul" => new BooleanQuestion { Instructions = instructions,
                Criteria = hasCriteria ? new BooleanCriteria
                {
                    WhenFalse = criteria.TryGetProperty("false", out var no) ? no.Clone() : default,
                    WhenTrue = criteria.TryGetProperty("true", out var yes) ? yes.Clone() : default,
                } : null,
                Labels = value.TryGetProperty("labels", out var labels) ? new BooleanLabels
                {
                    WhenFalse = labels.GetProperty("false").GetString()!, WhenTrue = labels.GetProperty("true").GetString()!,
                } : null },
            _ => throw new InvalidDataException("Unknown captured primitive."),
        };
    }
}
