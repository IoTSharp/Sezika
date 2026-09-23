using System.Text.Json;
using Sezika;

namespace Sezika.Tests;

public static class EncoderExecutionChecks
{
    public static bool Run()
    {
        var input = Enumerable.Range(0, 10).Select(value => value * 0.1f).ToArray();
        var weights = Enumerable.Range(0, 15).Select(value => (value - 7) * 0.03f).ToArray();
        var scalar = ScalarOps.Linear(input, weights, null, 2, 5, 3, CancellationToken.None);
        var simd = SimdOps.Linear(input, weights, null, 2, 5, 3, CancellationToken.None);
        if (scalar.Zip(simd, (left, right) => Math.Abs(left - right)).Max() > 1e-5f) return false;

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { _ = SimdOps.Linear(input, weights, null, 2, 5, 3, cancelled.Token); return false; }
        catch (OperationCanceledException) { }

        var config = new ModernBertConfig
        {
            VocabularySize = 32, HiddenSize = 4, IntermediateSize = 8, LayerCount = 1,
            HeadCount = 2, MaxTokens = 32,
        };
        var options = new EncoderExecutionOptions
        {
            MaxConcurrentRequests = 1,
            MaxWorkspaceBytes = EncoderWorkspacePool.EstimateBytes(config, 4) * 2,
            Deadline = TimeSpan.FromSeconds(1),
        };
        using var pool = new EncoderWorkspacePool(options);
        using var first = pool.Acquire(4, config);
        try { _ = pool.Acquire(4, config); return false; }
        catch (DecisionException exception) when (exception.Code == "encoder_session_busy") { }
        first.Dispose();
        return pool.ActiveCount == 0 && pool.OutstandingBytes == 0;
    }
}

public static class PrimitiveAlignmentChecks
{
    public static bool Run()
    {
        var fixturePath = Path.Combine("tests", "fixtures", "primitive-reference-synthetic.json");
        if (!File.Exists(fixturePath)) return false;
        var loaded = PrimitiveAlignment.LoadFixture(File.ReadAllBytes(fixturePath));
        if (loaded.Cases.Count != 4 || loaded.Cases.Any(item => item.Input.State.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)) return false;
        var probabilities = DecisionMath.Softmax([0.5f, -0.5f]);
        var answer = new ChoiceAnswer
        {
            Status = "answered",
            Calibration = new CalibrationInfo { Status = "uncalibrated" },
            Choice = "en",
            Concentration = DecisionMath.Concentration(probabilities),
            Logits = new Dictionary<string, double>(StringComparer.Ordinal) { ["en"] = 0.5, ["zh"] = -0.5 },
            Probabilities = new Dictionary<string, double>(StringComparer.Ordinal) { ["en"] = probabilities[0], ["zh"] = probabilities[1] },
        };
        var fixture = new PrimitiveReferenceFixture
        {
            SchemaVersion = "sezika.primitive-reference.v1",
            ModelRevision = "fixture-revision",
            TokenizerRevision = "fixture-tokenizer",
            PromptSchema = "sezika.prompt.v1",
            Cases = [new PrimitiveReferenceCase
            {
                CaseId = "case-en-1", Language = "en", Primitive = "choice",
                ModelRevision = "fixture-revision", TokenizerRevision = "fixture-tokenizer",
                Input = new PrimitiveReferenceInput
                {
                    State = JsonDocument.Parse("{\"request\":\"choose billing\"}").RootElement.Clone(),
                    Instructions = JsonDocument.Parse("\"route the request\"").RootElement.Clone(),
                    Criteria = JsonDocument.Parse("{\"en\":\"billing\",\"zh\":\"support\"}").RootElement.Clone(),
                },
                Logits = new Dictionary<string, double>(answer.Logits),
                Probabilities = new Dictionary<string, double>(answer.Probabilities),
                Status = "answered",
            }],
        };
        var response = new DecisionResponse
        {
            Model = "fixture", ModelRevision = "fixture-revision", TokenizerRevision = "fixture-tokenizer", Backend = "cpu",
            Answers = new Dictionary<string, Answer>(StringComparer.Ordinal) { ["case-en-1"] = answer },
        };
        var report = PrimitiveAlignment.Compare(fixture, response, 1e-12);
        if (!report.Passed || report.ComparedCount != 1 || report.MaxLogitError != 0d)
            return false;

        // Exercise the checked-in bilingual fixture, including score legend and
        // boolean abstention fields, so this remains a four-case reference run
        // instead of only testing an in-memory single choice.
        var bilingualFixturePath = Path.Combine("tests", "fixtures", "primitive-reference-synthetic.json");
        if (!File.Exists(bilingualFixturePath)) return false;
        var bilingual = PrimitiveAlignment.LoadFixture(File.ReadAllBytes(bilingualFixturePath));
        var answers = new Dictionary<string, Answer>(StringComparer.Ordinal);
        foreach (var expected in bilingual.Cases)
        {
            var calibration = new CalibrationInfo { Status = "uncalibrated" };
            var bilingualProbabilities = new Dictionary<string, double>(expected.Probabilities, StringComparer.Ordinal);
            var logits = new Dictionary<string, double>(expected.Logits, StringComparer.Ordinal);
            switch (expected.Primitive)
            {
                case "choice":
                    answers[expected.CaseId] = new ChoiceAnswer
                    {
                        Status = expected.Status, AbstentionReason = expected.AbstentionReason, Calibration = calibration,
                        Choice = bilingualProbabilities.OrderByDescending(pair => pair.Value).First().Key,
                        Probabilities = bilingualProbabilities, Logits = logits,
                        Concentration = DecisionMath.Concentration(bilingualProbabilities.Values.ToArray()),
                    };
                    break;
                case "score":
                    answers[expected.CaseId] = new ScoreAnswer
                    {
                        Status = expected.Status, AbstentionReason = expected.AbstentionReason, Calibration = calibration,
                        Score = bilingualProbabilities.Select((pair, index) => pair.Value * index).Sum(),
                        Legend = new Dictionary<string, JsonElement>(expected.Legend!, StringComparer.Ordinal),
                        Probabilities = bilingualProbabilities, Logits = logits,
                        Concentration = DecisionMath.Concentration(bilingualProbabilities.Values.ToArray()),
                    };
                    break;
                case "boolean":
                    answers[expected.CaseId] = new BooleanAnswer
                    {
                        Status = expected.Status, AbstentionReason = expected.AbstentionReason, Calibration = calibration,
                        ProbabilityTrue = bilingualProbabilities["true"], Logits = logits,
                    };
                    break;
                default:
                    return false;
            }
        }
        var bilingualResponse = new DecisionResponse
        {
            Model = "fixture", ModelRevision = bilingual.ModelRevision, TokenizerRevision = bilingual.TokenizerRevision,
            Backend = "cpu", Answers = answers,
        };
        var bilingualReport = PrimitiveAlignment.Compare(bilingual, bilingualResponse, 1e-12);
        return bilingualReport.Passed && bilingualReport.CaseCount == 4 && bilingualReport.ComparedCount == 4 &&
            bilingualReport.MaxLogitError <= 1e-12 && bilingualReport.MaxProbabilityError <= 1e-12;
    }
}

