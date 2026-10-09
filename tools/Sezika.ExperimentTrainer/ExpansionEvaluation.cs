using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;

namespace Sezika.ExperimentTrainer;

internal sealed record ExpansionOptions(string Command, string Data, string ManifestSha256, string Package, string Output,
    string? ReportSha256, string? ValidationOutput, int Device, int TimeoutSeconds, int TrainingSeconds)
{
    internal static ExpansionOptions Parse(IReadOnlyList<string> arguments, CancellationToken token)
    {
        if (arguments.Count is < 9 or > 31 || (arguments.Count & 1) == 0 || arguments[0] is not ("train-expanded" or "validate-expanded")) throw ExperimentData.Invalid("Expected an explicit expanded command with bounded named pairs.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal); var clock = Stopwatch.StartNew();
        string[] allowed = ["--data", "--manifest-sha256", "--package", "--output", "--report-sha256", "--validation-output", "--device", "--timeout-seconds", "--training-seconds", "--steps", "--seed", "--learning-rate", "--max-tokens"];
        for (var index = 1; index < arguments.Count && index < 31; index += 2)
        {
            token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 5 || !allowed.Contains(arguments[index], StringComparer.Ordinal) || string.IsNullOrWhiteSpace(arguments[index + 1]) || arguments[index + 1].Length > 2048 || !values.TryAdd(arguments[index], arguments[index + 1])) throw ExperimentData.Invalid("Unknown/duplicate/empty expanded option.");
        }
        string Need(string name) => values.TryGetValue(name, out var value) ? value : throw ExperimentData.Invalid("Missing " + name);
        int Number(string name, int minimum, int maximum, int fallback)
        {
            if (!values.TryGetValue(name, out var text)) return fallback;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum) throw ExperimentData.Invalid("Invalid expanded " + name);
            return value;
        }
        var manifest = Need("--manifest-sha256"); ExperimentData.RequireHash(manifest);
        var validate = arguments[0] == "validate-expanded";
        var report = validate ? Need("--report-sha256") : null; if (report is not null) ExperimentData.RequireHash(report);
        if (!validate && (values.ContainsKey("--report-sha256") || values.ContainsKey("--validation-output"))) throw ExperimentData.Invalid("Training does not accept validation output/source options.");
        if (validate && new[] { "--training-seconds", "--steps", "--seed", "--learning-rate", "--max-tokens" }.Any(values.ContainsKey)) throw ExperimentData.Invalid("Fresh validation uses the pinned training report configuration.");
        if (Number("--steps", 24_576, 24_576, 24_576) != 24_576 || Number("--seed", 17, 17, 17) != 17 || Number("--max-tokens", 1024, 1024, 1024) != 1024 ||
            (values.TryGetValue("--learning-rate", out var rate) && (!float.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRate) || parsedRate != 0.001f))) throw ExperimentData.Invalid("Expanded training configuration is fixed.");
        return new(arguments[0], Path.GetFullPath(Need("--data")), manifest, Path.GetFullPath(Need("--package")), Path.GetFullPath(Need("--output")), report,
            validate ? Path.GetFullPath(Need("--validation-output")) : null, Number("--device", 0, 15, 0), Number("--timeout-seconds", 1, 1500, 1500), Number("--training-seconds", 1, 300, 300));
    }
}

