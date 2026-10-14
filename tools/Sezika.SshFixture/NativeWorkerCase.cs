using System.Diagnostics;
using System.Security.Cryptography;
using Sezika.RemoteTool;
using Sezika.Tooling;

namespace Sezika.SshFixture;

internal static class NativeWorkerCase
{
    internal static async Task<int> RunAsync(string output, string workerSource, string payloadSource, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Case9 validates the actual Linux native C# worker and broker; Windows protocol cases1..8 remain separate.");
        output = Path.GetFullPath(output); if (output.Split('/', StringSplitOptions.RemoveEmptyEntries).Length > 3) throw new InvalidDataException("Case9 output must use an explicitly owned shallow POSIX root (for example /tmp/unique-task) within the512 frame cap.");
        var budget = new Budget(60, null, cancellation); SafePaths.NoLinks(output, budget);
        if (Path.Exists(output)) throw new IOException("Native fixture output exists."); Directory.CreateDirectory(output);
        // Reuse this fresh owned root to keep complete native-image readback plus ancestry checks within512 frames.
        var root = output;
        var workerDirectory = Path.Combine(root, "worker"); var payloadDirectory = Path.Combine(root, "payload");
        CopyPublished(workerSource, workerDirectory, budget); CopyPublished(payloadSource, payloadDirectory, budget);
        var worker = Path.Combine(workerDirectory, "Sezika.RemoteWorker"); var payload = Path.Combine(payloadDirectory, "Sezika.ProcessFixture");
        ProcessExecutor.ValidateExecutable(worker, budget); ProcessExecutor.ValidateExecutable(payload, budget);
        File.SetUnixFileMode(worker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(payload, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var workerSha = AssetTransport.FileHash(worker, budget); var payloadSha = AssetTransport.FileHash(payload, budget);
        var workerBundle = BundleProof.Capture(workerDirectory, workerDirectory, "Sezika.RemoteWorker", RuntimeBundleKind.NativeAotImage, budget);
        var payloadBundle = BundleProof.Capture(payloadDirectory, payloadDirectory, "Sezika.ProcessFixture", RuntimeBundleKind.SelfContainedDotnet, budget);
        var nativeBytes = new FileInfo(worker).Length;
        if (nativeBytes > 32L * 1024 * 1024) throw new InvalidDataException("Actual NativeAOT worker exceeds the explicit case9 32MiB native-image bound.");
        Console.WriteLine("NativeAOT worker bytes=" + nativeBytes + ";complete payload files=" + payloadBundle.Manifest.Files.Length);
        using var rsa = RSA.Create(2048); var parameters = rsa.ExportParameters(false); using var writer = new WireWriter(); writer.Text("ssh-rsa");
        Mpint(parameters.Exponent!, writer); Mpint(parameters.Modulus!, writer);
        var server = new FixtureServer(root, writer.ToArray(), root, 512, new(worker, workerSha), workerBundle, payloadBundle);
        var config = new RemoteConfiguration(1, "127.0.0.1", server.Port, FixtureServer.UserName, server.HostPin, root, worker, workerSha,
            [new(payload, payloadSha)], "stdin", "password", MaximumSeconds: 50, MaximumDirectoryEntries: 256, MaximumFileBytes: 1048576,
            WorkerBundle: workerBundle, AllowedPayloadBundles: [payloadBundle]);
        var self = ProcessObserver.Observe(Environment.ProcessId); if (self.State != "Present" || string.IsNullOrEmpty(self.CommandLine)) throw new IOException("Native fixture process identity unavailable.");
        AssetTransport.WriteJson(Path.Combine(output, "self.result.json"), new LegacyReceipt { Processes = [new ProcessRecord { Pid = self.Pid, Started = self.Started, ParentPid = self.ParentPid,
            CommandLine = self.CommandLine, Platform = self.Platform, StartTicks = self.StartTicks, BootId = self.BootId }] }, ToolingJsonContext.Default.LegacyReceipt, budget);
        AssetTransport.WriteJson(Path.Combine(output, "connection-public.json"), config, RemoteJsonContext.Default.RemoteConfiguration, budget);
        var request = new WorkerRequest(1, "native-fixture-" + Guid.NewGuid().ToString("N"), root, worker, workerSha, config.AllowedExecutables, payload, payloadSha,
            ["success"], root, Path.Combine(root, "native-result"), 3, CleanupSeconds: 5, MaximumProcesses: 16, MaximumOutputBytes: 65536,
            WorkerBundle: workerBundle, PayloadBundle: payloadBundle);
        var status = "NOT_RUN"; var failure = ""; var handles = -1;
        try
        {
            using var session = await PinnedSession.OpenAsync(config, new(Password: FixtureServer.Password), true, budget, cancellation);
            var report = await RemoteDispatch.RunAsync(session, request, [], Path.Combine(output, "controller"), budget, cancellation);
            AssetTransport.WriteJson(Path.Combine(output, "dispatch-result.json"), report, RemoteJsonContext.Default.RemoteDispatchReport, budget);
            if (report.Status != "PASS_remote_native_execution_and_receipt" || report.ResourceReclamation != "PASS_EXACT_REMOTE_EXECUTION_RECEIPT")
                throw new InvalidOperationException("Actual native worker/receipt/reclamation chain was not accepted.");
            var receipt = AssetTransport.ReadJson(Path.Combine(output, "controller/remote-execution.json"), ExecutionJsonContext.Default.ExecutionResult, budget);
            foreach (var identity in receipt.Processes)
            {
                budget.Check(); var observation = ProcessObserver.Observe(identity.Pid);
                var differentBirth = observation.State == "Present" && observation.StartTicks is not null && observation.BootId is not null &&
                    identity.StartTicks is not null && identity.BootId is not null &&
                    (observation.StartTicks != identity.StartTicks || observation.BootId != identity.BootId);
                if (observation.State != "Absent" && !differentBirth)
                    throw new IOException("Native worker receipt process absence unproven: pid=" + identity.Pid + ";state=" + observation.State +
                        ";actualTicks=" + observation.StartTicks + ";actualBoot=" + observation.BootId + ";receiptTicks=" + identity.StartTicks + ";receiptBoot=" + identity.BootId);
            }
            status = "PASS_actual_Linux_fixed_worker_SSH_SFTP_native_cleanup";
        }
        catch (Exception error) { status = "FAIL_actual_native_worker_case"; failure = error.GetType().Name + ":" + error.Message; Console.Error.WriteLine(error.StackTrace); }
        finally
        {
            try { await server.DisposeAsync(); }
            catch (Exception error) { status = "FAIL_native_fixture_cleanup"; failure += ";cleanup=" + error.GetType().Name; }
            finally { handles = server.RemainingHandles; }
        }
        if (handles != 0 || server.Requests > 512) { status = "FAIL_native_fixture_budget_or_cleanup"; failure += ";handles=" + handles + ";frames=" + server.Requests; }
        AssetTransport.WriteJson(Path.Combine(output, "fixture-result.json"), new FixtureReceipt(status, 9, self.Pid, self.Started, self.ParentPid, self.StartTicks, self.BootId,
            config.HostName, config.Port, config.HostKeySha256, server.Requests, server.Connections, server.AuthenticationAccepted, server.PublicKeyAccepted, handles, failure, "NOT_RUN", status, server.Failures, server.Diagnostics, server.OperationCounts),
            FixtureJsonContext.Default.FixtureReceipt, new Budget(5, null, CancellationToken.None));
        Console.WriteLine(status + ";frames=" + server.Requests + ";handles=" + handles + ";error=" + failure);
        return status.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1;
    }

    private static void Mpint(byte[] value, WireWriter writer)
    {
        var leading = value[0] >= 128; var bytes = new byte[value.Length + (leading ? 1 : 0)]; value.CopyTo(bytes, leading ? 1 : 0); writer.Bytes(bytes); CryptographicOperations.ZeroMemory(bytes);
    }
    private static void CopyPublished(string source, string destination, Budget budget)
    {
        source = Path.GetFullPath(source); SafePaths.NoLinks(source, budget); if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Explicit published fixture/tool directory missing.");
        Directory.CreateDirectory(destination); var pending = new Queue<(string Source, string Target)>(); pending.Enqueue((source, destination)); var files = 0; long bytes = 0;
        for (var directory = 0; directory < 64; directory++)
        {
            budget.Check(); if (pending.Count == 0) return; var current = pending.Dequeue(); var entries = 0;
            foreach (var item in Directory.EnumerateFileSystemEntries(current.Source))
            {
                budget.Check(); if (++entries > 256 || files + pending.Count >= 512) throw new InvalidDataException("Published input inventory cap exceeded."); SafePaths.NoLinks(item, budget);
                var target = Path.Combine(current.Target, Path.GetFileName(item));
                if (Directory.Exists(item)) { Directory.CreateDirectory(target); pending.Enqueue((item, target)); }
                else
                {
                    RegularFiles.Require(item, budget); var size = new FileInfo(item).Length; bytes = checked(bytes + size); if (++files > 512 || bytes > 536870912) throw new InvalidDataException("Published input count/byte cap exceeded.");
                    var sha = AssetTransport.FileHash(item, budget); File.Copy(item, target, overwrite: false); if (AssetTransport.FileHash(target, budget) != sha) throw new InvalidDataException("Frozen published input copied bytes differ.");
                    if (OperatingSystem.IsLinux()) File.SetUnixFileMode(target, File.GetUnixFileMode(item));
                }
            }
        }
        throw new InvalidDataException("Published input directory cap exceeded.");
    }
}
