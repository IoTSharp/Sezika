using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Sezika.Tooling;

internal sealed record WindowsLifecyclePin(string Path, long Bytes, string Sha256);
internal sealed record WindowsLifecyclePid(int Pid, DateTimeOffset? Started, string Ownership, string State,
    string HandleReadback, uint? WaitResult, int? NativeError, string? ExitProof);
internal sealed record WindowsLifecycleCase(string Name, string ConsoleMode, string Status,
    WindowsLifecyclePin? Request = null, WindowsLifecyclePin? NativeReceipt = null, WindowsLifecyclePin? Audit = null,
    WindowsLifecyclePid[]? Pids = null, int ConsoleHosts = 0, string? Failure = null);
internal sealed record WindowsLifecycleReport(string Status, int Planned, int Completed, int Passed, int Failed,
    int Unprocessed, WindowsLifecyclePin Git, string Repository, WindowsLifecycleCase[] Cases,
    bool SyntheticTransport = false, bool FixtureOnly = true, bool NativeAotVerified = false,
    string Scope = "Actual bounded read-only Windows Git lifecycle, retained-birth HANDLE readback and native audit only");

[JsonSourceGenerationOptions(WriteIndented = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(WindowsLifecycleReport))]
internal partial class WindowsLifecycleJson : JsonSerializerContext;

