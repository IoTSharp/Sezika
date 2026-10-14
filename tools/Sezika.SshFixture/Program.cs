using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.RemoteTool;
using Sezika.SshFixture;
using Sezika.Tooling;

if (args.Length == 3 && args[0] == "process" && args[1] is "broker" or "watchdog")
    return args[1] == "broker" ? ProcessExecutor.RunBroker(args[2], CancellationToken.None) : ProcessExecutor.RunWatchdog(args[2], CancellationToken.None);
if (args is ["crossos-server", "--request", var serverRequest, "--output", var serverOutput])
{
    using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(130));
    StartupGate.Wait(serverCancellation.Token);
    return await Sezika.SshFixture.Checks.ServeAsync(serverRequest, serverOutput, serverCancellation.Token);
}
if (args is ["crossos-client", "--request", var clientRequest, "--output", var clientOutput, "--maximum-checks", var countText] &&
    int.TryParse(countText, out var crossOsChecks) && crossOsChecks is 1 or 2)
{
    using var clientCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(85));
    StartupGate.Wait(clientCancellation.Token);
    return await Sezika.SshFixture.Checks.RunAsync(clientRequest, clientOutput, crossOsChecks, clientCancellation.Token);
}
if (args.Length is not (4 or 8) || args[0] != "--output" || args[2] != "--case" || !int.TryParse(args[3], out var selected) || selected is < 1 or > 9)
    throw new ArgumentException("--output <fresh-owned-directory> --case <1..9>; case9 requires --worker-directory <publishedLinuxRemoteTool> --payload-directory <publishedLinuxProcessFixture>.");
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
StartupGate.Wait(cancellation.Token); var budget = new Budget(60, null, cancellation.Token);
if (selected == 9)
{
    if (args.Length != 8 || args[4] != "--worker-directory" || args[6] != "--payload-directory") throw new ArgumentException("Case9 requires explicit two frozen published directory arguments.");
    return await NativeWorkerCase.RunAsync(args[1], args[5], args[7], cancellation.Token);
}
if (args.Length != 4) throw new ArgumentException("Cases1..8 do not accept native deployment parameters.");
var output = Path.GetFullPath(args[1]); SafePaths.NoLinks(output, budget);
if (Path.Exists(output)) throw new IOException("Fixture output exists."); Directory.CreateDirectory(output);
var self = ProcessObserver.Observe(Environment.ProcessId);
if (self.State != "Present" || string.IsNullOrEmpty(self.CommandLine)) throw new IOException("Fixture process identity unavailable.");
AssetTransport.WriteJson(Path.Combine(output, "self.result.json"), new LegacyReceipt { Processes = [new ProcessRecord { Pid = self.Pid, Started = self.Started, ParentPid = self.ParentPid,
    CommandLine = self.CommandLine, Platform = self.Platform, StartTicks = self.StartTicks, BootId = self.BootId }] }, ToolingJsonContext.Default.LegacyReceipt, budget);
var root = Path.Combine(output, "server-files"); Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "case-data"));
var bytes = Encoding.UTF8.GetBytes("Sezika actual fixture bytes 中文\n"); var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
var original = Path.Combine(output, "original.bin"); File.WriteAllBytes(original, bytes);
if (selected != 1) File.WriteAllBytes(Path.Combine(root, "case-data/model.bin"), bytes);
using var key = RSA.Create(2048); var privateKey = key.ExportRSAPrivateKeyPem(); var parameters = key.ExportParameters(false);
using var keyWire = new WireWriter(); keyWire.Text("ssh-rsa");
void Mpint(byte[] value) { var leading = value[0] >= 128; var buffer = new byte[value.Length + (leading ? 1 : 0)]; value.CopyTo(buffer, leading ? 1 : 0); keyWire.Bytes(buffer); CryptographicOperations.ZeroMemory(buffer); }
Mpint(parameters.Exponent!); Mpint(parameters.Modulus!);
var server = new FixtureServer(root, keyWire.ToArray());
var config = new RemoteConfiguration(1, "127.0.0.1", server.Port, FixtureServer.UserName, server.HostPin, "/fixture", "/fixture/worker", sha,
    [new("/fixture/payload", sha)], "stdin", selected == 8 ? "private-key" : "password", MaximumSeconds: 45, MaximumDirectoryEntries: 128, MaximumFileBytes: 1048576);
