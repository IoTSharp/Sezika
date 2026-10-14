using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Events;
using Microsoft.DevTunnels.Ssh.Messages;
using Sezika.RemoteTool;
using Sezika.Tooling;

namespace Sezika.SshFixture;

internal sealed class FixtureServer : IAsyncDisposable
{
    internal const string UserName = "fixture-user";
    internal const string Password = "fixture-password-not-production";
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop;
    private readonly int maximumSeconds;
    private readonly Microsoft.DevTunnels.Ssh.Algorithms.IKeyPair hostKey;
    private readonly string root;
    private readonly byte[] clientKey;
    private readonly string virtualRoot;
    private readonly int maximumRequests;
    private readonly ExecutablePin? nativeWorker;
    private readonly BundlePin? workerBundle;
    private readonly BundlePin? payloadBundle;
    private readonly List<Task> sessions = [];
    private readonly List<SftpBackend> backends = [];
    private readonly List<SshServerSession> owners = [];
    private readonly List<string> diagnostics = [];
    private readonly Task accepting;
    private readonly object gate = new();
    internal int Port { get; }
    internal string HostPin { get; }
    internal int Connections { get; private set; }
    internal int AuthenticationAccepted { get; private set; }
    internal int PublicKeyAccepted { get; private set; }
    internal int Requests => backends.Sum(b => b.Requests);
    internal int RemainingHandles => backends.Sum(b => b.OpenHandles);
    internal SftpOperationFailure[] Failures => backends.SelectMany(b => b.Failures).Take(8).ToArray();
    internal SftpOperationCount[] OperationCounts => backends.SelectMany(b => b.OperationCounts).GroupBy(c => c.Operation)
        .OrderBy(g => g.Key).Select(g => new SftpOperationCount(g.Key, g.Sum(c => c.Count))).ToArray();
    internal string[] Diagnostics { get { lock (gate) return diagnostics.ToArray(); } }

