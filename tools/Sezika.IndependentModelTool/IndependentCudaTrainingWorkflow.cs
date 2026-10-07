using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Sezika;
using Sezika.Cuda;

namespace Sezika.IndependentModelTool;

/// <summary>All three prior artifacts are explicit inputs; report SHA binds the expected head SHA.</summary>
public sealed record CudaTrainingResumePaths(string Checkpoint, string CheckpointSha256, string Head, string Report, string ReportSha256);
public sealed record CudaTrainingWorkflowResult(MarkerTrainingState State, GpuTrainingReport Report, string ReportSha256);

/// <summary>Only the GPU trainer updates weights. The caller owns the device and any formal input leases.</summary>
public static class IndependentCudaTrainingWorkflow
{
    public static CudaTrainingWorkflowResult RunFixture(CudaDevice device, MarkerFeatureSet train, IReadOnlyList<MarkerFeatureExample> development,
        string recordsSha256, MarkerTrainingOptions options, TrainingOutputPaths output, CudaTrainingResumePaths? resume = null,
        Action<MarkerTrainingProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Run(device, train, development, recordsSha256, options, output, false, resume, [], progress, cancellationToken);

    /// <summary>The caller must export the supplied approved records through the actual GPU encoder before invoking this method.</summary>
    internal static CudaTrainingWorkflowResult RunApproved(CudaDevice device, ReviewedOriginalTrainingInput admitted,
        MarkerFeatureSet train, IReadOnlyList<MarkerFeatureExample> development, MarkerTrainingOptions options, TrainingOutputPaths output,
        CudaTrainingResumePaths? resume = null, Action<MarkerTrainingProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admitted); ArgumentNullException.ThrowIfNull(train); ArgumentNullException.ThrowIfNull(development);
        cancellationToken.ThrowIfCancellationRequested();
        if (train.Identity.ModelId != IndependentModelLoader.ModelId ||
            !IndependentTrainingWorkflow.HashEqual(train.Identity.EncoderSha256, IndependentModelLoader.ConvertedEncoderSha256) ||
            !IndependentTrainingWorkflow.HashEqual(train.Identity.TokenizerSha256, IndependentModelLoader.TokenizerSha256) ||
            !IndependentTrainingWorkflow.HashEqual(train.Identity.DataManifestSha256, admitted.ManifestSha256))
            throw new DecisionException("training_admission_invalid", "GPU features differ from the admitted data or fixed original encoder identity.");
        var clock = Stopwatch.StartNew();
        var trainIndex = 0; var developmentIndex = 0;
        for (var index = 0; index < admitted.Records.Count && index < 32; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new DecisionException("training_admission_invalid", "Approved feature binding deadline expired.");
            var record = admitted.Records[index];
            var features = record.Split == "train" ? train.Examples : development;
            var featureIndex = record.Split == "train" ? trainIndex++ : developmentIndex++;
            if (featureIndex >= features.Count) throw new DecisionException("training_admission_invalid", "Approved split count differs from exported features.");
            var feature = features[featureIndex];
            var type = record.Question switch { IndependentChoiceQuestion => MarkerQuestionType.Choice, IndependentScoreQuestion => MarkerQuestionType.Score,
                IndependentBooleanQuestion => MarkerQuestionType.Boolean, _ => throw new DecisionException("training_admission_invalid", "Unknown approved question type.") };
            if (feature is null || feature.RecordId != record.RecordId || feature.Language != record.Language || feature.Type != type || feature.TargetIndex != record.TargetIndex)
                throw new DecisionException("training_admission_invalid", "Approved record order, mapping or label differs from exported features.");
        }
        if (trainIndex != train.Examples.Count || developmentIndex != development.Count)
            throw new DecisionException("training_admission_invalid", "Features include records outside the approved snapshot.");
        return Run(device, train, development, admitted.RecordsSha256, options, output, true, resume,
            [admitted.RecordsPath, admitted.ManifestPath], progress, cancellationToken);
    }

