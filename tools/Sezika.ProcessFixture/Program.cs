using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

// A genuine C# apphost payload; every spawned child lives at most15 seconds.
// The executor owns cleanup through its job/process-group and identity records.
if (args.Length is < 1 or > 8) return 64;
Console.OutputEncoding = new UTF8Encoding(false);
Console.InputEncoding = new UTF8Encoding(false);
var clock = Stopwatch.StartNew();
using var fixtureCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var mode = args[0];
switch (mode)
{
    case "--help":
    case "--version":
        Console.WriteLine("Sezika.ProcessFixture synthetic protocol fixture; no model or training.");
        return 0;
    case "success":
        Console.WriteLine("fixture-success");
        for (var index = 1; index < args.Length; index++) Console.WriteLine(index + ":" + args[index]);
        Console.Error.WriteLine("fixture-stderr");
        return 0;
    case "input":
        var bytes = new byte[1024];
        var count = Console.OpenStandardInput().Read(bytes);
        Console.OpenStandardOutput().Write(bytes, 0, count);
        return 0;
    case "failure": Console.Error.WriteLine("fixture-failure"); return 7;
    case "marked-sleep":
        if (args.Length != 2 || !Path.IsPathFullyQualified(args[1])) return 64;
        var markerPending = args[1] + ".pending";
        using (var marker = new FileStream(markerPending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            marker.Write(Encoding.ASCII.GetBytes(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)));
            marker.Flush(flushToDisk: true);
        }
        File.Move(markerPending, args[1], false);
        for (var attempt = 0; attempt < 150 && clock.Elapsed.TotalSeconds < 15; attempt++) Thread.Sleep(100);
        return 0;
    case "fast-tree":
        for (var query = 0; query < 8 && clock.Elapsed.TotalSeconds < 4; query++)
        {
            var queryStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            queryStart.ArgumentList.Add("success");
            using var process = FixtureProcesses.Start(queryStart);
            Console.WriteLine("fixture-fast-child=" + process.Id.ToString(CultureInfo.InvariantCulture));
            if (!process.WaitForExit(1000) || process.ExitCode != 0) throw new IOException("Bounded native query did not actually exit0.");
        }
        if (clock.Elapsed.TotalSeconds >= 4) throw new TimeoutException("Fast-tree fixture deadline4s.");
        return 0;
    case "sleep":
    case "escape-sleep":
        if (mode == "escape-sleep" && OperatingSystem.IsLinux() && FixtureNative.setsid() < 0) return 70;
        // 150 iterations AND a15s deadline, with100ms backoff.
        for (var attempt = 0; attempt < 150 && clock.Elapsed.TotalSeconds < 15; attempt++) Thread.Sleep(100);
        return 0;
    case "tree":
    case "escape-tree":
    case "late-tree":
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("Fixture apphost path unavailable.");
        if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return 65;
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(mode == "escape-tree" ? "escape-sleep" : mode == "late-tree" ? "late-parent" : "sleep");
        using (var child = FixtureProcesses.Start(start))
        {
            Console.WriteLine("fixture-child=" + child.Id.ToString(CultureInfo.InvariantCulture));
            // Keep the parent alive while the executor registers the full edge.
            var lifetime = mode == "late-tree" ? 5 : 1;
            for (var attempt = 0; attempt < lifetime * 10 && clock.Elapsed.TotalSeconds < lifetime; attempt++) Thread.Sleep(100);
        }
        return 0;
    case "late-parent":
        // These children continue to be born until the group/job is terminated.
        // <=12 apphost children and a1s creation window, then a15s lifetime.
        Thread.Sleep(200);
        var actualLateChildren = new List<int>();
        for (var childIndex = 0; childIndex < 12 && clock.Elapsed.TotalSeconds < 1; childIndex++)
        {
            fixtureCancellation.Token.ThrowIfCancellationRequested();
            var lateStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            lateStart.ArgumentList.Add("sleep");
            using var late = FixtureProcesses.Start(lateStart);
            actualLateChildren.Add(late.Id);
            Console.WriteLine("fixture-late-child=" + late.Id.ToString(CultureInfo.InvariantCulture));
            Thread.Sleep(30);
        }
        if (Environment.GetEnvironmentVariable("SEZIKA_FIXTURE_LATE_BIRTHS") is { } lateBirths)
        {
            var innerReceipts = Environment.GetEnvironmentVariable("SEZIKA_FIXTURE_INNER_RECEIPTS") ?? throw new IOException("Exact inner receipt directory required.");
            if (!Path.IsPathFullyQualified(innerReceipts) || Path.GetFullPath(innerReceipts) != innerReceipts || !Directory.Exists(innerReceipts))
                throw new IOException("Strict absolute inner receipt directory required.");
            var expectedBirths = Path.Combine(innerReceipts, "fixture-late-births");
            if (Path.GetFullPath(lateBirths) != expectedBirths || actualLateChildren.Count is < 1 or > 12)
                throw new IOException("Actual late-child marker scope/count differs.");
            using (var marker = new FileStream(lateBirths + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                marker.Write(Encoding.ASCII.GetBytes(string.Join('\n', actualLateChildren.Select(pid => pid.ToString(CultureInfo.InvariantCulture)))));
                marker.Flush(flushToDisk: true);
            }
            File.Move(lateBirths + ".pending", lateBirths, false);
        }
        for (var attempt = 0; attempt < 150 && clock.Elapsed.TotalSeconds < 15; attempt++) Thread.Sleep(100);
        return 0;
    case "supervisor":
        if (args.Length != 3) return 64;
        using (var requestJson = JsonDocument.Parse(File.ReadAllBytes(args[2])))
        {
            var innerDirectory = requestJson.RootElement.GetProperty("OutputDirectory").GetString()!;
            var ownerStart = new ProcessStartInfo(args[1]) { UseShellExecute = false, CreateNoWindow = true };
            var outerDirectory = OperatingSystem.IsLinux()
                ? Environment.GetEnvironmentVariable("SEZIKA_FIXTURE_OUTER_RECEIPTS") ?? throw new IOException("Explicit actual outer receipt directory required.")
                : null;
            if (outerDirectory is not null && (!Path.IsPathFullyQualified(outerDirectory) || Path.GetFullPath(outerDirectory) != outerDirectory || !Directory.Exists(outerDirectory)))
                throw new IOException("Strict absolute outer receipt directory required.");
            var lateBirthsPath = OperatingSystem.IsLinux() && requestJson.RootElement.GetProperty("ArgumentList")[0].GetString() == "late-tree"
                ? Path.Combine(innerDirectory, "fixture-late-births") : null;
            if (lateBirthsPath is not null)
            {
                ownerStart.Environment["SEZIKA_FIXTURE_INNER_RECEIPTS"] = innerDirectory;
                ownerStart.Environment["SEZIKA_FIXTURE_LATE_BIRTHS"] = lateBirthsPath;
            }
            foreach (var argument in new[] { "process", "run", "--request", args[2], "--output", innerDirectory + ".cli.json" }) ownerStart.ArgumentList.Add(argument);
            using var owner = FixtureProcesses.Start(ownerStart);
            Console.WriteLine("fixture-owner=" + owner.Id + ";started=" + owner.StartTime.ToUniversalTime().ToString("O") + ";executable=" + args[1]);
            var observed = new Dictionary<int, (DateTimeOffset? Started, ulong? Ticks)>();
            var innerPids = new HashSet<int>(); var outerAdmitted = false;
            try
            {
                for (var poll = 0; poll < 80 && clock.Elapsed.TotalSeconds < 4; poll++)
                {
                    fixtureCancellation.Token.ThrowIfCancellationRequested();
                    if (Directory.Exists(innerDirectory))
                    {
                        var files = 0;
                        foreach (var receipt in Directory.EnumerateFiles(innerDirectory, "registered*.json", SearchOption.TopDirectoryOnly))
                        {
                            if (++files > 257 || clock.Elapsed.TotalSeconds >= 4) throw new IOException("Supervisor fixture identity inventory bound.");
                            using var json = JsonDocument.Parse(File.ReadAllBytes(receipt));
                            if (json.RootElement.GetArrayLength() > 256) throw new IOException("Inner identity receipt member bound256.");
                            foreach (var identity in json.RootElement.EnumerateArray())
                            {
                                innerPids.Add(identity.GetProperty("Pid").GetInt32());
                                if (innerPids.Count > 256) throw new IOException("Inner identity inventory bound256.");
                                if (identity.GetProperty("Ownership").GetString() == "linux-watchdog") continue;
                                var pid = identity.GetProperty("Pid").GetInt32();
                                var stamp = identity.GetProperty("Started"); var ticks = identity.GetProperty("StartTicks");
                                observed[pid] = (stamp.ValueKind == JsonValueKind.Null ? null : stamp.GetDateTimeOffset(), ticks.ValueKind == JsonValueKind.Null ? null : ticks.GetUInt64());
                            }
                        }
                    }
                    var birthsComplete = lateBirthsPath is null;
                    if (lateBirthsPath is not null && File.Exists(lateBirthsPath))
                    {
                        if (new FileInfo(lateBirthsPath).Length > 256) throw new IOException("Late-child marker byte bound256.");
                        var births = File.ReadAllText(lateBirthsPath).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Select(text => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
                        if (births.Length is < 1 or > 12 || births.Distinct().Count() != births.Length) throw new IOException("Actual late-child PID count differs.");
                        birthsComplete = births.All(innerPids.Contains);
                    }
                    if (observed.Count >= 2 && birthsComplete && (outerDirectory is null ||
                        FixtureOuterReceipts.ContainsActualLiveIdentities(outerDirectory, innerPids, Environment.ProcessId, clock, fixtureCancellation.Token)))
                    { outerAdmitted = true; break; }
                    if (owner.HasExited) throw new IOException("Supervisor exited before root/child registration.");
                    Thread.Sleep(50);
                }
                if (observed.Count < 2 || !outerAdmitted) throw new IOException("Actual root/child/late births lack complete independent outer native registration within4s.");
                owner.Kill();
                if (!owner.WaitForExit(2000)) throw new TimeoutException("Owned supervisor did not exit.");
                for (var poll = 0; poll < 60 && clock.Elapsed.TotalSeconds < 7; poll++)
                {
                    var live = false;
                    foreach (var identity in observed)
                    {
                        try
                        {
                            if (OperatingSystem.IsWindows())
                            {
                                using var actual = Process.GetProcessById(identity.Key);
                                if (!actual.HasExited && actual.StartTime.ToUniversalTime() == identity.Value.Started?.UtcDateTime) live = true;
                            }
                            else
                            {
                                var stat = File.ReadAllText("/proc/" + identity.Key + "/stat");
                                var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (ulong.Parse(fields[19], CultureInfo.InvariantCulture) == identity.Value.Ticks && fields[0] is not ("Z" or "X")) live = true;
                            }
                        }
                        catch (ArgumentException) { }
                        catch (FileNotFoundException) { }
                        catch (DirectoryNotFoundException) { }
                    }
                    if (!live)
                    {
                        if (OperatingSystem.IsLinux())
                        {
                            var watchdogReceipt = Path.Combine(innerDirectory, "watchdog-result.json");
                            if (!File.Exists(watchdogReceipt)) { Thread.Sleep(50); continue; }
                            using var watchdog = JsonDocument.Parse(File.ReadAllBytes(watchdogReceipt));
                            if (watchdog.RootElement.GetProperty("Status").GetString() != "TERMINATED_PENDING_PARENT_REAP" || watchdog.RootElement.GetProperty("RemainingLive").GetInt32() != 0)
                                throw new IOException("Inner watchdog did not independently prove termination.");
                            Console.WriteLine("fixture-inner-watchdog=" + watchdog.RootElement.GetProperty("Reason").GetString());
                        }
                        Console.WriteLine("fixture-supervisor-exit-terminated"); return 0;
                    }
                    Thread.Sleep(50);
                }
                throw new TimeoutException("Registered payload survived supervisor exit.");
            }
            finally
            {
                if (!owner.HasExited) { owner.Kill(); owner.WaitForExit(2000); }
            }
        }
    case "output":
        var output = Console.OpenStandardOutput(); var block = Encoding.ASCII.GetBytes(new string('x', 1024));
        for (var index = 0; index < 256 && clock.Elapsed.TotalSeconds < 5; index++) output.Write(block);
        return 0;
    case "stderr-output":
        var stderr = Console.OpenStandardError(); var errorBlock = Encoding.ASCII.GetBytes(new string('e', 1024));
        for (var index = 0; index < 256 && clock.Elapsed.TotalSeconds < 5; index++) stderr.Write(errorBlock);
        return 0;
    default: return 64;
}

internal static partial class FixtureNative
{
    [LibraryImport("libc", SetLastError = true)] internal static partial int setsid();
    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        internal uint Size; internal nint Reserved, Desktop, Title;
        internal uint X, Y, Width, Height, CountX, CountY, Fill, Flags;
        internal ushort Show, ReservedSize; internal nint ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation { internal nint Process, Thread; internal uint Pid, Tid; }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static unsafe partial int CreateProcess(string executable, char* command, nint processSecurity, nint threadSecurity, int inherit,
        uint flags, nint environment, string directory, ref StartupInfo startup, out ProcessInformation information);
    [LibraryImport("kernel32.dll")] internal static partial nint GetStdHandle(int number);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial int CloseHandle(nint handle);
}

// The fixture may choose when to kill its supervisor. It never contributes
// identities to the executor: each PID must already have an independent outer
// snapshot matching its still-live native birth, parent and complete argv.
internal static class FixtureOuterReceipts
{
    private sealed record Actual(int Parent, ulong Ticks, string Boot, string Command);
    internal static bool ContainsActualLiveIdentities(string directory, HashSet<int> pids, int fixturePid, Stopwatch fixtureClock, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        void Check()
        {
            cancellation.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 1 || fixtureClock.Elapsed.TotalSeconds >= 4)
                throw new TimeoutException("Independent outer native handshake deadline.");
        }
        if (pids.Count is < 2 or > 256 || !Path.IsPathFullyQualified(directory)) throw new IOException("Outer identity scope/count differs.");
        var actual = new Dictionary<int, Actual>();
        var boot = Encoding.ASCII.GetString(Read("/proc/sys/kernel/random/boot_id", 64, Check)).Trim();
        if (!Guid.TryParse(boot, out _)) throw new IOException("Actual Linux boot identity differs.");
        foreach (var pid in pids)
        {
            Check();
            try
            {
                var root = "/proc/" + pid.ToString(CultureInfo.InvariantCulture);
                var before = Stat(Encoding.UTF8.GetString(Read(root + "/stat", 65536, Check)));
                var command = Encoding.UTF8.GetString(Read(root + "/cmdline", 65536, Check));
                var after = Stat(Encoding.UTF8.GetString(Read(root + "/stat", 65536, Check)));
                if (before != after || after.State is "Z" or "X" || command.Length == 0) return false;
                actual.Add(pid, new(after.Parent, after.Ticks, boot, command));
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
        var matched = new HashSet<int>(); var files = 0; var registrations = 0; var executions = 0;
        var evidence = Directory.EnumerateFiles(directory, "registered*.json", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(directory, "observed-exec-*.json", SearchOption.TopDirectoryOnly));
        foreach (var receipt in evidence)
        {
            Check();
            // At most257 existing registration frames plus one independently
            // observed exec proof for each of the maximum256 owned members.
            var afterExec = Path.GetFileName(receipt).StartsWith("observed-exec-", StringComparison.Ordinal);
            if (++files > 513 || (afterExec ? ++executions > 256 : ++registrations > 257) ||
                new FileInfo(receipt).Length > 1048576) throw new IOException("Outer receipt inventory/byte bound.");
            using var json = JsonDocument.Parse(Read(receipt, 1048576, Check));
            if (json.RootElement.GetArrayLength() > 256) throw new IOException("Outer receipt identity bound256.");
            foreach (var identity in json.RootElement.EnumerateArray())
            {
                Check(); var pid = identity.GetProperty("Pid").GetInt32();
                if (!actual.TryGetValue(pid, out var live)) continue;
                var command = identity.GetProperty("AfterExecCommandLine").GetString() ?? identity.GetProperty("CommandLine").GetString();
                var chain = identity.GetProperty("ParentChain").EnumerateArray().Select(value => value.GetInt32()).ToArray();
                if (identity.GetProperty("Platform").GetString() == "linux" && identity.GetProperty("ParentPid").GetInt32() == live.Parent &&
                    identity.GetProperty("StartTicks").ValueKind == JsonValueKind.Number && identity.GetProperty("StartTicks").GetUInt64() == live.Ticks &&
                    identity.GetProperty("BootId").GetString() == live.Boot && command == live.Command &&
                    identity.GetProperty("PendingObservationError").ValueKind == JsonValueKind.Null &&
                    chain.Length is > 0 and <= 64 && chain[^1] == live.Parent && chain.Contains(fixturePid)) matched.Add(pid);
            }
        }
        return matched.Count == pids.Count;
    }
    private static (int Parent, ulong Ticks, string State) Stat(string text)
    {
        var closing = text.LastIndexOf(')');
        if (closing < 0) throw new IOException("Native stat header differs.");
        var fields = text[(closing + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is < 20 or > 128) throw new IOException("Native stat field bound.");
        return (int.Parse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture), ulong.Parse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture), fields[0]);
    }
    private static byte[] Read(string path, int maximum, Action check)
    {
        using var input = File.OpenRead(path); using var output = new MemoryStream();
        var bytes = new byte[16384];
        for (var block = 0; block < maximum / bytes.Length + 2; block++)
        {
            check(); var count = input.Read(bytes);
            if (count == 0) return output.ToArray();
            if (output.Length + count > maximum) throw new IOException("Actual fixture metadata byte bound.");
            output.Write(bytes, 0, count);
        }
        throw new IOException("Actual fixture metadata read bound.");
    }
}

internal static class FixtureProcesses
{
    internal static unsafe Process Start(ProcessStartInfo start)
    {
        if (!OperatingSystem.IsWindows()) return Process.Start(start) ?? throw new IOException("Fixture child did not start.");
        var info = new FixtureNative.StartupInfo
        {
            Size = (uint)sizeof(FixtureNative.StartupInfo), Flags = 0x100,
            Input = FixtureNative.GetStdHandle(-10), Output = FixtureNative.GetStdHandle(-11), Error = FixtureNative.GetStdHandle(-12)
        };
        var command = (Quote(start.FileName) + " " + string.Join(' ', start.ArgumentList.Select(Quote)) + '\0').ToCharArray();
        fixed (char* pointer = command)
        {
            if (FixtureNative.CreateProcess(start.FileName, pointer, 0, 0, 1, 8 | 0x08000000, 0, Environment.CurrentDirectory, ref info, out var child) == 0)
                throw new IOException("Fixture detached CreateProcess failed: " + Marshal.GetLastPInvokeError());
            try
            {
                var process = Process.GetProcessById(checked((int)child.Pid));
                _ = process.Handle; // retain the native birth identity before releasing creation handles
                return process;
            }
            finally { FixtureNative.CloseHandle(child.Thread); FixtureNative.CloseHandle(child.Process); }
        }
    }
    private static string Quote(string text)
    {
        if (text.Length > 32760) throw new InvalidDataException("Fixture argv bound.");
        var result = new StringBuilder("\""); var slash = 0; var clock = Stopwatch.StartNew();
        foreach (var ch in text)
        {
            if (clock.Elapsed.TotalSeconds >= 1) throw new TimeoutException("Fixture argv formatting deadline.");
            if (ch == '\\') { slash++; continue; }
            result.Append('\\', ch == '"' ? slash * 2 + 1 : slash).Append(ch); slash = 0;
        }
        return result.Append('\\', slash * 2).Append('"').ToString();
    }
}
