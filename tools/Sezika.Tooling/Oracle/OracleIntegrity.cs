using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sezika.Tooling;

internal static class OracleIntegrity
{
    internal static void Require([DoesNotReturnIf(false)] bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    internal static bool IsSha(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static OracleFilePin Pin(string path, Budget budget, long maximum = 33554432)
    {
        Require(Path.IsPathFullyQualified(path), "Oracle artifact requires an explicit absolute path.");
        RegularFiles.Require(path, budget); using var input = File.OpenRead(path);
        Require(input.Length is > 0 && input.Length <= maximum, "Oracle artifact byte bound.");
        return new(Path.GetFullPath(path), input.Length, budget.Hash(input, input.Length));
    }
    internal static void Verify(OracleFilePin pin, Budget budget, long maximum = 2147483648)
    {
        Require(pin is not null && Path.IsPathFullyQualified(pin.Path) && Path.GetFullPath(pin.Path) == pin.Path &&
            pin.Bytes is >= 0 && pin.Bytes <= maximum && IsSha(pin.Sha256), "Oracle file pin is incomplete.");
        TaskOperations.VerifyFile(pin.Path, pin.Bytes, pin.Sha256, budget);
    }
    internal static void Package(OraclePackagePin package, Budget budget)
    {
        Require(package is not null && Path.IsPathFullyQualified(package.Root) && Path.GetFullPath(package.Root) == package.Root &&
            package.Files is { Length: > 0 and <= 2000 }, "Complete explicit Oracle package required.");
        SafePaths.NoLinks(package.Root, budget); Require(Directory.Exists(package.Root), "Frozen Oracle package root is absent.");
        var declared = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal); long total = 0;
        foreach (var file in package.Files)
        {
            budget.Check(); var relative = SafePaths.Relative(file.RelativePath);
            Require(declared.Add(relative), "Duplicate Oracle package path."); total = checked(total + file.Bytes);
            Require(total <= 2147483648, "Oracle package byte bound2GiB.");
            Verify(new(SafePaths.Within(package.Root, relative, budget), file.Bytes, file.Sha256), budget);
        }
        // This is integrity coverage of one explicitly declared small package,
        // not compiler discovery or a workspace-wide source scan.
        var directories = new Queue<(string Path, int Depth)>(); directories.Enqueue((package.Root, 0)); var entries = 0; var files = 0;
        for (var scan = 0; scan < 2001 && directories.Count > 0; scan++)
        {
            budget.Check(); var directory = directories.Dequeue(); Require(directory.Depth <= 32, "Oracle package directory depth bound32.");
            foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                budget.Check(); Require(++entries <= 4000, "Oracle package inventory entry bound4000."); SafePaths.NoLinks(path, budget);
                if (Directory.Exists(path)) { directories.Enqueue((path, directory.Depth + 1)); continue; }
                var relative = Path.GetRelativePath(package.Root, path).Replace('\\', '/');
                Require(declared.Contains(relative), "Unmanifested Oracle source/runtime/code byte path."); files++;
            }
        }
        Require(directories.Count == 0 && files == declared.Count, "Oracle package manifest coverage differs.");
    }
    internal static void VerifyProgram(OracleProgramPin program, string name, Budget budget, bool simulation)
    {
        Package(program.RuntimeAndCode, budget); Package(program.Source, budget);
        var fileName = Path.GetFileName(program.Executable); var application = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fileName[..^4] : fileName;
        Require(Path.IsPathFullyQualified(program.Executable) && application == name,
            "Oracle role must use its fixed project native apphost.");
        var relative = Path.GetRelativePath(program.RuntimeAndCode.Root, program.Executable).Replace('\\', '/');
        Require(program.RuntimeAndCode.Files.Any(file => file.RelativePath == relative) &&
            SafePaths.Within(program.RuntimeAndCode.Root, relative, budget) == program.Executable, "Oracle executable is outside its complete runtime/code pin.");
        if (!simulation) ProcessExecutor.ValidateExecutable(program.Executable, budget);
    }
    internal static T Read<T>(string path, JsonTypeInfo<T> type, Budget budget, long maximum = 33554432) where T : class
    {
        using var document = Document(path, budget, maximum);
        return document.RootElement.Deserialize(type) ?? throw new InvalidDataException("Oracle typed JSON is null.");
    }
    internal static JsonDocument Document(string path, Budget budget, long maximum = 33554432)
    {
        RegularFiles.Require(path, budget); using var input = File.OpenRead(path); budget.Check();
        Require(input.Length is > 0 && input.Length <= maximum, "Oracle JSON byte bound.");
        return JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 32, AllowDuplicateProperties = false });
    }
    internal static string Fingerprint(OracleBatchRequest request)
    {
        var identity = new OracleTaskFingerprint(request.TaskId, request.Plan, request.Contract, request.Model, request.ReferenceTool,
            request.ActualTool, request.CompareTool, request.ReferenceInputs, request.MaximumBatches, request.BatchOffset, request.RequireActualAot, request.BrokerTool, request.LegacyFrozenReferenceIndex);
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity, OracleJsonContext.Default.OracleTaskFingerprint)));
    }
    internal static void VerifyExecution(ExecutionResult result, bool comparison, bool simulation, Budget budget)
    {
        var expectedDifference = comparison && result.ExitCode == 1;
        Require(result.ExitCode is 0 || expectedDifference, "Oracle stage execution failed; numerical difference is only comparator exit1.");
        Require(result.CleanupStatus == "PASS" && result.RemainingProcesses == 0 && result.InputComplete && result.OutputComplete && !result.OutputLimitExceeded,
            "Oracle stage cleanup/input/output EOF was not proven.");
        Require(result.Errors.Length == 0 || expectedDifference && result.Errors.SequenceEqual(new[] { "Process exit code 1" }), "Oracle executor retained a non-numerical failure.");
        if (simulation) { Require(result.Status == "SIMULATION", "Simulation executor must explicitly mark its receipt."); return; }
        Require(result.RootPid is > 0 && result.Launcher is not null && result.Processes.Length is > 0 and <= 64 &&
            result.Status == (expectedDifference ? "FAIL" : "PASS") && result.ExecutionStatus == (expectedDifference ? "FAILED" : "SUCCEEDED"), "Oracle native process receipt status/identity is incomplete.");
        foreach (var identity in result.Processes)
        {
            budget.Check();
            Require(identity.Pid > 0 && identity.ParentPid > 0 && identity.ParentChain.Length is > 0 and <= 64 && identity.CommandLine.Length > 0 &&
                identity.PendingObservationError is null && (identity.Platform == "windows" ? identity.Started is not null :
                identity.Platform == "linux" && identity.StartTicks is not null && identity.BootId is not null), "Oracle native birth/argv/parent identity is incomplete.");
            var command = identity.AfterExecCommandLine ?? identity.CommandLine;
            var image = identity.Platform == "linux" ? command.Split('\0')[0] : command.TrimStart().StartsWith('"')
                ? command.TrimStart().Split('"')[1] : command.TrimStart().Split(' ')[0];
            Require(!new[] { "sh", "bash", "dash", "pwsh", "powershell", "cmd", "python", "python3", "node", "cscript", "wscript" }
                .Contains(Path.GetFileNameWithoutExtension(image), StringComparer.OrdinalIgnoreCase), "Oracle runtime interpreter descendant is forbidden.");
        }
    }
}
