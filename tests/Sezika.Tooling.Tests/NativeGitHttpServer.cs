using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Sezika.Tooling;

internal sealed record NativeHttpRequest(string Method, string Target, string? ContentType, string? ContentEncoding, string? GitProtocol, byte[] Body);
internal sealed record NativeHttpResponse(int Status, string ContentType, byte[] Body);
internal sealed record NativeGitHttpReceipt(string Status, string Backend, string BackendSha256, int Requests, string[] ProcessReceipts, string[] Errors);

// Fixture-only smart HTTP to an explicitly admitted native CGI image. The
// listener remains C#; no command strings, daemon, or shell transport.
internal sealed class NativeGitHttpServer : IDisposable
{
    private readonly string backend, digest, root, output, hooks;
    private readonly string? broker;
    private readonly Func<int> allocateCall;
    private readonly CancellationTokenSource stop;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object sync = new();
    private readonly HashSet<string> repositories = new(StringComparer.Ordinal);
    private readonly List<ExecutionResult> results = [];
    private readonly List<string> errors = [];
    private int requests;
    internal int Requests => requests;

    internal NativeGitHttpServer(string executable, string root, string output, string hooks, string? broker, Func<int> allocateCall, CancellationToken cancellation)
    {
        this.backend = Path.GetFullPath(executable); this.root = Path.GetFullPath(root); this.output = Path.GetFullPath(output); this.hooks = Path.GetFullPath(hooks); this.broker = broker;
        this.allocateCall = allocateCall;
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation); stop.CancelAfter(TimeSpan.FromSeconds(240));
        var budget = new Budget(10, null, stop.Token);
        ProcessExecutor.ValidateExecutable(backend, budget); SafePaths.NoLinks(root, budget); SafePaths.NoLinks(output, budget); SafePaths.NoLinks(hooks, budget);
        if (Path.Exists(output) || !Directory.Exists(root) || !Directory.Exists(hooks)) throw new IOException("Fresh HTTP backend output and owned root/hooks required.");
        using (var input = File.OpenRead(backend)) digest = budget.Hash(input, input.Length);
        Directory.CreateDirectory(output);
    }

    internal void Register(string name, string bare, bool future = false)
    {
        var budget = new Budget(5, null, stop.Token);
        if (name.Length is < 1 or > 64 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
            Path.GetFullPath(bare) != SafePaths.Within(root, name + ".git", budget) || (!future && !Directory.Exists(bare))) throw new InvalidDataException("Explicit owned HTTP bare repository scope required.");
        lock (sync) if (repositories.Count >= 6 || !repositories.Add(name)) throw new InvalidDataException("Git HTTP repository bound6 or duplicate scope.");
    }

    internal async Task<NativeHttpResponse> Handle(NativeHttpRequest request, CancellationToken cancellation)
    {
        var index = Interlocked.Increment(ref requests);
        if (index > 256 || clock.Elapsed.TotalSeconds >= 230 || request.Body.Length > 1048576) throw new InvalidDataException("Git HTTP request/time/body bounds differ.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var token = deadline.Token; var budget = new Budget(25, null, token);
        var question = request.Target.IndexOf('?'); var path = question < 0 ? request.Target : request.Target[..question]; var query = question < 0 ? "" : request.Target[(question + 1)..];
        var pieces = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length is < 3 or > 4 || pieces[0] != "git" || !pieces[1].EndsWith(".git", StringComparison.Ordinal)) throw new InvalidDataException("Git HTTP fixture route differs.");
        var name = pieces[1][..^4]; lock (sync) if (!repositories.Contains(name)) throw new InvalidDataException("Git HTTP unknown repository.");
        var bare = SafePaths.Within(root, name + ".git", budget);
        if (!Directory.Exists(bare)) throw new InvalidDataException("Admitted future bare repository has not actually been created.");
        var discovery = request.Method == "GET" && pieces.Length == 4 && pieces[2] == "info" && pieces[3] == "refs" && (query is "service=git-upload-pack" or "service=git-receive-pack") && request.Body.Length == 0;
        var rpc = request.Method == "POST" && pieces.Length == 3 && (pieces[2] is "git-upload-pack" or "git-receive-pack") && query.Length == 0 &&
            request.ContentType == "application/x-" + pieces[2] + "-request";
        if (!discovery && !rpc) throw new InvalidDataException("Only native smart HTTP discovery/RPC is admitted.");
        if (request.ContentEncoding is not (null or "gzip" or "identity") || request.GitProtocol is not (null or "version=1" or "version=2")) throw new InvalidDataException("Git HTTP encoding/protocol header differs.");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null", ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_PROJECT_ROOT"] = root, ["GIT_HTTP_EXPORT_ALL"] = "1", ["REQUEST_METHOD"] = request.Method, ["PATH_INFO"] = "/" + string.Join('/', pieces.Skip(1)),
            ["QUERY_STRING"] = query, ["CONTENT_TYPE"] = request.ContentType, ["CONTENT_LENGTH"] = request.Body.Length.ToString(CultureInfo.InvariantCulture),
            ["HTTP_CONTENT_ENCODING"] = request.ContentEncoding, ["HTTP_GIT_PROTOCOL"] = request.GitProtocol,
            ["SERVER_PROTOCOL"] = "HTTP/1.1", ["SERVER_NAME"] = "127.0.0.1", ["REMOTE_ADDR"] = "127.0.0.1", ["REMOTE_USER"] = "native-loopback-fixture"
        };
        foreach (var variable in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_CONFIG", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT", "GIT_EXEC_PATH", "GIT_SSH", "GIT_SSH_COMMAND", "GIT_PROXY_COMMAND", "GIT_ASKPASS", "SSH_ASKPASS", "GIT_EDITOR", "GIT_SEQUENCE_EDITOR", "GIT_TEMPLATE_DIR", "GIT_TRACE", "GIT_TRACE_CURL", "GIT_CURL_VERBOSE" }) environment[variable] = null;
        (string Key, string Value)[] configuration = [("credential.helper", ""), ("core.hooksPath", hooks), ("core.fsmonitor", "false"), ("gc.auto", "0"), ("maintenance.auto", "false"), ("commit.gpgSign", "false"), ("http.receivepack", "true"), ("http.maxRequestBuffer", "1048576")];
        environment["GIT_CONFIG_COUNT"] = configuration.Length.ToString(CultureInfo.InvariantCulture);
        for (var item = 0; item < configuration.Length; item++) { environment["GIT_CONFIG_KEY_" + item] = configuration[item].Key; environment["GIT_CONFIG_VALUE_" + item] = configuration[item].Value; }
        var directory = Path.Combine(output, "backend-" + allocateCall().ToString("D3", CultureInfo.InvariantCulture));
        try
        {
            TaskOperations.VerifyFile(backend, new FileInfo(backend).Length, digest, budget);
            var result = await ProcessExecutor.ExecuteAsync(new ExecutionRequest
            {
                Executable = backend, ArgumentList = [], WorkingDirectory = root, OutputDirectory = directory, Environment = environment,
                StandardInput = request.Body, MaximumInputBytes = 1048576, MaximumOutputBytes = 2 * 1048576, TimeoutSeconds = 20, CleanupSeconds = 5, MaximumProcesses = 32, BrokerExecutable = broker
            }, budget, token).ConfigureAwait(false);
            lock (sync) results.Add(result);
            if (result.Status != "PASS" || result.ExitCode != 0 || result.RemainingProcesses != 0) throw new IOException("Native CGI execution/identity/recovery failure: " + directory);
            if (index % 16 == 0) Console.Error.WriteLine($"native-git-http requests={index}/256");
            var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "stdout.bin"), token).ConfigureAwait(false);
            var separator = -1; var separatorBytes = 0;
            for (var item = 0; item < Math.Min(16384, bytes.Length - 1); item++)
            {
                token.ThrowIfCancellationRequested();
                if (bytes[item] == '\n' && bytes[item + 1] == '\n') { separator = item; separatorBytes = 2; break; }
                if (item + 3 < bytes.Length && bytes[item] == '\r' && bytes[item + 1] == '\n' && bytes[item + 2] == '\r' && bytes[item + 3] == '\n') { separator = item; separatorBytes = 4; break; }
            }
            if (separator < 0 || bytes.Length - separator - separatorBytes > 1048576) throw new InvalidDataException("Native CGI header/body byte bounds differ.");
            var status = 200; string? type = null; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in Encoding.ASCII.GetString(bytes, 0, separator).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = line.TrimEnd('\r'); var colon = clean.IndexOf(':');
                if (colon <= 0 || !seen.Add(clean[..colon])) throw new InvalidDataException("Native CGI header invalid/duplicate.");
                if (clean[..colon].Equals("Status", StringComparison.OrdinalIgnoreCase)) status = int.Parse(clean[(colon + 1)..].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
                if (clean[..colon].Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) type = clean[(colon + 1)..].Trim();
            }
            if (status is < 200 or > 599 || type is null || type.Length > 256 || type.Any(char.IsControl)) throw new InvalidDataException("Native CGI status/type differs.");
            return new(status, type, bytes.AsSpan(separator + separatorBytes).ToArray());
        }
        catch (Exception error) { lock (sync) errors.Add(error.GetType().Name + ": " + error.Message); throw; }
    }

    internal ExecutionResult[] Results { get { lock (sync) return results.ToArray(); } }
    internal string[] Errors { get { lock (sync) return errors.ToArray(); } }
    public void Dispose()
    {
        stop.Cancel(); var captured = Results;
        var receipt = new NativeGitHttpReceipt(errors.Count > 0 ? "FAIL" : captured.Length == 0 ? "NOT_RUN" : "PASS_native_CGI_fixture_only", backend, digest, requests,
            captured.Select(result => Path.Combine(result.OutputDirectory, "result.json")).ToArray(), errors.ToArray());
        TaskOperations.WriteNew(Path.Combine(output, "http-server.json"), receipt, NativeGitHttpJson.Default.NativeGitHttpReceipt);
        stop.Dispose();
    }
}

[JsonSerializable(typeof(NativeGitHttpReceipt))]
internal partial class NativeGitHttpJson : JsonSerializerContext;
