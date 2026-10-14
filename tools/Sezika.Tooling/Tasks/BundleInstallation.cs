using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record BundleDependencyProbe(string Path, string Argument, int ExpectedExitCode = 0, string? ExpectedStandardError = null);
internal sealed record BundleInstallationRequest(string Archive, string ArchiveSha256, string Destination,
    ReviewFilePin[] Files, string[] Executables, BundleDependencyProbe[] Probes, string EvidenceDirectory,
    string Mode = "install", long MaximumBytes = 2_621_440_000, int ProbeSeconds = 5, string? BrokerExecutable = null);
internal sealed record BundleProbeResult(string Path, string Status, int? ExitCode, string OriginalExecutionStatus,
    string? NativeReceipt, string? NativeReceiptSha256);
internal sealed record BundleInstallationReport(string Status, string Directory, string ArchiveSha256, int Files,
    int Executables, BundleProbeResult[] Probes, bool ExistingFilesOnly, bool DependencyProbesPassed,
    bool SyntheticTransport, string? Failure, bool PartialOutputsPreserved = true, bool ServerAccepted = false,
    bool TrainingRun = false, bool ModelQualityAccepted = false);

internal static class BundleInstallation
{
    internal static Task<BundleInstallationReport> RunAsync(BundleInstallationRequest request, Budget budget,
        CancellationToken cancellation) => RunCoreAsync(request, budget, cancellation, null);

    // Test-only injection is never selected from a JSON request. Such reports
    // remain explicitly synthetic even when the adapter uses a real native echo.
    internal static Task<BundleInstallationReport> RunSyntheticAsync(BundleInstallationRequest request, Budget budget,
        CancellationToken cancellation, Func<ExecutionRequest, Budget, CancellationToken, Task<ExecutionResult>> execute)
        => RunCoreAsync(request, budget, cancellation, execute);

