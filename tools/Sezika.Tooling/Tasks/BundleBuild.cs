using System.Text;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record BundleProject(string Name, string ProjectPath);
internal sealed record BundleBuildRequest(string SourceRoot, string SourceCommit, string DotnetExecutable, string GitExecutable, string OutputDirectory, string RuntimeIdentifier, BundleProject[] Projects, BundleFile[] EngineeringSources, string? BrokerExecutable = null, long MinimumFreeDiskBytes = 512L * 1024 * 1024, string? SourceRepositoryRoot = null);
internal sealed record BundleInputRequest(string SourceRoot, BundleProject[] Projects);
internal sealed record BundleInputReceipt(string Status, string SourceRoot, BundleProject[] Projects, BundleFile[] EngineeringSources);

internal static class BundleBuild
{
    internal static BundleInputReceipt InspectInputs(BundleInputRequest request, Budget budget)
    {
        var files = BundleInputCoverage.Capture(request.SourceRoot, budget);
        _ = BundleInputCoverage.Validate(request.SourceRoot, request.Projects, files, budget);
        return new("PASS_complete_engineering_input_bytes_only", Path.GetFullPath(request.SourceRoot), request.Projects, files);
    }

    internal static async Task<BundleReceipt> BuildAsync(BundleBuildRequest request, Budget budget, CancellationToken cancellation)
    {
        TaskOperations.Commit(request.SourceCommit);
        var root = Path.GetFullPath(request.SourceRoot); SafePaths.NoLinks(root, budget);
        var repositoryRoot = root;
        if (request.SourceRepositoryRoot is { } sourceRepositoryRoot)
        {
            if (!Path.IsPathFullyQualified(sourceRepositoryRoot)) throw new InvalidDataException("Explicit SourceRepositoryRoot must be absolute.");
            repositoryRoot = Path.GetFullPath(sourceRepositoryRoot); SafePaths.NoLinks(repositoryRoot, budget);
            if (!Directory.Exists(repositoryRoot)) throw new DirectoryNotFoundException("Explicit SourceRepositoryRoot must exist.");
        }
        ProcessExecutor.ValidateExecutable(request.DotnetExecutable, budget); ProcessExecutor.ValidateExecutable(request.GitExecutable, budget);
        if (request.RuntimeIdentifier is not ("linux-x64" or "win-x64") || request.Projects.Length is < 1 or > 5 || request.EngineeringSources.Length is < 1 or > 4096) throw new InvalidDataException("Build runtime/project/source bounds differ.");
        var output = Path.GetFullPath(request.OutputDirectory); SafePaths.NoLinks(output, budget);
        if (Path.Exists(output)) throw new IOException("Bundle build output exists; frozen candidates are never replaced.");
        if (request.MinimumFreeDiskBytes is < 128L * 1024 * 1024 or > 8L * 1024 * 1024 * 1024) throw new InvalidDataException("Bundle free disk reserve must be128MiB..8GiB.");
        _ = TaskOperations.InspectHost(new(Path.GetDirectoryName(output)!, request.MinimumFreeDiskBytes), budget);
        _ = BundleInputCoverage.Validate(root, request.Projects, request.EngineeringSources, budget);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in request.Projects)
        {
            budget.Check();
            if (SafePaths.Relative(project.Name).Contains('/') || !names.Add(project.Name) || !project.ProjectPath.EndsWith(".csproj", StringComparison.Ordinal) || TaskOperations.IsScript(project.ProjectPath)) throw new InvalidDataException("Explicit unique project identities required.");
            RegularFiles.Require(SafePaths.Within(root, project.ProjectPath, budget), budget);
        }
        Directory.CreateDirectory(output);
        var calls = 0;
        async Task<string> Run(string executable, string[] arguments, string name, string? workingDirectory = null)
        {
            budget.Check(); if (++calls > 7) throw new InvalidDataException("Bundle build native call bound7.");
            var result = await ProcessExecutor.ExecuteAsync(new ExecutionRequest
            {
                Executable = executable, ArgumentList = arguments, WorkingDirectory = workingDirectory ?? root,
                OutputDirectory = Path.Combine(output, "process-" + name), BrokerExecutable = request.BrokerExecutable,
                TimeoutSeconds = Math.Max(1, (int)Math.Min(1200, budget.RemainingSeconds)), MaximumOutputBytes = 8 * 1024 * 1024,
                Environment = new() { ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1", ["MSBUILDDISABLENODEREUSE"] = "1", ["UseSharedCompilation"] = "false", ["VSCMD_SKIP_SENDTELEMETRY"] = "1", ["VSCMD_SKIP_VCPKG"] = "1" }
            }, budget, cancellation).ConfigureAwait(false);
            if (result.Status != "PASS" || result.ExitCode != 0) throw new IOException("Bundle native build/ref failed; failure candidate and identities retained.");
            return Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(result.OutputDirectory, "stdout.bin"), cancellation).ConfigureAwait(false)).Trim();
        }
        if (await Run(request.GitExecutable, ["rev-parse", "HEAD"], "source-before", repositoryRoot).ConfigureAwait(false) != request.SourceCommit) throw new InvalidDataException("Actual source HEAD differs.");
        var buildRoot = Path.Combine(output, "build-inputs");
        _ = BundleInputCoverage.CopyToFreshBuildRoot(root, buildRoot, request.Projects, request.EngineeringSources, budget);
        foreach (var project in request.Projects)
        {
            _ = TaskOperations.InspectHost(new(output, request.MinimumFreeDiskBytes), budget);
            var destination = Path.Combine(output, "published", project.Name);
            _ = await Run(request.DotnetExecutable, ["publish", project.ProjectPath, "-c", "Release", "-r", request.RuntimeIdentifier, "--self-contained", "true", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false", "-p:UseAppHost=true", "-o", destination], project.Name, buildRoot).ConfigureAwait(false);
        }
        if (await Run(request.GitExecutable, ["rev-parse", "HEAD"], "source-after", repositoryRoot).ConfigureAwait(false) != request.SourceCommit) throw new InvalidDataException("Source HEAD changed during the freeze.");
        _ = BundleInputCoverage.Validate(root, request.Projects, request.EngineeringSources, budget);
        var copiedSources = request.EngineeringSources.Select(file => file with { Source = SafePaths.Within(buildRoot, file.RelativePath, budget) }).ToArray();
        _ = BundleInputCoverage.Validate(buildRoot, request.Projects, copiedSources, budget);
        var files = new List<BundleFile>(); long bytes = 0;
        var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((Path.Combine(output, "published"), 0));
        for (var directory = 0; directory < 256 && queue.Count != 0; directory++)
        {
            budget.Check(); var next = queue.Dequeue();
            if (next.Depth > 8) throw new InvalidDataException("Published tool depth bound8.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(next.Path))
            {
                budget.Check(); SafePaths.NoLinks(entry, budget);
                if (Directory.Exists(entry)) { if (queue.Count + directory >= 255) throw new InvalidDataException("Published directory enqueue bound256."); queue.Enqueue((entry, next.Depth + 1)); continue; }
                if (files.Count >= 900 || TaskOperations.IsScript(entry)) throw new InvalidDataException("Published tool file bound900 or script dependency.");
                RegularFiles.Require(entry, budget);
                using var input = File.OpenRead(entry);
                bytes = checked(bytes + input.Length); if (bytes > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Published tool bytes bound4GiB.");
                files.Add(new(entry, SafePaths.Relative(Path.GetRelativePath(Path.Combine(output, "published"), entry).Replace('\\', '/')), input.Length, budget.Hash(input, input.Length)));
            }
        }
        if (queue.Count != 0) throw new InvalidDataException("Published directory count bound256.");
        var sourceManifest = Path.Combine(output, "engineering-overlay.json");
        TaskOperations.WriteNew(sourceManifest, request, BundleBuildJson.Default.BundleBuildRequest);
        using (var input = File.OpenRead(sourceManifest)) files.Add(new(sourceManifest, "engineering-overlay.json", input.Length, budget.Hash(input, input.Length)));
        foreach (var name in new[] { "LICENSE", "NOTICE" })
        {
            var path = SafePaths.Within(root, name, budget); RegularFiles.Require(path, budget); using var input = File.OpenRead(path);
            files.Add(new(path, "code-" + name + ".txt", input.Length, budget.Hash(input, input.Length)));
        }
        _ = TaskOperations.InspectHost(new(output, checked(request.MinimumFreeDiskBytes + files.Sum(file => file.Bytes))), budget);
        return TaskOperations.Freeze(new(Path.Combine(output, "frozen"), request.SourceCommit, request.RuntimeIdentifier, files.ToArray()), budget);
    }
}

[JsonSerializable(typeof(BundleBuildRequest))]
[JsonSerializable(typeof(BundleInputRequest))]
[JsonSerializable(typeof(BundleInputReceipt))]
[JsonSourceGenerationOptions(WriteIndented = true, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class BundleBuildJson : JsonSerializerContext;
