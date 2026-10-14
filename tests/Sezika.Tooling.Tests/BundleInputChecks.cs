using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class BundleInputChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Budget Limit() => new(25, null, cancellation.Token);
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Reject(Action action)
        {
            try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OperationCanceledException or TimeoutException) { return; }
            throw new InvalidOperationException("Expected engineering input fixture rejection.");
        }
        string PathFor(string name) => Path.Combine(root, "bundle-input-" + name);
        void Write(string directory, string relative, string text)
        {
            var path = SafePaths.Within(directory, relative, Limit()); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        (string Root, BundleProject[] Projects, BundleFile[] Pins) Fixture(string name)
        {
            cancellation.Token.ThrowIfCancellationRequested(); var directory = PathFor(name); Directory.CreateDirectory(directory);
            // Synthetic source/ABI identity fixtures only; no build, kernel,
            // optimizer, CUDA or model is executed by this suite.
            (string Path, string Text)[] files =
            [
                ("Directory.Build.props", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"),
                ("global.json", "{\"sdk\":{\"version\":\"10.0.100\"}}"),
                (".editorconfig", "root = true\n"), ("README.md", "Synthetic engineering source fixture\n"),
                ("LICENSE", "Synthetic license fixture\n"), ("NOTICE", "Synthetic notice fixture\n"), ("THIRD_PARTY_NOTICES.md", "Synthetic provenance fixture\n"),
                ("src/Sezika/Sezika.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"), ("src/Sezika/Runtime.cs", "// Runtime source identity fixture\n"),
                ("src/Sezika.Kernels/Sezika.Kernels.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"), ("src/Sezika.Kernels/Kernels.cs", "// Kernel source identity fixture\n"),
                ("src/Sezika.Cuda/Sezika.Cuda.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"),
                ("src/Sezika.Cuda/Generated/Add.ptx", "Synthetic PTX identity bytes; never executed\n"),
                ("src/Sezika.Cuda/Generated/kernel-manifest.json", "{\"syntheticAbiIdentity\":true}"),
                ("src/Sezika.Cuda/Generated/EncoderBackward/AdamW.ptx", "Synthetic backward PTX identity bytes; never executed\n"),
                ("src/Sezika.Cuda/Generated/EncoderBackward/backward-kernel-manifest.json", "{\"syntheticBackwardAbiIdentity\":true}"),
                ("tools/Sezika.KernelCompiler/Sezika.KernelCompiler.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"), ("tools/Sezika.KernelCompiler/Program.cs", "// Forward compiler source fixture\n"),
                ("tools/Sezika.BackwardKernelCompiler/Sezika.BackwardKernelCompiler.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"), ("tools/Sezika.BackwardKernelCompiler/Program.cs", "// Backward compiler source fixture\n"),
                ("tools/Sezika.RepositoryTrainer/Sezika.RepositoryTrainer.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"), ("tools/Sezika.RepositoryTrainer/Program.cs", "// Trainer source fixture\n")
            ];
            foreach (var file in files) { cancellation.Token.ThrowIfCancellationRequested(); Write(directory, file.Path, file.Text); }
            return (directory, [new("RepositoryTrainer", "tools/Sezika.RepositoryTrainer/Sezika.RepositoryTrainer.csproj")], BundleInputCoverage.Capture(directory, Limit()));
        }
        check("Bundle source tiny capture pins root configuration and source identity", () =>
        {
            var fixture = Fixture("tiny"); Assert(fixture.Pins.Length == 22 && fixture.Pins.All(file => Path.IsPathFullyQualified(file.Source)) && fixture.Pins.Any(file => file.RelativePath == "Directory.Build.props"), "Complete synthetic input capture differs.");
        });
        check("Bundle source exact full manifest is accepted", () =>
        {
            var fixture = Fixture("complete"); var validated = BundleInputCoverage.Validate(fixture.Root, fixture.Projects, fixture.Pins, Limit());
            Assert(validated.SequenceEqual(fixture.Pins), "Validated complete engineering manifest changed.");
        });
        check("Bundle source missing PTX and ABI inputs are rejected", () =>
        {
            var fixture = Fixture("ptx-abi");
            foreach (var missing in new[] { "src/Sezika.Cuda/Generated/Add.ptx", "src/Sezika.Cuda/Generated/kernel-manifest.json", "src/Sezika.Cuda/Generated/EncoderBackward/AdamW.ptx", "src/Sezika.Cuda/Generated/EncoderBackward/backward-kernel-manifest.json" })
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Reject(() => BundleInputCoverage.Validate(fixture.Root, fixture.Projects, fixture.Pins.Where(file => file.RelativePath != missing).ToArray(), Limit()));
            }
        });
        check("Bundle source missing project props and both compiler sources are rejected", () =>
        {
            var fixture = Fixture("compiler");
            foreach (var missing in new[] { "tools/Sezika.RepositoryTrainer/Sezika.RepositoryTrainer.csproj", "Directory.Build.props", "tools/Sezika.KernelCompiler/Program.cs", "tools/Sezika.BackwardKernelCompiler/Program.cs" })
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Reject(() => BundleInputCoverage.Validate(fixture.Root, fixture.Projects, fixture.Pins.Where(file => file.RelativePath != missing).ToArray(), Limit()));
            }
        });
        check("Bundle source rejects external paths relative mismatches and duplicates", () =>
        {
            var fixture = Fixture("paths"); var original = fixture.Pins[0]; var outside = PathFor("external.cs"); File.WriteAllText(outside, File.ReadAllText(original.Source), new UTF8Encoding(false));
            Reject(() => BundleInputCoverage.Validate(fixture.Root, fixture.Projects, [original with { Source = outside }, .. fixture.Pins.Skip(1)], Limit()));
            Reject(() => BundleInputCoverage.Validate(fixture.Root, fixture.Projects, [original with { RelativePath = "src/Sezika/Other.cs" }, .. fixture.Pins.Skip(1)], Limit()));
            Reject(() => BundleInputCoverage.Validate(fixture.Root, fixture.Projects, [.. fixture.Pins, original], Limit()));
        });
        check("Bundle source changed bytes reject before creating a build snapshot", () =>
        {
            var fixture = Fixture("changed"); var source = fixture.Pins.Single(file => file.RelativePath == "tools/Sezika.RepositoryTrainer/Program.cs"); File.AppendAllText(source.Source, "// Changed source\n");
            var destination = PathFor("changed-snapshot");
            Reject(() => BundleInputCoverage.CopyToFreshBuildRoot(fixture.Root, destination, fixture.Projects, fixture.Pins, Limit()));
            Assert(!Directory.Exists(destination), "A wrong engineering byte pin created a build snapshot.");
        });
        check("Bundle source real copy retains independent hashes root licenses and PTX", () =>
        {
            var fixture = Fixture("copy"); var destination = PathFor("copy-snapshot");
            var copied = BundleInputCoverage.CopyToFreshBuildRoot(fixture.Root, destination, fixture.Projects, fixture.Pins, Limit());
            Assert(copied.Length == fixture.Pins.Length && copied.Select(file => (file.RelativePath, file.Bytes, file.Sha256)).SequenceEqual(fixture.Pins.Select(file => (file.RelativePath, file.Bytes, file.Sha256))), "Real copied snapshot identity differs.");
            foreach (var file in copied)
            {
                cancellation.Token.ThrowIfCancellationRequested(); TaskOperations.VerifyFile(file.Source, file.Bytes, file.Sha256, Limit());
                Assert(Path.GetFullPath(file.Source) == Path.GetFullPath(Path.Combine(destination, file.RelativePath)), "Copied source does not bind the fresh build root.");
            }
            Write(fixture.Root, "tools/Sezika.RepositoryTrainer/Program.cs", "// Original changed after copy\n");
            var snapshot = copied.Single(file => file.RelativePath == "tools/Sezika.RepositoryTrainer/Program.cs"); TaskOperations.VerifyFile(snapshot.Source, snapshot.Bytes, snapshot.Sha256, Limit());
            Reject(() => BundleInputCoverage.CopyToFreshBuildRoot(fixture.Root, destination, fixture.Projects, fixture.Pins, Limit()));
        });
        check("Bundle source excludes build data and model directories and refuses script pins", () =>
        {
            var fixture = Fixture("exclusions");
            foreach (var excluded in new[] { "tools/Sezika.RepositoryTrainer/bin/extra.cs", "tools/Sezika.RepositoryTrainer/obj/extra.cs", "tools/Sezika.RepositoryTrainer/data/training.json", "tools/Sezika.RepositoryTrainer/models/weights.json", ".artifacts/extra.cs", ".git/extra.cs", "tools/Sezika.RepositoryTrainer/inert.ps1" })
                Write(fixture.Root, excluded, "Inert excluded fixture bytes; never executed\n");
            var captured = BundleInputCoverage.Capture(fixture.Root, Limit()); Assert(captured.SequenceEqual(fixture.Pins), "Excluded data/build/script inputs entered the engineering manifest.");
            var script = Path.Combine(fixture.Root, "tools", "Sezika.RepositoryTrainer", "inert.ps1"); using var input = File.OpenRead(script);
            var pin = new BundleFile(script, "tools/Sezika.RepositoryTrainer/inert.ps1", input.Length, Limit().Hash(input, input.Length));
            Reject(() => BundleInputCoverage.Validate(fixture.Root, fixture.Projects, [.. fixture.Pins, pin], Limit()));
            var destination = PathFor("excluded-snapshot"); _ = BundleInputCoverage.CopyToFreshBuildRoot(fixture.Root, destination, fixture.Projects, fixture.Pins, Limit());
            Assert(!File.Exists(Path.Combine(destination, pin.RelativePath)), "A script was copied into the build root.");
        });
        check("Bundle source cancellation leaves no new build snapshot", () =>
        {
            var fixture = Fixture("cancelled"); var destination = PathFor("cancelled-snapshot");
            Reject(() => BundleInputCoverage.CopyToFreshBuildRoot(fixture.Root, destination, fixture.Projects, fixture.Pins, new Budget(25, null, new CancellationToken(canceled: true))));
            Assert(!Directory.Exists(destination), "Pre-existing cancellation created a snapshot.");
        });
        check("Bundle source refuses selected projects outside the complete input contract", () =>
        {
            var fixture = Fixture("selected-project");
            Reject(() => BundleInputCoverage.Validate(fixture.Root, [new("Other", "tools/Other/Other.csproj")], fixture.Pins, Limit()));
            Reject(() => BundleInputCoverage.Validate(fixture.Root, [new("Bad/Name", fixture.Projects[0].ProjectPath)], fixture.Pins, Limit()));
        });
        check("Bundle source refuses excessive traversal depth and manifest count", () =>
        {
            var fixture = Fixture("bounds"); var directory = "tools";
            for (var depth = 0; depth < 13; depth++) { cancellation.Token.ThrowIfCancellationRequested(); directory += "/d" + depth; }
            Write(fixture.Root, directory + "/Deep.cs", "// Excessive source depth fixture\n");
            Reject(() => BundleInputCoverage.Capture(fixture.Root, Limit()));
            Reject(() => BundleInputCoverage.Validate(fixture.Root, fixture.Projects, Enumerable.Repeat(fixture.Pins[0], BundleInputCoverage.MaximumFiles + 1).ToArray(), Limit()));
        });
        check("Bundle repository relative missing and linked roots reject before any native launch", () =>
        {
            var fixture = Fixture("repository-paths"); var image = Environment.ProcessPath ?? throw new InvalidOperationException("Current native test host missing.");
            // The existing test apphost is only an unused executable-admission
            // input. Every branch must reject before output or a native call.
            var request = new BundleBuildRequest(fixture.Root, new string('a', 40), image, image, PathFor("repository-output"),
                OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", fixture.Projects, fixture.Pins, MinimumFreeDiskBytes: 128L * 1024 * 1024);
            Assert(request.SourceRepositoryRoot is null, "Legacy constructor no longer defaults to SourceRoot.");
            using (var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(request, BundleBuildJson.Default.BundleBuildRequest)))
            using (var memory = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(memory))
                {
                    writer.WriteStartObject(); var fields = 0;
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        cancellation.Token.ThrowIfCancellationRequested(); if (++fields > 16) throw new InvalidDataException("Bundle request fixture field bound16.");
                        if (property.Name != nameof(BundleBuildRequest.SourceRepositoryRoot)) property.WriteTo(writer);
                    }
                    writer.WriteEndObject();
                }
                var legacy = JsonSerializer.Deserialize(memory.ToArray(), BundleBuildJson.Default.BundleBuildRequest) ?? throw new InvalidDataException("Legacy typed request missing.");
                Assert(legacy.SourceRepositoryRoot is null, "An old JSON request acquired a required repository field.");
            }
            void RejectRepository(string repository, string expected)
            {
                string? observed = null;
                try { BundleBuild.BuildAsync(request with { SourceRepositoryRoot = repository }, Limit(), cancellation.Token).GetAwaiter().GetResult(); }
                catch (Exception error) when (error is InvalidDataException or DirectoryNotFoundException) { observed = error.Message; }
                Assert(observed == expected && !Directory.Exists(request.OutputDirectory), "Repository preflight hit another gate or created native execution output.");
            }
            RejectRepository("relative-repository", "Explicit SourceRepositoryRoot must be absolute.");
            RejectRepository(PathFor("repository-missing"), "Explicit SourceRepositoryRoot must exist.");
            var repository = PathFor("repository-existing"); Directory.CreateDirectory(repository);
            var link = PathFor("repository-link"); Directory.CreateSymbolicLink(link, repository);
            try { RejectRepository(link, "Linked path ancestry is not allowed."); }
            finally
            {
                if (new DirectoryInfo(link).LinkTarget != repository) throw new IOException("Owned repository link changed; preserve it for review.");
                Directory.Delete(link);
            }
            Assert(Directory.Exists(repository), "Repository link cleanup removed its task-owned target directory.");
        });
        check("Bundle separate repository cannot replace or bypass frozen snapshot byte validation", () =>
        {
            var fixture = Fixture("repository-snapshot"); var repository = Fixture("repository-baseline");
            var source = fixture.Pins.Single(file => file.RelativePath == "tools/Sezika.RepositoryTrainer/Program.cs");
            var baseline = repository.Pins.Single(file => file.RelativePath == source.RelativePath); TaskOperations.VerifyFile(baseline.Source, baseline.Bytes, baseline.Sha256, Limit());
            Write(fixture.Root, source.RelativePath, "// Changed frozen snapshot; repository still retains the original bytes\n");
            var image = Environment.ProcessPath ?? throw new InvalidOperationException("Current native test host missing.");
            var request = new BundleBuildRequest(fixture.Root, new string('a', 40), image, image, PathFor("repository-snapshot-output"),
                OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", fixture.Projects, fixture.Pins, MinimumFreeDiskBytes: 128L * 1024 * 1024, SourceRepositoryRoot: repository.Root);
            using (var overlay = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(request, BundleBuildJson.Default.BundleBuildRequest)))
                Assert(overlay.RootElement.GetProperty(nameof(BundleBuildRequest.SourceRepositoryRoot)).GetString() == repository.Root &&
                    overlay.RootElement.GetProperty(nameof(BundleBuildRequest.SourceRoot)).GetString() == fixture.Root,
                    "Typed engineering-overlay serialization lost the separate repository or frozen source identity.");
            string? observed = null;
            try { BundleBuild.BuildAsync(request, Limit(), cancellation.Token).GetAwaiter().GetResult(); }
            catch (InvalidDataException error) { observed = error.Message; }
            Assert(observed == "Missing or changed engineering input: " + source.RelativePath && !Directory.Exists(request.OutputDirectory), "Another directory bypassed frozen SourceRoot validation or launched a native call.");
            TaskOperations.VerifyFile(baseline.Source, baseline.Bytes, baseline.Sha256, Limit());
            Assert(File.ReadAllText(source.Source).StartsWith("// Changed frozen snapshot", StringComparison.Ordinal), "Rejected snapshot bytes were overwritten from the repository directory.");
        });
    }
}
