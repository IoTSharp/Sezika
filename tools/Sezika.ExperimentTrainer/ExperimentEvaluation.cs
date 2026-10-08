using System.Runtime.InteropServices;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;

namespace Sezika.ExperimentTrainer;

internal sealed record ExperimentEvaluationRow(string RecordId, string Split, string Language, string Type,
    string Variant, string[] OriginalCandidateIds, string[] CandidateIds, string[] CandidateTextSha256,
    string TeacherTargetId, int OriginalTeacherTargetIndex, int TeacherTargetIndex, int TeacherTargetRank,
    int PredictedIndex, string SelectedCandidateId, bool AgreesWithTeacher, double[] Logits,
    double[] Probabilities, double GpuTeacherCe, double StableSoftmaxTeacherNll,
    int[] TokenIds, int[] MarkerPositions, string TokenSha256, string MarkerSha256, string FeatureSha256,
    string OriginalTokenSha256, string OriginalMarkerSha256, string OriginalFeatureSha256);

internal sealed record ExperimentEvaluationGroup(string Split, string Variant, string Language, string Type,
    int Total, int Completed, int TeacherAgreementCount, double TeacherAgreementRate,
    double MeanGpuTeacherCe, double MeanTeacherTargetRank);

internal sealed record ExperimentPositionComparison(string RecordId, string TeacherTargetId,
    string OriginalSelectedCandidateId, string RotatedSelectedCandidateId, bool SelectedIdChanged,
    int OriginalTargetRank, int RotatedTargetRank, bool OriginalAgreesWithTeacher, bool RotatedAgreesWithTeacher);

internal sealed record ExperimentEvaluationObservations(ExperimentLoss Train, ExperimentLoss? Development,
    ExperimentLoss RotatedTrainingChoice, ExperimentInference? DevelopmentPipeline, ExperimentEvaluationRow[] Rows, ExperimentEvaluationGroup[] Groups,
    ExperimentPositionComparison[] PositionComparisons);