    private static async Task<BundleInstallationReport> RunCoreAsync(BundleInstallationRequest request, Budget budget,
        CancellationToken cancellation, Func<ExecutionRequest, Budget, CancellationToken, Task<ExecutionResult>>? execute)
    {
        budget.Check(); cancellation.ThrowIfCancellationRequested(); TaskOperations.Sha(request.ArchiveSha256);
        if (request.Mode is not ("install" or "verify") || request.Files.Length is < 2 or > 1000 ||
            request.Executables.Length is < 1 or > 16 || request.MaximumBytes is < 1 or > ArchiveOperations.MaximumBytes ||
            request.ProbeSeconds is < 1 or > 5 || request.Probes.Length != (request.Mode == "install" ? request.Executables.Length : 0))
            throw new InvalidDataException("Installation mode, complete file inventory or probe bounds differ.");
        if (!Path.IsPathFullyQualified(request.Archive) || !Path.IsPathFullyQualified(request.Destination) ||
            !Path.IsPathFullyQualified(request.EvidenceDirectory)) throw new InvalidDataException("Absolute installation paths required.");
        var archive = Path.GetFullPath(request.Archive); var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        var evidence = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.EvidenceDirectory));
        SafePaths.NoLinks(archive, budget); SafePaths.NoLinks(destination, budget); SafePaths.NoLinks(evidence, budget);
        if (destination.StartsWith("\\\\", StringComparison.Ordinal) || evidence.StartsWith("\\\\", StringComparison.Ordinal) ||
            Same(destination, Path.GetPathRoot(destination)!) || Same(evidence, Path.GetPathRoot(evidence)!) ||
            Same(destination, evidence) || Beneath(destination, evidence) || Beneath(evidence, destination) || Beneath(archive, destination) ||
            !Directory.Exists(Path.GetDirectoryName(destination)) || !Directory.Exists(Path.GetDirectoryName(evidence)))
            throw new InvalidDataException("Installation/evidence roots must be separate local paths with existing parents.");
        RegularFiles.Require(archive, budget);
        if (new FileInfo(archive).Length is < 1 || new FileInfo(archive).Length > request.MaximumBytes)
            throw new InvalidDataException("Uncompressed TAR exceeds the declared installation byte bound.");
        if (Path.Exists(evidence) || request.Mode == "install" && Path.Exists(destination) || request.Mode == "verify" && !Directory.Exists(destination))
            throw new IOException("Fresh installation/evidence or existing verify root required; preserve prior output.");
        var pins = new Dictionary<string, ReviewFilePin>(StringComparer.OrdinalIgnoreCase); long bytes = 0;
        foreach (var pin in request.Files)
        {
            budget.Check(); var relative = SafePaths.Relative(pin.Path); TaskOperations.Sha(pin.Sha256);
            if (relative != pin.Path || TaskOperations.IsScript(relative) || pin.Bytes < 0 || pin.Bytes > request.MaximumBytes - bytes || !pins.TryAdd(relative, pin))
                throw new InvalidDataException("Unique portable nonscript complete installation pins required.");
            bytes += pin.Bytes;
        }
        if (!pins.ContainsKey("files.sha256")) throw new InvalidDataException("Installation pins must include files.sha256 itself.");
        var executableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var executable in request.Executables)
            if (SafePaths.Relative(executable) != executable || !pins.ContainsKey(executable) || !executableNames.Add(executable))
                throw new InvalidDataException("Installed executable must be a unique pinned inventory member.");
        var probeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var probe in request.Probes)
        {
            budget.Check();
            if (!executableNames.Contains(probe.Path) || !probeNames.Add(probe.Path) || probe.Argument is not ("--help" or "--version") ||
                probe.ExpectedExitCode is < 0 or > 2 || probe.ExpectedStandardError?.Length > 1024 ||
                probe.ExpectedExitCode != 0 && string.IsNullOrWhiteSpace(probe.ExpectedStandardError))
                throw new InvalidDataException("Dependency probes require one fixed help/version call per pinned executable and a precise nonzero stderr target.");
        }
        Directory.CreateDirectory(evidence);
        var marker = Path.Combine(evidence, ".sezika-installation-in-progress");
        using (var markerStream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            markerStream.Write(Encoding.UTF8.GetBytes("Owned incomplete installation evidence; not an accepted installation.\n"));
            markerStream.Flush(true);
        }
        TaskOperations.WriteNew(Path.Combine(evidence, "request.json"), request, BundleInstallationJson.Default.BundleInstallationRequest);
        var results = new List<BundleProbeResult>(16); var synthetic = execute is not null;
        BundleInstallationReport Report(string status, bool passed, string? failure = null) => new(status, destination,
            request.ArchiveSha256, pins.Count, executableNames.Count, results.ToArray(), request.Mode == "verify", passed, synthetic, failure);
        try
        {
            if (request.Mode == "install") ArchiveOperations.Extract(archive, request.ArchiveSha256, destination, budget);
            else TaskOperations.VerifyFile(archive, new FileInfo(archive).Length, request.ArchiveSha256, budget);
            VerifyInventory(destination, pins, request.MaximumBytes, budget);
            foreach (var executable in request.Executables)
            {
                budget.Check(); cancellation.ThrowIfCancellationRequested(); var full = SafePaths.Within(destination, executable, budget);
                ProcessExecutor.ValidateExecutable(full, budget);
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    if (request.Mode == "install") File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    if ((File.GetUnixFileMode(full) & UnixFileMode.UserExecute) == 0) throw new InvalidDataException("Pinned native entrypoint lacks its owner execute permission.");
                }
            }
            for (var index = 0; index < request.Probes.Length; index++)
            {
                budget.Check(); cancellation.ThrowIfCancellationRequested();
                if (budget.RemainingSeconds < request.ProbeSeconds + 10) throw new TimeoutException("Installation has no complete dependency probe and cleanup reserve.");
                var probe = request.Probes[index]; var full = SafePaths.Within(destination, probe.Path, budget);
                TaskOperations.VerifyFile(full, pins[probe.Path].Bytes, pins[probe.Path].Sha256, budget);
                var execution = new ExecutionRequest { Executable = full, ArgumentList = [probe.Argument], WorkingDirectory = destination,
                    OutputDirectory = Path.Combine(evidence, "probe-" + index.ToString("D2", CultureInfo.InvariantCulture)), TimeoutSeconds = request.ProbeSeconds,
                    CleanupSeconds = 10, MaximumProcesses = 8, MaximumInputBytes = 0, MaximumOutputBytes = 65536, BrokerExecutable = request.BrokerExecutable };
                var result = execute is null ? await ProcessExecutor.ExecuteAsync(execution, budget, cancellation).ConfigureAwait(false)
                    : await execute(execution, budget, cancellation).ConfigureAwait(false);
                var expectedErrors = probe.ExpectedExitCode == 0 ? Array.Empty<string>() : ["Process exit code " + probe.ExpectedExitCode.ToString(CultureInfo.InvariantCulture)];
                var accepted = result.ExitCode == probe.ExpectedExitCode && result.ExecutionStatus == (probe.ExpectedExitCode == 0 ? "SUCCEEDED" : "FAILED") &&
                    result.Status == (probe.ExpectedExitCode == 0 ? "PASS" : "FAIL") && result.CleanupStatus == "PASS" && result.RemainingProcesses == 0 &&
                    result.InputComplete && result.OutputComplete && !result.OutputLimitExceeded && result.Errors.SequenceEqual(expectedErrors) &&
                    Same(result.Executable, full) && Same(result.OutputDirectory, execution.OutputDirectory) && result.ArgumentList.SequenceEqual(execution.ArgumentList);
                string? native = null; string? nativeSha = null;
                if (!synthetic)
                {
                    native = Path.Combine(execution.OutputDirectory, "result.json"); RegularFiles.Require(native, budget);
                    if (result.RootPid is null or <= 0 || result.Processes.Length == 0) accepted = false;
                    using var stream = File.OpenRead(native);
                    if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Native dependency receipt exceeds16MiB.");
                    nativeSha = budget.Hash(stream, stream.Length);
                }
                if (probe.ExpectedStandardError is { Length: > 0 } target)
                {
                    var stderr = Path.Combine(execution.OutputDirectory, "stderr.bin"); RegularFiles.Require(stderr, budget);
                    if (new FileInfo(stderr).Length > 65536 ||
                        NormalizeStandardError(File.ReadAllText(stderr, new UTF8Encoding(false, true))) != NormalizeStandardError(target)) accepted = false;
                }
                results.Add(new(probe.Path, accepted ? "PASS_declared_dependency_probe_only" : "FAIL_dependency_probe", result.ExitCode, result.Status, native, nativeSha));
                if (!accepted) throw new InvalidDataException("Pinned native dependency probe failed; preserve installation and original execution evidence.");
            }
            VerifyInventory(destination, pins, request.MaximumBytes, budget);
            var report = Report(request.Mode == "verify" ? "PASS_existing_installation_bytes_permissions_only_dependencies_not_rerun"
                : synthetic ? "PASS_SYNTHETIC_INSTALLATION_CONTRACT_ONLY" : "PASS_fresh_installation_bytes_permissions_dependency_probes_only", request.Mode == "install");
            File.Delete(marker);
            WriteFinal(evidence, "installation.passed.pending.json", report);
            return report;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or TimeoutException or OperationCanceledException)
        {
            var failed = Report("FAIL_installation_preserve_partial_and_native_receipts", false, error.GetType().Name + ": " + error.Message);
            WriteFinal(evidence, "installation.failed.pending.json", failed);
            return failed;
        }
    }

    private static void WriteFinal(string evidence, string pendingName, BundleInstallationReport report)
    {
        var pending = Path.Combine(evidence, pendingName);
        TaskOperations.WriteNew(pending, report, BundleInstallationJson.Default.BundleInstallationReport);
        File.Move(pending, Path.Combine(evidence, "installation.json"), false);
    }

    private static bool Same(string left, string right) => Path.GetFullPath(left).Equals(Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string NormalizeStandardError(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.EndsWith('\n') ? normalized[..^1] : normalized;
    }
    private static bool Beneath(string path, string directory) => path.StartsWith(directory + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void VerifyInventory(string root, Dictionary<string, ReviewFilePin> pins, long maximumBytes, Budget budget)
    {
        var pending = new Queue<(string Path, int Depth)>(); pending.Enqueue((root, 0));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var entries = 0; long bytes = 0;
        for (var directoryIndex = 0; directoryIndex < 1006 && pending.Count != 0; directoryIndex++)
        {
            budget.Check(); var directory = pending.Dequeue(); SafePaths.NoLinks(directory.Path, budget);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                budget.Check(); if (++entries > 1006) throw new InvalidDataException("Installation directory/member bound1006.");
                SafePaths.NoLinks(path, budget);
                if (Directory.Exists(path)) { if (directory.Depth >= 32 || pending.Count >= 1006) throw new InvalidDataException("Installation depth/queue bound."); pending.Enqueue((path, directory.Depth + 1)); continue; }
                var name = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!seen.Add(name) || !pins.TryGetValue(name, out var pin)) throw new InvalidDataException("Installation contains an undeclared or duplicate regular file.");
                RegularFiles.Require(path, budget); var actualBytes = new FileInfo(path).Length;
                if (actualBytes > maximumBytes - bytes) throw new InvalidDataException("Installed byte bound exceeded.");
                bytes += actualBytes; TaskOperations.VerifyFile(path, pin.Bytes, pin.Sha256, budget);
            }
        }
        if (pending.Count != 0 || !seen.SetEquals(pins.Keys)) throw new InvalidDataException("Installation inventory does not cover the complete pinned file set.");
    }
}

[JsonSerializable(typeof(BundleInstallationRequest))]
[JsonSerializable(typeof(BundleInstallationReport))]
[JsonSourceGenerationOptions(WriteIndented = true, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class BundleInstallationJson : System.Text.Json.Serialization.JsonSerializerContext;