    internal FixtureServer(string root, byte[] clientPublicKey, string? virtualRoot = null, int maximumRequests = 256, ExecutablePin? nativeWorker = null,
        BundlePin? workerBundle = null, BundlePin? payloadBundle = null, int maximumSeconds = 60)
    {
        this.root = root; clientKey = clientPublicKey; this.virtualRoot = virtualRoot ?? "/fixture"; this.maximumRequests = maximumRequests; this.nativeWorker = nativeWorker;
        this.workerBundle = workerBundle; this.payloadBundle = payloadBundle;
        if (maximumSeconds is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(maximumSeconds));
        this.maximumSeconds = maximumSeconds;
        stop = new CancellationTokenSource(TimeSpan.FromSeconds(maximumSeconds));
        if (maximumRequests is < 1 or > 512 || nativeWorker is not null && (!OperatingSystem.IsLinux() || this.virtualRoot != root || maximumRequests != 512))
            throw new InvalidDataException("Native fixture requires the explicit Linux physical root and approved case9 budget.");
        if (nativeWorker is not null && (workerBundle is null || payloadBundle is null || new FileInfo(nativeWorker.Path).Length > 33554432))
            throw new InvalidDataException("Native fixture requires exact worker/payload code bundles and <=32MiB actual NativeAOT image.");
        hostKey = SshAlgorithms.PublicKey.RsaWithSha512.GenerateKeyPair(2048);
        var key = hostKey.GetPublicKeyBytes(); HostPin = "SHA256:" + Convert.ToBase64String(SHA256.HashData(key.Array.AsSpan(key.Offset, key.Count))).TrimEnd('=');
        listener.Start(4); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try
        {
            for (var connection = 0; connection < 8; connection++)
            {
                stop.Token.ThrowIfCancellationRequested(); var tcp = await listener.AcceptTcpClientAsync(stop.Token); Connections++;
                var task = SessionAsync(tcp); lock (gate) sessions.Add(task);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }
    private async Task SessionAsync(TcpClient tcp)
    {
        var trace = new TraceSource("fixture-ssh", SourceLevels.Error);
        trace.Listeners.Clear(); trace.Listeners.Add(new FixtureTrace(RecordDiagnostic));
        try
        {
        using (tcp)
        using (var session = new SshServerSession(new SshSessionConfiguration(), trace))
        {
            lock (gate) owners.Add(session);
            session.Credentials.PublicKeys.Add(hostKey);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Closed += (_, _) => closed.TrySetResult();
            session.Authenticating += (_, authentication) =>
            {
                var accepted = authentication.Username == UserName &&
                    (authentication.AuthenticationType == SshAuthenticationType.ClientPassword && authentication.Password == Password ||
                     authentication.AuthenticationType is SshAuthenticationType.ClientPublicKey or SshAuthenticationType.ClientPublicKeyQuery && authentication.PublicKey is not null &&
                     CryptographicOperations.FixedTimeEquals(PublicBytes(authentication.PublicKey), clientKey));
                if (accepted)
                {
                    AuthenticationAccepted++;
                    if (authentication.AuthenticationType is SshAuthenticationType.ClientPublicKey or SshAuthenticationType.ClientPublicKeyQuery) PublicKeyAccepted++;
                }
                authentication.AuthenticationTask = Task.FromResult<ClaimsPrincipal?>(accepted ? new(new ClaimsIdentity([new Claim(ClaimTypes.Name, UserName)], "fixture")) : null);
            };
            session.ChannelOpening += (_, opening) =>
            {
                if (opening.Request.ChannelType != "session") { opening.FailureReason = SshChannelOpenFailureReason.UnknownChannelType; return; }
                opening.Channel.Request += (_, request) =>
                {
                    request.IsAuthorized = false;
                    var packet = request.Request.ToBuffer(); var reader = new WireReader(packet.Array.AsSpan(packet.Offset, packet.Count).ToArray());
                    if (reader.Byte() != 98) return; reader.UInt32(); var type = reader.Text(); reader.Byte();
                    if (type == "exec" && nativeWorker is not null)
                    {
                        var text = reader.Text(4096); if (reader.Remaining != 0) return; var arguments = text.Split(' ');
                        if (arguments.Length != 6 || arguments[0] != nativeWorker.Path || arguments[1] != "worker" || arguments[2] != "--request" || arguments[4] != "--sha256") return;
                        try { RemotePaths.Absolute(arguments[3]); RemotePaths.Sha(arguments[5]); if (!RemotePaths.IsWithin(root, arguments[3])) return; }
                        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return; }
                        request.IsAuthorized = true;
                        request.ResponseContinuation = _ => { var task = ServeExecAsync(opening.Channel, arguments); lock (gate) sessions.Add(task); return Task.CompletedTask; }; return;
                    }
                    if (type != "subsystem" || reader.Text() != "sftp" || reader.Remaining != 0) return;
                    request.IsAuthorized = true;
                    request.ResponseContinuation = _ =>
                    {
                        var backend = new SftpBackend(root, stop.Token, virtualRoot, maximumRequests, nativeWorker is null ? 128 : 256,
                            nativeWorker is null ? 64 : 128, nativeWorker?.Path, nativeWorker is null ? 1048576 : new FileInfo(nativeWorker.Path).Length); lock (gate) backends.Add(backend);
                        var task = ServeChannelAsync(backend, opening.Channel); lock (gate) sessions.Add(task); return Task.CompletedTask;
                    };
                };
            };
            try
            {
                await session.ConnectAsync(tcp.GetStream(), stop.Token);
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(maximumSeconds), stop.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or TimeoutException or
                Microsoft.DevTunnels.Ssh.SshConnectionException or ArgumentException or CryptographicException)
            { if (error is not OperationCanceledException) RecordDiagnostic(error.GetType().Name + ":" + error.Message); }
            finally { lock (gate) owners.Remove(session); }
        }
        }
        finally { trace.Close(); }
    }
    private async Task ServeChannelAsync(SftpBackend backend, SshChannel channel)
    {
        using var stream = new SshStream(channel);
        try { await backend.ServeAsync(stream); }
        finally { backend.Dispose(); }
    }
    private async Task ServeExecAsync(SshChannel channel, string[] command)
    {
        using var stream = new SshStream(channel);
        var budget = new Budget(35, null, stop.Token);
        if (nativeWorker is null || AssetTransport.FileHash(nativeWorker.Path, budget) != nativeWorker.Sha256) throw new InvalidDataException("Fixed native fixture worker byte identity differs.");
        BundleProof.VerifyLocal(workerBundle!, budget); BundleProof.VerifyLocal(payloadBundle!, budget);
        var self = Environment.ProcessPath ?? throw new IOException("Fixture native apphost unavailable.");
        var result = await ProcessExecutor.ExecuteAsync(new ExecutionRequest
        {
            Executable = nativeWorker.Path, ArgumentList = command[1..], WorkingDirectory = root, OutputDirectory = Path.Combine(root, "ssh-outer-execution"),
            TimeoutSeconds = 20, CleanupSeconds = 5, MaximumProcesses = 32, MaximumOutputBytes = 65536, BrokerExecutable = self,
            BrokerArgumentPrefix = ["process", "broker"], WatchdogArgumentPrefix = ["process", "watchdog"],
            NestedBrokers = [new() { Executable = nativeWorker.Path, Sha256 = nativeWorker.Sha256, ArgumentPrefix = ["process", "broker"] }]
        }, budget, stop.Token);
        var output = Path.Combine(result.OutputDirectory, "stdout.bin");
        if (File.Exists(output)) await stream.WriteAsync(await File.ReadAllBytesAsync(output, stop.Token), stop.Token);
        await stream.FlushAsync(stop.Token);
        await channel.CloseAsync((uint)(result.Status == "PASS" ? 0 : 1), cancellation: stop.Token);
    }
    private static byte[] PublicBytes(Microsoft.DevTunnels.Ssh.Algorithms.IKeyPair key)
    {
        var value = key.GetPublicKeyBytes(); return value.Array.AsSpan(value.Offset, value.Count).ToArray();
    }
    private void RecordDiagnostic(string value)
    {
        value = value.Replace(Password, "[fixture-auth-redacted]", StringComparison.Ordinal);
        if (value.Length > 512) value = value[..512];
        lock (gate) { if (diagnostics.Count < 8) diagnostics.Add(value); }
    }
    private sealed class FixtureTrace(Action<string> record) : TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        { if (eventType is TraceEventType.Error or TraceEventType.Critical) record(id + ":" + message); }
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? arguments)
        { if (eventType is TraceEventType.Error or TraceEventType.Critical) record(id + ":" + (arguments is null ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format ?? "", arguments))); }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop();
        SshServerSession[] active; Task[] tasks;
        lock (gate) { active = owners.ToArray(); tasks = sessions.ToArray(); }
        foreach (var session in active) session.Dispose();
        var failures = new List<Exception>();
        try
        {
            try { await accepting.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { }
            catch (Exception error) { failures.Add(error); }
            foreach (var task in tasks)
            {
                try { await task.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { }
                catch (Exception error) { failures.Add(error); }
            }
        }
        finally
        {
            foreach (var backend in backends) backend.Dispose(); hostKey.Dispose(); CryptographicOperations.ZeroMemory(clientKey); stop.Dispose();
        }
        if (failures.Count != 0) throw new AggregateException("Fixture resource cleanup retained task failures.", failures);
    }
}
