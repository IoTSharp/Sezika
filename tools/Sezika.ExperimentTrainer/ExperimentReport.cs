using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

namespace Sezika.ExperimentTrainer;

internal sealed class ExperimentAcceptance
{
    public bool FormalTrainingAdmission { get; init; }
    public bool HumanQuality { get; init; }
    public bool LicenseRelease { get; init; }
    public bool Calibration { get; init; }
    public bool SealedTest { get; init; }
    public bool NativeAot { get; init; }
    public bool Performance { get; init; }
    public bool Publication { get; init; }
    internal void Validate()
    {
        if (FormalTrainingAdmission || HumanQuality || LicenseRelease || Calibration || SealedTest || NativeAot || Performance || Publication)
            throw ExperimentData.Invalid("Experiment cannot assert formal acceptance.");
    }
}
internal sealed record ExperimentLoss(string Scope, string Backend, int ExampleCount, int CompletedExamples, double MeanLoss,
    string FeatureSha256, string ParameterSha256, string DeviceParameterSha256)
{
    internal static ExperimentLoss From(string scope, MarkerHeadEvaluationResult value) => new(scope, IndependentCudaMarkerHeadEvaluator.Backend,
        value.ExampleCount, value.CompletedExamples, value.MeanLoss, value.FeatureSha256, value.ParameterSha256, value.DeviceParameterSha256);
}
internal sealed record ExperimentPrediction(string RecordId, string Language, string Type, string[] CandidateIds, int TeacherTargetIndex,
    int PredictedIndex, string SelectedCandidateId, double[] Logits, double[] Probabilities, double TeacherNll, bool AgreesWithTeacher);
internal sealed record ExperimentInference(string Backend, string Use, int Total, int Completed, int TeacherAgreementCount,
    double TeacherAgreementRate, double MeanTeacherNll, ExperimentPrediction[] Rows);
internal sealed record ExperimentDevice(string Uuid, string Name, int DriverVersion, int ComputeCapabilityMajor,
    int ComputeCapabilityMinor, ulong TotalMemoryBytes, int DeviceIndex);
internal sealed record ExperimentResources(CudaMemorySnapshot SnapshotBeforeContextDispose, long ProcessPeakWorkingSetBytes,
    long ProcessWorkingSetBytes, bool DeviceDisposeReturned, bool? ModelDisposeReturned);
