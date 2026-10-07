using System.Diagnostics;
using System.Globalization;
using Sezika;
using Sezika.Cuda;

namespace Sezika.IndependentModelTool;

/// <summary>Explicit GPU command. CPU arguments and their historical four-input resume remain unchanged.</summary>
public sealed record OriginalCudaTrainingCommand(OriginalTrainingCommand Training, int DeviceIndex, CudaTrainingResumePaths? Resume)
{
    public static OriginalCudaTrainingCommand Parse(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (arguments.Count is < 1 or > 33 || !arguments[0].Equals("train-original-gpu", StringComparison.OrdinalIgnoreCase))
            throw Invalid("Expected train-original-gpu with at most 16 option pairs.");
        var clock = Stopwatch.StartNew();
        var cpuArguments = new List<string>(31) { "train-original" };
        string? deviceText = null, head = null;
        for (var index = 1; index < arguments.Count && index < 33; index += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw Invalid("GPU argument parsing deadline expired.");
            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1])) throw Invalid("Missing GPU option value.");
            if (arguments[index] == "--device")
            {
                if (deviceText is not null) throw Invalid("Duplicate --device.");
                deviceText = arguments[index + 1];
            }
            else if (arguments[index] == "--resume-head")
            {
                if (head is not null) throw Invalid("Duplicate --resume-head.");
                head = arguments[index + 1];
            }
            else { cpuArguments.Add(arguments[index]); cpuArguments.Add(arguments[index + 1]); }
        }
        var device = 0;
        if (deviceText is not null && (!int.TryParse(deviceText, NumberStyles.None, CultureInfo.InvariantCulture, out device) || device is < 0 or > 15))
            throw Invalid("--device must be an integer in [0,15].");
        var cpu = OriginalTrainingCommand.Parse(cpuArguments, cancellationToken);
        if ((cpu.Resume is null) != (head is null)) throw Invalid("GPU resume requires checkpoint, checkpoint SHA, head, report and report SHA together.");
        return new(cpu, device, cpu.Resume is null ? null : new(cpu.Resume.Checkpoint, cpu.Resume.CheckpointSha256,
            Path.GetFullPath(head!), cpu.Resume.Report, cpu.Resume.ReportSha256));
    }

    /// <summary>Only admission and output checks. No model or CUDA API is touched.</summary>
    public ReviewedOriginalTrainingInput Admit(Action<int, int>? progress = null, CancellationToken cancellationToken = default)
    {
        var manifest = Path.Combine(Path.GetDirectoryName(Training.Records)!, "manifest.json");
        var sources = new List<string> { Training.Records, manifest, Path.Combine(Training.Package, "model.json"),
            Path.Combine(Training.Package, "encoder.safetensors"), Path.Combine(Training.Package, "tokenizer", "tokenizer.json") };
        if (Resume is not null) { sources.Add(Resume.Checkpoint); sources.Add(Resume.Head); sources.Add(Resume.Report); }
        IndependentTrainingWorkflow.ValidateOutputPaths(Training.Outputs, sources);
        return OriginalTrainingAdmission.ReadBounded(Training.Records, manifest, progress, cancellationToken);
    }

    /// <summary>Validate a prior formal schema3 report before device opening or encoder execution.</summary>
    public GpuTrainingReport? ReadResumeReport(ReviewedOriginalTrainingInput admitted, CancellationToken cancellationToken = default)
    {
        if (Resume is null) return null;
        var report = GpuTrainingReport.ReadBounded(Resume.Report, Resume.ReportSha256, cancellationToken);
        _ = report.BindApprovedIdentity(new(IndependentModelLoader.ModelId, IndependentModelLoader.ConvertedEncoderSha256,
            IndependentModelLoader.TokenizerSha256, admitted.ManifestSha256, report.Training.Identity.HiddenSize), cancellationToken);
        if (!IndependentTrainingWorkflow.HashEqual(report.Training.RecordsSha256, admitted.RecordsSha256) ||
            !IndependentTrainingWorkflow.HashEqual(report.Training.CheckpointSha256, Resume.CheckpointSha256) ||
            report.Training.Seed != Training.Options.Seed || report.Training.LearningRate != Training.Options.LearningRate ||
            report.Training.TrainCount != admitted.Manifest.SplitCounts.Train || report.Training.DevelopmentCount != admitted.Manifest.SplitCounts.Development ||
            report.Training.CompletedSteps > Training.Options.MaxSteps)
            throw new DecisionException("training_resume_mismatch", "Prior GPU report differs from current records, optimizer or total step target.");
        return report;
    }

    public CudaTrainingWorkflowResult Run(Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Training.TimeoutSeconds));
        var token = deadline.Token;
        using var admitted = Admit((done, total) => progress?.Invoke($"admission {done}/{total}"), token);
        using var reportLease = Resume is null ? null : new FileStream(Resume.Report, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var checkpointLease = Resume is null ? null : new FileStream(Resume.Checkpoint, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var headLease = Resume is null ? null : new FileStream(Resume.Head, FileMode.Open, FileAccess.Read, FileShare.Read);
        var prior = ReadResumeReport(admitted, token);
        token.ThrowIfCancellationRequested();
        using var device = CudaDevice.Open(DeviceIndex);
        var execution = IndependentCudaTrainingWorkflow.CaptureExecution(device, approvedOriginal: true, token);
        prior?.RequireExecutionCompatible(execution, token);
        using var model = IndependentModelLoader.Load(Training.Package,
            new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromMinutes(10) }, token);
        _ = prior?.BindApprovedIdentity(new(model.ModelId, model.EncoderSha256, model.TokenizerSha256,
            admitted.ManifestSha256, model.Encoder.Config.HiddenSize), token);
        // Loader construction validates fixed tensors; all actual forward work below is CUDA.
        var features = IndependentCudaFeatureExporter.ExportApproved(device, model, admitted, progress, token);
        var progressClock = Stopwatch.StartNew();
        return IndependentCudaTrainingWorkflow.RunApproved(device, admitted, features.Train, features.Development,
            Training.Options, Training.Outputs, Resume, item =>
            {
                if (item.CompletedSteps == 0 || item.CompletedSteps == item.MaxSteps || progressClock.Elapsed > TimeSpan.FromSeconds(5))
                { progress?.Invoke($"steps {item.CompletedSteps}/{item.MaxSteps} loss={item.LastLoss:R}"); progressClock.Restart(); }
            }, token);
    }

    private static DecisionException Invalid(string message) => new("training_arguments_invalid", message);
}
