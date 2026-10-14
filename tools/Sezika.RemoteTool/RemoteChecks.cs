using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

namespace Sezika.RemoteTool;

internal static class RemoteChecks
{
    internal static int Run(string path, int maximumChecks, CancellationToken cancellation)
    {
        path = Path.GetFullPath(path); var budget = new Budget(20, null, cancellation); SafePaths.NoLinks(path, budget);
        if (Path.Exists(path)) throw new IOException("Remote checks output exists."); Directory.CreateDirectory(path);
        var checks = 0; var names = new List<string>(); var clock = Stopwatch.StartNew();
        void Check(string name, Action action)
        {
            if (checks >= maximumChecks) return; budget.Check(); if (clock.Elapsed.TotalSeconds >= 20) throw new TimeoutException("Remote fixture wall cap.");
            action(); checks++; names.Add(name); Console.WriteLine("PASS " + checks + ": " + name);
        }
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Reject(Action action)
        {
            var rejected = false; try { action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException or FormatException or IOException) { rejected = true; }
            Assert(rejected, "Remote expected rejection absent.");
        }
        var key = "explicit-offline-key"u8.ToArray(); var sha = Convert.ToHexStringLower(SHA256.HashData(key));
        var pin = "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');
        var workerDirectory = Path.Combine(path, "worker-bundle"); var payloadDirectory = Path.Combine(path, "payload-bundle");
        Directory.CreateDirectory(workerDirectory); Directory.CreateDirectory(payloadDirectory);
        File.WriteAllBytes(Path.Combine(workerDirectory, "worker"), key); File.WriteAllBytes(Path.Combine(payloadDirectory, "payload"), key);
        var workerBundle = BundleProof.Capture(workerDirectory, "/tmp/sezika-fixture/worker-bundle", "worker", RuntimeBundleKind.NativeAotImage, budget);
        var payloadBundle = BundleProof.Capture(payloadDirectory, "/tmp/sezika-fixture/payload-bundle", "payload", RuntimeBundleKind.NativeAotImage, budget);
        var config = new RemoteConfiguration(1, "127.0.0.1", 2222, "fixture-user", pin, "/tmp/sezika-fixture", "/tmp/sezika-fixture/worker-bundle/worker", sha,
            [new("/tmp/sezika-fixture/payload-bundle/payload", sha)], "stdin", "private-key", WorkerBundle: workerBundle, AllowedPayloadBundles: [payloadBundle]);
        Check("remote tiny explicit SHA256 key identity", () => Assert(RemotePaths.MatchesPin(pin, key), "Exact key pin differs."));
        var canonicalFixture = new RuntimeBundleManifest(1, "/tmp/sezika-canonical-v1/worker", "worker", RuntimeBundleKind.NativeAotImage,
            [new("worker", 3, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"),
             new("worker.dbg", 4, "4c8a43980498636e9c1d1595fa5d115af7937c2422dfe68a2520a52b7a5fb4de")]);
        const string canonicalHash = "eda730047f69d0ccd4dc735a2d5ca95712d6112212bb37039588b1b1472c1fc4";
        const string canonicalLiteral = "{\n  \"SchemaVersion\": 1,\n  \"Directory\": \"/tmp/sezika-canonical-v1/worker\",\n" +
            "  \"EntryPoint\": \"worker\",\n  \"Runtime\": 0,\n  \"Files\": [\n    {\n      \"RelativePath\": \"worker\",\n" +
            "      \"Bytes\": 3,\n      \"Sha256\": \"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\"\n" +
            "    },\n    {\n      \"RelativePath\": \"worker.dbg\",\n      \"Bytes\": 4,\n" +
            "      \"Sha256\": \"4c8a43980498636e9c1d1595fa5d115af7937c2422dfe68a2520a52b7a5fb4de\"\n    }\n  ]\n}";
        Check("remote canonical two-member manifest has fixed LF bytes and known SHA", () =>
        {
            var expected = Encoding.UTF8.GetBytes(canonicalLiteral); var actual = BundleProof.CanonicalBytes(canonicalFixture);
            Assert(expected.Length == 429 && actual.AsSpan().SequenceEqual(expected) && !actual.Contains((byte)'\r'), "Canonical manifest bytes depend on host formatting.");
            Assert(BundleProof.Hash(canonicalFixture) == canonicalHash, "Known independent LF manifest SHA differs.");
            BundleProof.Validate(new(canonicalFixture, canonicalHash), budget);
            var crlf = JsonSerializer.Deserialize(Encoding.UTF8.GetBytes(canonicalLiteral.Replace("\n", "\r\n", StringComparison.Ordinal)),
                RemoteJsonContext.Default.RuntimeBundleManifest) ?? throw new InvalidDataException("Canonical CRLF input fixture missing.");
            Assert(BundleProof.Hash(crlf) == canonicalHash && BundleProof.CanonicalBytes(crlf).AsSpan().SequenceEqual(expected), "CRLF input did not recanonicalize to exact LF bytes.");
        });
        Check("remote canonical manifest binds schema version scope runtime and every ordered source pin", () =>
        {
            RuntimeBundleManifest[] changed =
            [
                canonicalFixture with { SchemaVersion = 2 },
                canonicalFixture with { Directory = "/tmp/sezika-canonical-v2/worker" },
                canonicalFixture with { EntryPoint = "other" },
                canonicalFixture with { Runtime = RuntimeBundleKind.SelfContainedDotnet },
                canonicalFixture with { Files = [canonicalFixture.Files[1], canonicalFixture.Files[0]] },
                canonicalFixture with { Files = [canonicalFixture.Files[0]] },
                canonicalFixture with { Files = [canonicalFixture.Files[0] with { RelativePath = "other" }, canonicalFixture.Files[1]] },
                canonicalFixture with { Files = [canonicalFixture.Files[0] with { Bytes = 4 }, canonicalFixture.Files[1]] },
                canonicalFixture with { Files = [canonicalFixture.Files[0] with { Sha256 = new('0', 64) }, canonicalFixture.Files[1]] },
                canonicalFixture with { Files = [canonicalFixture.Files[0], canonicalFixture.Files[1] with { Sha256 = new('0', 64) }] }
            ];
            for (var index = 0; index < changed.Length && index < 10; index++)
            {
                budget.Check(); Assert(BundleProof.Hash(changed[index]) != canonicalHash, "Canonical complete metadata mutation was ignored: " + index);
            }
        });
        Check("remote changed and unknown host key rejected", () => { Assert(!RemotePaths.MatchesPin(pin, "other-key"u8.ToArray()), "Changed key trusted."); Reject(() => RemotePaths.Pin("accept-new")); });
        Check("remote portable root and path escape rejected", () => { Reject(() => RemotePaths.Absolute("/tmp/../etc")); Reject(() => RemotePaths.Within(config.Root, "../etc")); Reject(() => RemotePaths.Absolute("/tmp/a;id")); Reject(() => RemotePaths.Within(config.Root, ".git/config")); });
        Check("remote host and port must be explicit", () => { RemotePaths.Validate(config); Reject(() => RemotePaths.Validate(config with { HostName = "user@host" })); Reject(() => RemotePaths.Validate(config with { Port = 0 })); });
        Check("remote current MoArk username remains valid", () => RemotePaths.Validate(config with { UserName = "root+vm-fixture" }));
        Check("remote worker command is fixed native and typed", () =>
        {
            var command = RemotePaths.Command(config, config.Root + "/requests/request-123.json", sha);
            Assert(command == config.WorkerPath + " worker --request " + config.Root + "/requests/request-123.json --sha256 " + sha, "Fixed worker command changed.");
            Reject(() => RemotePaths.Command(config, "/etc/request.json", sha)); Reject(() => RemotePaths.Command(config with { WorkerPath = "/bin/bash" }, config.Root + "/request.json", sha));
        });
        var request = new WorkerRequest(1, "request-123", config.Root, config.WorkerPath, sha, config.AllowedExecutables, config.AllowedExecutables[0].Path, sha,
            ["--help"], config.Root, config.Root + "/run-123", 5, WorkerBundle: workerBundle, PayloadBundle: payloadBundle);
        Check("remote typed worker allowlist and byte identity", () => { RemoteWorker.Validate(request, budget, config.Root); Reject(() => RemoteWorker.Validate(request with { Executable = "/bin/sh" }, budget, config.Root)); Reject(() => RemoteWorker.Validate(request with { ExecutableSha256 = new('0', 64) }, budget, config.Root)); });
        Check("remote worker interpreter argument and oversized resource rejected", () => { Reject(() => RemoteWorker.Validate(request with { Arguments = ["payload.py"] }, budget)); Reject(() => RemoteWorker.Validate(request with { TimeoutSeconds = 86401 }, budget)); Reject(() => RemoteWorker.Validate(request with { MaximumProcesses = 257 }, budget)); });
        Check("remote transfer identity and byte bound rejected", () => { SftpTransfers.Validate(config, new(1, "transfer-123", "models/model.bin", 16, sha)); Reject(() => SftpTransfers.Validate(config, new(1, "transfer-123", "../model", 16, sha))); Reject(() => SftpTransfers.Validate(config, new(1, "transfer-123", "model", config.MaximumFileBytes + 1, sha))); });
        Check("remote source-generated request keeps secret channel separate", () =>
        {
            var file = Path.Combine(path, "connection.json"); AssetTransport.WriteJson(file, config, RemoteJsonContext.Default.RemoteConfiguration, budget);
            var actual = AssetTransport.ReadJson(file, RemoteJsonContext.Default.RemoteConfiguration, budget);
            Assert(actual.AllowedExecutables.SequenceEqual(config.AllowedExecutables) && actual.WorkerBundle?.Sha256 == workerBundle.Sha256 &&
                actual.AllowedPayloadBundles is { Length: 1 } && actual.AllowedPayloadBundles[0].Sha256 == payloadBundle.Sha256 &&
                actual with { AllowedExecutables = config.AllowedExecutables, WorkerBundle = config.WorkerBundle, AllowedPayloadBundles = config.AllowedPayloadBundles } == config, "Static connection JSON differs.");
            var json = JsonSerializer.Serialize(request, RemoteJsonContext.Default.WorkerRequest); Assert(!json.Contains("Password", StringComparison.Ordinal) && !json.Contains("PrivateKey", StringComparison.Ordinal), "Secret entered persistent worker request.");
        });
        Check("remote duplicate configuration JSON rejected", () => { var file = Path.Combine(path, "duplicate.json"); File.WriteAllText(file, "{\"SchemaVersion\":1,\"SchemaVersion\":1}"); Reject(() => AssetTransport.ReadJson(file, RemoteJsonContext.Default.RemoteConfiguration, budget)); });
        Check("remote complete bundle actual bytes and missing request binding", () =>
        {
            BundleProof.VerifyLocal(payloadBundle, budget, payloadDirectory); Reject(() => RemoteWorker.Validate(request with { PayloadBundle = null }, budget));
            Reject(() => BundleProof.Validate(payloadBundle with { Sha256 = new('0', 64) }, budget));
        });
        Check("remote same-length changed code rejected by whole bundle", () =>
        {
            File.WriteAllBytes(Path.Combine(payloadDirectory, "payload"), "changed-offline-key!"u8.ToArray());
            Reject(() => BundleProof.VerifyLocal(payloadBundle, budget, payloadDirectory)); File.WriteAllBytes(Path.Combine(payloadDirectory, "payload"), key);
        });
        Check("remote additional DLL cannot hide behind unchanged apphost SHA", () =>
        {
            File.WriteAllBytes(Path.Combine(payloadDirectory, "extra.dll"), key); Reject(() => BundleProof.VerifyLocal(payloadBundle, budget, payloadDirectory));
            File.Move(Path.Combine(payloadDirectory, "extra.dll"), Path.Combine(path, "rejected-extra-dll-evidence.bin"), overwrite: false);
        });
        Check("remote JIT bundle requires complete code and runtime metadata", () =>
        {
            var changed = payloadBundle.Manifest with { Runtime = RuntimeBundleKind.SelfContainedDotnet };
            Reject(() => BundleProof.Validate(new(changed, BundleProof.Hash(changed)), budget));
        });
        Check("remote complete typed bundle producer actual local JSON", () =>
        {
            var output = Path.Combine(path, "produced-bundle.json");
            var status = RemoteCommand.RunAsync(["bundle", "--directory", workerDirectory, "--remote-directory", workerBundle.Manifest.Directory,
                "--entry", "worker", "--runtime", "native-aot", "--output", output]).GetAwaiter().GetResult();
            var actual = AssetTransport.ReadJson(output, RemoteJsonContext.Default.BundlePin, budget);
            Assert(status == 0 && actual.Sha256 == workerBundle.Sha256 && actual.Manifest.Files.SequenceEqual(workerBundle.Manifest.Files), "Actual CLI complete bundle output differs.");
            Assert(RemoteCommand.RunAsync(["bundle", "--directory", workerDirectory, "--remote-directory", workerBundle.Manifest.Directory,
                "--entry", "worker", "--runtime", "native-aot", "--output", output]).GetAwaiter().GetResult() != 0, "Bundle producer overwrote an existing proof.");
        });
        var receiptPath = config.Root + "/requests/" + request.RequestId + ".json";
        var workerCommand = string.Join('\0', new[] { request.WorkerPath, "worker", "--request", receiptPath, "--sha256", sha }) + '\0';
        var payloadCommand = string.Join('\0', new[] { request.Executable }.Concat(request.Arguments)) + '\0';
        const string boot = "00000000-0000-0000-0000-000000000001";
        var ownership = new string('a', 32);
        var validReceipt = new ExecutionResult
        {
            Status = "PASS", ExecutionStatus = "SUCCEEDED", CleanupStatus = "PASS", ExitCode = 0, RootPid = 101, Platform = "linux",
            Executable = request.Executable, ArgumentList = request.Arguments, WorkingDirectory = request.WorkingDirectory, OutputDirectory = request.OutputDirectory,
            OwnershipId = ownership, InputComplete = true, OutputComplete = true, StandardInputBytes = request.StandardInputBytes,
            Launcher = new() { Pid = 100, Platform = "linux", ParentPid = 99, StartTicks = 10, BootId = boot, CommandLine = workerCommand },
            Processes =
            [
                new() { Pid = 101, Platform = "linux", ParentPid = 100, ParentChain = [100], StartTicks = 11, BootId = boot, ScopeId = ownership,
                    CommandLine = string.Join('\0', new[] { request.WorkerPath, "process", "broker", request.OutputDirectory + "/broker.json" }) + '\0',
                    ExpectedExecCommandLine = payloadCommand, State = "EXITED_PENDING_REAP" },
                new() { Pid = 102, Platform = "linux", ParentPid = 101, ParentChain = [100, 101], StartTicks = 12, BootId = boot, ScopeId = ownership,
                    CommandLine = string.Join('\0', new[] { request.WorkerPath, "process", "watchdog", request.OutputDirectory + "/watchdog.json" }) + '\0', State = "EXITED" }
            ]
        };
        void RejectReceipt(Action<ExecutionResult> change)
        {
            var actual = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(validReceipt, ExecutionJsonContext.Default.ExecutionResult),
                ExecutionJsonContext.Default.ExecutionResult) ?? throw new InvalidDataException("Offline typed receipt fixture missing.");
            change(actual); Reject(() => RemoteDispatch.ValidateReceipt(request, actual, receiptPath, sha, budget));
        }
        Check("remote receipt correct identity accepted and schema/platform mismatch rejected", () =>
        {
            RemoteDispatch.ValidateReceipt(request, validReceipt, receiptPath, sha, budget);
            RejectReceipt(r => r.SchemaVersion = 2); RejectReceipt(r => r.Platform = "windows");
        });
        Check("remote receipt working directory remains request bound", () => RejectReceipt(r => r.WorkingDirectory = request.Root + "/other"));
        Check("remote receipt process count respects request scope", () => RejectReceipt(r => r.Processes = Enumerable.Repeat(r.Processes[0], request.MaximumProcesses + 1).ToArray()));
        Check("remote receipt duplicate process PID rejected", () => RejectReceipt(r => r.Processes[1].Pid = r.Processes[0].Pid));
        Check("remote receipt root must exist in identity inventory", () => RejectReceipt(r => r.RootPid = 999));
        Check("remote receipt launcher requires complete birth and exact fixed worker argv", () =>
        {
            RejectReceipt(r => r.Launcher!.StartTicks = null); RejectReceipt(r => r.Launcher!.CommandLine = "unknown-worker");
        });
        Check("remote receipt process missing birth and UNKNOWN observation rejected", () =>
        {
            RejectReceipt(r => r.Processes[0].BootId = null); RejectReceipt(r => r.Processes[1].State = "UNKNOWN_PENDING_RETAINED_EXIT");
            RejectReceipt(r => r.Processes[1].PendingObservationError = "Unknown");
        });
        Check("remote receipt root parent and declared payload argv remain bound", () =>
        {
            RejectReceipt(r => r.Processes[0].ParentPid = 99); RejectReceipt(r => r.Processes[0].ExpectedExecCommandLine = "other-payload");
        });
        File.WriteAllLines(Path.Combine(path, "checks.txt"), names.Append("PASS_offline_contract_checks_only; SSH/SFTP actual loopback integration NOT_RUN; no process/network/credential reads"));
        return checks > 0 ? 0 : 1;
    }
}
