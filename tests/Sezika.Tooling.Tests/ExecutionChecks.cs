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
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var clock = Stopwatch.StartNew(); var index = 0;
        ExecutionRequest Request(string mode) => new()
        {
            Executable = fixture, ArgumentList = [mode], WorkingDirectory = Path.GetDirectoryName(fixture)!,
            OutputDirectory = Path.Combine(root, "execution-" + (++index).ToString("D2") + "-" + mode),
            TimeoutSeconds = 3, CleanupSeconds = 5, BrokerExecutable = broker, MaximumOutputBytes = 65536
        };
        ExecutionResult Execute(ExecutionRequest request, CancellationToken? token = null)
        {
            if (clock.Elapsed.TotalSeconds >= 33) throw new TimeoutException("Execution checks suite deadline35s.");
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
