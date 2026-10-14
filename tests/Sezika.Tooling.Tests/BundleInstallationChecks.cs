using System.Security.Cryptography;
using System.Text;
using Sezika.Tooling;

internal static class BundleInstallationChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        Budget Limit() => new(35, null, cancellation.Token);
        static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Reject(Action action)
        {
            try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OperationCanceledException or TimeoutException) { return; }
            throw new InvalidOperationException("Expected installation rejection.");
        }
        var fixture = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_FIXTURE") ?? throw new InvalidOperationException("Pinned ProcessFixture bundle required for installation checks.");
        var broker = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_BROKER");
        var source = Path.Combine(root, "installation-source"); Directory.CreateDirectory(source);
        var stem = Path.Combine(Path.GetDirectoryName(fixture)!, "Sezika.ProcessFixture");
        var members = new List<string>();
        foreach (var file in new[] { fixture, stem + ".dll", stem + ".deps.json", stem + ".runtimeconfig.json" }.Distinct(StringComparer.Ordinal))
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (!File.Exists(file)) { if (file == fixture) throw new FileNotFoundException("Native fixture missing."); continue; }
            var name = Path.GetFileName(file); File.Copy(file, Path.Combine(source, name), false); members.Add(name);
        }
        string Sha(string path) { using var input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
        var manifest = Path.Combine(source, "files.sha256");
        File.WriteAllText(manifest, string.Join('\n', members.OrderBy(value => value, StringComparer.Ordinal).Select(name => Sha(Path.Combine(source, name)) + "  " + name)) + "\n", new UTF8Encoding(false));
        members.Add("files.sha256");
        var pins = members.Select(name => new ReviewFilePin(name, new FileInfo(Path.Combine(source, name)).Length, Sha(Path.Combine(source, name)))).ToArray();
        var pack = TaskOperations.Pack(new(source, Path.Combine(root, "installation-source.tar")), Limit());
        var fixtureName = Path.GetFileName(fixture); var requests = 0;
        BundleInstallationRequest Request(string name)
        {
            if (++requests > 16) throw new InvalidDataException("Installation fixture request bound16.");
            return new(pack.Archive, pack.Sha256, Path.Combine(root, "installed-" + name), pins, [fixtureName], [new(fixtureName, "--help")],
                Path.Combine(root, "installation-evidence-" + name), MaximumBytes: 64 * 1024 * 1024, BrokerExecutable: broker);
        }
        Task<ExecutionResult> Synthetic(ExecutionRequest execution, Budget budget, CancellationToken token)
        {
            budget.Check(); token.ThrowIfCancellationRequested(); Directory.CreateDirectory(execution.OutputDirectory);
            File.WriteAllText(Path.Combine(execution.OutputDirectory, "stderr.bin"), "SYNTHETIC_dependency_fixture_only\n", new UTF8Encoding(false));
            return Task.FromResult(new ExecutionResult { Status = "PASS", ExecutionStatus = "SUCCEEDED", ExitCode = 0,
                CleanupStatus = "PASS", RemainingProcesses = 0, InputComplete = true, OutputComplete = true,
                Executable = execution.Executable, ArgumentList = execution.ArgumentList, OutputDirectory = execution.OutputDirectory });
        }
        BundleInstallationReport Simulate(BundleInstallationRequest request,
            Func<ExecutionRequest, Budget, CancellationToken, Task<ExecutionResult>>? execute = null)
            => BundleInstallation.RunSyntheticAsync(request, Limit(), cancellation.Token, execute ?? Synthetic).GetAwaiter().GetResult();
        var positive = Request("tiny");
        check("installation tiny complete archive and declared native probe remain explicitly synthetic", () =>
        {
            var report = Simulate(positive);
            Assert(report.Status == "PASS_SYNTHETIC_INSTALLATION_CONTRACT_ONLY" && report.DependencyProbesPassed && report.SyntheticTransport &&
                !report.ServerAccepted && !report.TrainingRun && report.Files == pins.Length && report.Probes.Single().NativeReceipt is null &&
                !File.Exists(Path.Combine(positive.EvidenceDirectory, ".sezika-installation-in-progress")), "Synthetic installation overstated native/server acceptance.");
            foreach (var pin in pins) Assert(Sha(Path.Combine(positive.Destination, pin.Path)) == pin.Sha256, "Installed bytes differ from independently pinned archive source.");
        });
        check("installation existing verification rehashes bytes and permissions without rerunning dependencies", () =>
        {
            var request = positive with { Mode = "verify", Probes = [], EvidenceDirectory = Path.Combine(root, "installation-evidence-verify") };
            var report = BundleInstallation.RunAsync(request, Limit(), cancellation.Token).GetAwaiter().GetResult();
            Assert(report.Status == "PASS_existing_installation_bytes_permissions_only_dependencies_not_rerun" && report.ExistingFilesOnly &&
                !report.DependencyProbesPassed && report.Probes.Length == 0 && !report.ServerAccepted, "Existing bytes became a fresh dependency acceptance.");
        });
        check("installation wrong archive and existing outputs preserve original bytes", () =>
        {
            var before = Sha(Path.Combine(positive.Destination, fixtureName)); Reject(() => Simulate(positive));
            var wrong = Request("wrong-archive") with { ArchiveSha256 = new string('0', 64) }; var failed = Simulate(wrong);
            Assert(failed.Status.StartsWith("FAIL", StringComparison.Ordinal) && !Directory.Exists(wrong.Destination) &&
                Sha(Path.Combine(positive.Destination, fixtureName)) == before, "Archive or fresh-output rejection modified accepted bytes.");
        });
        check("installation complete inventory rejects missing extra and changed pinned assets", () =>
        {
            var request = Request("missing-pin") with { Files = pins.Select(pin => pin.Path == fixtureName ? pin with { Path = "not-in-archive.bin" } : pin).ToArray(), Executables = ["files.sha256"] };
            var report = Simulate(request with { Probes = [new("files.sha256", "--help")] });
            Assert(report.Status.StartsWith("FAIL", StringComparison.Ordinal) && File.Exists(Path.Combine(request.EvidenceDirectory, ".sezika-installation-in-progress")), "Undeclared archive content was accepted or failure evidence lost.");
            request = Request("changed-pin") with { Files = pins.Select(pin => pin.Path == fixtureName ? pin with { Sha256 = new string('0', 64) } : pin).ToArray() };
            Assert(Simulate(request).Status.StartsWith("FAIL", StringComparison.Ordinal), "Changed expected asset SHA was accepted.");
        });
        check("installation script paths traversal and declared byte bounds reject before extraction", () =>
        {
            var request = Request("preflight");
            Reject(() => Simulate(request with { Files = [pins[0] with { Path = "hidden.sh" }, pins.Single(pin => pin.Path == "files.sha256")] }));
            Reject(() => Simulate(request with { Executables = ["../escape"] }));
            Reject(() => Simulate(request with { MaximumBytes = 1 }));
            Assert(!Directory.Exists(request.Destination) && !Directory.Exists(request.EvidenceDirectory), "Rejected install created business output.");
        });
        check("installation usage exit needs exact stderr and help or version only", () =>
        {
            var request = Request("usage") with { Probes = [new(fixtureName, "--help", 2, "SYNTHETIC exact usage")] };
            var report = Simulate(request, async (execution, budget, token) =>
            {
                var result = await Synthetic(execution, budget, token); result.Status = "FAIL"; result.ExecutionStatus = "FAILED"; result.ExitCode = 2;
                result.Errors = ["Process exit code 2"];
                File.WriteAllText(Path.Combine(execution.OutputDirectory, "stderr.bin"), "SYNTHETIC exact usage\n", new UTF8Encoding(false)); return result;
            });
            Assert(report.Status == "PASS_SYNTHETIC_INSTALLATION_CONTRACT_ONLY" && report.Probes.Single().OriginalExecutionStatus == "FAIL", "Declared usage rejected or original FAIL rewritten.");
            var extra = Simulate(Request("usage-extra") with { Probes = [new(fixtureName, "--help", 2, "SYNTHETIC exact usage")] },
                async (execution, budget, token) =>
                {
                    var result = await Synthetic(execution, budget, token); result.Status = "FAIL"; result.ExecutionStatus = "FAILED"; result.ExitCode = 2;
                    result.Errors = ["Process exit code 2"];
                    File.WriteAllText(Path.Combine(execution.OutputDirectory, "stderr.bin"), "SYNTHETIC exact usage\nfatal dependency missing\n", new UTF8Encoding(false)); return result;
                });
            Assert(extra.Status.StartsWith("FAIL", StringComparison.Ordinal) && !extra.DependencyProbesPassed, "A usage substring concealed additional fatal dependency output.");
            Reject(() => Simulate(Request("unsafe-probe") with { Probes = [new(fixtureName, "train-corpus")] }));
        });
        check("installation failed cleanup or mismatched executed arguments cannot certify dependency probes", () =>
        {
            foreach (var mode in new[] { "cleanup", "arguments", "marker" })
            {
                var request = Request("failed-" + mode);
                var report = Simulate(request, async (execution, budget, token) =>
                {
                    var result = await Synthetic(execution, budget, token);
                    if (mode == "cleanup") result.CleanupStatus = "UNKNOWN";
                    else if (mode == "arguments") result.ArgumentList = ["different"];
                    else
                    {
                        var marker = Path.Combine(request.EvidenceDirectory, ".sezika-installation-in-progress");
                        File.Copy(marker, Path.Combine(request.EvidenceDirectory, "marker-original.txt"), false);
                        File.Delete(marker); Directory.CreateDirectory(marker); // Owned fault fixture; File.Delete must refuse a directory.
                    }
                    return result;
                });
                Assert(report.Status.StartsWith("FAIL", StringComparison.Ordinal) && !report.DependencyProbesPassed &&
                    Directory.Exists(request.Destination) && Path.Exists(Path.Combine(request.EvidenceDirectory, ".sezika-installation-in-progress")), "A failed dependency probe was promoted or partial evidence removed.");
                using var final = TaskOperations.ReadDocument(Path.Combine(request.EvidenceDirectory, "installation.json"), Limit());
                Assert(final.RootElement.GetProperty("Status").GetString()!.StartsWith("FAIL", StringComparison.Ordinal), "Final failure artifact retained a PASS conclusion.");
            }
        });
        check("installation cancelled preflight creates no output", () =>
        {
            var request = Request("cancelled"); var cancelled = new CancellationToken(canceled: true);
            Reject(() => BundleInstallation.RunAsync(request, new Budget(10, null, cancelled), cancelled).GetAwaiter().GetResult());
            Assert(!Directory.Exists(request.Destination) && !Directory.Exists(request.EvidenceDirectory), "Cancelled install created output.");
        });
        check("installation and observation CLI cannot write outer failure receipts into protected trees", () =>
        {
            var request = positive with { Mode = "verify", Probes = [], EvidenceDirectory = Path.Combine(root, "installation-cli-evidence") };
            var requestFile = Path.Combine(root, "installation-cli-request.json"); TaskOperations.WriteNew(requestFile, request, BundleInstallationJson.Default.BundleInstallationRequest);
            var forbidden = Path.Combine(positive.Destination, "must-not-create.json");
            Assert(ToolingCommand.Run(["bundle", "install", "--request", requestFile, "--output", forbidden]) == 1 && !File.Exists(forbidden) &&
                !Directory.Exists(request.EvidenceDirectory), "CLI error fallback polluted an existing installation tree.");
            if (OperatingSystem.IsWindows())
                Assert(ToolingCommand.Run(["bundle", "install", "--request", requestFile, "--output", forbidden.ToUpperInvariant()]) == 1 && !File.Exists(forbidden), "Windows case alias bypassed the protected output root.");
            var gpuRoot = Path.Combine(root, "observation-protected"); Directory.CreateDirectory(gpuRoot);
            var gpuRequest = new GpuObservationRequest(fixture, Sha(fixture), "GPU-11111111-2222-3333-4444-555555555555", gpuRoot);
            var gpuRequestFile = Path.Combine(root, "observation-cli-request.json"); TaskOperations.WriteNew(gpuRequestFile, gpuRequest, GpuObservationJsonContext.Default.GpuObservationRequest);
            var gpuForbidden = Path.Combine(gpuRoot, "summary.json");
            Assert(ToolingCommand.Run(["host", "sample-gpu", "--request", gpuRequestFile, "--output", gpuForbidden]) == 1 && !File.Exists(gpuForbidden), "CLI error fallback polluted protected observation evidence.");
        });
        check("installation real native fixture help binds exact execution and complete cleanup", () =>
        {
            var request = Request("native-help");
            var report = BundleInstallation.RunAsync(request, Limit(), cancellation.Token).GetAwaiter().GetResult();
            Assert(report.Status == "PASS_fresh_installation_bytes_permissions_dependency_probes_only" && !report.SyntheticTransport &&
                report.DependencyProbesPassed && report.Probes.Single().NativeReceipt is not null && report.Probes.Single().NativeReceiptSha256?.Length == 64 &&
                !report.ServerAccepted && !report.TrainingRun, "Real native dependency probe lacks its own execution evidence.");
        });
    }
}
