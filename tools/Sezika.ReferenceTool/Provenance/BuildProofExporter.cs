using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.PortProvenance;

namespace Sezika.ReferenceTool.Provenance;

internal sealed record BuildProofRequest(ProgramFilePin BuildRequest, ProgramFilePin BuildExecution,
    ProgramFilePin CompilerExecutable, string SourceRoot, string RuntimeDirectory);
[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(BuildProofRequest))]
internal partial class BuildProofJson : JsonSerializerContext;

internal static class BuildProofExporter
{
    private static StringComparer Paths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    internal static int Run(string requestPath, string summaryPath, CancellationToken cancellation)
    {
        var budget = new Budget(30, 536870912, null, cancellation); var requestPin = budget.Pin(requestPath, 4194304);
        var request = budget.Read(requestPin, BuildProofJson.Default.BuildProofRequest, 4194304);
        Budget.Safe(summaryPath);
        if (Path.Exists(summaryPath) || !Directory.Exists(Path.GetDirectoryName(summaryPath))) throw new IOException("Fresh build-proof summary path required.");
        Budget.Safe(request.SourceRoot); Budget.Safe(request.RuntimeDirectory);
        if (!Directory.Exists(request.SourceRoot) || !Directory.Exists(request.RuntimeDirectory) || Within(summaryPath, request.RuntimeDirectory))
            throw new InvalidDataException("Existing explicit source/runtime directories and summary outside application output required.");
        var build = budget.Read(Local(request.BuildRequest), CompiledProgramJson.Default.ProgramBuildRequest, 4194304);
        _ = budget.Read(Local(request.BuildExecution), CompiledProgramJson.Default.ProgramBuildExecution, 20971520);
        budget.Verify(Local(request.CompilerExecutable), 536870912);
        if (!SamePath(build.WorkingDirectory, request.SourceRoot) || !SamePath(build.Executable, request.CompilerExecutable.Path) ||
            build.ArgumentList.Length is < 4 or > 64 || build.ArgumentList[0] is not ("build" or "publish"))
            throw new InvalidDataException("Summary exporter requires the exact real compiler/build/source snapshot request.");
        var projectPath = Path.GetFullPath(build.ArgumentList[1], request.SourceRoot); Budget.Safe(projectPath);
        if (!Within(projectPath, request.SourceRoot) || Path.GetFileName(projectPath) != "Sezika.ReferenceTool.csproj" || !File.Exists(projectPath))
            throw new InvalidDataException("Exactly one real pinned ReferenceTool project required by build argv.");
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var sources = EnumerateFiles(projectDirectory, budget, 2048, 64, 16, skipBuildDirectories: true)
            .Where(path => Extension(path, ".cs") || Extension(path, ".csproj")).ToArray();
        if (sources.Length is < 2 or > 64 || sources.Count(path => Extension(path, ".csproj")) != 1 || !sources.Any(path => SamePath(path, projectPath)))
            throw new InvalidDataException("Default project C# source/project inventory must be complete, unique and bounded64.");
        var sourcePins = sources.OrderBy(path => path, Paths).Select(path => Pin(path, budget, 4194304)).ToArray();
        var inputs = BuildInputs(projectDirectory, request.SourceRoot, projectPath, budget);
        var runtimeFiles = EnumerateFiles(request.RuntimeDirectory, budget, 512, 256, 8)
            .OrderBy(path => path, Paths).Select(path => Pin(path, budget, 536870912)).ToArray();
        var assembly = Required(runtimeFiles, "Sezika.ReferenceTool.dll"); var symbols = Required(runtimeFiles, "Sezika.ReferenceTool.pdb");
        _ = Required(runtimeFiles, "Sezika.ReferenceTool.deps.json"); _ = Required(runtimeFiles, "Sezika.ReferenceTool.runtimeconfig.json");
        var apphosts = runtimeFiles.Where(pin => SamePath(Path.GetDirectoryName(pin.Path)!, request.RuntimeDirectory) &&
            Path.GetFileName(pin.Path) is "Sezika.ReferenceTool" or "Sezika.ReferenceTool.exe").ToArray();
        if (apphosts.Length != 1) throw new InvalidDataException("Exactly one complete FDD application host required.");
        ProgramSourceDocument[] documents;
        budget.Verify(Local(assembly), 536870912);
        using (var imageStream = File.OpenRead(assembly.Path))
        using (var image = new PEReader(imageStream, PEStreamOptions.LeaveOpen))
            if (!image.HasMetadata || image.PEHeaders.CorHeader is null) throw new InvalidDataException("Build-proof exporter only supports real managed JIT PE; Native AOT remains unverified.");
        budget.Verify(Local(symbols), 536870912);
        using (var symbolStream = File.OpenRead(symbols.Path))
        using (var provider = MetadataReaderProvider.FromPortablePdbStream(symbolStream, MetadataStreamOptions.LeaveOpen))
            documents = CompiledProgramProof.ReadSourceDocuments(provider.GetMetadataReader(), budget.Check);
        var handwritten = sourcePins.Where(pin => Extension(pin.Path, ".cs")).ToDictionary(pin => Canonical(pin.Path), Paths);
        var generatedDirectory = GeneratedDirectory(build, request.SourceRoot);
        ProgramFilePin[]? exported = null; var generated = new List<ProgramGeneratedSource>();
        foreach (var document in documents)
        {
            budget.Check(); var path = Canonical(document.Path);
            if (!Within(document.Path, request.SourceRoot)) throw new InvalidDataException("Actual compiled PDB document must belong to this exact source snapshot: " + document.Path);
            if (handwritten.TryGetValue(path, out var source))
            {
                if (source.Sha256 != document.Sha256) throw new InvalidDataException("Handwritten source differs from actual compiled PDB checksum: " + document.Path);
                continue;
            }
            ProgramFilePin selected;
            if (File.Exists(document.Path)) selected = Pin(document.Path, budget, 4194304);
            else
            {
                if (generatedDirectory is null) throw new InvalidDataException("Virtual PDB source has no actual compiler generated-output argument: " + document.Path);
                exported ??= EnumerateFiles(generatedDirectory, budget, 2048, 512, 8).Where(file => Extension(file, ".cs"))
                    .Select(file => Pin(file, budget, 4194304)).ToArray();
                var matches = exported.Where(pin => Paths.Equals(Path.GetFileName(pin.Path), Path.GetFileName(document.Path)) && pin.Sha256 == document.Sha256).ToArray();
                if (matches.Length != 1) throw new InvalidDataException("Virtual PDB source requires exactly one real leafname/full-SHA generated-file match: " + document.Path);
                selected = matches[0];
            }
            if (selected.Sha256 != document.Sha256) throw new InvalidDataException("Real generated source bytes differ from compiled PDB checksum: " + document.Path);
            generated.Add(new(document.Path, selected));
            if (generated.Count > 512) throw new InvalidDataException("Generated source count bound512.");
        }
        var summary = new PortBuildSummary("PASS_exact_reference_program_source_build_only", sourcePins,
            apphosts[0], assembly, request.BuildExecution, request.BuildRequest, request.CompilerExecutable, inputs,
            Canonical(request.RuntimeDirectory), runtimeFiles, assembly, symbols, "Release", "net10.0", "managed_jit_source_build_only", generated.ToArray());
        var verified = CompiledProgramProof.Verify(summary, cancellation, budget.Check);
        budget.Verify(requestPin, 4194304);
        // Only a verified source-build-only summary is written. Failed/incomplete proof remains absent.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(summary, CompiledProgramJson.Default.PortBuildSummary);
        if (bytes.Length > 4194304) throw new InvalidDataException("Build-proof summary byte bound4MiB.");
        Budget.WriteNew(summaryPath, bytes);
        Console.WriteLine(verified.Status + " documents=" + verified.SourceDocuments + " runtime_files=" + verified.RuntimeFiles);
        return 0;
    }

