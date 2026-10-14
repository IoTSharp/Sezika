using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml;
using System.Xml.Linq;

namespace Sezika.PortProvenance;

internal sealed record ProgramFilePin(string Path, long Bytes, string Sha256);
internal sealed record ProgramGeneratedSource(string DocumentPath, ProgramFilePin Source,
    Guid? PdbChecksumAlgorithm = null, string? PdbChecksum = null, string? PdbChecksumSha256 = null);
internal sealed record PortBuildSummary(string Status, ProgramFilePin[] ProgramSources,
    ProgramFilePin RuntimeExecutable, ProgramFilePin? RuntimeAssembly, ProgramFilePin BuildExecution,
    ProgramFilePin BuildRequest, ProgramFilePin CompilerExecutable, ProgramFilePin[] ProgramBuildInputs,
    string RuntimeDirectory, ProgramFilePin[] RuntimeFiles, ProgramFilePin CompiledAssembly,
    ProgramFilePin RuntimeSymbols, string Configuration, string TargetFramework, string BuildKind,
    ProgramGeneratedSource[] GeneratedSources);
internal sealed record PortCompiledProofResult(string Status, Guid AssemblyMvid, Guid PdbGuid,
    uint PdbStamp, int SourceDocuments, int RuntimeFiles);
internal sealed record ProgramSourceDocument(string Path, string? Sha256, Guid ChecksumAlgorithm, string Checksum);
internal sealed record ProgramBuildRequest(string Executable, string[] ArgumentList, string WorkingDirectory,
    string OutputDirectory, int TimeoutSeconds, int CleanupSeconds, int MaximumProcesses,
    long MaximumOutputBytes, Dictionary<string, string> Environment);
internal sealed record ProgramBuildProcess(int Pid, int ParentPid, int[] ParentChain, DateTimeOffset? Started,
    ulong? StartTicks, string? BootId, string CommandLine, string State, string Ownership, string? ScopeId,
    string? AfterExecCommandLine = null, string? ExpectedExecCommandLine = null, string? ExitProof = null);
internal sealed record ProgramBuildExecution(int SchemaVersion, string Status, string ExecutionStatus, string CleanupStatus,
    int? ExitCode, int RootPid, string Platform, string Executable, string[] ArgumentList,
    string WorkingDirectory, string OutputDirectory, string OwnershipId, bool InputComplete,
    bool OutputComplete, bool OutputLimitExceeded, int RemainingProcesses, ProgramBuildProcess[] Processes,
    JsonElement[] Errors);

[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(PortBuildSummary))]
[JsonSerializable(typeof(ProgramBuildRequest))]
[JsonSerializable(typeof(ProgramBuildExecution))]
[JsonSerializable(typeof(ProgramBuildProcess[]))]
internal partial class CompiledProgramJson : JsonSerializerContext;

// This managed-JIT gate uses PE/Portable-PDB metadata without loading or executing an assembly.
// It proves compiled source and local artifacts, never upstream execution, model parity, or Native AOT.
internal static partial class CompiledProgramProof
{
    private static readonly Guid Sha256Document = new("8829d00f-11b8-4213-878b-770e8597ac16");
    private static readonly Guid Sha1Document = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid CSharpDocument = new("3f5162f8-07c6-11d3-9053-00c04fa302a1");

