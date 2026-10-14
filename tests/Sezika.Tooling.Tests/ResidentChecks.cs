using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.Tooling;

internal static class ResidentChecks
{
    private sealed record Fixture(string Directory, ResidentRequest Request, CheckpointInputContract Inputs, ResidentPlanFixture Plan, ResidentProfileFixture Profile);

    internal static void Run(string root, Action<string, Action> check)
    {
        var image = Environment.GetEnvironmentVariable("SEZIKA_CONTROLLER_FIXTURE") ?? throw new InvalidOperationException("SEZIKA_CONTROLLER_FIXTURE must name the separately built native C# CPU simulation apphost.");
        var broker = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_BROKER");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var clock = Stopwatch.StartNew(); var fixtures = 0; var operations = 0; var receipts = 0; var identities = 0;
        Budget Limit() => new(700, null, cancellation.Token);
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Reject(Action action)
        {
            try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OperationCanceledException or TimeoutException) { return; }
            throw new InvalidOperationException("Expected explicit controller simulation rejection.");
        }
        void Gate()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 170) throw new TimeoutException("Resident simulation suite bound180s.");
        }
        string Hash(string path)
        {
            RegularFiles.Require(path, Limit()); using var stream = File.OpenRead(path); return Limit().Hash(stream, stream.Length);
        }
        void Write<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        {
            Gate(); SafePaths.NoLinks(path, Limit()); File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, type));
        }
        Fixture Prepare(string name, string mode = "normal", long records = 2, int epochs = 2, string? mismatch = null)
        {
            Gate(); if (++fixtures > 12) throw new InvalidDataException("Controller simulation fixture bound12.");
            var directory = Path.GetFullPath(Path.Combine(root, "resident-fixture-" + name)); var run = "cpu-controller-" + name;
            var source = new string('a', 40); var dataset = new string('b', 40); var model = new string('c', 40); var gpu = Guid.NewGuid().ToString("N"); var manifest = new string('d', 64);
            _ = TaskOperations.Prepare(new(run, directory, source, dataset, model, 600, 60, records, epochs, 2, 1), Limit());
            var plan = new ResidentPlanFixture(run, records, epochs, 17, mismatch == "SourceCommit" ? new string('f', 40) : source, mismatch == "DatasetCommit" ? new string('f', 40) : dataset, mismatch == "ModelCommit" ? new string('f', 40) : model, manifest, 2, 1, gpu, 1048576, true);
            var planPath = Path.Combine(directory, "plan.json"); Write(planPath, plan, ResidentFixtureJson.Default.ResidentPlanFixture);
            var orderBytes = Encoding.UTF8.GetBytes(TaskOperations.ScheduleAlgorithm + "\n17\n" + epochs.ToString(CultureInfo.InvariantCulture) + "\n" + records.ToString(CultureInfo.InvariantCulture) + "\n" + manifest);
            var inputs = new CheckpointInputContract(Hash(planPath), manifest, new string('e', 64), new string('f', 64), new string('a', 64), Convert.ToHexStringLower(SHA256.HashData(orderBytes)), new string('b', 64));
            var profile = new ResidentProfileFixture("SIMULATION_cpu_controller_fixture_only", run, inputs, gpu, false, 0.01, 0.01, true, true, false, false, false, mode);
            var profilePath = Path.Combine(directory, "profile.json"); Write(profilePath, profile, ResidentFixtureJson.Default.ResidentProfileFixture);
            var request = new ResidentRequest(directory, image, Hash(image), planPath, inputs.PlanSha256, profilePath, Hash(profilePath), gpu, image, Hash(image), 2, 600, 1, BrokerExecutable: broker);
            return new(directory, request, inputs, plan, profile);
        }
        Fixture Profile(Fixture fixture, ResidentProfileFixture profile)
        {
            Write(fixture.Request.ProfilePath, profile, ResidentFixtureJson.Default.ResidentProfileFixture);
            return fixture with { Profile = profile, Request = fixture.Request with { ProfileSha256 = Hash(fixture.Request.ProfilePath) } };
        }
        void Cleanup(ExecutionResult result)
        {
            Gate();
            Assert(result.RemainingProcesses == 0 && result.CleanupStatus == "PASS" && result.Processes.Length is > 0 and <= 64 && result.RootPid is not null, "Simulation native tree cleanup/identity is incomplete: " + result.OutputDirectory);
            foreach (var identity in result.Processes)
            {
                Gate(); if (++identities > 2048) throw new InvalidDataException("Simulation process identity bound2048.");
                Assert(identity.Pid > 0 && identity.ParentPid > 0 && identity.ParentChain.Length is > 0 and <= 64 && identity.CommandLine.Length > 0 && (identity.Platform == "windows" ? identity.Started is not null : identity.StartTicks is not null && identity.BootId is not null), "Simulation PID/birth/full argv/parent identity missing.");
                var current = ProcessObserver.Observe(identity.Pid);
                var reused = current.State == "Present" && (identity.Platform == "windows" ? current.Started is not null && current.Started != identity.Started : current.StartTicks is not null && (current.StartTicks != identity.StartTicks || current.BootId != identity.BootId));
                Assert(current.State == "Absent" || reused, "Simulation native tree survives or absence is UNKNOWN.");
            }
        }
        ExecutionResult ReadReceipt(string path)
        {
            Gate(); if (++receipts > 320) throw new InvalidDataException("Simulation process receipt bound320.");
            using var stream = File.OpenRead(path); var result = JsonSerializer.Deserialize(stream, ExecutionJsonContext.Default.ExecutionResult) ?? throw new InvalidDataException("Simulation native receipt absent."); Cleanup(result); return result;
        }
        string[] Stages(Fixture fixture)
        {
            var stages = Directory.EnumerateDirectories(fixture.Directory, "resident-*").Take(17).ToArray();
            if (stages.Length > 16) throw new InvalidDataException("Controller simulation stage directory bound16."); return stages;
        }
        void Audit(Fixture fixture)
        {
            foreach (var stage in Stages(fixture))
                foreach (var relative in new[] { "admission-gpu/result.json", "admission-compute/result.json", "execution/result.json" })
                {
                    Gate(); var path = Path.Combine(stage, relative); if (File.Exists(path)) _ = ReadReceipt(path);
                }
        }
        ResidentReceipt Run(Fixture fixture, ResidentRequest? request = null, bool production = false)
        {
            Gate(); if (++operations > 16) throw new InvalidDataException("Controller simulation operation bound16.");
            try { return (production ? ResidentController.RunAsync(request ?? fixture.Request, Limit(), cancellation.Token) : ResidentController.RunSimulationAsync(request ?? fixture.Request, Limit(), cancellation.Token)).GetAwaiter().GetResult(); }
            finally { Audit(fixture); }
        }
        void NoLaunch(Fixture fixture) => Assert(Stages(fixture).Length == 0, "Rejected controller contract started a native driver/trainer stage.");
        void Simulation(ResidentReceipt result)
        {
            Assert(result.ControllerSimulation && result.Status == "SIMULATION_cpu_controller_saved_cursor_only" && !result.ModelQualityAccepted && !result.Calibrated && !result.Published, "Synthetic controller evidence lost its explicit scope.");
            var execution = ReadReceipt(result.ProcessReceipt); Assert(execution.Status == "PASS" && execution.ExitCode == 0, "Simulation trainer did not exit cleanly.");
            using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(result.CheckpointDirectory)!, "training-report.json")));
            var element = report.RootElement;
            Assert(element.GetProperty("ControllerSimulation").GetBoolean() && element.GetProperty("SyntheticAssets").GetBoolean() && !element.GetProperty("GpuRun").GetBoolean() && !element.GetProperty("OptimizerLoaded").GetBoolean() && !element.GetProperty("AotAccepted").GetBoolean() && element.GetProperty("ProcessId").GetInt32() == execution.RootPid, "Synthetic native process/report binding or acceptance labels differ.");
        }
        async Task Signal(Fixture fixture, string filename, CancellationToken token)
        {
            var wait = Stopwatch.StartNew();
            for (var attempt = 0; attempt < 200 && wait.Elapsed.TotalSeconds < 10; attempt++)
            {
                token.ThrowIfCancellationRequested();
                foreach (var stage in Stages(fixture))
                {
                    var ready = Path.Combine(stage, "training", "fixture-ready.json");
                    if (!File.Exists(ready)) continue;
                    using var proof = JsonDocument.Parse(File.ReadAllBytes(ready));
                    Assert(proof.RootElement.GetProperty("ControllerSimulation").GetBoolean() && proof.RootElement.GetProperty("CompletedUpdates").GetInt64() >= 1, "Signal preceded a complete synthetic identity update.");
                    var marker = SafePaths.Within(fixture.Directory, filename, Limit());
                    using var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    var bytes = Encoding.UTF8.GetBytes("human " + filename + " CPU simulation fixture\n"); stream.Write(bytes); stream.Flush(true); return;
                }
                await Task.Delay(50, token).ConfigureAwait(false);
            }
            throw new TimeoutException("CPU fixture ready signal exceeded200 polls/10s.");
        }
        void JoinSignal(CancellationTokenSource signalCancellation, Task signal)
        {
            signalCancellation.Cancel(); try { signal.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        }

        check("Simulation tiny native CPU query has explicit labels and complete process cleanup", () =>
        {
            var directory = Path.Combine(root, "resident-query"); Directory.CreateDirectory(directory); var uuid = Guid.NewGuid().ToString("D");
            var result = ProcessExecutor.ExecuteAsync(new ExecutionRequest { Executable = image, ArgumentList = ["--id=GPU-" + uuid, "--query-gpu=uuid,memory.free", "--format=csv,noheader,nounits"], WorkingDirectory = directory, OutputDirectory = Path.Combine(directory, "execution"), TimeoutSeconds = 10, CleanupSeconds = 5, BrokerExecutable = broker }, new Budget(20, null, cancellation.Token), cancellation.Token).GetAwaiter().GetResult();
            Cleanup(result); Assert(result.Status == "PASS" && File.ReadAllText(Path.Combine(result.OutputDirectory, "stdout.bin")).Trim() == "GPU-" + uuid + ", 65536" && File.ReadAllText(Path.Combine(result.OutputDirectory, "stderr.bin")).Contains("SIMULATION_cpu_controller_fixture_only", StringComparison.Ordinal), "Native query is not the explicit CPU fixture.");
        });
        check("Simulation prepared first save restores exact inputs and reaches only the synthetic global cursor", () =>
        {
            var fixture = Prepare("resume"); var first = Run(fixture); Simulation(first); Assert(first.CompletedUpdates == 2 && first.GlobalUpdates == 4 && !first.FullEpochCoverage, "Initial synthetic stage/global denominator differs.");
            var verification = new CheckpointVerificationRequest(first.CheckpointDirectory, fixture.Request.PlanPath, first.CheckpointSha256, first.CursorSha256, fixture.Inputs);
            var identity = TaskOperations.VerifyCheckpoint(verification, Limit()); Assert(!identity.GpuRun && !identity.OptimizerLoaded && identity.CompletedUpdates == 2, "Synthetic four-file identity became GPU/optimizer acceptance.");
            var next = fixture.Request with { ResumeDirectory = first.CheckpointDirectory, ResumeIdentity = verification };
            Reject(() => Run(fixture, next with { ResumeIdentity = verification with { Inputs = fixture.Inputs with { BaseEncoderSha256 = new string('0', 64) } } })); Assert(Stages(fixture).Length == 1, "Mismatched predecessor inputs launched a resumed stage.");
            var final = Run(fixture, next); Simulation(final); Assert(final.CompletedUpdates == 4 && final.GlobalUpdates == 4 && final.FullEpochCoverage, "Synthetic resume did not reach its pinned global cursor.");
            using var cursor = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(final.CheckpointDirectory, "corpus-cursor.json")));
            Assert(cursor.RootElement.GetProperty("ControllerSimulation").GetBoolean() && cursor.RootElement.GetProperty("Epoch").GetInt64() == 2 && cursor.RootElement.GetProperty("WithinEpoch").GetInt64() == 0 && cursor.RootElement.GetProperty("Schedule").GetProperty("Sources").GetArrayLength() == 0, "Synthetic final streaming schedule differs.");
        });
        check("Simulation source model dataset and prepared time mismatch reject before native launch", () =>
        {
            foreach (var mismatch in new[] { "SourceCommit", "ModelCommit", "DatasetCommit", "MaximumSeconds" })
            {
                Gate(); var fixture = Prepare(mismatch.ToLowerInvariant(), mismatch: mismatch);
                Reject(() => Run(fixture, mismatch == "MaximumSeconds" ? fixture.Request with { MaximumSeconds = 601 } : fixture.Request)); NoLaunch(fixture);
            }
        });
        check("Simulation wrong profile and production admission refuse synthetic evidence", () =>
        {
            var fixture = Prepare("profile"); Reject(() => Run(fixture, fixture.Request with { ProfileSha256 = new string('0', 64) })); NoLaunch(fixture);
            var invalid = Profile(fixture, fixture.Profile with { Status = "INVALID_cpu_simulation_profile" }); Reject(() => Run(invalid)); NoLaunch(invalid);
            var restored = Profile(invalid, fixture.Profile); Reject(() => Run(restored, production: true)); NoLaunch(restored);
        });
        check("Simulation bad saved checkpoint bytes are rejected after a clean native exit", () =>
        {
            var fixture = Prepare("checkpoint", "bad-checkpoint"); Reject(() => Run(fixture));
            var stage = Stages(fixture).Single(); var execution = ReadReceipt(Path.Combine(stage, "execution", "result.json"));
            Assert(execution.Status == "PASS" && Directory.Exists(Path.Combine(stage, "training", "checkpoint-0000000002")), "Invalid synthetic checkpoint or native failure evidence was lost.");
        });
        check("Simulation bad report input contract and missing simulation flag are rejected", () =>
        {
            foreach (var mode in new[] { "bad-report-inputs", "bad-report-flag" })
            {
                Gate(); var fixture = Prepare(mode, mode); Reject(() => Run(fixture));
                var stage = Stages(fixture).Single(); Assert(ReadReceipt(Path.Combine(stage, "execution", "result.json")).Status == "PASS" && File.Exists(Path.Combine(stage, "training", "training-report.json")), "Malformed synthetic report evidence was lost.");
            }
        });
        check("Simulation human STOP permits a safe save and is preserved without hard termination", () =>
        {
            var fixture = Prepare("stop", "slow", 12, 1); var request = fixture.Request with { StageUpdates = 8 };
            using var signalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); var signal = Signal(fixture, "STOP", signalCancellation.Token);
            ResidentReceipt result;
            try { result = Run(fixture, request); signal.GetAwaiter().GetResult(); }
            finally { JoinSignal(signalCancellation, signal); }
            Simulation(result); var execution = ReadReceipt(result.ProcessReceipt);
            Assert(result.CompletedUpdates is >= 1 and < 8 && result.StopReason == "stop_file" && execution.ExecutionStatus == "SUCCEEDED" && File.ReadAllText(Path.Combine(fixture.Directory, "STOP")) == "human STOP CPU simulation fixture\n", "STOP was cleared, became hard termination, or failed to preserve a bounded save.");
        });
        check("Simulation human CANCEL preserves failed receipts and reclaims the exact native tree", () =>
        {
            var fixture = Prepare("cancel", "slow", 12, 1); var request = fixture.Request with { StageUpdates = 8 };
            using var signalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); var signal = Signal(fixture, "CANCEL", signalCancellation.Token);
            try { Reject(() => Run(fixture, request)); signal.GetAwaiter().GetResult(); }
            finally { JoinSignal(signalCancellation, signal); }
            var stage = Stages(fixture).Single(); var execution = ReadReceipt(Path.Combine(stage, "execution", "result.json"));
            Assert(execution.Status == "FAIL" && execution.ExecutionStatus == "CANCELLED" && execution.RemainingProcesses == 0 && File.ReadAllText(Path.Combine(fixture.Directory, "CANCEL")) == "human CANCEL CPU simulation fixture\n" && File.Exists(Path.Combine(stage, "training", "fixture-ready.json")), "CANCEL failed to retain immutable failure evidence or reclaim its owned native tree.");
        });
    }
}

internal sealed record ResidentPlanFixture(string RunId, long TrainRecords, int Epochs, int Seed, string SourceCommit, string DatasetCommit, string ModelCommit, string CorpusManifestSha256, int MaximumTokens, int CheckpointEvery, string ExpectedGpuUuid, ulong MaximumOwnedBytes, bool ControllerSimulation);
internal sealed record ResidentProfileFixture(string Status, string RunId, CheckpointInputContract Inputs, string GpuUuid, bool QualityAccepted, double MaximumStepSeconds, double CheckpointSeconds, bool ControllerSimulation, bool SyntheticAssets, bool GpuRun, bool OptimizerLoaded, bool AotAccepted, string FixtureMode);
[JsonSerializable(typeof(ResidentPlanFixture))]
[JsonSerializable(typeof(ResidentProfileFixture))]
[JsonSourceGenerationOptions(WriteIndented = false, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class ResidentFixtureJson : JsonSerializerContext;