public static class CalibrationProfileChecks
{
    public static bool Run()
    {
        var path = Path.Combine("data", "s4-03", "calibration-profiles.json");
        if (!File.Exists(path)) return false;
        var binding = new CalibrationProfileBinding
        {
            ModelId = "convaiinnovations/laya-multilingual",
            ModelRevision = "052592a15d198d9ad47da779604259b10b47b7aa",
            ModelWeightsSha256 = "9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204",
            TokenizerRevision = "052592a15d198d9ad47da779604259b10b47b7aa",
            TokenizerSha256 = "609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f",
            PromptSchemaId = "sezika.prompt.v1",
            PromptSchemaSha256 = "76e551b8b13a49debae6641af72241c052e8db6111dba0e82a0bde0aa8f653e5",
            DatasetManifestSha256 = "a3b26f4a62d8cc26c1b5a5b680255b6f425458a7015c1013fc42878490d5485d",
        };
        var manifest = CalibrationProfileManifestLoader.LoadAndValidate(path, binding);
        if (manifest.Status != "pending_measurement" || manifest.Profiles.Length != 12 ||
            manifest.Profiles.Any(profile => profile.Status != "pending_measurement"))
            return false;

        // A profile may only become verified after fitting and an independent
        // test metric snapshot that satisfies every frozen gate.
        try
        {
            (manifest.Profiles[0] with { Status = "verified", FitStatus = "not_fitted" })
                .Validate(binding, manifest.FrozenGates);
            return false;
        }
        catch (DecisionException exception) when (exception.Code == "decision_calibration_not_ready")
        {
        }

        try
        {
            (manifest.Profiles[0] with
            {
                Status = "verified",
                FitStatus = "fitted",
                ObservedMetrics = new CalibrationMetricSnapshot
                {
                    Count = 1, Accuracy = 1, MacroF1 = 1, NegativeLogLikelihood = 0,
                    Brier = 0, ExpectedCalibrationError = 0, Coverage = 1, SelectiveRisk = 0,
                },
            }).Validate(binding, manifest.FrozenGates);
            return false;
        }
        catch (DecisionException exception) when (exception.Code == "decision_calibration_quality_gate_failed")
        {
        }

        return true;
    }
}
