using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.Tooling;

internal sealed record NativeGitHttpFixtureRequest(string ScopeId, string OwnedRoot, string ClientRoot,
    string ServerExecutable, string ServerSha256, string BackendExecutable, string BackendSha256,
    string BrokerExecutable, string BrokerSha256, string ReadyReceipt, string CancelFile,
    string[] RepositoryNames, int MaximumSeconds = 240);
internal sealed record NativeGitHttpFixtureReady(string ScopeId, string RequestSha256, string OwnedRoot, string ClientRoot,
    string RepositoryRoot, string BackendExecutable, string BackendSha256, string BrokerExecutable, string BrokerSha256,
    string[] RepositoryNames, int Port, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, ProcessObservation ServerIdentity);
internal sealed record NativeGitHttpFixtureResult(string Status, string ScopeId, string RequestSha256, string ShutdownReason,
    ProcessObservation ServerIdentity, NativeLfsReceipt? Lfs, string[] BackendProcessReceipts, string[] Errors);
internal sealed record NativeGitHttpFixtureClientReceipt(string Status, NativeGitHttpFixtureReady Ready, NativeLfsReceipt? LastStatus, string CancelFile);

// A separate C# apphost owns the listener and native CGI executor. Its Linux
// serial lease never shares a process with the client lease. The caller still
// freezes the complete runtime/code package and independently audits both trees.
internal static class NativeGitHttpFixture
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsLinux() || args.Length != 5 || args[0] != "--serve-native-git-http" || args[1] != "--request" || args[3] != "--output")
            throw new ArgumentException("Linux --serve-native-git-http --request <typed-json> --output <owned-root/server-result.json>");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(250));
        StartupGate.Wait(cancellation.Token);
        var initial = new Budget(10, null, cancellation.Token);
        var (request, requestSha) = ReadRequest(args[2], initial);
        var output = SafePaths.Within(request.OwnedRoot, "server-result.json", initial);
        if (Path.GetFullPath(args[4]) != output || Path.Exists(request.OwnedRoot)) throw new IOException("Fresh server-owned output root required.");
        var identity = ProcessObserver.Observe(Environment.ProcessId);
        RequireServer(identity, request, Path.GetFullPath(args[2]), output);
        Directory.CreateDirectory(request.OwnedRoot);
        var repositories = SafePaths.Within(request.OwnedRoot, "repositories", initial);
        var hooks = SafePaths.Within(request.OwnedRoot, "empty-hooks", initial);
        var backendOutput = SafePaths.Within(request.OwnedRoot, "backend-executions", initial);
        Directory.CreateDirectory(repositories); Directory.CreateDirectory(hooks);
        cancellation.CancelAfter(TimeSpan.FromSeconds(request.MaximumSeconds));
        var clock = Stopwatch.StartNew(); var errors = new List<string>(); var calls = 0;
        var reason = "NOT_STARTED"; NativeLfsServer? lfs = null; NativeGitHttpServer? git = null;
        ConsoleCancelEventHandler interrupt = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += interrupt;
        try
        {
            git = new(request.BackendExecutable, repositories, backendOutput, hooks, request.BrokerExecutable, () =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var call = Interlocked.Increment(ref calls);
                if (call > 256 || clock.Elapsed.TotalSeconds >= request.MaximumSeconds) throw new IOException("Separate native CGI count/time bound.");
                return call;
            }, cancellation.Token);
            lfs = new(cancellation.Token) { GitHandler = git.Handle };
            foreach (var name in request.RepositoryNames)
            {
                initial.Check(); var bare = SafePaths.Within(repositories, name + ".git", initial);
                git.Register(name, bare, future: true); lfs.Register(name, bare, future: true);
            }
            var listener = lfs;
            lfs.ControlHandler = (message, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (message.Method != "GET" || message.Target != "/fixture/" + request.ScopeId + "/status" || message.Body.Length != 0)
                    throw new InvalidDataException("Exact scope read-only fixture status route required.");
                return Task.FromResult(new NativeHttpResponse(200, "application/json",
                    JsonSerializer.SerializeToUtf8Bytes(Snapshot(listener, "LIVE_fixture_only"), NativeLfsJson.Default.NativeLfsReceipt)));
            };
            var created = DateTimeOffset.UtcNow;
            TaskOperations.WriteNew(request.ReadyReceipt, new NativeGitHttpFixtureReady(request.ScopeId, requestSha, request.OwnedRoot,
                request.ClientRoot, repositories, request.BackendExecutable, request.BackendSha256, request.BrokerExecutable,
                request.BrokerSha256, request.RepositoryNames, lfs.Port, created, created.AddSeconds(request.MaximumSeconds), identity),
                NativeGitHttpFixtureJson.Default.NativeGitHttpFixtureReady);
            var nextProgress = 10d;
            for (var poll = 0; poll < request.MaximumSeconds * 10 + 1 && clock.Elapsed.TotalSeconds < request.MaximumSeconds; poll++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (File.Exists(request.CancelFile))
                {
                    RegularFiles.Require(request.CancelFile, new Budget(5, null, cancellation.Token));
                    if (new FileInfo(request.CancelFile).Length > 64 || File.ReadAllText(request.CancelFile) != request.ScopeId)
                        throw new IOException("Cancellation file does not bind the exact admitted scope.");
                    reason = "SCOPED_CLIENT_CANCEL"; break;
                }
                if (clock.Elapsed.TotalSeconds >= nextProgress)
                {
                    Console.Error.WriteLine($"native-git-http-server scope={request.ScopeId} seconds={clock.Elapsed.TotalSeconds:F1}/{request.MaximumSeconds} requests={lfs.Requests}/256");
                    nextProgress = clock.Elapsed.TotalSeconds + 10;
                }
                await Task.Delay(100, cancellation.Token).ConfigureAwait(false);
            }
            if (reason != "SCOPED_CLIENT_CANCEL") throw new TimeoutException("Separate HTTP fixture count/wall deadline exhausted.");
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or OperationCanceledException or TimeoutException)
        { reason = error is OperationCanceledException ? "CANCELLED_OR_DEADLINE" : "FAILED"; errors.Add(error.GetType().Name + ": " + error.Message); }
        finally
        {
            Console.CancelKeyPress -= interrupt; cancellation.Cancel();
            try { lfs?.Dispose(); } catch (Exception error) { errors.Add("Listener cleanup: " + error.Message); }
            if (lfs is not null) errors.AddRange(lfs.Errors);
            if (git is not null)
            {
                try
                {
                    var recovery = new Budget(10, null, CancellationToken.None);
                    foreach (var result in git.Results)
                    {
                        recovery.Check(); VerifyBackendCleanup(result, recovery);
                    }
                }
                catch (Exception error) { errors.Add("Native CGI cleanup: " + error.Message); }
                try { git.Dispose(); } catch (Exception error) { errors.Add("CGI receipt: " + error.Message); }
                errors.AddRange(git.Errors);
            }
            var status = errors.Count > 0 ? "FAIL" : "PASS_server_lifecycle_only";
            TaskOperations.WriteNew(output, new NativeGitHttpFixtureResult(status, request.ScopeId, requestSha, reason, identity,
                lfs is null ? null : Snapshot(lfs, errors.Count > 0 ? "FAIL" : Completed(lfs) == 0 ? "NOT_RUN" : "PASS_protocol_fixture_only"),
                git?.Results.Select(result => Path.Combine(result.OutputDirectory, "result.json")).ToArray() ?? [], errors.ToArray()),
                NativeGitHttpFixtureJson.Default.NativeGitHttpFixtureResult);
        }
        return errors.Count == 0 ? 0 : 1;
    }

    internal sealed class Client : IDisposable
    {
        private readonly NativeGitHttpFixtureRequest request;
        private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 65536 };
        private int statusReads;
        internal NativeGitHttpFixtureReady Ready { get; }
        internal NativeLfsReceipt? LastStatus { get; private set; }
        private Client(NativeGitHttpFixtureRequest request, NativeGitHttpFixtureReady ready) { this.request = request; Ready = ready; }
        internal static Client Open(string configuration, string clientRoot, Budget budget)
        {
            var (request, digest) = ReadRequest(configuration, budget);
            if (request.ClientRoot != Path.GetFullPath(clientRoot)) throw new IOException("External server request belongs to another client output root.");
            RegularFiles.Require(request.ReadyReceipt, budget);
            if (new FileInfo(request.ReadyReceipt).Length > 65536) throw new IOException("External ready receipt byte bound64KiB.");
            var ready = JsonSerializer.Deserialize(File.ReadAllBytes(request.ReadyReceipt), NativeGitHttpFixtureJson.Default.NativeGitHttpFixtureReady)
                ?? throw new IOException("Missing external HTTP ready receipt.");
            if (ready.ScopeId != request.ScopeId || ready.RequestSha256 != digest || ready.OwnedRoot != request.OwnedRoot || ready.ClientRoot != request.ClientRoot ||
                ready.RepositoryRoot != SafePaths.Within(request.OwnedRoot, "repositories", budget) || ready.BackendExecutable != request.BackendExecutable ||
                ready.BackendSha256 != request.BackendSha256 || ready.BrokerExecutable != request.BrokerExecutable || ready.BrokerSha256 != request.BrokerSha256 ||
                !ready.RepositoryNames.SequenceEqual(request.RepositoryNames) || ready.Port is < 1 or > 65535 || ready.CreatedUtc > DateTimeOffset.UtcNow.AddSeconds(5) ||
                ready.ExpiresUtc != ready.CreatedUtc.AddSeconds(request.MaximumSeconds) || ready.ExpiresUtc <= DateTimeOffset.UtcNow)
                throw new IOException("External ready scope/request/endpoint/expiry differs.");
            RequireServer(ready.ServerIdentity, request, Path.GetFullPath(configuration), SafePaths.Within(request.OwnedRoot, "server-result.json", budget));
            var client = new Client(request, ready);
            try { client.RequireLive(); return client; }
            catch { client.Dispose(); throw; }
        }
        internal string GitEndpoint(string name) { RequireName(name); return $"http://127.0.0.1:{Ready.Port}/git/{name}.git"; }
        internal string LfsEndpoint(string name) { RequireName(name); return $"http://127.0.0.1:{Ready.Port}/lfs/{name}"; }
        private void RequireName(string name)
        {
            if (!request.RepositoryNames.Contains(name, StringComparer.Ordinal)) throw new IOException("External server has not admitted this exact repository name.");
        }
        private void RequireLive()
        {
            var actual = ProcessObserver.Observe(Ready.ServerIdentity.Pid); var registered = Ready.ServerIdentity;
            if (Ready.ExpiresUtc <= DateTimeOffset.UtcNow || actual.State != "Present" || actual.ParentPid != registered.ParentPid ||
                actual.StartTicks != registered.StartTicks || actual.BootId != registered.BootId || actual.CommandLine != registered.CommandLine ||
                File.ResolveLinkTarget("/proc/" + actual.Pid.ToString(CultureInfo.InvariantCulture) + "/exe", false)?.FullName != request.ServerExecutable)
                throw new IOException("External server live native birth/parent/argv/image differs.");
        }
        internal NativeLfsReceipt Counters(CancellationToken cancellation)
        {
            if (++statusReads > 8) throw new IOException("Fixture-only status read bound8.");
            RequireLive(); using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var bytes = http.GetByteArrayAsync($"http://127.0.0.1:{Ready.Port}/fixture/{request.ScopeId}/status", deadline.Token).GetAwaiter().GetResult();
            LastStatus = JsonSerializer.Deserialize(bytes, NativeLfsJson.Default.NativeLfsReceipt) ?? throw new IOException("Missing actual external protocol counters.");
            if (LastStatus.Port != Ready.Port || LastStatus.Status != "LIVE_fixture_only" || LastStatus.Errors.Length != 0) throw new IOException("External live protocol counter scope/status differs.");
            return LastStatus;
        }
        internal void Cancel(string clientReceipt)
        {
            var recovery = new Budget(10, null, CancellationToken.None); SafePaths.NoLinks(request.CancelFile, recovery);
            if (File.Exists(request.CancelFile))
            {
                RegularFiles.Require(request.CancelFile, recovery);
                if (new FileInfo(request.CancelFile).Length > 64 || File.ReadAllText(request.CancelFile) != request.ScopeId) throw new IOException("Foreign cancellation file preserved.");
            }
            else
            {
                using (var pending = new FileStream(request.CancelFile + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { pending.Write(Encoding.ASCII.GetBytes(request.ScopeId)); pending.Flush(flushToDisk: true); }
                File.Move(request.CancelFile + ".pending", request.CancelFile, false);
            }
            TaskOperations.WriteNew(clientReceipt, new NativeGitHttpFixtureClientReceipt("CANCEL_REQUESTED_server_final_NOT_VERIFIED", Ready, LastStatus, request.CancelFile),
                NativeGitHttpFixtureJson.Default.NativeGitHttpFixtureClientReceipt);
        }
        public void Dispose() => http.Dispose();
    }

    private static (NativeGitHttpFixtureRequest Request, string Digest) ReadRequest(string path, Budget budget)
    {
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(path)) throw new IOException("Explicit Linux native HTTP request required.");
        RegularFiles.Require(path, budget);
        if (new FileInfo(path).Length is < 2 or > 16384) throw new IOException("Separate HTTP request byte bound16KiB.");
        using var input = File.OpenRead(path); var digest = budget.Hash(input, input.Length); input.Position = 0;
        var request = JsonSerializer.Deserialize(input, NativeGitHttpFixtureJson.Default.NativeGitHttpFixtureRequest) ?? throw new IOException("Missing typed HTTP fixture request.");
        if (!Guid.TryParseExact(request.ScopeId, "N", out _) || request.MaximumSeconds is < 1 or > 240 || request.RepositoryNames.Length is < 1 or > 6 ||
            request.RepositoryNames.Distinct(StringComparer.Ordinal).Count() != request.RepositoryNames.Length) throw new IOException("Explicit HTTP scope/time/repository bounds differ.");
        foreach (var name in request.RepositoryNames)
        {
            budget.Check();
            if (name.Length is < 1 or > 64 || name.Any(value => !char.IsAsciiLetterOrDigit(value) && value != '-')) throw new IOException("Explicit portable repository name required.");
        }
        if (!Path.IsPathFullyQualified(request.OwnedRoot) || !Path.IsPathFullyQualified(request.ClientRoot) || Path.GetFullPath(request.OwnedRoot) != request.OwnedRoot ||
            Path.GetFullPath(request.ClientRoot) != request.ClientRoot || request.ReadyReceipt != SafePaths.Within(request.OwnedRoot, "ready.json", budget) ||
            request.CancelFile != SafePaths.Within(request.OwnedRoot, "CANCEL", budget)) throw new IOException("Exact owned/client/ready/cancel paths required.");
        SafePaths.NoLinks(request.ClientRoot, budget);
        var serverPrefix = Path.TrimEndingDirectorySeparator(request.OwnedRoot) + Path.DirectorySeparatorChar;
        var clientPrefix = Path.TrimEndingDirectorySeparator(request.ClientRoot) + Path.DirectorySeparatorChar;
        if (request.OwnedRoot == request.ClientRoot || request.ClientRoot.StartsWith(serverPrefix, StringComparison.Ordinal) || request.OwnedRoot.StartsWith(clientPrefix, StringComparison.Ordinal))
            throw new IOException("Fresh server and client evidence roots must be disjoint.");
        foreach (var image in new[] { (request.ServerExecutable, request.ServerSha256), (request.BackendExecutable, request.BackendSha256), (request.BrokerExecutable, request.BrokerSha256) })
        {
            budget.Check(); ProcessExecutor.ValidateExecutable(image.Item1, budget);
            using var executable = File.OpenRead(image.Item1);
            if (executable.Length is < 4 or > 134217728 || budget.Hash(executable, executable.Length) != image.Item2) throw new IOException("Complete native fixture image SHA differs.");
        }
        return (request, digest);
    }
    private static void RequireServer(ProcessObservation identity, NativeGitHttpFixtureRequest request, string configuration, string output)
    {
        var command = string.Join('\0', new[] { request.ServerExecutable, "--serve-native-git-http", "--request", configuration, "--output", output }) + '\0';
        if (identity.State != "Present" || identity.Platform != "linux" || identity.Pid <= 0 || identity.ParentPid <= 0 || identity.StartTicks is null || identity.BootId is null || identity.CommandLine != command)
            throw new IOException("Actual separate server native birth/parent/complete argv differs.");
    }
    private static int Completed(NativeLfsServer server) => server.BatchResponses + server.UploadResponses + server.DownloadResponses;
    private static NativeLfsReceipt Snapshot(NativeLfsServer server, string status) => new(status, server.Port, server.Requests, server.BatchResponses,
        server.UploadResponses, server.DownloadResponses, server.MissingObjects, server.UnsupportedLockResponses, server.ActualRequests, server.Errors);
    private static void VerifyBackendCleanup(ExecutionResult result, Budget budget)
    {
        if (result.Status != "PASS" || result.CleanupStatus != "PASS" || result.RemainingProcesses != 0 || result.Processes.Length is < 1 or > 32) throw new IOException("Actual separate CGI execution/cleanup is incomplete.");
        foreach (var identity in result.Processes)
        {
            budget.Check();
            if (identity.Platform != "linux" || identity.StartTicks is null || identity.BootId is null || identity.ParentChain.Length is < 1 or > 64 || identity.CommandLine.Length == 0)
                throw new IOException("Separate CGI native birth/argv/parent chain is incomplete.");
            var actual = ProcessObserver.Observe(identity.Pid);
            if (actual.State != "Absent" && !(actual.State == "Present" && (actual.StartTicks != identity.StartTicks || actual.BootId != identity.BootId)))
                throw new IOException("Separate CGI owned process survives or absence is UNKNOWN.");
            var image = (identity.AfterExecCommandLine ?? identity.CommandLine).Split('\0')[0];
            if (new[] { "sh", "bash", "dash", "zsh", "pwsh", "powershell", "cmd", "python", "python3", "node", "cscript", "wscript" }
                .Contains(Path.GetFileNameWithoutExtension(image), StringComparer.OrdinalIgnoreCase)) throw new IOException("Interpreter descendant in separate native CGI fixture.");
        }
    }
}

[JsonSerializable(typeof(NativeGitHttpFixtureRequest))]
[JsonSerializable(typeof(NativeGitHttpFixtureReady))]
[JsonSerializable(typeof(NativeGitHttpFixtureResult))]
[JsonSerializable(typeof(NativeGitHttpFixtureClientReceipt))]
[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal partial class NativeGitHttpFixtureJson : JsonSerializerContext;
