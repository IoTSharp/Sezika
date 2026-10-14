using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Sezika.Tooling;

// A separate C# process survives the payload and its original supervisor. It
// only terminates authenticated ownership; the living subreaper or init still
// has to reap zombies. This file never reports reaping as proven by a signal.
internal static class LinuxWatchdog
{
    internal static unsafe void Start(BrokerConfiguration broker, Budget budget, CancellationToken cancellation)
    {
        budget.Check();
        AncestorAdmission.RequireActive(broker.OutputDirectory, broker.OwnershipId, budget);
        ProcessExecutor.ValidateExecutable(broker.WatchdogExecutable, budget);
        var observation = ProcessObserver.Observe(Environment.ProcessId);
        if (observation.State != "Present" || observation.StartTicks is null || observation.ParentPid != broker.ParentPid)
            throw new IOException("Watchdog root identity is incomplete.");
        var config = new WatchdogConfiguration
        {
            Broker = broker,
            Root = new() { Pid = observation.Pid, Platform = "linux", ParentPid = observation.ParentPid, StartTicks = observation.StartTicks,
                BootId = observation.BootId, CommandLine = observation.CommandLine!, ParentChain = [broker.ParentPid], Ownership = "linux-root", ScopeId = broker.OwnershipId }
        };
        var configuration = Path.Combine(broker.OutputDirectory, "watchdog.json");
        Write(configuration, JsonSerializer.SerializeToUtf8Bytes(config, ExecutionJsonContext.Default.WatchdogConfiguration));
        using var nullFile = new FileStream("/dev/null", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var descriptor = checked((int)nullFile.SafeFileHandle.DangerousGetHandle());
        var actions = stackalloc byte[512]; var attributes = stackalloc byte[512];
        new Span<byte>(actions, 512).Clear(); new Span<byte>(attributes, 512).Clear();
        var actionsReady = false; var attributesReady = false; var spawned = false;
        Write(Path.Combine(broker.OutputDirectory, "watchdog.spawning"), Encoding.ASCII.GetBytes(broker.OwnershipId));
        try
        {
            Check(ExecutionNative.posix_spawn_file_actions_init(actions)); actionsReady = true;
            Check(ExecutionNative.posix_spawnattr_init(attributes)); attributesReady = true;
            Check(ExecutionNative.posix_spawnattr_setflags(attributes, 2)); Check(ExecutionNative.posix_spawnattr_setpgroup(attributes, 0));
            for (var target = 0; target < 3; target++) Check(ExecutionNative.posix_spawn_file_actions_adddup2(actions, descriptor, target));
            Check(ExecutionNative.posix_spawn_file_actions_addclose(actions, descriptor));
            using var argv = new NativeUtf8Vector(new[] { broker.WatchdogExecutable }.Concat(broker.WatchdogArgumentPrefix).Append(configuration));
            using var env = new NativeUtf8Vector(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
                .Where(e => (string)e.Key != "SEZIKA_START_GATE").Select(e => (string)e.Key + "=" + (string?)e.Value));
            fixed (nint* arguments = argv.Pointers)
            fixed (nint* environment = env.Pointers)
            {
                AncestorAdmission.RequireActive(broker.OutputDirectory, broker.OwnershipId, budget);
                Check(ExecutionNative.posix_spawn(out var pid, broker.WatchdogExecutable, actions, attributes, arguments, environment));
                spawned = true;
                Write(Path.Combine(broker.OutputDirectory, "watchdog.pid"), Encoding.ASCII.GetBytes(pid.ToString(CultureInfo.InvariantCulture)));
            }
        }
        catch
        {
            if (!spawned) Write(Path.Combine(broker.OutputDirectory, "watchdog.not-spawned"), Encoding.ASCII.GetBytes(broker.OwnershipId));
            throw;
        }
        finally
        {
            if (actionsReady) ExecutionNative.posix_spawn_file_actions_destroy(actions);
            if (attributesReady) ExecutionNative.posix_spawnattr_destroy(attributes);
        }
        var clock = Stopwatch.StartNew();
        for (var poll = 0; poll < 100 && clock.Elapsed.TotalSeconds < 2; poll++)
        {
            budget.Check(); cancellation.ThrowIfCancellationRequested();
            if (File.Exists(Path.Combine(broker.OutputDirectory, "watchdog.ready")) && File.Exists(Path.Combine(broker.OutputDirectory, "watchdog-registered"))) return;
            if (cancellation.WaitHandle.WaitOne(20)) cancellation.ThrowIfCancellationRequested();
        }
        throw new TimeoutException("Watchdog readiness exceeded100 polls/2 seconds.");
    }

    internal static int Run(string configurationPath, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux ownership watchdog.");
        var errors = new List<string>(); var rootExitProofs = new List<string>(); var clock = Stopwatch.StartNew();
        var configPath = Path.GetFullPath(configurationPath);
        if (new FileInfo(configPath).Length > 262144) throw new InvalidDataException("Watchdog configuration byte bound exceeded.");
        var config = JsonSerializer.Deserialize(File.ReadAllBytes(configPath), ExecutionJsonContext.Default.WatchdogConfiguration) ?? throw new InvalidDataException("Missing watchdog configuration.");
        var broker = config.Broker;
        if (broker.OwnershipId != Environment.GetEnvironmentVariable("SEZIKA_EXECUTION_ID") || broker.MaximumSeconds is < 1 or > 86400 ||
            broker.CleanupSeconds is < 1 or > 30 || broker.MaximumProcesses is < 1 or > 256 || config.Root is null || config.Root.StartTicks is null)
            throw new InvalidDataException("Watchdog ownership or bounds differ.");
        if (ExecutionNative.prctl(1, 0, 0, 0, 0) != 0) throw new IOException("Watchdog must survive parent death.");
        var result = new WatchdogResult { Pid = Environment.ProcessId };
        var descriptors = new Dictionary<int, ExecutionFd>();
        var identities = new Dictionary<int, ExecutionIdentity> { [config.Root.Pid] = config.Root };
        ExecutionFd? parent = null;
        var ready = false;
        try
        {
            var parentDescriptor = ExecutionNative.PidFdOpen(434, broker.ParentPid, 0);
            if (parentDescriptor >= 0)
            {
                parent = new(checked((int)parentDescriptor));
                var actual = LinuxExecutionSession.TryStat(broker.ParentPid);
                if (actual is null || actual.Value.Start != broker.Launcher?.StartTicks) { parent.Dispose(); parent = null; }
            }
            Register(config.Root, descriptors, requireNativeRoot: true);
            rootExitProofs.Add("FULL_NATIVE_ROOT_PINNED pid=" + config.Root.Pid + ";ticks=" + config.Root.StartTicks + ";boot=" + config.Root.BootId +
                ";parent=" + config.Root.ParentPid + ";scope=" + config.Root.ScopeId + ";argv_sha256=" +
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(config.Root.CommandLine))));
            ready = true;
            var admitted = false;
            try { admitted = AncestorAdmission.Wait(new Budget(5, null, cancellation), cancellation, "watchdog",
                () => parent is null || Exited(parent) || Exited(descriptors[config.Root.Pid])); }
            catch (Exception admission) when (admission is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or TimeoutException or OperationCanceledException)
            { result.Reason = "ANCESTOR_ADMISSION_FAILED"; errors.Add(admission.GetType().Name + ": " + admission.Message); }
            if (admitted)
                Write(Path.Combine(broker.OutputDirectory, "watchdog.ready"), Encoding.ASCII.GetBytes(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)));
            // <=864,051 polls AND<=86,405 seconds,100ms backoff and cancellation.
            for (var poll = 0; poll < (broker.MaximumSeconds + 5) * 10 + 1 && clock.Elapsed.TotalSeconds < broker.MaximumSeconds + 5 && result.Reason.Length == 0; poll++)
            {
                if (cancellation.IsCancellationRequested) { result.Reason = "WATCHDOG_CANCELLED"; break; }
                if (parent is null || Exited(parent)) { result.Reason = "SUPERVISOR_EXITED"; break; }
                if (Exited(descriptors[config.Root.Pid], out var events))
                { result.Reason = "ROOT_EXITED"; RootExitProof(config.Root, events, 0, "root-exit-poll", rootExitProofs); break; }
                if (cancellation.WaitHandle.WaitOne(100)) { result.Reason = "WATCHDOG_CANCELLED"; break; }
            }
            if (result.Reason.Length == 0) result.Reason = "WATCHDOG_DEADLINE";
            var cleanup = Stopwatch.StartNew();
            for (var pass = 0; pass < broker.CleanupSeconds * 10 + 1 && cleanup.Elapsed.TotalSeconds < broker.CleanupSeconds; pass++)
            {
                var stable = false;
                for (var discovery = 0; discovery < Math.Min(64, broker.MaximumProcesses) && cleanup.Elapsed.TotalSeconds < broker.CleanupSeconds; discovery++)
                {
                    var before = descriptors.Count;
                    Discover(config, identities, descriptors, errors, rootExitProofs, cleanup);
                    if (discovery > 0 && before == descriptors.Count) { stable = true; break; }
                }
                if (!stable) throw new IOException("Stopped ownership inventory did not stabilize within count/time bounds.");
                // No kill(-old-pgid) here: init may have already reaped root.
                // Only retained pidfds are signalled, so PGID/PID reuse cannot
                // redirect termination to an unrelated current process group.
                foreach (var pair in descriptors)
                {
                    if (pair.Key == Environment.ProcessId) continue;
                    if (ExecutionNative.PidFdSignal(424, pair.Value.Value, 9, 0, 0) < 0 && Marshal.GetLastPInvokeError() != 3)
                        errors.Add("pidfd termination failed: " + pair.Key);
                }
                result.RemainingLive = 0; result.ZombiesPendingReap = 0;
                foreach (var identity in identities.Values)
                {
                    var actual = RegisteredRootStat(config, descriptors, identity.Pid, "remaining-inventory", rootExitProofs);
                    if (actual is null || actual.Value.Start != identity.StartTicks) continue;
                    if (actual.Value.State is "Z" or "X") result.ZombiesPendingReap++; else result.RemainingLive++;
                }
                if (result.RemainingLive == 0) { result.Status = errors.Count == 0 ? "TERMINATED_PENDING_PARENT_REAP" : "UNKNOWN"; break; }
                Thread.Sleep(100);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or TimeoutException or OperationCanceledException)
        { errors.Add(error.GetType().Name + ": " + error.Message); }
        finally
        {
            parent?.Dispose(); foreach (var descriptor in descriptors.Values) descriptor.Dispose();
            result.Errors = errors.Distinct(StringComparer.Ordinal).ToArray();
            result.RootExitProofs = rootExitProofs.Distinct(StringComparer.Ordinal).ToArray();
            if (ready) Write(Path.Combine(broker.OutputDirectory, "watchdog-result.json"), JsonSerializer.SerializeToUtf8Bytes(result, ExecutionJsonContext.Default.WatchdogResult));
        }
        return result.Status == "TERMINATED_PENDING_PARENT_REAP" ? 0 : 1;
    }

    private static void Discover(WatchdogConfiguration config, Dictionary<int, ExecutionIdentity> identities, Dictionary<int, ExecutionFd> descriptors,
        List<string> errors, List<string> rootExitProofs, Stopwatch cleanup)
    {
        var count = 0; var scan = Stopwatch.StartNew();
        foreach (var path in Directory.EnumerateFiles(config.Broker.OutputDirectory, "registered*.json", SearchOption.TopDirectoryOnly))
        {
            if (++count > 257 || scan.Elapsed.TotalSeconds >= 1 || cleanup.Elapsed.TotalSeconds >= config.Broker.CleanupSeconds) throw new IOException("Watchdog receipt inventory bound.");
            if (new FileInfo(path).Length > 1_048_576) throw new IOException("Watchdog identity receipt byte bound.");
            try
            {
                var records = JsonSerializer.Deserialize(File.ReadAllBytes(path), ExecutionJsonContext.Default.ExecutionIdentityArray) ?? [];
                if (records.Length > config.Broker.MaximumProcesses) throw new IOException("Watchdog registered-process bound.");
                foreach (var record in records)
                    if (record.Pid != Environment.ProcessId && !identities.ContainsKey(record.Pid)) identities.Add(record.Pid, record);
            }
            catch (JsonException) { errors.Add("Incomplete identity receipt."); }
        }
        count = 0;
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (++count > 8192 || scan.Elapsed.TotalSeconds >= 2 || cleanup.Elapsed.TotalSeconds >= config.Broker.CleanupSeconds) throw new IOException("Watchdog process inventory bound.");
            if (!int.TryParse(Path.GetFileName(directory), out var pid) || pid == Environment.ProcessId) continue;
            var stat = RegisteredRootStat(config, descriptors, pid, "discovery-stat", rootExitProofs);
            if (stat is null) continue;
            var alreadyKnown = identities.TryGetValue(pid, out var recorded) && stat.Value.Start == recorded.StartTicks;
            if (!alreadyKnown)
            {
                if (stat.Value.State is "Z" or "X") continue;
                string environment;
                try { environment = LinuxExecutionSession.ReadMetadata(directory + "/environ"); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                catch (UnauthorizedAccessException) { if (stat.Value.Group == config.Root.Pid) errors.Add("Unobservable group member " + pid); continue; }
                catch (IOException) { if (stat.Value.Group == config.Root.Pid) errors.Add("Unreadable group member metadata " + pid); continue; }
                var scopes = identities.Values.Select(record => record.ScopeId).Where(scope => scope is not null).Append(config.Broker.OwnershipId).ToHashSet(StringComparer.Ordinal);
                if (!environment.Split('\0').Any(entry => entry.StartsWith("SEZIKA_EXECUTION_ID=", StringComparison.Ordinal) && scopes.Contains(entry[20..]))) continue;
                var actual = ProcessObserver.Observe(pid);
                if (actual.State != "Present") { errors.Add("Unobservable owned member " + pid); continue; }
                if (identities.Count >= config.Broker.MaximumProcesses) throw new IOException("Watchdog owned-process count exceeded.");
                var chain = identities.TryGetValue(actual.ParentPid, out var ancestor) ? ancestor.ParentChain.Append(ancestor.Pid).ToArray() : [];
                var parentStat = ancestor is null ? null : LinuxExecutionSession.TryStat(ancestor.Pid);
                if (chain.Length == 0 || parentStat is null || parentStat.Value.Start != ancestor!.StartTicks)
                {
                    errors.Add("UNKNOWN unregistered inherited member parent chain " + pid);
                    continue;
                }
                identities[pid] = new() { Pid = pid, Platform = "linux", ParentPid = actual.ParentPid, ParentChain = chain,
                    StartTicks = actual.StartTicks, BootId = actual.BootId, CommandLine = actual.CommandLine!, Ownership = "watchdog-inherited-marker" };
            }
            if (!descriptors.ContainsKey(pid) && stat.Value.State is not ("Z" or "X")) Register(identities[pid], descriptors);
            if (descriptors.TryGetValue(pid, out var descriptor) && stat.Value.State is not ("Z" or "X"))
                if (ExecutionNative.PidFdSignal(424, descriptor.Value, 19, 0, 0) < 0 && Marshal.GetLastPInvokeError() != 3)
                    errors.Add("pidfd stop failed: " + pid);
        }
    }
    private static void Register(ExecutionIdentity identity, Dictionary<int, ExecutionFd> descriptors, bool requireNativeRoot = false)
    {
        if (requireNativeRoot)
        {
            var actual = ProcessObserver.Observe(identity.Pid);
            if (identity.Platform != "linux" || identity.StartTicks is null || !Guid.TryParse(identity.BootId, out _) || identity.ParentPid <= 0 ||
                identity.ParentChain is null || identity.ParentChain.Length is < 1 or > 64 || identity.ParentChain[^1] != identity.ParentPid ||
                string.IsNullOrEmpty(identity.CommandLine) || !Guid.TryParseExact(identity.ScopeId, "N", out _) || actual.State != "Present" ||
                actual.StartTicks != identity.StartTicks || actual.BootId != identity.BootId || actual.ParentPid != identity.ParentPid ||
                actual.CommandLine != identity.CommandLine || LinuxExecutionSession.ExecutionScope(identity.Pid) != identity.ScopeId)
                throw new IOException("Watchdog root complete native birth/parent/argv/scope differs before retained pidfd registration.");
        }
        var opened = ExecutionNative.PidFdOpen(434, identity.Pid, 0);
        if (opened < 0) throw new IOException("Watchdog pidfd acquisition failed.");
        var descriptor = new ExecutionFd(checked((int)opened));
        var stat = LinuxExecutionSession.TryStat(identity.Pid);
        if (stat is null || stat.Value.Start != identity.StartTicks) { descriptor.Dispose(); throw new IOException("Watchdog PID birth changed while acquiring pidfd."); }
        descriptors.Add(identity.Pid, descriptor);
    }
    private static (int Parent, int Group, ulong Start, string State)? RegisteredRootStat(WatchdogConfiguration config,
        Dictionary<int, ExecutionFd> descriptors, int pid, string stage, List<string> proofs)
    {
        try { return LinuxExecutionSession.TryStat(pid); }
        catch (IOException error) when (error.HResult == 3 && pid == config.Root.Pid && descriptors.TryGetValue(pid, out var retained) && Exited(retained))
        {
            // ESRCH is native errno3 in IOException.HResult. Only this exact
            // fully registered root's retained pidfd proves termination here;
            // it never proves the independent parent's reaping completed.
            _ = Exited(descriptors[pid], out var events); RootExitProof(config.Root, events, error.HResult, stage, proofs);
            return null;
        }
    }
    private static void RootExitProof(ExecutionIdentity root, short events, int errno, string stage, List<string> proofs)
    {
        if (proofs.Count >= 16) return;
        var proof = "REGISTERED_ROOT_EXIT_NOT_PARENT_REAP_PROOF stage=" + stage + ";pid=" + root.Pid + ";ticks=" + root.StartTicks +
            ";boot=" + root.BootId + ";parent=" + root.ParentPid + ";scope=" + root.ScopeId + ";pidfd_events=" + events + ";errno=" + errno;
        if (!proofs.Contains(proof, StringComparer.Ordinal)) proofs.Add(proof);
    }
    private static bool Exited(ExecutionFd descriptor) => Exited(descriptor, out _);
    private static bool Exited(ExecutionFd descriptor, out short events)
    {
        var poll = new ExecutionNative.PollFd { Descriptor = descriptor.Value, Events = 1 };
        var result = ExecutionNative.poll(ref poll, 1, 0);
        if (result < 0) throw new IOException("Watchdog pidfd poll failed.");
        if ((poll.Returned & (8 | 32)) != 0) throw new IOException("Watchdog pidfd poll is invalid or failed.");
        events = checked((short)(poll.Returned & (1 | 16)));
        return result > 0 && events != 0;
    }
    private static void Write(string path, byte[] bytes)
    {
        var pending = path + ".pending";
        using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { file.Write(bytes); file.Flush(true); }
        File.Move(pending, path);
    }
    private static void Check(int error) { if (error != 0) throw new IOException("Watchdog posix_spawn failed: " + error); }
}
