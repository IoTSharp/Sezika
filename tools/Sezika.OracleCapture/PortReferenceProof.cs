using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sezika.PortProvenance;

namespace Sezika.OracleProof;

internal static partial class PortReferenceProof
{
    internal const string ContractSchema = "sezika.laya-oracle-contract.port-derived.v2";
    internal const string ReferenceSchema = "sezika.laya-oracle.port-derived.v2";
    internal const string ActualSchema = "sezika.laya-oracle.actual.v2";
    private const string Kind = "independently_ported_original_laya_weights_capture";

    internal static void Validate(byte[] referenceBytes, byte[] contractBytes, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew(); long hashed = 0;
        var verifiedPins = new Dictionary<string, (long Bytes, string Sha)>(PathComparer);
        void Check()
        {
            cancellation.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds > 30) throw new TimeoutException("Port reference proof deadline30s.");
        }
        void Require(bool condition, string reason)
        {
            Check(); if (!condition) throw new InvalidDataException("Port reference proof: " + reason);
        }
        using var document = JsonDocument.Parse(referenceBytes, new JsonDocumentOptions { MaxDepth = 48, AllowDuplicateProperties = false });
        using var contractDocument = JsonDocument.Parse(contractBytes, new JsonDocumentOptions { MaxDepth = 48, AllowDuplicateProperties = false });
        var reference = document.RootElement; var contract = contractDocument.RootElement;
        Require(Text(contract, "schema_version") == ContractSchema && Text(contract, "output_schema_version") == ReferenceSchema &&
            Text(contract, "actual_output_schema_version") == ActualSchema, "explicit v2 reference/actual contract required");
        var tolerances = contract.GetProperty("tolerances");
        foreach (var (name, absolute, relative) in new[] { ("raw_logits", 0.0005, 0.0001), ("probabilities", 0.0001, 0.0001), ("score", 0.001, 0.0001) })
            Require(tolerances.GetProperty(name).GetProperty("absolute").GetDouble() == absolute &&
                tolerances.GetProperty(name).GetProperty("relative").GetDouble() == relative, "frozen numerical tolerance differs");
        Require(tolerances.GetProperty("near_tie_logit_margin").GetDouble() == 0.001, "near-tie tolerance differs");
        Require(Text(reference, "schema_version") == ReferenceSchema && Text(reference, "kind") == Kind &&
            !reference.GetProperty("fixture_only").GetBoolean() && !reference.GetProperty("upstream_executed").GetBoolean(), "port-derived reference cannot claim upstream execution");
        var provenance = reference.GetProperty("provenance");
        Require(Text(provenance, "implementation") == "laya-4066d5-transformers-4.57.1-csharp-fp32-v2" &&
            Text(provenance, "backend") == "independent_csharp_cpu_fp32" && !provenance.GetProperty("upstream_executed").GetBoolean() &&
            !provenance.GetProperty("represents_parity").GetBoolean() && !provenance.GetProperty("represents_quality").GetBoolean() &&
            !provenance.GetProperty("represents_calibration").GetBoolean(), "independent numerical port provenance differs");
        var policy = contract.GetProperty("port_reference_policy");
        Require(Text(provenance, "model_id") == "convaiinnovations/laya-multilingual" &&
            Text(provenance, "model_revision") == "052592a15d198d9ad47da779604259b10b47b7aa" &&
            Text(provenance, "upstream_source_revision") == "4066d5d5fbf08b66c6757ddeedbd797bd7655bc0" &&
            Text(provenance, "upstream_version") == "0.3.20" && provenance.GetProperty("max_len").GetInt32() == 1024 &&
            provenance.GetProperty("head_max_len").GetInt32() == 256 && Text(provenance, "temperature_policy") == "checkpoint_fixed_1_no_overrides" &&
            Text(provenance, "weights_sha256") == "9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204" &&
            Text(provenance, "tokenizer_sha256") == "609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f" &&
            Text(provenance, "contract_sha256") == Convert.ToHexStringLower(SHA256.HashData(contractBytes)), "fixed original model/source/contract policy differs");
        foreach (var name in new[] { "source_lock_sha256", "upstream_agent_sha256", "upstream_common_sha256", "encoder_config_sha256", "agent_config_sha256", "tokenizer_config_sha256" })
            Require(Text(provenance, name) == Text(policy, name), "frozen source/config identity differs: " + name);
        Require(provenance.GetProperty("effective_global_rope_theta").GetDouble() == 160000 &&
            provenance.GetProperty("effective_local_rope_theta").GetDouble() == 160000 &&
            provenance.GetProperty("batch_size").GetInt32() == 1, "fixed upstream effective numerical policy differs");
        var roles = provenance.GetProperty("tokenizer_roles");
        foreach (var (name, token, id) in new[] { ("cls", "<bos>", 2), ("sep", "<eos>", 1), ("mask", "<mask>", 4), ("pad", "<pad>", 0) })
            Require(Text(roles, name + "_token") == token && roles.GetProperty(name + "_id").GetInt32() == id, "frozen tokenizer role differs: " + name);
        var sources = provenance.GetProperty("program_sources");
        Require(sources.ValueKind == JsonValueKind.Array && sources.GetArrayLength() is >= 8 and <= 64, "complete bounded C# source pins required");
        var sourcePins = new Dictionary<string, (long Bytes, string Sha)>(PathComparer);
        foreach (var source in sources.EnumerateArray())
        {
            Check(); var pin = VerifyPin(source, 2 * 1024 * 1024);
            Require(Path.GetExtension(pin.Path) is ".cs" or ".csproj" && sourcePins.TryAdd(pin.Path, (pin.Bytes, pin.Sha)), "unique C# source/project pins required");
        }
        Require(sourcePins.Keys.Any(path => path.EndsWith("Sezika.ReferenceTool.csproj", StringComparison.Ordinal)), "standalone reference project pin missing");
        var runtime = VerifyPin(provenance.GetProperty("runtime_executable"), 128 * 1024 * 1024);
        (string Path, long Bytes, string Sha)? assembly = null;
        if (provenance.GetProperty("runtime_assembly").ValueKind != JsonValueKind.Null)
            assembly = VerifyPin(provenance.GetProperty("runtime_assembly"), 128 * 1024 * 1024);
        var build = VerifyPin(provenance.GetProperty("build_receipt"), 4 * 1024 * 1024);
        using var buildDocument = Read(build.Path, 4 * 1024 * 1024);
        var buildRoot = buildDocument.RootElement;
        Require(Text(buildRoot, "Status") == "PASS_exact_reference_program_source_build_only", "independent bound build receipt required");
        var compiled = buildRoot.Deserialize(CompiledProgramJson.Default.PortBuildSummary)
            ?? throw new InvalidDataException("Port proof compiled program summary is absent.");
        Require(CompiledProgramProof.Verify(compiled, cancellation, Check).Status == "SOURCE_BUILD_ONLY", "compiled source proof differs");
        var declaredRuntime = provenance.GetProperty("runtime_files");
        Require(declaredRuntime.ValueKind == JsonValueKind.Array && declaredRuntime.GetArrayLength() == compiled.RuntimeFiles.Length, "runtime provenance denominator differs");
        var runtimeNames = new HashSet<string>(PathComparer);
        foreach (var member in declaredRuntime.EnumerateArray())
        {
            Check(); var path = Path.GetFullPath(Text(member, "path"));
            var expected = compiled.RuntimeFiles.SingleOrDefault(pin => PathComparer.Equals(Path.GetFullPath(pin.Path), path));
            Require(runtimeNames.Add(path) && expected is not null && member.GetProperty("bytes").GetInt64() == expected.Bytes &&
                Text(member, "sha256") == expected.Sha256, "runtime provenance differs from the complete verified build inventory");
        }
        var buildExecution = VerifyPin(buildRoot.GetProperty("BuildExecution"), 4 * 1024 * 1024, pascal: true);
        Require(SameSnakePin(provenance.GetProperty("build_execution"), buildExecution), "captured compiler execution pin differs");
        using var buildExecutionDocument = Read(buildExecution.Path, 4 * 1024 * 1024);
        var nativeBuild = buildExecutionDocument.RootElement;
        Require(Text(nativeBuild, "Status") == "PASS" && Text(nativeBuild, "ExecutionStatus") == "SUCCEEDED" &&
            Text(nativeBuild, "CleanupStatus") == "PASS" && nativeBuild.GetProperty("ExitCode").GetInt32() == 0 &&
            nativeBuild.GetProperty("RemainingProcesses").GetInt32() == 0 && nativeBuild.GetProperty("Errors").GetArrayLength() == 0 &&
            nativeBuild.GetProperty("InputComplete").GetBoolean() && nativeBuild.GetProperty("OutputComplete").GetBoolean(), "actual native build/EOF/cleanup proof differs");
        var builtSources = buildRoot.GetProperty("ProgramSources");
        Require(builtSources.ValueKind == JsonValueKind.Array && builtSources.GetArrayLength() == sourcePins.Count, "build source denominator differs");
        var buildNames = new HashSet<string>(PathComparer);
        foreach (var source in builtSources.EnumerateArray())
        {
            Check(); var path = Path.GetFullPath(Text(source, "Path"));
            Require(buildNames.Add(path) && sourcePins.TryGetValue(path, out var pin) && source.GetProperty("Bytes").GetInt64() == pin.Bytes &&
                Text(source, "Sha256") == pin.Sha, "build source bytes/hash differ");
        }
        Require(SamePin(buildRoot.GetProperty("RuntimeExecutable"), runtime), "build/runtime executable differs");
        if (assembly is { } managed) Require(SamePin(buildRoot.GetProperty("RuntimeAssembly"), managed), "build/managed program differs");
        else Require(buildRoot.GetProperty("RuntimeAssembly").ValueKind == JsonValueKind.Null, "native program cannot substitute a managed assembly");
        var evidence = VerifyPin(provenance.GetProperty("process_evidence"), 4 * 1024 * 1024);
        using var evidenceDocument = Read(evidence.Path, 4 * 1024 * 1024);
        var records = evidenceDocument.RootElement;
        Require(records.ValueKind == JsonValueKind.Array && records.GetArrayLength() is > 0 and <= 256, "independent registered process array required");
        var process = provenance.GetProperty("process"); var pid = process.GetProperty("pid").GetInt32();
        var parent = process.GetProperty("parent_pid").GetInt32(); var command = Text(process, "command_line");
        var chain = process.GetProperty("parent_chain").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        Require(pid > 0 && parent > 0 && chain.Length is > 0 and <= 64 && chain[^1] == parent &&
            chain.Distinct().Count() == chain.Length && chain.All(value => value > 0 && value != pid), "complete process/parent identity required");
        var matches = records.EnumerateArray().Where(row => row.GetProperty("Pid").GetInt32() == pid).ToArray();
        Require(matches.Length == 1, "exactly one independently registered process required");
        var actual = matches[0];
        Require(actual.GetProperty("ParentPid").GetInt32() == parent && Text(actual, "CommandLine") == command &&
            actual.GetProperty("ParentChain").EnumerateArray().Select(item => item.GetInt32()).SequenceEqual(chain) &&
            Text(actual, "ScopeId") == Text(process, "scope_id") && Text(actual, "Ownership") == Text(process, "ownership") &&
            actual.GetProperty("PendingObservationError").ValueKind == JsonValueKind.Null, "independent process birth/argv/parent/scope differs");
        Require(Guid.TryParseExact(Text(process, "scope_id"), "N", out _), "exact native ownership scope required");
        if (Text(actual, "Platform") == "linux")
            Require(actual.GetProperty("StartTicks").GetUInt64() > 0 && actual.GetProperty("StartTicks").GetUInt64() == process.GetProperty("start_ticks").GetUInt64() &&
                Guid.TryParse(Text(actual, "BootId"), out _) && Text(actual, "BootId") == Text(process, "boot_id"), "Linux birth/boot proof differs");
        else
            Require(Text(actual, "Platform") == "windows" && DateTimeOffset.Parse(Text(actual, "Started"), CultureInfo.InvariantCulture) ==
                DateTimeOffset.Parse(Text(process, "started"), CultureInfo.InvariantCulture), "Windows creation time proof differs");

