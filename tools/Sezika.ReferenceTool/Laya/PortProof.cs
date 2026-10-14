using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Sezika.PortProvenance;

namespace Sezika.ReferenceTool.Laya;

internal static class PortProof
{
    internal const string SourceRevision = "4066d5d5fbf08b66c6757ddeedbd797bd7655bc0";
    internal const string ModelRevision = "052592a15d198d9ad47da779604259b10b47b7aa";
    internal const string CommonSha = "4ab41be6a4d9fdbad74eccf4f64c43e2c7404d227988d97ed8233ded8b182cb1";
    internal const string AgentSha = "1473ffd335a1becf55bf7aadde6d31f9d6be27862f5deaa5f4ebd3451fa154e3";
    internal const string LockSha = "901527bdd7da7732c2d12903a45dcc1984ac2187e14214ae780286c585f2e287";
    internal const string Kind = "independently_ported_original_laya_weights_capture";
    internal static (FilePin Evidence, PortProcess Process, FilePin BuildExecution, FilePin[] RuntimeFiles) Runtime(FilePin[] sources, FilePin executable, FilePin? assembly,
        FilePin build, string evidenceDirectory, Budget budget)
    {
        if (sources.Length is < 1 or > 64 || sources.Select(pin => Path.GetFullPath(pin.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Length) throw new InvalidDataException("Complete explicit C# source pin inventory bound64/duplicate.");
        foreach (var source in sources) { budget.Check(); if (Path.GetExtension(source.Path) is not (".cs" or ".csproj")) throw new InvalidDataException("Only C# project/source pins admitted."); budget.Verify(source, 4194304); }
        var summary = budget.Read(build, CompiledProgramJson.Default.PortBuildSummary, 4194304);
        if (summary.Status != "PASS_exact_reference_program_source_build_only" || !summary.ProgramSources.SequenceEqual(sources.Select(ProgramPin)) ||
            summary.RuntimeExecutable != ProgramPin(executable) || summary.RuntimeAssembly != (assembly is null ? null : ProgramPin(assembly)))
            throw new InvalidDataException("Compound actual build source/image/assembly binding differs from this request.");
        _ = CompiledProgramProof.Verify(summary, CancellationToken.None, budget.Check);
        var runtime = budget.Verify(executable, 536870912); var actual = Environment.ProcessPath ?? throw new IOException("Actual runtime image unavailable.");
        if (Path.GetFullPath(actual) != runtime.Path) throw new InvalidDataException("Actual runtime differs from pinned image.");
        var assemblyPath = typeof(PortProof).Assembly.Location;
        if (assemblyPath.Length == 0) { if (assembly is not null) throw new InvalidDataException("Native image has unexpected managed-assembly declaration."); }
        else if (assembly is null || budget.Verify(assembly, 536870912).Path != Path.GetFullPath(assemblyPath)) throw new InvalidDataException("Complete actual managed assembly pin missing/different.");
        Budget.Safe(evidenceDirectory); var pin = budget.Pin(Path.Combine(evidenceDirectory, "registered.json"), 1048576);
        var identities = budget.Read(pin, PortRegistrationJson.Default.PortProcessArray); if (identities.Length is < 1 or > 256) throw new InvalidDataException("Registered process count bound256.");
        var process = identities.SingleOrDefault(item => item.Pid == Environment.ProcessId) ?? throw new InvalidDataException("Current PID absent from caller's execution registration.");
        if (process.ParentPid <= 0 || process.ParentChain.Length is < 1 or > 64 || process.ParentChain[^1] != process.ParentPid ||
            process.ParentChain.Any(pid => pid <= 0 || pid == process.Pid) || string.IsNullOrEmpty(process.CommandLine) ||
            process.ScopeId != Environment.GetEnvironmentVariable("SEZIKA_EXECUTION_ID") || !Guid.TryParseExact(process.ScopeId, "N", out _)) throw new InvalidDataException("Registered argv/parent chain/ownership scope missing.");
        using var observed = Process.GetCurrentProcess(); string command;
        if (OperatingSystem.IsWindows())
        {
            if (process.Started is null || Math.Abs((process.Started.Value.UtcDateTime - observed.StartTime.ToUniversalTime()).TotalMilliseconds) > 1) throw new InvalidDataException("Actual Windows process birth differs.");
            if (NtQueryInformationProcess(observed.Handle, 0, out var info, (uint)Marshal.SizeOf<BasicInformation>(), out _) != 0 || info.Parent.ToInt64() != process.ParentPid) throw new InvalidDataException("Actual Windows parent differs.");
            command = WindowsCommandLine(budget);
        }
        else if (OperatingSystem.IsLinux())
        {
            var stat = File.ReadAllText("/proc/self/stat"); if (stat.Length > 16384) throw new InvalidDataException("Native proc-stat bound."); var end = stat.LastIndexOf(')');
            if (end < 0 || end + 2 >= stat.Length) throw new InvalidDataException("Malformed native process stat."); var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length is < 20 or > 64 || process.StartTicks != ulong.Parse(fields[19], CultureInfo.InvariantCulture) ||
                process.ParentPid != int.Parse(fields[1], CultureInfo.InvariantCulture) || process.BootId != File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim()) throw new InvalidDataException("Actual Linux PID/birth/boot/parent differs.");
            command = File.ReadAllText("/proc/self/cmdline", new UTF8Encoding(false, true));
        }
        else throw new PlatformNotSupportedException("Reference process proof requires Windows/Linux.");
        if (command.Length is < 1 or > 65536 || command != process.CommandLine && command != process.ExpectedExecCommandLine) throw new InvalidDataException("Actual runtime argv differs from registration/declared exec.");
        return (pin, process with { ActualCommandLine = command }, LocalPin(summary.BuildExecution), summary.RuntimeFiles.Select(LocalPin).ToArray());
    }
    private static ProgramFilePin ProgramPin(FilePin pin) => new(pin.Path, pin.Bytes, pin.Sha256);
    private static FilePin LocalPin(ProgramFilePin pin) => new(pin.Path, pin.Bytes, pin.Sha256);
    internal static void VerifyRuntimeAfter(FilePin[] files, Budget budget)
    {
        if (files.Length is < 4 or > 256) throw new InvalidDataException("Complete framework-dependent runtime inventory count bound256.");
        var assembly = files.Single(pin => Path.GetFileName(pin.Path) == "Sezika.ReferenceTool.dll");
        foreach (var file in files) budget.Verify(file, 536870912);
        CompiledProgramProof.VerifyRuntimeDirectoryFiles(Path.GetDirectoryName(assembly.Path)!, files.Select(ProgramPin).ToArray(), budget.Check);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation { internal IntPtr Reserved1, Peb, Reserved2, Reserved3, Pid, Parent; }
    private static string WindowsCommandLine(Budget budget)
    {
        // This UTF16 buffer belongs to the system. Copy its exact characters without re-quoting; never free or modify it.
        var pointer = GetCommandLineW(); if (pointer == IntPtr.Zero) throw new IOException("Native Windows command line unavailable.");
        for (var length = 0; length <= 32767; length++)
        {
            if ((length & 255) == 0) budget.Check();
            if (Marshal.ReadInt16(pointer, checked(length * 2)) == 0)
                return Marshal.PtrToStringUni(pointer, length) ?? throw new IOException("Native Windows command line copy failed.");
        }
        throw new InvalidDataException("Native Windows command line character bound32767.");
    }
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCommandLineW();
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, out BasicInformation information, uint bytes, out uint returned);
}
