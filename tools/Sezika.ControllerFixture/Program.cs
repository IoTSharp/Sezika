using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.Tooling;

return await ControllerFixture.RunAsync(args);

internal static class ControllerFixture
{
    internal const string SimulationStatus = "SIMULATION_cpu_controller_fixture_only";

    internal static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var budget = new Budget(20, null, cancellation.Token);
        try
        {
            StartupGate.Wait(cancellation.Token);
            if (args.Length is < 1 or > 32 || args.Any(value => value.Length > 4096)) throw new ArgumentException("Controller fixture argument bound32.");
            if (args[0].StartsWith("--id=", StringComparison.Ordinal)) return Query(args);
            if (args[0] != "train-corpus") throw new ArgumentException("Explicit simulation mode required.");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < args.Length; index += 2)
            {
                budget.Check();
                if (index + 1 >= args.Length || args[index] is not ("--plan" or "--profile" or "--output" or "--stage-updates" or "--gpu-uuid" or "--resident" or "--stop-file" or "--checkpoint-every" or "--timeout-seconds" or "--resume") || !options.TryAdd(args[index], args[index + 1])) throw new ArgumentException("Controller fixture option contract differs.");
            }
            string Required(string name) => options.TryGetValue(name, out var value) ? value : throw new ArgumentException("Missing simulation option: " + name);
            var root = Path.GetFullPath(Environment.CurrentDirectory); SafePaths.NoLinks(root, budget);
            if (!File.Exists(SafePaths.Within(root, "OWNED_BY_SEZIKA_REPOSITORY_TASK", budget))) throw new InvalidDataException("Simulation requires an owned prepared task.");
            string Within(string value)
            {
                if (!Path.IsPathFullyQualified(value)) throw new InvalidDataException("Absolute simulation input/output required.");
                return SafePaths.Within(root, Path.GetRelativePath(root, value).Replace('\\', '/'), budget);
            }
            var planPath = Within(Required("--plan")); var profilePath = Within(Required("--profile"));
            using var planDocument = ReadSmall(planPath, budget); using var profileDocument = ReadSmall(profilePath, budget);
            var plan = planDocument.RootElement; var profile = profileDocument.RootElement;
            if (profile.GetProperty("Status").GetString() != SimulationStatus || !profile.GetProperty("ControllerSimulation").GetBoolean() || !plan.GetProperty("ControllerSimulation").GetBoolean()) throw new InvalidDataException("CPU fixture cannot consume a real training plan/profile.");
            var inputs = JsonSerializer.Deserialize(profile.GetProperty("Inputs"), TaskJsonContext.Default.CheckpointInputContract) ?? throw new InvalidDataException("Simulation inputs missing.");
            if (inputs.PlanSha256 != FileHash(planPath, budget)) throw new InvalidDataException("Simulation plan SHA differs.");
            var run = plan.GetProperty("RunId").GetString() ?? throw new InvalidDataException("Simulation run missing.");
            var records = plan.GetProperty("TrainRecords").GetInt64(); var epochs = plan.GetProperty("Epochs").GetInt32(); var global = checked(records * epochs);
            if (records is < 1 or > 32 || epochs is < 1 or > 8 || global > 32) throw new InvalidDataException("CPU fixture global update bound32.");
            var stage = long.Parse(Required("--stage-updates"), NumberStyles.None, CultureInfo.InvariantCulture);
            var checkpointEvery = int.Parse(Required("--checkpoint-every"), NumberStyles.None, CultureInfo.InvariantCulture);
            var declaredTimeout = int.Parse(Required("--timeout-seconds"), NumberStyles.None, CultureInfo.InvariantCulture);
            var gpu = Required("--gpu-uuid");
            if (stage is < 1 or > 32 || checkpointEvery is < 1 or > 32 || checkpointEvery != plan.GetProperty("CheckpointEvery").GetInt32() || declaredTimeout is < 1 or > 86400 || gpu != plan.GetProperty("ExpectedGpuUuid").GetString() || gpu != profile.GetProperty("GpuUuid").GetString() || Required("--resident") != "true") throw new InvalidDataException("CPU simulation stage/control identity differs.");
            var mode = profile.GetProperty("FixtureMode").GetString() ?? "normal";
            if (mode is not ("normal" or "slow" or "bad-checkpoint" or "bad-report-inputs" or "bad-report-flag")) throw new InvalidDataException("Unknown explicit simulation fixture case.");
            var stop = Within(Required("--stop-file")); var output = Within(Required("--output"));
            if (Path.Exists(output)) throw new IOException("Fresh simulation output required; preserve previous evidence.");
            long initial = 0;
            if (options.TryGetValue("--resume", out var predecessor))
            {
                var resume = Within(predecessor);
                using var previous = ReadSmall(SafePaths.Within(resume, "checkpoint.json", budget), budget);
                if (!previous.RootElement.GetProperty("ControllerSimulation").GetBoolean()) throw new InvalidDataException("CPU simulation cannot resume real optimizer assets.");
                var verification = new CheckpointVerificationRequest(resume, planPath, FileHash(Path.Combine(resume, "checkpoint.json"), budget), FileHash(Path.Combine(resume, "corpus-cursor.json"), budget), inputs);
                initial = TaskOperations.VerifyCheckpoint(verification, budget).CompletedUpdates;
            }
            if (initial >= global || stage > global - initial) throw new InvalidDataException("Simulation stage exceeds remaining global cursor.");
            Directory.CreateDirectory(output); SafePaths.NoLinks(output, budget);
            var completed = initial; var reason = "stage_update_bound"; SavedCheckpoint? saved = null;
            var processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            for (long iteration = 0; iteration < stage; iteration++)
            {
                budget.Check();
                if (File.Exists(stop)) { reason = "stop_file"; break; }
                completed++;
                if (completed % checkpointEvery == 0) saved = Save(output, run, completed, records, epochs, inputs, mode, budget);
                if (iteration == 0)
                {
                    var pending = Path.Combine(output, "fixture-ready.pending.json");
                    TaskOperations.WriteNew(pending, new SimulationReady(SimulationStatus, true, Environment.ProcessId, processStart, completed), FixtureJson.Default.SimulationReady);
                    File.Move(pending, Path.Combine(output, "fixture-ready.json"), false);
                }
                await Task.Delay(mode == "slow" ? 500 : 15, cancellation.Token).ConfigureAwait(false);
            }
            if (completed == initial) throw new InvalidDataException("Simulation stopped before its first complete identity update.");
            if (saved is null || saved.CompletedUpdates != completed) saved = Save(output, run, completed, records, epochs, inputs, mode, budget);
            var reportInputs = mode == "bad-report-inputs" ? inputs with { BaseEncoderSha256 = new string('0', 64) } : inputs;
            var report = new SimulationReport("SIMULATION_cpu_controller_saved_cursor_only", run, reportInputs, gpu, Environment.ProcessId, processStart, initial, completed, stage, global, checkpointEvery,
                saved.CheckpointSha256, saved.CursorSha256, completed == global, false, false, mode != "bad-report-flag", false, false, false, true, reason);
            TaskOperations.WriteNew(Path.Combine(output, "training-report.json"), report, FixtureJson.Default.SimulationReport);
            File.WriteAllText(Path.Combine(output, "resources-reclaimed.txt"), "SIMULATION_cpu_fixture_resources_reclaimed\n", new UTF8Encoding(false));
            Console.WriteLine("SIMULATION_cpu_controller_fixture_only completed=" + completed.ToString(CultureInfo.InvariantCulture));
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or JsonException or FormatException or OverflowException or TimeoutException or OperationCanceledException)
        {
            Console.Error.WriteLine("SIMULATION_cpu_controller_fixture_only failure=" + error.GetType().Name);
            return 7;
        }
    }

    private static int Query(string[] args)
    {
        if (args.Length != 3 || args[2] != "--format=csv,noheader,nounits" || !args[0].StartsWith("--id=GPU-", StringComparison.Ordinal) || !Guid.TryParseExact(args[0][9..], "D", out _)) throw new ArgumentException("CPU fixture query identity differs.");
        Console.Error.WriteLine(SimulationStatus + ";pid=" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ";simulated_driver_query=true");
        if (args[1] == "--query-gpu=uuid,memory.free") Console.WriteLine(args[0][5..] + ", 65536");
        else if (args[1] != "--query-compute-apps=gpu_uuid,pid") throw new ArgumentException("Unknown CPU fixture query.");
        return 0;
    }

    private static SavedCheckpoint Save(string output, string run, long completed, long records, int epochs, CheckpointInputContract inputs, string mode, Budget budget)
    {
        budget.Check(); var directory = SafePaths.Within(output, "checkpoint-" + completed.ToString("D10", CultureInfo.InvariantCulture), budget);
        if (Path.Exists(directory)) throw new IOException("Synthetic checkpoint cannot be overwritten.");
        Directory.CreateDirectory(directory);
        var weights = Encoding.ASCII.GetBytes("SIMULATION_WEIGHTS_IDENTITY_" + completed.ToString(CultureInfo.InvariantCulture));
        var moments = Encoding.ASCII.GetBytes("SIMULATION_MOMENTS_IDENTITY_" + completed.ToString(CultureInfo.InvariantCulture));
        File.WriteAllBytes(Path.Combine(directory, "weights.safetensors"), weights); File.WriteAllBytes(Path.Combine(directory, "moments.safetensors"), moments);
        var epoch = completed / records; var within = completed % records;
        var schedule = new SimulationSchedule(epoch, within, epoch == epochs ? 0 : within, 0, epoch == epochs ? [] : [new("cpu-controller-fixture", 0, within, within)]);
        var scheduleBytes = JsonSerializer.SerializeToUtf8Bytes(schedule, FixtureJson.Default.SimulationSchedule);
        var checkpoint = new SimulationCheckpoint(1, run, completed, inputs, mode == "bad-checkpoint" ? new string('0', 64) : Hash(weights), Hash(moments), Hash(scheduleBytes), true, true);
        var checkpointPath = Path.Combine(directory, "checkpoint.json"); TaskOperations.WriteNew(checkpointPath, checkpoint, FixtureJson.Default.SimulationCheckpoint);
        var checkpointSha = FileHash(checkpointPath, budget);
        var cursor = new SimulationCursor(1, run, inputs, completed, epoch, within, records, epochs, checkpointSha, TaskOperations.ScheduleAlgorithm, schedule, true, true);
        var cursorPath = Path.Combine(directory, "corpus-cursor.json"); TaskOperations.WriteNew(cursorPath, cursor, FixtureJson.Default.SimulationCursor);
        return new(directory, completed, checkpointSha, FileHash(cursorPath, budget));
    }
    private static JsonDocument ReadSmall(string path, Budget budget)
    {
        RegularFiles.Require(path, budget); if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("CPU fixture metadata bound128KiB.");
        return TaskOperations.ReadDocument(path, budget);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string FileHash(string path, Budget budget)
    {
        RegularFiles.Require(path, budget); using var stream = File.OpenRead(path); return budget.Hash(stream, stream.Length);
    }
}

