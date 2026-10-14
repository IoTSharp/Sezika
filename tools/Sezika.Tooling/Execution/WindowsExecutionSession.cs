using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Sezika.Tooling;

internal interface IExecutionSession : IDisposable
{
    int Pid { get; }
    void Release();
    bool TryExit(out int exitCode);
    int ReadOutput(bool error, byte[] buffer);
    Task WriteInputAsync(byte[] bytes, CancellationToken cancellation);
    int[] Members();
    void Register(ExecutionIdentity identity);
    void Terminate();
    bool Reap();
    string Ownership { get; }
}

internal sealed class WindowsExecutionSession : IExecutionSession
{
    private readonly ExecutionHandle job;
    private ExecutionHandle? process, thread, output, error, input;
    private readonly int maximumProcesses;
    private readonly bool hiddenConsole;
    private readonly Dictionary<int, ExecutionHandle> registered = [];
    private readonly HashSet<int> consoleHosts = [];
    private WindowsProcessNotifications? notifications;
    public int Pid { get; private set; }
    public string Ownership => "windows-job";
    internal WindowsExecutionSession(ExecutionRequest request, Dictionary<string, string> environment)
    {
        maximumProcesses = request.MaximumProcesses;
        hiddenConsole = request.WindowsConsoleMode == "hidden";
        job = ExecutionNative.CreateJobObjectW(0, 0);
        if (job.IsInvalid) throw Error("CreateJobObject");
        try { notifications = new WindowsProcessNotifications(job, request); Start(request, environment); }
        catch (Exception error)
        {
            var cleared = true;
            if (process is not null && !process.IsInvalid)
            {
                ExecutionNative.TerminateProcess(process, 127);
                cleared = ExecutionNative.WaitForSingleObject(process, 2000) == 0;
            }
            Dispose();
            throw new ExecutionStartupException(error.Message, Pid, cleared, error);
        }
    }

