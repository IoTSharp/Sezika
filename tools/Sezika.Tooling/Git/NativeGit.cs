using System.Text;
using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record GitAsset(string Path, long Bytes, string Sha256);
internal sealed record GitRequest(string GitExecutable, string LfsExecutable, string Operation, string RepositoryDirectory, string Remote, string ExpectedCommit, string OutputDirectory, GitAsset[] Files, bool LocalFixture = false, string? CredentialTarget = null, string CredentialEncoding = "utf8", string? BrokerExecutable = null, string? NativeHookDirectory = null, string? CommitMessage = null, string? NativeHookSha256 = null, string Branch = "master", string? AuthorName = null, string? AuthorEmail = null, string? LocalLfsEndpoint = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GitAttributeAppend? AttributeChange { get; init; }
    public string CredentialSource { get; init; } = "auto";
    public string AuthenticationProvider { get; init; } = "moark";
}
internal sealed record GitAttributeAppend(string BeforeSha256, string AfterSha256, string[] LfsPaths, string[] VerbatimPaths);
internal sealed record GitReceipt(string Status, string Operation, string SourceCommit, string? PublishedCommit, int NativeCalls, int VerifiedFiles, long VerifiedBytes, bool LocalFixture, string IndependentReadback, string ModelQuality = "NOT_ACCEPTED",
    string TrackedState = "NOT_REPORTED", int HydratedLfsFiles = 0, int VerifiedDownloadedLfsObjects = 0)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LocalCommit { get; init; }
}
internal sealed record GitLocalState(string Status, string? Head, string? RemoteRef, string? IndexSha256,
    string? AttributesSha256, string? LocalCommit = null, string? ErrorType = null);
internal sealed record GitNativeCallPlan(string Operation, int Files, int PathQueryChunks, int StagedQueryChunks, int MaximumNativeCalls, int NativeCallLimit = 160);

internal static class NativeGit
{
    internal static async Task<GitReceipt> RunAsync(GitRequest request, Budget budget, CancellationToken cancellation)
        => await RunAsync(request, budget, cancellation, null).ConfigureAwait(false);

