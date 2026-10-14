using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class ExecutionChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        var fixture = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_FIXTURE") ?? throw new InvalidOperationException("SEZIKA_PROCESS_FIXTURE must name the separately built native C# fixture apphost.");
        var broker = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_BROKER");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(70));
        var clock = Stopwatch.StartNew(); var index = 0;
        ExecutionRequest Request(string mode) => new()
        {
            Executable = fixture, ArgumentList = [mode], WorkingDirectory = Path.GetDirectoryName(fixture)!,
            OutputDirectory = Path.Combine(root, "execution-" + (++index).ToString("D2") + "-" + mode),
            TimeoutSeconds = 3, CleanupSeconds = 5, BrokerExecutable = broker, MaximumOutputBytes = 65536
        };
        ExecutionResult Execute(ExecutionRequest request, CancellationToken? token = null)
        {
            if (clock.Elapsed.TotalSeconds >= 68) throw new TimeoutException("Execution checks suite deadline70s.");
            var actualToken = token ?? cancellation.Token;
            return ProcessExecutor.ExecuteAsync(request, new Budget(10, null, actualToken), actualToken).GetAwaiter().GetResult();
        }
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Clean(ExecutionResult result)
        {
            Assert(result.CleanupStatus == "PASS" && result.RemainingProcesses == 0, JsonSerializer.Serialize(result, ExecutionJsonContext.Default.ExecutionResult));
            foreach (var identity in result.Processes)
            {
                var actual = ProcessObserver.Observe(identity.Pid);
                var reused = actual.State == "Present" && (identity.Platform == "windows"
                    ? actual.Started is not null && identity.Started is not null && actual.Started != identity.Started
                    : actual.StartTicks is not null && identity.StartTicks is not null && (actual.StartTicks != identity.StartTicks || actual.BootId != identity.BootId));
                Assert(actual.State == "Absent" || reused, "Recorded task process survives or absence is UNKNOWN.");
            }
        }
        // Tiny real process first; run this check alone before the full suite.
        check("execution actual success and immutable receipt", () =>
        {
            var result = Execute(Request("success")); Clean(result);
            Assert(result.Status == "PASS" && result.ExitCode == 0 && result.Processes.Count(p => p.Ownership != "linux-watchdog") == 1 && result.Processes[0].ParentChain.SequenceEqual(new[] { Environment.ProcessId }), "Success identity/result differs.");
            Assert(File.ReadAllText(Path.Combine(result.OutputDirectory, "stdout.bin")).Contains("fixture-success", StringComparison.Ordinal), "stdout missing.");
            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(result.OutputDirectory, "result.json")));
            Assert(json.RootElement.GetProperty("Status").GetString() == "PASS", "Execution receipt differs.");
        });
        void NativeGitBroker(int repetitions, string name)
        {
            if (broker is null) throw new InvalidOperationException("Explicit native C# broker required.");
            var git = Environment.GetEnvironmentVariable("SEZIKA_GIT_EXECUTABLE") ?? "/usr/bin/git";
            var pressure = Stopwatch.StartNew();
            using var pressureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            pressureCancellation.CancelAfter(TimeSpan.FromSeconds(16));
            for (var repetition = 0; repetition < repetitions && pressure.Elapsed.TotalSeconds < 16; repetition++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var request = Request("fast-git-" + name); request.Executable = git;
                request.WorkingDirectory = root; request.ArgumentList = ["--version"]; request.MaximumProcesses = 8;
                var result = Execute(request, pressureCancellation.Token); Clean(result);
                Assert(result.Status == "PASS" && result.OutputComplete && result.InputComplete, string.Join(';', result.Errors));
                Assert(result.ExitCode == 0 &&
                    File.ReadAllText(Path.Combine(result.OutputDirectory, "stdout.bin")).StartsWith("git version ", StringComparison.Ordinal),
                    "Actual fast native Git execution did not complete with EOF and cleanup.");
                var expected = git + '\0' + "--version" + '\0';
                var native = result.Processes.SingleOrDefault(identity => identity.Pid == result.RootPid);
                Assert(native is not null && native.ExpectedExecCommandLine == expected && native.ParentPid == Environment.ProcessId &&
                    native.ParentChain.SequenceEqual(new[] { Environment.ProcessId }) && native.StartTicks is not null &&
                    !string.IsNullOrEmpty(native.BootId) && native.CommandLine.Length > 0 && native.ScopeId == result.OwnershipId &&
                    native.PendingObservationError is null, "Fast Git lacks independently registered native birth/parent/argv/scope evidence.");
                Assert(result.Processes.Count(identity => identity.CommandLine.Contains("\0process\0watchdog\0", StringComparison.Ordinal)) == 1 &&
                    result.Processes.All(identity => identity.StartTicks is not null && !string.IsNullOrEmpty(identity.BootId) &&
                        identity.CommandLine.Length > 0 && identity.ScopeId is not null && identity.ParentChain.Length > 0),
                    "Actual Git watchdog birth, argv, parent or ownership evidence is incomplete.");
                var watchdog = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(result.OutputDirectory, "watchdog-result.json")), ExecutionJsonContext.Default.WatchdogResult);
                Assert(watchdog?.Status == "TERMINATED_PENDING_PARENT_REAP" && watchdog.RemainingLive == 0 && watchdog.Errors.Length == 0,
                    "Actual fast Git watchdog did not prove bounded termination.");
                var audit = ProcessAudit.Run([result.OutputDirectory], new Budget(3, null, pressureCancellation.Token));
                Assert(audit.Status == "PASS_current_recorded_task_identities_absent_or_reused" &&
                    audit.NativeReceipts is [{ OriginalStatus: "PASS" }],
                    "Actual fast Git immutable native registrations, final identity or current absence audit failed.");
                Console.WriteLine($"execution actual fast Git broker/watchdog {name} {repetition + 1}/{repetitions}");
            }
            Assert(pressure.Elapsed.TotalSeconds < 16, "Fast Git broker/watchdog pressure shared16s deadline exceeded.");
        }
        if (OperatingSystem.IsLinux())
        {
            check("execution actual fast native Git broker and watchdog two-case sample", () => NativeGitBroker(2, "small"));
            check("execution actual fast native Git broker and watchdog eight-case pressure", () => NativeGitBroker(8, "pressure"));
            check("execution three native observers admit actual nested fast Git and watchdog", () =>
            {
                if (broker is null) throw new InvalidOperationException("Explicit native C# broker required.");
                var git = Environment.GetEnvironmentVariable("SEZIKA_GIT_EXECUTABLE") ?? "/usr/bin/git";
                var outer = Request("three-observers"); outer.Executable = broker; outer.TimeoutSeconds = 8; outer.MaximumProcesses = 16;
                var inner = new ExecutionRequest { Executable = git, ArgumentList = ["--version"], WorkingDirectory = root,
                    OutputDirectory = outer.OutputDirectory + "-git", TimeoutSeconds = 3, CleanupSeconds = 5, MaximumProcesses = 8, BrokerExecutable = broker };
                var configuration = outer.OutputDirectory + "-git.json";
                File.WriteAllBytes(configuration, JsonSerializer.SerializeToUtf8Bytes(inner, ExecutionJsonContext.Default.ExecutionRequest));
                outer.ArgumentList = ["process", "run", "--request", configuration, "--output", inner.OutputDirectory + ".cli.json"];
                var result = Execute(outer); Clean(result);
                Assert(result.Status == "PASS", string.Join(';', result.Errors));
                var nested = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(inner.OutputDirectory, "result.json")), ExecutionJsonContext.Default.ExecutionResult)
                    ?? throw new IOException("Missing actual three-observer Git receipt.");
                Clean(nested);
                Assert(nested.Status == "PASS" && nested.ExitCode == 0 && File.ReadAllText(Path.Combine(inner.OutputDirectory, "stdout.bin")).StartsWith("git version ", StringComparison.Ordinal),
                    "Three-observer actual Git payload failed.");
                Assert(result.Processes.Any(identity => identity.Pid == nested.RootPid && identity.ParentPid == result.RootPid &&
                    identity.StartTicks is not null && identity.CommandLine.Length > 0 && identity.ScopeId == nested.OwnershipId),
                    "Three-observer Git lacks independent caller native identity.");
                var audit = ProcessAudit.Run([result.OutputDirectory, nested.OutputDirectory], new Budget(5, null, cancellation.Token));
                Assert(audit.Status == "PASS_current_recorded_task_identities_absent_or_reused", "Three-observer immutable native audit failed.");
            });
            check("execution missing declared ancestor ACK rejects actual native broker before payload", () =>
            {
                foreach (var malformed in new AdmissionObserver[][] { null!, [null!], [new()] })
                {
                    var invalid = Request("success"); invalid.LinuxObservers = malformed;
                    var refused = Execute(invalid);
                    Assert(refused.Status == "FAIL" && refused.RootPid is null && refused.Processes.Length == 0,
                        "Null or incomplete Linux observer declaration started a native payload.");
                }
                var inherited = AncestorAdmission.Read(); Assert(inherited.Length > 0, "Actual outer native observer required for this check.");
                var directory = Path.Combine(inherited[0].OutputDirectory, "missing-ack-observer");
                Assert(!Path.Exists(directory), "Foreign missing-ACK fixture directory preserved."); Directory.CreateDirectory(directory);
                var scope = Guid.NewGuid().ToString("N");
                using (var marker = new FileStream(Path.Combine(directory, "ownership.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    marker.Write(Encoding.UTF8.GetBytes(scope));
                var actual = ProcessObserver.Observe(Environment.ProcessId);
                var caller = new ExecutionIdentity { Pid = actual.Pid, Platform = "linux", ParentPid = actual.ParentPid, StartTicks = actual.StartTicks,
                    BootId = actual.BootId, CommandLine = actual.CommandLine!, Ownership = "launcher" };
                var observer = AncestorAdmission.Declare(directory, scope, caller, new Budget(5, null, cancellation.Token));
                var request = Request("success"); request.LinuxObservers = [observer];
                var result = Execute(request); Clean(result);
                var stderr = File.ReadAllText(Path.Combine(result.OutputDirectory, "stderr.bin"));
                Assert(result.Status == "FAIL" && result.ExitCode != 0 && result.StandardOutputBytes == 0 &&
                    stderr.Contains("stage=broker", StringComparison.Ordinal) && stderr.Contains("missing_observers=" + Environment.ProcessId + "/" + scope, StringComparison.Ordinal),
                    "An actual broker executed payload or lost exact missing ancestor failure evidence.");
                Assert(result.Processes.All(identity => identity.AfterExecCommandLine is null), "Missing ACK broker executed its declared payload.");
            });
        }
        check("execution quotes spaces Unicode and literal shell syntax argv", () =>
        {
            var request = Request("success"); request.ArgumentList = ["success", "a b", "quote\"slash\\", "中文", "$HOME $(literal) ; | &&"];
            var result = Execute(request); Clean(result);
            Assert(result.Status == "PASS", string.Join(';', result.Errors));
            Assert(result.ArgumentList.SequenceEqual(request.ArgumentList), "Declared argv changed.");
            if (OperatingSystem.IsWindows()) Assert(result.Processes[0].CommandLine.Contains("中文", StringComparison.Ordinal), "Native Windows root argv lost Unicode.");
            var text = File.ReadAllText(Path.Combine(result.OutputDirectory, "stdout.bin"));
            foreach (var arg in request.ArgumentList.Skip(1)) Assert(text.Contains(arg, StringComparison.Ordinal), "Structured argv changed: " + arg);
        });
        check("execution nonzero failure preserved", () =>
        {
            var result = Execute(Request("failure")); Clean(result);
            Assert(result.Status == "FAIL" && result.ExitCode == 7 && result.ExecutionStatus == "FAILED", "Original payload failure lost.");
        });
        check("execution hard timeout and tree cleanup", () =>
        {
            var request = Request("sleep"); request.TimeoutSeconds = 1;
            var result = Execute(request); Clean(result);
            Assert(result.ExecutionStatus == "TIMED_OUT" && result.Status == "FAIL" && result.ElapsedSeconds < 7, "Hard timeout result differs.");
        });
        check("execution parent exits surviving child reclaimed", () =>
        {
            var result = Execute(Request("tree")); Clean(result);
            var payloads = result.Processes.Where(p => p.Ownership != "linux-watchdog").ToArray();
            Assert(result.Status == "PASS" && payloads.Length == 2 && payloads[1].ParentChain.Length == 2, string.Join(';', result.Errors));
        });
        if (OperatingSystem.IsLinux()) check("execution Linux escaped process group reclaimed by pidfd", () =>
        {
            var result = Execute(Request("escape-tree")); Clean(result);
            Assert(result.Status == "PASS" && result.Processes.Count(p => p.Ownership != "linux-watchdog") == 2, string.Join(';', result.Errors));
        });
        check("execution cancellation token cleanup", () =>
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
            var result = Execute(Request("sleep"), stop.Token); Clean(result);
            Assert(result.ExecutionStatus == "CANCELLED" && result.Status == "FAIL", "Cancellation token result differs.");
        });
        check("execution observed CANCEL file cleanup", () =>
        {
            var request = Request("sleep"); request.CancelFile = Path.Combine(root, "execution-CANCEL");
            var signal = Task.Run(async () => { await Task.Delay(450, cancellation.Token); File.WriteAllText(request.CancelFile, "cancel"); }, cancellation.Token);
            var result = Execute(request); signal.GetAwaiter().GetResult(); Clean(result);
            Assert(result.ExecutionStatus == "CANCELLED", "CANCEL file result differs.");
        });
        check("execution observed STOP file cleanup", () =>
        {
            var request = Request("sleep"); request.StopFile = Path.Combine(root, "execution-STOP");
            var signal = Task.Run(async () => { await Task.Delay(450, cancellation.Token); File.WriteAllText(request.StopFile, "stop"); }, cancellation.Token);
            var result = Execute(request); signal.GetAwaiter().GetResult(); Clean(result);
            Assert(result.ExecutionStatus == "CANCELLED", "STOP file result differs.");
        });
        check("execution stdout byte limit actual overflow", () =>
        {
            var request = Request("output"); request.MaximumOutputBytes = 1024;
            var result = Execute(request); Clean(result);
            Assert(result.Status == "FAIL" && result.OutputLimitExceeded && new FileInfo(Path.Combine(result.OutputDirectory, "stdout.bin")).Length == 1024, "stdout limit was not enforced.");
        });
        check("execution stderr byte limit actual overflow", () =>
        {
            var request = Request("stderr-output"); request.MaximumOutputBytes = 1024;
            var result = Execute(request); Clean(result);
            Assert(result.Status == "FAIL" && result.OutputLimitExceeded && new FileInfo(Path.Combine(result.OutputDirectory, "stderr.bin")).Length == 1024, "stderr limit was not enforced.");
        });
        check("execution real standard input and EOF", () =>
        {
            var request = Request("input"); request.StandardInput = Encoding.UTF8.GetBytes("stdin 中文 fixture");
            var result = Execute(request); Clean(result);
            Assert(result.Status == "PASS" && result.InputComplete && File.ReadAllBytes(Path.Combine(result.OutputDirectory, "stdout.bin")).SequenceEqual(request.StandardInput), "stdin bytes differ.");
        });
        check("execution oversized stdin rejected before launch", () =>
        {
            var request = Request("success"); request.StandardInput = new byte[2]; request.MaximumInputBytes = 1;
            var result = Execute(request);
            Assert(result.Status == "FAIL" && result.Processes.Length == 0 && !Directory.Exists(request.OutputDirectory), "Oversized input started a process.");
        });
        check("execution interpreter and shebang rejected", () =>
        {
            var request = Request("success");
            request.Executable = OperatingSystem.IsWindows() ? @"C:\Program Files\PowerShell\7\pwsh.exe" : "/bin/bash";
            var result = Execute(request); Assert(result.Status == "FAIL" && result.Processes.Length == 0, "Interpreter accepted.");
            var script = Path.Combine(root, "execution-script"); File.WriteAllText(script, "#!/bin/sh\nexit 0\n");
            request = Request("success"); request.Executable = script;
            result = Execute(request); Assert(result.Status == "FAIL" && result.Processes.Length == 0, "Script accepted.");
        });
        check("execution existing evidence preserved", () =>
        {
            var request = Request("success"); Directory.CreateDirectory(request.OutputDirectory);
            var receipt = Path.Combine(request.OutputDirectory, "result.json"); File.WriteAllText(receipt, "FAIL prior evidence");
            var result = Execute(request);
            Assert(result.Status == "FAIL" && File.ReadAllText(receipt) == "FAIL prior evidence", "Prior failure receipt overwritten.");
        });
        foreach (var mode in new[] { "tree", "late-tree" }) check("execution supervisor dies and " + mode + " payload reclaimed", () =>
        {
            if (broker is null) throw new InvalidOperationException("SEZIKA_PROCESS_BROKER is required for nested supervision checks.");
            var inner = Request(mode); inner.TimeoutSeconds = 6;
            var configuration = Path.Combine(root, "supervisor-" + mode + ".json");
            File.WriteAllBytes(configuration, JsonSerializer.SerializeToUtf8Bytes(inner, ExecutionJsonContext.Default.ExecutionRequest));
            var request = Request("supervisor"); request.TimeoutSeconds = 8; request.ArgumentList = ["supervisor", broker, configuration];
            if (OperatingSystem.IsLinux())
            {
                request.Environment["SEZIKA_FIXTURE_OUTER_RECEIPTS"] = request.OutputDirectory;
                using var executable = File.OpenRead(broker);
                request.NestedBrokers = [new() { Executable = broker, Sha256 = new Budget(10, null, cancellation.Token).Hash(executable, executable.Length) }];
            }
            var result = Execute(request); Clean(result);
            Assert(result.Status == "PASS" && File.ReadAllText(Path.Combine(result.OutputDirectory, "stdout.bin")).Contains("fixture-supervisor-exit-terminated", StringComparison.Ordinal), string.Join(';', result.Errors));
        });
        if (OperatingSystem.IsLinux()) check("execution explicit nested broker wrong SHA rejects actual native admission", () =>
        {
            if (broker is null) throw new InvalidOperationException("Explicit native C# broker required.");
            var inner = Request("tree"); inner.TimeoutSeconds = 6;
            var configuration = Path.Combine(root, "supervisor-wrong-broker-pin.json");
            File.WriteAllBytes(configuration, JsonSerializer.SerializeToUtf8Bytes(inner, ExecutionJsonContext.Default.ExecutionRequest));
            var request = Request("supervisor"); request.TimeoutSeconds = 8; request.ArgumentList = ["supervisor", broker, configuration];
            request.Environment["SEZIKA_FIXTURE_OUTER_RECEIPTS"] = request.OutputDirectory;
            request.NestedBrokers = [new() { Executable = broker, Sha256 = new string('0', 64) }];
            var result = Execute(request); Clean(result);
            Assert(result.Status == "FAIL" && result.Errors.Any(error => error.Contains("Nested broker complete native-image SHA differs", StringComparison.Ordinal)),
                "An actual nested native broker was accepted without its declared byte identity.");
        });
        if (OperatingSystem.IsWindows()) check("execution explicit hidden console fully registered", () =>
        {
            var request = Request("success"); request.WindowsConsoleMode = "hidden";
            var result = Execute(request); Clean(result);
            Assert(result.Status == "PASS" && result.Processes.Count(p => p.Pid == result.RootPid) == 1 &&
                result.Processes.All(p => p.Platform == "windows" && p.Started is not null && p.CommandLine.Length > 0), string.Join(';', result.Errors));
            Assert(result.Processes.Where(p => p.Pid != result.RootPid).All(p => p.Ownership == "windows-job-console-host"), "Unexpected hidden-console child image.");
        });
        if (OperatingSystem.IsWindows()) check("execution two Windows Jobs launch and reclaim independently", () =>
        {
            using var firstCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            using var secondCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            var first = Request("marked-sleep"); var second = Request("marked-sleep");
            var firstMarker = Path.Combine(root, "job-first.ready"); var secondMarker = Path.Combine(root, "job-second.ready");
            first.ArgumentList = ["marked-sleep", firstMarker]; second.ArgumentList = ["marked-sleep", secondMarker];
            first.TimeoutSeconds = second.TimeoutSeconds = 6;
            var firstRun = ProcessExecutor.ExecuteAsync(first, new Budget(10, null, firstCancel.Token), firstCancel.Token);
            var secondRun = ProcessExecutor.ExecuteAsync(second, new Budget(10, null, secondCancel.Token), secondCancel.Token);
            try
            {
                var launch = Stopwatch.StartNew();
                // <=100 polls and <=2 seconds; markers publish after actual launch.
                for (var poll = 0; poll < 100 && launch.Elapsed.TotalSeconds < 2 && !(File.Exists(firstMarker) && File.Exists(secondMarker)); poll++)
                { cancellation.Token.ThrowIfCancellationRequested(); Thread.Sleep(20); }
                Assert(File.Exists(firstMarker) && File.Exists(secondMarker), "Two independent Jobs did not launch within2s.");
                var firstPid = int.Parse(File.ReadAllText(firstMarker), System.Globalization.CultureInfo.InvariantCulture);
                var secondPid = int.Parse(File.ReadAllText(secondMarker), System.Globalization.CultureInfo.InvariantCulture);
                var secondRegistered = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(second.OutputDirectory, "registered.json")), ExecutionJsonContext.Default.ExecutionIdentityArray)!.Single();
                Assert(firstPid != secondPid && secondRegistered.Pid == secondPid, "Concurrent fixture PID/registration differs.");
                firstCancel.Cancel(); var firstResult = firstRun.GetAwaiter().GetResult(); Clean(firstResult);
                Assert(firstResult.RootPid == firstPid && firstResult.ExecutionStatus == "CANCELLED", "First Job did not reclaim its own root.");
                var stillLive = ProcessObserver.Observe(secondPid);
                Assert(!secondRun.IsCompleted && stillLive.State == "Present" && stillLive.Started == secondRegistered.Started && stillLive.CommandLine == secondRegistered.CommandLine,
                    "First Job cleanup altered the second Job or its birth/argv became UNKNOWN.");
                secondCancel.Cancel(); var secondResult = secondRun.GetAwaiter().GetResult(); Clean(secondResult);
                Assert(secondResult.RootPid == secondPid && secondResult.ExecutionStatus == "CANCELLED" && secondResult.OwnershipId != firstResult.OwnershipId,
                    "Second Job root or independent ownership differs.");
            }
            finally
            {
                firstCancel.Cancel(); secondCancel.Cancel();
                Task.WhenAll(firstRun, secondRun).GetAwaiter().GetResult();
            }
        });
        if (OperatingSystem.IsWindows()) check("execution Job notifications observe eight actual fast native children", () =>
        {
            var request = Request("fast-tree"); request.TimeoutSeconds = 5; request.MaximumProcesses = 16;
            var result = Execute(request); Clean(result);
            Assert(result.Status == "PASS", string.Join(';', result.Errors));
            var children = File.ReadAllLines(Path.Combine(request.OutputDirectory, "stdout.bin"))
                .Where(line => line.StartsWith("fixture-fast-child=", StringComparison.Ordinal))
                .Select(line => int.Parse(line["fixture-fast-child=".Length..], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Assert(children.Length == 8 && children.Distinct().Count() == 8, "Eight actual fixture child launches were not reported.");
            Assert(result.Processes.Length == 9 && children.All(pid => result.Processes.Any(identity => identity.Pid == pid && identity.ParentPid == result.RootPid &&
                identity.Started is not null && identity.CommandLine.Contains("success", StringComparison.Ordinal) && identity.ParentChain.Length == 2)),
                "A real fast child lacks complete independent outer native birth/argv/parent evidence.");
        });
    }
}