    private unsafe void Start(ExecutionRequest request, Dictionary<string, string> environment)
    {
        ExecutionNative.JobLimits limits = default;
        limits.Basic.Flags = 0x2000 | 8; // KILL_ON_JOB_CLOSE | ACTIVE_PROCESS
        limits.Basic.ActiveProcessLimit = (uint)request.MaximumProcesses;
        if (ExecutionNative.SetInformationJobObject(job, 9, &limits, (uint)sizeof(ExecutionNative.JobLimits)) == 0)
            throw Error("SetInformationJobObject");
        var security = new ExecutionNative.SecurityAttributes { Length = (uint)sizeof(ExecutionNative.SecurityAttributes), Inherit = 1 };
        MakePipe(ref security, out var outRead, out var outWrite);
        output = outRead;
        using var childOutput = outWrite;
        MakePipe(ref security, out var errRead, out var errWrite);
        error = errRead;
        using var childError = errWrite;
        MakePipe(ref security, out var inRead, out var inWrite);
        using var childInput = inRead;
        input = inWrite;
        foreach (var handle in new[] { output, error, input })
            if (ExecutionNative.SetHandleInformation(handle, 1, 0) == 0) throw Error("SetHandleInformation");
        nuint attributeBytes = 0;
        ExecutionNative.InitializeProcThreadAttributeList(null, 2, 0, ref attributeBytes);
        if (attributeBytes is < 1 or > 65536) throw new IOException("Invalid process attribute allocation size.");
        var attributes = NativeMemory.Alloc(attributeBytes);
        var initialized = false;
        try
        {
            if (ExecutionNative.InitializeProcThreadAttributeList(attributes, 2, 0, ref attributeBytes) == 0)
                throw Error("InitializeProcThreadAttributeList");
            initialized = true;
            nint* handles = stackalloc nint[3] { childInput.DangerousGetHandle(), childOutput.DangerousGetHandle(), childError.DangerousGetHandle() };
            // No other process/pipe/job handles may leak into this child.
            if (ExecutionNative.UpdateProcThreadAttribute(attributes, 0, 0x20002, handles, (nuint)(3 * sizeof(nint)), 0, 0) == 0)
                throw Error("UpdateProcThreadAttribute");
            nint jobHandle = job.DangerousGetHandle();
            // PROC_THREAD_ATTRIBUTE_JOB_LIST joins the job atomically inside
            // CreateProcess, closing the suspended-before-assignment crash gap.
            if (ExecutionNative.UpdateProcThreadAttribute(attributes, 0, 0x2000D, &jobHandle, (nuint)sizeof(nint), 0, 0) == 0)
                throw Error("UpdateProcThreadAttribute(JOB_LIST)");
            var info = new ExecutionNative.StartupInfoEx
            {
                Startup = new() { Size = (uint)sizeof(ExecutionNative.StartupInfoEx), Flags = hiddenConsole ? 0x101U : 0x100U,
                    Input = handles[0], Output = handles[1], Error = handles[2] }, Attributes = (nint)attributes
            };
            var command = (Quote(request.Executable) + (request.ArgumentList.Length == 0 ? "" : " " + string.Join(' ', request.ArgumentList.Select(Quote))) + '\0').ToCharArray();
            var env = (string.Join('\0', environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value)) + "\0\0").ToCharArray();
            fixed (char* commandPointer = command)
            fixed (char* environmentPointer = env)
            {
                if (ExecutionNative.CreateProcess(request.Executable, commandPointer, 0, 0, 1,
                    // DETACHED_PROCESS prevents console-host allocation even on
                    // systems which create a hidden console for NO_WINDOW.
                    4U | 0x400U | 0x80000U | (hiddenConsole ? 0x10U : 8U | 0x08000000U), environmentPointer, request.WorkingDirectory, ref info, out var created) == 0)
                    throw Error("CreateProcessW");
                process = new(created.Process); thread = new(created.Thread); Pid = checked((int)created.Pid);
            }
            if (ExecutionNative.IsProcessInJob(process, job, out var member) == 0 || member == 0)
                throw Error("Atomic job membership confirmation");
        }
        finally
        {
            if (initialized) ExecutionNative.DeleteProcThreadAttributeList(attributes);
            NativeMemory.Free(attributes);
        }
    }

    private static void MakePipe(ref ExecutionNative.SecurityAttributes security, out ExecutionHandle read, out ExecutionHandle write)
    {
        if (ExecutionNative.CreatePipe(out var r, out var w, ref security, 65536) == 0) throw Error("CreatePipe");
        read = new(r); write = new(w);
    }
    internal static string Quote(string text)
    {
        // Plain options remain plain for native programs with their own argv
        // parser (including wsl.exe). This is a command line for CreateProcess,
        // never a command passed through a shell.
        if (text.Length > 0 && !text.Any(ch => char.IsWhiteSpace(ch) || ch == '"')) return text;
        var result = new StringBuilder("\"");
        var slash = 0;
        foreach (var ch in text)
        {
            if (ch == '\\') { slash++; continue; }
            result.Append('\\', ch == '"' ? slash * 2 + 1 : slash);
            result.Append(ch); slash = 0;
        }
        return result.Append('\\', slash * 2).Append('"').ToString();
    }
    public void Release()
    {
        if (thread is null || ExecutionNative.ResumeThread(thread) == uint.MaxValue) throw Error("ResumeThread");
        thread.Dispose(); thread = null;
    }
    public bool TryExit(out int exitCode)
    {
        exitCode = 0;
        if (process is null) throw new InvalidOperationException("Missing retained process handle.");
        var wait = ExecutionNative.WaitForSingleObject(process, 0);
        if (wait == 258) return false;
        if (wait != 0 || ExecutionNative.GetExitCodeProcess(process, out var code) == 0) throw Error("GetExitCodeProcess");
        exitCode = unchecked((int)code); return true;
    }
    public unsafe int ReadOutput(bool isError, byte[] buffer)
    {
        var handle = isError ? error : output;
        if (handle is null) return -1;
        if (ExecutionNative.PeekNamedPipe(handle, null, 0, 0, out var available, 0) == 0)
        {
            if (Marshal.GetLastPInvokeError() == 109) return -1;
            throw Error("PeekNamedPipe");
        }
        if (available == 0) return 0;
        fixed (byte* bytes = buffer)
        {
            if (ExecutionNative.ReadFile(handle, bytes, Math.Min(available, (uint)buffer.Length), out var read, 0) == 0)
            {
                if (Marshal.GetLastPInvokeError() == 109) return -1;
                throw Error("ReadFile");
            }
            return checked((int)read);
        }
    }
    public Task WriteInputAsync(byte[] bytes, CancellationToken cancellation) => Task.Run(() => WriteInput(bytes, cancellation), CancellationToken.None);
    private unsafe void WriteInput(byte[] bytes, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var written = 0;
            for (var block = 0; block < bytes.Length / 4096 + 1 && written < bytes.Length; block++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (clock.Elapsed.TotalSeconds >= 86400) throw new TimeoutException("Standard input transfer deadline.");
                fixed (byte* pointer = bytes)
                    if (ExecutionNative.WriteFile(input!, pointer + written, (uint)Math.Min(4096, bytes.Length - written), out var count, 0) == 0)
                        throw Error("WriteFile(stdin)");
                    else written += checked((int)count);
            }
            if (written != bytes.Length) throw new IOException("Incomplete standard input transfer.");
        }
        finally { input?.Dispose(); input = null; }
    }
    public unsafe int[] Members()
    {
        // JOBOBJECT_BASIC_PROCESS_ID_LIST: two ULONG fields followed by ULONG_PTR.
        var bytes = new byte[8 + (maximumProcesses + 1) * IntPtr.Size];
        fixed (byte* pointer = bytes)
        {
            var queried = ExecutionNative.QueryInformationJobObject(job, 3, pointer, (uint)bytes.Length, out var returned);
            var assigned = *(uint*)pointer; var count = *(uint*)(pointer + 4);
            var detail = $"assigned={assigned}; count={count}; maximum={maximumProcesses}; bufferBytes={bytes.Length}; returnedBytes={returned}";
            if (queried == 0) throw Error("QueryInformationJobObject; " + detail);
            if (assigned != count || count > maximumProcesses) throw new IOException("Job member list is incomplete or over budget; " + detail);
            var result = new int[count];
            for (var i = 0; i < result.Length; i++) result[i] = checked((int)((nuint*)(pointer + 8))[i]);
            return result;
        }
    }
    public void Register(ExecutionIdentity identity)
    {
        if (identity.Pid == Pid) return;
        var sampled = notifications?.RetainedHandle(identity);
        var handle = sampled ?? ExecutionNative.OpenProcess(0x00101000, 0, (uint)identity.Pid);
        try
        {
            if (handle.IsInvalid || ExecutionNative.GetProcessTimes(handle, out var created, out _, out _, out _) == 0 ||
                DateTimeOffset.FromFileTime(created).ToUniversalTime() != identity.Started ||
                (sampled is null && (ExecutionNative.IsProcessInJob(handle, job, out var member) == 0 || member == 0)))
                throw Error("Job member retained birth identity");
            var consoleHost = notifications?.ConsoleHostProof(identity) ?? SystemConsoleHost(handle);
            if (consoleHost) { consoleHosts.Add(identity.Pid); identity.Ownership = "windows-job-console-host"; }
            registered.Add(identity.Pid, handle);
        }
        catch { if (sampled is null) handle.Dispose(); throw; }
    }
    internal bool RetainsBirth(ExecutionIdentity identity)
    {
        var handle = identity.Pid == Pid ? process : registered.GetValueOrDefault(identity.Pid);
        return handle is not null && !handle.IsInvalid && ExecutionNative.GetProcessTimes(handle, out var created, out _, out _, out _) != 0 &&
            DateTimeOffset.FromFileTime(created).ToUniversalTime() == identity.Started;
    }
    internal bool HasExitedBirth(ExecutionIdentity identity)
    {
        var handle = identity.Pid == Pid ? process : registered.GetValueOrDefault(identity.Pid);
        return RetainsBirth(identity) && handle is not null && ExecutionNative.WaitForSingleObject(handle, 0) == 0;
    }
    internal bool ConsoleHost(int pid) => consoleHosts.Contains(pid);
    internal int[] NotifiedPids() => notifications?.Pids() ?? [];
    internal ProcessObservation ObserveRegisteredCandidate(int pid)
    {
        if (notifications?.TryComplete(pid, out var complete) == true) return complete!;
        return ProcessObserver.Observe(pid);
    }
    internal string NotificationDetail(int pid) => notifications?.PendingDetail(pid) ?? "Notification unavailable.";
    internal string[] NotificationErrors => notifications?.Errors ?? [];
    internal void StopNotifications() => notifications?.Stop();
    internal static unsafe bool SystemConsoleHost(ExecutionHandle handle)
    {
        var image = new char[32768]; var system = new char[32768];
        uint length = (uint)image.Length;
        fixed (char* imagePointer = image)
        fixed (char* systemPointer = system)
        {
            if (ExecutionNative.QueryFullProcessImageNameW(handle, 0, imagePointer, ref length) == 0) throw Error("QueryFullProcessImageNameW");
            var systemLength = ExecutionNative.GetSystemDirectoryW(systemPointer, (uint)system.Length);
            if (length is < 1 or > 32767 || systemLength is < 1 or > 32767) throw Error("System image path bounds");
            return string.Equals(new string(image, 0, (int)length), Path.Combine(new string(system, 0, (int)systemLength), "conhost.exe"), StringComparison.OrdinalIgnoreCase);
        }
    }
    public void Terminate() { if (ExecutionNative.TerminateJobObject(job, 125) == 0) throw Error("TerminateJobObject"); }
    public bool Reap()
    {
        if (!TryExit(out _) || Members().Length != 0) return false;
        if (registered.Count > maximumProcesses) throw new IOException("Retained task HANDLE count exceeds configured process bound.");
        // An admitted console host can leave the live Job list before its
        // retained birth HANDLE signals. The caller owns the one cleanup
        // deadline/50ms backoff; each HANDLE probe here is nonblocking.
        foreach (var pair in registered)
        {
            if (pair.Value.IsInvalid || pair.Value.IsClosed) throw new IOException("Registered task HANDLE is unavailable for PID " + pair.Key);
            var wait = ExecutionNative.WaitForSingleObject(pair.Value, 0);
            if (wait == 258) return false;
            if (wait != 0) throw Error("WaitForSingleObject(registered task exit), PID " + pair.Key);
        }
        return true;
    }
    public void Dispose()
    {
        // Closing the job is also a kernel-enforced fallback for exceptional paths.
        notifications?.Stop();
        job.Dispose(); thread?.Dispose(); process?.Dispose(); output?.Dispose(); error?.Dispose(); input?.Dispose();
        foreach (var handle in registered.Values) handle.Dispose();
        notifications?.Dispose();
    }
    private static Win32Exception Error(string operation) => new(Marshal.GetLastPInvokeError(), operation);
}
