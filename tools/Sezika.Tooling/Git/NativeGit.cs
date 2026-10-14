using System.Text;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record GitAsset(string Path, long Bytes, string Sha256);
internal sealed record GitRequest(string GitExecutable, string LfsExecutable, string Operation, string RepositoryDirectory, string Remote, string ExpectedCommit, string OutputDirectory, GitAsset[] Files, bool LocalFixture = false, string? CredentialTarget = null, string CredentialEncoding = "utf8", string? BrokerExecutable = null, string? NativeHookDirectory = null, string? CommitMessage = null, string? NativeHookSha256 = null, string Branch = "master", string? AuthorName = null, string? AuthorEmail = null, string? LocalLfsEndpoint = null);
internal sealed record GitReceipt(string Status, string Operation, string SourceCommit, string? PublishedCommit, int NativeCalls, int VerifiedFiles, long VerifiedBytes, bool LocalFixture, string IndependentReadback, string ModelQuality = "NOT_ACCEPTED",
    string TrackedState = "NOT_REPORTED", int HydratedLfsFiles = 0, int VerifiedDownloadedLfsObjects = 0);

internal static class NativeGit
{
    internal static async Task<GitReceipt> RunAsync(GitRequest request, Budget budget, CancellationToken cancellation)
    {
        ProcessExecutor.ValidateExecutable(request.GitExecutable, budget);
        ProcessExecutor.ValidateExecutable(request.LfsExecutable, budget);
        TaskOperations.Commit(request.ExpectedCommit);
        if (SafePaths.Relative(request.Branch) != request.Branch || request.Branch.Length > 256) throw new InvalidDataException("Explicit portable publication branch required.");
        if (request.Operation is not ("clone" or "readback" or "publish") || request.Files.Length > 1000) throw new InvalidDataException("Git operation or asset bound1000 differs.");
        var repository = Path.GetFullPath(request.RepositoryDirectory); SafePaths.NoLinks(repository, budget);
        var output = Path.GetFullPath(request.OutputDirectory); SafePaths.NoLinks(output, budget);
        if (Path.Exists(output)) throw new IOException("Git task output exists; preserve it.");
        ValidateRemote(request.Remote, request.LocalFixture);
        if (request.Operation == "publish" && string.IsNullOrWhiteSpace(request.CommitMessage)) throw new InvalidDataException("Explicit source publication message required.");
        if (request.Operation == "publish" && request.NativeHookDirectory is null) throw new InvalidDataException("Verified native LFS pre-push hook required before publication.");
        if (request.NativeHookDirectory is not null) NativeGitHook.VerifyDirectory(request.NativeHookDirectory, request.NativeHookSha256 ?? "", budget);
        if (request.LocalFixture && request.CredentialTarget is not null) throw new InvalidDataException("Fixtures never read live credentials.");
        if (request.LocalFixture)
        {
            if (!Uri.TryCreate(request.LocalLfsEndpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1" || endpoint.Port is < 1024 or > 65535 || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.AbsolutePath.Length < 2 || SafePaths.Relative(endpoint.AbsolutePath.Trim('/')) != endpoint.AbsolutePath.Trim('/')) throw new InvalidDataException("Explicit C# basic-protocol loopback fixture LFS endpoint required.");
        }
        else if (request.LocalLfsEndpoint is not null) throw new InvalidDataException("Local LFS fixture endpoint cannot be used by platform transport.");
        Directory.CreateDirectory(output);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_LFS_SKIP_SMUDGE"] = "1", ["GIT_TRACE"] = null,
            ["GIT_TRACE_CURL"] = null, ["GIT_CURL_VERBOSE"] = null, ["GIT_TRACE_PACKET"] = null,
            ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null"
        };
        foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_CEILING_DIRECTORIES", "GIT_PREFIX", "GIT_CONFIG", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT", "GIT_EXEC_PATH", "GIT_SSH", "GIT_SSH_COMMAND", "GIT_PROXY_COMMAND", "GIT_ASKPASS", "SSH_ASKPASS", "GIT_EDITOR", "GIT_SEQUENCE_EDITOR", "GIT_TEMPLATE_DIR" }) environment[name] = null;
        if (request.LocalFixture)
        {
            foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" }) environment[name] = null;
            environment["NO_PROXY"] = "127.0.0.1";
            environment["no_proxy"] = "127.0.0.1";
        }
        var safeConfigurations = new List<(string Key, string Value)>
        {
            ("credential.helper", ""), ("lfs.standalonetransferagent", ""), ("lfs.basictransfersonly", "true"),
            ("lfs.concurrenttransfers", "1"), ("lfs.transfer.maxretries", "0"), ("http.lowSpeedLimit", "1024"), ("http.lowSpeedTime", "30"),
            ("core.fsmonitor", "false"), ("core.autocrlf", "false"), ("commit.gpgSign", "false"), ("tag.gpgSign", "false"), ("gc.auto", "0"), ("maintenance.auto", "false")
        };
        safeConfigurations.Add(("lfs.storage", Path.Combine(repository, ".git", "lfs")));
        var emptyHooks = Path.Combine(output, "read-only-hooks"); Directory.CreateDirectory(emptyHooks);
        safeConfigurations.Add(("core.hooksPath", request.NativeHookDirectory ?? emptyHooks));
        // Git filters stay disabled for both Git and direct Git-LFS. Native
        // fetch stores bytes; C# independently validates and hydrates pointers.
        safeConfigurations.Add(("filter.lfs.process", ""));
        safeConfigurations.Add(("filter.lfs.clean", ""));
        safeConfigurations.Add(("filter.lfs.smudge", ""));
        safeConfigurations.Add(("filter.lfs.required", "false"));
        if (request.LocalFixture)
        {
            safeConfigurations.Add(("lfs.url", request.LocalLfsEndpoint!));
            safeConfigurations.Add(("http.proxy", ""));
        }
        if (request.NativeHookDirectory is not null)
        {
            var hookLogs = Path.Combine(output, "hooks"); Directory.CreateDirectory(hookLogs);
            environment["SEZIKA_NATIVE_GIT_LFS"] = request.LfsExecutable;
            environment["SEZIKA_NATIVE_HOOK_LOG_ROOT"] = hookLogs;
            environment["SEZIKA_NATIVE_GIT_FIXTURE"] = request.LocalFixture ? "1" : "0";
            environment["SEZIKA_NATIVE_GIT_BROKER"] = request.BrokerExecutable;
        }
        using var credential = request.CredentialTarget is null ? null : WindowsCredentialStore.Read(request.CredentialTarget);
        if (credential is not null)
        {
            var secret = credential.Text(request.CredentialEncoding);
            if (secret.Length is < 1 or > 4096 || secret.Any(char.IsControl) || credential.UserName.Any(char.IsControl)) throw new InvalidDataException("Credential HTTP header bounds differ.");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.UserName + ":" + secret));
            // Native Git accepts scoped config in child memory. Credentials are
            // absent from argv, written config, receipts and stdin/stdout logs.
            safeConfigurations.Add(("http." + request.Remote.TrimEnd('/') + "/.extraHeader", "Authorization: Basic " + basic));
        }
        environment["GIT_CONFIG_COUNT"] = safeConfigurations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0; index < safeConfigurations.Count; index++)
        {
            environment["GIT_CONFIG_KEY_" + index] = safeConfigurations[index].Key;
            environment["GIT_CONFIG_VALUE_" + index] = safeConfigurations[index].Value;
        }
        var calls = 0;
        async Task<byte[]> CallRaw(string[] arguments, bool lfs = false, byte[]? standardInput = null, int maximumOutputBytes = 4 * 1024 * 1024)
        {
            budget.Check();
            if (++calls > 160) throw new InvalidDataException("Native Git call bound160.");
            var configured = lfs ? arguments : Configure(arguments, request.NativeHookDirectory);
            var execution = await ProcessExecutor.ExecuteAsync(new ExecutionRequest
            {
                Executable = lfs ? request.LfsExecutable : request.GitExecutable, ArgumentList = configured,
                WorkingDirectory = request.Operation == "clone" && !Directory.Exists(repository) ? Path.GetDirectoryName(repository)! : repository,
                OutputDirectory = Path.Combine(output, "call-" + calls.ToString("D2")), Environment = environment,
                StandardInput = standardInput ?? [],
                TimeoutSeconds = Math.Max(1, (int)Math.Min(300, budget.RemainingSeconds)), MaximumOutputBytes = maximumOutputBytes,
                BrokerExecutable = request.BrokerExecutable
            }, budget, cancellation).ConfigureAwait(false);
            if (execution.Status != "PASS" || execution.ExitCode != 0) throw new IOException("Native Git operation failed; immutable process receipt retained.");
            return await File.ReadAllBytesAsync(Path.Combine(execution.OutputDirectory, "stdout.bin"), cancellation).ConfigureAwait(false);
        }
        async Task<string> Call(string[] arguments, bool lfs = false, byte[]? standardInput = null) =>
            Encoding.UTF8.GetString(await CallRaw(arguments, lfs, standardInput).ConfigureAwait(false)).Trim();
        try
        {
            if (request.Operation == "clone")
            {
                if (Path.Exists(repository) || !Directory.Exists(Path.GetDirectoryName(repository))) throw new IOException("Fresh clone parent required.");
                _ = await Call(["clone", "--no-checkout", "--origin", "origin", "--", request.Remote, repository]).ConfigureAwait(false);
                _ = await Call(["-C", repository, "checkout", "--detach", request.ExpectedCommit]).ConfigureAwait(false);
                var head = await Call(["-C", repository, "rev-parse", "HEAD"]).ConfigureAwait(false);
                if (head != request.ExpectedCommit) throw new InvalidDataException("Pinned clone commit differs.");
            }
            else
            {
                if (!Directory.Exists(repository)) throw new DirectoryNotFoundException("Repository missing.");
                var remote = await Call(["remote", "get-url", "origin"]).ConfigureAwait(false);
                if (remote != request.Remote) throw new InvalidDataException("Actual repository remote differs.");
            }
            var top = Path.GetFullPath(await Call(["rev-parse", "--show-toplevel"]).ConfigureAwait(false));
            if (!top.Equals(repository, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Actual Git worktree does not match the explicit owned repository.");
            var gitDirectory = Path.GetFullPath(await Call(["rev-parse", "--absolute-git-dir"]).ConfigureAwait(false));
            if (!gitDirectory.Equals(Path.Combine(repository, ".git"), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Alternate Git metadata/index is not admitted in this transport.");
            var current = await Call(["rev-parse", "HEAD"]).ConfigureAwait(false);
            TaskOperations.Commit(current);
            if (request.Operation != "publish" && current != request.ExpectedCommit) throw new InvalidDataException("Readback pinned commit differs before download/hydration.");
            if (request.Operation == "clone")
            {
                foreach (var group in request.Files.Chunk(16))
                {
                    budget.Check(); var include = string.Join(',', group.Select(file => SafePaths.Relative(file.Path)));
                    if (include.Length > 20000) throw new InvalidDataException("LFS include bound.");
                    _ = await Call(["fetch", "origin", request.ExpectedCommit, "--include=" + include, "--exclude="], true).ConfigureAwait(false);
                }
            }
            long bytes = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var verifiedLfsPaths = new HashSet<string>(StringComparer.Ordinal); var hydrated = 0;
            foreach (var file in request.Files)
            {
                budget.Check();
                if (!seen.Add(SafePaths.Relative(file.Path))) throw new InvalidDataException("Duplicate owned Git path.");
                if (request.Operation != "publish")
                {
                    var attributes = (await Call(["check-attr", "--cached", "-z", "filter", "--", file.Path]).ConfigureAwait(false)).Split('\0');
                    if (attributes.Length < 3 || attributes[0] != file.Path || attributes[1] != "filter") throw new InvalidDataException("Readback cached filter identity differs.");
                    if (attributes[2] == "lfs")
                    {
                        var pointer = await CallRaw(["cat-file", "blob", request.ExpectedCommit + ":" + file.Path], maximumOutputBytes: NativeLfsFiles.MaximumPointerBytes).ConfigureAwait(false);
                        var indexPointer = await CallRaw(["cat-file", "blob", ":" + file.Path], maximumOutputBytes: NativeLfsFiles.MaximumPointerBytes).ConfigureAwait(false);
                        if (!NativeLfsFiles.TryParsePointer(pointer, out var oid, out var size) || oid != file.Sha256 || size != file.Bytes ||
                            !pointer.SequenceEqual(NativeLfsFiles.CreatePointer(file.Bytes, file.Sha256)) || !indexPointer.SequenceEqual(pointer))
                            throw new InvalidDataException("Readback committed/index LFS pointer differs from the pinned actual-byte manifest.");
                        if (request.Operation == "clone")
                        {
                            _ = NativeLfsFiles.HydrateFreshPointer(repository, gitDirectory, file.Path, pointer, file.Bytes, file.Sha256, budget); hydrated++;
                        }
                        verifiedLfsPaths.Add(file.Path);
                    }
                    else if (attributes[2] is not ("unspecified" or "unset")) throw new InvalidDataException("Unreviewed readback filter is not admitted.");
                }
                TaskOperations.VerifyFile(SafePaths.Within(repository, file.Path, budget), file.Bytes, file.Sha256, budget);
                bytes = checked(bytes + file.Bytes);
                if (bytes > 137438953472) throw new InvalidDataException("Git asset bytes exceed128GiB.");
            }
            if (request.Operation != "publish")
            {
                var nativeStatus = Encoding.UTF8.GetString(await CallRaw(["status", "--porcelain", "-z", "--untracked-files=no"]).ConfigureAwait(false));
                foreach (var change in nativeStatus.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    budget.Check();
                    if (!change.StartsWith(" M ", StringComparison.Ordinal) || !verifiedLfsPaths.Contains(change[3..]))
                        throw new InvalidDataException("Readback native tracked change is outside exact committed/index-bound verified LFS raw bytes: " + change);
                }
                if (await Call(["rev-parse", "HEAD"]).ConfigureAwait(false) != request.ExpectedCommit) throw new InvalidDataException("Readback pinned ref changed during hydration.");
                return new("PASS_pinned_checkout_actual_file_sha_only", request.Operation, current, null, calls, request.Files.Length, bytes, request.LocalFixture,
                    hydrated > 0 ? "PINNED_FRESH_NATIVE_LFS_FETCH_AND_CSHARP_HYDRATION" : "PINNED_LOCAL_CHECKOUT_ONLY",
                    TrackedState: nativeStatus.Length == 0 ? "NATIVE_CLEAN" : "NATIVE_MODIFIED_ONLY_EXACT_VERIFIED_LFS_RAW_BYTES",
                    HydratedLfsFiles: hydrated, VerifiedDownloadedLfsObjects: hydrated);
            }
            var remoteRef = await Call(["ls-remote", "--exit-code", "origin", "refs/heads/" + request.Branch]).ConfigureAwait(false);
            if (!remoteRef.StartsWith(request.ExpectedCommit + "\t", StringComparison.Ordinal) || current != request.ExpectedCommit) throw new InvalidDataException("Expected local and remote publication refs differ.");
            var index = await Call(["diff", "--cached", "--name-only"]).ConfigureAwait(false);
            if (index.Length != 0) throw new InvalidDataException("Publication index is not empty; preserve unrelated work.");
            if (request.Files.Length == 0 || request.CommitMessage!.Length > 512 || request.CommitMessage.Any(char.IsControl)) throw new InvalidDataException("Publication payload/message bounds differ.");
            foreach (var file in request.Files)
            {
                budget.Check();
                var attributes = (await Call(["check-attr", "-z", "filter", "--", file.Path]).ConfigureAwait(false)).Split('\0');
                if (attributes.Length < 3 || attributes[0] != file.Path || attributes[1] != "filter") throw new InvalidDataException("Owned filter identity differs.");
                if (attributes[2] == "lfs")
                {
                    _ = NativeLfsFiles.StoreObject(gitDirectory, SafePaths.Within(repository, file.Path, budget), file.Bytes, file.Sha256, budget);
                    var pointer = NativeLfsFiles.CreatePointer(file.Bytes, file.Sha256);
                    var objectId = await Call(["hash-object", "-w", "--stdin"], standardInput: pointer).ConfigureAwait(false);
                    TaskOperations.Commit(objectId);
                    _ = await Call(["update-index", "--add", "--cacheinfo", "100644," + objectId + "," + file.Path]).ConfigureAwait(false);
                }
                else if (attributes[2] is "unspecified" or "unset") _ = await Call(["add", "--", file.Path]).ConfigureAwait(false);
                else throw new InvalidDataException("Unreviewed local Git filter is not admitted.");
            }
            var staged = (await Call(["diff", "--cached", "--name-only"]).ConfigureAwait(false)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (staged.Any(p => !seen.Contains(p))) throw new InvalidDataException("Publication staged ownership differs.");
            if (staged.Length == 0) return new("PASS_no_changed_owned_files_ref_verified", request.Operation, current, current, calls, request.Files.Length, bytes, request.LocalFixture, "FULL_FRESH_CONTENT_READBACK_NOT_RUN");
            if (string.IsNullOrWhiteSpace(request.AuthorName) || string.IsNullOrWhiteSpace(request.AuthorEmail) || request.AuthorName.Length > 128 || request.AuthorEmail.Length > 256 || request.AuthorName.Any(char.IsControl) || request.AuthorEmail.Any(char.IsControl)) throw new InvalidDataException("Explicit bounded commit author required.");
            _ = await Call(["-c", "user.name=" + request.AuthorName, "-c", "user.email=" + request.AuthorEmail, "commit", "-m", request.CommitMessage!]).ConfigureAwait(false);
            var published = await Call(["rev-parse", "HEAD"]).ConfigureAwait(false); TaskOperations.Commit(published);
            // No automatic push retry: an ambiguous network write must be read
            // back before a new invocation is authorized to mutate the ref.
            _ = await Call(["push", "origin", "HEAD:refs/heads/" + request.Branch]).ConfigureAwait(false);
            var readback = await Call(["ls-remote", "--exit-code", "origin", "refs/heads/" + request.Branch]).ConfigureAwait(false);
            if (!readback.StartsWith(published + "\t", StringComparison.Ordinal)) throw new InvalidDataException("Published remote ref readback differs.");
            return new("PASS_owned_git_ref_only", request.Operation, current, published, calls, request.Files.Length, bytes, request.LocalFixture, "FULL_FRESH_CONTENT_READBACK_NOT_RUN");
        }
        finally
        {
            for (var index = 0; index < safeConfigurations.Count; index++) { environment.Remove("GIT_CONFIG_VALUE_" + index); environment.Remove("GIT_CONFIG_KEY_" + index); }
            environment.Remove("GIT_CONFIG_COUNT");
        }
    }

    private static string[] Configure(string[] arguments, string? hook)
    {
        var prefix = new List<string> { "-c", "credential.helper=", "-c", "lfs.standalonetransferagent=", "-c", "lfs.basictransfersonly=true", "-c", "lfs.concurrenttransfers=1", "-c", "lfs.transfer.maxretries=0", "-c", "http.lowSpeedLimit=1024", "-c", "http.lowSpeedTime=30" };
        if (hook is not null)
        {
            var path = Path.GetFullPath(hook);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Native hook directory missing.");
            prefix.Add("-c"); prefix.Add("core.hooksPath=" + path);
        }
        return prefix.Concat(arguments).ToArray();
    }

    internal static void ValidateRemote(string remote, bool fixture)
    {
        if (fixture)
        {
            if (!Uri.TryCreate(remote, UriKind.Absolute, out var local) || local.Scheme is not ("git" or "http") || local.Host != "127.0.0.1" || local.Port is < 1024 or > 65535 || local.UserInfo.Length != 0 || local.Query.Length != 0 || local.Fragment.Length != 0 || local.AbsolutePath.Length < 2 || SafePaths.Relative(local.AbsolutePath.Trim('/')) != local.AbsolutePath.Trim('/')) throw new InvalidDataException("Explicit native Git loopback fixture URI required.");
            return;
        }
        if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.Host is not ("moark.com" or "huggingface.co" or "github.com") || SafePaths.Relative(uri.AbsolutePath.Trim('/')) != uri.AbsolutePath.Trim('/')) throw new InvalidDataException("Repository URI identity/scope differs.");
        var components = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length != 2 && !(uri.Host == "huggingface.co" && components.Length == 3 && components[0] == "datasets")) throw new InvalidDataException("Repository path scope differs.");
    }
}

[JsonSerializable(typeof(GitRequest))]
[JsonSerializable(typeof(GitReceipt))]
[JsonSourceGenerationOptions(WriteIndented = true, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class GitJsonContext : JsonSerializerContext;