internal static partial class ExperimentOperations
{
    internal const string ExpandedHeadName = "expanded-head.asset";
    internal const string ExpandedCheckpointName = "expanded-training.checkpoint";
    internal const string ExpandedReportName = "expanded-training-report.json";
    internal static void RunExpanded(string[] arguments, CancellationToken token)
    {
        var options = ExpansionOptions.Parse(arguments, token);
        using var budget = new ExperimentBudget(options.TimeoutSeconds, token) { EncoderForwardLimit = 384 };
        using var data = ExperimentData.OpenExpanded(options.Data, options.ManifestSha256, budget.Check, budget.Token);
        if (options.Command == "train-expanded") TrainExpanded(options, data, budget); else ValidateExpanded(options, data, budget);
    }
    private static MarkerFeatureSet ExportExpandedRows(CudaDevice device, IndependentEncoderModelPackage model, ExperimentRecord[] rows, MarkerFeatureIdentity identity, ExperimentBudget budget)
    {
        if (rows.Length is < 1 or > 256) throw ExperimentData.Invalid("Expanded GPU export denominator differs.");
        var examples = new MarkerFeatureExample[rows.Length];
        using (var encoder = new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, budget.Token))
        {
            for (var index = 0; index < rows.Length && index < 256; index++)
            {
                CheckGpu(device, budget); var row = rows[index];
                Progress($"expanded GPU feature export {index + 1}/{rows.Length} {row.RecordId}");
                var sequence = IndependentDecisionV1SequenceBuilder.Build(model.Tokenizer, row.Request, row.Question, new IndependentDecisionSequenceOptions { MaxTokens = 1024 }, budget.Token);
                if (row.Request.ModelId != model.ModelId || !sequence.CandidateIds.SequenceEqual(row.CandidateIds) || sequence.MarkerPositions.Length != row.CandidateIds.Length) throw ExperimentData.Invalid("Expanded actual rendered model/candidate identity differs.");
                budget.BeginEncoderForward(); var hidden = encoder.Encode(sequence.TokenIds, budget.Token); budget.CompleteEncoderForward();
                try
                {
                    if (hidden.Length != checked(sequence.TokenIds.Length * identity.HiddenSize) || hidden.Length > 1024 * 2048) throw ExperimentData.Invalid("Expanded GPU hidden tensor incomplete.");
                    for (var offset = 0; offset < hidden.Length && offset < 1024 * 2048; offset++)
                    { if ((offset & 255) == 0) budget.Check(); if (!float.IsFinite(hidden[offset])) throw ExperimentData.Invalid("Expanded GPU hidden tensor nonfinite."); }
                    var markers = new float[sequence.MarkerPositions.Length][];
                    for (var candidate = 0; candidate < markers.Length && candidate < 8; candidate++) { budget.Check(); markers[candidate] = hidden.AsSpan(sequence.MarkerPositions[candidate] * identity.HiddenSize, identity.HiddenSize).ToArray(); }
                    examples[index] = new(row.RecordId, row.Type, row.Language, sequence.TokenIds, sequence.MarkerPositions, markers, row.TargetIndex);
                }
                finally { Array.Clear(hidden); }
                CheckGpu(device, budget);
            }
        }
        RequireReleased(device, budget); return new(identity, examples);
    }
    private static (MarkerFeatureSet Train, MarkerFeatureSet Development) ExportExpanded(CudaDevice device, IndependentEncoderModelPackage model, ExperimentData data, ExperimentBudget budget)
    {
        var identity = new MarkerFeatureIdentity(model.ModelId, model.EncoderSha256, model.TokenizerSha256, data.ManifestSha256, model.Encoder.Config.HiddenSize);
        var all = ExportExpandedRows(device, model, data.Records, identity, budget);
        var trainIds = data.Records.Where(row => row.Split == "train").Select(row => row.RecordId).ToHashSet(StringComparer.Ordinal);
        return (new(identity, all.Examples.Where(row => trainIds.Contains(row.RecordId)).ToArray()), new(identity, all.Examples.Where(row => !trainIds.Contains(row.RecordId)).ToArray()));
    }
    private static ExperimentRecord RotateExpanded(ExperimentRecord row, Action check)
    {
        check();
        if (row.Split != "train" || row.Question is not IndependentChoiceQuestion choice || choice.Candidates.Count is < 2 or > 8) throw ExperimentData.Invalid("Expanded rotation accepts training Choice2..8 only.");
        var count = choice.Candidates.Count; var candidates = new IndependentChoiceCandidate[count];
        for (var index = 0; index < count && index < 8; index++) { check(); candidates[index] = choice.Candidates[(index + 1) % count]; }
        var question = choice with { Candidates = candidates }; var ids = candidates.Select(candidate => candidate.Id).ToArray();
        var target = Array.IndexOf(ids, row.CandidateIds[row.TargetIndex]); if (target < 0) throw ExperimentData.Invalid("Expanded rotated target ID missing.");
        return row with { TargetIndex = target, Question = question, Request = row.Request with { Questions = [question] }, CandidateIds = ids };
    }
    private static (ExperimentLoss Loss, ExperimentEvaluationRow[] Rows) ScoreExpandedFeatures(string split, string variant, CudaDevice device, MarkerFeatureSet set, LinearMarkerHead head, ExperimentData data, ExperimentBudget budget, ExperimentEvaluationRow[]? originals = null)
    {
        CheckGpu(device, budget);
        var ce = IndependentCudaMarkerHeadEvaluator.Evaluate(device, set, head, TimeSpan.FromSeconds(300), item => { if (item.CompletedExamples == 1 || item.CompletedExamples == item.ExampleCount) Progress($"expanded GPU row CE {split}/{variant} {item.CompletedExamples}/{item.ExampleCount}"); }, budget.Token);
        RequireReleased(device, budget); var rows = new ExperimentEvaluationRow[set.Examples.Count]; var weights = head.CopyWeights(); var right = new float[weights.Length];
        try
        {
            for (var hidden = 0; hidden < head.HiddenSize && hidden < 2048; hidden++) { budget.Check(); for (var type = 0; type < 3; type++) right[hidden * 3 + type] = weights[type * head.HiddenSize + hidden]; }
            using (var gemm = device.CreateGemm())
            {
                for (var index = 0; index < set.Examples.Count && index < 256; index++)
                {
                    CheckGpu(device, budget); var example = set.Examples[index]; var source = data.Records.Single(row => row.RecordId == example.RecordId);
                    var rendered = variant == "original" ? source : RotateExpanded(source, budget.Check);
                    var packed = new float[checked(example.Features.Length * head.HiddenSize)]; var values = new float[example.Features.Length * 3];
                    try
                    {
                        for (var candidate = 0; candidate < example.Features.Length && candidate < 8; candidate++) { budget.Check(); example.Features[candidate].CopyTo(packed, candidate * head.HiddenSize); }
                        gemm.Execute(packed, example.Features.Length, head.HiddenSize, right, 3, values); CheckGpu(device, budget);
                        var logits = new double[example.Features.Length];
                        for (var candidate = 0; candidate < logits.Length && candidate < 8; candidate++) { budget.Check(); logits[candidate] = values[candidate * 3 + (int)example.Type]; }
                        var max = logits.Max(); var probabilities = logits.Select(value => Math.Exp(value - max)).ToArray(); var sum = probabilities.Sum();
                        for (var candidate = 0; candidate < probabilities.Length && candidate < 8; candidate++) { budget.Check(); probabilities[candidate] /= sum; }
                        var predicted = Array.IndexOf(logits, max); var rank = 1 + logits.Count(value => value > logits[example.TargetIndex]);
                        var nll = max + Math.Log(sum) - logits[example.TargetIndex]; var gpu = ce.Rows[index];
                        if (gpu.RecordId != example.RecordId || gpu.TargetIndex != example.TargetIndex || gpu.Type != example.Type) throw ExperimentData.Invalid("Expanded GPU CE row identity differs.");
                        var tokenHash = IntHash(example.TokenIds); var markerHash = IntHash(example.MarkerPositions); var featureHash = FeatureHash(example, budget.Check);
                        var original = originals?.Single(row => row.RecordId == example.RecordId);
                        string[] texts = rendered.Question switch { IndependentChoiceQuestion choice => choice.Candidates.Select(candidate => candidate.Text).ToArray(), IndependentScoreQuestion score => score.Levels.ToArray(), IndependentBooleanQuestion boolean => [boolean.WhenFalse, boolean.WhenTrue], _ => throw ExperimentData.Invalid("Unsupported expanded question.") };
                        rows[index] = new(source.RecordId, source.Split, source.Language, source.Type.ToString().ToLowerInvariant(), variant, source.CandidateIds, rendered.CandidateIds,
                            texts.Select(value => ExperimentData.Hash(System.Text.Encoding.UTF8.GetBytes(value))).ToArray(), source.CandidateIds[source.TargetIndex], source.TargetIndex, example.TargetIndex, rank,
                            predicted, rendered.CandidateIds[predicted], predicted == example.TargetIndex, logits, probabilities, gpu.Loss, nll, example.TokenIds, example.MarkerPositions,
                            tokenHash, markerHash, featureHash, original?.TokenSha256 ?? tokenHash, original?.MarkerSha256 ?? markerHash, original?.FeatureSha256 ?? featureHash);
                    }
                    finally { Array.Clear(packed); Array.Clear(values); }
                    if ((index + 1) % 16 == 0 || index + 1 == set.Examples.Count) Progress($"expanded GPU row logits {split}/{variant} {index + 1}/{set.Examples.Count}");
                }
            }
        }
        finally { Array.Clear(weights); Array.Clear(right); }
        RequireReleased(device, budget); return (ExperimentLoss.From(split + "/" + variant, ce), rows);
    }
    private static ExpansionObservations ObserveExpanded(CudaDevice device, IndependentEncoderModelPackage model, ExperimentData data, MarkerFeatureSet train, MarkerFeatureSet development, IndependentMarkerHeadAsset head, ExperimentBudget budget)
    {
        var trained = ScoreExpandedFeatures("train", "original", device, train, head.Head, data, budget);
        var dev = ScoreExpandedFeatures("development", "original", device, development, head.Head, data, budget);
        var rotatedRows = data.Records.Where(row => row.Split == "train" && row.Type == MarkerQuestionType.Choice).Take(16).Select(row => RotateExpanded(row, budget.Check)).ToArray();
        if (rotatedRows.Length != 16) throw ExperimentData.Invalid("Expanded rotation requires fixed first16 training Choice rows in frozen file order.");
        var rotated = ExportExpandedRows(device, model, rotatedRows, train.Identity, budget);
        var swapped = ScoreExpandedFeatures("train", "fixed-left1", device, rotated, head.Head, data, budget, trained.Rows);
        var rows = trained.Rows.Concat(dev.Rows).Concat(swapped.Rows).ToArray();
        var result = new ExpansionObservations(trained.Loss, dev.Loss, swapped.Loss, rows, Groups(rows, budget.Check), ExpandedPositionComparisons(rows, budget.Check));
        ExpansionTrainingReport.ValidateObservations(result, trained.Loss.ParameterSha256, budget.Check, data.Manifest.Train, data.Manifest.Development); return result;
    }
    internal static ExperimentPositionComparison[] ExpandedPositionComparisons(ExperimentEvaluationRow[] rows, Action check)
    {
        var rotated = rows.Where(row => row.Variant == "fixed-left1").ToArray(); if (rotated.Length != 16) throw ExperimentData.Invalid("Expanded comparison requires16 rotations.");
        var result = new ExperimentPositionComparison[16];
        for (var index = 0; index < 16; index++)
        {
            check(); var row = rotated[index]; var original = rows.Single(value => value.RecordId == row.RecordId && value.Variant == "original");
            if (row.OriginalTeacherTargetIndex != original.TeacherTargetIndex || !row.OriginalCandidateIds.SequenceEqual(original.CandidateIds) || row.OriginalTokenSha256 != original.TokenSha256 ||
                row.OriginalMarkerSha256 != original.MarkerSha256 || row.OriginalFeatureSha256 != original.FeatureSha256 || row.TokenSha256 == original.TokenSha256 ||
                !row.CandidateTextSha256.SequenceEqual(original.CandidateTextSha256.Skip(1).Concat(original.CandidateTextSha256.Take(1)))) throw ExperimentData.Invalid("Expanded rotation original text/token/feature binding differs.");
            result[index] = new(row.RecordId, row.TeacherTargetId, original.SelectedCandidateId, row.SelectedCandidateId, original.SelectedCandidateId != row.SelectedCandidateId,
                original.TeacherTargetRank, row.TeacherTargetRank, original.AgreesWithTeacher, row.AgreesWithTeacher);
        }
        return result;
    }
    private static void TrainExpanded(ExpansionOptions options, ExperimentData data, ExperimentBudget budget)
    {
        var output = new ExpansionOutput(options.Output, budget.Check); Exception? primary = null;
        try { TrainExpandedCore(options, data, budget, output); } catch (Exception error) { primary = error; }
        try { output.Dispose(); } catch (Exception cleanup) { if (primary is not null) throw new AggregateException("Expanded train/rollback failed.", primary, cleanup); throw; }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
    private static void TrainExpandedCore(ExpansionOptions options, ExperimentData data, ExperimentBudget budget, ExpansionOutput output)
    {
        using var model = LoadModel(options.Package, budget); using var device = CudaDevice.Open(options.Device); CheckGpu(device, budget);
        var artifacts = CudaTrainingExecutionIdentity.Read(budget.Token); var features = ExportExpanded(device, model, data, budget);
        var zero = new LinearMarkerHead(model.Encoder.Config.HiddenSize, new float[3 * model.Encoder.Config.HiddenSize]);
        var baselineTrain = Measure($"expanded_zero_train{features.Train.Examples.Count}", device, features.Train, zero, budget); var baselineDev = Measure($"expanded_zero_development{features.Development.Examples.Count}", device, features.Development, zero, budget);
        var lastProgress = Stopwatch.StartNew();
        var visits = data.Manifest.SchemaVersion == 3 ? new ExpansionVisitObserver(features.Train.Examples.Select(row => row.RecordId).ToArray(), 17, budget.Check) : null;
        var state = IndependentCudaMarkerHeadTrainer.Train(device, features.Train, new(17, 24_576, 0.001f, TimeSpan.FromSeconds(options.TrainingSeconds)), progress: item =>
        {
            visits?.Committed(item.CompletedSteps);
            CheckGpu(device, budget); if (item.CompletedSteps == 24_576 || lastProgress.Elapsed.TotalSeconds >= 5) { Progress($"expanded GPU SGD {item.CompletedSteps}/24576 pre-update selected-example CE={item.LastLoss:R}"); lastProgress.Restart(); }
        }, cancellationToken: budget.Token);
        RequireReleased(device, budget); var weights = state.Head.CopyWeights(); string parameterHash;
        try { parameterHash = ExperimentData.Hash(MemoryMarshal.AsBytes(weights.AsSpan())); if (state.CompletedSteps != 24_576 || !weights.Any(value => value != 0)) throw ExperimentData.Invalid("Expanded fixed24576 committed updates were not completed."); }
        finally { Array.Clear(weights); }
        var trainedTrain = Measure($"expanded_trained_train{features.Train.Examples.Count}", device, features.Train, state.Head, budget); var trainedDev = Measure($"expanded_trained_development{features.Development.Examples.Count}", device, features.Development, state.Head, budget);
        var checkpointHash = MarkerTrainingCheckpoint.SaveNew(output.Checkpoint.Path, state, budget.Token); output.Checkpoint.Acquire(checkpointHash);
        var headHash = IndependentMarkerHeadAssetStore.SaveNew(output.Head.Path, state, budget.Token); output.Head.Acquire(headHash);
        var loadedCheckpoint = MarkerTrainingCheckpoint.Load(output.Checkpoint.Path, features.Train, checkpointHash, budget.Token);
        var loadedHead = IndependentMarkerHeadAssetStore.Load(output.Head.Path, state.Identity, state.FeatureSha256, headHash, budget.Token);
        RequireRoundtrip(state, loadedCheckpoint, loadedHead, parameterHash);
        var observations = ObserveExpanded(device, model, data, features.Train, features.Development, loadedHead, budget); var info = device.Info;
        var resources = CloseResources(device, model, budget);
        var repaired = data.Manifest.SchemaVersion == 3;
        var report = new ExpansionTrainingReport(repaired ? ExpansionTrainingReport.RepairedContract : ExpansionTrainingReport.Contract, "completed_experiment_not_formal_acceptance", data.Manifest, data.ManifestSha256, data.RecordsSha256, data.ProvenanceSha256, data.Root,
            state.Identity, artifacts, new(info.Uuid, info.Name, info.DriverVersion, info.ComputeCapabilityMajor, info.ComputeCapabilityMinor, info.TotalMemoryBytes, device.DeviceIndex),
            state.Seed, state.LearningRate, state.CompletedSteps, state.LastLoss, true, "all_zero_three_linear_rows", IndependentCudaMarkerHeadTrainer.Backend, "cuda-modernbert-fp32-v1",
            repaired ? ExpansionTrainingReport.RepairedStepSemantics : "24576_total_committed_updates_128_complete_192_example_epochs_no_resume", data.Manifest.Train, data.Manifest.Development, 1024, options.TrainingSeconds, options.TimeoutSeconds, budget.EncoderForwardLimit, budget.EncoderForwardsStarted, budget.EncoderForwardsCompleted,
            state.FeatureSha256, parameterHash, headHash, checkpointHash, true, baselineTrain, baselineDev, trainedTrain, trainedDev, observations, new(), resources, budget.ElapsedSeconds)
        { DerivationLedgerSha256 = data.DerivationLedgerSha256, SampleVisits = visits?.Complete(state.CompletedSteps) };
        report.Validate(budget.Check); var bytes = JsonSerializer.SerializeToUtf8Bytes(report, ExperimentJsonContext.Default.ExpansionTrainingReport);
        if (bytes.Length > 4_194_304) throw ExperimentData.Invalid("Expanded report exceeds4MiB.");
        var hash = ExperimentData.Hash(bytes); output.Report.WriteNew(bytes, hash); budget.Check(); output.Commit();
        Console.WriteLine($"{{\"status\":\"completed_experiment_not_formal_acceptance\",\"report_sha256\":\"{hash}\",\"head_sha256\":\"{headHash}\",\"checkpoint_sha256\":\"{checkpointHash}\",\"completed_steps\":24576,\"encoder_forwards_completed\":{budget.EncoderForwardsCompleted}}}");
    }
    private static void ValidateExpanded(ExpansionOptions options, ExperimentData data, ExperimentBudget budget)
    {
        RequireEvaluationOutput(options.ValidationOutput!, options.Output, budget.Check);
        var output = new ExperimentOwnedFile(options.ValidationOutput!, 4_194_304); Exception? primary = null;
        try { ValidateExpandedCore(options, data, budget, output); } catch (Exception error) { primary = error; }
        if (primary is null) return;
        try { output.Cleanup(() => { if (budget.ElapsedSeconds > options.TimeoutSeconds + 10) throw new IOException("Expanded validation rollback deadline expired."); }); }
        catch (Exception cleanup) { throw new AggregateException("Expanded validation/rollback failed.", primary, cleanup); }
        ExceptionDispatchInfo.Capture(primary).Throw();
    }
    private static void ValidateExpandedCore(ExpansionOptions options, ExperimentData data, ExperimentBudget budget, ExperimentOwnedFile output)
    {
        var reportPath = Path.Combine(options.Output, ExpandedReportName); var headPath = Path.Combine(options.Output, ExpandedHeadName); var checkpointPath = Path.Combine(options.Output, ExpandedCheckpointName);
        foreach (var path in new[] { reportPath, headPath, checkpointPath }) { budget.Check(); ExperimentData.CheckPath(path, budget.Check); }
        using var reportLease = new FileStream(reportPath, FileMode.Open, FileAccess.Read, FileShare.Read); using var headLease = new FileStream(headPath, FileMode.Open, FileAccess.Read, FileShare.Read); using var checkpointLease = new FileStream(checkpointPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = ExperimentData.Snapshot(reportLease, 4_194_304, budget.Check);
        if (!ExperimentData.SameHash(ExperimentData.Hash(bytes), options.ReportSha256)) throw ExperimentData.Invalid("Expanded source report pinned hash differs.");
        ExperimentData.CheckJson(bytes, 4_194_304, budget.Token, 500_000);
        var report = JsonSerializer.Deserialize(bytes, ExperimentJsonContext.Default.ExpansionTrainingReport) ?? throw ExperimentData.Invalid("Expanded report null."); report.Validate(budget.Check);
        if (!ExperimentData.SameHash(report.ManifestSha256, data.ManifestSha256) || !ExperimentData.SameHash(report.RecordsSha256, data.RecordsSha256) || !ExperimentData.SameHash(report.InputProvenanceSha256, data.ProvenanceSha256) || report.DataDeclaration.Contract != data.Manifest.Contract ||
            (data.Manifest.SchemaVersion == 3 && !ExperimentData.SameHash(report.DerivationLedgerSha256, data.DerivationLedgerSha256))) throw ExperimentData.Invalid("Expanded fresh data/derivation identity differs.");
        using var model = LoadModel(options.Package, budget); using var device = CudaDevice.Open(options.Device); CheckGpu(device, budget); var artifacts = CudaTrainingExecutionIdentity.Read(budget.Token);
        if (device.Info.Uuid != report.Device.Uuid || device.Info.DriverVersion != report.Device.DriverVersion || model.ModelId != report.Identity.ModelId || !ExperimentData.SameHash(model.EncoderSha256, report.Identity.EncoderSha256) || !ExperimentData.SameHash(model.TokenizerSha256, report.Identity.TokenizerSha256)) throw ExperimentData.Invalid("Expanded fresh encoder/device identity differs.");
        RequireArtifactIdentities(report.CudaArtifacts, artifacts, budget.Check);
        var features = ExportExpanded(device, model, data, budget); var checkpoint = MarkerTrainingCheckpoint.Load(checkpointPath, features.Train, report.CheckpointSha256, budget.Token);
        var head = IndependentMarkerHeadAssetStore.Load(headPath, features.Train.Identity, report.FeatureSha256, report.HeadSha256, budget.Token); RequireRoundtrip(checkpoint, checkpoint, head, report.ParameterSha256);
        if (checkpoint.CompletedSteps != report.CompletedSteps || checkpoint.Seed != report.Seed || checkpoint.LearningRate != report.LearningRate || checkpoint.LastLoss != report.LastStepPreUpdateSelectedExampleLoss) throw ExperimentData.Invalid("Expanded fresh optimizer metadata differs.");
        var observed = ObserveExpanded(device, model, data, features.Train, features.Development, head, budget); RequireExpandedObservationsMatch(report.LoadedObservations, observed, budget.Check);
        var forwardCount = data.Manifest.Total + 16;
        if (budget.EncoderForwardsStarted != forwardCount || budget.EncoderForwardsCompleted != forwardCount) throw ExperimentData.Invalid("Expanded fresh forward denominator differs.");
        var resources = CloseResources(device, model, budget); ExpansionTrainingReport.ValidateResources(resources);
        var validation = new ExpansionValidationReport(data.Manifest.SchemaVersion == 3 ? "sezika/teacher-labelled-gpu-expanded-experiment-validation/v3" : "sezika/teacher-labelled-gpu-expanded-experiment-validation/v2", "completed_experiment_not_formal_acceptance", options.ReportSha256!, data.ManifestSha256, data.RecordsSha256, data.ProvenanceSha256,
            head.FileSha256, report.CheckpointSha256, report.ParameterSha256, true, budget.EncoderForwardsStarted, budget.EncoderForwardsCompleted, observed, new(), resources, budget.ElapsedSeconds)
        { DerivationLedgerSha256 = data.DerivationLedgerSha256 };
        bytes = JsonSerializer.SerializeToUtf8Bytes(validation, ExperimentJsonContext.Default.ExpansionValidationReport); if (bytes.Length > 4_194_304) throw ExperimentData.Invalid("Expanded validation output exceeds4MiB.");
        var hash = ExperimentData.Hash(bytes); output.WriteNew(bytes, hash); budget.Check();
        Console.WriteLine($"{{\"status\":\"completed_experiment_not_formal_acceptance\",\"fresh_disk_outputs_matched\":true,\"validation_sha256\":\"{hash}\",\"encoder_forwards_completed\":{budget.EncoderForwardsCompleted}}}");
    }
    internal static void RequireExpandedObservationsMatch(ExpansionObservations expected, ExpansionObservations actual, Action check)
    {
        check(); var count = expected.Train.ExampleCount + expected.Development.ExampleCount + 16;
        if (count is not (272 or 231) || expected.Rows.Length != count || actual.Rows.Length != count || expected.Groups.Length != actual.Groups.Length || !expected.PositionComparisons.SequenceEqual(actual.PositionComparisons)) throw ExperimentData.Invalid("Expanded fresh observations denominator/position differs.");
        var oldLoss = new[] { expected.Train, expected.Development, expected.RotatedTrainingChoice }; var freshLoss = new[] { actual.Train, actual.Development, actual.RotatedTrainingChoice };
        for (var index = 0; index < 3; index++)
        {
            check(); var old = oldLoss[index]; var fresh = freshLoss[index];
            if (old.ExampleCount != fresh.ExampleCount || old.CompletedExamples != fresh.CompletedExamples || !ExperimentData.SameHash(old.FeatureSha256, fresh.FeatureSha256) || !ExperimentData.SameHash(old.ParameterSha256, fresh.ParameterSha256) || !ExperimentData.SameHash(old.DeviceParameterSha256, fresh.DeviceParameterSha256) || Math.Abs(old.MeanLoss - fresh.MeanLoss) > 2e-6) throw ExperimentData.Invalid("Expanded fresh GPU CE/feature/device parameter differs.");
        }
        for (var index = 0; index < count && index < 272; index++)
        {
            check(); var old = expected.Rows[index]; var fresh = actual.Rows[index];
            if (old.Logits.Length != fresh.Logits.Length || old.Probabilities.Length != fresh.Probabilities.Length || Math.Abs(old.GpuTeacherCe - fresh.GpuTeacherCe) > 2e-6 || Math.Abs(old.StableSoftmaxTeacherNll - fresh.StableSoftmaxTeacherNll) > 2e-6) throw ExperimentData.Invalid("Expanded fresh row CE/shape differs.");
            for (var candidate = 0; candidate < fresh.Logits.Length && candidate < 8; candidate++) { check(); if (Math.Abs(old.Logits[candidate] - fresh.Logits[candidate]) > 2e-6 || Math.Abs(old.Probabilities[candidate] - fresh.Probabilities[candidate]) > 2e-6) throw ExperimentData.Invalid("Expanded fresh GPU row logits/probabilities differ."); }
            var normalized = old with { Logits = fresh.Logits, Probabilities = fresh.Probabilities, GpuTeacherCe = fresh.GpuTeacherCe, StableSoftmaxTeacherNll = fresh.StableSoftmaxTeacherNll };
            if (!JsonSerializer.SerializeToUtf8Bytes(normalized, ExperimentJsonContext.Default.ExperimentEvaluationRow).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(fresh, ExperimentJsonContext.Default.ExperimentEvaluationRow))) throw ExperimentData.Invalid("Expanded fresh row canonical/text/token/feature/decision identity differs.");
        }
        for (var index = 0; index < actual.Groups.Length && index < 18; index++)
        {
            check(); var old = expected.Groups[index]; var fresh = actual.Groups[index];
            if (Math.Abs(old.MeanGpuTeacherCe - fresh.MeanGpuTeacherCe) > 2e-6 || (old with { MeanGpuTeacherCe = fresh.MeanGpuTeacherCe }) != fresh) throw ExperimentData.Invalid("Expanded fresh grouped teacher agreement/rank differs.");
        }
    }
}

