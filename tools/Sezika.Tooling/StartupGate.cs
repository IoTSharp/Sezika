using System.Diagnostics;
using System.Globalization;

namespace Sezika.Tooling;

// Optional compatibility with the existing task guardian. It supplies an explicit
// task-owned directory and writes this process ID only after identity registration.
internal static class StartupGate
{
    internal static void Wait(CancellationToken cancellation)
    {
        var value = Environment.GetEnvironmentVariable("SEZIKA_START_GATE");
        if (string.IsNullOrEmpty(value)) return;
        var gate = Path.GetFullPath(value);
        if (!Directory.Exists(gate)) throw new DirectoryNotFoundException("Startup gate is missing.");
        var budget = new Budget(40, Path.Combine(gate, "cancel.request"), cancellation);
        SafePaths.NoLinks(gate, budget);
        var clock = Stopwatch.StartNew();
        var release = Path.Combine(gate, "go");
        for (var attempt = 0; attempt < 160; attempt++)
        {
            budget.Check();
            if (File.Exists(release))
            {
                SafePaths.NoLinks(release, budget);
                if (new FileInfo(release).Length > 64 || !int.TryParse(File.ReadAllText(release), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid != Environment.ProcessId)
                    throw new InvalidDataException("Startup gate PID differs from this process.");
                Environment.SetEnvironmentVariable("SEZIKA_START_GATE", null);
                return;
            }
            if (clock.Elapsed.TotalSeconds >= 40) break;
            if (cancellation.WaitHandle.WaitOne(250)) cancellation.ThrowIfCancellationRequested();
        }
        throw new TimeoutException("Startup registration gate exceeded 160 polls/40 seconds.");
    }
}
