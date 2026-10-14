using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record ResidentRequest(string TaskDirectory, string TrainerExecutable, string TrainerSha256, string PlanPath, string PlanSha256, string ProfilePath, string ProfileSha256, string GpuUuid, string NvidiaSmiExecutable, string NvidiaSmiSha256, long StageUpdates, int MaximumSeconds, int CheckpointEvery, string? ResumeDirectory = null, CheckpointVerificationRequest? ResumeIdentity = null, string? BrokerExecutable = null);
internal sealed record ResidentReceipt(string Status, string RunId, long CompletedUpdates, long GlobalUpdates, string StopReason, string ProcessReceipt, string CheckpointDirectory, string CheckpointSha256, string CursorSha256, bool FullEpochCoverage, bool ModelQualityAccepted = false, bool Calibrated = false, bool Published = false, bool ControllerSimulation = false);

internal static class ResidentController
{
    internal static Task<ResidentReceipt> RunAsync(ResidentRequest request, Budget budget, CancellationToken cancellation) => RunCoreAsync(request, budget, cancellation, false);

    // No command-line route exposes the simulation entry. Tests exercise the
    // same controller/process/checkpoint code with explicitly synthetic assets.
    internal static Task<ResidentReceipt> RunSimulationAsync(ResidentRequest request, Budget budget, CancellationToken cancellation) => RunCoreAsync(request, budget, cancellation, true);