internal sealed record SavedCheckpoint(string Directory, long CompletedUpdates, string CheckpointSha256, string CursorSha256);
internal sealed record SimulationReady(string Status, bool ControllerSimulation, int ProcessId, DateTime ProcessStartUtc, long CompletedUpdates);
internal sealed record SimulationSource(string Source, int Shard, long Row, long Accepted);
internal sealed record SimulationSchedule(long Epoch, long WithinEpoch, long Round, int NextSource, SimulationSource[] Sources);
internal sealed record SimulationCheckpoint(int SchemaVersion, string RunId, long CompletedUpdates, CheckpointInputContract Inputs, string WeightsSha256, string MomentsSha256, string CorpusScheduleSha256, bool ControllerSimulation, bool SyntheticAssets);
internal sealed record SimulationCursor(int SchemaVersion, string RunId, CheckpointInputContract Inputs, long CompletedUpdates, long Epoch, long WithinEpoch, long TrainingRecordsPerEpoch, int PlannedEpochs, string CheckpointReceiptSha256, string ScheduleAlgorithm, SimulationSchedule Schedule, bool ControllerSimulation, bool SyntheticAssets);
internal sealed record SimulationReport(string Status, string RunId, CheckpointInputContract Inputs, string GpuUuid, int ProcessId, DateTime ProcessStartUtc, long InitialUpdates, long CompletedUpdates, long StageRequestedUpdates, long GlobalUpdates, int CheckpointEvery, string CheckpointReceiptSha256, string CursorReceiptSha256, bool FullCorpusEpochCoverage, bool QualityAccepted, bool Calibrated, bool ControllerSimulation, bool GpuRun, bool OptimizerLoaded, bool AotAccepted, bool SyntheticAssets, string StopReason);

[JsonSerializable(typeof(SimulationReady))]
[JsonSerializable(typeof(SimulationSchedule))]
[JsonSerializable(typeof(SimulationCheckpoint))]
[JsonSerializable(typeof(SimulationCursor))]
[JsonSerializable(typeof(SimulationReport))]
[JsonSourceGenerationOptions(WriteIndented = false, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class FixtureJson : JsonSerializerContext;