    private static ProgramFilePin[] BuildInputs(string projectDirectory, string sourceRoot, string project, Budget budget)
    {
        var names = new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.Config", "NuGet.config" };
        var paths = new HashSet<string>(Paths) { Canonical(project) }; var cursor = projectDirectory; var complete = false;
        for (var depth = 0; depth < 32; depth++)
        {
            budget.Check();
            foreach (var name in names)
            {
                var path = Path.Combine(cursor, name); Budget.Safe(path);
                if (File.Exists(path)) paths.Add(Canonical(path));
            }
            if (SamePath(cursor, sourceRoot)) { complete = true; break; }
            var parent = Path.GetDirectoryName(cursor);
            if (parent is null || !SamePath(parent, sourceRoot) && !Within(parent, sourceRoot)) throw new InvalidDataException("Build-input ancestry escaped the exact source snapshot.");
            cursor = parent;
        }
        if (!complete || paths.Count > 128) throw new InvalidDataException("Build-input ancestry/inventory bounds32/128.");
        return paths.OrderBy(path => path, Paths).Select(path => Pin(path, budget, 4194304)).ToArray();
    }
    private static string? GeneratedDirectory(ProgramBuildRequest request, string sourceRoot)
    {
        var emit = request.ArgumentList.Count(argument => argument == "-p:EmitCompilerGeneratedFiles=true");
        var settings = request.ArgumentList.Where(argument => argument.StartsWith("-p:CompilerGeneratedFilesOutputPath=", StringComparison.Ordinal)).ToArray();
        if (settings.Length == 0 && emit == 0) return null;
        if (settings.Length != 1 || emit != 1) throw new InvalidDataException("Exactly one actual emitted compiler-output setting required.");
        var directory = settings[0]["-p:CompilerGeneratedFilesOutputPath=".Length..]; Budget.Safe(directory);
        if (!Within(directory, sourceRoot) || !Directory.Exists(directory)) throw new InvalidDataException("Actual compiler generated-output directory must exist in the source snapshot.");
        return Canonical(directory);
    }
    private static string[] EnumerateFiles(string directory, Budget budget, int maximumEntries, int maximumFiles,
        int maximumDepth, bool skipBuildDirectories = false)
    {
        Budget.Safe(directory); var pending = new Queue<(string Path, int Depth)>(); pending.Enqueue((directory, 0));
        var files = new List<string>(); var entries = 0;
        for (var index = 0; index < 256 && pending.Count != 0; index++)
        {
            budget.Check(); var current = pending.Dequeue();
            foreach (var path in Directory.EnumerateFileSystemEntries(current.Path))
            {
                budget.Check(); if (++entries > maximumEntries) throw new InvalidDataException("Build-proof directory entry bound."); Budget.Safe(path);
                if (Directory.Exists(path))
                {
                    var name = Path.GetFileName(path);
                    if (skipBuildDirectories && (name is "bin" or "obj" || name.StartsWith(".", StringComparison.Ordinal))) continue;
                    if (current.Depth >= maximumDepth) throw new InvalidDataException("Build-proof directory depth bound.");
                    pending.Enqueue((path, current.Depth + 1));
                }
                else
                {
                    files.Add(Canonical(path)); if (files.Count > maximumFiles) throw new InvalidDataException("Build-proof file count bound.");
                }
            }
        }
        if (pending.Count != 0 || files.Distinct(Paths).Count() != files.Count) throw new InvalidDataException("Build-proof directory count bound256/duplicate paths.");
        return files.ToArray();
    }
    private static ProgramFilePin Required(ProgramFilePin[] files, string name)
    {
        var matches = files.Where(pin => Path.GetFileName(pin.Path) == name).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Exactly one mandatory runtime file required: " + name);
        return matches[0];
    }
    private static ProgramFilePin Pin(string path, Budget budget, long maximum)
    {
        var pin = budget.Pin(path, maximum); return new(pin.Path, pin.Bytes, pin.Sha256);
    }
    private static FilePin Local(ProgramFilePin pin) => new(pin.Path, pin.Bytes, pin.Sha256);
    private static string Canonical(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    private static bool SamePath(string left, string right) => Paths.Equals(Canonical(left), Canonical(right));
    private static bool Within(string path, string directory) => Canonical(path).StartsWith(Canonical(directory) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Extension(string path, string extension) => Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase);
}