internal sealed record ExperimentEvaluationReport(string Contract, string Status, string Phase,
    string Use, string RankSemantics, string PredictionTieSemantics, string ProbabilityBackend,
    string SourceReportSha256, string HeadSha256, string CheckpointSha256, string ParameterSha256,
    string ManifestSha256, string RecordsSha256, string InputProvenanceSha256, string ActualDataRoot,
    MarkerFeatureIdentity Identity, CudaTrainingArtifactsIdentity CudaArtifacts, ExperimentDevice Device,
    int CompletedSteps, int MaxTokens, int EncoderForwardLimit, int EncoderForwardsStarted,
    int EncoderForwardsCompleted, int FailedRecords, double ElapsedSeconds, bool? FreshDiskOutputsMatched,
    string? ComparedEvaluationSha256, ExperimentEvaluationObservations Observations,
    ExperimentAcceptance Acceptance, ExperimentResources Resources)
{
    internal const string EvaluationContract = "sezika/teacher-labelled-gpu-learnability-evaluation/v1";
    internal void Validate(Action check)
    {
        check(); Acceptance.Validate();
        if (Contract != EvaluationContract || Status != "completed_experiment_not_formal_acceptance" ||
            Phase is not ("existing_head_train24" or "new_head_disk_loaded_same_process" or "fresh_process_disk_loaded") ||
            FailedRecords != 0 || MaxTokens != 1024 || CompletedSteps is < 1 or > 768 ||
            EncoderForwardLimit is not (32 or 72) || EncoderForwardsStarted != EncoderForwardsCompleted ||
            EncoderForwardsCompleted != (Phase == "existing_head_train24" ? 32 : 48) ||
            EncoderForwardsCompleted > EncoderForwardLimit || !double.IsFinite(ElapsedSeconds) || ElapsedSeconds < 0 ||
            RankSemantics != "competition_rank_1_plus_count_of_strictly_greater_logits_ties_share_rank" ||
            PredictionTieSemantics != "first_candidate_in_rendered_order" || ProbabilityBackend != "host_stable_softmax_of_gpu_fp32_logits_not_calibrated_accuracy" ||
            Use != "teacher_agreement_on_exposed_unreviewed_train_and_development_diagnostic_not_gold_accuracy" ||
            !Resources.DeviceDisposeReturned || Resources.ModelDisposeReturned != true ||
            Resources.SnapshotBeforeContextDispose.OwnedBytes != 0 || Resources.SnapshotBeforeContextDispose.OwnedAllocationCount != 0 ||
            Resources.SnapshotBeforeContextDispose.LoadedModuleCount != 0 || Resources.SnapshotBeforeContextDispose.ReleaseFailureCount != 0 ||
            Resources.SnapshotBeforeContextDispose.PeakOwnedBytes > ExperimentOperations.GpuLimit || Resources.ProcessPeakWorkingSetBytes > 4L * 1024 * 1024 * 1024)
            throw ExperimentData.Invalid("Evaluation execution/resource contract differs.");
        foreach (var hash in new[] { SourceReportSha256, HeadSha256, CheckpointSha256, ParameterSha256, ManifestSha256, RecordsSha256, InputProvenanceSha256 })
        { check(); ExperimentData.RequireHash(hash); }
        if (ComparedEvaluationSha256 is not null) ExperimentData.RequireHash(ComparedEvaluationSha256);
        if ((Phase == "fresh_process_disk_loaded") != (FreshDiskOutputsMatched == true && ComparedEvaluationSha256 is not null))
            throw ExperimentData.Invalid("Fresh disk evaluation comparison evidence differs.");
        ValidateObservations(Observations, Phase == "existing_head_train24" ? 0 : 8, ParameterSha256, check);
    }

    internal static void ValidateObservations(ExperimentEvaluationObservations observations, int development, string parameterHash, Action check)
    {
        var expectedRows = 32 + development;
        if (observations.Rows.Length != expectedRows || observations.PositionComparisons.Length != 8)
            throw ExperimentData.Invalid("Evaluation complete row denominator differs.");
        var losses = new[] { observations.Train, observations.Development, observations.RotatedTrainingChoice };
        for (var index = 0; index < 3; index++)
        {
            check(); var loss = losses[index];
            if (index == 1 && development == 0) { if (loss is not null) throw ExperimentData.Invalid("Stage A must not evaluate development."); continue; }
            var count = index == 0 ? 24 : 8;
            if (loss is null || loss.ExampleCount != count || loss.CompletedExamples != count || !double.IsFinite(loss.MeanLoss) || loss.MeanLoss < 0 ||
                loss.Backend != IndependentCudaMarkerHeadEvaluator.Backend || !ExperimentData.SameHash(loss.ParameterSha256, parameterHash) ||
                !ExperimentData.SameHash(loss.DeviceParameterSha256, parameterHash)) throw ExperimentData.Invalid("GPU evaluation full CE/device parameter identity differs.");
            ExperimentData.RequireHash(loss.FeatureSha256);
        }
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < expectedRows && index < 40; index++)
        {
            check(); var row = observations.Rows[index]; var count = row.CandidateIds.Length;
            if (!unique.Add(row.RecordId + ":" + row.Variant) || row.Split is not ("train" or "development") || row.Language is not ("zh" or "en") ||
                row.Type is not ("choice" or "score" or "boolean") || row.Variant is not ("original" or "fixed-left1") ||
                count != (row.Type == "choice" ? 4 : row.Type == "score" ? 5 : 2) || row.Logits.Length != count || row.Probabilities.Length != count ||
                row.OriginalCandidateIds.Length != count || row.CandidateTextSha256.Length != count || row.TokenIds.Length is < 2 or > 1024 || row.MarkerPositions.Length != count ||
                (uint)row.TeacherTargetIndex >= (uint)count || (uint)row.OriginalTeacherTargetIndex >= (uint)count || (uint)row.PredictedIndex >= (uint)count ||
                row.TeacherTargetId != row.CandidateIds[row.TeacherTargetIndex] || row.TeacherTargetId != row.OriginalCandidateIds[row.OriginalTeacherTargetIndex] ||
                row.SelectedCandidateId != row.CandidateIds[row.PredictedIndex] || row.AgreesWithTeacher != (row.SelectedCandidateId == row.TeacherTargetId) ||
                !double.IsFinite(row.GpuTeacherCe) || row.GpuTeacherCe < 0 || !double.IsFinite(row.StableSoftmaxTeacherNll) || row.StableSoftmaxTeacherNll < 0 ||
                Math.Abs(row.GpuTeacherCe - row.StableSoftmaxTeacherNll) > 2e-5 || row.Logits.Any(value => !double.IsFinite(value)) ||
                row.Probabilities.Any(value => !double.IsFinite(value) || value < 0 || value > 1) || Math.Abs(row.Probabilities.Sum() - 1) > 1e-12 ||
                row.TeacherTargetRank != 1 + row.Logits.Count(value => value > row.Logits[row.TeacherTargetIndex]) ||
                row.PredictedIndex != Array.IndexOf(row.Logits, row.Logits.Max())) throw ExperimentData.Invalid("Evaluation row/label/logit/CE/rank contract differs.");
            for (var candidate = 0; candidate < count && candidate < 5; candidate++)
            {
                check(); ExperimentData.RequireHash(row.CandidateTextSha256[candidate]);
                var expectedProbability = Math.Exp(row.Logits[candidate] - row.Logits.Max()) / row.Logits.Sum(value => Math.Exp(value - row.Logits.Max()));
                if (Math.Abs(row.Probabilities[candidate] - expectedProbability) > 1e-12) throw ExperimentData.Invalid("Probability is not the stable softmax of actual GPU logits.");
                if ((uint)row.MarkerPositions[candidate] >= (uint)row.TokenIds.Length || (candidate > 0 && row.MarkerPositions[candidate] <= row.MarkerPositions[candidate - 1]))
                    throw ExperimentData.Invalid("Evaluation marker sequence differs.");
                var expectedId = row.OriginalCandidateIds[row.Variant == "original" ? candidate : (candidate + 1) % count];
                if (row.CandidateIds[candidate] != expectedId || (row.Variant == "fixed-left1" && (row.Type != "choice" || row.Split != "train")))
                    throw ExperimentData.Invalid("Only training Choice fixed-left1 identity rotation is allowed.");
            }
            foreach (var hash in new[] { row.TokenSha256, row.MarkerSha256, row.FeatureSha256, row.OriginalTokenSha256, row.OriginalMarkerSha256, row.OriginalFeatureSha256 })
            { check(); ExperimentData.RequireHash(hash); }
            if (!ExperimentData.SameHash(row.TokenSha256, ExperimentOperations.IntHash(row.TokenIds)) ||
                !ExperimentData.SameHash(row.MarkerSha256, ExperimentOperations.IntHash(row.MarkerPositions))) throw ExperimentData.Invalid("Token/marker byte identity differs.");
        }
        if (observations.Rows.Count(row => row.Split == "train" && row.Variant == "original") != 24 ||
            observations.Rows.Count(row => row.Split == "development" && row.Variant == "original") != development ||
            observations.Rows.Count(row => row.Variant == "fixed-left1") != 8) throw ExperimentData.Invalid("Evaluation split denominator differs.");
        if (development == 0) { if (observations.DevelopmentPipeline is not null) throw ExperimentData.Invalid("Stage A has no development pipeline."); }
        else ExperimentOperations.RequirePipelineMatch(observations.DevelopmentPipeline ?? throw ExperimentData.Invalid("Development pipeline missing."), observations.Rows, check);
        var groups = ExperimentOperations.Groups(observations.Rows, check);
        if (!groups.SequenceEqual(observations.Groups)) throw ExperimentData.Invalid("Evaluation group denominators/aggregates differ.");
        var comparisons = ExperimentOperations.PositionComparisons(observations.Rows, check);
        if (!comparisons.SequenceEqual(observations.PositionComparisons)) throw ExperimentData.Invalid("Position comparison identities differ.");
        foreach (var row in observations.Rows.Where(row => row.Variant == "fixed-left1").Take(8))
        {
            check(); var original = observations.Rows.Single(value => value.RecordId == row.RecordId && value.Variant == "original");
            if (row.OriginalTeacherTargetIndex != original.TeacherTargetIndex || !row.OriginalCandidateIds.SequenceEqual(original.CandidateIds) ||
                row.OriginalTokenSha256 != original.TokenSha256 || row.OriginalMarkerSha256 != original.MarkerSha256 || row.OriginalFeatureSha256 != original.FeatureSha256 ||
                row.TokenSha256 == original.TokenSha256 ||
                !row.CandidateTextSha256.SequenceEqual(original.CandidateTextSha256.Skip(1).Concat(original.CandidateTextSha256.Take(1))))
                throw ExperimentData.Invalid("Rotated text/token/source-feature identity differs.");
        }
        var scopes = new[] { ("train", "original", observations.Train), ("development", "original", observations.Development), ("train", "fixed-left1", observations.RotatedTrainingChoice) };
        foreach (var (split, variant, loss) in scopes)
        {
            check(); if (loss is null) continue;
            var rows = observations.Rows.Where(row => row.Split == split && row.Variant == variant).ToArray();
            if (rows.Length != loss.CompletedExamples || Math.Abs(rows.Average(row => row.GpuTeacherCe) - loss.MeanLoss) > 1e-12)
                throw ExperimentData.Invalid("Per-row GPU CE differs from complete CE mean.");
        }
    }
}