AssetTransport.WriteJson(Path.Combine(output, "connection-public.json"), config, RemoteJsonContext.Default.RemoteConfiguration, budget);
var secret = selected == 8 ? new RemoteSecretInput(PrivateKey: privateKey) : new RemoteSecretInput(Password: FixtureServer.Password);
var actual = "NOT_RUN"; var failure = ""; var requests = 0; var connections = 0; var accepted = 0; var publicKeyAccepted = 0; var handles = -1;
try
{
    using var session = await PinnedSession.OpenAsync(config, secret, false, budget, cancellation.Token);
    TransferRequest Request(string file, string id) => new(1, id, "case-data/" + file, bytes.Length, sha);
    static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    async Task Reject(Func<Task> action)
    {
        var rejected = false; try { await action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or Renci.SshNet.Common.SshException or OperationCanceledException) { rejected = true; }
        Assert(rejected, "Actual fixture expected rejection absent.");
    }
    switch (selected)
    {
        case 1:
        {
            var report = await SftpTransfers.UploadAsync(session, Request("model.bin", "fixture-upload"), original, budget, cancellation.Token);
            Assert(report.IndependentReadback && File.ReadAllBytes(Path.Combine(root, "case-data/model.bin")).SequenceEqual(bytes), "Actual upload physical bytes differ."); break;
        }
        case 2:
        {
            var path = Path.Combine(output, "download.bin"); var report = await SftpTransfers.DownloadAsync(session, Request("model.bin", "fixture-download"), path, budget, cancellation.Token);
            Assert(report.IndependentReadback && File.ReadAllBytes(path).SequenceEqual(bytes), "Actual complete download differs.");
            await Reject(async () => { _ = await SftpTransfers.DownloadAsync(session, Request("model.bin", "fixture-download"), path, budget, cancellation.Token); }); break;
        }
        case 3:
        {
            var request = Request("resume.bin", "fixture-resume-upload"); var remote = "/fixture/" + request.Path;
            File.WriteAllBytes(Path.Combine(root, "case-data/resume.bin." + request.OperationId + ".sezika-part"), bytes[..7]);
            AssetTransport.WriteJson(Path.Combine(root, "case-data/resume.bin." + request.OperationId + ".sezika-sftp.json"),
                new RemoteTransferState(1, request.OperationId, config.HostName, config.Port, config.HostKeySha256, remote, request.Bytes, request.Sha256, new FileInfo(original).LastWriteTimeUtc),
                RemoteJsonContext.Default.RemoteTransferState, budget);
            var report = await SftpTransfers.UploadAsync(session, request, original, budget, cancellation.Token);
            Assert(report.ResumedBytes == 7 && report.IndependentReadback && File.ReadAllBytes(Path.Combine(root, "case-data/resume.bin")).SequenceEqual(bytes), "Real upload resume differs."); break;
        }
        case 4:
        {
            var request = Request("model.bin", "fixture-resume-download"); var target = Path.Combine(output, "resumed.bin");
            var attributes = await session.AttributesAsync("/fixture/" + request.Path, false, budget, cancellation.Token) ?? throw new IOException("Fixture source absent.");
            File.WriteAllBytes(target + ".sezika-part", bytes[..9]);
            AssetTransport.WriteJson(target + ".sezika-sftp.json", new RemoteTransferState(1, request.OperationId, config.HostName, config.Port, config.HostKeySha256,
                "/fixture/" + request.Path, request.Bytes, request.Sha256, attributes.LastWriteTimeUtc), RemoteJsonContext.Default.RemoteTransferState, budget);
            var report = await SftpTransfers.DownloadAsync(session, request, target, budget, cancellation.Token);
            Assert(report.ResumedBytes == 9 && File.ReadAllBytes(target).SequenceEqual(bytes), "Real local resume differs."); break;
        }
        case 5:
        {
            var before = server.AuthenticationAccepted; var wrong = "SHA256:" + Convert.ToBase64String(SHA256.HashData("wrong-public-key"u8)).TrimEnd('=');
            await Reject(async () => { using var rejected = await PinnedSession.OpenAsync(config with { HostKeySha256 = wrong }, secret, false, budget, cancellation.Token); });
            Assert(server.AuthenticationAccepted == before, "Wrong host key sent authentication to the server."); break;
        }
        case 6:
        {
            session.Sftp.SymbolicLink("/fixture/case-data/model.bin", "/fixture/case-data/link.bin");
            await Reject(async () => { _ = await SftpTransfers.DownloadAsync(session, Request("link.bin", "fixture-link-file"), Path.Combine(output, "linked.bin"), budget, cancellation.Token); });
            session.Sftp.SymbolicLink("/fixture/case-data", "/fixture/link-directory");
            await Reject(async () => { _ = await session.AttributesAsync("/fixture/link-directory/model.bin", false, budget, cancellation.Token); });
            // SSH.NET DeleteFileAsync canonicalizes links to their targets. The fixture owns the virtual link table and reclaims it when the backend closes.
            Assert(File.ReadAllBytes(Path.Combine(root, "case-data/model.bin")).SequenceEqual(bytes), "Link rejection changed the source bytes."); break;
        }
        case 7:
        {
            var path = Path.Combine(output, "bad-sha.bin");
            await Reject(async () => { _ = await SftpTransfers.DownloadAsync(session, Request("model.bin", "fixture-bad-sha") with { Sha256 = new('0', 64) }, path, budget, cancellation.Token); });
            Assert(!File.Exists(path) && File.Exists(path + ".sezika-part"), "Invalid SHA completed or lost failure evidence.");
            session.Sftp.Disconnect(); await Reject(async () => { _ = await session.AttributesAsync("/fixture/case-data/model.bin", false, budget, cancellation.Token); }); break;
        }
        case 8:
        {
            var path = Path.Combine(output, "public-key-auth.bin");
            var report = await SftpTransfers.DownloadAsync(session, Request("model.bin", "fixture-public-key"), path, budget, cancellation.Token);
            Assert(report.IndependentReadback && server.PublicKeyAccepted > 0 && File.ReadAllBytes(path).SequenceEqual(bytes), "Actual SSH public-key authentication/readback differs."); break;
        }
    }
    actual = "PASS_actual_Csharp_loopback_ssh_sftp_case";
}
catch (Exception error)
{
    actual = "FAIL_actual_loopback_case"; failure = error.GetType().Name + ":" + error.Message;
    Console.Error.WriteLine(error.StackTrace);
}
finally
{
    try { await server.DisposeAsync(); }
    catch (Exception error) { actual = "FAIL_fixture_resource_cleanup"; failure += ";cleanup=" + error.GetType().Name; }
    finally { handles = server.RemainingHandles; }
    requests = server.Requests; connections = server.Connections; accepted = server.AuthenticationAccepted; publicKeyAccepted = server.PublicKeyAccepted;
    privateKey = "";
}
if (handles != 0 || requests > 256) { actual = "FAIL_fixture_bounds_or_resource_cleanup"; failure += ";handles=" + handles + ";requests=" + requests; }
var receipt = new FixtureReceipt(actual, selected, self.Pid, self.Started, self.ParentPid, self.StartTicks, self.BootId, config.HostName, config.Port, config.HostKeySha256,
    requests, connections, accepted, publicKeyAccepted, handles, failure, "NOT_RUN", "NOT_RUN", server.Failures, server.Diagnostics, server.OperationCounts);
AssetTransport.WriteJson(Path.Combine(output, "fixture-result.json"), receipt, FixtureJsonContext.Default.FixtureReceipt, new Budget(5, null, CancellationToken.None));
Console.WriteLine(actual + ";case=" + selected + ";requests=" + requests + ";handles=" + handles + ";error=" + failure);
return actual.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1;

internal sealed record FixtureReceipt(string Status, int Case, int Pid, DateTimeOffset? Started, int ParentPid, ulong? StartTicks, string? BootId,
    string Host, int Port, string HostKeySha256, int SftpRequests, int Connections, int AuthenticationAccepted, int PublicKeyAccepted,
    int RemainingHandles, string Failure, string RealServer, string NativeWorker, SftpOperationFailure[]? SftpFailures = null, string[]? ServerDiagnostics = null,
    SftpOperationCount[]? SftpOperationCounts = null);
[JsonSerializable(typeof(FixtureReceipt))]
internal partial class FixtureJsonContext : JsonSerializerContext;
