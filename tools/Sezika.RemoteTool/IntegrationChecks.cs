using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

namespace Sezika.RemoteTool;

internal static class IntegrationChecks
{
    internal static async Task<int> RunAsync(RemoteConfiguration config, RemoteInvocationInput invocation, string output, int maximumChecks,
        WorkerRequest? workerTemplate, CancellationToken cancellation)
    {
        RemotePaths.Validate(config);
        if (!IPAddress.TryParse(config.HostName, out var address) || !IPAddress.IsLoopback(address) || config.AuthenticationSource != "stdin" ||
            maximumChecks is < 1 or > 32 || invocation.Authentication is null || (invocation.StandardInput?.Length ?? 0) != 0)
            throw new InvalidDataException("Actual integration accepts only an explicitly pinned literal loopback server and memory fixture credentials.");
        output = Path.GetFullPath(output); var budget = new Budget(Math.Min(300, config.MaximumSeconds), null, cancellation); SafePaths.NoLinks(output, budget);
        if (Path.Exists(output)) throw new IOException("Integration evidence directory must be fresh."); Directory.CreateDirectory(output);
        var identity = "fixture-" + Guid.NewGuid().ToString("N"); var prefix = "protocol-fixtures/" + identity;
        using var session = await PinnedSession.OpenAsync(config, invocation.Authentication, workerTemplate is not null, budget, cancellation);
        var clock = Stopwatch.StartNew(); var names = new List<string>(); var checks = 0;
        async Task Check(string name, Func<Task> action)
        {
            if (checks >= maximumChecks) return; budget.Check(); await action(); checks++; names.Add(name); Console.WriteLine("PASS actual " + checks + ": " + name);
        }
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Reject(Func<Task> action)
        {
            var rejected = false; try { await action(); }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or Renci.SshNet.Common.SshException or OperationCanceledException) { rejected = true; }
            Assert(rejected, "Actual protocol expected rejection absent.");
        }
        var data = Encoding.UTF8.GetBytes("Sezika actual SFTP independent bytes 中文\n"); var sha = Convert.ToHexStringLower(SHA256.HashData(data));
        var local = Path.Combine(output, "original.bin"); File.WriteAllBytes(local, data);
        TransferRequest Transfer(string name, string operation) => new(1, operation, prefix + "/" + name, data.Length, sha);
        await Check("real SFTP byte upload and independent remote readback", async () =>
        {
            var actual = await SftpTransfers.UploadAsync(session, Transfer("model.bin", identity + "-upload"), local, budget, cancellation);
            Assert(actual.IndependentReadback && actual.ResumedBytes == 0, "Upload complete SHA/readback evidence differs.");
        });
        await Check("real SFTP fresh download and no overwrite", async () =>
        {
            var target = Path.Combine(output, "download.bin");
            var actual = await SftpTransfers.DownloadAsync(session, Transfer("model.bin", identity + "-download"), target, budget, cancellation);
            Assert(actual.IndependentReadback && File.ReadAllBytes(target).SequenceEqual(data), "Downloaded real bytes differ.");
            await Reject(async () => { _ = await SftpTransfers.DownloadAsync(session, Transfer("model.bin", identity + "-download"), target, budget, cancellation); });
            await Reject(async () => { _ = await SftpTransfers.UploadAsync(session, Transfer("model.bin", identity + "-upload"), local, budget, cancellation); });
        });
        await Check("real SFTP owned upload partial resumes exact bytes", async () =>
        {
            var request = Transfer("resume.bin", identity + "-resume-upload"); var remote = RemotePaths.Within(config.Root, request.Path);
            var part = remote + "." + request.OperationId + ".sezika-part"; var state = remote + "." + request.OperationId + ".sezika-sftp.json";
            await using (var stream = await session.Sftp.OpenAsync(part, FileMode.CreateNew, FileAccess.Write, cancellation)) { await stream.WriteAsync(data.AsMemory(0, 7), cancellation); await stream.FlushAsync(cancellation); }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new RemoteTransferState(1, request.OperationId, config.HostName, config.Port, config.HostKeySha256,
                remote, request.Bytes, request.Sha256, new FileInfo(local).LastWriteTimeUtc), RemoteJsonContext.Default.RemoteTransferState);
            await using (var stream = await session.Sftp.OpenAsync(state, FileMode.CreateNew, FileAccess.Write, cancellation)) { await stream.WriteAsync(bytes, cancellation); await stream.FlushAsync(cancellation); }
            var actual = await SftpTransfers.UploadAsync(session, request, local, budget, cancellation); Assert(actual.ResumedBytes == 7 && actual.IndependentReadback, "Remote partial resume differs.");
        });
        await Check("real SFTP owned local download partial resumes", async () =>
        {
            var request = Transfer("model.bin", identity + "-resume-download"); var remote = RemotePaths.Within(config.Root, request.Path);
            var attributes = await session.AttributesAsync(remote, false, budget, cancellation) ?? throw new IOException("Fixture source disappeared.");
            var target = Path.Combine(output, "resumed-download.bin"); File.WriteAllBytes(target + ".sezika-part", data[..9]);
            AssetTransport.WriteJson(target + ".sezika-sftp.json", new RemoteTransferState(1, request.OperationId, config.HostName, config.Port, config.HostKeySha256,
                remote, request.Bytes, request.Sha256, attributes.LastWriteTimeUtc), RemoteJsonContext.Default.RemoteTransferState, budget);
            var actual = await SftpTransfers.DownloadAsync(session, request, target, budget, cancellation);
            Assert(actual.ResumedBytes == 9 && File.ReadAllBytes(target).SequenceEqual(data), "Local partial resume differs.");
        });
        await Check("real SSH changed host key rejected before authentication", async () =>
        {
            var badPin = "SHA256:" + Convert.ToBase64String(SHA256.HashData("wrong-fixture-key"u8)).TrimEnd('=');
            await Reject(async () => { using var rejected = await PinnedSession.OpenAsync(config with { HostKeySha256 = badPin }, invocation.Authentication, false, budget, cancellation); });
        });
        await Check("real SFTP symbolic asset and ancestor rejected", async () =>
        {
            var source = RemotePaths.Within(config.Root, prefix + "/model.bin"); var link = RemotePaths.Within(config.Root, prefix + "/linked.bin");
            budget.Check(); session.Sftp.SymbolicLink(source, link);
            await Reject(async () => { _ = await SftpTransfers.DownloadAsync(session, Transfer("linked.bin", identity + "-link"), Path.Combine(output, "linked-download.bin"), budget, cancellation); });
            var directoryLink = RemotePaths.Within(config.Root, prefix + "/linked-directory"); session.Sftp.SymbolicLink(RemotePaths.Within(config.Root, prefix), directoryLink);
            await Reject(async () => { _ = await session.AttributesAsync(directoryLink + "/model.bin", false, budget, cancellation); });
            // Do not use a canonicalizing delete API on links: it would remove source targets. These exact link fixtures are retained as evidence in the owned fixture namespace.
        });
        await Check("real SFTP untrusted full SHA cannot complete destination", async () =>
        {
            var target = Path.Combine(output, "wrong-sha.bin");
            await Reject(async () => { _ = await SftpTransfers.DownloadAsync(session, Transfer("model.bin", identity + "-wrong-sha") with { Sha256 = new('0', 64) }, target, budget, cancellation); });
            Assert(!File.Exists(target) && File.Exists(target + ".sezika-part"), "Mismatched full SHA promoted destination or erased failed bytes.");
        });
        await Check("real SFTP disconnected channel does not claim completion", async () =>
        {
            using var disconnected = await PinnedSession.OpenAsync(config, invocation.Authentication, false, budget, cancellation); disconnected.Sftp.Disconnect();
            var target = Path.Combine(output, "disconnected.bin");
            await Reject(async () => { _ = await SftpTransfers.DownloadAsync(disconnected, Transfer("model.bin", identity + "-disconnected"), target, budget, cancellation); });
            Assert(!File.Exists(target), "Disconnected channel claimed a completed download.");
        });
        if (workerTemplate is not null)
        {
            RemoteWorker.Validate(workerTemplate, budget, config.Root);
            if (Path.GetFileName(workerTemplate.Executable) != "Sezika.ProcessFixture" || !workerTemplate.Arguments.SequenceEqual(["success"]))
                throw new InvalidDataException("Loopback worker integration requires the frozen native Sezika.ProcessFixture success probe; no GPU or business program is admitted.");
            await Check("real SSH fixed C# worker and native lifecycle receipt", async () =>
            {
                var request = workerTemplate with { RequestId = identity + "-native", OutputDirectory = RemotePaths.Within(config.Root, prefix + "/native-run"), TimeoutSeconds = 3, CleanupSeconds = 5 };
                var report = await RemoteDispatch.RunAsync(session, request, [], Path.Combine(output, "native-worker"), budget, cancellation);
                Assert(report.Status == "PASS_remote_native_execution_and_receipt" && report.ResourceReclamation == "PASS_EXACT_REMOTE_EXECUTION_RECEIPT", "Native worker output/reclamation evidence differs.");
            });
            await Check("real SSH native timeout remains failure with complete cleanup", async () =>
            {
                var request = workerTemplate with { RequestId = identity + "-timeout", Arguments = ["sleep"], OutputDirectory = RemotePaths.Within(config.Root, prefix + "/timeout-run"), TimeoutSeconds = 1, CleanupSeconds = 5 };
                var report = await RemoteDispatch.RunAsync(session, request, [], Path.Combine(output, "timeout-worker"), budget, cancellation);
                Assert(report.Status.StartsWith("FAIL", StringComparison.Ordinal) && report.ResourceReclamation == "PASS_EXACT_REMOTE_EXECUTION_RECEIPT", "Native timeout failure or cleanup evidence lost.");
            });
        }
        File.WriteAllLines(Path.Combine(output, "checks.txt"), names.Append("PASS_actual_loopback_protocol_only; checks=" + checks + "; seconds=" + clock.Elapsed.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
            "; endpoint=" + config.HostName + ":" + config.Port + "; fixtures retained in explicit remote scope " + prefix + "; real server/model/GPU deployment NOT_RUN"));
        return checks > 0 ? 0 : 1;
    }
}
