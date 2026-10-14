namespace Sezika.Tooling;

internal static class BundleInputCoverage
{
    internal const int MaximumFiles = 4096;
    internal const int MaximumDirectories = 256;
    internal const int MaximumDepth = 12;
    internal const long MaximumBytes = 512L * 1024 * 1024;
    private static readonly string[] RequiredRootInputs = ["Directory.Build.props", "global.json", "README.md", "LICENSE", "NOTICE", "THIRD_PARTY_NOTICES.md"];
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".artifacts", "data", "dataset", "datasets", "model", "models", "downloads", "outputs", "logs"
    };

    // Capture repository inputs only: root configuration/licensing plus src and
    // tools. Generated PTX and ABI JSON are inputs; build outputs are excluded.
    internal static BundleFile[] Capture(string sourceRoot, Budget budget)
    {
        var root = Root(sourceRoot, budget);
        var pending = new Queue<(string Path, int Depth)>(); pending.Enqueue((root, 0));
        var files = new List<BundleFile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queued = 1; var examined = 0; long bytes = 0;
        for (var directory = 0; directory < MaximumDirectories && pending.Count != 0; directory++)
        {
            budget.Check(); var item = pending.Dequeue();
            foreach (var path in Directory.EnumerateFileSystemEntries(item.Path))
            {
                budget.Check(); if (++examined > 8192) throw new InvalidDataException("Build input directory-entry bound8192.");
                var name = Path.GetFileName(path);
                if (Directory.Exists(path))
                {
                    if (item.Depth == 0 && name is not ("src" or "tools") || ExcludedDirectories.Contains(name)) continue;
                    SafePaths.NoLinks(path, budget);
                    if (item.Depth >= MaximumDepth || ++queued > MaximumDirectories) throw new InvalidDataException("Build input directory bound256 or depth bound12.");
                    pending.Enqueue((path, item.Depth + 1)); continue;
                }
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!Admitted(relative)) continue;
                relative = SafePaths.Relative(relative); RegularFiles.Require(path, budget);
                if (files.Count >= MaximumFiles || !names.Add(relative)) throw new InvalidDataException("Build input file bound4096 or portable duplicate.");
                using var input = File.OpenRead(path);
                var length = input.Length; bytes = checked(bytes + length);
                if (length < 0 || bytes > MaximumBytes) throw new InvalidDataException("Build input byte bound512MiB.");
                files.Add(new(Path.GetFullPath(path), relative, length, budget.Hash(input, length)));
            }
        }
        if (pending.Count != 0) throw new InvalidDataException("Build input directory iteration bound256.");
        return files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
    }

    internal static BundleFile[] Validate(string sourceRoot, BundleProject[] projects, BundleFile[] engineeringSources, Budget budget)
    {
        budget.Check();
        if (projects.Length is < 1 or > 5 || engineeringSources.Length is < 1 or > MaximumFiles) throw new InvalidDataException("Build project bound5 or engineering input bound4096.");
        var root = Root(sourceRoot, budget);
        var supplied = new Dictionary<string, BundleFile>(StringComparer.OrdinalIgnoreCase); long suppliedBytes = 0;
        foreach (var file in engineeringSources)
        {
            budget.Check(); var relative = SafePaths.Relative(file.RelativePath);
            if (relative != file.RelativePath || !Admitted(relative) || TaskOperations.IsScript(relative) || !Path.IsPathFullyQualified(file.Source)) throw new InvalidDataException("Nonportable, script or nonengineering input.");
            var expected = SafePaths.Within(root, relative, budget); var actual = Path.GetFullPath(file.Source);
            if (!actual.Equals(expected, PathComparison) || !supplied.TryAdd(relative, file)) throw new InvalidDataException("Engineering source escaped SourceRoot, relative path differs, or duplicate input.");
            TaskOperations.Sha(file.Sha256);
            if (file.Bytes < 0 || (suppliedBytes = checked(suppliedBytes + file.Bytes)) > MaximumBytes) throw new InvalidDataException("Engineering input byte bound512MiB.");
        }
        var captured = Capture(root, budget);
        if (captured.Length != supplied.Count) throw new InvalidDataException("Engineering input manifest does not cover every admitted repository input exactly.");
        foreach (var actual in captured)
        {
            budget.Check();
            if (!supplied.TryGetValue(actual.RelativePath, out var pin) || pin.RelativePath != actual.RelativePath || pin.Bytes != actual.Bytes || pin.Sha256 != actual.Sha256) throw new InvalidDataException("Missing or changed engineering input: " + actual.RelativePath);
        }
        var projectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            budget.Check(); var name = SafePaths.Relative(project.Name); var path = SafePaths.Relative(project.ProjectPath);
            if (name.Contains('/') || !projectNames.Add(name) || path != project.ProjectPath || !path.EndsWith(".csproj", StringComparison.Ordinal) || !supplied.ContainsKey(path)) throw new InvalidDataException("Selected project does not belong to the complete engineering input manifest.");
        }
        return captured;
    }

    // The build must use this fresh snapshot as WorkingDirectory and resolve its
    // projects there. Revalidating the original root alone cannot bind a build.
    internal static BundleFile[] CopyToFreshBuildRoot(string sourceRoot, string destinationRoot, BundleProject[] projects, BundleFile[] engineeringSources, Budget budget)
    {
        var validated = Validate(sourceRoot, projects, engineeringSources, budget);
        if (!Path.IsPathFullyQualified(destinationRoot)) throw new InvalidDataException("Absolute fresh build snapshot required.");
        var destination = Path.GetFullPath(destinationRoot); SafePaths.NoLinks(destination, budget);
        if (Path.Exists(destination)) throw new IOException("Build snapshot exists; preserve its bytes.");
        var source = Path.GetFullPath(sourceRoot);
        foreach (var scope in new[] { "src", "tools" })
        {
            var admittedRoot = Path.Combine(source, scope);
            if (destination.Equals(admittedRoot, PathComparison) || destination.StartsWith(admittedRoot + Path.DirectorySeparatorChar, PathComparison)) throw new InvalidDataException("Build snapshot cannot expand an enumerated engineering source directory.");
        }
        budget.Check(); Directory.CreateDirectory(destination); SafePaths.NoLinks(destination, budget);
        var copied = new BundleFile[validated.Length];
        for (var index = 0; index < validated.Length; index++)
        {
            budget.Check(); var file = validated[index]; var target = SafePaths.Within(destination, file.RelativePath, budget);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); SafePaths.NoLinks(target, budget);
            Copy(file.Source, target, file.Bytes, budget); TaskOperations.VerifyFile(target, file.Bytes, file.Sha256, budget);
            copied[index] = new(target, file.RelativePath, file.Bytes, file.Sha256);
        }
        // Source changes/additions during copying reject the candidate. The
        // failed owned snapshot remains explicit evidence and is never replaced.
        _ = Validate(sourceRoot, projects, engineeringSources, budget);
        _ = Validate(destination, projects, copied, budget);
        return copied;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static string Root(string sourceRoot, Budget budget)
    {
        budget.Check();
        if (!Path.IsPathFullyQualified(sourceRoot)) throw new InvalidDataException("Absolute SourceRoot required.");
        var root = Path.GetFullPath(sourceRoot); SafePaths.NoLinks(root, budget);
        if (!Directory.Exists(root) || Path.TrimEndingDirectorySeparator(root).Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), PathComparison)) throw new InvalidDataException("Explicit repository SourceRoot required.");
        foreach (var name in RequiredRootInputs) RegularFiles.Require(SafePaths.Within(root, name, budget), budget);
        return root;
    }
    private static bool Admitted(string relative)
    {
        if (TaskOperations.IsScript(relative)) return false;
        var components = relative.Split('/');
        if (components.Length > 1 && (components[0] is not ("src" or "tools") || components.Take(components.Length - 1).Any(ExcludedDirectories.Contains))) return false;
        var name = Path.GetFileName(relative);
        if (components.Length == 1 && (RequiredRootInputs.Contains(name, StringComparer.Ordinal) || name.Equals("README.en.md", StringComparison.Ordinal))) return true;
        if (name.Equals(".editorconfig", StringComparison.Ordinal)) return true;
        return Path.GetExtension(relative).ToLowerInvariant() is ".cs" or ".csproj" or ".props" or ".targets" or ".ptx" or ".json" or ".resx" or ".config" or ".rsp" or ".slnx" or ".sln";
    }
    private static void Copy(string source, string destination, long bytes, Budget budget)
    {
        RegularFiles.Require(source, budget);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != bytes) throw new InvalidDataException("Engineering source length changed before snapshot.");
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[128 * 1024]; long total = 0; var eof = false;
        for (long block = 0, maximum = bytes / buffer.Length + 2; block < maximum; block++)
        {
            budget.Check(); var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes - total + 1));
            if (read == 0) { if (total != bytes) throw new InvalidDataException("Engineering source ended before its pinned size."); eof = true; break; }
            total += read; if (total > bytes) throw new InvalidDataException("Engineering source grew during snapshot.");
            output.Write(buffer, 0, read);
        }
        if (!eof) throw new InvalidDataException("Engineering source copy block bound reached.");
        output.Flush(true);
    }
}
