using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Sezika.Tooling;

internal static class ProcessExecutor
{
    private static readonly SemaphoreSlim SerialLease = new(1, 1);
    private static readonly SemaphoreSlim WindowsLease = new(2, 2);
    private static readonly HashSet<string> Interpreters = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash", "sh", "dash", "zsh", "ksh", "pwsh", "powershell", "powershell_ise", "cmd", "python", "python3",
        "py", "perl", "ruby", "node", "cscript", "wscript", "mshta", "rscript", "lua",
        "env", "nohup", "setsid", "timeout", "busybox"
    };

    internal static async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, Budget budget, CancellationToken cancellation)
    {
        var result = new ExecutionResult
        {
            Platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unsupported",
            Executable = request.Executable, ArgumentList = request.ArgumentList.ToArray(), WorkingDirectory = request.WorkingDirectory,
            OutputDirectory = request.OutputDirectory, OwnershipId = Guid.NewGuid().ToString("N"), StartedUtc = DateTimeOffset.UtcNow
        };
        var errors = new List<string>(); var clock = Stopwatch.StartNew(); var acquired = false; var ownedOutput = false;
        // Independent Windows Jobs permit one bounded fixture server and one
        // client. Linux keeps its process-wide subreaper lifecycle serialized.
        var lease = OperatingSystem.IsWindows() ? WindowsLease : SerialLease;
        IExecutionSession? session = null; Task? input = null;
        using var inputCancellation = new CancellationTokenSource();
        FileStream? stdout = null; FileStream? stderr = null;
        var records = new Dictionary<int, ExecutionIdentity>();
        var pendingRegistrations = new Dictionary<int, string>();
        var snapshotCount = 0;
        void Snapshot()
        {
            if (!ownedOutput || records.Count == snapshotCount) return;
            var target = Path.Combine(request.OutputDirectory, "registered-" + records.Count.ToString("D3", CultureInfo.InvariantCulture) + ".json");
            WriteNew(target, JsonSerializer.SerializeToUtf8Bytes(records.Values.ToArray(), ExecutionJsonContext.Default.ExecutionIdentityArray));
            snapshotCount = records.Count;
        }
        var outputDone = false; var errorDone = false;
        try
        {
            Validate(request, budget);
            for (var attempt = 0; attempt < request.TimeoutSeconds * 10 + 1; attempt++)
            {
                Check(request, budget, cancellation, clock);
                if (await lease.WaitAsync(100, cancellation).ConfigureAwait(false)) { acquired = true; break; }
            }
            if (!acquired) throw new TimeoutException("Execution lease poll budget exhausted.");
            result.OutputDirectory = request.OutputDirectory = Path.GetFullPath(request.OutputDirectory);
            result.WorkingDirectory = request.WorkingDirectory = Path.GetFullPath(request.WorkingDirectory);
            // Claim before any process starts; preserve all failure evidence.
            if (Path.Exists(request.OutputDirectory)) throw new IOException("Execution output already exists.");
            Directory.CreateDirectory(request.OutputDirectory); ownedOutput = true;
            WriteNew(Path.Combine(request.OutputDirectory, "ownership.json"), Encoding.UTF8.GetBytes(result.OwnershipId));
            stdout = NewFile(Path.Combine(request.OutputDirectory, "stdout.bin"));
            stderr = NewFile(Path.Combine(request.OutputDirectory, "stderr.bin"));
            var launcher = ProcessObserver.Observe(Environment.ProcessId);
            result.Launcher = Identity(launcher, [], "launcher");
            var environment = BuildEnvironment(request, result.OwnershipId);
            var gate = Path.Combine(request.OutputDirectory, "start-gate");
            if (OperatingSystem.IsWindows()) session = new WindowsExecutionSession(request, environment);
            else if (OperatingSystem.IsLinux())
            {
                AncestorAdmission.Attach(request, environment, result.OwnershipId, result.Launcher!, budget);
                Directory.CreateDirectory(gate); environment["SEZIKA_START_GATE"] = gate;
                var config = Path.Combine(request.OutputDirectory, "broker.json");
                WriteNew(config, JsonSerializer.SerializeToUtf8Bytes(new BrokerConfiguration
                { Executable = request.Executable, ArgumentList = request.ArgumentList, OwnershipId = result.OwnershipId,
                    OutputDirectory = request.OutputDirectory, WatchdogExecutable = request.BrokerExecutable!, WatchdogArgumentPrefix = request.WatchdogArgumentPrefix,
                    ParentPid = Environment.ProcessId, MaximumSeconds = request.TimeoutSeconds, CleanupSeconds = request.CleanupSeconds,
                    MaximumProcesses = request.MaximumProcesses, Launcher = result.Launcher }, ExecutionJsonContext.Default.BrokerConfiguration));
                session = new LinuxExecutionSession(request, environment, result.OwnershipId, gate, config);
            }
            else throw new PlatformNotSupportedException("Execution supports Windows and Linux.");
            result.RootPid = session.Pid;
            var actual = ProcessObserver.Observe(session.Pid);
            var registration = Stopwatch.StartNew();
            // posix_spawn may return while /proc exposes the new mm before its
            // argv range is populated. The C# broker remains behind the gate;
            // require complete native metadata before any release.
            for (var attempt = 0; attempt < 100 && registration.Elapsed.TotalSeconds < 2 && !CompleteIdentity(actual); attempt++)
            {
                Check(request, budget, cancellation, clock);
                await Task.Delay(20, cancellation).ConfigureAwait(false);
                actual = ProcessObserver.Observe(session.Pid);
            }
            if (actual.ParentPid != Environment.ProcessId) throw new IOException("Root process parent identity differs.");
            var root = Identity(actual, [Environment.ProcessId], session.Ownership);
            root.ScopeId = result.OwnershipId;
            if (OperatingSystem.IsLinux()) root.ExpectedExecCommandLine = string.Join('\0', new[] { request.Executable }.Concat(request.ArgumentList)) + '\0';
            records.Add(root.Pid, root); session.Register(root);
            WriteNew(Path.Combine(request.OutputDirectory, "registered.json"), JsonSerializer.SerializeToUtf8Bytes(records.Values.ToArray(), ExecutionJsonContext.Default.ExecutionIdentityArray));
            snapshotCount = records.Count;
            if (session is LinuxExecutionSession initialLinux) AncestorAdmission.Publish(request.OutputDirectory, result.OwnershipId, records.Values, initialLinux, budget);
            Check(request, budget, cancellation, clock);
            session.Release();
            result.ExecutionStatus = "RUNNING";
            input = session.WriteInputAsync(request.StandardInput, inputCancellation.Token);
            var buffer = new byte[16384]; var nextObservation = 0.0; var nextProgress = 10.0;
            // <=864,001 polls AND <=86,400 seconds. A poll sleeps100ms; stream
            // reads additionally cap32 chunks each, with a byte cap checked first.
            for (var poll = 0; poll < request.TimeoutSeconds * 10 + 1; poll++)
            {
                Check(request, budget, cancellation, clock);
                Drain(session, false, stdout, buffer, request.MaximumOutputBytes, ref outputDone, result);
                Drain(session, true, stderr, buffer, request.MaximumOutputBytes, ref errorDone, result);
                if (input.IsFaulted) await input.ConfigureAwait(false);
                if (clock.Elapsed.TotalSeconds >= nextObservation)
                {
                    ObserveMembers(session, records, request, budget, errors, pendingRegistrations);
                    Snapshot();
                    if (session is LinuxExecutionSession linux)
                    { AncestorAdmission.Publish(request.OutputDirectory, result.OwnershipId, records.Values, linux, budget); linux.AcknowledgeWatchdog(records); }
                    nextObservation = clock.Elapsed.TotalSeconds + .2;
                }
                if (session.TryExit(out var code))
                {
                    ObserveMembers(session, records, request, budget, errors, pendingRegistrations);
                    Snapshot();
                    result.ExitCode = code;
                    result.ExecutionStatus = code == 0 ? "SUCCEEDED" : "FAILED";
                    if (code != 0) errors.Add("Process exit code " + code.ToString(CultureInfo.InvariantCulture));
                    break;
                }
                if (clock.Elapsed.TotalSeconds >= nextProgress)
                {
                    Console.Error.WriteLine($"execution pid={session.Pid} elapsed={clock.Elapsed.TotalSeconds:F1}s registered={records.Count}/{request.MaximumProcesses}");
                    nextProgress = clock.Elapsed.TotalSeconds + 10;
                }
                await Task.Delay(100, cancellation).ConfigureAwait(false);
            }
            if (result.ExecutionStatus == "RUNNING") throw new TimeoutException("Execution poll bound exhausted.");
        }
        catch (Exception error) when (Expected(error))
        {
            result.ExecutionStatus = error is TimeoutException ? "TIMED_OUT" : error is OperationCanceledException ? "CANCELLED" : "FAILED";
            errors.Add(error.GetType().Name + ": " + error.Message);
            if (error is ExecutionStartupException startup)
            {
                result.RootPid = startup.Pid > 0 ? startup.Pid : null;
                result.CleanupStatus = startup.Pid == 0 ? "NOT_NEEDED" : startup.CleanupProven ? "PASS" : "UNKNOWN";
            }
        }
        finally
        {
            if (session is not null)
            {
                result.CleanupStatus = "UNKNOWN";
                var cleanup = Stopwatch.StartNew(); var cleanupComplete = false;
                try
                {
                    // Cancellation ends payload work, but never skips the bounded
                    // ownership cleanup. One cleanup deadline covers all passes.
                    inputCancellation.Cancel();
                    var cleanupBudget = new Budget(request.CleanupSeconds, null, CancellationToken.None);
                    var buffer = new byte[16384];
                    for (var pass = 0; pass < request.CleanupSeconds * 20 + 1 && cleanup.Elapsed.TotalSeconds < request.CleanupSeconds; pass++)
                    {
                        cleanupBudget.Check();
                        ObserveMembers(session, records, request, cleanupBudget, errors, pendingRegistrations);
                        Snapshot();
                        if (session is LinuxExecutionSession linux)
                        { AncestorAdmission.Publish(request.OutputDirectory, result.OwnershipId, records.Values, linux, cleanupBudget); linux.AcknowledgeWatchdog(records); }
                        if (session is LinuxExecutionSession admissionCleanup && !admissionCleanup.PrepareTermination(records, cleanupBudget))
                        { await Task.Delay(20, CancellationToken.None).ConfigureAwait(false); continue; }
                        session.Terminate();
                        Drain(session, false, stdout!, buffer, request.MaximumOutputBytes, ref outputDone, result, false);
                        Drain(session, true, stderr!, buffer, request.MaximumOutputBytes, ref errorDone, result, false);
                        if (session.Reap()) { cleanupComplete = true; break; }
                        await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
                    }
                    result.RemainingProcesses = session.Members().Length;
                    if (cleanupComplete && result.RemainingProcesses == 0) result.CleanupStatus = "PASS";
                    else errors.Add("Final process inventory or reaping was not proven complete.");
                    // Drain buffered bytes even after all processes have exited.
                    for (var pass = 0; pass < request.CleanupSeconds * 20 + 1 && cleanup.Elapsed.TotalSeconds < request.CleanupSeconds && !(outputDone && errorDone); pass++)
                    {
                        Drain(session, false, stdout!, buffer, request.MaximumOutputBytes, ref outputDone, result, false);
                        Drain(session, true, stderr!, buffer, request.MaximumOutputBytes, ref errorDone, result, false);
                        if (!(outputDone && errorDone)) await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception error) when (Expected(error)) { errors.Add("Cleanup " + error.GetType().Name + ": " + error.Message); }
                finally
                {
                    // Dispose the Windows job even if observation/drain failed.
                    // Linux group/pidfd termination still runs after a bad scan.
                    try { session.Terminate(); } catch (Exception error) when (Expected(error)) { errors.Add("Final termination: " + error.Message); }
                    if (session is WindowsExecutionSession notifications)
                    {
                        try
                        {
                            notifications.StopNotifications();
                            ObserveMembers(session, records, request, new Budget(3, null, CancellationToken.None), errors, pendingRegistrations);
                        }
                        catch (Exception error) when (Expected(error)) { errors.Add("Final Job notification inventory: " + error.Message); }
                        errors.AddRange(notifications.NotificationErrors);
                    }
                    foreach (var pending in pendingRegistrations.OrderBy(pair => pair.Key))
                        errors.Add("UNKNOWN parent chain/argv for owned member " + pending.Key + "; " + pending.Value);
                    // A Windows process can clear PEB/argv before the retained
                    // HANDLE becomes signalled. Resolve only with its exact
                    // registered birth and actual exit, retaining the transient
                    // observation as a note. Unresolved cases remain failures.
                    foreach (var record in records.Values.Where(record => record.PendingObservationError is not null))
                    {
                        if (session is WindowsExecutionSession retained && retained.HasExitedBirth(record))
                        {
                            record.State = "EXITED_RETAINED_HANDLE";
                            record.ExitProof = "Registered creation time and signalled retained HANDLE resolve " + record.PendingObservationError;
                            record.PendingObservationError = null;
                        }
                        else if (session is LinuxExecutionSession retainedLinux && retainedLinux.HasExitedBirth(record))
                        {
                            record.State = "EXITED";
                            record.ExitProof = "Complete registered native birth/parent/argv and retained pidfd termination/reaping signal resolve " + record.PendingObservationError;
                            record.PendingObservationError = null;
                        }
                        else errors.Add("UNKNOWN member identity " + record.Pid + ": " + record.PendingObservationError);
                    }
                    if (input is not null)
                    {
                        try
                        {
                            var remaining = Math.Max(1, Math.Min(2000, (request.CleanupSeconds - cleanup.Elapsed.TotalSeconds) * 1000));
                            await input.WaitAsync(TimeSpan.FromMilliseconds(remaining), CancellationToken.None).ConfigureAwait(false);
                            result.StandardInputBytes = request.StandardInput.Length; result.InputComplete = true;
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception error) when (Expected(error)) { errors.Add("Standard input: " + error.Message); }
                        if (!input.IsCompleted) { result.CleanupStatus = "UNKNOWN"; errors.Add("Standard input writer remained blocked after bounded cleanup."); }
                    }
                    try { session.Dispose(); }
                    catch (Exception error) when (Expected(error)) { result.CleanupStatus = "UNKNOWN"; errors.Add("Session resource disposal: " + error.Message); }
                }
                result.OutputComplete = outputDone && errorDone;
                if (!result.OutputComplete) errors.Add("Output EOF was not verified within the cleanup budget.");
            }
            stdout?.Dispose(); stderr?.Dispose();
            result.ElapsedSeconds = clock.Elapsed.TotalSeconds;
            result.Processes = records.Values.ToArray(); result.Errors = errors.Distinct(StringComparer.Ordinal).ToArray();
            result.Status = result.CleanupStatus == "UNKNOWN" ? "UNKNOWN" :
                result.ExecutionStatus == "SUCCEEDED" && result.CleanupStatus == "PASS" && result.OutputComplete && result.InputComplete && result.Errors.Length == 0 ? "PASS" : "FAIL";
            try
            {
                if (ownedOutput)
                    WriteNew(Path.Combine(request.OutputDirectory, "result.json"), JsonSerializer.SerializeToUtf8Bytes(result, ExecutionJsonContext.Default.ExecutionResult));
            }
            finally { if (acquired) lease.Release(); }
        }
        return result;
    }

    private static void Validate(ExecutionRequest request, Budget budget)
    {
        budget.Check();
        ValidateExecutable(request.Executable, budget);
        if (!Path.IsPathFullyQualified(request.WorkingDirectory) || !Directory.Exists(request.WorkingDirectory) || !Path.IsPathFullyQualified(request.OutputDirectory))
            throw new ArgumentException("Explicit absolute working and new output directories are required.");
        SafePaths.NoLinks(request.WorkingDirectory, budget); SafePaths.NoLinks(request.OutputDirectory, budget);
        if (request.TimeoutSeconds is < 1 or > 86400 || request.CleanupSeconds is < 1 or > 30 || request.MaximumProcesses is < 1 or > 256 ||
            request.MaximumInputBytes is < 0 or > 16_777_216 || request.MaximumOutputBytes is < 1 or > 67_108_864 || request.StandardInput.Length > request.MaximumInputBytes)
            throw new ArgumentException("Execution time/process/input/output bounds are invalid.");
        if (request.WindowsConsoleMode is not ("detached" or "hidden") || (!OperatingSystem.IsWindows() && request.WindowsConsoleMode != "detached"))
            throw new ArgumentException("WindowsConsoleMode must be detached or hidden, with hidden used only on Windows.");
        ValidateArguments(request.ArgumentList);
        if (request.Environment.Count > 256) throw new ArgumentException("Environment overlay exceeds256 items.");
        foreach (var pair in request.Environment)
        {
            budget.Check();
            if (pair.Key.Length is < 1 or > 256 || pair.Key.Contains('=') || pair.Key.Contains('\0') || (pair.Value?.Length ?? 0) > 65536 || pair.Value?.Contains('\0') == true ||
                pair.Key is "SEZIKA_START_GATE" or "SEZIKA_EXECUTION_ID" or AncestorAdmission.EnvironmentName) throw new ArgumentException("Unsafe or reserved environment overlay.");
        }
        if (request.LinuxObservers is null || request.LinuxObservers.Length > 64 || (!OperatingSystem.IsLinux() && request.LinuxObservers.Length != 0))
            throw new ArgumentException("Linux observers require Linux and at most64 explicit declarations.");
        foreach (var path in new[] { request.CancelFile, request.StopFile })
            if (path is not null) { if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Absolute cancellation/STOP paths required."); SafePaths.NoLinks(path, budget); }
        if (request.NestedBrokers.Length > 4) throw new ArgumentException("Nested broker allowlist bound4.");
        var nestedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pin in request.NestedBrokers)
        {
            budget.Check();
            if (!Path.IsPathFullyQualified(pin.Executable) || pin.Executable.Length > 4096 || !nestedPaths.Add(pin.Executable) ||
                pin.Sha256.Length != 64 || pin.Sha256.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)) ||
                pin.ArgumentPrefix.Length is < 1 or > 8 || pin.ArgumentPrefix.Any(a => a.Length is < 1 or > 128))
                throw new ArgumentException("Nested broker exact path/SHA/prefix declaration invalid.");
            // A fixture may copy this explicit image later. No discovery/glob;
            // actual image and complete bytes are checked on native observation.
            SafePaths.NoLinks(pin.Executable, budget); ValidateArguments(pin.ArgumentPrefix);
        }
        if (OperatingSystem.IsLinux())
        {
            if (request.BrokerExecutable is null) throw new ArgumentException("Explicit C# broker executable required on Linux.");
            ValidateExecutable(request.BrokerExecutable, budget); ValidateArguments(request.BrokerArgumentPrefix);
            ValidateArguments(request.WatchdogArgumentPrefix);
        }
    }
    internal static void ValidateExecutable(string path, Budget budget)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new ArgumentException("Executable must be an existing absolute file.");
        SafePaths.NoLinks(path, budget);
        var name = Path.GetFileNameWithoutExtension(path);
        if (Interpreters.Contains(name) || name.StartsWith("python", StringComparison.OrdinalIgnoreCase) || name.StartsWith("powershell", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Interpreter/script execution is not allowed.");
        using var file = File.OpenRead(path); Span<byte> header = stackalloc byte[4];
        if (file.Read(header) != header.Length || (OperatingSystem.IsWindows() ? !(header[0] == 'M' && header[1] == 'Z') : !(header[0] == 0x7f && header[1] == 'E' && header[2] == 'L' && header[3] == 'F')))
            throw new ArgumentException("Executable must be a native apphost/image; scripts and shebang wrappers are rejected.");
    }
    private static void ValidateArguments(string[] arguments)
    {
        if (arguments.Length > 256 || arguments.Sum(a => (long)a.Length + 1) > 30000 || arguments.Any(a => a.Contains('\0')))
            throw new ArgumentException("Argument count/text bound exceeded.");
        if (arguments.Any(a => new[] { ".ps1", ".sh", ".bash", ".py", ".cmd", ".bat", ".vbs", ".js", ".rb", ".pl" }.Any(extension => a.EndsWith(extension, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Interpreter script arguments are not allowed.");
    }
    private static Dictionary<string, string> BuildEnvironment(ExecutionRequest request, string ownership)
    {
        var environment = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (environment.Count >= 1024) throw new ArgumentException("Inherited environment item bound exceeded.");
            if (entry.Key is string key && entry.Value is string value) environment[key] = value;
        }
        foreach (var pair in request.Environment)
            if (pair.Value is null) environment.Remove(pair.Key); else environment[pair.Key] = pair.Value;
        environment.Remove("SEZIKA_START_GATE"); environment["SEZIKA_EXECUTION_ID"] = ownership;
        if (environment.Sum(e => (long)e.Key.Length + e.Value.Length + 2) > 1_048_576) throw new ArgumentException("Environment text bound exceeded.");
        return environment;
    }
    private static void Check(ExecutionRequest request, Budget budget, CancellationToken cancellation, Stopwatch clock)
    {
        cancellation.ThrowIfCancellationRequested(); budget.Check();
        if (request.CancelFile is not null && File.Exists(request.CancelFile)) throw new OperationCanceledException("CANCEL file observed.");
        if (request.StopFile is not null && File.Exists(request.StopFile)) throw new OperationCanceledException("STOP file observed.");
        if (clock.Elapsed.TotalSeconds >= request.TimeoutSeconds) throw new TimeoutException("Hard execution deadline exceeded.");
    }
    private static ExecutionIdentity Identity(ProcessObservation actual, int[] chain, string ownership)
    {
        if (!CompleteIdentity(actual))
            throw new IOException($"Process identity is incomplete: state={actual.State}; pid={actual.Pid}; parent={actual.ParentPid}; argv_chars={actual.CommandLine?.Length ?? 0}; started={actual.Started is not null}; ticks={actual.StartTicks is not null}; boot={actual.BootId is not null}; detail={actual.Error}");
        return new() { Pid = actual.Pid, ParentPid = actual.ParentPid, ParentChain = chain, Started = actual.Started,
            StartTicks = actual.StartTicks, BootId = actual.BootId, CommandLine = actual.CommandLine!, Ownership = ownership, Platform = actual.Platform };
    }
    private static bool CompleteIdentity(ProcessObservation actual) => actual.State == "Present" && !string.IsNullOrEmpty(actual.CommandLine) && actual.ParentPid > 0 &&
        (actual.Platform == "linux" ? actual.StartTicks is not null && !string.IsNullOrEmpty(actual.BootId) : actual.Started is not null);
    private static void ObserveMembers(IExecutionSession session, Dictionary<int, ExecutionIdentity> records, ExecutionRequest request, Budget budget, List<string> errors, Dictionary<int, string> pendingRegistrations)
    {
        var activeMembers = session is WindowsExecutionSession windowsMembers ? windowsMembers.Members(budget) : session.Members(); var clock = Stopwatch.StartNew();
        var members = session is WindowsExecutionSession notifiedWindows ? activeMembers.Concat(notifiedWindows.NotifiedPids()).Distinct().ToArray() : activeMembers;
        if (members.Length > request.MaximumProcesses) throw new IOException("Member count bound exceeded.");
        // Retry ordering only within a bounded snapshot: parent before child.
        var pending = new HashSet<int>(members.Where(pid => !records.ContainsKey(pid)));
        for (var depth = 0; depth < 64 && pending.Count > 0 && clock.Elapsed.TotalSeconds < 3; depth++)
        {
            budget.Check(); var progress = false;
            foreach (var pid in pending.ToArray())
            {
                budget.Check();
                var actual = session is WindowsExecutionSession sampledWindows ? sampledWindows.ObserveRegisteredCandidate(pid) : ProcessObserver.Observe(pid);
                if (actual.State == "Absent")
                {
                    // A NEW_PROCESS event whose complete identity was never
                    // observed is still UNKNOWN after exit, not fabricated proof.
                    if (session is WindowsExecutionSession incompleteWindows && incompleteWindows.NotifiedPids().Contains(pid)) continue;
                    if (session is LinuxExecutionSession)
                        pendingRegistrations.TryAdd(pid, "Owned Linux member disappeared before complete native birth/parent/argv registration.");
                    pending.Remove(pid); progress = true; continue;
                }
                if (actual.State != "Present") continue;
                if (!records.TryGetValue(actual.ParentPid, out var parent)) continue;
                var liveParent = ProcessObserver.Observe(parent.Pid);
                // A fully registered Windows parent's retained birth HANDLE
                // proves exit even when its dying PEB/argv query is UNKNOWN.
                // Require the signalled exact-birth HANDLE, not just retention;
                // an unregistered parent or incomplete child stays unproven.
                var retainedExitedParent = session is WindowsExecutionSession windows && liveParent.State != "Present" && windows.HasExitedBirth(parent);
                if (!retainedExitedParent && (liveParent.State != "Present" || (parent.Started is not null && parent.Started != liveParent.Started) ||
                    (parent.StartTicks is not null && (parent.StartTicks != liveParent.StartTicks || parent.BootId != liveParent.BootId))))
                    continue;
                if (session is LinuxExecutionSession)
                {
                    actual = LinuxExecutionSession.ObserveRegistrationCandidate(actual, liveParent.CommandLine, budget);
                    if (!CompleteIdentity(actual)) continue;
                }
                if (parent.ParentChain.Length >= 64) throw new IOException("Parent-chain depth exceeded.");
                var identity = Identity(actual, parent.ParentChain.Append(parent.Pid).ToArray(), session.Ownership);
                identity.ScopeId = parent.ScopeId;
                if (OperatingSystem.IsLinux())
                {
                    try
                    {
                        var observedScope = LinuxExecutionSession.ExecutionScope(actual.Pid);
                        identity.ExpectedExecCommandLine = BrokerExecCommandLine(actual, liveParent, request, budget, out var nestedScope);
                        if (nestedScope is not null)
                        {
                            if (observedScope != nestedScope) throw new IOException("Nested broker ownership marker differs from its explicit configuration.");
                            identity.ScopeId = nestedScope;
                        }
                    }
                    catch (Exception admission) when (Expected(admission) && admission is not (OperationCanceledException or TimeoutException))
                    {
                        // Native birth/parent/original argv establish owned
                        // cleanup membership independently of broker trust.
                        // Refusal remains permanent evidence, while the exact
                        // pidfd can still terminate/reap the rejected tree.
                        identity.ScopeId = parent.ScopeId;
                        identity.ExpectedExecCommandLine = null;
                        var refusal = "Nested broker admission rejected: " + admission.GetType().Name + ": " + admission.Message;
                        errors.Add(refusal); identity.ObservationNotes = [refusal];
                    }
                }
                if (session is LinuxExecutionSession registration && !registration.ReadyForRegistration(identity)) continue;
                session.Register(identity);
                if (pendingRegistrations.Remove(pid, out var previous)) identity.ObservationNotes = ["Later complete native birth/parent/argv registration resolves " + previous];
                records.Add(pid, identity); pending.Remove(pid); progress = true;
            }
            if (!progress) break;
        }
        if (pending.Count > 0)
        {
            if (session is WindowsExecutionSession diagnostics)
                foreach (var pid in pending) pendingRegistrations[pid] = diagnostics.NotificationDetail(pid);
            else
                foreach (var pid in pending)
                    pendingRegistrations.TryAdd(pid, "Linux owned member lacks complete independently observed native birth/parent/argv admission.");
        }
        foreach (var pid in members.Where(records.ContainsKey))
        {
            budget.Check();
            var actual = ProcessObserver.Observe(pid); var record = records[pid];
            if (actual.State == "Absent")
            {
                record.State = "EXITED";
                if (record.PendingObservationError is not null) record.ExitProof = "Absent observation resolves " + record.PendingObservationError;
                record.PendingObservationError = null; continue;
            }
            if (actual.State != "Present" && session is WindowsExecutionSession exitedWindows && exitedWindows.HasExitedBirth(record))
            {
                record.State = "EXITED_RETAINED_HANDLE";
                record.ExitProof = "Same creation time on retained HANDLE and signalled exit; observer=" + actual.State + "; detail=" + actual.Error;
                record.PendingObservationError = null;
                continue;
            }
            if ((actual.State != "Present" || string.IsNullOrEmpty(actual.CommandLine)) && session is LinuxExecutionSession exitedLinux && exitedLinux.HasExitedBirth(record))
            {
                record.State = "EXITED";
                record.ExitProof = "Complete registered native birth/parent/argv and retained pidfd termination/reaping signal; observer=" + actual.State + "; detail=" + actual.Error;
                record.PendingObservationError = null;
                continue;
            }
            if (OperatingSystem.IsLinux() && actual.State == "Unknown" && LinuxExecutionSession.TryStat(pid)?.State is "Z" or "X")
            { record.State = "EXITED_PENDING_REAP"; continue; }
            if (actual.State != "Present")
            {
                if (session is WindowsExecutionSession retained && retained.RetainsBirth(record))
                {
                    var note = "observer=" + actual.State + "; detail=" + actual.Error;
                    if (!record.ObservationNotes.Contains(note, StringComparer.Ordinal))
                    {
                        if (record.ObservationNotes.Length >= 8) throw new IOException("Native observation note bound exceeded.");
                        record.ObservationNotes = record.ObservationNotes.Append(note).ToArray();
                    }
                    record.PendingObservationError = note;
                    record.State = "UNKNOWN_PENDING_RETAINED_EXIT";
                }
                else errors.Add("UNKNOWN member identity " + pid + ": " + actual.Error);
                continue;
            }
            if ((record.Started is not null && record.Started != actual.Started) || (record.StartTicks is not null && (record.StartTicks != actual.StartTicks || record.BootId != actual.BootId)))
            { errors.Add("UNKNOWN reused member PID " + pid); continue; }
            if (string.IsNullOrEmpty(actual.CommandLine) && session is LinuxExecutionSession retainedLinux && retainedLinux.RetainsBirth(record))
            {
                const string note = "Present native Linux observation has empty argv before retained pidfd exit proof.";
                if (!record.ObservationNotes.Contains(note, StringComparer.Ordinal))
                {
                    if (record.ObservationNotes.Length >= 8) throw new IOException("Native observation note bound exceeded.");
                    record.ObservationNotes = record.ObservationNotes.Append(note).ToArray();
                }
                record.PendingObservationError = note;
                record.State = "UNKNOWN_PENDING_PIDFD_EXIT";
                continue;
            }
            if (session is WindowsExecutionSession && record.ParentPid != actual.ParentPid)
            { errors.Add("UNKNOWN member parent changed " + pid); continue; }
            if (actual.CommandLine != record.CommandLine)
            {
                if (session is WindowsExecutionSession windows && windows.ConsoleHost(pid))
                {
                    if (record.CommandLineRevisions.LastOrDefault() != actual.CommandLine)
                    {
                        if (record.CommandLineRevisions.Length >= 4) throw new IOException("System console-host argv revision bound exceeded.");
                        record.CommandLineRevisions = record.CommandLineRevisions.Append(actual.CommandLine!).ToArray();
                    }
                    continue;
                }
                // The registered Linux broker execs exactly the declared argv;
                // arbitrary descendant argv changes retain UNKNOWN evidence.
                if (OperatingSystem.IsLinux() && record.ExpectedExecCommandLine is not null && actual.CommandLine == record.ExpectedExecCommandLine)
                    record.AfterExecCommandLine = actual.CommandLine;
                else if (actual.CommandLine != record.AfterExecCommandLine) errors.Add("UNKNOWN member argv changed " + pid + "; actual=" + actual.CommandLine);
            }
            if (record.PendingObservationError is not null && actual.CommandLine == record.CommandLine && actual.Started == record.Started && actual.ParentPid == record.ParentPid)
            {
                record.ExitProof = "Subsequent complete native birth/parent/argv observation resolves " + record.PendingObservationError;
                record.PendingObservationError = null; record.State = "REGISTERED";
            }
        }
    }
    private static string? BrokerExecCommandLine(ProcessObservation actual, ProcessObservation registeredParent, ExecutionRequest request, Budget budget, out string? scope)
    {
        scope = null;
        var arguments = actual.CommandLine!.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (arguments.Length < 3) return null;
        var pin = request.NestedBrokers.SingleOrDefault(candidate => candidate.Executable == arguments[0]);
        var prefix = pin?.ArgumentPrefix ?? (arguments[0] == request.BrokerExecutable ? request.BrokerArgumentPrefix : null);
        if (prefix is null || arguments.Length != prefix.Length + 2 || !arguments.AsSpan(1, prefix.Length).SequenceEqual(prefix)) return null;
        ValidateExecutable(arguments[0], budget);
        var mapped = File.ResolveLinkTarget("/proc/" + actual.Pid.ToString(CultureInfo.InvariantCulture) + "/exe", false)?.FullName;
        if (mapped != arguments[0]) throw new IOException("Nested broker actual native image differs from its admitted path.");
        if (pin is not null)
        {
            using var executable = File.OpenRead(pin.Executable);
            if (executable.Length is < 4 or > 134217728 || budget.Hash(executable, executable.Length) != pin.Sha256)
                throw new IOException("Nested broker complete native-image SHA differs.");
        }
        var path = arguments[^1]; SafePaths.NoLinks(path, budget);
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > 262144) throw new IOException("Nested broker configuration bound.");
        var broker = JsonSerializer.Deserialize(File.ReadAllBytes(path), ExecutionJsonContext.Default.BrokerConfiguration) ?? throw new IOException("Missing nested broker configuration.");
        var launcher = broker.Launcher;
        if (registeredParent.State != "Present" || registeredParent.Pid != actual.ParentPid || launcher is null || broker.ParentPid != actual.ParentPid ||
            launcher.Pid != registeredParent.Pid || launcher.Platform != "linux" || launcher.ParentPid != registeredParent.ParentPid ||
            launcher.StartTicks != registeredParent.StartTicks || launcher.BootId != registeredParent.BootId || launcher.CommandLine != registeredParent.CommandLine ||
            !Guid.TryParseExact(broker.OwnershipId, "N", out _) || LinuxExecutionSession.ExecutionScope(actual.Pid) != broker.OwnershipId)
            throw new IOException("Nested broker launcher identity differs.");
        ValidateExecutable(broker.Executable, budget); ValidateArguments(broker.ArgumentList);
        var expected = string.Join('\0', new[] { broker.Executable }.Concat(broker.ArgumentList)) + '\0';
        var after = ProcessObserver.Observe(actual.Pid);
        if (after.State != "Present" || after.ParentPid != actual.ParentPid || after.StartTicks != actual.StartTicks || after.BootId != actual.BootId ||
            (after.CommandLine != actual.CommandLine && after.CommandLine != expected))
            throw new IOException("Nested broker birth/parent/argv changed during native admission.");
        if (after.CommandLine == expected && File.ResolveLinkTarget("/proc/" + actual.Pid.ToString(CultureInfo.InvariantCulture) + "/exe", false)?.FullName != broker.Executable)
            throw new IOException("Nested broker actual exec image differs from its exact configuration.");
        scope = broker.OwnershipId;
        return expected;
    }
    private static void Drain(IExecutionSession session, bool error, FileStream output, byte[] buffer, int maximum, ref bool done, ExecutionResult result, bool enforce = true)
    {
        if (done) return;
        for (var chunk = 0; chunk < 32; chunk++)
        {
            var size = session.ReadOutput(error, buffer);
            if (size < 0) { done = true; break; }
            if (size == 0) break;
            var count = error ? result.StandardErrorBytes : result.StandardOutputBytes;
            var accepted = (int)Math.Min(size, Math.Max(0, maximum - count));
            if (accepted > 0) output.Write(buffer, 0, accepted);
            if (error) result.StandardErrorBytes += accepted; else result.StandardOutputBytes += accepted;
            if (accepted != size)
            {
                result.OutputLimitExceeded = true;
                if (enforce) throw new InvalidDataException((error ? "stderr" : "stdout") + " byte bound exceeded; partial output preserved.");
            }
        }
    }
    private static FileStream NewFile(string path) => new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    private static void WriteNew(string path, byte[] bytes)
    {
        // Publish only complete metadata, after the writer has closed. Readers
        // never see a live JSON writer or a partially written registration gate.
        var pending = path + ".pending";
        using (var file = NewFile(pending)) { file.Write(bytes); file.Flush(true); }
        File.Move(pending, path); // final and pending paths are never overwritten
    }
    private static bool Expected(Exception error) => error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or
        OperationCanceledException or TimeoutException or Win32Exception or PlatformNotSupportedException or UnauthorizedAccessException or JsonException;

    internal static unsafe int RunBroker(string configurationPath, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("execve broker is Linux only.");
        var budget = new Budget(40, null, cancellation); budget.Check();
        var path = Path.GetFullPath(configurationPath); SafePaths.NoLinks(path, budget);
        if (new FileInfo(path).Length > 262144) throw new InvalidDataException("Broker configuration byte bound exceeded.");
        var config = JsonSerializer.Deserialize(File.ReadAllBytes(path), ExecutionJsonContext.Default.BrokerConfiguration) ?? throw new InvalidDataException("Missing broker configuration.");
        if (Environment.GetEnvironmentVariable("SEZIKA_EXECUTION_ID") != config.OwnershipId) throw new InvalidDataException("Broker ownership marker differs.");
        // Death notification is armed before the registration gate. Check the
        // parent again afterwards, closing the pre-prctl parent-death race.
        if (ExecutionNative.prctl(1, 9, 0, 0, 0) != 0 || ExecutionNative.getppid() != config.ParentPid)
            throw new IOException("Broker parent death protection or identity failed.");
        ValidateExecutable(config.Executable, budget); ValidateArguments(config.ArgumentList);
        StartupGate.Wait(cancellation);
        AncestorAdmission.RequireActive(config.OutputDirectory, config.OwnershipId, budget);
        _ = AncestorAdmission.Wait(budget, cancellation, "broker", cancelDirectory: config.OutputDirectory);
        LinuxWatchdog.Start(config, budget, cancellation);
        AncestorAdmission.RequireActive(config.OutputDirectory, config.OwnershipId, budget);
        var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().Select(e => (string)e.Key + "=" + (string?)e.Value).ToArray();
        using var argv = new NativeUtf8Vector(new[] { config.Executable }.Concat(config.ArgumentList));
        using var env = new NativeUtf8Vector(environment);
        fixed (nint* arguments = argv.Pointers)
        fixed (nint* environmentPointers = env.Pointers)
            if (ExecutionNative.execve(config.Executable, arguments, environmentPointers) < 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "execve target failed");
        return 127; // execve never returns after success.
    }

    internal static int RunWatchdog(string configurationPath, CancellationToken cancellation) => LinuxWatchdog.Run(configurationPath, cancellation);
}