    private static async Task<ResidentReceipt> RunCoreAsync(ResidentRequest request, Budget budget, CancellationToken cancellation, bool simulation)
    {
        var root = Path.GetFullPath(request.TaskDirectory); SafePaths.NoLinks(root, budget);
        if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "OWNED_BY_SEZIKA_REPOSITORY_TASK"))) throw new InvalidDataException("Explicit owned task marker required.");
        ProcessExecutor.ValidateExecutable(request.TrainerExecutable, budget);
        TaskOperations.VerifyFile(request.TrainerExecutable, new FileInfo(request.TrainerExecutable).Length, request.TrainerSha256, budget);
        TaskOperations.VerifyFile(request.PlanPath, new FileInfo(request.PlanPath).Length, request.PlanSha256, budget);
        TaskOperations.VerifyFile(request.ProfilePath, new FileInfo(request.ProfilePath).Length, request.ProfileSha256, budget);
        if (request.GpuUuid.Length != 32 || !request.GpuUuid.All(char.IsAsciiHexDigit) || request.MaximumSeconds is < 60 or > 86400 || request.MaximumSeconds > Math.Ceiling(budget.RemainingSeconds) || request.CheckpointEvery is < 1 or > 1000000 || request.StageUpdates is < 1 or > 800000000) throw new InvalidDataException("Resident GPU/time/update bounds differ.");
        var stop = Path.Combine(root, "STOP"); var cancel = Path.Combine(root, "CANCEL");
        if (Path.Exists(stop) || Path.Exists(cancel)) throw new OperationCanceledException("Existing STOP/CANCEL is preserved; no launch.");
        using var planJson = TaskOperations.ReadDocument(request.PlanPath, budget); var plan = planJson.RootElement;
        using var profileJson = TaskOperations.ReadDocument(request.ProfilePath, budget); var profile = profileJson.RootElement;
        var run = plan.GetProperty("RunId").GetString()!;
        var prepared = ReadPreparedContract(root, plan, request, budget);
        var independentInputs = JsonSerializer.Deserialize(profile.GetProperty("Inputs"), TaskJsonContext.Default.CheckpointInputContract) ?? throw new InvalidDataException("Pinned profile input identity missing.");
        // Historical ABI hashes can be uppercase. Preserve the exact pinned
        // identity string; file/plan SHA contracts remain canonical lowercase.
        foreach (var digest in new[] { independentInputs.PlanSha256, independentInputs.DataManifestSha256, independentInputs.RecordsSha256, independentInputs.BaseEncoderSha256, independentInputs.TokenizerSha256, independentInputs.OrderSha256, independentInputs.BackwardManifestSha256 })
            if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Pinned input digest syntax differs.");
        if (independentInputs.PlanSha256 != request.PlanSha256 || independentInputs.DataManifestSha256 != plan.GetProperty("CorpusManifestSha256").GetString() || profile.GetProperty("QualityAccepted").GetBoolean()) throw new InvalidDataException("Independent profile inputs or quality semantics differ.");
        var expectedProfileStatus = simulation ? "SIMULATION_cpu_controller_fixture_only" : "PASS_real_base_corpus_profile_only";
        if (File.ReadAllText(Path.Combine(root, "OWNED_BY_SEZIKA_REPOSITORY_TASK")).Trim() != run || profile.GetProperty("RunId").GetString() != run || profile.GetProperty("Status").GetString() != expectedProfileStatus || !profile.GetProperty("GpuUuid").GetString()!.Equals(request.GpuUuid, StringComparison.OrdinalIgnoreCase) || profile.GetProperty("Inputs").GetProperty("PlanSha256").GetString() != request.PlanSha256) throw new InvalidDataException("Profile/plan/task/GPU identity or simulation scope differs.");
        var checkpointSeconds = profile.GetProperty("CheckpointSeconds").GetDouble(); var maximumStep = profile.GetProperty("MaximumStepSeconds").GetDouble();
        if (!double.IsFinite(checkpointSeconds) || !double.IsFinite(maximumStep) || checkpointSeconds <= 0 || maximumStep <= 0) throw new InvalidDataException("Measured same-tool checkpoint and full-sequence step required.");
        var reserve = Math.Max(prepared.TransferReserveSeconds, checked((int)Math.Ceiling(2 * Math.Max(120, 4 * checkpointSeconds) + 60 + 4 * maximumStep)));
        if (reserve >= request.MaximumSeconds - 20) throw new InvalidDataException("Insufficient save/cleanup reserve; no GPU launch.");
        var global = checked(plan.GetProperty("TrainRecords").GetInt64() * plan.GetProperty("Epochs").GetInt32());
        if (global is < 1 or > 800000000) throw new InvalidDataException("Immutable global update denominator differs.");
        long initial = 0;
        if (request.ResumeDirectory is not null)
        {
            if (request.ResumeIdentity is null || Path.GetFullPath(request.ResumeIdentity.Directory) != Path.GetFullPath(request.ResumeDirectory) || Path.GetFullPath(request.ResumeIdentity.PlanPath) != Path.GetFullPath(request.PlanPath) || request.ResumeIdentity.Inputs != independentInputs) throw new InvalidDataException("Explicit four-file predecessor verification contract required.");
            initial = TaskOperations.VerifyCheckpoint(request.ResumeIdentity, budget).CompletedUpdates;
        }
        else if (request.ResumeIdentity is not null) throw new InvalidDataException("Resume identity supplied without a predecessor.");
        if (initial >= global || request.StageUpdates > global - initial) throw new InvalidDataException("Resident stage cannot alter or exceed the remaining immutable plan.");
        var output = Path.Combine(root, "resident-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var leaseDirectory = Path.GetFullPath(simulation ? Path.Combine(root, "simulation-leases") : Path.Combine(Path.GetTempPath(), "sezika-gpu-leases"));
        SafePaths.NoLinks(leaseDirectory, budget); Directory.CreateDirectory(leaseDirectory);
        var leasePath = Path.Combine(leaseDirectory, "gpu-" + request.GpuUuid.ToLowerInvariant() + ".lock");
        SafePaths.NoLinks(leasePath, budget);
        if (Path.Exists(leasePath)) RegularFiles.Require(leasePath, budget);
        // Persistent empty lock file is evidence, never deleted or used to clear
        // another process's lease. FileShare.None provides the live exclusive lock.
        using var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await RequireIdleGpu(request, output, budget, cancellation).ConfigureAwait(false);
        var executionSeconds = Math.Min(request.MaximumSeconds, (int)budget.RemainingSeconds);
        if (reserve >= executionSeconds - 20) throw new InvalidDataException("Driver admission consumed the save reserve; no training launch.");
        var arguments = new List<string> { "train-corpus", "--plan", request.PlanPath, "--output", Path.Combine(output, "training"), "--profile", request.ProfilePath, "--stage-updates", request.StageUpdates.ToString(CultureInfo.InvariantCulture), "--gpu-uuid", request.GpuUuid, "--resident", "true", "--stop-file", stop, "--checkpoint-every", request.CheckpointEvery.ToString(CultureInfo.InvariantCulture), "--timeout-seconds", (executionSeconds - 10).ToString(CultureInfo.InvariantCulture) };
        if (request.ResumeDirectory is not null) { arguments.Add("--resume"); arguments.Add(request.ResumeDirectory); }
        using var signalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var signal = SafeStopAsync(stop, executionSeconds - reserve, signalCancellation.Token);
        ExecutionResult execution;
        try
        {
            execution = await ProcessExecutor.ExecuteAsync(new ExecutionRequest
            {
                Executable = request.TrainerExecutable, ArgumentList = arguments.ToArray(), WorkingDirectory = root,
                OutputDirectory = Path.Combine(output, "execution"), TimeoutSeconds = executionSeconds,
                CleanupSeconds = 10, CancelFile = cancel, BrokerExecutable = request.BrokerExecutable, MaximumOutputBytes = 16 * 1024 * 1024
                // STOP is consumed by the trainer for safe saving. It is not a
                // ProcessExecutor termination signal for a training payload.
            }, budget, cancellation).ConfigureAwait(false);
        }
        finally
        {
            signalCancellation.Cancel();
            try { await signal.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        if (execution.Status != "PASS" || execution.ExitCode != 0 || execution.RemainingProcesses != 0) throw new IOException("Resident payload or process recovery failed; partial model and receipts retained.");
        var trainingRoot = Path.Combine(output, "training");
        using var reportJson = TaskOperations.ReadDocument(Path.Combine(trainingRoot, "training-report.json"), budget); var report = reportJson.RootElement;
        var simulatedReport = report.TryGetProperty("ControllerSimulation", out var simulationFlag) && simulationFlag.ValueKind == JsonValueKind.True;
        if (simulatedReport != simulation) throw new InvalidDataException("Controller fixture report cannot satisfy real training admission.");
        if (report.GetProperty("RunId").GetString() != run || report.GetProperty("ProcessId").GetInt32() != execution.RootPid || report.GetProperty("InitialUpdates").GetInt64() != initial || report.GetProperty("GlobalUpdates").GetInt64() != global || report.GetProperty("QualityAccepted").GetBoolean() || report.GetProperty("Calibrated").GetBoolean()) throw new InvalidDataException("Resident report binding or acceptance semantics differ.");
        var completed = report.GetProperty("CompletedUpdates").GetInt64();
        var reportInputs = JsonSerializer.Deserialize(report.GetProperty("Inputs"), TaskJsonContext.Default.CheckpointInputContract);
        if (reportInputs != independentInputs || !report.GetProperty("GpuUuid").GetString()!.Equals(request.GpuUuid, StringComparison.OrdinalIgnoreCase) || report.GetProperty("StageRequestedUpdates").GetInt64() != request.StageUpdates || report.GetProperty("CheckpointEvery").GetInt32() != request.CheckpointEvery) throw new InvalidDataException("Saved report differs from independently pinned profile/stage/GPU.");
        if (completed < initial || completed > initial + request.StageUpdates) throw new InvalidDataException("Saved resident progress outside requested stage.");
        var checkpointRoot = Path.Combine(trainingRoot, "checkpoint-" + completed.ToString("D10", CultureInfo.InvariantCulture));
        using var cpJson = TaskOperations.ReadDocument(Path.Combine(checkpointRoot, "checkpoint.json"), budget);
        var inputs = JsonSerializer.Deserialize(cpJson.RootElement.GetProperty("Inputs"), TaskJsonContext.Default.CheckpointInputContract) ?? throw new InvalidDataException("Saved input identity missing.");
        if (inputs != independentInputs) throw new InvalidDataException("Saved checkpoint input identity differs from pinned predecessor/profile.");
        var verified = TaskOperations.VerifyCheckpoint(new(checkpointRoot, request.PlanPath, report.GetProperty("CheckpointReceiptSha256").GetString()!, report.GetProperty("CursorReceiptSha256").GetString()!, independentInputs), budget);
        if (verified.CompletedUpdates != completed) throw new InvalidDataException("Saved checkpoint progress differs.");
        RegularFiles.Require(Path.Combine(trainingRoot, "resources-reclaimed.txt"), budget);
        var expectedResources = simulation ? "SIMULATION_cpu_fixture_resources_reclaimed" : "PASS_owned_buffers_modules_restored_and_context_disposed";
        if (File.ReadAllText(Path.Combine(trainingRoot, "resources-reclaimed.txt")).Trim() != expectedResources) throw new InvalidDataException("Trainer resource declaration or simulation scope differs.");
        var complete = report.GetProperty("FullCorpusEpochCoverage").GetBoolean();
        if (complete != (completed == global)) throw new InvalidDataException("Full epoch coverage does not match global cursor.");
        return new(simulation ? "SIMULATION_cpu_controller_saved_cursor_only" : complete ? "PASS_saved_global_cursor_only" : "PARTIAL_saved_bounded_resident_stage", run, completed, global,
            report.TryGetProperty("StopReason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString()! : "stage_update_bound", Path.Combine(output, "execution", "result.json"), checkpointRoot, verified.CheckpointSha256, verified.CursorSha256, complete, ControllerSimulation: simulation);
    }

    internal static TaskPreparationRequest ReadPreparedContract(string root, JsonElement plan, ResidentRequest request, Budget budget)
    {
        using var document = TaskOperations.ReadDocument(SafePaths.Within(root, "task.json", budget), budget);
        var prepared = JsonSerializer.Deserialize(document.RootElement, TaskJsonContext.Default.PreparedTask) ?? throw new InvalidDataException("Prepared task contract missing.");
        var contract = prepared.Contract;
        foreach (var commit in new[] { contract.SourceCommit, contract.DatasetCommit, contract.ModelCommit }) TaskOperations.Commit(commit);
        if (prepared.Status != "PREPARED_configuration_only" || prepared.GpuConcurrency != 1 || prepared.PurchaseAuthorized || prepared.QualityAccepted || contract.Method != "encoder-full" ||
            Path.GetFullPath(contract.OutputDirectory) != Path.GetFullPath(root) || contract.RunId != plan.GetProperty("RunId").GetString() ||
            contract.MaximumSeconds is < 60 or > 86400 || request.MaximumSeconds > contract.MaximumSeconds || contract.TransferReserveSeconds is < 1 || contract.TransferReserveSeconds >= contract.MaximumSeconds ||
            contract.TrainRecords != plan.GetProperty("TrainRecords").GetInt64() || contract.Epochs != plan.GetProperty("Epochs").GetInt32() ||
            contract.MaximumTokens != plan.GetProperty("MaximumTokens").GetInt32() || contract.CheckpointEvery != request.CheckpointEvery ||
            contract.CheckpointEvery != plan.GetProperty("CheckpointEvery").GetInt32() || !plan.GetProperty("ExpectedGpuUuid").GetString()!.Equals(request.GpuUuid, StringComparison.OrdinalIgnoreCase) ||
            contract.SourceCommit != plan.GetProperty("SourceCommit").GetString() || contract.DatasetCommit != plan.GetProperty("DatasetCommit").GetString() || contract.ModelCommit != plan.GetProperty("ModelCommit").GetString())
            throw new InvalidDataException("Resident request differs from prepared task source/model/dataset/global/time contract.");
        return contract;
    }

    private static async Task SafeStopAsync(string stop, int seconds, CancellationToken cancellation)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellation).ConfigureAwait(false);
        if (!File.Exists(stop))
        {
            var pending = stop + ".sezika-" + Guid.NewGuid().ToString("N") + ".pending";
            await File.WriteAllTextAsync(pending, "bounded_controller_save_reserve\n", cancellation).ConfigureAwait(false);
            try { File.Move(pending, stop, false); }
            catch (IOException) when (File.Exists(stop)) { /* User STOP takes precedence; preserve task-owned pending evidence. */ }
        }
    }

    private static async Task RequireIdleGpu(ResidentRequest request, string output, Budget budget, CancellationToken cancellation)
    {
        ProcessExecutor.ValidateExecutable(request.NvidiaSmiExecutable, budget);
        TaskOperations.VerifyFile(request.NvidiaSmiExecutable, new FileInfo(request.NvidiaSmiExecutable).Length, request.NvidiaSmiSha256, budget);
        var uuid = "GPU-" + request.GpuUuid[..8] + "-" + request.GpuUuid.Substring(8, 4) + "-" + request.GpuUuid.Substring(12, 4) + "-" + request.GpuUuid.Substring(16, 4) + "-" + request.GpuUuid[20..];
        foreach (var query in new[] { "--query-gpu=uuid,memory.free", "--query-compute-apps=gpu_uuid,pid" })
        {
            budget.Check(); var name = query.StartsWith("--query-gpu", StringComparison.Ordinal) ? "gpu" : "compute";
            var execution = await ProcessExecutor.ExecuteAsync(new ExecutionRequest { Executable = request.NvidiaSmiExecutable, ArgumentList = ["--id=" + uuid, query, "--format=csv,noheader,nounits"], WorkingDirectory = request.TaskDirectory, OutputDirectory = Path.Combine(output, "admission-" + name), TimeoutSeconds = 15, MaximumOutputBytes = 32768, BrokerExecutable = request.BrokerExecutable }, budget, cancellation).ConfigureAwait(false);
            if (execution.Status != "PASS" || execution.ExitCode != 0) throw new IOException("GPU driver process/UUID admission unavailable.");
            var text = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(execution.OutputDirectory, "stdout.bin"), cancellation).ConfigureAwait(false)).Trim();
            if (name == "compute") { if (text.Length != 0) throw new InvalidDataException("GPU has existing compute consumers; none are terminated."); }
            else
            {
                var fields = text.Split(',', StringSplitOptions.TrimEntries);
                if (fields.Length != 2 || !fields[0].Equals(uuid, StringComparison.OrdinalIgnoreCase) || !ulong.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mib)) throw new InvalidDataException("GPU identity/memory evidence differs.");
                using var plan = TaskOperations.ReadDocument(request.PlanPath, budget);
                if (checked(mib * 1048576) < plan.RootElement.GetProperty("MaximumOwnedBytes").GetUInt64()) throw new InvalidDataException("GPU memory cannot fit the fixed ownership budget.");
            }
        }
    }
}

[JsonSerializable(typeof(ResidentRequest))]
[JsonSerializable(typeof(ResidentReceipt))]
[JsonSourceGenerationOptions(WriteIndented = true, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class ResidentJsonContext : JsonSerializerContext;
