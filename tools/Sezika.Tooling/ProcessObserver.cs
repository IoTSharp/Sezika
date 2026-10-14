using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Sezika.Tooling;

internal static class ProcessObserver
{
    internal static ProcessObservation Observe(int pid) => Observe(pid, null);

    internal static ProcessObservation Observe(int pid, Budget? budget = null)
    {
        budget?.Check();
        var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unsupported";
        try
        {
            if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
            ProcessObservation observation = platform switch
            {
                "windows" => Windows(pid),
                "linux" => Linux(pid, budget),
                _ => new("Unknown", pid, 0, null, null, platform, null, "Unsupported observer platform.")
            };
            budget?.Check();
            return observation;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OverflowException)
        {
            budget?.Check();
            return new("Unknown", pid, 0, null, null, platform, null, error.GetType().Name);
        }
    }

    private static unsafe ProcessObservation Windows(int pid)
    {
        using var handle = Native.OpenProcess(0x00101000, 0, (uint)pid); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            return new(error == 87 ? "Absent" : "Unknown", pid, 0, null, null, "windows", null, $"OpenProcess:{error}");
        }
        if (Native.WaitForSingleObject(handle, 0) == 0) return new("Absent", pid, 0, null, null, "windows", null);
        if (Native.GetProcessTimes(handle, out var created, out _, out _, out _) == 0)
            return new("Unknown", pid, 0, null, null, "windows", null, "GetProcessTimes failed.");
        Native.BasicInformation basic = default;
        if (Native.NtQueryInformationProcess(handle, 0, &basic, (uint)sizeof(Native.BasicInformation), out _) < 0 || basic.Pid != (nuint)pid)
            return new("Unknown", pid, 0, null, null, "windows", null, "Parent identity query failed.");
        var bytes = new byte[65536];
        string commandLine;
        fixed (byte* address = bytes)
        {
            var status = Native.NtQueryInformationProcess(handle, 60, address, (uint)bytes.Length, out _);
            if (status < 0) return new("Unknown", pid, (int)basic.ParentPid, null, null, "windows", null, $"CommandLineInformation:{status:x8}");
            var value = *(Native.UnicodeString*)address;
            var offset = (byte*)value.Buffer - address;
            if (value.Length % 2 != 0 || offset < sizeof(Native.UnicodeString) || offset > bytes.Length - value.Length)
                throw new InvalidOperationException("Command line native buffer bounds differ.");
            commandLine = new string((char*)value.Buffer, 0, value.Length / 2);
        }
        if (Native.WaitForSingleObject(handle, 0) == 0) return new("Absent", pid, 0, null, null, "windows", null);
        return new("Present", pid, checked((int)basic.ParentPid), DateTimeOffset.FromFileTime(created).ToUniversalTime(), commandLine, "windows", null);
    }

    private static ProcessObservation Linux(int pid, Budget? budget)
    {
        var root = "/proc/" + pid.ToString(CultureInfo.InvariantCulture);
        try
        {
            var before = ParseLinuxStat(Encoding.UTF8.GetString(ReadBounded(root + "/stat", budget)), pid);
            var command = Encoding.UTF8.GetString(ReadBounded(root + "/cmdline", budget));
            var after = ParseLinuxStat(Encoding.UTF8.GetString(ReadBounded(root + "/stat", budget)), pid);
            if (before.ParentPid != after.ParentPid || before.StartTicks != after.StartTicks)
                return new("Unknown", pid, after.ParentPid, null, null, "linux", after.StartTicks, "Identity changed during observation.");
            if (after.State is "Z" or "X")
                return new("Unknown", pid, after.ParentPid, null, null, "linux", after.StartTicks, "Exited/zombie process still has proc metadata; reaping is unverified.");
            // Keep NUL separators: reconstructing a shell command line would lose argv identity.
            var bootId = Encoding.ASCII.GetString(ReadBounded("/proc/sys/kernel/random/boot_id", budget)).Trim();
            if (!Guid.TryParse(bootId, out _)) throw new InvalidDataException("Invalid Linux boot identity.");
            return new("Present", pid, after.ParentPid, null, command, "linux", after.StartTicks, BootId: bootId);
        }
        catch (FileNotFoundException) { return new("Absent", pid, 0, null, null, "linux", null); }
        catch (DirectoryNotFoundException) { return new("Absent", pid, 0, null, null, "linux", null); }
    }

    internal static (int ParentPid, ulong StartTicks, string State) ParseLinuxStat(string text, int expectedPid)
    {
        var opening = text.IndexOf('(');
        var closing = text.LastIndexOf(')');
        if (opening < 2 || closing <= opening || !int.TryParse(text[..opening].Trim(), out var pid) || pid != expectedPid)
            throw new InvalidDataException("Malformed /proc process header.");
        var fields = text[(closing + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length is < 20 or > 128 || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent) ||
            !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            throw new InvalidDataException("Malformed /proc identity fields.");
        return (parent, ticks, fields[0]);
    }

    private static byte[] ReadBounded(string path, Budget? budget)
    {
        budget?.Check();
        using var stream = File.OpenRead(path);
        var bytes = new byte[65537];
        var count = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var attempt = 0; attempt < 65; attempt++)
        {
            budget?.Check();
            if (watch.Elapsed.TotalSeconds >= 2) throw new IOException("Process metadata deadline2s.");
            var size = stream.Read(bytes.AsSpan(count, Math.Min(1024, bytes.Length - count)));
            budget?.Check();
            count += size;
            if (count > 65536) throw new InvalidDataException("Process metadata exceeds 64 KiB.");
            if (size == 0) return bytes[..count];
        }
        throw new InvalidDataException("Process metadata read iteration bound exceeded.");
    }
}

internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BasicInformation
    {
        internal nint ExitStatus;
        internal nint Peb;
        internal nuint Affinity;
        internal nint BasePriority;
        internal nuint Pid;
        internal nuint ParentPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal nint Buffer;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial SafeProcessHandle OpenProcess(uint access, int inherit, uint pid);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int GetProcessTimes(SafeProcessHandle handle, out long creation, out long exit, out long kernel, out long user);
    [LibraryImport("ntdll.dll")]
    internal static unsafe partial int NtQueryInformationProcess(SafeProcessHandle handle, int infoClass, void* buffer, uint length, out uint returnedLength);
}
