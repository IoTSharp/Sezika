using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Sezika.Tooling;

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
    private readonly HashSet<int> known = [];
    private readonly Dictionary<int, ulong> births = [];
    private readonly int maximumProcesses;
    private readonly HashSet<string> ownershipScopes = new(StringComparer.Ordinal);
    private readonly string gate;
    private readonly string outputDirectory;
    private readonly int priorSubreaper;
    private bool restored, reaped;
    public int Pid { get; private set; }
    public string Ownership => "linux-process-group-pidfd-subreaper";
    internal LinuxExecutionSession(ExecutionRequest request, Dictionary<string, string> environment, string ownershipId, string gate, string config)
    {
        if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Linux execution supports x64/arm64 libc ABIs.");
        maximumProcesses = request.MaximumProcesses; this.gate = gate; outputDirectory = request.OutputDirectory;
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
    public int[] Members()
    {
        var result = new List<int>(); var clock = Stopwatch.StartNew(); var count = 0;
        // /proc metadata enumeration only: <=8192 entries and a shared2s wall
        // bound. Executable/tool discovery never uses this scan.
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (++count > 8192 || clock.Elapsed.TotalSeconds >= 2) throw new IOException("Linux process inventory bound exceeded.");
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
            var stat = TryStat(pid);
            if (stat is null) continue;
            if (births.TryGetValue(pid, out var birth) && birth != stat.Value.Start) continue;
            if (pid == Pid || known.Contains(pid) || stat.Value.Group == Pid || known.Contains(stat.Value.Parent) ||
                (stat.Value.Parent == Environment.ProcessId && HasOwnershipMarker(pid)))
            {
                if (result.Count >= maximumProcesses) throw new IOException("Linux owned-process count exceeded.");
                result.Add(pid); known.Add(pid); births.TryAdd(pid, stat.Value.Start);
            }
        }
        return result.ToArray();
    }
    internal static (int Parent, int Group, ulong Start, string State)? TryStat(int pid)
    {
        try
        {
            var text = ReadMetadata("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat");
            var parsed = ProcessObserver.ParseLinuxStat(text, pid);
            var fields = text[(text.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var group)) throw new IOException("Invalid process group field.");
            return (parsed.ParentPid, group, parsed.StartTicks, parsed.State);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    private bool HasOwnershipMarker(int pid)
    {
        var scope = ExecutionScope(pid);
        return scope is not null && ownershipScopes.Contains(scope);
    }
    internal static string? ExecutionScope(int pid)
    {
        // Only the task marker is returned. No inherited credential/environment
        // values enter receipts, errors, command lines or public results.
        try
        {
            var entries = ReadMetadata("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/environ").Split('\0');
            foreach (var entry in entries)
                if (entry.StartsWith("SEZIKA_EXECUTION_ID=", StringComparison.Ordinal) && Guid.TryParseExact(entry[20..], "N", out _)) return entry[20..];
            return null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    internal static string ReadMetadata(string path)
    {
        using var source = File.OpenRead(path); using var output = new MemoryStream();
        var buffer = new byte[1024]; var clock = Stopwatch.StartNew();
        for (var block = 0; block < 65 && clock.Elapsed.TotalSeconds < 1; block++)
        {
            var size = source.Read(buffer);
            if (size == 0) return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
            if (output.Length + size > 65536) throw new IOException("Linux process metadata exceeds64KiB.");
            output.Write(buffer, 0, size);
        }
        throw new IOException("Linux process metadata read bound exceeded.");
    }
    public void Register(ExecutionIdentity identity)
    {
        if (pidFds.ContainsKey(identity.Pid)) return;
        var opened = ExecutionNative.PidFdOpen(434, identity.Pid, 0);
        if (opened < 0) throw Error("pidfd_open");
        var handle = new ExecutionFd(checked((int)opened));
        var current = TryStat(identity.Pid);
        if (current is null || current.Value.Start != identity.StartTicks)
        {
            handle.Dispose(); throw new IOException("Identity changed while acquiring pidfd.");
        }
        pidFds.Add(identity.Pid, handle);
        births[identity.Pid] = identity.StartTicks!.Value;
        if (identity.ScopeId is not null) ownershipScopes.Add(identity.ScopeId);
        if (identity.Pid == WatchdogPid()) identity.Ownership = "linux-watchdog";
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
    private int? WatchdogPid()
    {
        var path = Path.Combine(outputDirectory, "watchdog.pid");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 32) throw new IOException("Watchdog PID metadata bound.");
        return int.TryParse(File.ReadAllText(path), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0 ? pid : null;
    }
    internal void AcknowledgeWatchdog()
    {
        var pid = WatchdogPid();
        if (pid is null || !pidFds.ContainsKey(pid.Value)) return;
        var path = Path.Combine(outputDirectory, "watchdog-registered");
        if (File.Exists(path)) return;
        using (var file = new FileStream(path + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { file.Write(Encoding.ASCII.GetBytes(pid.Value.ToString(CultureInfo.InvariantCulture))); file.Flush(true); }
        File.Move(path + ".pending", path);
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