internal sealed class ExperimentTrainingReport
{
    internal const string Contract = "sezika/teacher-labelled-gpu-experiment-training/v1";
    public required int SchemaVersion { get; init; }
    public required string TrainingContract { get; init; }
    public required string Status { get; init; }
    public required string DataMode { get; init; }
    public required string FeatureBackend { get; init; }
    public required string TrainingBackend { get; init; }
    public required bool EncoderFrozen { get; init; }
    public required string InitialParameterSource { get; init; }
    public required MarkerFeatureIdentity Identity { get; init; }
    public required string ManifestSha256 { get; init; }
    public required string RecordsSha256 { get; init; }
    public required string InputProvenanceSha256 { get; init; }
    public required string ActualDataRoot { get; init; }
    public required string OriginalSourceVerification { get; init; }
    public required ExperimentManifest DataDeclaration { get; init; }
    public required CudaTrainingArtifactsIdentity CudaArtifacts { get; init; }
    public required ExperimentDevice Device { get; init; }
    public required ExperimentResources Resources { get; init; }
    public required int Seed { get; init; }
    public required float LearningRate { get; init; }
    public required int CompletedSteps { get; init; }
    public required string StepSemantics { get; init; }
    public required double LastStepPreUpdateSelectedExampleLoss { get; init; }
    public required bool ParametersChangedFromZero { get; init; }
    public required int TrainCount { get; init; }
    public required int DevelopmentCount { get; init; }
    public required int MaxTokens { get; init; }
    public required int TrainingTimeoutSeconds { get; init; }
    public required int OverallTimeoutSeconds { get; init; }
    public required long OwnedGpuLimitBytes { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required string FeatureSha256 { get; init; }
    public required string CheckpointSha256 { get; init; }
    public required string HeadSha256 { get; init; }
    public required string ParameterSha256 { get; init; }
    public required bool CheckpointAndHeadRoundtripMatched { get; init; }
    public required ExperimentLoss BaselineTrain { get; init; }
    public required ExperimentLoss BaselineDevelopment { get; init; }
    public required ExperimentLoss TrainedTrain { get; init; }
    public required ExperimentLoss TrainedDevelopment { get; init; }
    public required ExperimentLoss LoadedTrain { get; init; }
    public required ExperimentLoss LoadedDevelopment { get; init; }
    public required ExperimentInference LoadedInference { get; init; }
    public required ExperimentAcceptance Acceptance { get; init; }

    internal void Validate()
    {
        Acceptance.Validate(); ExperimentData.ValidateManifest(DataDeclaration);
        if (SchemaVersion != 1 || TrainingContract != Contract || Status != "completed_experiment_not_formal_acceptance" ||
            DataMode != "teacher_labelled_experiment" || FeatureBackend != "cuda-modernbert-fp32-v1" ||
            TrainingBackend != IndependentCudaMarkerHeadTrainer.Backend || !EncoderFrozen || InitialParameterSource != "all_zero_three_linear_rows" ||
            Identity is null || Identity.ModelId != IndependentModelLoader.ModelId || Identity.HiddenSize != 768 ||
            !ExperimentData.SameHash(Identity.EncoderSha256, IndependentModelLoader.ConvertedEncoderSha256) ||
            !ExperimentData.SameHash(Identity.TokenizerSha256, IndependentModelLoader.TokenizerSha256) ||
            !ExperimentData.SameHash(Identity.DataManifestSha256, ManifestSha256) || Identity.Protocol != "decision-v1" || Identity.LengthPolicy != "strict" ||
            CompletedSteps is < 1 or > 768 || !ParametersChangedFromZero || !CheckpointAndHeadRoundtripMatched ||
            StepSemantics != "total_committed_steps_no_resume" || TrainCount != 24 || DevelopmentCount != 8 ||
            MaxTokens is < 64 or > 1024 || TrainingTimeoutSeconds is < 1 or > 600 || OverallTimeoutSeconds is < 1 or > 1200 ||
            OwnedGpuLimitBytes != 3L * 1024 * 1024 * 1024 || !float.IsFinite(LearningRate) || LearningRate <= 0 || LearningRate > 0.1f ||
            !double.IsFinite(LastStepPreUpdateSelectedExampleLoss) || LastStepPreUpdateSelectedExampleLoss < 0 ||
            !double.IsFinite(ElapsedSeconds) || ElapsedSeconds < 0 || Resources is null || !Resources.DeviceDisposeReturned || Resources.ModelDisposeReturned != true ||
            Resources.SnapshotBeforeContextDispose.OwnedBytes != 0 || Resources.SnapshotBeforeContextDispose.OwnedAllocationCount != 0 ||
            Resources.SnapshotBeforeContextDispose.LoadedModuleCount != 0 || Resources.SnapshotBeforeContextDispose.ReleaseFailureCount != 0 ||
            Resources.SnapshotBeforeContextDispose.PeakOwnedBytes > 3UL * 1024 * 1024 * 1024 || Resources.ProcessPeakWorkingSetBytes > 4L * 1024 * 1024 * 1024)
            throw ExperimentData.Invalid("Experiment training report contract is invalid.");
        foreach (var hash in new[] { ManifestSha256, RecordsSha256, InputProvenanceSha256, FeatureSha256, CheckpointSha256, HeadSha256, ParameterSha256 }) ExperimentData.RequireHash(hash);
        var losses = new[] { BaselineTrain, BaselineDevelopment, TrainedTrain, TrainedDevelopment, LoadedTrain, LoadedDevelopment };
        for (var index = 0; index < 6; index++)
        {
            var loss = losses[index]; var expected = index % 2 == 0 ? 24 : 8;
            if (loss is null || loss.ExampleCount != expected || loss.CompletedExamples != expected || !double.IsFinite(loss.MeanLoss) || loss.MeanLoss < 0 ||
                loss.Backend != IndependentCudaMarkerHeadEvaluator.Backend || !ExperimentData.SameHash(loss.ParameterSha256, loss.DeviceParameterSha256))
                throw ExperimentData.Invalid("Experiment measured CE denominator or device parameter identity differs.");
            ExperimentData.RequireHash(loss.FeatureSha256);
        }
        if (!ExperimentData.SameHash(TrainedTrain.ParameterSha256, ParameterSha256) || !ExperimentData.SameHash(LoadedTrain.ParameterSha256, ParameterSha256) ||
            !ExperimentData.SameHash(TrainedDevelopment.ParameterSha256, ParameterSha256) || !ExperimentData.SameHash(LoadedDevelopment.ParameterSha256, ParameterSha256))
            throw ExperimentData.Invalid("Measured trained/loaded parameter identity differs.");
        if (LoadedInference is null || LoadedInference.Backend != "cuda-independent-linear-marker-head" || LoadedInference.Total != 8 ||
            LoadedInference.Completed != 8 || LoadedInference.Rows.Length != 8 || LoadedInference.TeacherAgreementCount is < 0 or > 8 ||
            LoadedInference.TeacherAgreementRate != LoadedInference.TeacherAgreementCount / 8d ||
            !double.IsFinite(LoadedInference.MeanTeacherNll) || LoadedInference.MeanTeacherNll < 0)
            throw ExperimentData.Invalid("Loaded inference full denominator is invalid.");
    }
}
internal sealed class ExperimentValidationReport
{
    public required string Contract { get; init; }
    public required string Status { get; init; }
    public required string SourceReportSha256 { get; init; }
    public required string HeadSha256 { get; init; }
    public required string CheckpointSha256 { get; init; }
    public required string FeatureSha256 { get; init; }
    public required string ParameterSha256 { get; init; }
    public required string ActualDataRoot { get; init; }
    public required ExperimentLoss LoadedTrain { get; init; }
    public required ExperimentLoss LoadedDevelopment { get; init; }
    public required ExperimentInference LoadedInference { get; init; }
    public required ExperimentAcceptance Acceptance { get; init; }
    public required ExperimentResources Resources { get; init; }
}
internal sealed record ExperimentGpuCheckReport(string Status, int PassedChecks, double MaximumLossAbsoluteError,
    bool SyntheticFeaturesOnly, bool TrainingExecuted, ExperimentResources Resources);
internal sealed record ExperimentLengthRow(string RecordId, string Language, string Split, string Type,
    int TokenCount, int[] MarkerPositions, string[] CandidateIds);
internal sealed record ExperimentLengthReport(string Contract, string Status, string ModelId,
    string ManifestSha256, string RecordsSha256, string InputProvenanceSha256, string TokenizerSha256,
    string ActualDataRoot, string ActualTokenizerPath, int DataRecords, int RequestedRecords, int CompletedRecords,
    int MaxAllowedTokens, int MaximumMeasuredTokens, int TimeoutSeconds, double ElapsedSeconds,
    bool EncoderLoaded, bool FeatureComputed, bool TrainingExecuted, bool GpuOpened,
    long ProcessPeakWorkingSetBytes, ExperimentLengthRow[] Rows);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true)]