    internal static async Task<GitReceipt> RunAsync(GitRequest request, Budget budget, CancellationToken cancellation, Stream? credentialInput)
    {
        ProcessExecutor.ValidateExecutable(request.GitExecutable, budget);
        ProcessExecutor.ValidateExecutable(request.LfsExecutable, budget);
        TaskOperations.Commit(request.ExpectedCommit);
        if (SafePaths.Relative(request.Branch) != request.Branch || request.Branch.Length > 256) throw new InvalidDataException("Explicit portable publication branch required.");
        if (request.Operation is not ("clone" or "readback" or "publish" or "preview" or "commit-only") || request.Files.Length > 1000) throw new InvalidDataException("Git operation or asset bound1000 differs.");
        var localOnly = request.Operation is "preview" or "commit-only";
        var nativePlan = PlanNativeCalls(request, budget);
        if (nativePlan.MaximumNativeCalls > 160) throw new InvalidDataException("Predicted native Git budget exceeds160 before repository mutation.");
        if (!localOnly && request.AttributeChange is not null) throw new InvalidDataException("Attribute append is an explicit preview/commit-only contract.");
        var repository = Path.GetFullPath(request.RepositoryDirectory); SafePaths.NoLinks(repository, budget);
        var output = Path.GetFullPath(request.OutputDirectory); SafePaths.NoLinks(output, budget);
        if (Path.Exists(output)) throw new IOException("Git task output exists; preserve it.");
        if (localOnly && (output.Equals(repository, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            output.StartsWith(Path.TrimEndingDirectorySeparator(repository) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            throw new InvalidDataException("Local planning output must be outside the repository.");
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
        TaskOperations.WriteNew(Path.Combine(output, "native-call-plan.json"), nativePlan, GitJsonContext.Default.GitNativeCallPlan);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_LFS_SKIP_SMUDGE"] = "1", ["GIT_TRACE"] = null,
            ["GIT_TRACE_CURL"] = null, ["GIT_CURL_VERBOSE"] = null, ["GIT_TRACE_PACKET"] = null,
            ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null"
        };
        if (localOnly) environment["GIT_OPTIONAL_LOCKS"] = "0";
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
        var credentialSource = request.CredentialSource == "auto" ? request.CredentialTarget is null ? "none" : "credential-manager" : request.CredentialSource;
        if (credentialSource is not ("none" or "credential-manager" or "stdin") || credentialInput is not null && credentialSource != "stdin" ||
            credentialSource != "credential-manager" && request.CredentialTarget is not null || credentialSource == "credential-manager" && request.CredentialTarget is null)
            throw new InvalidDataException("Conflicting or unknown Git authentication source.");
        if (credentialSource == "credential-manager" && request.AuthenticationProvider != "moark") throw new InvalidDataException("Stored Git credentials retain the existing Basic contract; alternate providers require explicit stdin.");
        using var credential = credentialSource == "credential-manager" ? WindowsCredentialStore.Read(request.CredentialTarget!) : null;
        AuthenticationHeaderValue? authentication = null;
        if (credential is not null)
        {
            var secret = credential.Text(request.CredentialEncoding);
            if (secret.Length is < 1 or > 4096 || secret.Any(char.IsControl) || credential.UserName.Any(char.IsControl)) throw new InvalidDataException("Credential HTTP header bounds differ.");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.UserName + ":" + secret));
            // Native Git accepts scoped config in child memory. Credentials are
            // absent from argv, written config, receipts and stdin/stdout logs.
            authentication = new("Basic", basic);
        }
        else if (credentialSource == "stdin") authentication = HttpCredentials.Read("stdin", null, request.CredentialEncoding, request.AuthenticationProvider, budget, credentialInput);
        if (authentication is not null)
        {
            safeConfigurations.Add(("http.extraHeader", ""));
            safeConfigurations.Add(("http." + request.Remote.TrimEnd('/') + "/.extraHeader", ""));
            safeConfigurations.Add(("http." + request.Remote.TrimEnd('/') + "/.extraHeader", "Authorization: " + authentication));
            safeConfigurations.Add(("http." + request.Remote.TrimEnd('/') + "/.followRedirects", "false"));
            if (request.LocalFixture)
            {
                var service = new Uri(request.LocalLfsEndpoint!); var origin = new Uri(request.Remote);
                if (service.Scheme != origin.Scheme || service.Authority != origin.Authority) throw new InvalidDataException("Authenticated fixture LFS service must share the admitted Git origin.");
                safeConfigurations.Add(("http." + request.LocalLfsEndpoint!.TrimEnd('/') + "/.extraHeader", ""));
                safeConfigurations.Add(("http." + request.LocalLfsEndpoint.TrimEnd('/') + "/.extraHeader", "Authorization: " + authentication));
            }
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
                MaximumInputBytes = Math.Max(1_048_576, standardInput?.Length ?? 0),
                // Windows LFS also owns native Git wrappers and console hosts;
                // its cumulative identity budget is separate from160 Git calls.
                MaximumProcesses = lfs && OperatingSystem.IsWindows() ? 128 : 64,
                TimeoutSeconds = Math.Max(1, (int)Math.Min(300, budget.RemainingSeconds)), MaximumOutputBytes = maximumOutputBytes,
                BrokerExecutable = request.BrokerExecutable
            }, budget, cancellation).ConfigureAwait(false);
            if (execution.Status != "PASS" || execution.ExitCode != 0) throw new IOException("Native Git operation failed; immutable process receipt retained.");
            return await File.ReadAllBytesAsync(Path.Combine(execution.OutputDirectory, "stdout.bin"), cancellation).ConfigureAwait(false);
        }
        async Task<string> Call(string[] arguments, bool lfs = false, byte[]? standardInput = null) =>
            Encoding.UTF8.GetString(await CallRaw(arguments, lfs, standardInput).ConfigureAwait(false)).Trim();
        async Task<Dictionary<string, string>> Filters(string[] paths, bool cached = false)
        {
            if (paths.Length == 0) return new(StringComparer.Ordinal);
            var input = Encoding.UTF8.GetBytes(string.Join('\0', paths) + '\0');
            string[] arguments = cached ? ["check-attr", "--cached", "-z", "--stdin", "filter"] : ["check-attr", "-z", "--stdin", "filter"];
            var values = Encoding.UTF8.GetString(await CallRaw(arguments, standardInput: input).ConfigureAwait(false)).Split('\0');
            if (values.Length != paths.Length * 3 + 1 || values[^1].Length != 0) throw new InvalidDataException("Batched attribute response count differs.");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < paths.Length; index++)
            {
                budget.Check(); var offset = index * 3;
                if (values[offset] != paths[index] || values[offset + 1] != "filter" || values[offset + 2] is not ("lfs" or "unspecified" or "unset") || !result.TryAdd(paths[index], values[offset + 2]))
                    throw new InvalidDataException("Batched owned filter identity differs.");
            }
            return result;
        }
        async Task<Dictionary<string, (string Mode, string Oid)>> Entries(string[] paths, string? tree = null)
        {
            var result = new Dictionary<string, (string Mode, string Oid)>(StringComparer.Ordinal);
            foreach (var group in PathChunks(paths, budget))
            {
                budget.Check();
                string[] prefix = tree is null ? ["ls-files", "--stage", "-z", "--"] : ["ls-tree", "-z", tree, "--"];
                var rows = Encoding.UTF8.GetString(await CallRaw(prefix.Concat(group).ToArray()).ConfigureAwait(false)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (rows.Length > group.Length) throw new InvalidDataException("Batched Git entry count differs.");
                var selected = group.ToHashSet(StringComparer.Ordinal);
                foreach (var row in rows)
                {
                    budget.Check(); var tab = row.IndexOf('\t'); string[] fields = tab < 0 ? [] : row[..tab].Split(' ');
                    var name = tab < 0 ? "" : row[(tab + 1)..];
                    if (fields.Length != 3 || !selected.Contains(name) || fields[0] is not ("100644" or "100755") ||
                        (tree is null ? fields[2] != "0" : fields[1] != "blob")) throw new InvalidDataException("Batched Git mode/stage/type differs.");
                    var oid = tree is null ? fields[1] : fields[2]; TaskOperations.Commit(oid);
                    if (!result.TryAdd(name, (fields[0], oid))) throw new InvalidDataException("Duplicate batched Git path.");
                }
            }
            return result;
        }
        async Task<byte[][]> Blobs(string[] ids)
        {
            if (ids.Length == 0) return [];
            if (ids.Length > 2000) throw new InvalidDataException("Pointer batch object bound2000.");
            foreach (var id in ids) { budget.Check(); TaskOperations.Commit(id); }
            var outputBytes = await CallRaw(["cat-file", "--batch"], standardInput: Encoding.ASCII.GetBytes(string.Join('\n', ids) + '\n')).ConfigureAwait(false);
            using var stream = new MemoryStream(outputBytes, false); var blobs = new byte[ids.Length][];
            for (var index = 0; index < ids.Length; index++)
            {
                budget.Check(); var header = new List<byte>(); var ended = false;
                for (var character = 0; character < 128; character++) { var value = stream.ReadByte(); if (value < 0) break; if (value == 10) { ended = true; break; } header.Add((byte)value); }
                var fields = Encoding.ASCII.GetString(header.ToArray()).Split(' ');
                if (!ended || fields.Length != 3 || fields[0] != ids[index] || fields[1] != "blob" || !int.TryParse(fields[2], out var size) || size is < 1 or > NativeLfsFiles.MaximumPointerBytes)
                    throw new InvalidDataException("Pointer batch header/byte bound differs.");
                var valueBytes = new byte[size]; stream.ReadExactly(valueBytes);
                if (stream.ReadByte() != 10) throw new InvalidDataException("Pointer batch frame terminator differs."); blobs[index] = valueBytes;
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Pointer batch trailing bytes."); return blobs;
        }
        async Task<List<(string Path, string ObjectId, string Mode)>> HashAndIndex(List<(string Path, string Mode, string Source)> inputs)
        {
            if (inputs.Count is < 1 or > 1001) throw new InvalidDataException("Batched object input bound1001.");
            var names = new StringBuilder();
            foreach (var input in inputs)
            {
                budget.Check(); RegularFiles.Require(input.Source, budget);
                var full = Path.GetFullPath(input.Source).Replace('\\', '/');
                if (full.Any(char.IsControl)) throw new InvalidDataException("Hash input path contains control characters.");
                names.Append('"').Append(full.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"\n");
            }
            var data = Encoding.UTF8.GetBytes(names.ToString()); if (data.Length > 16_777_216) throw new InvalidDataException("Hash pathname input bound16MiB.");
            var ids = Encoding.ASCII.GetString(await CallRaw(["hash-object", "-w", "--no-filters", "--stdin-paths"], standardInput: data).ConfigureAwait(false)).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length != inputs.Count) throw new InvalidDataException("Batched object hash count differs.");
            var indexInfo = new StringBuilder(); var result = new List<(string Path, string ObjectId, string Mode)>();
            for (var index = 0; index < inputs.Count; index++)
            {
                budget.Check(); TaskOperations.Commit(ids[index]); var input = inputs[index];
                indexInfo.Append(input.Mode).Append(' ').Append(ids[index]).Append('\t').Append(input.Path).Append('\0'); result.Add((input.Path, ids[index], input.Mode));
            }
            await CallRaw(["update-index", "-z", "--index-info"], standardInput: Encoding.UTF8.GetBytes(indexInfo.ToString())).ConfigureAwait(false);
            return result;
        }
        async Task<GitReceipt> RunLocalAsync(string gitDirectory, string current)
        {
            if (request.Files.Length == 0) throw new InvalidDataException("Local plan requires1..1000 owned files.");
            if (request.Operation == "commit-only" && (string.IsNullOrWhiteSpace(request.CommitMessage) || request.CommitMessage.Length > 512 || request.CommitMessage.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(request.AuthorName) || request.AuthorName.Length > 128 || request.AuthorName.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(request.AuthorEmail) || request.AuthorEmail.Length > 256 || request.AuthorEmail.Any(char.IsControl)))
                throw new InvalidDataException("Explicit bounded local commit message and author required.");
            var attributesPath = Path.Combine(repository, ".gitattributes");
            var indexPath = Path.Combine(gitDirectory, "index");
            async Task<string> RemoteRef()
            {
                var value = await Call(["ls-remote", "--exit-code", "origin", "refs/heads/" + request.Branch]).ConfigureAwait(false);
                if (value != request.ExpectedCommit + "\trefs/heads/" + request.Branch) throw new InvalidDataException("Exact remote parent ref differs; no automatic write retry.");
                return value;
            }
            async Task<GitLocalState> State(string status, string? localCommit = null) => new(status,
                await Call(["rev-parse", "HEAD"]).ConfigureAwait(false), await RemoteRef().ConfigureAwait(false),
                AssetTransport.FileHash(indexPath, budget), Path.Exists(attributesPath) ? AssetTransport.FileHash(attributesPath, budget) : null, localCommit);
            static bool SameState(GitLocalState first, GitLocalState second) => first.Head == second.Head && first.RemoteRef == second.RemoteRef &&
                first.IndexSha256 == second.IndexSha256 && first.AttributesSha256 == second.AttributesSha256;
            var before = await State("BEFORE_LOCAL_PLAN").ConfigureAwait(false);
            TaskOperations.WriteNew(Path.Combine(output, "local-before.json"), before, GitJsonContext.Default.GitLocalState);
            if (current != request.ExpectedCommit || before.Head != current ||
                (await CallRaw(["diff", "--cached", "--name-only", "-z"]).ConfigureAwait(false)).Length != 0)
                throw new InvalidDataException("Local commit parent or clean index differs; preserve unrelated work.");
            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in request.Files)
            {
                budget.Check(); var name = SafePaths.Relative(file.Path);
                if (name != file.Path || name.Length is < 1 or > 512 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '/' or '-')) || name.Split('/').Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase)) ||
                    name.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase) || !owned.Add(name))
                    throw new InvalidDataException("Local owned path is duplicate, reserved or malformed.");
                TaskOperations.VerifyFile(SafePaths.Within(repository, name, budget), file.Bytes, file.Sha256, budget);
            }
            byte[]? attributeBytes = null;
            if (request.AttributeChange is { } change)
            {
                var informationAttributes = Path.Combine(gitDirectory, "info", "attributes");
                SafePaths.NoLinks(informationAttributes, budget);
                if (Path.Exists(informationAttributes)) throw new InvalidDataException("Root attribute append does not admit Git info/attributes overrides.");
                RegularFiles.Require(attributesPath, budget);
                if (new FileInfo(attributesPath).Length > 1_048_576) throw new InvalidDataException("Attribute byte cap1MiB.");
                var original = await File.ReadAllBytesAsync(attributesPath, cancellation).ConfigureAwait(false);
                var headAttributes = await CallRaw(["show", "HEAD:.gitattributes"], maximumOutputBytes: 1_048_576).ConfigureAwait(false);
                var indexAttributes = await CallRaw(["show", ":.gitattributes"], maximumOutputBytes: 1_048_576).ConfigureAwait(false);
                var attributeEntry = Encoding.UTF8.GetString(await CallRaw(["ls-files", "--stage", "-z", "--", ".gitattributes"]).ConfigureAwait(false));
                if (!attributeEntry.StartsWith("100644 ", StringComparison.Ordinal) || !attributeEntry.EndsWith(" 0\t.gitattributes\0", StringComparison.Ordinal) || attributeEntry.Count(c => c == '\0') != 1)
                    throw new InvalidDataException("Attribute append requires one ordinary100644 stage-zero index entry.");
                TaskOperations.Sha(change.BeforeSha256); TaskOperations.Sha(change.AfterSha256);
                if (Convert.ToHexStringLower(SHA256.HashData(original)) != change.BeforeSha256 ||
                    !original.SequenceEqual(headAttributes) || !original.SequenceEqual(indexAttributes))
                    throw new InvalidDataException("Attribute HEAD/index/worktree before bytes differ.");
                attributeBytes = BuildAttributeAppend(change, original, owned, budget);
            }
            var plans = new List<(GitAsset File, string Filter, string Mode)>(); long bytes = 0;
            var paths = request.Files.Select(file => file.Path).ToArray();
            var filters = await Filters(paths).ConfigureAwait(false); var cachedFilters = await Filters(paths, true).ConfigureAwait(false);
            var indexEntries = await Entries(paths).ConfigureAwait(false);
            foreach (var file in request.Files)
            {
                budget.Check();
                if (filters[file.Path] != cachedFilters[file.Path]) throw new InvalidDataException("Owned worktree and index filters differ before local admission.");
                if (attributeBytes is not null)
                {
                    var parts = file.Path.Split('/'); var ancestor = repository;
                    for (var part = 0; part < parts.Length - 1 && part < 256; part++)
                    {
                        budget.Check(); ancestor = Path.Combine(ancestor, parts[part]);
                        if (Path.Exists(Path.Combine(ancestor, ".gitattributes"))) throw new InvalidDataException("Root attribute append does not admit nested attribute overrides.");
                    }
                    if (parts.Length > 256) throw new InvalidDataException("Owned attribute ancestor depth bound256.");
                }
                var finalFilter = request.AttributeChange?.LfsPaths.Contains(file.Path, StringComparer.Ordinal) == true ? "lfs" : filters[file.Path];
                if (file.Bytes >= 10L * 1024 * 1024 && finalFilter != "lfs") throw new InvalidDataException("Large owned file requires an established or exact planned LFS rule.");
                var mode = indexEntries.TryGetValue(file.Path, out var indexed) ? indexed.Mode : "100644";
                plans.Add((file, finalFilter, mode)); bytes = checked(bytes + file.Bytes);
                if (bytes > 137438953472) throw new InvalidDataException("Local asset bytes exceed128GiB.");
            }
            foreach (var file in request.Files) { budget.Check(); TaskOperations.VerifyFile(SafePaths.Within(repository, file.Path, budget), file.Bytes, file.Sha256, budget); }
            var admitted = await State("ADMITTED_LOCAL_PLAN").ConfigureAwait(false);
            if (!SameState(before, admitted)) throw new InvalidDataException("Repository changed during local admission; no writes performed.");
            if (calls + (attributeBytes is null ? 0 : 1) + 10 + 2 * nativePlan.StagedQueryChunks > 160)
                throw new InvalidDataException("Remaining native call budget cannot complete the admitted local plan before mutation.");
            if (request.Operation == "preview")
            {
                TaskOperations.WriteNew(Path.Combine(output, "local-after.json"), admitted with { Status = "PASS_PREVIEW_NO_REPOSITORY_WRITES" }, GitJsonContext.Default.GitLocalState);
                return new("PASS_preview_owned_bytes_attributes_and_refs_only", request.Operation, current, null, calls, plans.Count, bytes, request.LocalFixture, "FULL_FRESH_CONTENT_READBACK_NOT_RUN");
            }
            var mutationStarted = false; var temporaryCreated = false; string? committed = null; string? temporaryAttributes = null;
            try
            {
                if (attributeBytes is not null)
                {
                    var informationAttributes = Path.Combine(gitDirectory, "info", "attributes");
                    SafePaths.NoLinks(informationAttributes, budget);
                    if (Path.Exists(informationAttributes)) throw new InvalidDataException("Git info/attributes appeared before root attribute apply.");
                    if (AssetTransport.FileHash(attributesPath, budget) != request.AttributeChange!.BeforeSha256) throw new InvalidDataException("Attribute bytes changed before apply.");
                    temporaryAttributes = Path.Combine(repository, ".gitattributes.sezika-" + Guid.NewGuid().ToString("N") + ".tmp");
                    mutationStarted = true;
                    using (var stream = new FileStream(temporaryAttributes, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { temporaryCreated = true; stream.Write(attributeBytes); }
                    SafePaths.NoLinks(attributesPath, budget); File.Move(temporaryAttributes, attributesPath, true); temporaryAttributes = null; temporaryCreated = false;
                    if (AssetTransport.FileHash(attributesPath, budget) != request.AttributeChange.AfterSha256) throw new InvalidDataException("Applied attribute bytes differ.");
                    var effective = await Filters(paths).ConfigureAwait(false);
                    foreach (var plan in plans)
                    {
                        budget.Check();
                        if (effective[plan.File.Path] != plan.Filter)
                            throw new InvalidDataException("Applied effective filter differs from the fully admitted plan; partial attributes preserved.");
                    }
                }
                var inputs = new List<(string Path, string Mode, string Source)>();
                var pointerDirectory = Path.Combine(output, "pointer-inputs"); Directory.CreateDirectory(pointerDirectory);
                foreach (var plan in plans)
                {
                    budget.Check(); TaskOperations.VerifyFile(SafePaths.Within(repository, plan.File.Path, budget), plan.File.Bytes, plan.File.Sha256, budget);
                    var source = SafePaths.Within(repository, plan.File.Path, budget);
                    if (plan.Filter == "lfs")
                    {
                        mutationStarted = true; _ = NativeLfsFiles.StoreObject(gitDirectory, SafePaths.Within(repository, plan.File.Path, budget), plan.File.Bytes, plan.File.Sha256, budget);
                        source = Path.Combine(pointerDirectory, inputs.Count.ToString("D4") + ".pointer");
                        using var pointer = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        pointer.Write(NativeLfsFiles.CreatePointer(plan.File.Bytes, plan.File.Sha256));
                    }
                    inputs.Add((plan.File.Path, plan.Mode, source));
                }
                if (attributeBytes is not null) { owned.Add(".gitattributes"); inputs.Add((".gitattributes", "100644", attributesPath)); }
                if (AssetTransport.FileHash(indexPath, budget) != before.IndexSha256) throw new InvalidDataException("Index changed before batched staging; no index overwrite.");
                mutationStarted = true; var stagedObjects = await HashAndIndex(inputs).ConfigureAwait(false);
                var staged = Encoding.UTF8.GetString(await CallRaw(["diff", "--cached", "--name-only", "-z"]).ConfigureAwait(false)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (staged.Any(path => !owned.Any(name => name == path))) throw new InvalidDataException("Local staged set escaped exact owned paths; partial state preserved.");
                if (await Call(["rev-parse", "HEAD"]).ConfigureAwait(false) != current) throw new InvalidDataException("Local parent changed before commit.");
                _ = await RemoteRef().ConfigureAwait(false);
                foreach (var file in request.Files) { budget.Check(); TaskOperations.VerifyFile(SafePaths.Within(repository, file.Path, budget), file.Bytes, file.Sha256, budget); }
                if (attributeBytes is not null && AssetTransport.FileHash(attributesPath, budget) != request.AttributeChange!.AfterSha256) throw new InvalidDataException("Attribute bytes changed before commit.");
                var stagedEntries = await Entries(stagedObjects.Select(entry => entry.Path).ToArray()).ConfigureAwait(false);
                foreach (var entry in stagedObjects)
                {
                    budget.Check();
                    if (!stagedEntries.TryGetValue(entry.Path, out var actual) || actual.Mode != entry.Mode || actual.Oid != entry.ObjectId)
                        throw new InvalidDataException("Owned staged object identity changed before commit.");
                }
                if (staged.Length != 0)
                {
                    environment["GIT_AUTHOR_NAME"] = request.AuthorName; environment["GIT_COMMITTER_NAME"] = request.AuthorName;
                    environment["GIT_AUTHOR_EMAIL"] = request.AuthorEmail; environment["GIT_COMMITTER_EMAIL"] = request.AuthorEmail;
                    environment["GIT_AUTHOR_DATE"] = null; environment["GIT_COMMITTER_DATE"] = null;
                    _ = await Call(["commit", "-m", request.CommitMessage!]).ConfigureAwait(false);
                    committed = await Call(["rev-parse", "HEAD"]).ConfigureAwait(false); TaskOperations.Commit(committed);
                    if (await Call(["rev-list", "--parents", "-n", "1", committed]).ConfigureAwait(false) != committed + " " + current)
                        throw new InvalidDataException("Local commit parent differs; commit preserved.");
                    var committedEntries = await Entries(stagedObjects.Select(entry => entry.Path).ToArray(), committed).ConfigureAwait(false);
                    foreach (var entry in stagedObjects)
                    {
                        budget.Check();
                        if (!committedEntries.TryGetValue(entry.Path, out var actual) || actual.Mode != entry.Mode || actual.Oid != entry.ObjectId)
                            throw new InvalidDataException("Committed owned object/mode differs.");
                    }
                }
                var after = await State(staged.Length == 0 ? "PASS_LOCAL_NO_CHANGE_REMOTE_UNCHANGED" : "PASS_LOCAL_COMMIT_REMOTE_UNCHANGED", committed).ConfigureAwait(false);
                if (after.Head != (committed ?? current) || after.RemoteRef != before.RemoteRef) throw new InvalidDataException("Local or remote ref changed during final readback; state preserved.");
                TaskOperations.WriteNew(Path.Combine(output, "local-after.json"), after, GitJsonContext.Default.GitLocalState);
                return new(staged.Length == 0 ? "PASS_no_changed_owned_files_remote_unchanged" : "PASS_owned_local_commit_remote_unchanged", request.Operation,
                    current, null, calls, plans.Count, bytes, request.LocalFixture, "FULL_FRESH_CONTENT_READBACK_NOT_RUN") { LocalCommit = committed };
            }
            catch (Exception error) when (mutationStarted)
            {
                var partial = new GitLocalState("UNKNOWN_PARTIAL_LOCAL_MUTATION_PRESERVED", null, null, null, null, committed, error.GetType().Name);
                try { partial = (await State("PARTIAL_LOCAL_MUTATION_PRESERVED", committed).ConfigureAwait(false)) with { ErrorType = error.GetType().Name }; }
                catch (Exception observation) when (observation is IOException or InvalidDataException or OperationCanceledException or TimeoutException) { }
                TaskOperations.WriteNew(Path.Combine(output, "partial-local-state.json"), partial, GitJsonContext.Default.GitLocalState);
                throw;
            }
            finally
            {
                if (temporaryCreated && temporaryAttributes is not null && File.Exists(temporaryAttributes))
                {
                    // Only the exact CreateNew temporary path owned above is removed; applied attributes and partial index remain.
                    RegularFiles.Require(temporaryAttributes, new Budget(2, null, CancellationToken.None)); File.Delete(temporaryAttributes);
                }
            }
        }
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
            if (localOnly)
            {
                SafePaths.NoLinks(gitDirectory, budget);
                return await RunLocalAsync(gitDirectory, current).ConfigureAwait(false);
            }
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
            var readPaths = request.Files.Select(file => file.Path).ToArray();
            Dictionary<string, string>? readFilters = null;
            Dictionary<string, (string Mode, string Oid)>? readIndex = null, readTree = null;
            var pointerPairs = new Dictionary<string, (byte[] Committed, byte[] Indexed)>(StringComparer.Ordinal);
            if (request.Operation != "publish")
            {
                readFilters = await Filters(readPaths, true).ConfigureAwait(false);
                readIndex = await Entries(readPaths).ConfigureAwait(false); readTree = await Entries(readPaths, request.ExpectedCommit).ConfigureAwait(false);
                var lfsPaths = readPaths.Where(path => readFilters[path] == "lfs").ToArray(); var ids = new List<string>();
                foreach (var path in lfsPaths)
                {
                    budget.Check();
                    if (!readIndex.TryGetValue(path, out var indexed) || !readTree.TryGetValue(path, out var committed)) throw new InvalidDataException("Readback pointer entry missing.");
                    ids.Add(committed.Oid); ids.Add(indexed.Oid);
                }
                var blobs = await Blobs(ids.ToArray()).ConfigureAwait(false);
                for (var pointerIndex = 0; pointerIndex < lfsPaths.Length; pointerIndex++) { budget.Check(); pointerPairs.Add(lfsPaths[pointerIndex], (blobs[2 * pointerIndex], blobs[2 * pointerIndex + 1])); }
            }
            foreach (var file in request.Files)
            {
                budget.Check();
                if (!seen.Add(SafePaths.Relative(file.Path))) throw new InvalidDataException("Duplicate owned Git path.");
                if (request.Operation != "publish")
                {
                    if (!readIndex!.TryGetValue(file.Path, out var indexed) || !readTree!.TryGetValue(file.Path, out var committed) || indexed != committed)
                        throw new InvalidDataException("Owned readback file is not bound to the same committed and index object/mode.");
                    if (readFilters![file.Path] == "lfs")
                    {
                        var pointer = pointerPairs[file.Path].Committed; var indexPointer = pointerPairs[file.Path].Indexed;
                        if (!NativeLfsFiles.TryParsePointer(pointer, out var oid, out var size) || oid != file.Sha256 || size != file.Bytes ||
                            !pointer.SequenceEqual(NativeLfsFiles.CreatePointer(file.Bytes, file.Sha256)) || !indexPointer.SequenceEqual(pointer))
                            throw new InvalidDataException("Readback committed/index LFS pointer differs from the pinned actual-byte manifest.");
                        if (request.Operation == "clone")
                        {
                            _ = NativeLfsFiles.HydrateFreshPointer(repository, gitDirectory, file.Path, pointer, file.Bytes, file.Sha256, budget); hydrated++;
                        }
                        verifiedLfsPaths.Add(file.Path);
                    }
                    else if (readFilters![file.Path] is not ("unspecified" or "unset")) throw new InvalidDataException("Unreviewed readback filter is not admitted.");
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
            var publishFilters = await Filters(readPaths).ConfigureAwait(false);
            var ordinary = new List<string>(); var lfsInputs = new List<(string Path, string Mode, string Source)>();
            var pointerRoot = Path.Combine(output, "publish-pointer-inputs"); Directory.CreateDirectory(pointerRoot);
            foreach (var file in request.Files)
            {
                budget.Check();
                if (publishFilters[file.Path] == "lfs")
                {
                    _ = NativeLfsFiles.StoreObject(gitDirectory, SafePaths.Within(repository, file.Path, budget), file.Bytes, file.Sha256, budget);
                    var source = Path.Combine(pointerRoot, lfsInputs.Count.ToString("D4") + ".pointer");
                    using (var pointer = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None)) pointer.Write(NativeLfsFiles.CreatePointer(file.Bytes, file.Sha256));
                    lfsInputs.Add((file.Path, "100644", source));
                }
                else if (publishFilters[file.Path] is "unspecified" or "unset") ordinary.Add(file.Path);
                else throw new InvalidDataException("Unreviewed local Git filter is not admitted.");
            }
            if (lfsInputs.Count > 0) _ = await HashAndIndex(lfsInputs).ConfigureAwait(false);
            // Ordinary publication still uses native add and its established text/EOL semantics.
            foreach (var group in PathChunks(ordinary.ToArray(), budget)) _ = await CallRaw(new[] { "add", "--" }.Concat(group).ToArray()).ConfigureAwait(false);
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

    internal static string[][] PathChunks(string[] paths, Budget budget)
    {
        if (paths.Length > 1001) throw new InvalidDataException("Git query path bound1001.");
        var result = new List<string[]>(); var current = new List<string>(); var characters = 0;
        foreach (var path in paths)
        {
            budget.Check();
            if (path.Length is < 1 or > 1024 || path.Any(char.IsControl) || SafePaths.Relative(path) != path) throw new InvalidDataException("Git batch path shape differs.");
            if (current.Count == 64 || characters + path.Length + 1 > 20000) { result.Add(current.ToArray()); current.Clear(); characters = 0; }
            current.Add(path); characters += path.Length + 1;
        }
        if (current.Count != 0) result.Add(current.ToArray()); return result.ToArray();
    }

    internal static GitNativeCallPlan PlanNativeCalls(GitRequest request, Budget budget)
    {
        var paths = request.Files.Select(file => file.Path).ToArray(); var chunks = PathChunks(paths, budget).Length;
        var attribute = request.AttributeChange is not null ? 1 : 0;
        var stagedChunks = PathChunks(attribute == 0 ? paths : paths.Append(".gitattributes").ToArray(), budget).Length;
        var maximum = request.Operation switch
        {
            "preview" => 11 + 3 * attribute + chunks,
            "commit-only" => 21 + 4 * attribute + chunks + 2 * stagedChunks,
            "publish" => 14 + chunks,
            "clone" => 10 + (paths.Length + 15) / 16 + 2 * chunks,
            "readback" => 8 + 2 * chunks,
            _ => throw new InvalidDataException("Unknown Git operation for call planning.")
        };
        return new(request.Operation, paths.Length, chunks, stagedChunks, maximum);
    }

    private static byte[] BuildAttributeAppend(GitAttributeAppend change, byte[] original, HashSet<string> owned, Budget budget)
    {
        if (change.LfsPaths is null || change.VerbatimPaths is null || change.LfsPaths.Length > 1000 || change.VerbatimPaths.Length > 1000 ||
            change.LfsPaths.Length + change.VerbatimPaths.Length == 0) throw new InvalidDataException("Explicit bounded attribute additions required.");
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var append = new StringBuilder("\n");
        foreach (var group in new[] { (Paths: change.LfsPaths, Suffix: " filter=lfs diff=lfs merge=lfs -text\n"), (Paths: change.VerbatimPaths, Suffix: " -text\n") })
            foreach (var path in group.Paths)
            {
                budget.Check();
                if (path.Length is < 1 or > 512 || path.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '/' or '-')) ||
                    SafePaths.Relative(path) != path || !owned.Any(name => name == path) || !selected.Add(path))
                    throw new InvalidDataException("Attribute addition must name an exact unique portable owned path.");
                append.Append(path).Append(group.Suffix);
            }
        var extra = Encoding.UTF8.GetBytes(append.ToString());
        if (original.Length + extra.Length > 1_048_576) throw new InvalidDataException("Appended attribute byte cap1MiB.");
        var result = new byte[original.Length + extra.Length]; original.CopyTo(result, 0); extra.CopyTo(result, original.Length);
        if (Convert.ToHexStringLower(SHA256.HashData(result)) != change.AfterSha256) throw new InvalidDataException("Planned attribute after SHA differs.");
        return result;
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
[JsonSerializable(typeof(GitLocalState))]
[JsonSerializable(typeof(GitNativeCallPlan))]
[JsonSourceGenerationOptions(WriteIndented = true, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class GitJsonContext : JsonSerializerContext;