    public static GpuTrainingExecution CaptureExecution(CudaDevice device, bool approvedOriginal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device); cancellationToken.ThrowIfCancellationRequested();
        var artifacts = CudaTrainingExecutionIdentity.Read(cancellationToken);
        var info = device.Info;
        return new()
        {
            DataMode = approvedOriginal ? "approved_original" : "synthetic_fixture",
            FeatureBackend = approvedOriginal ? "cuda-modernbert-fp32-v1" : "synthetic-marker-features-v1",
            TrainingBackend = IndependentCudaMarkerHeadTrainer.Backend, NumericContract = "cuda-linear-ce-gradient-sgd-fp32-v1",
            OrderContract = "input-order-xorshift32-fixed-cycle-v1", DevelopmentBackend = "scalar-linear-marker-head-fp64",
            KernelSourceSha256 = artifacts.SourceSha256, KernelManifestSha256 = artifacts.ManifestSha256,
            Compiler = artifacts.Compiler, CompilerVersion = artifacts.CompilerVersion, Target = artifacts.Target, PtxVersion = artifacts.PtxVersion,
            Kernels = artifacts.Kernels.Select(kernel => new GpuTrainingKernelIdentity { Name = kernel.Name, Entry = kernel.Entry,
                BlockSize = kernel.BlockSize, ParameterCount = kernel.ParameterCount, Sha256 = kernel.Sha256 }).ToArray(),
            Device = new() { Uuid = info.Uuid, Name = info.Name, DriverVersion = info.DriverVersion, ComputeCapabilityMajor = info.ComputeCapabilityMajor,
                ComputeCapabilityMinor = info.ComputeCapabilityMinor, TotalMemoryBytes = info.TotalMemoryBytes, DeviceIndex = device.DeviceIndex }
        };
    }

    private static CudaTrainingWorkflowResult Run(CudaDevice device, MarkerFeatureSet train, IReadOnlyList<MarkerFeatureExample> development,
        string recordsSha256, MarkerTrainingOptions options, TrainingOutputPaths output, bool approvedOriginal, CudaTrainingResumePaths? resume,
        IReadOnlyList<string> inputs, Action<MarkerTrainingProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device); ArgumentNullException.ThrowIfNull(train); ArgumentNullException.ThrowIfNull(development);
        ArgumentNullException.ThrowIfNull(options);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var token = deadline.Token;
        var clock = Stopwatch.StartNew();
        void Check() { token.ThrowIfCancellationRequested(); if (clock.Elapsed >= TimeSpan.FromMinutes(10)) throw new DecisionException("training_deadline_exceeded", "GPU workflow deadline expired."); }
        Check();
        if (!IndependentTrainingWorkflow.IsHash(recordsSha256) || train.Examples is null || train.Examples.Count is < 1 or > 31 ||
            development.Count is < 1 or > 31 || train.Examples.Count + development.Count > 32 || inputs.Count > 2)
            throw new DecisionException("training_workflow_input_invalid", "GPU workflow records or split bounds are invalid.");
        var inputPaths = inputs.Concat(resume is null ? [] : new[] { resume.Checkpoint, resume.Head, resume.Report }).ToArray();
        IndependentTrainingWorkflow.ValidateOutputPaths(output, inputPaths);
        output = new(Path.GetFullPath(output.Checkpoint), Path.GetFullPath(output.Head), Path.GetFullPath(output.Report));
        // Keep restored inputs read-only throughout validation, training and publication.
        using var reportLease = resume is null ? null : new FileStream(resume.Report, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var checkpointLease = resume is null ? null : new FileStream(resume.Checkpoint, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var headLease = resume is null ? null : new FileStream(resume.Head, FileMode.Open, FileAccess.Read, FileShare.Read);
        var execution = CaptureExecution(device, approvedOriginal, token);
        execution.Validate(token);
        ValidateDevelopment(train, development, Check);
        MarkerTrainingState? restored = null;
        if (resume is not null)
        {
            var report = GpuTrainingReport.ReadBounded(resume.Report, resume.ReportSha256, token);
            report.RequireExecutionCompatible(execution, token);
            if (approvedOriginal) _ = report.BindApprovedIdentity(train.Identity, token);
            restored = MarkerTrainingCheckpoint.Load(resume.Checkpoint, train, resume.CheckpointSha256, token);
            var previous = report.Training;
            if (!IndependentTrainingWorkflow.HashEqual(previous.CheckpointSha256, resume.CheckpointSha256) ||
                !IndependentTrainingWorkflow.HashEqual(previous.RecordsSha256, recordsSha256) ||
                !IndependentTrainingWorkflow.HashEqual(previous.FeatureSha256, restored.FeatureSha256) || !previous.Identity.Matches(restored.Identity) ||
                previous.Seed != options.Seed || previous.LearningRate != options.LearningRate ||
                previous.Seed != restored.Seed || previous.LearningRate != restored.LearningRate || previous.CompletedSteps != restored.CompletedSteps ||
                previous.LastLoss != restored.LastLoss || previous.TrainCount != train.Examples.Count || previous.DevelopmentCount != development.Count)
                throw new DecisionException("training_resume_mismatch", "GPU report, checkpoint, full records or optimizer configuration differs.");
            var priorHead = IndependentMarkerHeadAssetStore.Load(resume.Head, restored.Identity, restored.FeatureSha256, previous.HeadSha256, token);
            if (priorHead.Seed != restored.Seed || priorHead.LearningRate != restored.LearningRate ||
                priorHead.CompletedSteps != restored.CompletedSteps || priorHead.LastLoss != restored.LastLoss)
                throw new DecisionException("training_resume_mismatch", "GPU resume head optimizer metadata differs from the checkpoint and report.");
            var priorWeights = priorHead.Head.CopyWeights(); var restoredWeights = restored.Head.CopyWeights();
            if (!MemoryMarshal.AsBytes(priorWeights.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(restoredWeights.AsSpan())))
                throw new DecisionException("training_resume_mismatch", "GPU resume head does not contain the checkpoint's exact weight bytes.");
        }
        Check();
        var state = IndependentCudaMarkerHeadTrainer.Train(device, train, options, restored, progress, token);
        var groups = DevelopmentGroups(state, development, Check);
        Check();
        string? checkpointHash = null, headHash = null, reportHash = null;
        var temporaryReport = Path.Combine(Path.GetDirectoryName(output.Report)!, $".{Path.GetFileName(output.Report)}.{Guid.NewGuid():N}.tmp");
        var ownsTemporaryReport = false;
        var publishedReport = false;
        try
        {
            foreach (var path in new[] { output.Checkpoint, output.Head, output.Report }) { Check(); Directory.CreateDirectory(Path.GetDirectoryName(path)!); }
            checkpointHash = MarkerTrainingCheckpoint.SaveNew(output.Checkpoint, state, token);
            headHash = IndependentMarkerHeadAssetStore.SaveNew(output.Head, state, token);
            _ = MarkerTrainingCheckpoint.Load(output.Checkpoint, train, checkpointHash, token);
            _ = IndependentMarkerHeadAssetStore.Load(output.Head, state.Identity, state.FeatureSha256, headHash, token);
            var report = new GpuTrainingReport
            {
                SchemaVersion = 3, Contract = GpuTrainingReport.ContractName, Execution = execution,
                Training = new()
                {
                    SchemaVersion = 2, Status = approvedOriginal ? "development_smoke_only" : "fixture_contract_only", Identity = WorkflowTrainingIdentity.From(state.Identity),
                    RecordsSha256 = recordsSha256, FeatureSha256 = state.FeatureSha256, HeadSha256 = headHash, CheckpointSha256 = checkpointHash,
                    Seed = state.Seed, LearningRate = state.LearningRate, StepSemantics = "total_completed_steps", InitialSteps = restored?.CompletedSteps ?? 0,
                    MaxSteps = options.MaxSteps, CompletedSteps = state.CompletedSteps, LastLoss = state.LastLoss, TrainCount = train.Examples.Count, DevelopmentCount = development.Count,
                    UsesRealIndependentEncoder = approvedOriginal, RepresentsQualityAcceptance = false, Optimizer = "sgd_v1_no_momentum",
                    FeatureSource = approvedOriginal ? "original_encoder" : "synthetic_fixture", TrainingTimeoutSeconds = options.MaxDuration.TotalSeconds,
                    ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds, Resume = resume is null ? null : new() { CheckpointSha256 = resume.CheckpointSha256,
                        ReportSha256 = resume.ReportSha256, InitialSteps = restored!.CompletedSteps }, DevelopmentGroups = groups
                }
            };
            var bytes = report.SerializeBounded(token);
            Check();
            using (var stream = new FileStream(temporaryReport, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporaryReport = true; stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            Check();
            // Calculate ownership identity before the final publication boundary, then reject a cancellation after it too.
            reportHash = Convert.ToHexString(SHA256.HashData(bytes));
            File.Move(temporaryReport, output.Report);
            publishedReport = true;
            ownsTemporaryReport = false;
            Check();
            return new(state, report, reportHash);
        }
        catch (Exception failure)
        {
            var cleanupClock = Stopwatch.StartNew();
            var cleanupFailures = new List<Exception>(4);
            void AttemptCleanup(Action action, string path)
            {
                try { action(); }
                catch (Exception cleanupFailure) { cleanupFailures.Add(new IOException("GPU publication cleanup failed: " + path, cleanupFailure)); }
            }
            if (publishedReport) AttemptCleanup(() => RemoveOwned(output.Report, reportHash, cleanupClock), output.Report);
            AttemptCleanup(() => RemoveOwned(output.Head, headHash, cleanupClock), output.Head);
            AttemptCleanup(() => RemoveOwned(output.Checkpoint, checkpointHash, cleanupClock), output.Checkpoint);
            if (ownsTemporaryReport) AttemptCleanup(() => { if (File.Exists(temporaryReport)) File.Delete(temporaryReport); }, temporaryReport);
            if (cleanupFailures.Count > 0)
                throw new AggregateException("GPU publication failed and owned-artifact cleanup had failures.", new[] { failure }.Concat(cleanupFailures));
            throw;
        }
    }

    private static void ValidateDevelopment(MarkerFeatureSet train, IReadOnlyList<MarkerFeatureExample> development, Action check)
    {
        var ids = new HashSet<string>(train.Examples.Select(example => example.RecordId), StringComparer.Ordinal);
        var hidden = train.Identity.HiddenSize;
        if (hidden is < 1 or > 2048) throw new DecisionException("training_shape_invalid", "Development hidden width is invalid.");
        for (var item = 0; item < development.Count && item < 32; item++)
        {
            check(); var example = development[item];
            if (example is null || string.IsNullOrWhiteSpace(example.RecordId) || example.RecordId.Length > 128 || !ids.Add(example.RecordId) ||
                example.Language is not ("zh" or "en") || (uint)example.Type > 2 || example.Features is null || example.Features.Length is < 2 or > 32 ||
                (example.Type == MarkerQuestionType.Boolean && example.Features.Length != 2) || (example.Type == MarkerQuestionType.Score && example.Features.Length > 10) ||
                (uint)example.TargetIndex >= (uint)example.Features.Length || example.TokenIds is null || example.TokenIds.Length is < 2 or > 1024 ||
                example.MarkerPositions is null || example.MarkerPositions.Length != example.Features.Length)
                throw new DecisionException("training_workflow_input_invalid", "Development features, full sequence or labels are invalid.");
            for (var candidate = 0; candidate < example.Features.Length && candidate < 32; candidate++)
            {
                check(); var marker = example.MarkerPositions[candidate]; var features = example.Features[candidate];
                if ((uint)marker >= (uint)example.TokenIds.Length || (candidate > 0 && marker <= example.MarkerPositions[candidate - 1]) || features is null || features.Length != hidden)
                    throw new DecisionException("training_shape_invalid", "Development marker positions or feature shape are invalid.");
                for (var index = 0; index < hidden && index < 2048; index++) { if ((index & 255) == 0) check(); if (!float.IsFinite(features[index])) throw new DecisionException("training_value_invalid", "Development feature is non-finite."); }
            }
        }
    }

    private static WorkflowDevelopmentGroup[] DevelopmentGroups(MarkerTrainingState state, IReadOnlyList<MarkerFeatureExample> development, Action check)
    {
        var groups = new List<WorkflowDevelopmentGroup>(6);
        for (var item = 0; item < development.Count && item < 32; item++)
        {
            check(); var example = development[item]; var logits = state.Head.Score(example.Type, example.Features, check); var predicted = 0;
            for (var index = 1; index < logits.Length && index < 32; index++) { check(); if (logits[index] > logits[predicted]) predicted = index; }
            var type = example.Type switch { MarkerQuestionType.Choice => "choice", MarkerQuestionType.Score => "score", _ => "boolean" };
            var groupIndex = groups.FindIndex(group => group.Language == example.Language && group.QuestionType == type);
            var prior = groupIndex >= 0 ? groups[groupIndex] : new WorkflowDevelopmentGroup { Language = example.Language, QuestionType = type, Count = 0, Correct = 0, Accuracy = 0 };
            var correct = prior.Correct + (predicted == example.TargetIndex ? 1 : 0);
            var next = prior with { Count = prior.Count + 1, Correct = correct, Accuracy = (double)correct / (prior.Count + 1) };
            if (groupIndex >= 0) groups[groupIndex] = next; else groups.Add(next);
        }
        return groups.OrderBy(group => group.Language, StringComparer.Ordinal).ThenBy(group => group.QuestionType, StringComparer.Ordinal).ToArray();
    }

    private static void RemoveOwned(string path, string? expectedSha256, Stopwatch cleanupClock)
    {
        if (expectedSha256 is null || !File.Exists(path)) return;
        if (cleanupClock.Elapsed > TimeSpan.FromSeconds(10)) throw new IOException("GPU publication rollback deadline expired; retain and report remaining owned artifacts.");
        var fullPath = Path.GetFullPath(path); var bytes = IndependentTrainingWorkflow.ReadSmallFile(fullPath);
        if (IndependentTrainingWorkflow.HashEqual(Convert.ToHexString(SHA256.HashData(bytes)), expectedSha256)) File.Delete(fullPath);
    }
}
