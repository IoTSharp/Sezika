using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sezika.Tooling;

// Each Job has one private, noninherited completion port and one bounded pump.
// Only actual complete observations held against a verified birth are cached.
internal sealed class WindowsProcessNotifications : IDisposable
{
    private sealed class Entry(ExecutionHandle? handle, DateTimeOffset? started, ProcessObservation observation)
    {
        internal ExecutionHandle? Handle { get; } = handle;
        internal DateTimeOffset? Started { get; } = started;
        internal ProcessObservation Observation { get; set; } = observation;
        internal ProcessObservation? Complete { get; set; }
        internal bool? ConsoleHost { get; set; }
    }
    private readonly ExecutionHandle job, port;
    private readonly nint key;
    private readonly int maximum, seconds;
    private readonly object sync = new();
    private readonly Dictionary<int, Entry> entries = [];
    private readonly List<string> errors = [];
    private readonly CancellationTokenSource stop = new();
    private readonly Task pump;
    private bool joined;

    internal unsafe WindowsProcessNotifications(ExecutionHandle job, ExecutionRequest request)
    {
        this.job = job; maximum = request.MaximumProcesses; seconds = checked(request.TimeoutSeconds + request.CleanupSeconds);
        key = job.DangerousGetHandle(); port = ExecutionNative.CreateIoCompletionPort(-1, 0, 0, 1);
        if (port.IsInvalid) { port.Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateIoCompletionPort"); }
        var association = new ExecutionNative.JobCompletionPort { Key = key, Port = port.DangerousGetHandle() };
        if (ExecutionNative.SetInformationJobObject(job, 7, &association, (uint)sizeof(ExecutionNative.JobCompletionPort)) == 0)
        { port.Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError(), "Associate Job completion port"); }
        pump = Task.Factory.StartNew(Pump, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void Pump()
    {
        var clock = Stopwatch.StartNew(); var messages = 0;
        try
        {
            // <=50 timeout polls/s plus the event bound and one wake,
            // <=timeout+cleanup seconds. Native wait
            // blocks20ms; no timeout polling spin or unbounded retry.
            for (var poll = 0; poll < seconds * 50 + maximum * 4 + 17 && clock.Elapsed.TotalSeconds < seconds; poll++)
            {
                if (ExecutionNative.GetQueuedCompletionStatus(port, out var message, out var actualKey, out var value, 20) == 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == 258) { if (stop.IsCancellationRequested) return; continue; }
                    throw new Win32Exception(error, "Job completion notification");
                }
                // The wake follows already queued notifications. Drain those
                // before stopping; an uncaptured exited NEW_PROCESS stays UNKNOWN.
                if (message == 0 && stop.IsCancellationRequested) return;
                if (++messages > maximum * 4 + 16) throw new IOException("Job completion event count bound exceeded.");
                if ((nint)actualKey != key) throw new IOException("Job completion ownership key differs.");
                if (message != 6) continue; // JOB_OBJECT_MSG_NEW_PROCESS
                if (value <= 0 || value > int.MaxValue) throw new IOException("Job notification PID invalid.");
                Capture((int)value, clock);
            }
            if (!stop.IsCancellationRequested) throw new IOException("Job notification poll/deadline bound exhausted.");
        }
        catch (Exception error) { lock (sync) errors.Add(error.GetType().Name + ": " + error.Message); }
    }

    private void Capture(int pid, Stopwatch clock)
    {
        lock (sync)
        {
            if (entries.ContainsKey(pid)) return;
            if (entries.Count >= maximum) throw new IOException("Job notification identity count bound exceeded.");
        }
        ExecutionHandle? handle = ExecutionNative.OpenProcess(0x00101000, 0, (uint)pid);
        DateTimeOffset? started = null;
        var actual = new ProcessObservation("Unknown", pid, 0, null, null, "windows", null, "Job notification native identity not yet read.");
        bool? consoleHost = null;
        try
        {
            if (handle.IsInvalid || ExecutionNative.GetProcessTimes(handle, out var created, out _, out _, out _) == 0 ||
                ExecutionNative.IsProcessInJob(handle, job, out var member) == 0 || member == 0)
            {
                handle.Dispose(); handle = null;
                actual = new("Unknown", pid, actual.ParentPid, null, null, "windows", null, "Job notification birth/membership handle unavailable; " + actual.Error);
            }
            else
            {
                started = DateTimeOffset.FromFileTime(created).ToUniversalTime();
                // Image classification must be captured against this same birth
                // while queryable, rather than queried from an exited cache.
                try { consoleHost = WindowsExecutionSession.SystemConsoleHost(handle); }
                catch (Win32Exception) { /* No positive image proof; no argv relaxation. */ }
                actual = ProcessObserver.Observe(pid);
            }
            var entry = new Entry(handle, started, actual); handle = null;
            entry.ConsoleHost = consoleHost;
            if (entry.Handle is not null && actual.State == "Present" && actual.Started == started && actual.ParentPid > 0 && !string.IsNullOrEmpty(actual.CommandLine)) entry.Complete = actual;
            lock (sync) entries.Add(pid, entry);
            var registration = Stopwatch.StartNew();
            // Initial PEB/argv can lag NEW_PROCESS. <=25 reads AND50ms, also
            // under the one pump deadline/cancel; no identity invented on exit.
            for (var attempt = 0; attempt < 25 && entry.Complete is null && registration.Elapsed.TotalMilliseconds < 50 && clock.Elapsed.TotalSeconds < seconds && !stop.IsCancellationRequested; attempt++)
            {
                if (entry.Handle is null) break;
                if (actual.State == "Present" && actual.Started == started && actual.ParentPid > 0 && !string.IsNullOrEmpty(actual.CommandLine))
                { lock (sync) { entry.Complete = actual; entry.Observation = actual; } break; }
                lock (sync) entry.Observation = actual;
                if (ExecutionNative.WaitForSingleObject(entry.Handle, 0) == 0) break;
                Thread.Sleep(2); actual = ProcessObserver.Observe(pid);
            }
        }
        finally { handle?.Dispose(); }
    }

    internal int[] Pids() { lock (sync) return entries.Keys.ToArray(); }
    internal bool TryComplete(int pid, out ProcessObservation? observation)
    {
        lock (sync)
        {
            observation = null;
            if (!entries.TryGetValue(pid, out var entry) || entry.Complete is null || !SameBirth(entry)) return false;
            // While alive, retain the actual initial observation; the executor
            // still re-observes argv changes. After exit the exact HANDLE must
            // be signalled, so neither metadata denial nor PID reuse is waived.
            var state = ExecutionNative.WaitForSingleObject(entry.Handle!, 0);
            if (state is not (0 or 258)) return false;
            observation = entry.Complete; return true;
        }
    }
    internal string PendingDetail(int pid)
    {
        lock (sync)
            return entries.TryGetValue(pid, out var entry) ? "state=" + entry.Observation.State + "; detail=" + entry.Observation.Error + "; complete=" + (entry.Complete is not null) : "No notification identity.";
    }
    internal string[] Errors { get { lock (sync) return errors.ToArray(); } }
    internal ExecutionHandle? RetainedHandle(ExecutionIdentity identity)
    {
        lock (sync)
            return entries.TryGetValue(identity.Pid, out var entry) && SameBirth(entry) && entry.Complete is { } complete && complete.Started == identity.Started &&
                complete.ParentPid == identity.ParentPid && complete.CommandLine == identity.CommandLine ? entry.Handle : null;
    }
    internal bool? ConsoleHostProof(ExecutionIdentity identity)
    {
        lock (sync)
            return entries.TryGetValue(identity.Pid, out var entry) && SameBirth(entry) && entry.Started == identity.Started ? entry.ConsoleHost : null;
    }
    private static bool SameBirth(Entry entry) => entry.Handle is not null && !entry.Handle.IsInvalid && entry.Started is not null &&
        ExecutionNative.GetProcessTimes(entry.Handle, out var created, out _, out _, out _) != 0 && DateTimeOffset.FromFileTime(created).ToUniversalTime() == entry.Started;

    internal void Stop()
    {
        if (joined) return;
        stop.Cancel(); _ = ExecutionNative.PostQueuedCompletionStatus(port, 0, (nuint)key, 0);
        joined = pump.Wait(2000);
        if (!joined) throw new IOException("Owned Job notification pump did not join within2s; handles retained.");
    }
    public void Dispose()
    {
        Stop(); port.Dispose();
        lock (sync) foreach (var entry in entries.Values) entry.Handle?.Dispose();
        stop.Dispose();
    }
}