internal static partial class ExperimentOperations
{
    internal const string EvaluationName = "experiment-learnability-evaluation.json";
    internal static string IntHash(int[] values) => ExperimentData.Hash(MemoryMarshal.AsBytes(values.AsSpan()));
    private static string FeatureHash(MarkerFeatureExample example, Action check)
    {
        var packed = new float[checked(example.Features.Length * example.Features[0].Length)];
        for (var candidate = 0; candidate < example.Features.Length && candidate < 32; candidate++)
        { check(); example.Features[candidate].CopyTo(packed, candidate * example.Features[0].Length); }
        try { return ExperimentData.Hash(MemoryMarshal.AsBytes(packed.AsSpan())); }
        finally { Array.Clear(packed); }
    }
    internal static ExperimentRecord Rotate(ExperimentRecord row, Action check)
    {
        check();
        if (row.Split != "train" || row.Type != MarkerQuestionType.Choice || row.Question is not IndependentChoiceQuestion choice || choice.Candidates.Count != 4)
            throw ExperimentData.Invalid("Rotation is limited to the eight four-candidate training Choice rows.");
        var candidates = new IndependentChoiceCandidate[4];
        for (var index = 0; index < 4; index++) { check(); candidates[index] = choice.Candidates[(index + 1) % 4]; }
        var question = choice with { Candidates = candidates };
        var ids = candidates.Select(candidate => candidate.Id).ToArray();
        var targetId = row.CandidateIds[row.TargetIndex]; var target = Array.IndexOf(ids, targetId);
        if (target < 0) throw ExperimentData.Invalid("Rotated teacher target ID is missing.");
        return row with { TargetIndex = target, Question = question, Request = row.Request with { Questions = [question] }, CandidateIds = ids };
    }
    private static MarkerFeatureSet ExportRotated(CudaDevice device, IndependentEncoderModelPackage model, ExperimentData data,
        MarkerFeatureIdentity identity, ExperimentBudget budget)
    {
        var rows = data.Records.Where(row => row.Split == "train" && row.Type == MarkerQuestionType.Choice).ToArray();
        if (rows.Length != 8) throw ExperimentData.Invalid("Rotated diagnostic requires exactly eight training Choice rows.");
        var examples = new MarkerFeatureExample[8];
        using (var encoder = new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, budget.Token))
        {
            for (var index = 0; index < 8; index++)
            {
                CheckGpu(device, budget); var row = Rotate(rows[index], budget.Check);
                Progress($"GPU fresh rotated feature export {index + 1}/8 {row.RecordId}");
                var sequence = IndependentDecisionV1SequenceBuilder.Build(model.Tokenizer, row.Request, row.Question,
                    new IndependentDecisionSequenceOptions { MaxTokens = 1024 }, budget.Token);
                if (!sequence.CandidateIds.SequenceEqual(row.CandidateIds)) throw ExperimentData.Invalid("Rendered rotated candidate identity differs.");
                budget.BeginEncoderForward(); var hidden = encoder.Encode(sequence.TokenIds, budget.Token); budget.CompleteEncoderForward();
                try
                {
                    if (hidden.Length != checked(sequence.TokenIds.Length * identity.HiddenSize)) throw ExperimentData.Invalid("Rotated GPU hidden tensor incomplete.");
                    var markers = new float[4][];
                    for (var candidate = 0; candidate < 4; candidate++)
                    {
                        budget.Check(); markers[candidate] = hidden.AsSpan(sequence.MarkerPositions[candidate] * identity.HiddenSize, identity.HiddenSize).ToArray();
                        for (var element = 0; element < identity.HiddenSize && element < 2048; element++)
                        { if ((element & 255) == 0) budget.Check(); if (!float.IsFinite(markers[candidate][element])) throw ExperimentData.Invalid("Rotated GPU feature nonfinite."); }
                    }
                    examples[index] = new(row.RecordId, row.Type, row.Language, sequence.TokenIds, sequence.MarkerPositions, markers, row.TargetIndex);
                }
                finally { Array.Clear(hidden); }
                CheckGpu(device, budget);
            }
        }
        RequireReleased(device, budget); return new(identity, examples);
    }
    private static (ExperimentLoss Loss, ExperimentEvaluationRow[] Rows) ScoreFeatures(string split, string variant, CudaDevice device,
        MarkerFeatureSet set, LinearMarkerHead head, ExperimentData data, ExperimentBudget budget, ExperimentEvaluationRow[]? originals = null)
    {
        CheckGpu(device, budget);
        var ce = IndependentCudaMarkerHeadEvaluator.Evaluate(device, set, head, TimeSpan.FromSeconds(300),
            item => { if (item.CompletedExamples == 1 || item.CompletedExamples == item.ExampleCount) Progress($"GPU row CE {split}/{variant} {item.CompletedExamples}/{item.ExampleCount}"); }, budget.Token);
        RequireReleased(device, budget); var rows = new ExperimentEvaluationRow[set.Examples.Count];
        var weights = head.CopyWeights(); var right = new float[weights.Length];
        try
        {
            for (var hidden = 0; hidden < head.HiddenSize && hidden < 2048; hidden++)
            { budget.Check(); for (var type = 0; type < 3; type++) right[hidden * 3 + type] = weights[type * head.HiddenSize + hidden]; }
            using (var gemm = device.CreateGemm())
            {
                for (var index = 0; index < set.Examples.Count && index < 32; index++)
                {
                    CheckGpu(device, budget); var example = set.Examples[index];
                    var source = data.Records.Single(row => row.RecordId == example.RecordId);
                    var rendered = variant == "original" ? source : Rotate(source, budget.Check);
                    var packed = new float[checked(example.Features.Length * head.HiddenSize)]; var values = new float[example.Features.Length * 3];
                    try
                    {
                        for (var candidate = 0; candidate < example.Features.Length && candidate < 32; candidate++)
                        { budget.Check(); example.Features[candidate].CopyTo(packed, candidate * head.HiddenSize); }
                        gemm.Execute(packed, example.Features.Length, head.HiddenSize, right, 3, values); CheckGpu(device, budget);
                        var logits = new double[example.Features.Length];
                        for (var candidate = 0; candidate < logits.Length && candidate < 32; candidate++)
                        { budget.Check(); logits[candidate] = values[candidate * 3 + (int)example.Type]; }
                        var max = logits.Max(); var probabilities = logits.Select(value => Math.Exp(value - max)).ToArray(); var sum = probabilities.Sum();
                        for (var candidate = 0; candidate < probabilities.Length && candidate < 32; candidate++) { budget.Check(); probabilities[candidate] /= sum; }
                        var predicted = Array.IndexOf(logits, max); var rank = 1 + logits.Count(value => value > logits[example.TargetIndex]);
                        var nll = max + Math.Log(sum) - logits[example.TargetIndex]; var gpu = ce.Rows[index];
                        if (gpu.RecordId != example.RecordId || gpu.TargetIndex != example.TargetIndex || gpu.Type != example.Type)
                            throw ExperimentData.Invalid("GPU CE row identity differs from scored logits.");
                        var tokenHash = IntHash(example.TokenIds); var markerHash = IntHash(example.MarkerPositions); var featureHash = FeatureHash(example, budget.Check);
                        var original = originals?.Single(row => row.RecordId == example.RecordId);
                        var texts = rendered.Question switch
                        {
                            IndependentChoiceQuestion choice => choice.Candidates.Select(candidate => candidate.Text).ToArray(),
                            IndependentScoreQuestion score => score.Levels.ToArray(),
                            IndependentBooleanQuestion boolean => new[] { boolean.WhenFalse, boolean.WhenTrue },
                            _ => throw ExperimentData.Invalid("Unsupported rendered diagnostic question."),
                        };
                        rows[index] = new(source.RecordId, source.Split, source.Language, source.Type.ToString().ToLowerInvariant(), variant,
                            source.CandidateIds, rendered.CandidateIds, texts.Select(value => ExperimentData.Hash(System.Text.Encoding.UTF8.GetBytes(value))).ToArray(),
                            source.CandidateIds[source.TargetIndex], source.TargetIndex, example.TargetIndex, rank, predicted, rendered.CandidateIds[predicted],
                            predicted == example.TargetIndex, logits, probabilities, gpu.Loss, nll, example.TokenIds, example.MarkerPositions,
                            tokenHash, markerHash, featureHash, original?.TokenSha256 ?? tokenHash, original?.MarkerSha256 ?? markerHash, original?.FeatureSha256 ?? featureHash);
                    }
                    finally { Array.Clear(packed); Array.Clear(values); }
                    Progress($"GPU row logits {split}/{variant} {index + 1}/{set.Examples.Count} {example.RecordId}");
                }
            }
        }
        finally { Array.Clear(weights); Array.Clear(right); }
        RequireReleased(device, budget); return (ExperimentLoss.From(split + "/" + variant, ce), rows);
    }
    internal static ExperimentEvaluationGroup[] Groups(ExperimentEvaluationRow[] rows, Action check)
    {
        check();
        var groups = rows.GroupBy(row => (row.Split, row.Variant, row.Language, row.Type)).OrderBy(group => group.Key.Split, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Variant, StringComparer.Ordinal).ThenBy(group => group.Key.Language, StringComparer.Ordinal).ThenBy(group => group.Key.Type, StringComparer.Ordinal).ToArray();
        if (groups.Length > 18) throw ExperimentData.Invalid("Evaluation grouping item bound differs.");
        var result = new ExperimentEvaluationGroup[groups.Length];
        for (var index = 0; index < groups.Length && index < 18; index++)
        {
            check(); var group = groups[index]; var total = group.Count(); var agreed = group.Count(row => row.AgreesWithTeacher);
            result[index] = new(group.Key.Split, group.Key.Variant, group.Key.Language, group.Key.Type, total, total, agreed, agreed / (double)total,
                group.Average(row => row.GpuTeacherCe), group.Average(row => row.TeacherTargetRank));
        }
        return result;
    }
    internal static ExperimentPositionComparison[] PositionComparisons(ExperimentEvaluationRow[] rows, Action check)
    {
        var rotated = rows.Where(row => row.Variant == "fixed-left1").ToArray();
        if (rotated.Length != 8) throw ExperimentData.Invalid("Position comparison requires eight rotations.");
        var result = new ExperimentPositionComparison[8];
        for (var index = 0; index < 8; index++)
        {
            check(); var row = rotated[index]; var original = rows.Single(value => value.RecordId == row.RecordId && value.Variant == "original");
            result[index] = new(row.RecordId, row.TeacherTargetId, original.SelectedCandidateId, row.SelectedCandidateId,
                original.SelectedCandidateId != row.SelectedCandidateId, original.TeacherTargetRank, row.TeacherTargetRank, original.AgreesWithTeacher, row.AgreesWithTeacher);
        }
        return result;
    }
    private static ExperimentEvaluationObservations Observe(CudaDevice device, IndependentEncoderModelPackage model, ExperimentData data,
        MarkerFeatureSet train, MarkerFeatureSet? development, IndependentMarkerHeadAsset head, ExperimentBudget budget, ExperimentInference? inference = null)
    {
        var trained = ScoreFeatures("train", "original", device, train, head.Head, data, budget);
        var dev = development is null ? ((ExperimentLoss Loss, ExperimentEvaluationRow[] Rows)?)null : ScoreFeatures("development", "original", device, development, head.Head, data, budget);
        var rotated = ExportRotated(device, model, data, train.Identity, budget);
        var swapped = ScoreFeatures("train", "fixed-left1", device, rotated, head.Head, data, budget, trained.Rows);
        var rows = trained.Rows.Concat(dev?.Rows ?? []).Concat(swapped.Rows).ToArray();
        var observations = new ExperimentEvaluationObservations(trained.Loss, dev?.Loss, swapped.Loss, inference, rows, Groups(rows, budget.Check), PositionComparisons(rows, budget.Check));
        ExperimentEvaluationReport.ValidateObservations(observations, development is null ? 0 : 8, trained.Loss.ParameterSha256, budget.Check);
        return observations;
    }
    private static ExperimentEvaluationReport MakeEvaluation(string phase, string sourceHash, ExperimentTrainingReport report,
        ExperimentEvaluationObservations observations, ExperimentResources resources, ExperimentBudget budget, bool? matched = null, string? comparedHash = null)
    {
        var result = new ExperimentEvaluationReport(ExperimentEvaluationReport.EvaluationContract, "completed_experiment_not_formal_acceptance", phase,
            "teacher_agreement_on_exposed_unreviewed_train_and_development_diagnostic_not_gold_accuracy",
            "competition_rank_1_plus_count_of_strictly_greater_logits_ties_share_rank", "first_candidate_in_rendered_order",
            "host_stable_softmax_of_gpu_fp32_logits_not_calibrated_accuracy", sourceHash, report.HeadSha256, report.CheckpointSha256, report.ParameterSha256,
            report.ManifestSha256, report.RecordsSha256, report.InputProvenanceSha256, report.ActualDataRoot, report.Identity, report.CudaArtifacts, report.Device,
            report.CompletedSteps, report.MaxTokens, budget.EncoderForwardLimit, budget.EncoderForwardsStarted, budget.EncoderForwardsCompleted,
            0, budget.ElapsedSeconds, matched, comparedHash, observations, new(), resources);
        result.Validate(budget.Check); return result;
    }
    internal static ExperimentTrainingReport ReadTrainingReport(string directory, string expectedHash, Action check, CancellationToken token)
    {
        var path = Path.Combine(directory, ReportName); ExperimentData.CheckPath(path, check);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadTrainingReportLease(stream, expectedHash, check, token);
    }
    private static ExperimentTrainingReport ReadTrainingReportLease(FileStream stream, string expectedHash, Action check, CancellationToken token)
    {
        stream.Position = 0;
        var bytes = ExperimentData.Snapshot(stream, 1_048_576, check);
        if (!ExperimentData.SameHash(ExperimentData.Hash(bytes), expectedHash)) throw ExperimentData.Invalid("Expected source report SHA differs.");
        ExperimentData.CheckJson(bytes, 1_048_576, token);
        var report = JsonSerializer.Deserialize(bytes, ExperimentJsonContext.Default.ExperimentTrainingReport) ?? throw ExperimentData.Invalid("Source report is null.");
        report.Validate(); return report;
    }
    private static void RequireDiskLeaseHash(FileStream stream, string hash, Action check)
    {
        check(); ExperimentData.CheckPath(stream.Name, check); stream.Position = 0;
        if (!ExperimentData.SameHash(ExperimentData.Hash(ExperimentData.Snapshot(stream, 1_048_576, check)), hash))
            throw ExperimentData.Invalid("Pinned read-only source artifact changed during evaluation.");
    }
    private static void RequireArtifactIdentities(CudaTrainingArtifactsIdentity expected, CudaTrainingArtifactsIdentity actual, Action check)
    {
        check();
        if (!ExperimentData.SameHash(expected.SourceSha256, actual.SourceSha256) || !ExperimentData.SameHash(expected.ManifestSha256, actual.ManifestSha256) ||
            expected.Compiler != actual.Compiler || expected.CompilerVersion != actual.CompilerVersion || expected.Target != actual.Target || expected.PtxVersion != actual.PtxVersion ||
            expected.Kernels.Count != 17 || actual.Kernels.Count != 17) throw ExperimentData.Invalid("GPU compiler/source/static PTX artifact identity differs.");
        for (var index = 0; index < 17; index++) { check(); if (expected.Kernels[index] != actual.Kernels[index]) throw ExperimentData.Invalid("GPU static PTX identity differs."); }
    }
    internal static void RequireEvaluationOutput(string output, string sourceDirectory, Action check)
    {
        check(); output = Path.GetFullPath(output); sourceDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        ExperimentData.CheckPath(output, check);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (File.Exists(output) || Directory.Exists(output) || !Directory.Exists(Path.GetDirectoryName(output)) ||
            output.Equals(sourceDirectory, comparison) || output.StartsWith(sourceDirectory + Path.DirectorySeparatorChar, comparison))
            throw ExperimentData.Invalid("Read-only evaluation output requires a fresh file outside its source directory under an existing parent.");
    }
    private static string WriteEvaluation(ExperimentOwnedFile owned, ExperimentEvaluationReport report, ExperimentBudget budget)
    {
        report.Validate(budget.Check); var bytes = JsonSerializer.SerializeToUtf8Bytes(report, ExperimentJsonContext.Default.ExperimentEvaluationReport);
        if (bytes.Length > 1_048_576) throw ExperimentData.Invalid("Evaluation report exceeds1MiB.");
        var hash = ExperimentData.Hash(bytes); owned.WriteNew(bytes, hash); budget.Check(); return hash;
    }
    private static void RequireSourceIdentity(ExperimentTrainingReport report, ExperimentData data, IndependentEncoderModelPackage model,
        CudaDevice device, CudaTrainingArtifactsIdentity artifacts, ExperimentBudget budget)
    {
        budget.Check();
        if (!ExperimentData.SameHash(report.ManifestSha256, data.ManifestSha256) || !ExperimentData.SameHash(report.RecordsSha256, data.RecordsSha256) ||
            !ExperimentData.SameHash(report.InputProvenanceSha256, data.ProvenanceSha256) || report.Identity.ModelId != model.ModelId ||
            !ExperimentData.SameHash(report.Identity.EncoderSha256, model.EncoderSha256) || !ExperimentData.SameHash(report.Identity.TokenizerSha256, model.TokenizerSha256) ||
            device.Info.Uuid != report.Device.Uuid || device.Info.DriverVersion != report.Device.DriverVersion ||
            !ExperimentData.SameHash(artifacts.SourceSha256, report.CudaArtifacts.SourceSha256) || !ExperimentData.SameHash(artifacts.ManifestSha256, report.CudaArtifacts.ManifestSha256) ||
            artifacts.Kernels.Count != report.CudaArtifacts.Kernels.Count || artifacts.Kernels.Count != 17) throw ExperimentData.Invalid("Evaluation source model/data/device/kernel identity differs.");
        RequireArtifactIdentities(report.CudaArtifacts, artifacts, budget.Check);
    }
    internal static void RequireObservationsMatch(ExperimentEvaluationObservations expected, ExperimentEvaluationObservations actual, Action check)
    {
        check();
        if (expected.Rows.Length != actual.Rows.Length || expected.Groups.Length != actual.Groups.Length || !expected.PositionComparisons.SequenceEqual(actual.PositionComparisons))
            throw ExperimentData.Invalid("Fresh evaluation denominator/position decisions differ.");
        var oldLosses = new[] { expected.Train, expected.Development, expected.RotatedTrainingChoice };
        var newLosses = new[] { actual.Train, actual.Development, actual.RotatedTrainingChoice };
        for (var index = 0; index < 3; index++)
        {
            check(); var old = oldLosses[index]; var fresh = newLosses[index];
            if (old is null || fresh is null || old.ExampleCount != fresh.ExampleCount || old.CompletedExamples != fresh.CompletedExamples ||
                !ExperimentData.SameHash(old.FeatureSha256, fresh.FeatureSha256) || !ExperimentData.SameHash(old.ParameterSha256, fresh.ParameterSha256) ||
                !ExperimentData.SameHash(old.DeviceParameterSha256, fresh.DeviceParameterSha256) || Math.Abs(old.MeanLoss - fresh.MeanLoss) > 2e-6)
                throw ExperimentData.Invalid("Fresh complete GPU CE/feature/device parameter differs.");
        }
        for (var index = 0; index < actual.Rows.Length && index < 40; index++)
        {
            check(); var old = expected.Rows[index]; var fresh = actual.Rows[index];
            if (old.RecordId != fresh.RecordId || old.Split != fresh.Split || old.Language != fresh.Language || old.Type != fresh.Type || old.Variant != fresh.Variant ||
                !old.OriginalCandidateIds.SequenceEqual(fresh.OriginalCandidateIds) || !old.CandidateIds.SequenceEqual(fresh.CandidateIds) || !old.CandidateTextSha256.SequenceEqual(fresh.CandidateTextSha256) ||
                old.TeacherTargetId != fresh.TeacherTargetId || old.OriginalTeacherTargetIndex != fresh.OriginalTeacherTargetIndex || old.TeacherTargetIndex != fresh.TeacherTargetIndex ||
                old.TeacherTargetRank != fresh.TeacherTargetRank || old.PredictedIndex != fresh.PredictedIndex || old.SelectedCandidateId != fresh.SelectedCandidateId || old.AgreesWithTeacher != fresh.AgreesWithTeacher ||
                !old.TokenIds.SequenceEqual(fresh.TokenIds) || !old.MarkerPositions.SequenceEqual(fresh.MarkerPositions) || old.TokenSha256 != fresh.TokenSha256 || old.MarkerSha256 != fresh.MarkerSha256 ||
                old.FeatureSha256 != fresh.FeatureSha256 || old.OriginalTokenSha256 != fresh.OriginalTokenSha256 || old.OriginalMarkerSha256 != fresh.OriginalMarkerSha256 || old.OriginalFeatureSha256 != fresh.OriginalFeatureSha256 ||
                Math.Abs(old.GpuTeacherCe - fresh.GpuTeacherCe) > 2e-6 || Math.Abs(old.StableSoftmaxTeacherNll - fresh.StableSoftmaxTeacherNll) > 2e-6 ||
                old.Logits.Length != fresh.Logits.Length || old.Probabilities.Length != fresh.Probabilities.Length) throw ExperimentData.Invalid("Fresh per-row identity/rank/CE differs.");
            for (var candidate = 0; candidate < fresh.Logits.Length && candidate < 5; candidate++)
            { check(); if (Math.Abs(old.Logits[candidate] - fresh.Logits[candidate]) > 2e-6 || Math.Abs(old.Probabilities[candidate] - fresh.Probabilities[candidate]) > 2e-6) throw ExperimentData.Invalid("Fresh per-row GPU logits/probabilities differ."); }
        }
        RequirePipelineMatch(expected.DevelopmentPipeline ?? throw ExperimentData.Invalid("Expected development pipeline missing."), actual.Rows, check);
    }
    internal static void RequirePipelineMatch(ExperimentInference pipeline, ExperimentEvaluationRow[] rows, Action check)
    {
        check(); var development = rows.Where(row => row.Split == "development" && row.Variant == "original").ToArray();
        if (development.Length != 8 || pipeline.Total != 8 || pipeline.Completed != 8 || pipeline.Rows.Length != 8 ||
            pipeline.Backend != "cuda-independent-linear-marker-head" || pipeline.TeacherAgreementCount != development.Count(row => row.AgreesWithTeacher) ||
            pipeline.TeacherAgreementRate != pipeline.TeacherAgreementCount / 8d || Math.Abs(pipeline.MeanTeacherNll - development.Average(row => row.StableSoftmaxTeacherNll)) > 2e-6)
            throw ExperimentData.Invalid("Development full pipeline differs from GPU feature/logit observations.");
        for (var index = 0; index < 8; index++)
        {
            check(); var row = development[index]; var inferred = pipeline.Rows[index];
            if (row.RecordId != inferred.RecordId || row.Language != inferred.Language || row.Type != inferred.Type || !row.CandidateIds.SequenceEqual(inferred.CandidateIds) ||
                row.TeacherTargetIndex != inferred.TeacherTargetIndex || row.PredictedIndex != inferred.PredictedIndex || row.SelectedCandidateId != inferred.SelectedCandidateId ||
                row.AgreesWithTeacher != inferred.AgreesWithTeacher || Math.Abs(row.StableSoftmaxTeacherNll - inferred.TeacherNll) > 2e-6 ||
                row.Logits.Length != inferred.Logits.Length || row.Probabilities.Length != inferred.Probabilities.Length) throw ExperimentData.Invalid("Development pipeline row identity differs.");
            for (var candidate = 0; candidate < row.Logits.Length && candidate < 5; candidate++)
            { check(); if (Math.Abs(row.Logits[candidate] - inferred.Logits[candidate]) > 2e-6 || Math.Abs(row.Probabilities[candidate] - inferred.Probabilities[candidate]) > 2e-6) throw ExperimentData.Invalid("Development pipeline GPU logits/probabilities differ."); }
        }
    }
    internal static void Evaluate(ExperimentOptions options, ExperimentData data, ExperimentBudget budget)
    {
        budget.EncoderForwardLimit = 32; RequireEvaluationOutput(options.EvaluationOutput!, options.Output, budget.Check);
        var owned = new ExperimentOwnedFile(options.EvaluationOutput!); var committed = false; Exception? primary = null;
        try
        {
            ExperimentData.CheckPath(Path.Combine(options.Output, ReportName), budget.Check); ExperimentData.CheckPath(Path.Combine(options.Output, HeadName), budget.Check); ExperimentData.CheckPath(Path.Combine(options.Output, CheckpointName), budget.Check);
            using var reportLease = new FileStream(Path.Combine(options.Output, ReportName), FileMode.Open, FileAccess.Read, FileShare.Read);
            using var headLease = new FileStream(Path.Combine(options.Output, HeadName), FileMode.Open, FileAccess.Read, FileShare.Read);
            using var checkpointLease = new FileStream(Path.Combine(options.Output, CheckpointName), FileMode.Open, FileAccess.Read, FileShare.Read);
            var report = ReadTrainingReportLease(reportLease, options.ReportSha256!, budget.Check, budget.Token);
            if (report.MaxTokens != 1024) throw ExperimentData.Invalid("Learnability evaluation requires source strict1024 identity.");
            RequireDiskLeaseHash(headLease, report.HeadSha256, budget.Check); RequireDiskLeaseHash(checkpointLease, report.CheckpointSha256, budget.Check);
            using var model = LoadModel(options.Package, budget); using var device = CudaDevice.Open(options.Device); CheckGpu(device, budget);
            var artifacts = CudaTrainingExecutionIdentity.Read(budget.Token); RequireSourceIdentity(report, data, model, device, artifacts, budget);
            var features = Export(device, model, data, report.MaxTokens, budget, includeDevelopment: false);
            var checkpoint = MarkerTrainingCheckpoint.Load(Path.Combine(options.Output, CheckpointName), features.Train, report.CheckpointSha256, budget.Token);
            var head = IndependentMarkerHeadAssetStore.Load(Path.Combine(options.Output, HeadName), features.Train.Identity, report.FeatureSha256, report.HeadSha256, budget.Token);
            RequireRoundtrip(checkpoint, checkpoint, head, report.ParameterSha256); RequireMetadata(checkpoint, report);
            var observations = Observe(device, model, data, features.Train, null, head, budget);
            if (Math.Abs(observations.Train.MeanLoss - report.LoadedTrain.MeanLoss) > 2e-6) throw ExperimentData.Invalid("Existing head complete train CE differs from v1 report.");
            RequireDiskLeaseHash(reportLease, options.ReportSha256!, budget.Check); RequireDiskLeaseHash(headLease, report.HeadSha256, budget.Check); RequireDiskLeaseHash(checkpointLease, report.CheckpointSha256, budget.Check);
            var resources = CloseResources(device, model, budget);
            var result = MakeEvaluation("existing_head_train24", options.ReportSha256!, report, observations, resources, budget) with { ActualDataRoot = data.Root };
            var hash = WriteEvaluation(owned, result, budget); committed = true;
            Console.WriteLine(JsonSerializer.Serialize(result, ExperimentJsonContext.Default.ExperimentEvaluationReport));
            Progress("evaluation_sha256=" + hash);
        }
        catch (Exception error) { primary = error; }
        CompleteEvaluationRollback(owned, committed, primary);
    }
    private static void RequireMetadata(MarkerTrainingState checkpoint, ExperimentTrainingReport report)
    {
        if (checkpoint.CompletedSteps != report.CompletedSteps || checkpoint.Seed != report.Seed || checkpoint.LearningRate != report.LearningRate ||
            checkpoint.LastLoss != report.LastStepPreUpdateSelectedExampleLoss) throw ExperimentData.Invalid("Disk optimizer metadata differs from v1 report.");
    }
    private static void ValidateLearnability(ExperimentOptions options, ExperimentData data, ExperimentBudget budget)
    {
        RequireEvaluationOutput(options.EvaluationOutput!, options.Output, budget.Check);
        var owned = new ExperimentOwnedFile(options.EvaluationOutput!); var committed = false; Exception? primary = null;
        try
        {
            ExperimentData.CheckPath(Path.Combine(options.Output, ReportName), budget.Check); ExperimentData.CheckPath(Path.Combine(options.Output, HeadName), budget.Check); ExperimentData.CheckPath(Path.Combine(options.Output, CheckpointName), budget.Check);
            using var reportLease = new FileStream(Path.Combine(options.Output, ReportName), FileMode.Open, FileAccess.Read, FileShare.Read);
            using var headLease = new FileStream(Path.Combine(options.Output, HeadName), FileMode.Open, FileAccess.Read, FileShare.Read);
            using var checkpointLease = new FileStream(Path.Combine(options.Output, CheckpointName), FileMode.Open, FileAccess.Read, FileShare.Read);
            var report = ReadTrainingReportLease(reportLease, options.ReportSha256!, budget.Check, budget.Token);
            if (report.MaxTokens != 1024 || report.CompletedSteps != 768 || report.Seed != 17 || report.LearningRate != 0.001f || report.TrainingTimeoutSeconds > 300 || report.OverallTimeoutSeconds > 460)
                throw ExperimentData.Invalid("Fresh learnability validation requires the fixed768step source experiment.");
            RequireDiskLeaseHash(headLease, report.HeadSha256, budget.Check); RequireDiskLeaseHash(checkpointLease, report.CheckpointSha256, budget.Check);
            var evaluationPath = Path.Combine(options.Output, EvaluationName); ExperimentData.CheckPath(evaluationPath, budget.Check);
            using var evaluationLease = new FileStream(evaluationPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var evaluationBytes = ExperimentData.Snapshot(evaluationLease, 1_048_576, budget.Check);
            if (!ExperimentData.SameHash(ExperimentData.Hash(evaluationBytes), options.EvaluationSha256)) throw ExperimentData.Invalid("Expected evaluation report SHA differs.");
            ExperimentData.CheckJson(evaluationBytes, 1_048_576, budget.Token);
            var expected = JsonSerializer.Deserialize(evaluationBytes, ExperimentJsonContext.Default.ExperimentEvaluationReport) ?? throw ExperimentData.Invalid("Expected evaluation report null.");
            expected.Validate(budget.Check);
            if (expected.Phase != "new_head_disk_loaded_same_process" || !ExperimentData.SameHash(expected.SourceReportSha256, options.ReportSha256) ||
                !ExperimentData.SameHash(expected.HeadSha256, report.HeadSha256) || !ExperimentData.SameHash(expected.CheckpointSha256, report.CheckpointSha256) ||
                !ExperimentData.SameHash(expected.ParameterSha256, report.ParameterSha256) || !ExperimentData.SameHash(expected.ManifestSha256, data.ManifestSha256) ||
                !ExperimentData.SameHash(expected.RecordsSha256, data.RecordsSha256) || !ExperimentData.SameHash(expected.InputProvenanceSha256, data.ProvenanceSha256) ||
                expected.Identity != report.Identity || expected.CompletedSteps != report.CompletedSteps || expected.MaxTokens != report.MaxTokens || expected.Device != report.Device)
                throw ExperimentData.Invalid("Expected evaluation does not bind this exact training/source identity.");
            using var model = LoadModel(options.Package, budget); using var device = CudaDevice.Open(options.Device); CheckGpu(device, budget);
            var artifacts = CudaTrainingExecutionIdentity.Read(budget.Token); RequireSourceIdentity(report, data, model, device, artifacts, budget);
            RequireArtifactIdentities(expected.CudaArtifacts, artifacts, budget.Check);
            var features = Export(device, model, data, report.MaxTokens, budget);
            var checkpoint = MarkerTrainingCheckpoint.Load(Path.Combine(options.Output, CheckpointName), features.Train, report.CheckpointSha256, budget.Token);
            var head = IndependentMarkerHeadAssetStore.Load(Path.Combine(options.Output, HeadName), features.Train.Identity, report.FeatureSha256, report.HeadSha256, budget.Token);
            RequireRoundtrip(checkpoint, checkpoint, head, report.ParameterSha256); RequireMetadata(checkpoint, report);
            var inference = Infer(device, model, head, data, report.MaxTokens, budget);
            var observations = Observe(device, model, data, features.Train, features.Development, head, budget, inference);
            RequireObservationsMatch(expected.Observations, observations, budget.Check);
            if (Math.Abs(observations.Train.MeanLoss - report.LoadedTrain.MeanLoss) > 2e-6 ||
                Math.Abs(observations.Development!.MeanLoss - report.LoadedDevelopment.MeanLoss) > 2e-6) throw ExperimentData.Invalid("Fresh complete GPU CE differs from v1 training report.");
            RequirePipelineMatch(report.LoadedInference, observations.Rows, budget.Check);
            RequireDiskLeaseHash(reportLease, options.ReportSha256!, budget.Check); RequireDiskLeaseHash(headLease, report.HeadSha256, budget.Check);
            RequireDiskLeaseHash(checkpointLease, report.CheckpointSha256, budget.Check); RequireDiskLeaseHash(evaluationLease, options.EvaluationSha256!, budget.Check);
            var resources = CloseResources(device, model, budget);
            var result = MakeEvaluation("fresh_process_disk_loaded", options.ReportSha256!, report, observations, resources, budget, true, options.EvaluationSha256) with { ActualDataRoot = data.Root };
            var hash = WriteEvaluation(owned, result, budget); committed = true;
            Console.WriteLine(JsonSerializer.Serialize(result, ExperimentJsonContext.Default.ExperimentEvaluationReport));
            Progress("evaluation_sha256=" + hash);
        }
        catch (Exception error) { primary = error; }
        CompleteEvaluationRollback(owned, committed, primary);
    }
    private static void CompleteEvaluationRollback(ExperimentOwnedFile owned, bool committed, Exception? primary)
    {
        try { if (!committed) CleanupEvaluation(owned); }
        catch (Exception cleanup)
        {
            if (primary is not null) throw new AggregateException("Evaluation failed and owned-output rollback failed.", primary, cleanup);
            throw;
        }
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }
    private static void CleanupEvaluation(ExperimentOwnedFile owned)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        owned.Cleanup(() => { if (clock.Elapsed.TotalSeconds > 10) throw new IOException("Evaluation rollback deadline expired."); });
    }
}
