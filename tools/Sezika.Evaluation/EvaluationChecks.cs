using System.Text.Json;
using Sezika;

internal static class EvaluationChecks
{
    public static int Run()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var passed = 0;
        void Check(bool value, string label)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (!value) throw new InvalidDataException(label);
            Console.WriteLine($"PASS: {label}");
            passed++;
        }
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (Exception exception) when (exception is InvalidDataException or DecisionException)
            { Check(true, label); return; }
            throw new InvalidDataException("Expected rejection: " + label);
        }
        try
        {
            using var boolean = JsonDocument.Parse("""{"state":"ready","questions":{"decision":{"type":"noul","instructions":"ready","criteria":{"true":"ready"},"labels":{"false":"no","true":"yes"}}}}""");
            var mapped = EvaluationInputs.Request(boolean.RootElement, "fixture", PromptLengthPolicy.LayaCompatible);
            Check(mapped.LengthPolicy == PromptLengthPolicy.LayaCompatible && mapped.Questions["decision"] is BooleanQuestion
                { Criteria.WhenFalse.ValueKind: JsonValueKind.Undefined, Labels.WhenTrue: "yes" }, "explicit policy, optional false criterion and display labels are preserved");
            using var choices = JsonDocument.Parse("""{"state":"ready","questions":{"decision":{"type":"choice","instructions":"pick","criteria":{"z":"first","a":"second"}}}}""");
            var choice = EvaluationInputs.Request(choices.RootElement, "fixture", PromptLengthPolicy.Strict);
            Check(((ChoiceQuestion)choice.Questions["decision"]).Criteria.Keys.SequenceEqual(["z", "a"]), "candidate insertion order is preserved");
            using var lateType = JsonDocument.Parse("""{"state":"ready","questions":{"decision":{"instructions":"pick","criteria":{"z":"first","a":"second"},"type":"choice"}}}""");
            Check(EvaluationInputs.Request(lateType.RootElement, "fixture", PromptLengthPolicy.Strict).Questions["decision"] is ChoiceQuestion,
                "dataset type discriminator can occur after ordinary question properties");
            Reject(() => { using var ignored = EvaluationInputs.ParseRow("{\"id\":\"a\",\"id\":\"b\"}", deadline.Token); }, "duplicate dataset fields fail");
            using var extra = JsonDocument.Parse("""{"state":"x","questions":{"decision":{"type":"noul","instructions":"x"},"ignored":{"type":"noul","instructions":"y"}}}""");
            Reject(() => EvaluationInputs.Request(extra.RootElement, "fixture", PromptLengthPolicy.Strict), "extra questions cannot silently disappear");
            using var unknown = JsonDocument.Parse("""{"state":"x","questions":{"decision":{"type":"noul","instructions":"x","criteria":{"maybe":"x"}}}}""");
            Reject(() => EvaluationInputs.Request(unknown.RootElement, "fixture", PromptLengthPolicy.Strict), "unknown Boolean criterion fails");

            var calibration = new CalibrationInfo { Status = "uncalibrated" };
            EvaluationRow BooleanRow(string id, string target, double probability, bool abstained = false) => Evaluation.ScoreAnswer(
                id, id, "original", "fixture", "noul", target, new BooleanAnswer
                { Status = abstained ? "abstained" : "answered", AbstentionReason = abstained ? "fixture" : null,
                    Calibration = calibration, ProbabilityTrue = probability }, 8, 0);
            var rows = new[] { BooleanRow("tp", "true", 0.8), BooleanRow("tn", "false", 0.2),
                BooleanRow("fp", "false", 0.7), BooleanRow("fn", "true", 0.3), BooleanRow("abstained", "true", 0.9, true) };
            var metrics = BooleanMetrics.Compute(rows, deadline.Token);
            Check(metrics is { TruePositive: 1, TrueNegative: 1, FalsePositive: 1, FalseNegative: 1, Answered: 4, Unanswered: 1 }
                && metrics.AurocOnAnswered == 0.75 && metrics.Coverage == 0.8, "confusion, tied-rank AUROC denominator and abstention coverage");
            Check(BooleanRow("threshold", "true", 0.5).Selected == "true", "Boolean threshold equality chooses true");
            var score = Evaluation.ScoreAnswer("score", "score", "original", "fixture", "score", "1", new ScoreAnswer
            { Status = "answered", Calibration = calibration, Score = 0.75, Legend = [],
                Probabilities = new() { ["0"] = 0.25, ["1"] = 0.75 }, Concentration = 0 }, 8, 0);
            Check(score.Selected == "1" && score.ExpectedScore == 0.75 && score.Brier == 0.125,
                "Score uses zero-based expectation and categorical Brier");
            var tied = new[] { BooleanRow("positive", "true", 0.5), BooleanRow("negative", "false", 0.5) };
            Check(BooleanMetrics.Compute(tied, deadline.Token).AurocOnAnswered == 0.5, "AUROC gives half credit to ties");
            Console.WriteLine($"Evaluation checks passed: {passed}; synthetic mechanics only, no model quality measurement.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
    }
}