[JsonSerializable(typeof(ExperimentManifest))]
[JsonSerializable(typeof(ExperimentRecordJson))]
[JsonSerializable(typeof(ExperimentTrainingReport))]
[JsonSerializable(typeof(ExperimentValidationReport))]
[JsonSerializable(typeof(ExperimentGpuCheckReport))]
[JsonSerializable(typeof(ExperimentLengthReport))]
[JsonSerializable(typeof(ExperimentEvaluationReport))]
[JsonSerializable(typeof(ExpansionTrainingReport))]
[JsonSerializable(typeof(ExpansionValidationReport))]
[JsonSerializable(typeof(ExperimentEvaluationRow))]
internal partial class ExperimentJsonContext : JsonSerializerContext;

internal sealed record ExpansionObservations(ExperimentLoss Train, ExperimentLoss Development, ExperimentLoss RotatedTrainingChoice,
    ExperimentEvaluationRow[] Rows, ExperimentEvaluationGroup[] Groups, ExperimentPositionComparison[] PositionComparisons);
internal sealed record ExpansionTrainingReport(string TrainingContract, string Status, ExperimentManifest DataDeclaration,
    string ManifestSha256, string RecordsSha256, string InputProvenanceSha256, string ActualDataRoot,
    MarkerFeatureIdentity Identity, CudaTrainingArtifactsIdentity CudaArtifacts, ExperimentDevice Device,
    int Seed, float LearningRate, int CompletedSteps, double LastStepPreUpdateSelectedExampleLoss,
    bool EncoderFrozen, string InitialParameterSource, string TrainingBackend, string FeatureBackend,
    string StepSemantics, int TrainCount, int DevelopmentCount, int MaxTokens, int TrainingTimeoutSeconds,
    int OverallTimeoutSeconds, int EncoderForwardLimit, int EncoderForwardsStarted, int EncoderForwardsCompleted,
    string FeatureSha256, string ParameterSha256, string HeadSha256, string CheckpointSha256,
    bool CheckpointAndHeadRoundtripMatched, ExperimentLoss BaselineTrain, ExperimentLoss BaselineDevelopment,
    ExperimentLoss TrainedTrain, ExperimentLoss TrainedDevelopment, ExpansionObservations LoadedObservations,
    ExperimentAcceptance Acceptance, ExperimentResources Resources, double ElapsedSeconds)
{
    internal const string Contract = "sezika/teacher-labelled-gpu-expanded-experiment-training/v2";
    internal void Validate(Action check)
    {
        check(); Acceptance.Validate(); ExperimentData.ValidateExpandedManifest(DataDeclaration);
        if (TrainingContract != Contract || Status != "completed_experiment_not_formal_acceptance" || Seed != 17 || LearningRate != 0.001f ||
            CompletedSteps != 24_576 || !double.IsFinite(LastStepPreUpdateSelectedExampleLoss) || LastStepPreUpdateSelectedExampleLoss < 0 ||
            !EncoderFrozen || InitialParameterSource != "all_zero_three_linear_rows" || TrainingBackend != IndependentCudaMarkerHeadTrainer.Backend ||
            FeatureBackend != "cuda-modernbert-fp32-v1" || StepSemantics != "24576_total_committed_updates_128_complete_192_example_epochs_no_resume" ||
            TrainCount != 192 || DevelopmentCount != 64 || MaxTokens != 1024 || TrainingTimeoutSeconds is < 1 or > 300 || OverallTimeoutSeconds is < 1 or > 1500 ||
            EncoderForwardLimit != 384 || EncoderForwardsStarted != 272 || EncoderForwardsCompleted != 272 || !CheckpointAndHeadRoundtripMatched ||
            Identity.ModelId != IndependentModelLoader.ModelId || Identity.HiddenSize != 768 || Identity.Protocol != "decision-v1" || Identity.LengthPolicy != "strict" ||
            !ExperimentData.SameHash(Identity.EncoderSha256, IndependentModelLoader.ConvertedEncoderSha256) || !ExperimentData.SameHash(Identity.TokenizerSha256, IndependentModelLoader.TokenizerSha256) ||
            !ExperimentData.SameHash(Identity.DataManifestSha256, ManifestSha256) || !double.IsFinite(ElapsedSeconds) || ElapsedSeconds < 0 || ElapsedSeconds > OverallTimeoutSeconds)
            throw ExperimentData.Invalid("Expanded execution contract differs.");
        ValidateResources(Resources);
        foreach (var hash in new[] { ManifestSha256, RecordsSha256, InputProvenanceSha256, FeatureSha256, ParameterSha256, HeadSha256, CheckpointSha256 }) { check(); ExperimentData.RequireHash(hash); }
        ValidateLoss(BaselineTrain, 192, null); ValidateLoss(BaselineDevelopment, 64, null);
        ValidateLoss(TrainedTrain, 192, ParameterSha256); ValidateLoss(TrainedDevelopment, 64, ParameterSha256);
        if (!ExperimentData.SameHash(BaselineTrain.ParameterSha256, BaselineDevelopment.ParameterSha256) ||
            !ExperimentData.SameHash(BaselineTrain.FeatureSha256, FeatureSha256) || !ExperimentData.SameHash(TrainedTrain.FeatureSha256, FeatureSha256) ||
            Math.Abs(TrainedTrain.MeanLoss - LoadedObservations.Train.MeanLoss) > 2e-6 || Math.Abs(TrainedDevelopment.MeanLoss - LoadedObservations.Development.MeanLoss) > 2e-6)
            throw ExperimentData.Invalid("Expanded baseline/trained/disk CE identity differs.");
        ValidateObservations(LoadedObservations, ParameterSha256, check);
    }
    internal static void ValidateResources(ExperimentResources resources)
    {
        var snapshot = resources.SnapshotBeforeContextDispose;
        if (!resources.DeviceDisposeReturned || resources.ModelDisposeReturned != true || snapshot.OwnedBytes != 0 || snapshot.OwnedAllocationCount != 0 ||
            snapshot.LoadedModuleCount != 0 || snapshot.ReleaseFailureCount != 0 || snapshot.PeakOwnedBytes > ExperimentOperations.GpuLimit ||
            resources.ProcessPeakWorkingSetBytes > 4L * 1024 * 1024 * 1024) throw ExperimentData.Invalid("Expanded task-owned resource release/limit differs.");
    }
    internal static void ValidateLoss(ExperimentLoss loss, int count, string? parameterHash)
    {
        if (loss.Backend != IndependentCudaMarkerHeadEvaluator.Backend || loss.ExampleCount != count || loss.CompletedExamples != count || !double.IsFinite(loss.MeanLoss) || loss.MeanLoss < 0 ||
            !ExperimentData.SameHash(loss.ParameterSha256, loss.DeviceParameterSha256) || (parameterHash is not null && !ExperimentData.SameHash(loss.ParameterSha256, parameterHash)))
            throw ExperimentData.Invalid("Expanded complete GPU CE/device parameter denominator differs.");
        ExperimentData.RequireHash(loss.FeatureSha256); ExperimentData.RequireHash(loss.ParameterSha256);
    }
    internal static void ValidateObservations(ExpansionObservations observations, string parameters, Action check)
    {
        ValidateLoss(observations.Train, 192, parameters); ValidateLoss(observations.Development, 64, parameters); ValidateLoss(observations.RotatedTrainingChoice, 16, parameters);
        if (observations.Rows.Length != 272 || observations.PositionComparisons.Length != 16 || observations.Rows.Count(row => row.Split == "train" && row.Variant == "original") != 192 ||
            observations.Rows.Count(row => row.Split == "development" && row.Variant == "original") != 64 || observations.Rows.Count(row => row.Split == "train" && row.Variant == "fixed-left1") != 16)
            throw ExperimentData.Invalid("Expanded original/rotation row denominator differs.");
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 272; index++)
        {
            check(); var row = observations.Rows[index]; var count = row.CandidateIds.Length;
            if (!unique.Add(row.RecordId + ":" + row.Variant) || row.Language is not ("zh" or "en") || row.Split is not ("train" or "development") ||
                row.Variant is not ("original" or "fixed-left1") || row.Type is not ("choice" or "score" or "boolean") ||
                (row.Type == "choice" ? count is < 2 or > 8 : count != (row.Type == "score" ? 5 : 2)) || row.OriginalCandidateIds.Length != count || row.CandidateTextSha256.Length != count ||
                row.Logits.Length != count || row.Probabilities.Length != count || row.TokenIds.Length is < 2 or > 1024 || row.MarkerPositions.Length != count ||
                (uint)row.TeacherTargetIndex >= (uint)count || (uint)row.OriginalTeacherTargetIndex >= (uint)count || (uint)row.PredictedIndex >= (uint)count ||
                row.TeacherTargetId != row.CandidateIds[row.TeacherTargetIndex] || row.TeacherTargetId != row.OriginalCandidateIds[row.OriginalTeacherTargetIndex] ||
                row.SelectedCandidateId != row.CandidateIds[row.PredictedIndex] || row.AgreesWithTeacher != (row.SelectedCandidateId == row.TeacherTargetId) ||
                !double.IsFinite(row.GpuTeacherCe) || row.GpuTeacherCe < 0 || !double.IsFinite(row.StableSoftmaxTeacherNll) || row.StableSoftmaxTeacherNll < 0 || Math.Abs(row.GpuTeacherCe - row.StableSoftmaxTeacherNll) > 2e-5 ||
                row.Logits.Any(value => !double.IsFinite(value)) || row.Probabilities.Any(value => !double.IsFinite(value) || value < 0 || value > 1) ||
                Math.Abs(row.Probabilities.Sum() - 1) > 1e-12 || row.PredictedIndex != Array.IndexOf(row.Logits, row.Logits.Max()) ||
                row.TeacherTargetRank != 1 + row.Logits.Count(value => value > row.Logits[row.TeacherTargetIndex])) throw ExperimentData.Invalid("Expanded row/teacher/GPU logits/rank differs.");
            for (var candidate = 0; candidate < count && candidate < 8; candidate++)
            {
                check(); ExperimentData.RequireHash(row.CandidateTextSha256[candidate]);
                var expectedId = row.OriginalCandidateIds[row.Variant == "original" ? candidate : (candidate + 1) % count];
                if (row.CandidateIds[candidate] != expectedId || (row.Variant == "fixed-left1" && (row.Type != "choice" || row.Split != "train")) ||
                    (uint)row.MarkerPositions[candidate] >= (uint)row.TokenIds.Length || (candidate > 0 && row.MarkerPositions[candidate] <= row.MarkerPositions[candidate - 1])) throw ExperimentData.Invalid("Expanded rendered candidate/marker order differs.");
                var probability = Math.Exp(row.Logits[candidate] - row.Logits.Max()) / row.Logits.Sum(value => Math.Exp(value - row.Logits.Max()));
                if (Math.Abs(row.Probabilities[candidate] - probability) > 1e-12) throw ExperimentData.Invalid("Expanded probabilities differ from GPU-logit stable softmax.");
            }
            foreach (var hash in new[] { row.TokenSha256, row.MarkerSha256, row.FeatureSha256, row.OriginalTokenSha256, row.OriginalMarkerSha256, row.OriginalFeatureSha256 }) { check(); ExperimentData.RequireHash(hash); }
            if (!ExperimentData.SameHash(row.TokenSha256, ExperimentOperations.IntHash(row.TokenIds)) || !ExperimentData.SameHash(row.MarkerSha256, ExperimentOperations.IntHash(row.MarkerPositions))) throw ExperimentData.Invalid("Expanded token/marker byte identity differs.");
        }
        if (!ExperimentOperations.Groups(observations.Rows, check).SequenceEqual(observations.Groups) || !ExperimentOperations.ExpandedPositionComparisons(observations.Rows, check).SequenceEqual(observations.PositionComparisons)) throw ExperimentData.Invalid("Expanded group/position evidence differs.");
        foreach (var loss in new[] { observations.Train, observations.Development, observations.RotatedTrainingChoice })
        {
            check(); var split = loss == observations.Development ? "development" : "train"; var variant = loss == observations.RotatedTrainingChoice ? "fixed-left1" : "original";
            var rows = observations.Rows.Where(row => row.Split == split && row.Variant == variant).ToArray();
            if (rows.Length != loss.CompletedExamples || Math.Abs(rows.Average(row => row.GpuTeacherCe) - loss.MeanLoss) > 1e-12) throw ExperimentData.Invalid("Expanded GPU row CE mean differs.");
        }
    }
}
internal sealed record ExpansionValidationReport(string Contract, string Status, string SourceReportSha256,
    string ManifestSha256, string RecordsSha256, string InputProvenanceSha256, string HeadSha256, string CheckpointSha256,
    string ParameterSha256, bool FreshDiskOutputsMatched, int EncoderForwardsStarted, int EncoderForwardsCompleted,
    ExpansionObservations Observations, ExperimentAcceptance Acceptance, ExperimentResources Resources, double ElapsedSeconds);
