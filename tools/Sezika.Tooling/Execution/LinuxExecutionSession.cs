using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Sezika.Tooling;

internal static class AncestorAdmission
{
    internal const string EnvironmentName = "SEZIKA_LINUX_OBSERVERS";
    internal static void Cancel(string directory, string scope, Budget budget)
    {
        var path = Path.Combine(directory, "admission.cancel"); SafePaths.NoLinks(path, budget);
        if (!File.Exists(path)) WriteNew(path, Encoding.ASCII.GetBytes(scope));
        RegularFiles.Require(path, budget);
        if (new FileInfo(path).Length > 64 || File.ReadAllText(path) != scope) throw new IOException("Foreign Linux admission cancellation marker preserved.");
    }
    internal static void RequireActive(string directory, string scope, Budget budget)
    {
        budget.Check(); var path = Path.Combine(directory, "admission.cancel"); SafePaths.NoLinks(path, budget);
        if (!File.Exists(path)) return;
        RegularFiles.Require(path, budget);
        if (new FileInfo(path).Length > 64 || File.ReadAllText(path) != scope) throw new IOException("Foreign Linux admission cancellation marker preserved.");
        throw new OperationCanceledException("Scoped Linux admission cancellation prevents payload execution.");
    }
    internal static AdmissionObserver Declare(string directory, string scope, ExecutionIdentity caller, Budget budget)
    {
        var observer = new AdmissionObserver { OutputDirectory = directory, OwnershipId = scope, NativeIdentity = caller };
        Shape(observer);
        RequireNativeCaller(caller, ProcessObserver.Observe(Environment.ProcessId, budget));
        RequireOwnedDirectory(observer, budget);
        WriteNew(Path.Combine(directory, "observer.json"), JsonSerializer.SerializeToUtf8Bytes(observer, ExecutionJsonContext.Default.AdmissionObserver));
        return observer;
    }
    internal static void Attach(ExecutionRequest request, Dictionary<string, string> environment, string scope, ExecutionIdentity caller, Budget budget)
    {
        var inherited = Read();
        if (request.LinuxObservers is null || request.LinuxObservers.Length > 63 - inherited.Length) throw new IOException("Linux observer chain exceeds64 declarations.");
        foreach (var additional in request.LinuxObservers)
        {
            Shape(additional); RequireNativeCaller(additional.NativeIdentity, ProcessObserver.Observe(Environment.ProcessId, budget));
            if (inherited.Length == 0 || !additional.OutputDirectory.StartsWith(inherited[0].OutputDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("Additional Linux observer declarations must be inside this task's inherited owned output.");
        }
        var observers = inherited.Concat(request.LinuxObservers).ToArray();
        Validate(observers, budget);
        var own = Declare(request.OutputDirectory, scope, caller, budget);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(observers.Append(own).ToArray(), ExecutionJsonContext.Default.AdmissionObserverArray);
        if (bytes.Length > 65536) throw new IOException("Linux observer chain exceeds64KiB.");
        environment[EnvironmentName] = Encoding.UTF8.GetString(bytes);
    }
    internal static AdmissionObserver[] Read()
    {
        var text = Environment.GetEnvironmentVariable(EnvironmentName);
        if (string.IsNullOrEmpty(text)) return [];
        if (Encoding.UTF8.GetByteCount(text) > 65536) throw new IOException("Linux observer chain exceeds64KiB.");
        var observers = JsonSerializer.Deserialize(text, ExecutionJsonContext.Default.AdmissionObserverArray) ?? throw new IOException("Missing Linux observer chain.");
        if (observers.Length > 64) throw new IOException("Linux observer chain exceeds64 declarations.");
        foreach (var observer in observers) Shape(observer);
        return observers;
    }
    private static void RequireOwnedDirectory(AdmissionObserver observer, Budget budget)
    {
        Shape(observer);
        if (!Path.IsPathFullyQualified(observer.OutputDirectory) || Path.GetFullPath(observer.OutputDirectory) != observer.OutputDirectory ||
            !Guid.TryParseExact(observer.OwnershipId, "N", out _)) throw new IOException("Linux observer path/scope is not exact.");
        SafePaths.NoLinks(observer.OutputDirectory, budget);
        var ownership = Path.Combine(observer.OutputDirectory, "ownership.json"); RegularFiles.Require(ownership, budget);
        if (new FileInfo(ownership).Length > 64 || File.ReadAllText(ownership) != observer.OwnershipId)
            throw new IOException("Linux observer ownership file differs.");
    }
    private static void Shape(AdmissionObserver? observer)
    {
        if (observer is null || observer.NativeIdentity is null || string.IsNullOrEmpty(observer.OutputDirectory) || observer.OutputDirectory.Length > 32768 ||
            observer.OwnershipId is null || observer.NativeIdentity.CommandLine is null || observer.NativeIdentity.CommandLine.Length is < 1 or > 32768 ||
            observer.NativeIdentity.Platform != "linux" || observer.NativeIdentity.Pid <= 0 || observer.NativeIdentity.ParentPid <= 0 ||
            observer.NativeIdentity.StartTicks is null || !Guid.TryParse(observer.NativeIdentity.BootId, out _) || observer.NativeIdentity.Ownership != "launcher" ||
            observer.NativeIdentity.ParentChain is not { Length: 0 } || observer.NativeIdentity.ScopeId is not null)
            throw new IOException("Linux observer declaration has null, incomplete or oversized fields.");
    }
    private static bool SameNative(ExecutionIdentity? identity, ProcessObservation actual) => identity is not null && actual.State == "Present" && identity.Platform == "linux" &&
        identity.Pid == actual.Pid && identity.ParentPid > 0 && identity.ParentPid == actual.ParentPid && identity.StartTicks is not null &&
        identity.StartTicks == actual.StartTicks && !string.IsNullOrEmpty(identity.BootId) && identity.BootId == actual.BootId &&
        !string.IsNullOrEmpty(identity.CommandLine) && identity.CommandLine == actual.CommandLine;
    private static void RequireNativeCaller(ExecutionIdentity identity, ProcessObservation actual)
    {
        if (!SameNative(identity, actual)) throw new IOException("Linux observer " + identity.Pid + " complete live native identity differs.");
    }
    private static void Validate(AdmissionObserver[] observers, Budget budget)
    {
        if (observers.Length == 0) { budget.Check(); return; }
        var remaining = new HashSet<int>();
        foreach (var observer in observers) { Shape(observer); remaining.Add(observer.NativeIdentity.Pid); }
        var clock = Stopwatch.StartNew(); var ancestors = new Dictionary<int, ulong>(); var cursor = Environment.ProcessId;
        // Stop at the highest declared task observer; OS/WSL ancestors beyond
        // that boundary are not part of this execution's ownership proof.
        for (var depth = 0; depth < 64 && cursor > 0 && remaining.Count > 0 && clock.Elapsed.TotalSeconds < 2; depth++)
        {
            budget.Check(); var stat = LinuxExecutionSession.TryStat(cursor, budget); if (stat is null) break;
            if (!ancestors.TryAdd(cursor, stat.Value.Start)) throw new IOException("Linux native ancestor cycle.");
            remaining.Remove(cursor); cursor = stat.Value.Parent;
        }
        var paths = new HashSet<string>(StringComparer.Ordinal); var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observer in observers)
        {
            budget.Check(); if (clock.Elapsed.TotalSeconds >= 2) throw new TimeoutException("Linux observer validation shared2s deadline.");
            Shape(observer);
            if (!paths.Add(observer.OutputDirectory) || !scopes.Add(observer.OwnershipId) || !ancestors.TryGetValue(observer.NativeIdentity.Pid, out var birth) || birth != observer.NativeIdentity.StartTicks)
                throw new IOException("Linux observer duplicate path or non-ancestor declaration.");
            var actual = ProcessObserver.Observe(observer.NativeIdentity.Pid, budget); RequireNativeCaller(observer.NativeIdentity, actual);
            RequireOwnedDirectory(observer, budget);
            var path = Path.Combine(observer.OutputDirectory, "observer.json"); RegularFiles.Require(path, budget);
            if (new FileInfo(path).Length > 65536) throw new IOException("Linux observer declaration exceeds64KiB.");
            var stored = JsonSerializer.Deserialize(File.ReadAllBytes(path), ExecutionJsonContext.Default.AdmissionObserver) ?? throw new IOException("Missing Linux observer declaration.");
            Shape(stored);
            if (stored.OutputDirectory != observer.OutputDirectory || stored.OwnershipId != observer.OwnershipId || !SameNative(stored.NativeIdentity, actual))
                throw new IOException("Linux observer stored declaration differs from its live native identity.");
            RequireNativeCaller(observer.NativeIdentity, actual);
        }
        if (clock.Elapsed.TotalSeconds >= 2) throw new TimeoutException("Linux observer validation shared2s deadline.");
    }
    private static string SignalPath(string directory, ExecutionIdentity member) => Path.Combine(directory, "admissions",
        member.Pid.ToString(CultureInfo.InvariantCulture) + "-" + member.StartTicks?.ToString(CultureInfo.InvariantCulture) + "-" + member.ScopeId + ".json");
    internal static void Publish(string directory, string scope, IEnumerable<ExecutionIdentity> records, LinuxExecutionSession session, Budget budget)
    {
        var clock = Stopwatch.StartNew(); var count = 0;
        foreach (var member in records)
        {
            budget.Check(); if (++count > 256 || clock.Elapsed.TotalSeconds >= 2) throw new IOException("Linux admission publication256 members/shared2s bound.");
            if (member.ScopeId is null || !session.RetainsBirth(member)) continue;
            if (session.AdmissionPublished(member)) continue;
            var path = SignalPath(directory, member); SafePaths.NoLinks(path, budget);
            if (File.Exists(path)) continue;
            if (!SameNative(member, ProcessObserver.Observe(member.Pid, budget)) || LinuxExecutionSession.ExecutionScope(member.Pid, budget) != member.ScopeId) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteNew(path, JsonSerializer.SerializeToUtf8Bytes(new AdmissionSignal { ObserverOwnershipId = scope, NativeIdentity = member }, ExecutionJsonContext.Default.AdmissionSignal));
            session.AdmissionPublished(member, true);
        }
    }
    internal static bool Wait(Budget budget, CancellationToken cancellation, string stage, Func<bool>? ownerExited = null, string? cancelDirectory = null)
    {
        if (ownerExited?.Invoke() == true) return false;
        var clock = Stopwatch.StartNew(); var observers = Read();
        var missing = observers.Select(observer => observer.NativeIdentity.Pid.ToString(CultureInfo.InvariantCulture) + "/" + observer.OwnershipId).ToHashSet(StringComparer.Ordinal);
        try { Validate(observers, budget); }
        catch (IOException error) { throw new IOException("Linux ancestor admission stage=" + stage + "; observer validation: " + error.Message, error); }
        catch (TimeoutException error) { throw new TimeoutException("Linux ancestor admission stage=" + stage + "; missing_observers=" + string.Join(',', missing.Order()) + "; declaration validation deadline.", error); }
        var actual = ProcessObserver.Observe(Environment.ProcessId, budget);
        var scope = Environment.GetEnvironmentVariable("SEZIKA_EXECUTION_ID");
        if (actual.State != "Present" || actual.StartTicks is null || string.IsNullOrEmpty(actual.CommandLine) || !Guid.TryParseExact(scope, "N", out _))
            throw new IOException("Linux admission wait lacks complete current native birth/argv/scope.");
        var member = new ExecutionIdentity { Pid = actual.Pid, Platform = "linux", ParentPid = actual.ParentPid, StartTicks = actual.StartTicks,
            BootId = actual.BootId, CommandLine = actual.CommandLine, ScopeId = scope };
        for (var poll = 0; poll < 100 && clock.Elapsed.TotalSeconds < 2; poll++)
        {
            budget.Check(); cancellation.ThrowIfCancellationRequested(); if (ownerExited?.Invoke() == true) return false;
            if (cancelDirectory is not null) RequireActive(cancelDirectory, scope!, budget);
            var complete = true;
            foreach (var observer in observers)
            {
                var key = observer.NativeIdentity.Pid.ToString(CultureInfo.InvariantCulture) + "/" + observer.OwnershipId;
                budget.Check(); if (clock.Elapsed.TotalSeconds >= 2) throw new TimeoutException("Linux ancestor admission stage=" + stage + "; shared2s deadline; missing_observers=" + string.Join(',', missing.Order()));
                if (!SameNative(observer.NativeIdentity, ProcessObserver.Observe(observer.NativeIdentity.Pid, budget)))
                    throw new IOException("Linux ancestor admission stage=" + stage + "; observer=" + observer.NativeIdentity.Pid + " died or changed native identity.");
                var path = SignalPath(observer.OutputDirectory, member); SafePaths.NoLinks(path, budget);
                if (!File.Exists(path)) { complete = false; missing.Add(key); continue; }
                RegularFiles.Require(path, budget); if (new FileInfo(path).Length > 65536) throw new IOException("Linux admission signal exceeds64KiB.");
                var signal = JsonSerializer.Deserialize(File.ReadAllBytes(path), ExecutionJsonContext.Default.AdmissionSignal);
                if (signal?.NativeIdentity is not { } signalled || signal.ObserverOwnershipId != observer.OwnershipId || !SameNative(signalled, actual) || signalled.ScopeId != scope)
                    throw new IOException("Linux ancestor scheduling signal differs from exact native birth/parent/argv/scope.");
                missing.Remove(key);
            }
            if (clock.Elapsed.TotalSeconds >= 2) throw new TimeoutException("Linux ancestor admission stage=" + stage + "; shared2s deadline; missing_observers=" + string.Join(',', missing));
            if (complete) return true;
            if (cancellation.WaitHandle.WaitOne(20)) cancellation.ThrowIfCancellationRequested();
        }
        throw new TimeoutException("Linux ancestor native admission stage=" + stage + "; missing_observers=" + string.Join(',', missing) + ";100 polls/shared2 seconds.");
    }
    private static void WriteNew(string path, byte[] bytes)
    {
        using (var file = new FileStream(path + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { file.Write(bytes); file.Flush(true); }
        File.Move(path + ".pending", path, false);
    }
}

internal sealed class NativeUtf8Vector : IDisposable
{
    internal nint[] Pointers { get; }
    internal NativeUtf8Vector(IEnumerable<string> values)
    {
        var texts = values.Take(1025).ToArray();
        if (texts.Length > 1024 || texts.Sum(s => Encoding.UTF8.GetByteCount(s) + 1L) > 1_048_576)
            throw new InvalidDataException("Native argv/environment bound exceeded.");
        Pointers = new nint[texts.Length + 1];
        try
        {
            for (var index = 0; index < texts.Length; index++)
                Pointers[index] = Marshal.StringToCoTaskMemUTF8(texts[index]);
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        for (var index = 0; index < Pointers.Length; index++)
            if (Pointers[index] != 0) { Marshal.FreeCoTaskMem(Pointers[index]); Pointers[index] = 0; }
    }
}

internal sealed class LinuxExecutionSession : IExecutionSession
{
    private ExecutionFd? input, output, error;
    private readonly Dictionary<int, ExecutionFd> pidFds = [];
    private readonly Dictionary<int, (ulong Ticks, string Boot, int Parent, string Command)> registeredBirths = [];
    private readonly HashSet<int> known = [];
    private readonly HashSet<int> publishedAdmissions = [];
    private readonly Dictionary<int, ulong> births = [];
    private readonly int maximumProcesses;
    private readonly HashSet<string> ownershipScopes = new(StringComparer.Ordinal);
    private readonly string gate;
    private readonly string outputDirectory;
    private readonly string ownershipId;
    private readonly string watchdogCommandLine;
    private readonly int priorSubreaper;
    private bool restored, reaped;
    private Stopwatch? cleanupAdmission;
    public int Pid { get; private set; }
    public string Ownership => "linux-process-group-pidfd-subreaper";
    internal LinuxExecutionSession(ExecutionRequest request, Dictionary<string, string> environment, string ownershipId, string gate, string config)
    {
        if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Linux execution supports x64/arm64 libc ABIs.");
        maximumProcesses = request.MaximumProcesses; this.gate = gate; outputDirectory = request.OutputDirectory;
        this.ownershipId = ownershipId;
        watchdogCommandLine = string.Join('\0', new[] { request.BrokerExecutable! }.Concat(request.WatchdogArgumentPrefix)
            .Append(Path.Combine(outputDirectory, "watchdog.json"))) + '\0';
        ownershipScopes.Add(ownershipId);
        priorSubreaper = ReadSubreaper();
        if (ExecutionNative.prctl(36, 1, 0, 0, 0) != 0) throw Error("PR_SET_CHILD_SUBREAPER");
        try { Start(request, environment, config); }
        catch (Exception error)
        {
            if (Pid > 0)
            {
                ExecutionNative.kill(-Pid, 9);
                var clock = Stopwatch.StartNew();
                for (var attempt = 0; attempt < 40 && clock.Elapsed.TotalSeconds < 2; attempt++)
                {
                    if (ExecutionNative.waitpid(Pid, out _, 1) == Pid) { reaped = true; break; }
                    Thread.Sleep(50);
                }
            }
            Dispose(); throw new ExecutionStartupException(error.Message, Pid, Pid == 0 || reaped, error);
        }
    }
    private static unsafe int ReadSubreaper()
    {
        var value = 0;
        if (ExecutionNative.prctl(37, (nuint)(&value), 0, 0, 0) != 0) throw Error("PR_GET_CHILD_SUBREAPER");
        return value;
    }
    private unsafe void Start(ExecutionRequest request, Dictionary<string, string> environment, string config)
    {
        var actions = stackalloc byte[512]; var attributes = stackalloc byte[512];
        new Span<byte>(actions, 512).Clear(); new Span<byte>(attributes, 512).Clear();
        var actionReady = false; var attributesReady = false;
        MakePipe(out var inRead, out var inWrite);
        using var childInput = inRead; input = inWrite;
        MakePipe(out var outRead, out var outWrite);
        output = outRead; using var childOutput = outWrite;
        MakePipe(out var errRead, out var errWrite);
        error = errRead; using var childError = errWrite;
        foreach (var descriptor in new[] { input, output, error })
        {
            var flags = ExecutionNative.fcntl(descriptor.Value, 3, 0);
            if (flags < 0 || ExecutionNative.fcntl(descriptor.Value, 4, flags | 0x800) < 0) throw Error("fcntl(O_NONBLOCK)");
        }
        try
        {
            Check(ExecutionNative.posix_spawn_file_actions_init(actions), "file_actions_init"); actionReady = true;
            Check(ExecutionNative.posix_spawnattr_init(attributes), "spawnattr_init"); attributesReady = true;
            Check(ExecutionNative.posix_spawnattr_setflags(attributes, 2), "spawnattr_setflags"); // POSIX_SPAWN_SETPGROUP
            Check(ExecutionNative.posix_spawnattr_setpgroup(attributes, 0), "spawnattr_setpgroup");
            Check(ExecutionNative.posix_spawn_file_actions_adddup2(actions, childInput.Value, 0), "dup2(stdin)");
            Check(ExecutionNative.posix_spawn_file_actions_adddup2(actions, childOutput.Value, 1), "dup2(stdout)");
            Check(ExecutionNative.posix_spawn_file_actions_adddup2(actions, childError.Value, 2), "dup2(stderr)");
            foreach (var descriptor in new[] { childInput, input, output, childOutput, error, childError })
                Check(ExecutionNative.posix_spawn_file_actions_addclose(actions, descriptor.Value), "file_actions_addclose");
            Check(ExecutionNative.posix_spawn_file_actions_addchdir_np(actions, request.WorkingDirectory), "file_actions_addchdir");
            using var argv = new NativeUtf8Vector(new[] { request.BrokerExecutable! }.Concat(request.BrokerArgumentPrefix).Append(config));
            using var env = new NativeUtf8Vector(environment.Select(p => p.Key + "=" + p.Value));
            fixed (nint* arguments = argv.Pointers)
            fixed (nint* environmentPointers = env.Pointers)
            {
                Check(ExecutionNative.posix_spawn(out var pid, request.BrokerExecutable!, actions, attributes, arguments, environmentPointers), "posix_spawn");
                Pid = pid; known.Add(pid);
            }
            if (ExecutionNative.getpgid(Pid) != Pid) throw new IOException("Spawned Linux process group identity differs.");
        }
        finally
        {
            if (actionReady) ExecutionNative.posix_spawn_file_actions_destroy(actions);
            if (attributesReady) ExecutionNative.posix_spawnattr_destroy(attributes);
        }
    }
    private static unsafe void MakePipe(out ExecutionFd read, out ExecutionFd write)
    {
        int* pair = stackalloc int[2];
        if (ExecutionNative.pipe2(pair, 0x80000) != 0) throw Error("pipe2(O_CLOEXEC)");
        read = new(pair[0]); write = new(pair[1]);
        if (pair[0] < 3 || pair[1] < 3) { read.Dispose(); write.Dispose(); throw new IOException("Open stdin/stdout/stderr required before spawning."); }
    }
    public void Release()
    {
        var final = Path.Combine(gate, "go");
        using (var release = new FileStream(final + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { release.Write(Encoding.ASCII.GetBytes(Pid.ToString(CultureInfo.InvariantCulture))); release.Flush(true); }
        File.Move(final + ".pending", final);
    }
    public unsafe bool TryExit(out int exitCode)
    {
        exitCode = 0;
        if (reaped) throw new InvalidOperationException("Root was already reaped.");
        var info = stackalloc byte[128]; new Span<byte>(info, 128).Clear();
        // WNOWAIT retains the zombie and reserves its PID/process group identity
        // until every owned descendant has been terminated and reaped.
        if (ExecutionNative.waitid(1, (uint)Pid, info, 4 | 1 | 0x1000000) != 0) throw Error("waitid(WNOWAIT)");
        if (*(int*)(info + 16) == 0) return false;
        if (*(int*)(info + 16) != Pid) throw new IOException("waitid returned an unexpected child.");
        var code = *(int*)(info + 8); var status = *(int*)(info + 24);
        exitCode = code == 1 ? status : 128 + status; return true;
    }
    public unsafe int ReadOutput(bool isError, byte[] bytes)
    {
        var descriptor = isError ? error : output;
        if (descriptor is null) return -1;
        fixed (byte* pointer = bytes)
        {
            var count = ExecutionNative.read(descriptor.Value, pointer, (nuint)bytes.Length);
            if (count == 0) return -1;
            if (count > 0) return checked((int)count);
            var nativeError = Marshal.GetLastPInvokeError();
            if (nativeError is 11 or 4) return 0;
            throw Error("read(pipe)");
        }
    }
    public Task WriteInputAsync(byte[] bytes, CancellationToken cancellation) => Task.Run(() => WriteInput(bytes, cancellation), CancellationToken.None);
    private unsafe void WriteInput(byte[] bytes, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew(); var total = 0;
        try
        {
            // At most 864,001 polls AND86,400 seconds;100ms backoff on EAGAIN.
            for (var attempt = 0; attempt < 864001 && total < bytes.Length; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (clock.Elapsed.TotalSeconds >= 86400) throw new TimeoutException("Standard input transfer deadline.");
                fixed (byte* pointer = bytes)
                {
                    var size = ExecutionNative.write(input!.Value, pointer + total, (nuint)Math.Min(16384, bytes.Length - total));
                    if (size >= 0) { total += checked((int)size); continue; }
                    if (Marshal.GetLastPInvokeError() is not (11 or 4)) throw Error("write(stdin)");
                }
                if (cancellation.WaitHandle.WaitOne(100)) cancellation.ThrowIfCancellationRequested();
            }
            if (total != bytes.Length) throw new IOException("Standard input poll bound exceeded.");
        }
        finally { input?.Dispose(); input = null; }
    }
    public int[] Members() => MembersCore(new Budget(2, null, CancellationToken.None), null);

    internal int[] Members(Budget parentBudget, Action<int, ulong, Budget> onOwned)
    {
        ArgumentNullException.ThrowIfNull(parentBudget);
        ArgumentNullException.ThrowIfNull(onOwned);
        parentBudget.Check();
        var scanBudget = new Budget(2, null, parentBudget.Cancellation, parentBudget);
        return MembersCore(scanBudget, onOwned);
    }

    private int[] MembersCore(Budget? scanBudget, Action<int, ulong, Budget>? onOwned)
    {
        var result = new List<int>(); var clock = Stopwatch.StartNew(); var count = 0;
        var selected = new HashSet<int>();
        void ObserveOwned(int pid)
        {
            scanBudget?.Check();
            var stat = TryStat(pid, scanBudget);
            scanBudget?.Check();
            if (stat is null) return;
            if (births.TryGetValue(pid, out var birth) && birth != stat.Value.Start) return;
            if (pid == Pid || known.Contains(pid) || stat.Value.Group == Pid || known.Contains(stat.Value.Parent) ||
                (stat.Value.Parent == Environment.ProcessId && HasOwnershipMarker(pid, scanBudget)))
            {
                if (selected.Add(pid))
                {
                    if (result.Count >= maximumProcesses) throw new IOException("Linux owned-process count exceeded.");
                    result.Add(pid); known.Add(pid); births.TryAdd(pid, stat.Value.Start);
                }
                if (onOwned is not null)
                {
                    scanBudget!.Check();
                    onOwned(pid, stat.Value.Start, scanBudget);
                    scanBudget.Check();
                }
            }
        }
        // A task-owned PID declaration is only a scheduling hint. The same
        // selector and complete native registration still apply; no cached
        // tuple or PID-file value can establish ownership or admission.
        var watchdog = WatchdogPid(scanBudget);
        if (watchdog is int declaredWatchdog)
        {
            if (++count > 8192 || clock.Elapsed.TotalSeconds >= 2) throw new IOException("Linux process inventory bound exceeded.");
            ObserveOwned(declaredWatchdog);
        }
        // Include the priority lookup in the original8192/shared2s inventory
        // bound. A repeated PID may be reobserved; owned members are counted once.
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            scanBudget?.Check();
            if (++count > 8192 || clock.Elapsed.TotalSeconds >= 2) throw new IOException("Linux process inventory bound exceeded.");
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
            ObserveOwned(pid);
        }
        scanBudget?.Check();
        return result.ToArray();
    }
    internal static (int Parent, int Group, ulong Start, string State)? TryStat(int pid, Budget? budget = null)
    {
        budget?.Check();
        try
        {
            var text = ReadMetadata("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat", budget);
            var parsed = ProcessObserver.ParseLinuxStat(text, pid);
            var fields = text[(text.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var group)) throw new IOException("Invalid process group field.");
            budget?.Check();
            return (parsed.ParentPid, group, parsed.StartTicks, parsed.State);
        }
        catch (FileNotFoundException) { budget?.Check(); return null; }
        catch (DirectoryNotFoundException) { budget?.Check(); return null; }
    }
    private bool HasOwnershipMarker(int pid, Budget? budget = null)
    {
        var scope = ExecutionScope(pid, budget);
        return scope is not null && ownershipScopes.Contains(scope);
    }
    internal static string? ExecutionScope(int pid, Budget? budget = null)
    {
        budget?.Check();
        // Only the task marker is returned. No inherited credential/environment
        // values enter receipts, errors, command lines or public results.
        try
        {
            var entries = ReadMetadata("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/environ", budget).Split('\0');
            foreach (var entry in entries)
            {
                budget?.Check();
                if (entry.StartsWith("SEZIKA_EXECUTION_ID=", StringComparison.Ordinal) && Guid.TryParseExact(entry[20..], "N", out _)) return entry[20..];
            }
            return null;
        }
        catch (IOException) { budget?.Check(); return null; }
        catch (UnauthorizedAccessException) { budget?.Check(); return null; }
    }
    internal static string ReadMetadata(string path, Budget? budget = null)
    {
        budget?.Check();
        using var source = File.OpenRead(path); using var output = new MemoryStream();
        var buffer = new byte[1024]; var clock = Stopwatch.StartNew();
        for (var block = 0; block < 65 && clock.Elapsed.TotalSeconds < 1; block++)
        {
            budget?.Check();
            var size = source.Read(buffer);
            budget?.Check();
            if (size == 0) return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
            if (output.Length + size > 65536) throw new IOException("Linux process metadata exceeds64KiB.");
            output.Write(buffer, 0, size);
        }
        throw new IOException("Linux process metadata read bound exceeded.");
    }
    internal static ProcessObservation ObserveRegistrationCandidate(ProcessObservation first, string? inheritedCommandLine, Budget budget)
    {
        budget.Check();
        if (first.State != "Present" || (!string.IsNullOrEmpty(first.CommandLine) && first.CommandLine != inheritedCommandLine) || first.StartTicks is null ||
            string.IsNullOrEmpty(first.BootId) || first.ParentPid <= 0) return first;
        var clock = Stopwatch.StartNew();
        var scope = ExecutionScope(first.Pid, budget);
        if (scope is null) return first;
        var actual = first;
        // One shared100ms deadline, at most20 observations and5ms cancellable
        // backoff. Empty argv and the exact live parent's pre-exec inherited
        // argv never establish a child identity or acquire its pidfd.
        for (var attempt = 0; attempt < 20 && clock.Elapsed.TotalMilliseconds < 100; attempt++)
        {
            budget.Check();
            var pause = Math.Min(5, (int)(100 - clock.Elapsed.TotalMilliseconds));
            if (pause <= 0) break;
            if (budget.Cancellation.WaitHandle.WaitOne(pause)) budget.Check();
            if (clock.Elapsed.TotalMilliseconds >= 100) break;
            actual = ProcessObserver.Observe(first.Pid, budget);
            budget.Check();
            if (actual.State != "Present") return actual;
            if (actual.ParentPid != first.ParentPid || actual.StartTicks != first.StartTicks || actual.BootId != first.BootId ||
                ExecutionScope(first.Pid, budget) != scope)
                return actual with { State = "Unknown", Error = "Native Linux birth/parent/boot/scope changed during bounded argv reobservation." };
            if (clock.Elapsed.TotalMilliseconds >= 100)
                return actual with { State = "Unknown", Error = "Native Linux argv reobservation exceeded its shared100ms deadline." };
            if (!string.IsNullOrEmpty(actual.CommandLine) && actual.CommandLine != inheritedCommandLine) return actual;
        }
        return actual with { State = "Unknown", Error = "Native Linux argv remained empty or exactly inherited at the shared100ms admission deadline." };
    }
    public void Register(ExecutionIdentity identity) => Register(identity, null);

    internal void Register(ExecutionIdentity identity, Budget? budget)
    {
        budget?.Check();
        if (pidFds.ContainsKey(identity.Pid))
        {
            if (!RetainsBirth(identity)) throw new IOException("Registered pidfd birth identity differs.");
            BindWatchdogRole(identity, budget);
            return;
        }
        if (identity.Platform != "linux" || identity.StartTicks is null || string.IsNullOrEmpty(identity.BootId) ||
            identity.ParentPid <= 0 || identity.CommandLine.Length == 0) throw new IOException("Complete native birth/parent/argv required before pidfd registration.");
        var opened = ExecutionNative.PidFdOpen(434, identity.Pid, 0);
        if (opened < 0) throw Error("pidfd_open");
        var handle = new ExecutionFd(checked((int)opened));
        var transferred = false;
        try
        {
            budget?.Check();
            var current = TryStat(identity.Pid, budget);
            budget?.Check();
            if (current is null || current.Value.Start != identity.StartTicks)
                throw new IOException("Identity changed while acquiring pidfd.");
            pidFds.Add(identity.Pid, handle);
            transferred = true;
            registeredBirths.Add(identity.Pid, (identity.StartTicks.Value, identity.BootId, identity.ParentPid, identity.CommandLine));
            births[identity.Pid] = identity.StartTicks!.Value;
            if (identity.ScopeId is not null) ownershipScopes.Add(identity.ScopeId);
            BindWatchdogRole(identity, budget);
        }
        finally
        {
            if (!transferred) handle.Dispose();
        }
    }
    internal bool ReadyForRegistration(ExecutionIdentity identity, Budget? budget = null)
    {
        budget?.Check();
        return identity.CommandLine != watchdogCommandLine || MatchesDeclaredWatchdog(identity, budget);
    }
    private void BindWatchdogRole(ExecutionIdentity identity, Budget? budget = null)
    {
        budget?.Check();
        if (identity.CommandLine != watchdogCommandLine) return;
        if (!RetainsBirth(identity) || !MatchesDeclaredWatchdog(identity, budget))
            throw new IOException("Declared watchdog native birth/parent/argv/scope and retained pidfd are not confirmed.");
        // Ownership is fixed before the first immutable registration snapshot.
        identity.Ownership = "linux-watchdog";
    }
    private bool MatchesDeclaredWatchdog(ExecutionIdentity identity, Budget? budget = null)
    {
        budget?.Check();
        if (WatchdogPid(budget) != identity.Pid || identity.ParentPid != Pid || identity.ScopeId != ownershipId ||
            identity.CommandLine != watchdogCommandLine) return false;
        var actual = ProcessObserver.Observe(identity.Pid, budget);
        return actual.State == "Present" && actual.StartTicks == identity.StartTicks && actual.BootId == identity.BootId &&
            actual.ParentPid == Pid && actual.CommandLine == watchdogCommandLine && ExecutionScope(identity.Pid, budget) == ownershipId;
    }
    internal bool RetainsBirth(ExecutionIdentity identity) => pidFds.ContainsKey(identity.Pid) && registeredBirths.TryGetValue(identity.Pid, out var pin) &&
        identity.Platform == "linux" && identity.StartTicks == pin.Ticks && identity.BootId == pin.Boot && identity.ParentPid == pin.Parent &&
        identity.CommandLine == pin.Command && pin.Command.Length > 0;
    internal bool AdmissionPublished(ExecutionIdentity identity, bool mark = false)
    {
        if (!RetainsBirth(identity)) return false;
        if (mark) publishedAdmissions.Add(identity.Pid);
        return publishedAdmissions.Contains(identity.Pid);
    }
    internal bool HasExitedBirth(ExecutionIdentity identity, Budget? budget = null)
    {
        budget?.Check();
        if (!RetainsBirth(identity)) return false;
        try { if (ReadMetadata("/proc/sys/kernel/random/boot_id", budget).Trim() != identity.BootId) return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        var descriptor = new ExecutionNative.PollFd { Descriptor = pidFds[identity.Pid].Value, Events = 1 }; // POLLIN
        budget?.Check();
        var observed = ExecutionNative.poll(ref descriptor, 1, 0);
        budget?.Check();
        if (observed < 0 || (descriptor.Returned & (8 | 32)) != 0) return false; // POLLERR/POLLNVAL never prove exit
        // The retained descriptor was pinned while native birth/parent/argv
        // were complete. POLLIN proves termination; POLLHUP proves reaping.
        return observed > 0 && (descriptor.Returned & (1 | 16)) != 0;
    }
    public void Terminate()
    {
        // Group ID is reserved by the unreaped root. pidfds also cover registered
        // descendants which deliberately leave the original process group.
        if (!reaped && ExecutionNative.kill(-Pid, 9) != 0 && Marshal.GetLastPInvokeError() != 3) throw Error("kill(process-group)");
        var watchdog = WatchdogPid();
        foreach (var pair in pidFds)
            if (pair.Key != watchdog && ExecutionNative.PidFdSignal(424, pair.Value.Value, 9, 0, 0) < 0 && Marshal.GetLastPInvokeError() != 3)
                throw Error("pidfd_send_signal");
    }
    public bool Reap()
    {
        var clock = Stopwatch.StartNew();
        var watchdog = WatchdogPid();
        foreach (var pid in known.Take(maximumProcesses).Where(pid => pid != Pid && pid != watchdog))
        {
            if (clock.Elapsed.TotalSeconds >= 2) throw new IOException("Reaping inventory deadline exceeded.");
            var stat = TryStat(pid);
            if (stat is null) continue;
            if (births.TryGetValue(pid, out var born) && stat.Value.Start != born) continue;
            if (stat.Value.State is not ("Z" or "X")) return false;
            var actual = ExecutionNative.waitpid(pid, out _, 1);
            if (actual < 0 && Marshal.GetLastPInvokeError() != 10) throw Error("waitpid(descendant)");
            if (TryStat(pid) is not null) return false;
        }
        if (!reaped)
        {
            var actual = ExecutionNative.waitpid(Pid, out _, 1);
            if (actual != Pid) return false;
            reaped = true;
        }
        if (watchdog is not null)
        {
            var stat = TryStat(watchdog.Value);
            if (stat is not null)
            {
                if (stat.Value.State is not ("Z" or "X")) return false;
                var result = ExecutionNative.waitpid(watchdog.Value, out _, 1);
                if (result < 0 && Marshal.GetLastPInvokeError() != 10) throw Error("waitpid(watchdog)");
                if (TryStat(watchdog.Value) is not null) return false;
            }
            var receiptPath = Path.Combine(outputDirectory, "watchdog-result.json");
            if (!File.Exists(receiptPath)) throw new IOException("Watchdog exited without a termination receipt.");
            if (new FileInfo(receiptPath).Length > 65536) throw new IOException("Watchdog receipt byte bound exceeded.");
            var receipt = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllBytes(receiptPath), ExecutionJsonContext.Default.WatchdogResult);
            if (receipt?.Status != "TERMINATED_PENDING_PARENT_REAP" || receipt.RemainingLive != 0)
                throw new IOException("Watchdog termination inventory remains UNKNOWN.");
        }
        if (Members().Length != 0) return false;
        if (!restored)
        {
            if (ExecutionNative.prctl(36, (nuint)priorSubreaper, 0, 0, 0) != 0) throw Error("Restore child-subreaper state");
            restored = true;
        }
        return true;
    }
    private int? WatchdogPid(Budget? budget = null)
    {
        budget?.Check();
        var path = Path.Combine(outputDirectory, "watchdog.pid");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 32) throw new IOException("Watchdog PID metadata bound.");
        var text = budget is null ? File.ReadAllText(path) : ReadMetadata(path, budget);
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0 ? pid : null;
    }
    internal void AcknowledgeWatchdog(IReadOnlyDictionary<int, ExecutionIdentity> records, Budget? budget = null)
    {
        budget?.Check();
        var pid = WatchdogPid(budget);
        if (pid is null || !records.TryGetValue(pid.Value, out var identity) || !RetainsBirth(identity) ||
            identity.Ownership != "linux-watchdog" || !MatchesDeclaredWatchdog(identity, budget)) return;
        var path = Path.Combine(outputDirectory, "watchdog-registered");
        if (File.Exists(path)) return;
        using (var file = new FileStream(path + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { file.Write(Encoding.ASCII.GetBytes(pid.Value.ToString(CultureInfo.InvariantCulture))); file.Flush(true); }
        File.Move(path + ".pending", path);
    }
    internal bool PrepareTermination(IReadOnlyDictionary<int, ExecutionIdentity> records, Budget budget)
    {
        budget.Check(); cleanupAdmission ??= Stopwatch.StartNew(); AncestorAdmission.Cancel(outputDirectory, ownershipId, budget);
        var watchdog = WatchdogPid(budget);
        if (watchdog is not null && File.Exists(Path.Combine(outputDirectory, "watchdog.ready")) &&
            records.TryGetValue(watchdog.Value, out var identity) && RetainsBirth(identity) && identity.ScopeId == ownershipId &&
            identity.CommandLine == watchdogCommandLine && identity.Ownership == "linux-watchdog") return true;
        var spawning = Path.Combine(outputDirectory, "watchdog.spawning");
        if (watchdog is null && (!File.Exists(spawning) || File.Exists(Path.Combine(outputDirectory, "watchdog.not-spawned")))) return true;
        if (cleanupAdmission.Elapsed.TotalSeconds >= 2 || budget.RemainingSeconds <= .1)
            throw new IOException("UNKNOWN watchdog ancestor admission before the shared cleanup deadline.");
        return false;
    }
    public void Dispose()
    {
        input?.Dispose(); output?.Dispose(); error?.Dispose();
        foreach (var handle in pidFds.Values) handle.Dispose();
        if (!restored)
        {
            // The process-wide subreaper setting is changed only under the
            // executor's serial lease, then restored on every exit path.
            ExecutionNative.prctl(36, (nuint)priorSubreaper, 0, 0, 0); restored = true;
        }
    }
    private static void Check(int error, string operation) { if (error != 0) throw new Win32Exception(error, operation); }
    private static Win32Exception Error(string operation) => new(Marshal.GetLastPInvokeError(), operation);
}