internal sealed record ExpansionSampleVisit(string RecordId, int CommittedUpdates);
internal sealed record ExpansionSampleVisits(string Semantics, int Seed, int TrainCount, int ObservedCommittedUpdates,
    int CompleteOrderCycles, int FinalCyclePrefixCount, string OrderIndexSha256, string[] FrozenFileOrder,
    string[] SeededCycleOrder, string[] FinalCyclePrefix, ExpansionSampleVisit[] Visits)
{
    internal const string VisitSemantics = "contiguous_actual_commit_callbacks_with_selected_ids_replayed_from_core_xorshift32_fisher_yates_fixed_order_not_direct_kernel_id_observation";
    internal void Validate(string[] fileOrder, int seed, int steps, Action check)
    {
        if (fileOrder.Length is < 1 or > 256 || steps is < 0 or > 24_576) throw ExperimentData.Invalid("Visit validation bound differs.");
        var replay = ExpansionVisitObserver.Replay(fileOrder, seed, steps, check);
        if (!JsonSerializer.SerializeToUtf8Bytes(this, ExperimentJsonContext.Default.ExpansionSampleVisits).AsSpan().SequenceEqual(
            JsonSerializer.SerializeToUtf8Bytes(replay, ExperimentJsonContext.Default.ExpansionSampleVisits))) throw ExperimentData.Invalid("Sample visit/order/partial cycle evidence differs from actual committed count.");
    }
}
internal sealed class ExpansionVisitObserver
{
    private readonly string[] _ids;
    private readonly int[] _order;
    private readonly int[] _visits;
    private readonly int _seed;
    private readonly Action _check;
    private int _committed = -1;
    internal ExpansionVisitObserver(string[] ids, int seed, Action check)
    {
        check();
        if (ids.Length is < 1 or > 256 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw ExperimentData.Invalid("Visit file-order identities are incomplete/duplicate.");
        _ids = ids.ToArray(); _seed = seed; _check = check; _visits = new int[ids.Length]; _order = Enumerable.Range(0, ids.Length).ToArray();
        // Exact replay of the reviewed core MakeOrder; the GPU progress callback observes commits, not selected IDs.
        uint state = unchecked((uint)seed) ^ 0x9E3779B9u; if (state == 0) state = 0xA341316Cu;
        for (var index = ids.Length - 1; index > 0 && index < 256; index--)
        {
            check(); state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            var other = (int)(state % (uint)(index + 1)); (_order[index], _order[other]) = (_order[other], _order[index]);
        }
    }
    internal void Committed(int completed)
    {
        _check();
        if (completed != _committed + 1 || completed is < 0 or > 24_576) throw ExperimentData.Invalid("Training commit callback skipped/repeated or exceeds fixed update bound.");
        if (completed > 0) _visits[_order[(completed - 1) % _ids.Length]]++;
        _committed = completed;
    }
    internal ExpansionSampleVisits Complete(int completed)
    {
        _check(); if (_committed != completed || completed < 0) throw ExperimentData.Invalid("Final visit observation differs from committed training state.");
        return new(ExpansionSampleVisits.VisitSemantics, _seed, _ids.Length, completed, completed / _ids.Length, completed % _ids.Length,
            ExperimentOperations.IntHash(_order), _ids.ToArray(), _order.Select(index => _ids[index]).ToArray(),
            _order.Take(completed % _ids.Length).Select(index => _ids[index]).ToArray(), _ids.Select((id, index) => new ExpansionSampleVisit(id, _visits[index])).ToArray());
    }
    internal static ExpansionSampleVisits Replay(string[] ids, int seed, int completed, Action check)
    {
        if (completed is < 0 or > 24_576) throw ExperimentData.Invalid("Visit replay step bound differs.");
        var expected = new ExpansionVisitObserver(ids, seed, check);
        for (var index = 0; index < ids.Length && index < 256; index++)
        {
            check(); expected._visits[expected._order[index]] = completed / ids.Length + (index < completed % ids.Length ? 1 : 0);
        }
        expected._committed = completed; return expected.Complete(completed);
    }
}

internal sealed class ExpansionOutput : IDisposable
{
    private readonly DirectoryInfo _directory;
    private readonly DateTime _created;
    private bool _committed;
    internal ExperimentOwnedFile Head { get; }
    internal ExperimentOwnedFile Checkpoint { get; }
    internal ExperimentOwnedFile Report { get; }
    internal ExpansionOutput(string directory, Action check)
    {
        directory = Path.GetFullPath(directory); check(); ExperimentData.CheckPath(directory, check);
        if (Directory.Exists(directory) || File.Exists(directory) || !Directory.Exists(Path.GetDirectoryName(directory))) throw ExperimentData.Invalid("Expanded output requires a fresh exclusive directory under an existing parent.");
        Head = new(Path.Combine(directory, ExperimentOperations.ExpandedHeadName)); Checkpoint = new(Path.Combine(directory, ExperimentOperations.ExpandedCheckpointName)); Report = new(Path.Combine(directory, ExperimentOperations.ExpandedReportName), 4_194_304);
        _directory = new(directory); _directory.Create(); _directory.Refresh(); _created = _directory.CreationTimeUtc;
    }
    internal void Commit() => _committed = true;
    public void Dispose()
    {
        if (_committed) return; var watch = Stopwatch.StartNew(); var errors = new List<Exception>(4);
        void Check() { if (watch.Elapsed.TotalSeconds >= 10) throw new IOException("Expanded rollback deadline expired."); }
        foreach (var file in new[] { Report, Head, Checkpoint }) { try { file.Cleanup(Check); } catch (Exception error) { errors.Add(error); } }
        try { Check(); _directory.Refresh(); if (_directory.Exists && _directory.CreationTimeUtc == _created && (_directory.Attributes & FileAttributes.ReparsePoint) == 0 && !Directory.EnumerateFileSystemEntries(_directory.FullName).Take(4).Any()) { Check(); _directory.Delete(false); } }
        catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException("Expanded rollback preserved changed/unowned paths.", errors);
    }
}