    internal static PortCompiledProofResult Verify(PortBuildSummary summary, CancellationToken cancellation,
        Action? check = null)
    {
        var budget = new ProofBudget(cancellation, check);
        if (summary.Status != "PASS_exact_reference_program_source_build_only" ||
            summary.BuildKind != "managed_jit_source_build_only" || summary.Configuration != "Release" ||
            summary.TargetFramework != "net10.0" || summary.RuntimeAssembly is null)
            throw new InvalidDataException("Only explicit Release/net10.0 managed JIT SOURCE_BUILD_ONLY proof is admitted; Native AOT is unverified.");
        Inventory(summary.ProgramSources, 64, "program sources", budget);
        Inventory(summary.ProgramBuildInputs, 128, "program build inputs", budget);
        Inventory(summary.RuntimeFiles, 256, "runtime files", budget);
        if (summary.GeneratedSources.Length > 512) throw new InvalidDataException("Generated source count bound512.");
        if (summary.GeneratedSources.Length != 0) Inventory(summary.GeneratedSources.Select(item => item.Source).ToArray(), 512, "generated sources", budget);
        var projects = summary.ProgramSources.Where(pin => Extension(pin.Path, ".csproj")).ToArray();
        if (projects.Length != 1 || Path.GetFileName(projects[0].Path) != "Sezika.ReferenceTool.csproj" ||
            summary.ProgramSources.Any(pin => !Extension(pin.Path, ".cs") && !Extension(pin.Path, ".csproj")) ||
            !summary.ProgramBuildInputs.Contains(projects[0]))
            throw new InvalidDataException("Exactly one pinned ReferenceTool project, its build-input pin, and explicit compiled C# source pins required.");
        var sourceFiles = summary.ProgramSources.Where(pin => Extension(pin.Path, ".cs")).ToArray();
        if (sourceFiles.Length is < 1 or > 63) throw new InvalidDataException("Handwritten compiled source count bound63.");
        var allSourcePins = summary.ProgramSources.Concat(summary.ProgramBuildInputs).Concat(summary.GeneratedSources.Select(item => item.Source));
        foreach (var pin in allSourcePins) budget.Verify(pin, 4194304);
        budget.Verify(summary.CompilerExecutable, 536870912);
        var buildRequest = budget.Read(summary.BuildRequest, CompiledProgramJson.Default.ProgramBuildRequest, 4194304);
        var execution = budget.Read(summary.BuildExecution, CompiledProgramJson.Default.ProgramBuildExecution, 20971520);
        ValidateBuild(summary, projects[0], buildRequest, execution, budget);
        VerifyRuntime(summary, budget);
        using var imageStream = budget.Open(summary.CompiledAssembly, 536870912);
        using var image = new PEReader(imageStream, PEStreamOptions.LeaveOpen);
        if (!image.HasMetadata || image.PEHeaders.CorHeader is null)
            throw new InvalidDataException("Pinned compiled output must contain managed PE metadata; Native AOT is unverified.");
        var imageMetadata = image.GetMetadataReader();
        var mvid = imageMetadata.GetGuid(imageMetadata.GetModuleDefinition().Mvid);
        var entries = image.ReadDebugDirectory();
        if (entries.Length is < 1 or > 32) throw new InvalidDataException("PE debug-directory count bound32.");
        var codeViews = entries.Where(item => item.Type == DebugDirectoryEntryType.CodeView).ToArray();
        if (codeViews.Length != 1 || !codeViews[0].IsPortableCodeView)
            throw new InvalidDataException("Exactly one Portable-PDB CodeView identity required.");
        var codeView = image.ReadCodeViewDebugDirectoryData(codeViews[0]);
        if (codeView.Age != 1 || Path.GetFileName(codeView.Path.Replace('\\', Path.DirectorySeparatorChar)) != Path.GetFileName(summary.RuntimeSymbols.Path))
            throw new InvalidDataException("PE CodeView age/symbol filename differs.");
        using var pdbStream = budget.Open(summary.RuntimeSymbols, 536870912);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream, MetadataStreamOptions.LeaveOpen);
        var pdb = provider.GetMetadataReader();
        var header = pdb.DebugMetadataHeader ?? throw new InvalidDataException("Portable-PDB header missing.");
        if (header.Id.Length != 20) throw new InvalidDataException("Portable-PDB content identity must contain20 bytes.");
        var identity = new BlobContentId(header.Id);
        if (identity.Guid != codeView.Guid || identity.Stamp != codeViews[0].Stamp)
            throw new InvalidDataException("Pinned PE CodeView GUID/stamp does not bind this Portable PDB.");
        var expected = sourceFiles.ToDictionary(pin => Canonical(pin.Path), PathComparer);
        foreach (var generated in summary.GeneratedSources)
        {
            budget.Check(); ProofBudget.Safe(generated.DocumentPath);
            if (!Extension(generated.DocumentPath, ".cs") || !Extension(generated.Source.Path, ".cs") ||
                !expected.TryAdd(Canonical(generated.DocumentPath), generated.Source))
                throw new InvalidDataException("Generated-source document alias must be unique C# with independently pinned exported bytes.");
        }
        var observed = new HashSet<string>(PathComparer);
        var generatedByDocument = summary.GeneratedSources.ToDictionary(item => Canonical(item.DocumentPath), PathComparer);
        foreach (var document in ReadSourceDocuments(pdb, budget.Check))
        {
            budget.Check(); var path = Canonical(document.Path);
            if (!observed.Add(path) || !expected.TryGetValue(path, out var pin))
                throw new InvalidDataException("PDB compiled C# source is undeclared or duplicate: " + document.Path);
            var isGenerated = generatedByDocument.TryGetValue(path, out var generated);
            if (isGenerated)
            {
                var declared = generated!;
                var hasMetadata = declared.PdbChecksumAlgorithm is not null || declared.PdbChecksum is not null || declared.PdbChecksumSha256 is not null;
                if (document.ChecksumAlgorithm == Sha1Document && !hasMetadata || hasMetadata &&
                    (declared.PdbChecksumAlgorithm != document.ChecksumAlgorithm || declared.PdbChecksum != document.Checksum ||
                     declared.PdbChecksumSha256 != document.Sha256))
                    throw new InvalidDataException("Declared generated PDB checksum metadata differs: " + document.Path);
            }
            if (!DocumentChecksumMatches(document, pin, isGenerated, cancellation, budget.Check))
                throw new InvalidDataException("Actual PDB source checksum differs from its source pin: " + document.Path);
        }
        if (!observed.SetEquals(expected.Keys))
            throw new InvalidDataException("Declared C# sources and actual Portable-PDB documents must match in both directions.");
        // Rehash after metadata reads so a changed image/symbol/source set cannot retain this result.
        foreach (var pin in allSourcePins) budget.Verify(pin, 4194304);
        budget.Verify(summary.CompilerExecutable, 536870912); budget.Verify(summary.BuildRequest, 4194304);
        budget.Verify(summary.BuildExecution, 20971520); VerifyRuntime(summary, budget);
        return new("SOURCE_BUILD_ONLY", mvid, identity.Guid, identity.Stamp, observed.Count, summary.RuntimeFiles.Length);
    }

    internal static ProgramSourceDocument[] ReadSourceDocuments(MetadataReader pdb, Action check)
    {
        if (pdb.Documents.Count is < 1 or > 512) throw new InvalidDataException("Portable-PDB document count bound512.");
        var documents = new List<ProgramSourceDocument>(pdb.Documents.Count); var names = new HashSet<string>(PathComparer);
        foreach (var handle in pdb.Documents)
        {
            check(); var document = pdb.GetDocument(handle); var name = pdb.GetString(document.Name); ProofBudget.Safe(name);
            if (!Extension(name, ".cs") || document.Language.IsNil || pdb.GetGuid(document.Language) != CSharpDocument ||
                document.HashAlgorithm.IsNil || !names.Add(Canonical(name)))
                throw new InvalidDataException("Every unique PDB document must be an absolute C# source with a declared checksum.");
            var algorithm = pdb.GetGuid(document.HashAlgorithm);
            var checksum = pdb.GetBlobBytes(document.Hash);
            if (algorithm != Sha256Document && algorithm != Sha1Document ||
                checksum.Length != (algorithm == Sha256Document ? 32 : 20))
                throw new InvalidDataException("PDB C# source checksum algorithm/length is not admitted.");
            var value = Convert.ToHexStringLower(checksum);
            documents.Add(new(name, algorithm == Sha256Document ? value : null, algorithm, value));
        }
        return documents.ToArray();
    }

    internal static bool DocumentChecksumMatches(ProgramSourceDocument document, ProgramFilePin source,
        bool generated, CancellationToken cancellation, Action check)
    {
        if (document.ChecksumAlgorithm == Sha256Document)
            return document.Sha256 is not null && document.Sha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase);
        if (!generated || document.ChecksumAlgorithm != Sha1Document)
            throw new InvalidDataException("Handwritten PDB sources require SHA256; SHA1 is generated-only.");
        // Open independently verifies the complete SHA256 file pin before reading
        // the declared generated-document checksum from these same real bytes.
        using var stream = new ProofBudget(cancellation, check).Open(source, 4194304);
        var actual = Convert.ToHexStringLower(SHA1.HashData(stream)); check();
        return actual == document.Checksum;
    }

    private static void ValidateBuild(PortBuildSummary summary, ProgramFilePin project, ProgramBuildRequest request,
        ProgramBuildExecution execution, ProofBudget budget)
    {
        if (execution.SchemaVersion != 1 || execution.Status != "PASS" || execution.ExecutionStatus != "SUCCEEDED" || execution.CleanupStatus != "PASS" ||
            execution.ExitCode != 0 || execution.RemainingProcesses != 0 || execution.Errors.Length != 0 ||
            !execution.InputComplete || !execution.OutputComplete || execution.OutputLimitExceeded || execution.RootPid <= 0 ||
            !Guid.TryParseExact(execution.OwnershipId, "N", out _) || !SamePath(request.Executable, summary.CompilerExecutable.Path) ||
            !SamePath(execution.Executable, request.Executable) || !execution.ArgumentList.SequenceEqual(request.ArgumentList) ||
            !SamePath(execution.WorkingDirectory, request.WorkingDirectory) || !SamePath(execution.OutputDirectory, request.OutputDirectory))
            throw new InvalidDataException("Exact successful build execution/request/compiler/root/cleanup binding absent.");
        if (request.TimeoutSeconds is < 1 or > 1800 || request.CleanupSeconds is < 1 or > 60 ||
            request.MaximumProcesses is < 1 or > 256 || request.MaximumOutputBytes is < 1 or > 33554432 ||
            request.ArgumentList.Length is < 4 or > 64 || request.Environment.Count > 64)
            throw new InvalidDataException("Build execution resource/argument inventory bounds differ.");
        var hostName = Path.GetFileName(request.Executable);
        if (hostName is not ("dotnet" or "dotnet.exe")) throw new InvalidDataException("Real dotnet build/publish host required; generic success probes are refused.");
        ProofBudget.Safe(request.WorkingDirectory); ProofBudget.Safe(request.OutputDirectory);
        if (!Directory.Exists(request.WorkingDirectory) || summary.ProgramSources.Concat(summary.ProgramBuildInputs).Any(pin => !Within(pin.Path, request.WorkingDirectory)))
            throw new InvalidDataException("All declared source/build inputs must belong to the exact build working-directory snapshot.");
        VerifyBuildInputs(summary, project, request.WorkingDirectory, budget);
        var args = request.ArgumentList;
        if (args[0] is not ("build" or "publish") || Extension(args[1], ".csproj") == false ||
            !SamePath(Path.GetFullPath(args[1], request.WorkingDirectory), project.Path))
            throw new InvalidDataException("Build command must build/publish the uniquely pinned ReferenceTool project.");
        var configuration = false; var framework = false; var singleNode = false; var noServers = false; var noShared = false;
        var emitGenerated = false; string? generatedDirectory = null;
        for (var index = 2; index < args.Length; index++)
        {
            budget.Check(); var arg = args[index];
            if (arg is "-c" or "--configuration")
            {
                if (configuration || ++index >= args.Length || args[index] != "Release") throw new InvalidDataException("Exactly Release configuration required.");
                configuration = true;
            }
            else if (arg is "-f" or "--framework")
            {
                if (framework || ++index >= args.Length || args[index] != "net10.0") throw new InvalidDataException("Exactly net10.0 framework required.");
                framework = true;
            }
            else if (arg == "--disable-build-servers" && !noServers) noServers = true;
            else if (arg == "-m:1" && !singleNode) singleNode = true;
            else if (arg == "-p:UseSharedCompilation=false" && !noShared) noShared = true;
            else if (arg is "--no-restore" or "--nologo" or "-p:DebugType=portable" or "-p:DebugSymbols=true") { }
            else if (arg == "-p:EmitCompilerGeneratedFiles=true" && !emitGenerated) emitGenerated = true;
            else if (arg.StartsWith("-p:CompilerGeneratedFilesOutputPath=", StringComparison.Ordinal))
            {
                var value = arg["-p:CompilerGeneratedFilesOutputPath=".Length..];
                if (generatedDirectory is not null || !Path.IsPathFullyQualified(value) || !Within(value, request.WorkingDirectory)) throw new InvalidDataException("Unique generated-source output must remain in the build snapshot.");
                generatedDirectory = value;
            }
            else throw new InvalidDataException("Unadmitted compiler argument/property; compilation cannot be redirected: " + arg);
        }
        if (!configuration || !noServers || !singleNode || !noShared)
            throw new InvalidDataException("Release, disabled build servers, single node, and disabled shared compilation required.");
        var expectedDirectory = Path.Combine(Path.GetDirectoryName(project.Path)!, "bin", "Release", "net10.0");
        if (args[0] == "publish") expectedDirectory = Path.Combine(expectedDirectory, "publish");
        if (!SamePath(expectedDirectory, summary.RuntimeDirectory)) throw new InvalidDataException("Output directory differs from the fixed Release/net10.0 build/publish location.");
        foreach (var generated in summary.GeneratedSources)
        {
            budget.Check(); var sameDocument = SamePath(generated.DocumentPath, generated.Source.Path);
            var sdkGenerated = sameDocument && Within(generated.Source.Path, Path.Combine(Path.GetDirectoryName(project.Path)!, "obj"));
            var explicitlyExported = emitGenerated && generatedDirectory is not null && Within(generated.Source.Path, generatedDirectory);
            if (!Within(generated.DocumentPath, request.WorkingDirectory) || !Within(generated.Source.Path, request.WorkingDirectory) ||
                !sdkGenerated && !explicitlyExported)
                throw new InvalidDataException("Generated source/PDB alias is not bound to exact SDK obj or emitted compiler-output arguments.");
        }
        if (execution.Processes.Length is < 1 or > 256 || execution.Processes.Select(item => item.Pid).Distinct().Count() != execution.Processes.Length)
            throw new InvalidDataException("Build process evidence count/duplicate bound256.");
        var root = execution.Processes.SingleOrDefault(item => item.Pid == execution.RootPid) ?? throw new InvalidDataException("Build root identity absent.");
        if (root.ParentPid <= 0 || root.ParentChain.Length is < 1 or > 64 || root.ParentChain[^1] != root.ParentPid ||
            root.ParentChain.Any(pid => pid <= 0 || pid == root.Pid) || root.ScopeId != execution.OwnershipId ||
            string.IsNullOrWhiteSpace(root.CommandLine) || root.State != "EXITED" || string.IsNullOrWhiteSpace(root.Ownership) ||
            execution.Platform == "windows" && root.Started is null ||
            execution.Platform == "linux" && (root.StartTicks is null || string.IsNullOrWhiteSpace(root.BootId)) ||
            execution.Platform is not ("windows" or "linux"))
            throw new InvalidDataException("Successful build root lacks exact PID/birth/parent/scope/exit evidence.");
        var command = root.CommandLine;
        if (execution.Platform == "linux")
        {
            if (string.IsNullOrEmpty(root.AfterExecCommandLine) ||
                root.AfterExecCommandLine != root.ExpectedExecCommandLine)
                throw new InvalidDataException("Linux compiled build requires observed post-exec argv equal to its registered expectation.");
            command = root.AfterExecCommandLine;
        }
        var actualArguments = RegisteredArguments(command, execution.Platform, budget);
        if (actualArguments.Length != request.ArgumentList.Length + 1 || !SamePath(actualArguments[0], request.Executable) ||
            !actualArguments.Skip(1).SequenceEqual(request.ArgumentList))
            throw new InvalidDataException("Actual registered build root argv differs from exact compiler/build request.");
    }

    // The genuine-proof checker supplies only in-memory negative copies of its pinned
    // native receipt to this same production gate; no successful receipt is fabricated.
    internal static void VerifyNativeBuildExecution(PortBuildSummary summary, ProgramBuildRequest request,
        ProgramBuildExecution execution, CancellationToken cancellation, Action check)
    {
        var project = summary.ProgramSources.Single(pin => Extension(pin.Path, ".csproj"));
        ValidateBuild(summary, project, request, execution, new ProofBudget(cancellation, check));
    }

    private static string[] RegisteredArguments(string command, string platform, ProofBudget budget)
    {
        if (command.Length is < 1 or > 65536) throw new InvalidDataException("Registered compiler argv character bound65536.");
        if (platform == "linux")
        {
            if (command[^1] != '\0') throw new InvalidDataException("Linux compiler argv must preserve native NUL framing.");
            var arguments = command[..^1].Split('\0');
            if (arguments.Length is < 1 or > 65) throw new InvalidDataException("Registered compiler argument count bound65.");
            return arguments;
        }
        if (platform != "windows" || !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows native build argv proof requires Windows.");
        var pointer = CommandLineToArgvW(command, out var count);
        if (pointer == IntPtr.Zero) throw new IOException("Native compiler argv parser failed; Win32=" + Marshal.GetLastPInvokeError());
        try
        {
            if (count is < 1 or > 65) throw new InvalidDataException("Registered compiler argument count bound65.");
            var arguments = new string[count];
            for (var index = 0; index < count; index++)
            {
                budget.Check(); var value = Marshal.ReadIntPtr(pointer, checked(index * IntPtr.Size));
                if (value == IntPtr.Zero) throw new InvalidDataException("Native compiler argument pointer missing.");
                var found = false;
                for (var length = 0; length <= 65536; length++)
                {
                    if ((length & 255) == 0) budget.Check();
                    if (Marshal.ReadInt16(value, checked(length * 2)) != 0) continue;
                    arguments[index] = Marshal.PtrToStringUni(value, length) ?? throw new IOException("Native compiler argument copy failed."); found = true; break;
                }
                if (!found) throw new InvalidDataException("Native compiler argument character bound65536.");
            }
            return arguments;
        }
        finally
        {
            if (LocalFree(pointer) != IntPtr.Zero) throw new IOException("Native compiler argv buffer release failed; Win32=" + Marshal.GetLastPInvokeError());
        }
    }
    [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CommandLineToArgvW(string commandLine, out int count);
    [LibraryImport("kernel32.dll", EntryPoint = "LocalFree", SetLastError = true)]
    private static partial IntPtr LocalFree(IntPtr memory);

    private static void VerifyBuildInputs(PortBuildSummary summary, ProgramFilePin project, string snapshot, ProofBudget budget)
    {
        // Fixed standalone-oracle profile. SDK installations and user-level NuGet configuration are
        // outside this source-build-only denominator; this does not claim toolchain attestation.
        var conventional = new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.Config", "NuGet.config" };
        var declared = summary.ProgramBuildInputs.ToDictionary(pin => Canonical(pin.Path), PathComparer);
        var observed = new HashSet<string>(PathComparer) { Canonical(project.Path) };
        var cursor = Path.GetDirectoryName(project.Path)!; var reachedSnapshot = false;
        for (var depth = 0; depth < 32; depth++)
        {
            budget.Check();
            foreach (var name in conventional)
            {
                var path = Path.Combine(cursor, name); ProofBudget.Safe(path);
                if (!File.Exists(path)) continue;
                var canonical = Canonical(path); observed.Add(canonical);
                if (!declared.ContainsKey(canonical)) throw new InvalidDataException("Actual snapshot build input is omitted: " + path);
            }
            if (SamePath(cursor, snapshot)) { reachedSnapshot = true; break; }
            var parent = Path.GetDirectoryName(cursor);
            if (parent is null || !SamePath(parent, snapshot) && !Within(parent, snapshot)) throw new InvalidDataException("Project build-input ancestry escapes the declared snapshot.");
            cursor = parent;
        }
        if (!reachedSnapshot || !observed.SetEquals(declared.Keys))
            throw new InvalidDataException("Declared and actual conventional snapshot build inputs must match in both directions; depth bound32.");
        var properties = new HashSet<string>(StringComparer.Ordinal)
        {
            "OutputType", "TargetFramework", "InvariantGlobalization", "JsonSerializerIsReflectionEnabledByDefault",
            "LangVersion", "Nullable", "ImplicitUsings", "TreatWarningsAsErrors", "Deterministic", "VersionPrefix",
            "VersionSuffix", "Authors", "PackageLicenseExpression", "ManagePackageVersionsCentrally", "CentralPackageTransitivePinningEnabled",
            "AllowUnsafeBlocks"
        };
        foreach (var pin in summary.ProgramBuildInputs)
        {
            budget.Check(); var name = Path.GetFileName(pin.Path);
            if (name == "global.json" || name.Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = budget.Open(pin, 4194304);
            using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = 4194304, MaxCharactersFromEntities = 0 });
            XDocument xml;
            try { xml = XDocument.Load(xmlReader); }
            catch (XmlException error) { throw new InvalidDataException("Invalid or prohibited standalone build XML: " + pin.Path, error); }
            var root = xml.Root;
            if (root is null || root.Name != "Project" || root.Attributes().Any(attribute => attribute.Name != "Sdk") ||
                root.Attribute("Sdk") is { Value: not "Microsoft.NET.Sdk" } ||
                SamePath(pin.Path, project.Path) && root.Attribute("Sdk") is null)
                throw new InvalidDataException("Fixed standalone build profile requires Microsoft.NET.Sdk project/default items and plain props.");
            var nodes = 0;
            foreach (var group in root.Elements())
            {
                budget.Check(); if (++nodes > 4096 || group.HasAttributes || group.Name != "PropertyGroup" && group.Name != "ItemGroup")
                    throw new InvalidDataException("Custom Import/Target/UsingTask/conditions are not admitted by the standalone reference build profile.");
                foreach (var item in group.Elements())
                {
                    budget.Check(); if (++nodes > 4096 || item.Elements().Any()) throw new InvalidDataException("Build XML node/depth profile bound4096.");
                    if (group.Name == "PropertyGroup")
                    {
                        if (item.HasAttributes || !properties.Contains(item.Name.LocalName) || item.Name.NamespaceName.Length != 0 ||
                            item.Value.Contains("$(", StringComparison.Ordinal) || item.Value.Contains("@(", StringComparison.Ordinal) ||
                            item.Value.Contains("%(", StringComparison.Ordinal))
                            throw new InvalidDataException("Unadmitted build property/executable expansion: " + item.Name);
                        if (item.Name == "TargetFramework" && item.Value != "net10.0" || item.Name == "OutputType" && item.Value != "Exe" ||
                            item.Name == "JsonSerializerIsReflectionEnabledByDefault" && item.Value != "false" ||
                            item.Name == "Deterministic" && item.Value != "true" || item.Name == "AllowUnsafeBlocks" && item.Value != "true")
                            throw new InvalidDataException("Fixed managed reference build property differs: " + item.Name);
                    }
                    else if (name != "Directory.Packages.props" || item.Name != "PackageVersion" ||
                        item.Attributes().Count() != 2 || item.Attribute("Include") is null || item.Attribute("Version") is null ||
                        item.Attributes().Any(attribute => attribute.Name != "Include" && attribute.Name != "Version") ||
                        item.Attributes().Any(attribute => attribute.Value.Length is < 1 or > 256 || attribute.Value.Contains("$(", StringComparison.Ordinal)))
                        throw new InvalidDataException("Only literal central PackageVersion metadata is admitted; CompileInclude/targets/imports are refused.");
                }
            }
        }
    }

    private static void VerifyRuntime(PortBuildSummary summary, ProofBudget budget)
    {
        ProofBudget.Safe(summary.RuntimeDirectory);
        if (!Directory.Exists(summary.RuntimeDirectory) || !SamePin(summary.RuntimeAssembly!, summary.CompiledAssembly) ||
            !Within(summary.CompiledAssembly.Path, summary.RuntimeDirectory) || !Within(summary.RuntimeSymbols.Path, summary.RuntimeDirectory) ||
            Path.GetFileName(summary.CompiledAssembly.Path) != "Sezika.ReferenceTool.dll" ||
            Path.GetFileName(summary.RuntimeSymbols.Path) != "Sezika.ReferenceTool.pdb")
            throw new InvalidDataException("Actual compiled assembly/symbol/output binding differs.");
        var inventory = summary.RuntimeFiles.ToDictionary(pin => Canonical(pin.Path), PathComparer);
        foreach (var pin in summary.RuntimeFiles)
        {
            budget.Check(); if (!Within(pin.Path, summary.RuntimeDirectory)) throw new InvalidDataException("Runtime file outside application directory.");
            budget.Verify(pin, 536870912);
        }
        foreach (var pin in new[] { summary.CompiledAssembly, summary.RuntimeSymbols })
            if (!inventory.TryGetValue(Canonical(pin.Path), out var member) || !SamePin(member, pin)) throw new InvalidDataException("Compiled assembly/symbol pin missing from runtime inventory.");
        var required = new[] { "Sezika.ReferenceTool.deps.json", "Sezika.ReferenceTool.runtimeconfig.json" };
        foreach (var name in required)
            if (!inventory.ContainsKey(Canonical(Path.Combine(summary.RuntimeDirectory, name)))) throw new InvalidDataException("Mandatory framework-dependent runtime file missing: " + name);
        if (Within(summary.RuntimeExecutable.Path, summary.RuntimeDirectory))
        {
            if (Path.GetFileName(summary.RuntimeExecutable.Path) is not ("Sezika.ReferenceTool.exe" or "Sezika.ReferenceTool") ||
                !inventory.TryGetValue(Canonical(summary.RuntimeExecutable.Path), out var apphost) || !SamePin(apphost, summary.RuntimeExecutable))
                throw new InvalidDataException("Application host must be a pinned complete-runtime member.");
        }
        else if (Path.GetFileName(summary.RuntimeExecutable.Path) is not ("dotnet" or "dotnet.exe"))
            throw new InvalidDataException("External runtime host must be independently pinned dotnet, not an application member.");
        budget.Verify(summary.RuntimeExecutable, 536870912);
        VerifyRuntimeDirectoryFiles(summary.RuntimeDirectory, summary.RuntimeFiles, budget.Check);
    }
    // The caller supplies its execution budget: exporter after-checks may run after a larger numerical allocation.
    // Item/depth/aggregate-byte limits remain identical to the managed-JIT provenance gate.
    internal static void VerifyRuntimeDirectoryFiles(string directory, ProgramFilePin[] files, Action check)
    {
        if (files.Length is < 4 or > 256) throw new InvalidDataException("Complete FDD runtime inventory count bound256.");
        var inventory = files.ToDictionary(pin => Canonical(pin.Path), PathComparer);
        var observed = new HashSet<string>(PathComparer); var pending = new Queue<(string Directory, int Depth)>();
        pending.Enqueue((directory, 0)); var entries = 0; long bytes = 0;
        for (var directoryIndex = 0; directoryIndex < 256 && pending.Count != 0; directoryIndex++)
        {
            check(); var current = pending.Dequeue();
            foreach (var path in Directory.EnumerateFileSystemEntries(current.Directory))
            {
                check(); if (++entries > 512) throw new InvalidDataException("Runtime directory entry bound512."); ProofBudget.Safe(path);
                if (Directory.Exists(path))
                {
                    if (current.Depth >= 8) throw new InvalidDataException("Runtime directory depth bound8.");
                    pending.Enqueue((path, current.Depth + 1));
                }
                else
                {
                    var canonical = Canonical(path); if (!observed.Add(canonical) || !inventory.TryGetValue(canonical, out var pin))
                        throw new InvalidDataException("Unpinned or duplicate actual runtime file: " + path);
                    bytes = checked(bytes + pin.Bytes); if (bytes > 536870912) throw new InvalidDataException("Complete runtime byte bound512MiB.");
                }
            }
        }
        if (pending.Count != 0 || !observed.SetEquals(inventory.Keys)) throw new InvalidDataException("Complete actual runtime directory inventory differs.");
    }

    private static void Inventory(ProgramFilePin[] pins, int maximum, string name, ProofBudget budget)
    {
        if (pins.Length is < 1 || pins.Length > maximum) throw new InvalidDataException(name + " inventory count bound.");
        var paths = new HashSet<string>(PathComparer);
        foreach (var pin in pins)
        {
            budget.Check(); ProofBudget.Safe(pin.Path);
            if (pin.Bytes < 0 || pin.Sha256.Length != 64 || pin.Sha256.Any(character => !char.IsAsciiHexDigit(character)) || !paths.Add(Canonical(pin.Path)))
                throw new InvalidDataException(name + " malformed pin or duplicate path.");
        }
    }
    private static bool Extension(string path, string extension) => Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase);
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string Canonical(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    private static bool SamePath(string left, string right) => PathComparer.Equals(Canonical(left), Canonical(right));
    private static bool SamePin(ProgramFilePin left, ProgramFilePin right) => SamePath(left.Path, right.Path) && left.Bytes == right.Bytes && left.Sha256.Equals(right.Sha256, StringComparison.OrdinalIgnoreCase);
    private static bool Within(string path, string directory) => Canonical(path).StartsWith(Canonical(directory) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class ProofBudget(CancellationToken cancellation, Action? externalCheck)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _nextMemory;
        internal void Check()
        {
            cancellation.ThrowIfCancellationRequested(); externalCheck?.Invoke();
            if (_clock.Elapsed.TotalSeconds >= 30) throw new TimeoutException("Compiled provenance wall bound30s.");
            if (_clock.Elapsed.TotalSeconds >= _nextMemory)
            {
                using var process = Process.GetCurrentProcess(); _nextMemory = _clock.Elapsed.TotalSeconds + .5;
                if (process.WorkingSet64 > 536870912) throw new InvalidDataException("Compiled provenance resident bound512MiB.");
            }
        }
        internal static void Safe(string path)
        {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)) throw new InvalidDataException("Absolute local provenance path required.");
            var cursor = Canonical(path);
            for (var depth = 0; depth < 64; depth++)
            {
                if (Path.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Provenance paths cannot traverse links.");
                var parent = Path.GetDirectoryName(cursor); if (string.IsNullOrEmpty(parent) || parent == cursor) return; cursor = parent;
            }
            throw new InvalidDataException("Provenance ancestor bound64.");
        }
        internal FileStream Open(ProgramFilePin pin, long maximum)
        {
            Safe(pin.Path); Check(); var stream = new FileStream(pin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (stream.Length != pin.Bytes || stream.Length > maximum || pin.Bytes < 0) throw new InvalidDataException("Provenance byte bound/pin differs.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1048576]; long count = 0;
                for (var block = 0; block < 513 && count < stream.Length; block++)
                {
                    Check(); var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, stream.Length - count));
                    if (read == 0) throw new EndOfStreamException(); hash.AppendData(buffer, 0, read); count += read;
                }
                if (count != stream.Length || !Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(pin.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Provenance source/artifact SHA differs: " + pin.Path);
                stream.Position = 0; return stream;
            }
            catch { stream.Dispose(); throw; }
        }
        internal void Verify(ProgramFilePin pin, long maximum) { using var stream = Open(pin, maximum); }
        internal T Read<T>(ProgramFilePin pin, JsonTypeInfo<T> type, int maximum)
        {
            using var stream = Open(pin, maximum); var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); Check();
            if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(pin.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Provenance JSON changed during read.");
            ValidateJson(bytes); return JsonSerializer.Deserialize(bytes, type) ?? throw new InvalidDataException("Missing typed provenance JSON.");
        }
        private void ValidateJson(byte[] bytes)
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 }); var scopes = new Stack<HashSet<string>?>();
            for (var tokens = 0; reader.Read(); tokens++)
            {
                if (tokens >= 200000) throw new InvalidDataException("Provenance JSON token bound200000."); if ((tokens & 255) == 0) Check();
                if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new(StringComparer.Ordinal));
                else if (reader.TokenType == JsonTokenType.StartArray) scopes.Push(null);
                else if (reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject) scopes.Pop();
                else if (reader.TokenType == JsonTokenType.PropertyName && !(scopes.Peek()?.Add(reader.GetString()!) ?? false)) throw new InvalidDataException("Duplicate provenance JSON property.");
            }
            if (scopes.Count != 0) throw new InvalidDataException("Incomplete provenance JSON.");
        }
    }
}
