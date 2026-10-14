using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.Tooling;

internal static class NativeGitChecks
{
    private sealed record Fixture(string Root, string Repository, string Remote, string BareRepository, string LfsEndpoint, string Commit, GitAsset Notes, string? Hooks, string? HookSha256);

    internal static void Run(string root, Action<string, Action> check)
    {
        var selectedText = Environment.GetEnvironmentVariable("SEZIKA_NATIVE_GIT_CASE");
        var selected = 0;
        if (selectedText is not null && (!int.TryParse(selectedText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out selected) || selected is < 1 or > 8))
            throw new ArgumentException("SEZIKA_NATIVE_GIT_CASE must be an explicit1..8.");
        var rootCheck = check; var caseIndex = 0; var completedCases = 0; var dependencies = new List<string>();
        check = (name, action) =>
        {
            var number = ++caseIndex;
            if (number > 8) throw new InvalidDataException("Native Git declared case bound8.");
            if (selected != 0 && selected != number) return;
            rootCheck("Native Git case " + number + "/8: " + name, () => { action(); completedCases++; });
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(240));
        var clock = Stopwatch.StartNew();
        var calls = 0; var operationCalls = 0; var fixtures = 0; var operations = 0; var receipts = 0; var identities = 0;
        var git = Environment.GetEnvironmentVariable("SEZIKA_GIT_EXECUTABLE") ?? (OperatingSystem.IsWindows() ? @"C:\Program Files\Git\ucrt64\bin\git.exe" : throw new InvalidOperationException("Explicit SEZIKA_GIT_EXECUTABLE required."));
        var lfs = Environment.GetEnvironmentVariable("SEZIKA_GIT_LFS_EXECUTABLE") ?? (OperatingSystem.IsWindows() ? @"C:\Program Files\Git\ucrt64\bin\git-lfs.exe" : throw new InvalidOperationException("Explicit SEZIKA_GIT_LFS_EXECUTABLE required."));
        var broker = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_BROKER");
        var owned = Path.GetFullPath(Path.Combine(root, "native-git-checks"));
        var emptyHooks = Path.Combine(owned, "empty-hooks");
        var httpRoot = Path.Combine(owned, "http-repositories");
        NativeGitHttpServer? gitServer = null; NativeLfsServer? lfsServer = null;
        NativeGitHttpFixture.Client? externalServer = null;
        Fixture? mixed = null; GitReceipt? published = null; GitAsset[] mixedAssets = [];
        Budget Limit() => new(100, null, cancellation.Token);
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Gate()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 230) throw new TimeoutException("Native Git suite wall bound240s.");
        }
        int NativeCall()
        {
            Gate(); var value = Interlocked.Increment(ref calls);
            if (value + Volatile.Read(ref operationCalls) > 200) throw new InvalidDataException("Combined native Git/CGI call bound200.");
            return value;
        }
        void Own()
        {
            Gate(); SafePaths.NoLinks(owned, Limit());
            Directory.CreateDirectory(owned); Directory.CreateDirectory(emptyHooks);
        }
        string New(string name)
        {
            Own(); return SafePaths.Within(owned, name, Limit());
        }
        void Reject(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OperationCanceledException or TimeoutException) { return; }
            throw new InvalidOperationException("Expected real local Git fixture rejection.");
        }
        string Hash(string path)
        {
            RegularFiles.Require(path, Limit()); using var stream = File.OpenRead(path); return Limit().Hash(stream, stream.Length);
        }
        GitAsset Asset(string repository, string relative)
        {
            var path = SafePaths.Within(repository, relative, Limit()); return new(relative, new FileInfo(path).Length, Hash(path));
        }
        void VerifyCleanup(ExecutionResult result, bool recovery = false)
        {
            var recoveryBudget = new Budget(10, null, CancellationToken.None);
            if (!recovery) Gate();
            Assert(result.CleanupStatus == "PASS" && result.RemainingProcesses == 0 && result.RootPid is not null && result.Processes.Length is > 0 and <= 64, "Native process cleanup/identity receipt incomplete: " + result.OutputDirectory);
            foreach (var identity in result.Processes)
            {
                if (recovery) recoveryBudget.Check(); else Gate();
                if (++identities > 8192) throw new InvalidDataException("Native Git identity bound8192.");
                Assert(identity.Pid > 0 && identity.ParentPid > 0 && identity.ParentChain.Length is > 0 and <= 64 && identity.CommandLine.Length > 0 && (identity.Platform == "windows" ? identity.Started is not null : identity.StartTicks is not null && identity.BootId is not null), "PID, birth, full argv or parent chain missing.");
                var current = ProcessObserver.Observe(identity.Pid);
                var reused = current.State == "Present" && (identity.Platform == "windows" ? current.Started is not null && current.Started != identity.Started : current.StartTicks is not null && (current.StartTicks != identity.StartTicks || current.BootId != identity.BootId));
                Assert(current.State == "Absent" || reused, "Native Git task process survives or absence is UNKNOWN: " + identity.Pid);
                var argv = identity.AfterExecCommandLine ?? identity.CommandLine;
                string executable;
                if (identity.Platform == "linux") executable = argv.Split('\0')[0];
                else
                {
                    var text = argv.TrimStart(); var end = text.StartsWith('"') ? text.IndexOf('"', 1) : text.IndexOf(' ');
                    executable = text.StartsWith('"') ? end > 1 ? text[1..end] : throw new InvalidDataException("Native executable argv quote missing.") : end < 0 ? text : text[..end];
                }
                var name = Path.GetFileNameWithoutExtension(executable);
                Assert(!new[] { "sh", "bash", "dash", "zsh", "pwsh", "powershell", "cmd", "python", "python3", "node", "cscript", "wscript" }.Contains(name, StringComparer.OrdinalIgnoreCase),
                    "Interpreter descendant in native Git fixture: " + executable);
            }
        }
        byte[] Execute(string executable, string directory, string[] arguments, Dictionary<string, string?>? environment = null, byte[]? input = null)
        {
            Own(); var call = NativeCall();
            var result = ProcessExecutor.ExecuteAsync(new ExecutionRequest
            {
                Executable = executable, ArgumentList = arguments, WorkingDirectory = directory,
                OutputDirectory = New("process-" + call.ToString("D3")), Environment = environment ?? CleanEnvironment(),
                StandardInput = input ?? [], TimeoutSeconds = 15, CleanupSeconds = 5,
                MaximumOutputBytes = 1024 * 1024, BrokerExecutable = broker
            }, new Budget(25, null, cancellation.Token), cancellation.Token).GetAwaiter().GetResult();
            VerifyCleanup(result);
            Assert(result.Status == "PASS" && result.ExitCode == 0, "Native setup/read failed; preserved receipt: " + result.OutputDirectory);
            return File.ReadAllBytes(Path.Combine(result.OutputDirectory, "stdout.bin"));
        }
        Dictionary<string, string?> CleanEnvironment()
        {
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_LFS_SKIP_SMUDGE"] = "1", ["GIT_CONFIG_NOSYSTEM"] = "1",
                ["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null"
            };
            foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_CONFIG", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT", "GIT_EXEC_PATH", "GIT_SSH", "GIT_SSH_COMMAND", "GIT_PROXY_COMMAND", "GIT_ASKPASS", "SSH_ASKPASS", "GIT_EDITOR", "GIT_SEQUENCE_EDITOR", "GIT_TEMPLATE_DIR", "GIT_TRACE", "GIT_TRACE_CURL", "GIT_CURL_VERBOSE", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" }) environment[name] = null;
            return environment;
        }
        byte[] Git(string directory, params string[] arguments)
        {
            Own();
            string[] safe = ["-c", "credential.helper=", "-c", "core.hooksPath=" + emptyHooks, "-c", "core.autocrlf=false", "-c", "core.fsmonitor=false", "-c", "commit.gpgSign=false", "-c", "gc.auto=0", "-c", "maintenance.auto=false", "-c", "filter.lfs.process=", "-c", "filter.lfs.clean=", "-c", "filter.lfs.smudge=", "-c", "filter.lfs.required=false"];
            return Execute(git, directory, safe.Concat(arguments).ToArray());
        }
        string GitText(string directory, params string[] arguments) => Encoding.UTF8.GetString(Git(directory, arguments)).Trim();
        void Servers()
        {
            if (gitServer is not null || externalServer is not null) return;
            if (!OperatingSystem.IsWindows())
            {
                var configuration = Environment.GetEnvironmentVariable("SEZIKA_NATIVE_GIT_HTTP_REQUEST")
                    ?? throw new InvalidOperationException("Linux native Git HTTP requires an explicit separately supervised server request.");
                externalServer = NativeGitHttpFixture.Client.Open(configuration, Path.GetFullPath(root), Limit());
                httpRoot = externalServer.Ready.RepositoryRoot;
                return;
            }
            Own(); Directory.CreateDirectory(httpRoot);
            lfsServer = new NativeLfsServer(cancellation.Token);
            var backend = Environment.GetEnvironmentVariable("SEZIKA_GIT_HTTP_BACKEND") ?? @"C:\Program Files\Git\ucrt64\libexec\git-core\git-http-backend.exe";
            gitServer = new NativeGitHttpServer(backend, httpRoot, New("http-backend-executions"), emptyHooks, broker, NativeCall, cancellation.Token);
            lfsServer.GitHandler = gitServer.Handle;
        }
        (string Path, string Sha256) Hook(string name)
        {
            Own(); var source = Environment.GetEnvironmentVariable("SEZIKA_NATIVE_GIT_TOOLING") ?? broker ?? throw new InvalidOperationException("SEZIKA_NATIVE_GIT_TOOLING or SEZIKA_PROCESS_BROKER must name the separately built Sezika.Tooling native apphost.");
            ProcessExecutor.ValidateExecutable(source, Limit());
            var directory = New(name); if (Path.Exists(directory)) throw new IOException("Fresh fixture hook directory required.");
            Directory.CreateDirectory(directory); var target = Path.Combine(directory, "pre-push"); File.Copy(source, target, false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(source));
            var copied = 0; long bytes = new FileInfo(source).Length;
            foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetFullPath(source))!))
            {
                Gate(); if (++copied > 256) throw new InvalidDataException("Explicit apphost sibling enumeration bound256.");
                var filename = Path.GetFileName(file);
                if (!(filename.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".so", StringComparison.Ordinal) || filename.EndsWith(".deps.json", StringComparison.Ordinal) || filename.EndsWith(".runtimeconfig.json", StringComparison.Ordinal))) continue;
                RegularFiles.Require(file, Limit()); bytes = checked(bytes + new FileInfo(file).Length);
                if (bytes > 512L * 1024 * 1024) throw new InvalidDataException("Fixture native hook payload bound512MiB.");
                File.Copy(file, SafePaths.Within(directory, filename, Limit()), false);
            }
            var sha = Hash(target); NativeGitHook.VerifyDirectory(directory, sha, Limit()); return (directory, sha);
        }
        Fixture Prepare(string name, string attributes = "*.bin filter=lfs diff=lfs merge=lfs -text\n", bool hook = false)
        {
            Gate(); if (++fixtures > 6) throw new InvalidDataException("Native Git fixture repository bound6.");
            var directory = New(name); if (Path.Exists(directory)) throw new IOException("Fresh local repository fixture required.");
            Directory.CreateDirectory(directory); var repository = Path.Combine(directory, "work");
            Servers(); var bare = Path.Combine(httpRoot, name + ".git");
            var remote = externalServer?.GitEndpoint(name) ?? $"http://127.0.0.1:{lfsServer!.Port}/git/{name}.git";
            _ = Git(directory, "init", "--initial-branch=master", repository);
            _ = Git(directory, "init", "--bare", "--initial-branch=master", bare);
            var endpoint = externalServer?.LfsEndpoint(name) ?? lfsServer!.Register(name, bare);
            gitServer?.Register(name, bare);
            File.WriteAllText(Path.Combine(repository, "notes.txt"), "Seed native transport\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(repository, ".gitattributes"), attributes, new UTF8Encoding(false));
            _ = Git(repository, "add", "--", "notes.txt", ".gitattributes");
            _ = Git(repository, "-c", "user.name=Native local fixture", "-c", "user.email=native-fixture@example.invalid", "commit", "-m", "Seed bounded local fixture");
            _ = Git(repository, "remote", "add", "origin", remote); _ = Git(repository, "push", "origin", "HEAD:refs/heads/master");
            var commit = GitText(repository, "rev-parse", "HEAD"); TaskOperations.Commit(commit);
            var native = hook ? Hook(name + "-hooks") : ((string Path, string Sha256)?)null;
            return new(directory, repository, remote, bare, endpoint, commit, Asset(repository, "notes.txt"), native?.Path, native?.Sha256);
        }
        GitRequest Request(Fixture fixture, string operation, string name, GitAsset[] files, string? commit = null, string? repository = null) => new(git, lfs, operation, repository ?? fixture.Repository, fixture.Remote, commit ?? fixture.Commit, New(name), files,
            LocalFixture: true, BrokerExecutable: broker, NativeHookDirectory: operation == "publish" ? fixture.Hooks : null, CommitMessage: operation == "publish" ? "Real native local fixture publication" : null,
            NativeHookSha256: operation == "publish" ? fixture.HookSha256 : null, Branch: "master", AuthorName: "Native local fixture", AuthorEmail: "native-fixture@example.invalid", LocalLfsEndpoint: fixture.LfsEndpoint);
        void AuditOperation(string output)
        {
            Gate(); if (!Directory.Exists(output)) return;
            var directories = Directory.EnumerateDirectories(output, "call-*").Take(161).ToArray();
            if (directories.Length > 160) throw new InvalidDataException("Git operation process receipt bound160.");
            var hooks = Path.Combine(output, "hooks");
            var delegated = Directory.Exists(hooks) ? Directory.EnumerateDirectories(hooks, "pre-push-*").Take(3).ToArray() : [];
            if (delegated.Length > 2) throw new InvalidDataException("Native pre-push invocation bound2.");
            foreach (var directory in directories.Concat(delegated))
            {
                Gate(); if (++receipts > 2048) throw new InvalidDataException("Git suite native receipt bound2048.");
                using var stream = File.OpenRead(Path.Combine(directory, "result.json"));
                var result = JsonSerializer.Deserialize(stream, ExecutionJsonContext.Default.ExecutionResult) ?? throw new InvalidDataException("Native receipt missing.");
                VerifyCleanup(result);
                if (Interlocked.Increment(ref operationCalls) + Volatile.Read(ref calls) > 200) throw new InvalidDataException("Combined native Git operation/CGI call bound200.");
            }
        }
        GitReceipt Run(GitRequest request)
        {
            Gate(); if (++operations > 8) throw new InvalidDataException("Native Git operation bound8.");
            try { return NativeGit.RunAsync(request, Limit(), cancellation.Token).GetAwaiter().GetResult(); }
            finally { AuditOperation(request.OutputDirectory); }
        }

        try
        {
        // Tiny real executables first: invoke this check alone before the suite.
        check("Native Git and LFS tiny real version calls retain complete process receipts", () =>
        {
            Own(); var gitVersion = Encoding.UTF8.GetString(Execute(git, owned, ["--version"]));
            var lfsVersion = Encoding.UTF8.GetString(Execute(lfs, owned, ["version"]));
            Assert(gitVersion.StartsWith("git version ", StringComparison.Ordinal) && lfsVersion.StartsWith("git-lfs/", StringComparison.Ordinal), "Native Git/LFS identity is not their real version output.");
        });
        void PublishedMixed()
        {
            if (mixed is not null && published?.PublishedCommit is not null) return;
            dependencies.Add("fresh-mixed-publication");
            mixed = Prepare("mixed", hook: true);
            var normal = Encoding.UTF8.GetBytes("Published ordinary file 中文\n"); var binary = new byte[] { 0, 1, 2, 10, 13, 127, 128, 255, 0 };
            File.WriteAllBytes(Path.Combine(mixed.Repository, "notes.txt"), normal); File.WriteAllBytes(Path.Combine(mixed.Repository, "weights.bin"), binary);
            mixedAssets = [Asset(mixed.Repository, "notes.txt"), Asset(mixed.Repository, "weights.bin")];
            var request = Request(mixed, "publish", "mixed-publication", mixedAssets); published = Run(request);
            Assert(published.Status == "PASS_owned_git_ref_only" && published.PublishedCommit is not null && published.VerifiedFiles == 2 && published.VerifiedBytes == normal.Length + binary.Length && published.LocalFixture && published.ModelQuality == "NOT_ACCEPTED", "Real local publication receipt differs.");
            Assert(Git(mixed.BareRepository, "show", published.PublishedCommit + ":notes.txt").SequenceEqual(normal), "Ordinary committed Git blob bytes differ.");
            var pointer = Git(mixed.BareRepository, "show", published.PublishedCommit + ":weights.bin");
            var parsed = NativeLfsFiles.TryParsePointer(pointer, out var oid, out var size);
            Assert(pointer.SequenceEqual(NativeLfsFiles.CreatePointer(binary.Length, mixedAssets[1].Sha256)) && parsed && oid == mixedAssets[1].Sha256 && size == binary.Length, "Committed LFS pointer is not canonical or bound to the real bytes.");
            var remoteObject = SafePaths.Within(mixed.BareRepository, "lfs/objects/" + oid[..2] + "/" + oid.Substring(2, 2) + "/" + oid, Limit());
            TaskOperations.VerifyFile(remoteObject, binary.Length, oid, Limit()); Assert(File.ReadAllBytes(remoteObject).SequenceEqual(binary), "Local remote LFS object bytes differ.");
            var hookDirectories = Directory.EnumerateDirectories(Path.Combine(request.OutputDirectory, "hooks"), "pre-push-*").Take(3).ToArray();
            Assert(hookDirectories.Length == 1, "Actual native pre-push was not invoked exactly once.");
            using var evidence = File.OpenRead(Path.Combine(hookDirectories[0], "result.json"));
            var hookResult = JsonSerializer.Deserialize(evidence, ExecutionJsonContext.Default.ExecutionResult)!;
            Assert(Path.GetFullPath(hookResult.Executable) == Path.GetFullPath(lfs) && hookResult.ArgumentList.SequenceEqual(new[] { "pre-push", "origin", mixed.Remote }) && hookResult.StandardInputBytes > 0 && hookResult.InputComplete && hookResult.Status == "PASS", "Native hook did not delegate actual Git ref stdin to actual git-lfs.");
            Assert(GitText(mixed.BareRepository, "rev-parse", "refs/heads/master") == published.PublishedCommit, "Remote local ref did not read back independently.");
            if (lfsServer is not null)
            {
                Assert(lfsServer.BatchResponses > 0 && lfsServer.UploadResponses > 0, "Native LFS did not traverse the actual C# basic batch/upload HTTP endpoint.");
                Assert(lfsServer.Errors.Length == 0, "Actual HTTP/LFS fixture protocol error: " + string.Join(" | ", lfsServer.Errors) + "; requests=" + string.Join(" | ", lfsServer.ActualRequests));
            }
            else
            {
                var actualServer = externalServer!.Counters(cancellation.Token);
                Assert(actualServer.BatchResponses > 0 && actualServer.UploadResponses > 0, "Native LFS did not traverse the actual external C# basic batch/upload HTTP endpoint.");
                Assert(actualServer.Errors.Length == 0, "Actual external HTTP/LFS fixture protocol error: " + string.Join(" | ", actualServer.Errors) + "; requests=" + string.Join(" | ", actualServer.ActualRequests));
            }
        }
        check("Native local ordinary and LFS publication stores exact Git bytes and calls native pre-push", PublishedMixed);
        check("Native fresh ordinary clone verifies pinned actual file bytes", () =>
        {
            var fixture = Prepare("ordinary"); var clone = Path.Combine(fixture.Root, "fresh-clone");
            var receipt = Run(Request(fixture, "clone", "ordinary-clone", [fixture.Notes], repository: clone));
            Assert(receipt.VerifiedFiles == 1 && receipt.SourceCommit == fixture.Commit && Hash(Path.Combine(clone, "notes.txt")) == fixture.Notes.Sha256 && receipt.ModelQuality == "NOT_ACCEPTED", "Fresh ordinary checkout identity or actual bytes differ.");
        });
        check("Native fresh LFS clone hydrates and independently verifies actual bytes", () =>
        {
            PublishedMixed();
            Assert(mixed is not null && published?.PublishedCommit is not null, "Mixed real local fixture must precede fresh LFS readback.");
            var clone = Path.Combine(mixed!.Root, "fresh-lfs-clone");
            var receipt = Run(Request(mixed, "clone", "lfs-clone", mixedAssets, published!.PublishedCommit, clone));
            Assert(receipt.VerifiedFiles == 2 && receipt.SourceCommit == published.PublishedCommit && Hash(Path.Combine(clone, "weights.bin")) == mixedAssets[1].Sha256 && new FileInfo(Path.Combine(clone, "weights.bin")).Length == mixedAssets[1].Bytes, "Fresh LFS readback accepted a pointer or wrong actual content.");
            Assert((lfsServer?.DownloadResponses ?? externalServer!.Counters(cancellation.Token).DownloadResponses) > 0, "Fresh LFS clone did not traverse a real basic HTTP GET.");
        });
        check("Native Git isolates inherited directory worktree and index redirection", () =>
        {
            var fixture = Prepare("environment"); var foreign = Path.Combine(fixture.Root, "foreign"); Directory.CreateDirectory(foreign);
            var sentinel = Path.Combine(foreign, "index"); File.WriteAllText(sentinel, "foreign fixture sentinel\n", new UTF8Encoding(false)); var before = File.ReadAllBytes(sentinel);
            var changes = new Dictionary<string, string> { ["GIT_DIR"] = Path.Combine(foreign, "missing.git"), ["GIT_WORK_TREE"] = foreign, ["GIT_INDEX_FILE"] = sentinel, ["GIT_COMMON_DIR"] = Path.Combine(foreign, "missing-common") };
            var previous = changes.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
            try
            {
                foreach (var pair in changes) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                var receipt = Run(Request(fixture, "readback", "environment-readback", [fixture.Notes]));
                Assert(receipt.SourceCommit == fixture.Commit && receipt.VerifiedFiles == 1 && File.ReadAllBytes(sentinel).SequenceEqual(before) && Directory.EnumerateFileSystemEntries(foreign).Take(2).Count() == 1, "Inherited Git ownership redirection affected the fixture or foreign sentinel.");
            }
            finally { foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
        });
        check("Native Git refuses an unknown filter before staging or committing its owned path", () =>
        {
            var fixture = Prepare("unknown-filter", "guarded.txt filter=unreviewed\n", hook: true);
            File.WriteAllText(Path.Combine(fixture.Repository, "guarded.txt"), "inert guarded data\n", new UTF8Encoding(false));
            var request = Request(fixture, "publish", "unknown-filter-rejected", [Asset(fixture.Repository, "guarded.txt")]);
            Reject(() => Run(request));
            Assert(GitText(fixture.Repository, "diff", "--cached", "--name-only") == "" && GitText(fixture.Repository, "rev-parse", "HEAD") == fixture.Commit && GitText(fixture.BareRepository, "rev-parse", "refs/heads/master") == fixture.Commit, "Unknown filter mutated the index or a publication ref.");
        });
        check("Native Git rejects script and additional hook payloads before native calls", () =>
        {
            Own(); var valid = Hook("hook-rejection"); var inert = SafePaths.Within(valid.Path, "inert.ps1", Limit());
            File.WriteAllText(inert, "Inert rejection fixture data. This file is never executed.\n", new UTF8Encoding(false));
            var output = New("script-hook-rejected");
            Servers();
            var remote = externalServer?.GitEndpoint("inert") ?? $"http://127.0.0.1:{lfsServer!.Port}/git/inert.git";
            var endpoint = externalServer?.LfsEndpoint("inert") ?? $"http://127.0.0.1:{lfsServer!.Port}/lfs/inert";
            var request = new GitRequest(git, lfs, "publish", owned, remote, new string('a', 40), output, [], LocalFixture: true, NativeHookDirectory: valid.Path, CommitMessage: "Inert rejection fixture", NativeHookSha256: valid.Sha256, LocalLfsEndpoint: endpoint);
            Reject(() => Run(request)); Assert(!Directory.Exists(output), "Script hook admitted a native call or created operation output.");
            var other = Hook("extra-hook-rejection"); File.Copy(Path.Combine(other.Path, "pre-push"), Path.Combine(other.Path, "pre-commit"), false);
            var otherOutput = New("extra-hook-rejected");
            Reject(() => Run(request with { NativeHookDirectory = other.Path, NativeHookSha256 = other.Sha256, OutputDirectory = otherOutput })); Assert(!Directory.Exists(otherOutput), "Unreviewed additional native hook admitted an operation.");
            var script = New("script-pre-push"); Directory.CreateDirectory(script); File.WriteAllText(Path.Combine(script, "pre-push"), "Inert nonnative pre-push fixture data\n", new UTF8Encoding(false));
            Reject(() => NativeGitHook.VerifyDirectory(script, Hash(Path.Combine(script, "pre-push")), Limit()));
        });
        check("Native missing LFS object fresh clone fails and retains actual process evidence", () =>
        {
            PublishedMixed();
            Assert(mixed is not null && published?.PublishedCommit is not null, "Mixed real local fixture must precede missing-object readback.");
            var asset = mixedAssets[1]; var relative = "lfs/objects/" + asset.Sha256[..2] + "/" + asset.Sha256.Substring(2, 2) + "/" + asset.Sha256;
            var objectPath = SafePaths.Within(mixed!.BareRepository, relative, Limit()); TaskOperations.VerifyFile(objectPath, asset.Bytes, asset.Sha256, Limit());
            var withheld = SafePaths.Within(mixed.BareRepository, relative + ".native-test-withheld", Limit());
            if (Path.Exists(withheld)) throw new IOException("Fresh task-owned withheld object path required.");
            var clone = Path.Combine(mixed.Root, "missing-lfs-clone"); var request = Request(mixed, "clone", "missing-lfs-readback", [asset], published!.PublishedCommit, clone);
            File.Move(objectPath, withheld, false);
            try
            {
                Reject(() => Run(request));
                Assert((lfsServer?.MissingObjects ?? externalServer!.Counters(cancellation.Token).MissingObjects) > 0, "Missing-object negative did not receive a real LFS batch404 response.");
                Assert(Directory.Exists(clone) && Directory.EnumerateDirectories(request.OutputDirectory, "call-*").Take(161).Any() && GitText(mixed.BareRepository, "rev-parse", "refs/heads/master") == published.PublishedCommit, "Failed fresh clone lost evidence or mutated the remote ref.");
                var cloned = Path.Combine(clone, "weights.bin");
                Assert(!File.Exists(cloned) || new FileInfo(cloned).Length != asset.Bytes || Hash(cloned) != asset.Sha256, "Missing-object fresh readback unexpectedly used non-fresh object bytes.");
            }
            finally
            {
                var recovery = new Budget(10, null, CancellationToken.None);
                SafePaths.NoLinks(objectPath, recovery); TaskOperations.VerifyFile(withheld, asset.Bytes, asset.Sha256, recovery);
                File.Move(withheld, objectPath, false); TaskOperations.VerifyFile(objectPath, asset.Bytes, asset.Sha256, recovery);
            }
        });
        }
        finally
        {
            try
            {
                if (lfsServer is not null)
                {
                    lfsServer.Dispose();
                    var completed = lfsServer.BatchResponses + lfsServer.UploadResponses + lfsServer.DownloadResponses;
                    var receipt = new NativeLfsReceipt(lfsServer.Errors.Length != 0 ? "FAIL" : completed == 0 ? "NOT_RUN" : "PASS_protocol_fixture_only",
                        lfsServer.Port, lfsServer.Requests, lfsServer.BatchResponses, lfsServer.UploadResponses, lfsServer.DownloadResponses, lfsServer.MissingObjects,
                        lfsServer.UnsupportedLockResponses, lfsServer.ActualRequests, lfsServer.Errors);
                    TaskOperations.WriteNew(Path.Combine(owned, "native-lfs-server.json"), receipt, NativeLfsJson.Default.NativeLfsReceipt);
                    Assert(lfsServer.Errors.Length == 0, "HTTP/LFS server protocol/handler failure retained in native-lfs-server.json.");
                }
            }
            finally
            {
                try
                {
                    if (gitServer is not null)
                    {
                        foreach (var result in gitServer.Results) VerifyCleanup(result, recovery: true);
                        gitServer.Dispose();
                    }
                }
                finally
                {
                    try
                    {
                        if (externalServer is not null)
                        {
                            try { externalServer.Cancel(Path.Combine(owned, "native-git-external-server.json")); }
                            finally { externalServer.Dispose(); }
                        }
                    }
                    finally
                    {
                        if (Directory.Exists(owned)) TaskOperations.WriteNew(Path.Combine(owned, "case-selection.json"),
                            new NativeGitCaseReceipt(selected == 0 ? null : selected, 8, completedCases, dependencies.ToArray(), calls, operationCalls,
                                externalServer is null ? null : Path.Combine(externalServer.Ready.OwnedRoot, "server-result.json")), NativeGitCaseJson.Default.NativeGitCaseReceipt);
                    }
                }
            }
        }
        // ProcessExecutor owns all launched trees. Frozen fixture bytes and
        // actual network/process receipts remain under this new root.
    }
}

internal sealed record NativeGitCaseReceipt(int? SelectedCase, int DeclaredCases, int CompletedCases, string[] FreshDependencies, int SetupAndBackendCalls, int OperationCalls, string? SeparateServerReceipt);
[JsonSerializable(typeof(NativeGitCaseReceipt))]
internal partial class NativeGitCaseJson : JsonSerializerContext;