internal static class WindowsLifecycleChecks
{
    // Root wires this separately: fresh tiny maximum1, then fresh full maximum8.
    internal static void Run(string root, string repositoryDirectory, int maximumCases, Action<string, Action> check,
        CancellationToken cancellation = default, string? cancelFile = null)
    {
        Assert(OperatingSystem.IsWindows(), "Windows lifecycle checks require real Windows.");
        Assert(maximumCases is 1 or 8, "Windows lifecycle profiles are exactly1 or8 cases.");
        const string git = @"C:\Program Files\Git\cmd\git.exe";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var seconds = maximumCases == 1 ? 45 : 240; deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        var budget = new Budget(seconds, cancelFile, deadline.Token);
        Assert(Path.IsPathFullyQualified(root) && Directory.Exists(root) && Path.IsPathFullyQualified(repositoryDirectory) &&
            Directory.Exists(repositoryDirectory), "Explicit existing case parent and repository directories required.");
        SafePaths.NoLinks(root, budget); SafePaths.NoLinks(repositoryDirectory, budget);
        var reportPath = Path.Combine(root, "windows-lifecycle-report.json");
        Assert(!Path.Exists(reportPath), "Preserve previous lifecycle report; use a fresh run root.");
        var gitPin = Pin(git, budget);
        var rows = Enumerable.Range(0, maximumCases).Select(index => new WindowsLifecycleCase(
            "git-" + (index % 2 == 0 ? "rev-parse" : "version") + "-" + (index + 1).ToString("D2"),
            index % 4 < 2 ? "detached" : "hidden", "UNPROCESSED")).ToArray();
        try
        {
            for (var index = 0; index < maximumCases && index < 8; index++)
            {
                budget.Check(); var current = index;
                check("Windows actual Git lifecycle " + rows[current].Name + " " + rows[current].ConsoleMode, () =>
                {
                    try
                    {
                        budget.Check(); Assert(Pin(git, budget) == gitPin, "Fixed Git executable bytes changed.");
                        var directory = SafePaths.Within(root, rows[current].Name, budget);
                        Assert(!Path.Exists(directory), "Lifecycle case directory must be fresh."); Directory.CreateDirectory(directory);
                        var request = new ExecutionRequest
                        {
                            Executable = git, ArgumentList = current % 2 == 0 ? ["rev-parse", "HEAD"] : ["--version"],
                            WorkingDirectory = repositoryDirectory, OutputDirectory = Path.Combine(directory, "execution"),
                            TimeoutSeconds = 10, CleanupSeconds = 10, MaximumProcesses = 8, MaximumInputBytes = 0,
                            MaximumOutputBytes = 65536, WindowsConsoleMode = rows[current].ConsoleMode, CancelFile = cancelFile
                        };
                        var requestPath = Path.Combine(directory, "request.json");
                        CorpusJson.Write(requestPath, request, ExecutionJsonContext.Default.ExecutionRequest, budget);
                        rows[current] = rows[current] with { Status = "FAIL_incomplete", Request = Pin(requestPath, budget) };
                        var result = ProcessExecutor.ExecuteAsync(request, budget, deadline.Token).GetAwaiter().GetResult();
                        var nativePin = Pin(Path.Combine(request.OutputDirectory, "result.json"), budget);
                        rows[current] = rows[current] with { NativeReceipt = nativePin };
                        Assert(result.Status == "PASS" && result.ExecutionStatus == "SUCCEEDED" && result.ExitCode == 0 &&
                            result.CleanupStatus == "PASS" && result.RemainingProcesses == 0 && result.Errors.Length == 0 &&
                            result.InputComplete && result.OutputComplete && !result.OutputLimitExceeded && result.StandardInputBytes == 0,
                            "Actual Git exit/EOF/cleanup failed: " + string.Join(';', result.Errors));
                        Assert(result.Executable == git && result.ArgumentList.SequenceEqual(request.ArgumentList) &&
                            result.Launcher?.Pid == Environment.ProcessId && result.RootPid is > 0 &&
                            Guid.TryParseExact(result.OwnershipId, "N", out _) && result.Processes.Length is > 0 and <= 8,
                            "Actual native request/root/launcher/scope inventory differs.");
                        var stdout = Path.Combine(request.OutputDirectory, "stdout.bin"); var stderr = Path.Combine(request.OutputDirectory, "stderr.bin");
                        Assert(new FileInfo(stdout).Length == result.StandardOutputBytes && new FileInfo(stderr).Length == result.StandardErrorBytes &&
                            result.StandardOutputBytes is > 0 and <= 4096 && result.StandardErrorBytes == 0, "Git output byte/EOF identity differs.");
                        var text = File.ReadAllText(stdout, CorpusJson.Utf8).TrimEnd('\r', '\n');
                        Assert(current % 2 == 0 ? text.Length == 40 && text.All(Uri.IsHexDigit) : text.StartsWith("git version ", StringComparison.Ordinal),
                            "Actual read-only Git stdout is not a commit or version.");
                        var rootIdentity = result.Processes.Single(identity => identity.Pid == result.RootPid);
                        Assert(rootIdentity.ParentPid == Environment.ProcessId && rootIdentity.ParentChain.SequenceEqual([Environment.ProcessId]) &&
                            rootIdentity.CommandLine == WindowsExecutionSession.Quote(git) + " " + string.Join(' ', request.ArgumentList.Select(WindowsExecutionSession.Quote)),
                            "Root original native argv/parent chain differs.");
                        var proofs = new List<WindowsLifecyclePid>(result.Processes.Length);
                        for (var pidIndex = 0; pidIndex < result.Processes.Length && pidIndex < 8; pidIndex++)
                        {
                            budget.Check(); var identity = result.Processes[pidIndex];
                            Assert(identity.Pid > 0 && identity.Started is not null && identity.Platform == "windows" && identity.CommandLine.Length > 0 &&
                                identity.ParentPid > 0 && identity.ParentChain.Length is > 0 and <= 8 && identity.ParentChain[^1] == identity.ParentPid &&
                                identity.ParentChain[0] == Environment.ProcessId && identity.ParentChain.All(pid => pid > 0 && pid != identity.Pid) &&
                                identity.ParentChain.Distinct().Count() == identity.ParentChain.Length && identity.ScopeId == result.OwnershipId &&
                                (identity.Ownership is "windows-job" or "windows-job-console-host") && identity.PendingObservationError is null &&
                                (identity.State is "EXITED" or "EXITED_RETAINED_HANDLE"), "Incomplete native task birth/argv/chain or unresolved member remains.");
                            proofs.Add(ReadHandle(identity, budget));
                        }
                        rows[current] = rows[current] with { Pids = proofs.ToArray(), ConsoleHosts = result.Processes.Count(identity => identity.Ownership == "windows-job-console-host") };
                        var audit = ProcessAudit.Run([request.OutputDirectory], new Budget(3, null, deadline.Token));
                        var auditPath = Path.Combine(directory, "native-audit.json"); CorpusJson.Write(auditPath, audit, ToolingJsonContext.Default.AuditReport, budget);
                        rows[current] = rows[current] with { Audit = Pin(auditPath, budget) };
                        Assert(audit.Status == "PASS_current_recorded_task_identities_absent_or_reused" &&
                            audit.MatchingLive == 0 && audit.Unknown == 0 && audit.IdentityRecords == result.Processes.Length &&
                            audit.NativeReceipts is [{ OriginalStatus: "PASS", ExecutionStatus: "SUCCEEDED", CleanupStatus: "PASS", ExitCode: 0, Errors: [] }] &&
                            Pin(nativePin.Path, budget) == nativePin && Pin(requestPath, budget) == rows[current].Request && Pin(git, budget) == gitPin,
                            "Independent immutable native receipt or current original-birth absence audit failed.");
                        rows[current] = rows[current] with { Status = "PASS" };
                        Console.WriteLine("Windows lifecycle " + (current + 1) + "/" + maximumCases + " PASS; pids=" + proofs.Count + "; consoleHosts=" + rows[current].ConsoleHosts);
                    }
                    catch (Exception error) { rows[current] = rows[current] with { Status = "FAIL", Failure = error.GetType().Name + ": " + error.Message }; throw; }
                });
                Assert(rows[current].Status == "PASS", "Lifecycle check callback was not completed.");
            }
        }
        finally
        {
            var passed = rows.Count(row => row.Status == "PASS"); var failed = rows.Count(row => row.Status == "FAIL");
            var completed = rows.Count(row => row.Status != "UNPROCESSED");
            CorpusJson.Write(reportPath, new WindowsLifecycleReport(passed == maximumCases ? "PASS_actual_Windows_Git_lifecycle" : "FAIL_OR_INCOMPLETE_Windows_Git_lifecycle",
                maximumCases, completed, passed, failed, maximumCases - completed, gitPin, repositoryDirectory, rows),
                WindowsLifecycleJson.Default.WindowsLifecycleReport, new Budget(5, null, CancellationToken.None));
        }
    }

