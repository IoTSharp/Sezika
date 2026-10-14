using System.Diagnostics;
using System.Text;
using Sezika.Tooling;

// Budget/observer contracts only. The controlled child-exit registration
// fixture and original Git case8 remain separate native runtime acceptance.
internal static class LinuxEarlyRegistrationChecks
{
    internal const int CheckCount = 7;

    internal static void Run(string root, Action<string, Action> check)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux early-registration checks require Linux.");
        using var suiteCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clock = Stopwatch.StartNew();
        void Gate()
        {
            suiteCancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 5) throw new TimeoutException("Linux early-registration contract deadline5s.");
        }
        void Case(string name, Action action) => check(name, () => { Gate(); action(); Gate(); });

        Case("Linux early registration child budget observes parent token cancellation", () =>
        {
            using var parentCancellation = new CancellationTokenSource();
            var parent = new Budget(30, null, parentCancellation.Token);
            var child = new Budget(30, null, CancellationToken.None, parent);
            parentCancellation.Cancel();
            Cancelled(child.Check);
        });
        Case("Linux early registration child reads observe parent cancel file", () =>
        {
            var directory = Path.Combine(root, "linux-early-parent-file");
            if (Path.Exists(directory)) throw new IOException("Preserve existing parent-cancel fixture.");
            Directory.CreateDirectory(directory);
            var cancelFile = Path.Combine(directory, "CANCEL.signal");
            var parent = new Budget(30, cancelFile, suiteCancellation.Token);
            var child = new Budget(30, null, CancellationToken.None, parent);
            using (var file = new FileStream(cancelFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                file.Write(Encoding.ASCII.GetBytes("SYNTHETIC_PARENT_CANCEL_ONLY\n"));
            Cancelled(child.Check);
            Cancelled(() => ProcessObserver.Observe(Environment.ProcessId, child));
            Cancelled(() => LinuxExecutionSession.TryStat(Environment.ProcessId, child));
        });
        Case("Linux early registration thirty-second child cannot extend expired one-second parent", () =>
        {
            var parent = new Budget(1, null, suiteCancellation.Token);
            var child = new Budget(30, null, CancellationToken.None, parent);
            Assert(child.RemainingSeconds <= parent.RemainingSeconds + .05, "Child budget extended the live parent.");
            // One bounded wait, cancellable by the complete five-second suite.
            if (suiteCancellation.Token.WaitHandle.WaitOne(1050)) Gate();
            Assert(child.RemainingSeconds == 0, "Expired parent still grants child time.");
            TimedOut(child.Check);
            TimedOut(() => ProcessObserver.Observe(Environment.ProcessId, child));
            TimedOut(() => LinuxExecutionSession.TryStat(Environment.ProcessId, child));
        });
        Case("Linux early registration pre-cancelled native observation and stat propagate cancellation", () =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var budget = new Budget(30, null, cancelled.Token);
            Cancelled(() => ProcessObserver.Observe(Environment.ProcessId, budget));
            Cancelled(() => LinuxExecutionSession.TryStat(Environment.ProcessId, budget));
        });
        Case("Linux early registration actual self native tuple shares one parent budget", () =>
        {
            var parent = new Budget(2, null, suiteCancellation.Token);
            var child = new Budget(30, null, CancellationToken.None, parent);
            var observed = ProcessObserver.Observe(Environment.ProcessId, child);
            var stat = LinuxExecutionSession.TryStat(Environment.ProcessId, child);
            Assert(observed.State == "Present" && observed.Platform == "linux" && observed.Pid == Environment.ProcessId &&
                observed.ParentPid > 0 && observed.StartTicks is > 0 && Guid.TryParse(observed.BootId, out _) &&
                !string.IsNullOrEmpty(observed.CommandLine) && observed.CommandLine.EndsWith('\0'),
                "Actual self native birth/boot/parent/NUL argv tuple is incomplete.");
            Assert(stat is not null && stat.Value.Parent == observed.ParentPid && stat.Value.Start == observed.StartTicks,
                "Actual stat does not match actual self observation.");
            Assert(child.RemainingSeconds <= parent.RemainingSeconds + .05, "Native reads escaped their parent budget.");
        });
        Case("Linux early registration metadata scope and argv gate propagate shared cancellation", () =>
        {
            using var cancellation = new CancellationTokenSource();
            var parent = new Budget(2, null, cancellation.Token);
            var child = new Budget(30, null, CancellationToken.None, parent);
            var actual = ProcessObserver.Observe(Environment.ProcessId, child);
            cancellation.Cancel();
            Cancelled(() => LinuxExecutionSession.ReadMetadata("/proc/self/stat", child));
            Cancelled(() => LinuxExecutionSession.ExecutionScope(Environment.ProcessId, child));
            Cancelled(() => LinuxExecutionSession.ObserveRegistrationCandidate(actual, actual.CommandLine, child));
        });
        Case("Linux early registration request guard precedes native metadata access", () =>
        {
            var calls = 0;
            var budget = new Budget(2, null, CancellationToken.None, guard: () =>
            {
                calls++;
                throw new OperationCanceledException("Synthetic execution request guard cancellation.");
            });
            Cancelled(() => ProcessObserver.Observe(Environment.ProcessId, budget));
            Assert(calls == 1, "Native observation ignored or repeated the execution request guard.");
        });
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Cancelled(Action action)
    {
        try { action(); } catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected shared-budget cancellation was not propagated.");
    }
    private static void TimedOut(Action action)
    {
        try { action(); } catch (TimeoutException) { return; }
        throw new InvalidOperationException("Expected parent-budget timeout was not propagated.");
    }
}
