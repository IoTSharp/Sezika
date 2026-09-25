using System.Globalization;
using System.Text.Json;

namespace Sezika.Tests;

/// <summary>
/// Checks JSON prompt formatting, numeric representation, Unicode preservation
/// and input limits. Runs at most 32 formatting examples within ten seconds.
/// </summary>
internal static class PromptJsonChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = deadline.Token;
        (string Name, string Json, string Expected)[] examples =
        [
            ("integer and float types, negative zeros, exponent notation",
                "[1,1.0,1e0,-0,-0.0,1e-5,1e16,9007199254740993]",
                "[1, 1.0, 1.0, 0, -0.0, 1e-05, 1e+16, 9007199254740993]"),
            ("fixed/scientific notation transition at minus four and sixteen",
                "[1e-4,1e-5,1e15,1e16,-1e-4,-1e-5,-1e15,-1e16]",
                "[0.0001, 1e-05, 1000000000000000.0, 1e+16, -0.0001, -1e-05, -1000000000000000.0, -1e+16]"),
            ("trailing mantissa zeros do not lose decimal position",
                "[100.0,1200.0,1.2300,0.001200,100.01,100.001,0.00010001]",
                "[100.0, 1200.0, 1.23, 0.0012, 100.01, 100.001, 0.00010001]"),
            ("JSON float parses to binary64 before shortest formatting",
                "[1.0000000000000001,9007199254740993.0,1.234567890123456789,1000000000000000100.0]",
                "[1.0, 9007199254740992.0, 1.2345678901234567, 1.0000000000000001e+18]"),
            ("large integers bypass Int64 and binary64",
                "[1234567890123456789012345678901234567890,-1234567890123456789012345678901234567890]",
                "[1234567890123456789012345678901234567890, -1234567890123456789012345678901234567890]"),
            ("finite binary64 extremes and subnormal",
                "[5e-324,2.2250738585072014e-308,1.7976931348623157e308]",
                "[5e-324, 2.2250738585072014e-308, 1.7976931348623157e+308]"),
            ("underflow and overflow retain CPython signs and spellings",
                "[1e-324,-1e-324,1e309,-1e309,-0e99]",
                "[0.0, -0.0, Infinity, -Infinity, -0.0]"),
            ("uppercase input exponent canonicalizes to lowercase",
                "[1.25E+020,-1.25E-005,12.500E+001]",
                "[1.25e+20, -1.25e-05, 125.0]"),
            ("root strings pass through without JSON quotes",
                "\"用户说：\\\"是\\\"\\n路径\\\\a\"",
                "用户说：\"是\"\n路径\\a"),
            ("structured state preserves property order and Unicode",
                """{"z":"é","甲":"🚀","a":true,"n":null,"v":[1,"两",false]}""",
                """{"z": "é", "甲": "🚀", "a": true, "n": null, "v": [1, "两", false]}"""),
            ("structured instructions use JSON instead of Python repr",
                """{"问题":"确认签收？","阈值":1e-5,"限定":{"允许":false}}""",
                """{"问题": "确认签收？", "阈值": 1e-05, "限定": {"允许": false}}"""),
            ("structured criteria use the same JSON separators",
                """[{"描述":"已签收","分":1.0},false,0,""]""",
                """[{"描述": "已签收", "分": 1.0}, false, 0, ""]"""),
            ("escaped unicode is decoded before ensure_ascii false rendering",
                """{"\u7532":"\u00e9\ud83d\ude80"}""",
                """{"甲": "é🚀"}"""),
            ("JSON punctuation escapes remain minimal and lowercase",
                """["\u0000\u001f\b\f\n\r\t\"\\/"]""",
                """["\u0000\u001f\b\f\n\r\t\"\\/"]"""),
            ("HTML and line separator characters remain Unicode text",
                "[\"<>&='\u2028\u2029\"]",
                "[\"<>&='\u2028\u2029\"]"),
            ("mask literal sanitization occurs after JSON rendering",
                """{"<mask>":"<mask>文本"}""",
                """{"<mask>": "<mask>文本"}"""),
            ("empty structures retain their type", "[{},[],\"\",null,true,false]", "[{}, [], \"\", null, true, false]"),
        ];
        if (examples.Length > 32) throw new InvalidOperationException("JSON contract example budget exceeded.");
        foreach (var example in examples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(example.Json);
            var actual = PromptJsonText.Render(document.RootElement, 16_384, cancellationToken);
            if (!string.Equals(actual, example.Expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"JSON prompt contract: {example.Name}. Expected {example.Expected}; actual {actual}.");
            check(true, "JSON prompt: " + example.Name);
        }

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            using var number = JsonDocument.Parse("[1234.5,1e-5]");
            check(PromptJsonText.Render(number.RootElement, 128, cancellationToken) == "[1234.5, 1e-05]",
                "JSON prompt numeric punctuation does not depend on process culture");
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }

        using var rootString = JsonDocument.Parse("\"abc\"");
        check(PromptJsonText.Render(rootString.RootElement, 3, cancellationToken) == "abc",
            "JSON prompt root string accepts the exact character limit");
        ExpectDecisionFailure("decision_input_limit_exceeded",
            () => PromptJsonText.Render(rootString.RootElement, 2, cancellationToken), check);

        using var nestedString = JsonDocument.Parse("[\"abc\"]");
        check(PromptJsonText.Render(nestedString.RootElement, 7, cancellationToken) == "[\"abc\"]",
            "JSON prompt structured character limit includes syntax");
        ExpectDecisionFailure("decision_input_limit_exceeded",
            () => PromptJsonText.Render(nestedString.RootElement, 6, cancellationToken), check);

        using var duplicate = JsonDocument.Parse("{\"a\":1,\"a\":2}");
        ExpectDecisionFailure("decision_duplicate_property",
            () => PromptJsonText.Render(duplicate.RootElement, 128, cancellationToken), check);

        using var deep = JsonDocument.Parse(new string('[', 33) + "0" + new string(']', 33),
            new JsonDocumentOptions { MaxDepth = 64 });
        ExpectDecisionFailure("decision_input_depth_exceeded",
            () => PromptJsonText.Render(deep.RootElement, 128, cancellationToken), check);
        ExpectDecisionFailure("decision_prompt_value_invalid",
            () => PromptJsonText.Render(default, 128, cancellationToken), check);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            _ = PromptJsonText.Render(rootString.RootElement, 128, cancelled.Token);
            throw new InvalidOperationException("Prompt JSON rendering ignored cancellation.");
        }
        catch (OperationCanceledException) { check(true, "JSON prompt rendering respects cancellation"); }

        // Invalid labels and character budgets are rejected during rendering,
        // before the encoder or decision head runs.
        var tokenizer = new TokenizerJson(".artifacts/models/laya-mmbert/tokenizer/tokenizer.json", cancellationToken);
        using var shortText = JsonDocument.Parse("\"ready\"");
        var nullLabelQuestion = new BooleanQuestion
        {
            Instructions = shortText.RootElement.Clone(),
            Labels = new BooleanLabels { WhenFalse = null!, WhenTrue = "yes" },
        };
        ExpectDecisionFailure("decision_boolean_labels_invalid",
            () => PromptSequenceBuilder.Build(tokenizer, shortText.RootElement, nullLabelQuestion,
                cancellationToken: cancellationToken), check);

        using var longDescription = JsonDocument.Parse("\"" + new string('a', 32) + "\"");
        var candidatesOverBudget = new ChoiceQuestion
        {
            Instructions = shortText.RootElement.Clone(),
            Criteria = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["a"] = longDescription.RootElement.Clone(),
                ["b"] = longDescription.RootElement.Clone(),
            },
        };
        var smallBudget = new PromptSequenceOptions { MaxInputCharacters = 64 };
        ExpectDecisionFailure("decision_input_limit_exceeded",
            () => PromptSequenceBuilder.Build(tokenizer, shortText.RootElement, candidatesOverBudget, smallBudget, cancellationToken), check);

        var labelOverBudget = new ChoiceQuestion
        {
            Instructions = shortText.RootElement.Clone(),
            Criteria = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [new string('a', 65)] = shortText.RootElement.Clone(),
            },
        };
        ExpectDecisionFailure("decision_input_limit_exceeded",
            () => PromptSequenceBuilder.Build(tokenizer, shortText.RootElement, labelOverBudget, smallBudget, cancellationToken), check);
    }

    private static void ExpectDecisionFailure(string code, Action action, Action<bool, string> check)
    {
        try { action(); throw new InvalidOperationException($"Prompt JSON did not reject {code}."); }
        catch (DecisionException exception) when (exception.Code == code)
        {
            check(true, "JSON prompt rejects " + code);
        }
    }
}
