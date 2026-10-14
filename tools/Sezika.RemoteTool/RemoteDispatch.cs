using System.Diagnostics;
using System.Text.Json;
using Renci.SshNet;
using Sezika.Tooling;

namespace Sezika.RemoteTool;

internal static class RemoteDispatch
{
    internal static async Task<RemoteDispatchReport> RunAsync(PinnedSession session, WorkerRequest request, byte[] input,
        string logs, Budget budget, CancellationToken cancellation)
    {
        var config = session.Configuration; RemoteWorker.Validate(request, budget, config.Root);
        if (request.WorkerPath != config.WorkerPath || request.WorkerSha256 != config.WorkerSha256 || request.StandardInputBytes != input.Length ||
            request.AllowedExecutables.Any(p => !config.AllowedExecutables.Contains(p))) throw new InvalidDataException("Dispatch request/native allowlist/input differs from configured worker scope.");
        BundleProof.ValidateBinding(config.WorkerBundle, config.WorkerPath, config.WorkerSha256, config.Root, budget, nativeWorker: true);
        if (config.WorkerBundle!.Sha256 != request.WorkerBundle!.Sha256 || config.AllowedPayloadBundles is null ||
            !config.AllowedPayloadBundles.Any(pin => pin.Sha256 == request.PayloadBundle!.Sha256))
            throw new InvalidDataException("Dispatch complete worker/payload bundle differs from the configured frozen manifest.");
        foreach (var pin in config.AllowedPayloadBundles)
        {
            budget.Check(); BundleProof.Validate(pin, budget);
            if (!config.AllowedExecutables.Any(executable => executable.Path == pin.Manifest.Directory + "/" + pin.Manifest.EntryPoint &&
                executable.Sha256 == pin.Manifest.Files.Single(f => f.RelativePath == pin.Manifest.EntryPoint).Sha256))
                throw new InvalidDataException("Configured payload bundle does not match an allowed executable identity.");
        }
        logs = Path.GetFullPath(logs); SafePaths.NoLinks(logs, budget);
        if (Path.Exists(logs)) throw new IOException("Controller evidence directory exists."); Directory.CreateDirectory(logs);
        var workerBytes = config.WorkerBundle.Manifest.Files.Single(f => f.RelativePath == config.WorkerBundle.Manifest.EntryPoint).Bytes;
        if (await SftpTransfers.HashRemoteAsync(session, config.WorkerPath, workerBytes, budget, cancellation) != config.WorkerSha256)
            throw new InvalidDataException("Controller's independent complete NativeAOT worker byte identity differs before SSH exec.");
        var localRequest = Path.Combine(logs, "worker-request.json"); AssetTransport.WriteJson(localRequest, request, RemoteJsonContext.Default.WorkerRequest, budget);
        var requestSha = AssetTransport.FileHash(localRequest, budget); var relative = "requests/" + request.RequestId + ".json";
        _ = await SftpTransfers.UploadAsync(session, new(1, request.RequestId, relative, new FileInfo(localRequest).Length, requestSha), localRequest, budget, cancellation);
        var remoteRequest = RemotePaths.Within(config.Root, relative); var remoteReceipt = request.OutputDirectory + "/worker-result.json";
        using var command = session.Ssh.CreateCommand(RemotePaths.Command(config, remoteRequest, requestSha));
        command.CommandTimeout = TimeSpan.FromSeconds(Math.Max(1, Math.Min(config.MaximumSeconds, budget.RemainingSeconds)));
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellation); work.CancelAfter(TimeSpan.FromSeconds(Math.Max(.01, Math.Min(config.MaximumSeconds, budget.RemainingSeconds))));
        var clock = Stopwatch.StartNew(); long outputBytes = 0, errorBytes = 0;
        var cancellationSignal = "NOT_NEEDED"; var remoteState = "UNKNOWN"; string? receiptSha = null;
        var execution = command.ExecuteAsync(work.Token);
        var sending = SendInputAsync(command, input, work.Token);
        var stdout = DrainAsync(command.OutputStream, Path.Combine(logs, "stdout.bin"), request.MaximumOutputBytes, budget, work.Token);
        var stderr = DrainAsync(command.ExtendedOutputStream, Path.Combine(logs, "stderr.bin"), request.MaximumOutputBytes, budget, work.Token);
        try
        {
            var completed = false;
            for (var poll = 0; poll < config.MaximumSeconds * 10 + 1; poll++)
            {
                budget.Check();
                if (sending.IsFaulted) await sending;
                if (stdout.IsFaulted) await stdout;
                if (stderr.IsFaulted) await stderr;
                if (execution.IsCompleted)
                {
                    await execution; await sending;
                    outputBytes = await stdout.WaitAsync(TimeSpan.FromSeconds(3), work.Token); errorBytes = await stderr.WaitAsync(TimeSpan.FromSeconds(3), work.Token);
                    completed = true; break;
                }
                if (clock.Elapsed.TotalSeconds >= config.MaximumSeconds) throw new TimeoutException("Remote SSH aggregate deadline exceeded.");
                await Task.Delay(100, work.Token);
            }
            if (!completed) throw new TimeoutException("Remote SSH poll count exceeded.");
        }
        catch
        {
            // Signal only this request's exact CANCEL file. SSH channel closure alone cannot prove payload descendants were reclaimed.
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var cleanupBudget = new Budget(10, null, cleanup.Token);
                var path = request.OutputDirectory + ".cancel";
                if (await session.AttributesAsync(path, true, cleanupBudget, cleanup.Token) is null)
                {
                    await using var marker = await session.Sftp.OpenAsync(path, FileMode.CreateNew, FileAccess.Write, cleanup.Token);
                    await marker.WriteAsync("cancel"u8.ToArray(), cleanup.Token); await marker.FlushAsync(cleanup.Token);
                }
                cancellationSignal = "REQUESTED_EXACT_WORKER_CANCEL_FILE";
            }
            catch { cancellationSignal = "UNKNOWN_REMOTE_CANCEL_DELIVERY"; }
            work.Cancel(); command.Dispose();
            foreach (var task in new Task[] { execution, sending, stdout, stderr })
            {
                try { await task.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None); } catch { }
            }
            AssetTransport.WriteJson(Path.Combine(logs, "dispatch-failure.json"), new RemoteDispatchReport("FAIL_REMOTE_STATE_REQUIRES_READBACK", request.RequestId,
                config.HostName, config.Port, config.HostKeySha256, command.ExitStatus, outputBytes, errorBytes, remoteReceipt, null, "UNKNOWN", "NOT_RUN", cancellationSignal),
                RemoteJsonContext.Default.RemoteDispatchReport, new Budget(5, null, CancellationToken.None));
            throw;
        }
        var inspected = await SftpTransfers.InspectAndHashRemoteAsync(session, remoteReceipt, 1048576, budget, cancellation);
        receiptSha = inspected.Sha256;
        var localReceipt = Path.Combine(logs, "remote-worker.json");
        _ = await SftpTransfers.DownloadAsync(session, new(1, request.RequestId, remoteReceipt[(config.Root.Length + 1)..], inspected.Bytes, receiptSha), localReceipt, budget, cancellation);
        var bound = AssetTransport.ReadJson(localReceipt, RemoteJsonContext.Default.WorkerResult, budget);
        if (bound.RequestId != request.RequestId || bound.RequestSha256 != requestSha || bound.WorkerBundleSha256 != request.WorkerBundle!.Sha256 ||
            bound.PayloadBundleSha256 != request.PayloadBundle!.Sha256 || bound.BundleValidation != "PASS_COMPLETE_CODE_RUNTIME_BUNDLES_BEFORE_AND_AFTER")
            throw new InvalidDataException("Actual native-worker receipt request/full-code/runtime-bundle binding differs.");
        var result = bound.Execution;
        var executionFile = Path.Combine(logs, "remote-execution.json");
        AssetTransport.WriteJson(executionFile, result, ExecutionJsonContext.Default.ExecutionResult, budget);
        if (AssetTransport.FileHash(executionFile, budget) != bound.ExecutionReceiptSha256)
            throw new InvalidDataException("Independently extracted execution receipt complete byte/SHA differs.");
        ValidateReceipt(request, result, remoteRequest, requestSha, budget);
        remoteState = result.CleanupStatus == "PASS" && result.RemainingProcesses == 0 ? "PASS_EXACT_REMOTE_EXECUTION_RECEIPT" : "UNKNOWN";
        var status = command.ExitStatus == 0 && result.Status == "PASS" && bound.Status == result.Status && result.ExecutionStatus == "SUCCEEDED" && result.ExitCode == 0 &&
            remoteState.StartsWith("PASS", StringComparison.Ordinal) ? "PASS_remote_native_execution_and_receipt" : "FAIL_remote_native_execution_receipt";
        return new(status, request.RequestId, config.HostName, config.Port, config.HostKeySha256, command.ExitStatus, outputBytes, errorBytes,
            remoteReceipt, receiptSha, remoteState, "PASS_FULL_RECEIPT_BYTE_SHA", cancellationSignal, bound.WorkerBundleSha256, bound.PayloadBundleSha256);
    }

    internal static void ValidateReceipt(WorkerRequest request, ExecutionResult result, string requestPath, string requestSha256, Budget budget)
    {
        budget.Check(); RemotePaths.Absolute(requestPath); RemotePaths.Sha(requestSha256);
        if (!RemotePaths.IsWithin(request.Root, requestPath) || result.SchemaVersion != 1 || result.Platform != "linux" ||
            result.WorkingDirectory != request.WorkingDirectory || result.OutputDirectory != request.OutputDirectory || result.Executable != request.Executable ||
            result.ArgumentList is null || !result.ArgumentList.SequenceEqual(request.Arguments) || result.Processes is not { Length: > 0 } ||
            result.Processes.Length > request.MaximumProcesses || result.RootPid is not > 0 || result.Errors is null || result.Errors.Length != 0 ||
            !result.InputComplete || !result.OutputComplete || result.OutputLimitExceeded || result.StandardInputBytes != request.StandardInputBytes ||
            !Guid.TryParseExact(result.OwnershipId, "N", out _))
            throw new InvalidDataException("Remote worker receipt schema/platform/payload/path/input/output/process scope differs.");

        static bool CompleteBirth(ExecutionIdentity? identity) => identity is not null && identity.Pid > 0 && identity.ParentPid > 0 &&
            identity.Platform == "linux" && identity.StartTicks is > 0 && Guid.TryParseExact(identity.BootId, "D", out _) &&
            !string.IsNullOrEmpty(identity.CommandLine) && identity.CommandLine.Length <= 65536 && identity.PendingObservationError is null &&
            identity.State is "REGISTERED" or "EXITED" or "EXITED_PENDING_REAP";
        var launcher = result.Launcher;
        var workerCommand = string.Join('\0', new[] { request.WorkerPath, "worker", "--request", requestPath, "--sha256", requestSha256 }) + '\0';
        if (launcher is null || !CompleteBirth(launcher) || launcher.CommandLine != workerCommand)
            throw new InvalidDataException("Remote execution launcher fixed worker argv/complete Linux birth is unproven.");

        var identities = new Dictionary<int, ExecutionIdentity>();
        foreach (var identity in result.Processes)
        {
            budget.Check();
            if (!CompleteBirth(identity) || identity.Pid == launcher.Pid || identity.BootId != launcher.BootId || identity.StartTicks < launcher.StartTicks ||
                identity.ParentChain is not { Length: > 0 and <= 64 } || identity.ParentChain[0] != launcher.Pid || identity.ParentChain[^1] != identity.ParentPid ||
                identity.ParentChain.Any(pid => pid <= 0 || pid == identity.Pid) || identity.ParentChain.Distinct().Count() != identity.ParentChain.Length ||
                !Guid.TryParseExact(identity.ScopeId, "N", out _) || !identities.TryAdd(identity.Pid, identity))
                throw new InvalidDataException("Remote execution process duplicate PID/complete Linux birth/parent chain is unproven.");
        }
        if (!identities.TryGetValue(result.RootPid.Value, out var root)) throw new InvalidDataException("Remote execution root PID is absent from its complete identity set.");
        var payloadCommand = string.Join('\0', new[] { request.Executable }.Concat(request.Arguments)) + '\0';
        var brokerCommand = string.Join('\0', new[] { request.WorkerPath, "process", "broker", request.OutputDirectory + "/broker.json" }) + '\0';
        if (root.ParentPid != launcher.Pid || !root.ParentChain.SequenceEqual([launcher.Pid]) || root.ScopeId != result.OwnershipId ||
            root.ExpectedExecCommandLine != payloadCommand || root.CommandLine != brokerCommand && root.CommandLine != payloadCommand ||
            root.AfterExecCommandLine is not null && root.AfterExecCommandLine != payloadCommand)
            throw new InvalidDataException("Remote execution root launcher/broker/payload argv/ownership binding differs.");
        foreach (var identity in result.Processes)
        {
            budget.Check(); if (identity.Pid == root.Pid) continue;
            if (!identities.TryGetValue(identity.ParentPid, out var parent) || !identity.ParentChain.SequenceEqual(parent.ParentChain.Append(parent.Pid)))
                throw new InvalidDataException("Remote execution descendant parent chain is outside its frozen identity set.");
        }
    }

    private static async Task SendInputAsync(SshCommand command, byte[] bytes, CancellationToken cancellation)
    {
        using var input = command.CreateInputStream(); if (bytes.Length > 0) await input.WriteAsync(bytes, cancellation); await input.FlushAsync(cancellation);
    }
    private static async Task<long> DrainAsync(Stream source, string path, int maximumBytes, Budget budget, CancellationToken cancellation)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); var buffer = new byte[16384]; long total = 0;
        for (var readIndex = 0; readIndex <= maximumBytes; readIndex++)
        {
            budget.Check(); var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximumBytes - total + 1)), cancellation);
            if (read == 0) { output.Flush(flushToDisk: true); return total; }
            if (total + read > maximumBytes) throw new InvalidDataException("Remote SSH output byte cap exceeded.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellation); total += read;
        }
        throw new InvalidDataException("Remote SSH output read iteration cap exceeded.");
    }
}