    private static WindowsLifecyclePid ReadHandle(ExecutionIdentity identity, Budget budget)
    {
        budget.Check(); using var handle = ExecutionNative.OpenProcess(0x00101000, 0, (uint)identity.Pid);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError(); var observed = ProcessObserver.Observe(identity.Pid);
            Assert(error == 87 && observed.Pid == identity.Pid && observed.State == "Absent", "Original birth HANDLE unavailable without proven absence; error=" + error);
            return new(identity.Pid, identity.Started, identity.Ownership, identity.State, "PASS_original_birth_absent_no_reopened_HANDLE", null, error, identity.ExitProof);
        }
        Assert(ExecutionNative.GetProcessTimes(handle, out var created, out _, out _, out _) != 0, "Readback HANDLE birth unavailable.");
        var sameBirth = DateTimeOffset.FromFileTime(created).ToUniversalTime() == identity.Started;
        var wait = ExecutionNative.WaitForSingleObject(handle, 0);
        Assert((wait is 0 or 258) && (!sameBirth || wait == 0), "Original registered birth HANDLE is not signalled or native wait failed.");
        return new(identity.Pid, identity.Started, identity.Ownership, identity.State,
            sameBirth ? "PASS_same_birth_signalled_reopened_HANDLE" : "PASS_reused_different_birth_preserved", wait, null, identity.ExitProof);
    }
    private static WindowsLifecyclePin Pin(string path, Budget budget)
    {
        budget.Check(); var bytes = new FileInfo(path).Length; Assert(bytes is >= 0 and <= 16777216, "Lifecycle pin byte bound16MiB.");
        return new(Path.GetFullPath(path), bytes, CorpusJson.FileHash(path, budget));
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
