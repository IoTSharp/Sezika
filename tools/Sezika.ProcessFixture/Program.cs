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
var mode = args[0];
switch (mode)
{
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
        for (var childIndex = 0; childIndex < 12 && clock.Elapsed.TotalSeconds < 1; childIndex++)
        {
            var lateStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            lateStart.ArgumentList.Add("sleep");
            using var late = FixtureProcesses.Start(lateStart);
            Console.WriteLine("fixture-late-child=" + late.Id.ToString(CultureInfo.InvariantCulture));
            Thread.Sleep(30);
        }
        for (var attempt = 0; attempt < 150 && clock.Elapsed.TotalSeconds < 15; attempt++) Thread.Sleep(100);
        return 0;
    case "supervisor":
        if (args.Length != 3) return 64;
        using (var requestJson = JsonDocument.Parse(File.ReadAllBytes(args[2])))
        {
            var innerDirectory = requestJson.RootElement.GetProperty("OutputDirectory").GetString()!;
            var ownerStart = new ProcessStartInfo(args[1]) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "process", "run", "--request", args[2], "--output", innerDirectory + ".cli.json" }) ownerStart.ArgumentList.Add(argument);
            using var owner = FixtureProcesses.Start(ownerStart);
            Console.WriteLine("fixture-owner=" + owner.Id + ";started=" + owner.StartTime.ToUniversalTime().ToString("O") + ";executable=" + args[1]);
            var observed = new Dictionary<int, (DateTimeOffset? Started, ulong? Ticks)>();
            try
            {
                for (var poll = 0; poll < 80 && clock.Elapsed.TotalSeconds < 4; poll++)
                {
                    if (Directory.Exists(innerDirectory))
                    {
                        var files = 0;
                        foreach (var receipt in Directory.EnumerateFiles(innerDirectory, "registered*.json", SearchOption.TopDirectoryOnly))
                        {
                            if (++files > 257 || clock.Elapsed.TotalSeconds >= 4) throw new IOException("Supervisor fixture identity inventory bound.");
                            using var json = JsonDocument.Parse(File.ReadAllBytes(receipt));
                            foreach (var identity in json.RootElement.EnumerateArray())
                            {
                                if (identity.GetProperty("Ownership").GetString() == "linux-watchdog") continue;
                                var pid = identity.GetProperty("Pid").GetInt32();
                                var stamp = identity.GetProperty("Started"); var ticks = identity.GetProperty("StartTicks");
                                observed[pid] = (stamp.ValueKind == JsonValueKind.Null ? null : stamp.GetDateTimeOffset(), ticks.ValueKind == JsonValueKind.Null ? null : ticks.GetUInt64());
                            }
                        }
                    }
                    if (observed.Count >= 2) break;
                    if (owner.HasExited) throw new IOException("Supervisor exited before root/child registration.");
                    Thread.Sleep(50);
                }
                if (observed.Count < 2) throw new IOException("Root/child identities were not registered.");
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