        var executionPath = Path.Combine(Path.GetDirectoryName(evidence.Path)!, "result.json");
        using var executionDocument = Read(executionPath, 4 * 1024 * 1024);
        var execution = executionDocument.RootElement;
        Require(Text(execution, "Status") == "PASS" && Text(execution, "ExecutionStatus") == "SUCCEEDED" &&
            Text(execution, "CleanupStatus") == "PASS" && execution.GetProperty("ExitCode").GetInt32() == 0 &&
            execution.GetProperty("RemainingProcesses").GetInt32() == 0 && execution.GetProperty("Errors").GetArrayLength() == 0 &&
            execution.GetProperty("InputComplete").GetBoolean() && execution.GetProperty("OutputComplete").GetBoolean() &&
            execution.GetProperty("RootPid").GetInt32() == pid && Text(execution, "Executable") == runtime.Path &&
            Text(execution, "Platform") == Text(actual, "Platform") &&
            Text(execution, "OwnershipId") == Text(process, "scope_id") &&
            Path.GetFullPath(Text(execution, "OutputDirectory")) == Path.GetDirectoryName(evidence.Path),
            "reference process is not this completed exact runtime stage");
        var finalProcesses = execution.GetProperty("Processes");
        Require(finalProcesses.GetArrayLength() is > 0 and <= 256, "final process denominator differs");
        var roots = finalProcesses.EnumerateArray().Where(item => item.GetProperty("Pid").GetInt32() == pid).ToArray();
        Require(roots.Length == 1, "exact final reference root required");
        var finalProcess = roots[0]; var nativeCommand = Text(process, "actual_command_line");
        var finalCommand = Text(finalProcess, "CommandLine");
        if (finalProcess.GetProperty("AfterExecCommandLine").ValueKind != JsonValueKind.Null)
            finalCommand = Text(finalProcess, "AfterExecCommandLine");
        else if (finalProcess.GetProperty("ExpectedExecCommandLine").ValueKind != JsonValueKind.Null)
        {
            var expectedExec = Text(finalProcess, "ExpectedExecCommandLine");
            Require(Text(actual, "Platform") == "linux" && expectedExec == nativeCommand &&
                Text(finalProcess, "Ownership") == "linux-process-group-pidfd-subreaper" &&
                Text(actual, "ExpectedExecCommandLine") == expectedExec && Text(process, "expected_exec_command_line") == expectedExec &&
                Text(finalProcess, "ExitProof").StartsWith("Complete registered native birth/parent/argv and retained pidfd termination/reaping signal;", StringComparison.Ordinal),
                "planned exec argv lacks registered birth and native termination proof");
            finalCommand = expectedExec;
        }
        Require(Text(finalProcess, "State") == "EXITED" && finalProcess.GetProperty("PendingObservationError").ValueKind == JsonValueKind.Null &&
            Text(finalProcess, "ScopeId") == Text(process, "scope_id") && finalProcess.GetProperty("ParentPid").GetInt32() == parent &&
            Text(finalProcess, "Platform") == Text(actual, "Platform") && Text(finalProcess, "Ownership") == Text(actual, "Ownership") &&
            finalProcess.GetProperty("ParentChain").EnumerateArray().Select(item => item.GetInt32()).SequenceEqual(chain) &&
            finalCommand == nativeCommand && finalProcess.GetProperty("Started").GetRawText() == actual.GetProperty("Started").GetRawText() &&
            finalProcess.GetProperty("StartTicks").GetRawText() == actual.GetProperty("StartTicks").GetRawText() &&
            finalProcess.GetProperty("BootId").GetRawText() == actual.GetProperty("BootId").GetRawText(), "final registered runtime identity/argv differs");
        string[] arguments;
        if (Text(actual, "Platform") == "linux")
        {
            Require(nativeCommand.EndsWith('\0'), "complete Linux native argv framing required");
            arguments = nativeCommand.Split('\0')[..^1];
            Require(arguments.All(argument => argument.Length > 0), "empty Linux reference argument is refused");
        }
        else arguments = WindowsArguments(nativeCommand, Check);
        var prefix = 1;
        Require(arguments.Length is >= 2 and <= 64 && Path.GetFullPath(arguments[0]) == runtime.Path, "native runtime argv executable differs");
        Require(arguments.Skip(1).SequenceEqual(execution.GetProperty("ArgumentList").EnumerateArray().Select(item => item.GetString())),
            "native runtime argv differs from its complete execution declaration");
        if (Path.GetFileNameWithoutExtension(runtime.Path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            Require(assembly is not null && arguments.Length > 2 && Path.GetFullPath(arguments[1]) == assembly.Value.Path, "managed runtime argv assembly differs"); prefix++;
        }
        Require(arguments[prefix] == "laya" && (arguments.Length - prefix) is 7 or 9, "exact bounded Laya runtime arguments required");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = prefix + 1; index < arguments.Length; index += 2)
            Require(arguments[index] is "--request" or "--output" or "--timeout-seconds" or "--cancel-file" &&
                options.TryAdd(arguments[index], arguments[index + 1]), "duplicate or unknown Laya runtime argument");
        Require(options.ContainsKey("--request") && options.ContainsKey("--output") && options.ContainsKey("--timeout-seconds"), "Laya runtime arguments incomplete");
        var export = VerifyPin(provenance.GetProperty("export_request"), 4 * 1024 * 1024);
        Require(Path.GetFullPath(options["--request"]) == export.Path, "captured export request differs from actual argv");
        using var requestDocument = Read(export.Path, 4 * 1024 * 1024); var request = requestDocument.RootElement;
        var requestSources = request.GetProperty("program_sources");
        Require(requestSources.GetArrayLength() == sourcePins.Count, "actual request source denominator differs");
        var requestedNames = new HashSet<string>(PathComparer);
        foreach (var source in requestSources.EnumerateArray())
        {
            Check(); var path = Path.GetFullPath(Text(source, "path"));
            Require(requestedNames.Add(path) && sourcePins.TryGetValue(path, out var pin) &&
                source.GetProperty("bytes").GetInt64() == pin.Bytes && Text(source, "sha256") == pin.Sha, "actual request source inventory differs");
        }
        foreach (var (name, hashField) in new[] { ("weights", "weights_sha256"), ("tokenizer", "tokenizer_sha256"),
            ("tokenizer_config", "tokenizer_config_sha256"), ("encoder_config", "encoder_config_sha256"), ("agent_config", "agent_config_sha256"),
            ("source_lock", "source_lock_sha256"), ("common_source", "upstream_common_sha256"), ("agent_source", "upstream_agent_sha256") })
        {
            Check(); var asset = request.GetProperty(name); var path = Text(asset, "path"); var bytes = asset.GetProperty("bytes").GetInt64();
            Require(Path.IsPathFullyQualified(path) && bytes is > 0 and <= 2147483648 && Text(asset, "sha256") == Text(provenance, hashField),
                "actual request fixed asset metadata differs: " + name);
            NoLinks(path, Check); Require(new FileInfo(path).Length == bytes, "actual request asset extent differs: " + name);
        }
        var casePin = VerifyPin(request.GetProperty("cases"), 4 * 1024 * 1024);
        var contractPin = VerifyPin(request.GetProperty("contract"), 4 * 1024 * 1024);
        Require(casePin.Sha == Text(provenance, "cases_sha256") && contractPin.Sha == Text(provenance, "contract_sha256"), "actual request cases/contract pins differ");
        using var casesDocument = Read(casePin.Path, 4 * 1024 * 1024);
        var manifestCount = casesDocument.RootElement.GetProperty("cases").GetArrayLength();
        var selected = reference.GetProperty("selected_case_count").GetInt32();
        Require(manifestCount is >= 1 and <= 46 && reference.GetProperty("total_manifest_cases").GetInt32() == manifestCount &&
            selected == Math.Min(request.GetProperty("maximum_cases").GetInt32(), manifestCount) && selected is >= 1 and <= 46 &&
            reference.GetProperty("cases").GetArrayLength() == selected && Text(reference, "measurement_status") == "completed", "complete selected reference row denominator differs");
        Require(int.TryParse(options["--timeout-seconds"], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 1 and <= 1800 &&
            request.GetProperty("timeout_seconds").GetInt32() == seconds &&
            SameSnakePin(request.GetProperty("runtime_executable"), runtime) &&
            (assembly is { } requestAssembly ? SameSnakePin(request.GetProperty("runtime_assembly"), requestAssembly) : request.GetProperty("runtime_assembly").ValueKind == JsonValueKind.Null) &&
            SameSnakePin(request.GetProperty("build_receipt"), build) &&
            Path.GetFullPath(Text(request, "process_evidence_directory")) == Path.GetDirectoryName(evidence.Path), "actual Laya request runtime/build/evidence scope differs");
        var requestedCancel = request.GetProperty("cancel_file");
        Require(requestedCancel.ValueKind == JsonValueKind.Null ? !options.ContainsKey("--cancel-file") :
            options.TryGetValue("--cancel-file", out var cancelPath) && cancelPath == requestedCancel.GetString(), "actual cancellation scope differs");
        using var producerDocument = Read(options["--output"], 4 * 1024 * 1024); var producer = producerDocument.RootElement;
        Require(Text(producer, "status") == "PASS_independent_laya_port_capture" && producer.GetProperty("error").ValueKind == JsonValueKind.Null &&
            Text(producer, "output_directory") == Text(request, "output_directory") &&
            Path.GetFullPath(Text(producer.GetProperty("capture"), "path")) == Path.Combine(Path.GetFullPath(Text(request, "output_directory")), "capture.json") &&
            producer.GetProperty("planned").GetInt32() == selected && producer.GetProperty("completed").GetInt32() == selected &&
            producer.GetProperty("answered").GetInt32() + producer.GetProperty("failed").GetInt32() == selected &&
            producer.GetProperty("capture").GetProperty("bytes").GetInt64() == referenceBytes.Length &&
            Text(producer.GetProperty("capture"), "sha256") == Convert.ToHexStringLower(SHA256.HashData(referenceBytes)), "actual producer receipt/capture binding differs");

        (string Path, long Bytes, string Sha) VerifyPin(JsonElement value, long maximum, bool pascal = false)
        {
            Check(); var path = Text(value, pascal ? "Path" : "path"); var length = value.GetProperty(pascal ? "Bytes" : "bytes").GetInt64(); var sha = Text(value, pascal ? "Sha256" : "sha256");
            Require(Path.IsPathFullyQualified(path) && path.Length <= 4096 && length is >= 1 && length <= maximum && sha.Length == 64 &&
                sha.All(character => char.IsAsciiHexDigit(character) && !char.IsUpper(character)), "absolute bounded complete file pin required");
            path = Path.GetFullPath(path); NoLinks(path, Check);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(input.Length == length && (hashed = checked(hashed + length)) <= 512L * 1024 * 1024, "pinned file/total byte bound differs");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[128 * 1024]; long read = 0;
            for (long block = 0, maximumBlocks = length / buffer.Length + 2; block < maximumBlocks; block++)
            {
                Check(); var count = input.Read(buffer); if (count == 0) break;
                read += count; Require(read <= length, "file grew while hashing"); hash.AppendData(buffer, 0, count);
            }
            Require(read == length && Convert.ToHexStringLower(hash.GetHashAndReset()) == sha, "actual complete file SHA differs");
            if (verifiedPins.TryGetValue(path, out var previous)) Require(previous == (length, sha), "same proof path has conflicting pins");
            else verifiedPins.Add(path, (length, sha));
            return (path, length, sha);
        }
        JsonDocument Read(string path, int maximum)
        {
            Check(); path = Path.GetFullPath(path); NoLinks(path, Check);
            using var input = File.OpenRead(path); Require(input.Length <= maximum, "proof JSON byte bound");
            var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
            Require(input.ReadByte() == -1, "proof JSON changed extent during read"); Check();
            if (verifiedPins.TryGetValue(path, out var pin))
                Require(pin.Bytes == bytes.Length && pin.Sha == Convert.ToHexStringLower(SHA256.HashData(bytes)), "pinned proof JSON changed between verification and read");
            return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48, AllowDuplicateProperties = false });
        }
    }

    private static bool SamePin(JsonElement value, (string Path, long Bytes, string Sha) pin) =>
        Path.GetFullPath(Text(value, "Path")) == pin.Path && value.GetProperty("Bytes").GetInt64() == pin.Bytes && Text(value, "Sha256") == pin.Sha;
    private static bool SameSnakePin(JsonElement value, (string Path, long Bytes, string Sha) pin) =>
        Path.GetFullPath(Text(value, "path")) == pin.Path && value.GetProperty("bytes").GetInt64() == pin.Bytes && Text(value, "sha256") == pin.Sha;
    private static string[] WindowsArguments(string command, Action check)
    {
        check(); var pointer = CommandLineToArgvW(command, out var count);
        if (pointer == 0) throw new InvalidDataException("Native Windows argument parsing failed.");
        try
        {
            if (count is < 1 or > 64) throw new InvalidDataException("Native Windows argument bound64.");
            var arguments = new string[count];
            for (var index = 0; index < count; index++)
            {
                check(); arguments[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, checked(index * IntPtr.Size)))
                    ?? throw new InvalidDataException("Native Windows argument is absent.");
                if (arguments[index].Length > 32768) throw new InvalidDataException("Native argument length bound32768.");
            }
            return arguments;
        }
        finally { if (LocalFree(pointer) != 0) throw new IOException("Native Windows argument buffer could not be released."); }
    }
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CommandLineToArgvW(string command, out int count);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint LocalFree(nint memory);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() is { Length: > 0 and <= 65536 } text
        ? text : throw new InvalidDataException("Port proof missing bounded string " + name);
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static void NoLinks(string path, Action check)
    {
        string? current = path;
        for (var depth = 0; depth < 64 && current is not null; depth++)
        {
            check(); var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Port proof path contains a link.");
            current = Path.GetDirectoryName(current);
        }
        if (current is not null) throw new InvalidDataException("Port proof path depth exceeds64.");
    }
}
