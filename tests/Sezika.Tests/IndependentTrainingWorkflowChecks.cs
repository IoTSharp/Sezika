using System.Text;
using Sezika;
using Sezika.IndependentModelTool;

namespace Sezika.Tests;

internal static class IndependentTrainingWorkflowChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string, string> expectCode)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var identity = new MarkerFeatureIdentity("sezika/workflow-fixture", new string('A', 64), new string('B', 64), new string('C', 64), 2);
        var train = new MarkerFeatureSet(identity,
        [
            Example("train-choice", MarkerQuestionType.Choice, "zh", [[1f, 0f], [0f, 1f]], 0),
            Example("train-score", MarkerQuestionType.Score, "en", [[0f, -1f], [1f, 0f], [0f, 1f]], 2),
            Example("train-boolean", MarkerQuestionType.Boolean, "zh", [[-1f, 0f], [1f, 0f]], 1)
        ]);
        var development = new[] { Example("development-boolean", MarkerQuestionType.Boolean, "en", [[-1f, 0f], [1f, 0f]], 1) };
        var recordsHash = new string('D', 64);
        var two = new MarkerTrainingOptions(17, 2, 0.05f, TimeSpan.FromSeconds(5));
        var four = two with { MaxSteps = 4 };
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"sezika-training-workflow-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(directory);
        TrainingOutputPaths Paths(string name) => new(Path.Combine(directory, name + ".checkpoint"),
            Path.Combine(directory, name + ".head"), Path.Combine(directory, name + ".json"));
        try
        {
            var firstPaths = Paths("first");
            var first = IndependentTrainingWorkflow.Run(train, development, recordsHash, two, firstPaths,
                TrainingFeatureSource.SyntheticFixture, cancellationToken: token);
            var resume = new TrainingResumePaths(firstPaths.Checkpoint, first.Report.CheckpointSha256, firstPaths.Report, first.ReportSha256);
            var resumed = IndependentTrainingWorkflow.Run(train, development, recordsHash, four, Paths("resumed"),
                TrainingFeatureSource.SyntheticFixture, resume, cancellationToken: token);
            var full = IndependentTrainingWorkflow.Run(train, development, recordsHash, four, Paths("full"),
                TrainingFeatureSource.SyntheticFixture, cancellationToken: token);
            check(resumed.State.CompletedSteps == 4 && resumed.Report.InitialSteps == 2 &&
                resumed.State.Head.CopyWeights().SequenceEqual(full.State.Head.CopyWeights()) &&
                resumed.Report.HeadSha256 == full.Report.HeadSha256 && resumed.Report.CheckpointSha256 == full.Report.CheckpointSha256,
                "workflow saved/restored two plus two updates equal uninterrupted four-step head and checkpoint bytes");
            var report = WorkflowTrainingReport.ReadBounded(firstPaths.Report, first.ReportSha256.ToLowerInvariant(), token);
            check(report.SchemaVersion == 2 && report.Resume is null && report.Status == "fixture_contract_only" &&
                !report.UsesRealIndependentEncoder && !report.RepresentsQualityAcceptance && report.Identity.Matches(identity) &&
                report.StepSemantics == "total_completed_steps" && report.DevelopmentGroups.Sum(group => group.Count) == 1,
                "workflow source-generated report is versioned, hash-bound and explicitly fixture-only");
            var boundIdentity = resumed.Report.BindIdentity(identity, token);
            var head = IndependentMarkerHeadAssetStore.Load(Paths("resumed").Head, boundIdentity, resumed.State.FeatureSha256,
                resumed.Report.HeadSha256, token);
            check(boundIdentity == identity && head.Head.CopyWeights().SequenceEqual(full.State.Head.CopyWeights()),
                "workflow full report identity binds actual model contract and exported inference head passes production loader");
            // A different positive hidden_size is syntactically valid v2 JSON.
            // The evaluator's public adapter must reject it before replacing it
            // with the actual model width or attempting the production head load.
            var wrongWidthReport = WorkflowTrainingReport.ParseBounded(Encoding.UTF8.GetBytes(File.ReadAllText(firstPaths.Report)
                .Replace("\"hidden_size\": 2", "\"hidden_size\": 1", StringComparison.Ordinal)), token);
            check(wrongWidthReport.Identity.HiddenSize == 1, "workflow parser preserves syntactically valid report dimension");
            expectCode(() => wrongWidthReport.BindIdentity(identity, token), "training_report_identity_mismatch",
                "workflow evaluation adapter rejects valid v2 report hidden-size mismatch before head load");
            expectCode(() => report.BindIdentity(identity with { Protocol = "different-protocol" }, token), "training_report_identity_mismatch",
                "workflow evaluation adapter compares complete protocol identity");
            expectCode(() => report.BindIdentity(identity with { LengthPolicy = "different-policy" }, token), "training_report_identity_mismatch",
                "workflow evaluation adapter compares complete length policy identity");

            expectCode(() => IndependentTrainingWorkflow.Run(train, development, recordsHash, four, Paths("wrong-hash"),
                TrainingFeatureSource.SyntheticFixture, resume with { CheckpointSha256 = new string('0', 64) }, cancellationToken: token),
                "training_checkpoint_hash_invalid", "workflow rejects wrong expected checkpoint hash");
            expectCode(() => IndependentTrainingWorkflow.Run(train, development, recordsHash, four, Paths("wrong-report"),
                TrainingFeatureSource.SyntheticFixture, resume with { ReportSha256 = new string('0', 64) }, cancellationToken: token),
                "training_report_hash_invalid", "workflow rejects wrong expected report hash");
            expectCode(() => IndependentTrainingWorkflow.Run(train, development, new string('E', 64), four, Paths("changed-records"),
                TrainingFeatureSource.SyntheticFixture, resume, cancellationToken: token),
                "training_resume_mismatch", "workflow binds full records including development split");
            expectCode(() => IndependentTrainingWorkflow.Run(train with { Identity = identity with { DataManifestSha256 = new string('E', 64) } },
                development, recordsHash, four, Paths("changed-manifest"), TrainingFeatureSource.SyntheticFixture, resume, cancellationToken: token),
                "training_resume_mismatch", "workflow binds dataset manifest identity");
            expectCode(() => IndependentTrainingWorkflow.Run(train, development, recordsHash, four with { Seed = 18 }, Paths("changed-seed"),
                TrainingFeatureSource.SyntheticFixture, resume, cancellationToken: token),
                "training_resume_mismatch", "workflow seed is reproducible and fixed on resume");
            expectCode(() => IndependentTrainingWorkflow.Run(train, development, recordsHash, four with { LearningRate = 0.1f }, Paths("changed-rate"),
                TrainingFeatureSource.SyntheticFixture, resume, cancellationToken: token),
                "training_resume_mismatch", "workflow learning rate is fixed on resume");
            expectCode(() => IndependentTrainingWorkflow.Run(train, development, recordsHash, two with { MaxSteps = 1 }, Paths("smaller-total"),
                TrainingFeatureSource.SyntheticFixture, resume, cancellationToken: token),
                "training_resume_mismatch", "workflow cannot resume to a total lower than completed steps");
            expectCode(() => IndependentTrainingWorkflow.Run(train, development, recordsHash, four, firstPaths,
                TrainingFeatureSource.SyntheticFixture, cancellationToken: token),
                "training_output_exists", "workflow preserves existing output files");
            expectCode(() => IndependentTrainingWorkflow.ValidateOutputPaths(Paths("collision") with { Head = Paths("collision").Report }, []),
                "training_output_invalid", "workflow output roles cannot share a path");
            expectCode(() => WorkflowTrainingReport.ParseBounded(Encoding.UTF8.GetBytes(File.ReadAllText(firstPaths.Report)
                .Replace("\"schema_version\": 2", "\"schema_version\": 2, \"schema_version\": 2", StringComparison.Ordinal)), token),
                "training_report_invalid", "workflow rejects duplicate report JSON properties");
            expectCode(() => WorkflowTrainingReport.ParseBounded(Encoding.UTF8.GetBytes(File.ReadAllText(firstPaths.Report)
                .Replace("\"schema_version\": 2", "\"unknown\": true, \"schema_version\": 2", StringComparison.Ordinal)), token),
                "training_report_invalid", "workflow rejects unknown report properties");

            // Simulate another owner occupying an output after preflight. The
            // completed checkpoint is rolled back and the foreign file survives.
            var raced = Paths("raced");
            try
            {
                IndependentTrainingWorkflow.Run(train, development, recordsHash, two, raced, TrainingFeatureSource.SyntheticFixture,
                    progress: progress => { if (progress.CompletedSteps == 0) File.WriteAllText(raced.Head, "other-owner"); }, cancellationToken: token);
                check(false, "workflow should reject an output published after preflight");
            }
            catch (IOException)
            {
                check(!File.Exists(raced.Checkpoint) && !File.Exists(raced.Report) && File.ReadAllText(raced.Head) == "other-owner",
                    "workflow failed publication removes only owned checkpoint and preserves foreign head");
            }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                IndependentTrainingWorkflow.Run(train, development, recordsHash, two, Paths("cancelled"),
                    TrainingFeatureSource.SyntheticFixture, cancellationToken: cancelled.Token);
                check(false, "workflow should honor cancellation");
            }
            catch (OperationCanceledException)
            {
                check(!File.Exists(Paths("cancelled").Report) && !File.Exists(Paths("cancelled").Checkpoint),
                    "workflow cancellation publishes no success report or checkpoint");
            }

            var basicArgs = new[] { "train-original", "--package", directory, "--records", "records.jsonl", "--checkpoint", "out.checkpoint", "--head", "out.head", "--report", "out.json" };
            var parsed = OriginalTrainingCommand.Parse(basicArgs.Concat(["--max-steps", "4", "--seed", "17", "--learning-rate", "0.05"]).ToArray());
            check(parsed.Options.MaxSteps == 4 && parsed.Options.Seed == 17 && parsed.Resume is null, "train command parses cumulative step target");
            expectCode(() => OriginalTrainingCommand.Parse(basicArgs.Concat(["--max-steps", "invalid"]).ToArray()),
                "training_arguments_invalid", "train command refuses malformed optional value instead of silently defaulting");
            expectCode(() => OriginalTrainingCommand.Parse(basicArgs.Concat(["--resume-checkpoint", "first.checkpoint"]).ToArray()),
                "training_arguments_invalid", "train command requires complete resume binding arguments");
            expectCode(() => OriginalTrainingCommand.Parse(basicArgs.Concat(["--learning-rate", "NaN"]).ToArray()),
                "training_arguments_invalid", "train command refuses nonfinite learning rate");
            check(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly).Take(100).Count() == 0,
                "workflow leaves no temporary publication files");
        }
        finally
        {
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetDirectoryName(directory) == tempRoot && Path.GetFileName(directory).StartsWith("sezika-training-workflow-", StringComparison.Ordinal) &&
                Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static MarkerFeatureExample Example(string id, MarkerQuestionType type, string language, float[][] features, int target) =>
        new(id, type, language, Enumerable.Range(1, features.Length + 2).ToArray(), Enumerable.Range(1, features.Length).ToArray(), features, target);
}
