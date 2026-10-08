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
internal partial class ExperimentJsonContext : JsonSerializerContext;
