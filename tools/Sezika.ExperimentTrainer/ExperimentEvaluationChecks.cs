using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.ExperimentTrainer;

internal static class ExperimentEvaluationChecks
{
    internal static void Run(string[] arguments, CancellationToken token)
    {
        if (arguments.Length != 11) throw new ArgumentException("check-evaluation requires --data --manifest-sha256 --source --report-sha256 --max-checks.");
        using var budget = new ExperimentBudget(30, token); var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < 11; index += 2)
        {
            budget.Check();
            if (arguments[index] is not ("--data" or "--manifest-sha256" or "--source" or "--report-sha256" or "--max-checks") ||
                arguments[index + 1].Length > 2048 || !values.TryAdd(arguments[index], arguments[index + 1])) throw ExperimentData.Invalid("Unknown/duplicate evaluation check option.");
        }
        if (!int.TryParse(values["--max-checks"], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is not (1 or 6))
            throw ExperimentData.Invalid("Evaluation checks require tiny1 or complete6.");
        using var data = ExperimentData.Open(values["--data"], values["--manifest-sha256"], budget.Check, budget.Token);
        var source = Path.GetFullPath(values["--source"]); var reportHash = values["--report-sha256"]; ExperimentData.RequireHash(reportHash);
        var choices = data.Records.Where(row => row.Split == "train" && row.Type == MarkerQuestionType.Choice).ToArray();
        var tests = new (string Name, Action Run)[]
        {
            ("fixed-left1-id-and-text-move-teacher-target-remaps-no-source-mutation", () =>
            {
                Assert(choices.Length == 8, "Exactly eight training Choice sources.");
                for (var index = 0; index < (count == 1 ? 1 : 8) && index < 8; index++)
                {
                    budget.Check(); var original = choices[index]; var beforeIds = original.CandidateIds.ToArray();
                    var originalChoice = (IndependentChoiceQuestion)original.Question; var beforeCandidates = originalChoice.Candidates.ToArray();
                    var changed = ExperimentOperations.Rotate(original, budget.Check); var choice = (IndependentChoiceQuestion)changed.Question;
                    for (var candidate = 0; candidate < 4; candidate++)
                    {
                        budget.Check(); Assert(choice.Candidates[candidate] == beforeCandidates[(candidate + 1) % 4], "Candidate ID and text move as one object.");
                        Assert(changed.CandidateIds[candidate] == beforeIds[(candidate + 1) % 4], "Fixed left-one IDs.");
                    }
                    Assert(changed.CandidateIds[changed.TargetIndex] == original.CandidateIds[original.TargetIndex], "Teacher target follows ID.");
                    Assert(changed.Request.Questions.Count == 1 && changed.Request.Questions[0] == changed.Question, "Request renderer observes moved question.");
                    Assert(original.CandidateIds.SequenceEqual(beforeIds) && originalChoice.Candidates.SequenceEqual(beforeCandidates), "Source records unchanged.");
                }
            }),
            ("old-v1-report-original-byte-sha-and-disk-head-read-compatible", () =>
            {
                var old = ExperimentOperations.ReadTrainingReport(source, reportHash, budget.Check, budget.Token);
                var head = IndependentMarkerHeadAssetStore.Load(Path.Combine(source, ExperimentOperations.HeadName), old.Identity, old.FeatureSha256, old.HeadSha256, budget.Token);
                Assert(head.CompletedSteps == old.CompletedSteps && head.Seed == old.Seed && head.LearningRate == old.LearningRate && head.LastLoss == old.LastStepPreUpdateSelectedExampleLoss, "Disk head metadata.");
                Assert(ExperimentData.SameHash(ExperimentData.Hash(MemoryMarshal.AsBytes(head.Head.CopyWeights().AsSpan())), old.ParameterSha256), "Old head raw F32 parameter SHA.");
                using var stream = new FileStream(Path.Combine(source, ExperimentOperations.ReportName), FileMode.Open, FileAccess.Read, FileShare.Read);
                Assert(ExperimentData.SameHash(ExperimentData.Hash(ExperimentData.Snapshot(stream, 1_048_576, budget.Check)), reportHash), "Old report remains pinned original bytes.");
            }),
            ("fixed-cli-budgets-and-unknown-options-rejected", () =>
            {
                string[] train = ["train", "--data", data.Root, "--manifest-sha256", data.ManifestSha256, "--package", source, "--output", source + "-not-created", "--steps", "768", "--max-tokens", "1024", "--diagnostics", "fixed-left1"];
                var parsed = ExperimentOptions.Parse(train, budget.Token);
                Assert(parsed.Steps == 768 && parsed.Seed == 17 && parsed.LearningRate == 0.001f && parsed.MaxTokens == 1024 && parsed.TrainingSeconds == 300 && parsed.TimeoutSeconds == 460, "Fixed predeclared CLI.");
                Rejects(() => ExperimentOptions.Parse([.. train, "--seed", "18"], budget.Token));
                Rejects(() => ExperimentOptions.Parse([.. train, "--timeout-seconds", "461"], budget.Token));
                Rejects(() => ExperimentOptions.Parse([.. train, "--training-seconds", "301"], budget.Token));
                Rejects(() => ExperimentOptions.Parse([.. train, "--resume", "yes"], budget.Token));
                string[] evaluate = ["evaluate", "--data", data.Root, "--manifest-sha256", data.ManifestSha256, "--package", source, "--output", source, "--report-sha256", reportHash, "--evaluation-output", source + "-not-created.json"];
                Assert(ExperimentOptions.Parse(evaluate, budget.Token).TimeoutSeconds == 460, "Read-only evaluate bounded default.");
                Rejects(() => ExperimentOptions.Parse([.. evaluate, "--max-tokens", "1024"], budget.Token));
            }),
            ("synthetic-contract-fixture-full-denominator-source-generated-roundtrip-tie-rank", () =>
            {
                var fixture = Fixture(data, budget.Check);
                ExperimentEvaluationReport.ValidateObservations(fixture, 8, ParameterFixture, budget.Check);
                Assert(fixture.Rows.All(row => row.TeacherTargetRank == 1), "Competition rank shares first rank at all-zero logit tie.");
                Assert(fixture.Rows.All(row => row.PredictedIndex == 0), "Prediction tie follows first rendered order.");
                var old = ExperimentOperations.ReadTrainingReport(source, reportHash, budget.Check, budget.Token);
                var fixtureReport = new ExperimentEvaluationReport(ExperimentEvaluationReport.EvaluationContract, "completed_experiment_not_formal_acceptance",
                    "new_head_disk_loaded_same_process", "teacher_agreement_on_exposed_unreviewed_train_and_development_diagnostic_not_gold_accuracy",
                    "competition_rank_1_plus_count_of_strictly_greater_logits_ties_share_rank", "first_candidate_in_rendered_order", "host_stable_softmax_of_gpu_fp32_logits_not_calibrated_accuracy",
                    reportHash, old.HeadSha256, old.CheckpointSha256, ParameterFixture, data.ManifestSha256, data.RecordsSha256, data.ProvenanceSha256, data.Root,
                    old.Identity, old.CudaArtifacts, old.Device, 768, 1024, 72, 48, 48, 0, 0, null, null, fixture, new(), old.Resources);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(fixtureReport, ExperimentJsonContext.Default.ExperimentEvaluationReport);
                var loaded = JsonSerializer.Deserialize(bytes, ExperimentJsonContext.Default.ExperimentEvaluationReport) ?? throw ExperimentData.Invalid("Fixture source-generated JSON missing.");
                loaded.Validate(budget.Check); ExperimentOperations.RequireObservationsMatch(fixture, loaded.Observations, budget.Check);
                Rejects(() => JsonSerializer.Deserialize(Encoding.UTF8.GetBytes("{\"unrecognized\":true," + Encoding.UTF8.GetString(bytes)[1..]), ExperimentJsonContext.Default.ExperimentEvaluationReport));
            }),
            ("synthetic-contract-fixture-rejects-tampered-logits-probability-ce-feature-device-parameter-target", () =>
            {
                var fixture = Fixture(data, budget.Check); var row = fixture.Rows[0];
                void RejectRow(ExperimentEvaluationRow changed)
                {
                    var rows = fixture.Rows.ToArray(); rows[0] = changed;
                    Rejects(() => ExperimentOperations.RequireObservationsMatch(fixture, fixture with { Rows = rows }, budget.Check));
                }
                RejectRow(row with { TeacherTargetRank = 2 });
                RejectRow(row with { TeacherTargetId = "different-id" });
                RejectRow(row with { FeatureSha256 = new string('D', 64) });
                RejectRow(row with { Logits = row.Logits.Select((value, index) => index == 0 ? value + 0.1 : value).ToArray() });
                RejectRow(row with { Probabilities = row.Probabilities.Select((value, index) => index == 0 ? value + 0.1 : value).ToArray() });
                RejectRow(row with { GpuTeacherCe = row.GpuTeacherCe + 0.1 });
                Rejects(() => ExperimentOperations.RequireObservationsMatch(fixture, fixture with { Train = fixture.Train with { DeviceParameterSha256 = new string('D', 64) } }, budget.Check));
                Rejects(() => ExperimentOperations.RequireObservationsMatch(fixture, fixture with { Train = fixture.Train with { FeatureSha256 = new string('D', 64) } }, budget.Check));
                Rejects(() => ExperimentOperations.RequireObservationsMatch(fixture, fixture with { Rows = fixture.Rows.Take(39).ToArray() }, budget.Check));
            }),
            ("read-only-output-under-source-and-cancelled-work-rejected-no-fixture-files", () =>
            {
                Rejects(() => ExperimentOperations.RequireEvaluationOutput(Path.Combine(source, "must-not-write.json"), source, budget.Check));
                Rejects(() => ExperimentOperations.RequireEvaluationOutput(Path.Combine(source, ExperimentOperations.ReportName), source, budget.Check));
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                try { ExperimentOperations.Rotate(choices[0], () => cancelled.Token.ThrowIfCancellationRequested()); }
                catch (OperationCanceledException) { return; }
                throw new InvalidOperationException("Cancelled diagnostic contract work ignored cancellation.");
            }),
        };
        for (var index = 0; index < count && index < 6; index++)
        {
            budget.Check(); Console.WriteLine($"evaluation contract check {index + 1}/{count}: {tests[index].Name}"); tests[index].Run(); Console.WriteLine("PASS: " + tests[index].Name);
        }
        Console.WriteLine($"PASS: {count} bounded CPU input/serialization/disk-read contracts; synthetic_contract_fixture_only=true; model/encoder/GPU/training/quality not executed; no fixture files created");
    }
    private static readonly string ParameterFixture = new('A', 64);
    private static ExperimentEvaluationObservations Fixture(ExperimentData data, Action check)
    {
        var originals = new List<ExperimentEvaluationRow>(32); var rotations = new List<ExperimentEvaluationRow>(8);
        for (var index = 0; index < 32; index++)
        {
            check(); var row = data.Records[index]; originals.Add(Row(row, row, "original", null));
            if (row.Split == "train" && row.Type == MarkerQuestionType.Choice)
                rotations.Add(Row(row, ExperimentOperations.Rotate(row, check), "fixed-left1", originals[^1]));
        }
        var rows = originals.Where(row => row.Split == "train").Concat(originals.Where(row => row.Split == "development")).Concat(rotations).ToArray();
        var development = rows.Where(row => row.Split == "development").ToArray(); var agreed = development.Count(row => row.AgreesWithTeacher);
        var pipeline = new ExperimentInference("cuda-independent-linear-marker-head", "teacher_agreement_on_exposed_unreviewed_development_not_gold_accuracy", 8, 8, agreed, agreed / 8d,
            development.Average(row => row.StableSoftmaxTeacherNll), development.Select(row => new ExperimentPrediction(row.RecordId, row.Language, row.Type, row.CandidateIds,
                row.TeacherTargetIndex, row.PredictedIndex, row.SelectedCandidateId, row.Logits, row.Probabilities, row.StableSoftmaxTeacherNll, row.AgreesWithTeacher)).ToArray());
        return new(Loss("train", "original"), Loss("development", "original"), Loss("train", "fixed-left1"), pipeline,
            rows, ExperimentOperations.Groups(rows, check), ExperimentOperations.PositionComparisons(rows, check));
        ExperimentLoss Loss(string split, string variant)
        {
            check(); var scoped = rows.Where(row => row.Split == split && row.Variant == variant).ToArray();
            return new(split + "/" + variant, Sezika.Cuda.IndependentCudaMarkerHeadEvaluator.Backend, scoped.Length, scoped.Length,
                scoped.Average(row => row.GpuTeacherCe), new string('B', 64), ParameterFixture, ParameterFixture);
        }
        ExperimentEvaluationRow Row(ExperimentRecord source, ExperimentRecord rendered, string variant, ExperimentEvaluationRow? original)
        {
            check(); var count = rendered.CandidateIds.Length; int[] tokens = variant == "original" ? [1, 2, 3, 4, 5, 6] : [1, 2, 3, 4, 5, 9];
            var markers = Enumerable.Range(0, count).ToArray(); var logits = new double[count]; var probs = Enumerable.Repeat(1d / count, count).ToArray();
            var tokenHash = ExperimentOperations.IntHash(tokens); var markerHash = ExperimentOperations.IntHash(markers); var featureHash = new string('C', 64);
            var texts = rendered.Question switch
            {
                IndependentChoiceQuestion choice => choice.Candidates.Select(candidate => candidate.Text).ToArray(),
                IndependentScoreQuestion score => score.Levels.ToArray(),
                IndependentBooleanQuestion boolean => new[] { boolean.WhenFalse, boolean.WhenTrue },
                _ => throw new InvalidOperationException(),
            };
            return new(source.RecordId, source.Split, source.Language, source.Type.ToString().ToLowerInvariant(), variant, source.CandidateIds, rendered.CandidateIds,
                texts.Select(text => ExperimentData.Hash(Encoding.UTF8.GetBytes(text))).ToArray(), source.CandidateIds[source.TargetIndex], source.TargetIndex, rendered.TargetIndex, 1,
                0, rendered.CandidateIds[0], rendered.TargetIndex == 0, logits, probs, Math.Log(count), Math.Log(count), tokens, markers,
                tokenHash, markerHash, featureHash, original?.TokenSha256 ?? tokenHash, original?.MarkerSha256 ?? markerHash, original?.FeatureSha256 ?? featureHash);
        }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Rejects(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is DecisionException or ArgumentException or JsonException or IOException) { return; }
        throw new InvalidOperationException("Tampered/invalid evaluation contract was not rejected.");
    }
}
