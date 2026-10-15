using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.RemoteTool;
using Sezika.Tooling;

namespace Sezika.SshFixture;

internal sealed record CrossOsFilePin(string Path, long Bytes, string Sha256);
internal sealed record CrossOsServerRequest(int SchemaVersion, string SessionId, string Root, string WorkerDirectory,
    string PayloadDirectory, BundlePin WorkerBundle, BundlePin PayloadBundle, string ReadyPath, string StopFile)
{
    [JsonRequired] public string ProcessEvidenceDirectory { get; init; } = "";
}
internal sealed record CrossOsClientRequest(int SchemaVersion, CrossOsFilePin ServerRequest, CrossOsFilePin Ready,
    string StopFile, string ServerResultPath, string LogsDirectory)
{
    [JsonRequired] public string ProcessEvidenceDirectory { get; init; } = "";
}
internal sealed record CrossOsIdentity(int Pid, int ParentPid, int[] ParentChain, string Platform,
    DateTimeOffset? Started, ulong? StartTicks, string? BootId, string CommandLine)
{
    public string Scope { get; init; } = "UNKNOWN_unregistered";
    public string? ScopeId { get; init; }
    public CrossOsFilePin? Registration { get; init; }
    public CrossOsFilePin? OwnershipEvidence { get; init; }
    public string? RegisteredCommandLine { get; init; }
    public string? ExpectedExecCommandLine { get; init; }
}
internal sealed record CrossOsReady(string Status, int Case, string SessionId, string ServerRequestSha256,
    CrossOsIdentity Server, RemoteConfiguration Configuration, WorkerRequest Request, string StopFile, string ServerResultPath);
internal sealed record CrossOsBirth(int Pid, ulong? StartTicks, string? BootId, string Status, string ReceiptState);
internal sealed record CrossOsServerReport(string Status, int Case, string? SessionId, CrossOsFilePin? ServerRequest,
    CrossOsIdentity? Server, int SftpRequests, int Connections, int AuthenticationAccepted, int RemainingHandles,
    string StopReason, string? Failure, CrossOsFilePin? NativeWorkerReceipt, CrossOsBirth[] NativeProcessReadback,
    SftpOperationFailure[] SftpFailures, string[] ServerDiagnostics, SftpOperationCount[] SftpOperationCounts,
    bool FixtureOnly = true, string OuterProcessAudit = "REQUIRED_SEPARATE_ROOT_EXECUTION_RECEIPT");
internal sealed record CrossOsCheck(string Name, string Status, string? ExpectedGate = null, string? ObservedGate = null);
internal sealed record CrossOsIdentityNegativeCheck(string Name, string Status, string FixtureDirectory, string Mutation,
    string ExpectedGate, string? ObservedGate = null, string? ObservedError = null,
    CrossOsFilePin? Registration = null, CrossOsFilePin? OwnershipEvidence = null);
internal sealed record CrossOsClientReport(string Status, int Case, int Planned, CrossOsCheck[] Checks,
    CrossOsIdentity? Controller, CrossOsFilePin? ClientRequest, CrossOsFilePin? ServerRequest, CrossOsFilePin? Ready,
    CrossOsFilePin? ServerResult, CrossOsServerReport? Server, RemoteDispatchReport? Dispatch, string StopDelivery,
    CrossOsFilePin? ControllerWorkerRequest, CrossOsFilePin? DownloadedNativeReceipt, CrossOsFilePin? ExtractedExecution,
    string? Failure, [property: JsonPropertyName("fixture_only")] bool FixtureOnly = true,
    bool UpstreamExecuted = false, bool RepresentsQuality = false, bool RepresentsParity = false,
    string OuterProcessAudit = "REQUIRED_SEPARATE_WINDOWS_AND_LINUX_ROOT_EXECUTION_RECEIPTS")
{
    public CrossOsIdentityNegativeCheck[] IdentityNegativeChecks { get; init; } = [];
    public int IdentityNegativePlanned { get; init; }
    public string IdentityNegativeStatus { get; init; } = "NOT_RUN";
    public string IdentityNegativeOriginalInputsStatus { get; init; } = "NOT_RUN";
    public CrossOsFilePin? IdentityNegativeRegistrationAfter { get; init; }
    public CrossOsFilePin? IdentityNegativeOwnershipAfter { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(CrossOsServerRequest))]
[JsonSerializable(typeof(CrossOsClientRequest))]
[JsonSerializable(typeof(CrossOsReady))]
[JsonSerializable(typeof(CrossOsServerReport))]
[JsonSerializable(typeof(CrossOsClientReport))]
internal partial class CrossOsJson : JsonSerializerContext;

// Case10 has a real Windows SSH.NET controller and a separate Linux fixture/native worker.
// Root owns launchers, native process trees, new immutable builds, and the final outer audit.
internal static class Checks
{
    internal static async Task CheckUploadPreflightAsync(int selected, PinnedSession session, string root, string original,
        byte[] bytes, string sha, Budget budget, CancellationToken cancellation)
    {
        if (selected is not (1 or 3 or 6)) return;
        async Task RejectUpload(TransferRequest request, string expectedMessage, bool invalidData)
        {
            var rejected = false;
            try { _ = await SftpTransfers.UploadAsync(session, request, original, budget, cancellation); }
            catch (InvalidDataException error) when (invalidData && error.Message == expectedMessage) { rejected = true; }
            catch (IOException error) when (!invalidData && error.Message == expectedMessage) { rejected = true; }
            Require(rejected, "Actual upload preflight expected rejection is absent or differs.");
        }
        if (selected is 1 or 3)
        {
            var name = selected == 1 ? "model.bin" : "resume.bin";
            var operation = "fixture-preflight-existing-" + selected;
            var request = new TransferRequest(1, operation, "case-data/" + name, bytes.Length, sha);
            await RejectUpload(request, "Remote completed destination exists; preserve it.", false);
            var actual = Path.Combine(root, "case-data", name);
            Require(File.ReadAllBytes(actual).SequenceEqual(bytes), "Existing completed upload bytes changed after rejection.");
            Require(!Path.Exists(actual + "." + operation + ".sezika-part") && !Path.Exists(actual + "." + operation + ".sezika-sftp.json"),
                "Rejected overwrite created or replaced upload state.");
            if (selected == 3)
                Require(!Path.Exists(actual + ".fixture-resume-upload.sezika-part") && !Path.Exists(actual + ".fixture-resume-upload.sezika-sftp.json"),
                    "Successful upload resume retained a promoted partial or its state.");
            return;
        }
        // Existing case6 already owns real virtual symlinks; these two additional leaves exercise the batched upload preflight.
        for (var index = 0; index < 2; index++)
        {
            budget.Check(); var kind = index == 0 ? "partial" : "state";
            var operation = "fixture-preflight-link-" + kind; var name = "preflight-link-" + kind + ".bin";
            var request = new TransferRequest(1, operation, "case-data/" + name, bytes.Length, sha);
            var remote = RemotePaths.Within(session.Configuration.Root, request.Path);
            var suffix = index == 0 ? ".sezika-part" : ".sezika-sftp.json";
            session.Sftp.SymbolicLink(RemotePaths.Within(session.Configuration.Root, "case-data/model.bin"), remote + "." + operation + suffix);
            await RejectUpload(request, "Remote links, devices, sockets and FIFOs are rejected.", true);
            Require(!File.Exists(Path.Combine(root, "case-data", name)) && File.ReadAllBytes(Path.Combine(root, "case-data/model.bin")).SequenceEqual(bytes),
                "Upload link rejection promoted a destination or changed the link target.");
        }
    }

    private const string MetadataGate = "Bundle schema/count/entry/complete-manifest SHA differs.";

    internal static async Task<int> ServeAsync(string requestPath, string outputPath, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(120)); var budget = new Budget(120, null, deadline.Token);
        CrossOsFilePin? requestPin = null; CrossOsServerRequest? request = null;
        CrossOsIdentity? self = null; FixtureServer? server = null;
        string status = "NOT_RUN", reason = "NOT_RUN"; string? failure = null;
        CrossOsFilePin? nativePin = null; CrossOsBirth[] births = [];
        try
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Cross-OS fixture server requires Linux.");
            requestPin = Pin(requestPath, budget); request = Read(requestPin, CrossOsJson.Default.CrossOsServerRequest, budget);
            self = Identity(request.ProcessEvidenceDirectory, budget);
            ValidateServer(request, outputPath, budget);
            // Each whole published source and its copy must match the supplied immutable manifest.
            CopyBundle(request.WorkerDirectory, request.WorkerBundle, budget);
            CopyBundle(request.PayloadDirectory, request.PayloadBundle, budget);
            var worker = request.WorkerBundle.Manifest.Directory + "/" + request.WorkerBundle.Manifest.EntryPoint;
            var payload = request.PayloadBundle.Manifest.Directory + "/" + request.PayloadBundle.Manifest.EntryPoint;
            File.SetUnixFileMode(worker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(payload, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            ProcessExecutor.ValidateExecutable(worker, budget); ProcessExecutor.ValidateExecutable(payload, budget);
            var workerSha = Entry(request.WorkerBundle).Sha256; var payloadSha = Entry(request.PayloadBundle).Sha256;
            using var rsa = RSA.Create(2048); using var publicKey = new WireWriter(); publicKey.Text("ssh-rsa");
            var parameters = rsa.ExportParameters(false); Mpint(parameters.Exponent!, publicKey); Mpint(parameters.Modulus!, publicKey);
            server = new FixtureServer(request.Root, publicKey.ToArray(), request.Root, 512, new(worker, workerSha),
                request.WorkerBundle, request.PayloadBundle, maximumSeconds: 120);
            var configuration = new RemoteConfiguration(1, "127.0.0.1", server.Port, FixtureServer.UserName, server.HostPin,
                request.Root, worker, workerSha, [new(payload, payloadSha)], "stdin", "password", MaximumSeconds: 50,
                MaximumDirectoryEntries: 256, MaximumFileBytes: 1048576, WorkerBundle: request.WorkerBundle,
                AllowedPayloadBundles: [request.PayloadBundle]);
            var work = new WorkerRequest(1, "crossos-" + request.SessionId, request.Root, worker, workerSha,
                configuration.AllowedExecutables, payload, payloadSha, ["success"], request.Root, request.Root + "/native-result",
                3, CleanupSeconds: 5, MaximumProcesses: 16, MaximumOutputBytes: 65536,
                WorkerBundle: request.WorkerBundle, PayloadBundle: request.PayloadBundle);
            RemoteWorker.Validate(work, budget, request.Root);
            AssetTransport.WriteJson(request.ReadyPath, new CrossOsReady("READY_linux_native_crossos_fixture", 10,
                request.SessionId, requestPin.Sha256, self, configuration, work, request.StopFile, outputPath),
                CrossOsJson.Default.CrossOsReady, budget);
            Console.WriteLine("crossos-server READY session=" + request.SessionId + " port=" + server.Port);
            var stopped = false;
            for (var poll = 0; poll < 2400; poll++)
            {
                budget.Check();
                if (File.Exists(request.StopFile))
                {
                    RegularFiles.Require(request.StopFile, budget);
                    if (new FileInfo(request.StopFile).Length != 32 || File.ReadAllText(request.StopFile) != request.SessionId)
                        throw new InvalidDataException("Cross-OS stop marker must bind this exact session.");
                    stopped = true; break;
                }
                await Task.Delay(50, deadline.Token);
            }
            if (!stopped) throw new TimeoutException("Cross-OS server poll count2400 exceeded.");
            reason = "EXACT_SESSION_STOP_FILE";
            status = "PENDING_native_receipt_and_server_cleanup";
        }
        catch (Exception error) when (ExpectedFailure(error)) { status = "FAIL_crossos_linux_server"; failure = Error(error); }
        finally
        {
            if (server is not null)
            {
                try { await server.DisposeAsync(); }
                catch (Exception error) when (ExpectedFailure(error) || error is AggregateException)
                { status = "FAIL_crossos_server_cleanup"; failure = Error(error); }
            }
        }
        // Separate bounded final proof survives a normal stop or expired serving deadline.
        var proof = new Budget(10, null, CancellationToken.None);
        if (status == "PENDING_native_receipt_and_server_cleanup" && request is not null)
        {
            try
            {
                var nativePath = request.Root + "/native-result/worker-result.json"; nativePin = Pin(nativePath, proof);
                var result = Read(nativePin, RemoteJsonContext.Default.WorkerResult, proof);
                var uploadedPath = request.Root + "/requests/crossos-" + request.SessionId + ".json";
                var uploaded = AssetTransport.ReadJson(uploadedPath, RemoteJsonContext.Default.WorkerRequest, proof);
                Require(uploaded.MaximumProcesses == 16 && uploaded.TimeoutSeconds == 3 && uploaded.CleanupSeconds == 5 &&
                    uploaded.MaximumOutputBytes == 65536 && uploaded.StandardInputBytes == 0 && uploaded.Arguments.SequenceEqual(["success"]),
                    "Cross-OS uploaded native request must retain its fixed payload/resource bounds.");
                Require(result.RequestId == uploaded.RequestId && result.RequestSha256 == AssetTransport.FileHash(uploadedPath, proof) &&
                    result.WorkerBundleSha256 == request.WorkerBundle.Sha256 && result.PayloadBundleSha256 == request.PayloadBundle.Sha256 &&
                    result.BundleValidation == "PASS_COMPLETE_CODE_RUNTIME_BUNDLES_BEFORE_AND_AFTER", "Cross-OS server native request/full-bundle receipt binding differs.");
                RemoteDispatch.ValidateReceipt(uploaded, result.Execution, uploadedPath, result.RequestSha256, proof);
                RequireExecution(result.Execution);
                Require(result.ExecutionReceiptSha256 == AssetTransport.FileHash(request.Root + "/native-result/result.json", proof),
                    "Cross-OS server original execution file SHA differs.");
                BundleProof.VerifyLocal(request.WorkerBundle, proof); BundleProof.VerifyLocal(request.PayloadBundle, proof);
                births = Readback(result.Execution, proof);
                Require(births.All(item => item.Status.StartsWith("PASS_", StringComparison.Ordinal)), "Cross-OS native process original birth remains present or UNKNOWN.");
                Require(server is not null && server.RemainingHandles == 0 && server.Requests <= 512 && server.Failures.Length == 0 && server.Diagnostics.Length == 0,
                    "Cross-OS server SFTP512/handles/diagnostics gate differs.");
                status = "PASS_actual_linux_crossos_server_native_receipt_and_cleanup";
            }
            catch (Exception error) when (ExpectedFailure(error)) { status = "FAIL_crossos_linux_final_proof"; failure = Error(error); }
        }
        try { if (requestPin is not null) Verify(requestPin, proof); }
        catch (Exception error) when (ExpectedFailure(error)) { status = "FAIL_crossos_server_input_recheck"; failure = (failure is null ? "" : failure + ";") + Error(error); }
        var report = new CrossOsServerReport(status, 10, request?.SessionId, requestPin, self, server?.Requests ?? 0,
            server?.Connections ?? 0, server?.AuthenticationAccepted ?? 0, server?.RemainingHandles ?? 0, reason, failure,
            nativePin, births, server?.Failures ?? [], server?.Diagnostics ?? [], server?.OperationCounts ?? []);
        try { AssetTransport.WriteJson(outputPath, report, CrossOsJson.Default.CrossOsServerReport, proof); }
        catch (Exception error) when (ExpectedFailure(error))
        { Console.Error.WriteLine("FAIL_crossos_server_structured_report:" + Error(error)); return 1; }
        Console.WriteLine(status + ";frames=" + report.SftpRequests + ";handles=" + report.RemainingHandles);
        return status.StartsWith("PASS_", StringComparison.Ordinal) ? 0 : 1;
    }

    internal static async Task<int> RunAsync(string requestPath, string outputPath, int maximumChecks, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(60)); var budget = new Budget(60, null, deadline.Token);
        CrossOsFilePin? requestPin = null; CrossOsClientRequest? request = null;
        CrossOsServerRequest? source = null; CrossOsReady? ready = null; CrossOsIdentity? self = null;
        var trustedSession = false;
        var checks = new List<CrossOsCheck>(2); RemoteDispatchReport? dispatch = null;
        var identityNegatives = new List<CrossOsIdentityNegativeCheck>(4);
        var identityNegativeStatus = maximumChecks == 1 ? "NOT_RUN_tiny1" : "NOT_RUN";
        var identityOriginalInputsStatus = identityNegativeStatus;
        CrossOsFilePin? identityRegistrationAfter = null, identityOwnershipAfter = null;
        CrossOsServerReport? server = null; CrossOsFilePin? serverPin = null, workPin = null, nativePin = null, executionPin = null;
        string? failure = null; var stop = "NOT_REQUESTED"; var status = "NOT_RUN";
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Case10 controller must execute real SSH.NET on Windows.");
            if (maximumChecks is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(maximumChecks), "Cross-OS checks bound1..2.");
            requestPin = Pin(requestPath, budget); request = Read(requestPin, CrossOsJson.Default.CrossOsClientRequest, budget);
            source = Read(request.ServerRequest, CrossOsJson.Default.CrossOsServerRequest, budget);
            ready = Read(request.Ready, CrossOsJson.Default.CrossOsReady, budget);
            ValidateClient(request, source, ready, outputPath, budget); trustedSession = true;
            self = Identity(request.ProcessEvidenceDirectory, budget);
            if (maximumChecks == 2)
            {
                identityNegativeStatus = "FAIL_incomplete";
                identityOriginalInputsStatus = "UNKNOWN_original_input_recheck_incomplete";
                try { CheckIdentityNegatives(outputPath, self, identityNegatives, budget); }
                finally
                {
                    identityRegistrationAfter = Pin(self.Registration!.Path, budget);
                    identityOwnershipAfter = Pin(self.OwnershipEvidence!.Path, budget);
                    Require(identityRegistrationAfter == self.Registration && identityOwnershipAfter == self.OwnershipEvidence,
                        "Cross-OS identity negative fixtures changed original registration or ownership bytes/SHA.");
                    identityOriginalInputsStatus = "PASS_original_bytes_and_SHA_unchanged";
                }
                identityNegativeStatus = identityNegatives.Count == 4 && identityNegatives.All(item => item.Status == "PASS") ? "PASS" : "FAIL";
                Require(identityNegativeStatus == "PASS", "Cross-OS same-process identity negative gate count4 differs.");
            }
            // No proxy retry: an unavailable WSL loopback route is an actual test failure.
            using (var session = new PinnedSession(ready.Configuration, new(Password: FixtureServer.Password), useProxy: false))
            {
                await session.ConnectSftpAsync(budget, deadline.Token); await session.ConnectSshAsync(budget, deadline.Token);
                dispatch = await RemoteDispatch.RunAsync(session, ready.Request, [], request.LogsDirectory, budget, deadline.Token);
                Require(dispatch.Status == "PASS_remote_native_execution_and_receipt" && dispatch.ResourceReclamation == "PASS_EXACT_REMOTE_EXECUTION_RECEIPT" &&
                    dispatch.ReceiptReadback == "PASS_FULL_RECEIPT_BYTE_SHA" && dispatch.SshExitCode == 0, "Windows-to-Linux dispatch/native receipt was not accepted.");
            }
            workPin = Pin(Path.Combine(request.LogsDirectory, "worker-request.json"), budget);
            nativePin = Pin(Path.Combine(request.LogsDirectory, "remote-worker.json"), budget);
            executionPin = Pin(Path.Combine(request.LogsDirectory, "remote-execution.json"), budget);
            var native = Read(nativePin, RemoteJsonContext.Default.WorkerResult, budget);
            var execution = Read(executionPin, ExecutionJsonContext.Default.ExecutionResult, budget); RequireExecution(execution);
            Require(dispatch.ReceiptSha256 == nativePin.Sha256 && native.RequestSha256 == workPin.Sha256 &&
                native.ExecutionReceiptSha256 == executionPin.Sha256 && native.WorkerBundleSha256 == source.WorkerBundle.Sha256 &&
                native.PayloadBundleSha256 == source.PayloadBundle.Sha256, "Cross-OS controller original request/native/extracted execution byte binding differs.");
            checks.Add(new("actual-windows-sshnet-to-linux-native-worker-complete-roundtrip", "PASS"));
            Console.WriteLine("crossos-check 1/" + maximumChecks + " actual-roundtrip PASS");
            if (maximumChecks == 2)
            {
                // Reuse the one completed roundtrip. The mutant never reaches session/network APIs.
                var original = ready.Request.PayloadBundle!; var files = (Sezika.RemoteTool.BundleFile[])original.Manifest.Files.Clone();
                files[0] = files[0] with { Sha256 = new string('0', 64) };
                var mutant = ready.Request with { PayloadBundle = original with { Manifest = original.Manifest with { Files = files } } };
                string observed;
                try { RemoteWorker.Validate(mutant, budget, source.Root); observed = "UNEXPECTED_ACCEPTANCE"; }
                catch (InvalidDataException error) { observed = error.Message; }
                checks.Add(new("tampered-complete-bundle-rejected-before-network", observed == MetadataGate ? "PASS" : "FAIL", MetadataGate, observed));
                Verify(request.Ready, budget); Verify(request.ServerRequest, budget);
                Verify(workPin, budget); Verify(nativePin, budget); Verify(executionPin, budget);
                Console.WriteLine("crossos-check 2/2 metadata-gate " + checks[^1].Status);
            }
            status = checks.All(item => item.Status == "PASS") ? "PENDING_linux_server_final_cleanup" : "FAIL_crossos_metadata_gate";
        }
        catch (Exception error) when (ExpectedFailure(error)) { status = "FAIL_actual_windows_linux_crossos_controller"; failure = Error(error); }
        finally
        {
            if (trustedSession && request is not null && source is not null)
            {
                try { WriteStop(request.StopFile, source.SessionId); stop = "EXACT_SESSION_STOP_FILE_CREATED"; }
                catch (Exception error) when (ExpectedFailure(error))
                { stop = "UNKNOWN_exact_stop_delivery"; failure = (failure is null ? "" : failure + ";") + Error(error); status = "FAIL_crossos_stop_delivery"; }
            }
            else stop = "NOT_REQUESTED_untrusted_session_or_input_root_fallback_required";
        }
        // A stopped Linux server must emit its independent final proof before the client can pass.
        if (trustedSession && request is not null && source is not null && ready is not null)
        {
        var finalProof = new Budget(10, null, CancellationToken.None);
        try
        {
            var found = false;
            for (var poll = 0; poll < 200; poll++)
            {
                finalProof.Check(); if (File.Exists(request.ServerResultPath)) { found = true; break; }
                await Task.Delay(50, CancellationToken.None);
            }
            if (!found) throw new TimeoutException("Cross-OS server final result poll count200/10s exceeded.");
            serverPin = Pin(request.ServerResultPath, finalProof); server = Read(serverPin, CrossOsJson.Default.CrossOsServerReport, finalProof);
            Require(server.SessionId == source.SessionId && server.Case == 10 && server.ServerRequest?.Sha256 == request.ServerRequest.Sha256 &&
                server.Server is not null && server.Server.Pid == ready.Server.Pid && server.Server.StartTicks == ready.Server.StartTicks && server.Server.BootId == ready.Server.BootId &&
                server.Server.Platform == ready.Server.Platform && server.Server.Started == ready.Server.Started &&
                server.Server.ParentPid == ready.Server.ParentPid && server.Server.ParentChain.SequenceEqual(ready.Server.ParentChain) &&
                server.Server.CommandLine == ready.Server.CommandLine && server.Server.RegisteredCommandLine == ready.Server.RegisteredCommandLine &&
                server.Server.ExpectedExecCommandLine == ready.Server.ExpectedExecCommandLine && server.Server.Registration == ready.Server.Registration &&
                server.Server.OwnershipEvidence == ready.Server.OwnershipEvidence &&
                server.Server.Scope == "registered-task-execution" && server.Server.ScopeId == ready.Server.ScopeId &&
                server.Status == "PASS_actual_linux_crossos_server_native_receipt_and_cleanup" && server.RemainingHandles == 0 && server.SftpRequests <= 512 &&
                server.NativeWorkerReceipt is not null && server.NativeWorkerReceipt.Sha256 == nativePin?.Sha256 &&
                server.NativeProcessReadback.Length > 0 && server.NativeProcessReadback.All(item => item.Status.StartsWith("PASS_", StringComparison.Ordinal)),
                "Independent Linux server final birth/native receipt/frame/handle cleanup proof differs.");
            if (status == "PENDING_linux_server_final_cleanup") status = "PASS_actual_windows_controller_to_linux_native_worker";
        }
        catch (Exception error) when (ExpectedFailure(error))
        { status = "FAIL_crossos_independent_server_final_proof"; failure = (failure is null ? "" : failure + ";") + Error(error); }
        }
        var proof = new Budget(5, null, CancellationToken.None);
        try { if (requestPin is not null) Verify(requestPin, proof); }
        catch (Exception error) when (ExpectedFailure(error))
        { status = "FAIL_crossos_client_input_recheck"; failure = (failure is null ? "" : failure + ";") + Error(error); }
        var report = new CrossOsClientReport(status, 10, maximumChecks, checks.ToArray(), self, requestPin,
            request?.ServerRequest, request?.Ready, serverPin, server, dispatch, stop, workPin, nativePin, executionPin, failure)
        {
            IdentityNegativeChecks = identityNegatives.ToArray(), IdentityNegativePlanned = maximumChecks == 2 ? 4 : 0,
            IdentityNegativeStatus = identityNegativeStatus, IdentityNegativeOriginalInputsStatus = identityOriginalInputsStatus,
            IdentityNegativeRegistrationAfter = identityRegistrationAfter, IdentityNegativeOwnershipAfter = identityOwnershipAfter
        };
        try { AssetTransport.WriteJson(outputPath, report, CrossOsJson.Default.CrossOsClientReport, proof); }
        catch (Exception error) when (ExpectedFailure(error))
        { Console.Error.WriteLine("FAIL_crossos_client_structured_report:" + Error(error)); return 1; }
        Console.WriteLine(status); return status.StartsWith("PASS_", StringComparison.Ordinal) ? 0 : 1;
    }

    private static void ValidateServer(CrossOsServerRequest request, string output, Budget budget)
    {
        Require(request.SchemaVersion == 1 && Guid.TryParseExact(request.SessionId, "N", out _) &&
            request.Root.StartsWith("/tmp/sezika-crossos-", StringComparison.Ordinal) && request.Root.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 2,
            "Cross-OS server session/schema/shallow owned root differs.");
        foreach (var path in new[] { request.Root, request.WorkerDirectory, request.PayloadDirectory, request.ReadyPath, request.StopFile, output })
        { Require(Path.IsPathFullyQualified(path), "Explicit absolute cross-OS server path required."); SafePaths.NoLinks(path, budget); }
        Require(!Path.Exists(request.Root) && !Path.Exists(request.ReadyPath) && !Path.Exists(request.StopFile) && !Path.Exists(output) &&
            Directory.Exists(Path.GetDirectoryName(request.ReadyPath)) && Directory.Exists(Path.GetDirectoryName(request.StopFile)) && Directory.Exists(Path.GetDirectoryName(output)),
            "Fresh cross-OS server root/ready/stop/result paths with existing share parents required.");
        Require(request.ReadyPath.StartsWith("/mnt/c/", StringComparison.Ordinal) && request.StopFile.StartsWith("/mnt/c/", StringComparison.Ordinal) &&
            output.StartsWith("/mnt/c/", StringComparison.Ordinal), "Cross-OS ready/stop/result require explicit shared C paths.");
        BundleProof.Validate(request.WorkerBundle, budget); BundleProof.Validate(request.PayloadBundle, budget);
        Require(request.WorkerBundle.Manifest.Directory == request.Root + "/worker" && request.WorkerBundle.Manifest.EntryPoint == "Sezika.RemoteWorker" &&
            request.WorkerBundle.Manifest.Runtime == RuntimeBundleKind.NativeAotImage && Entry(request.WorkerBundle).Bytes <= 33554432 &&
            request.PayloadBundle.Manifest.Directory == request.Root + "/payload" && request.PayloadBundle.Manifest.EntryPoint == "Sezika.ProcessFixture" &&
            request.PayloadBundle.Manifest.Runtime == RuntimeBundleKind.SelfContainedDotnet, "Complete frozen cross-OS worker/payload manifest scope differs.");
        Directory.CreateDirectory(request.Root);
    }

    private static void ValidateClient(CrossOsClientRequest request, CrossOsServerRequest source, CrossOsReady ready, string output, Budget budget)
    {
        foreach (var path in new[] { output, request.StopFile, request.ServerResultPath, request.LogsDirectory })
        { Require(Path.IsPathFullyQualified(path), "Absolute Windows cross-OS evidence paths required."); SafePaths.NoLinks(path, budget); }
        Require(request.SchemaVersion == 1 && source.SchemaVersion == 1 && Guid.TryParseExact(source.SessionId, "N", out _) &&
            source.Root.StartsWith("/tmp/sezika-crossos-", StringComparison.Ordinal) && source.Root.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 2 &&
            !Path.Exists(output) && !Path.Exists(request.StopFile) && !Path.Exists(request.LogsDirectory) &&
            Directory.Exists(Path.GetDirectoryName(output)) && Directory.Exists(Path.GetDirectoryName(request.StopFile)), "Fresh Windows cross-OS output/stop/logs required.");
        Require(ready.Status == "READY_linux_native_crossos_fixture" && ready.Case == 10 && ready.SessionId == source.SessionId &&
            ready.ServerRequestSha256 == request.ServerRequest.Sha256 && ready.Server.Platform == "linux" && ready.Server.Pid > 0 &&
            ready.Server.ParentPid > 0 && ready.Server.StartTicks is > 0 && Guid.TryParseExact(ready.Server.BootId, "D", out _) && ready.Server.ParentChain.Length is > 0 and <= 64 &&
            ready.Server.ParentChain[^1] == ready.Server.ParentPid && ready.Server.ParentChain.All(pid => pid > 0 && pid != ready.Server.Pid) &&
            ready.Server.ParentChain.Distinct().Count() == ready.Server.ParentChain.Length && ready.Server.Scope == "registered-task-execution" &&
            Guid.TryParseExact(ready.Server.ScopeId, "N", out _) && ready.Server.Registration is not null && ready.Server.OwnershipEvidence is { Bytes: 32 } &&
            ready.Server.Registration.Path == source.ProcessEvidenceDirectory.TrimEnd('/') + "/registered.json" &&
            ready.Server.OwnershipEvidence.Path == source.ProcessEvidenceDirectory.TrimEnd('/') + "/ownership.json" &&
            SamePath(request.Ready.Path, WindowsShare(source.ReadyPath)) && SamePath(request.StopFile, WindowsShare(source.StopFile)) &&
            SamePath(request.ServerResultPath, WindowsShare(ready.ServerResultPath)) && ready.StopFile == source.StopFile,
            "Windows client ready pin/share/session/Linux server complete birth differs.");
        Require(ready.Configuration.HostName == "127.0.0.1" && ready.Configuration.UserName == FixtureServer.UserName &&
            ready.Configuration.AuthenticationSource == "stdin" && ready.Configuration.AuthenticationKind == "password" &&
            ready.Configuration.MaximumSeconds == 50 && ready.Configuration.MaximumDirectoryEntries == 256 && ready.Configuration.MaximumFileBytes == 1048576 &&
            ready.Configuration.Root == source.Root && ready.Configuration.WorkerBundle?.Sha256 == source.WorkerBundle.Sha256 &&
            ready.Configuration.AllowedPayloadBundles is { Length: 1 } && ready.Configuration.AllowedPayloadBundles[0].Sha256 == source.PayloadBundle.Sha256,
            "Only exact direct-loopback published cross-OS fixture configuration admitted.");
        RemoteWorker.Validate(ready.Request, budget, source.Root);
        Require(ready.Request.RequestId == "crossos-" + source.SessionId && ready.Request.Arguments.SequenceEqual(["success"]) &&
            ready.Request.TimeoutSeconds == 3 && ready.Request.CleanupSeconds == 5 && ready.Request.MaximumProcesses == 16 &&
            ready.Request.MaximumOutputBytes == 65536 && ready.Request.StandardInputBytes == 0 &&
            ready.Request.WorkerBundle!.Sha256 == source.WorkerBundle.Sha256 && ready.Request.PayloadBundle!.Sha256 == source.PayloadBundle.Sha256,
            "Cross-OS fixed native success request/resource/bundle pins differ.");
    }

    private static void CheckIdentityNegatives(string outputPath, CrossOsIdentity positive,
        List<CrossOsIdentityNegativeCheck> results, Budget budget)
    {
        // Four same-process local mutations share the client's existing60s
        // budget. Preserve each owned fixture; never change the real scope.
        string[] names = ["missing-registration", "wrong-birth", "wrong-ownership-scope", "empty-parent-chain"];
        string[] mutations = ["remove only copied registered.json", "current PID Started plus one tick",
            "change only copied raw32-hex ownership marker", "current PID ParentChain=[]"];
        string[] gates = ["", "InvalidDataException:Cross-OS native Windows birth or actual argv differs from registration.",
            "InvalidDataException:Cross-OS ownership file and actual execution environment scope differ.",
            "InvalidDataException:Cross-OS registered task parent chain/ownership/platform/argv differs."];
        Require(results.Count == 0, "Cross-OS identity negative results must begin empty.");
        for (var index = 0; index < 4; index++)
        {
            budget.Check(); var directory = Path.GetFullPath(outputPath + ".identity-" + names[index]);
            results.Add(new(names[index], "NOT_RUN", directory, mutations[index], index == 0
                ? "FileNotFoundException:" + Path.Combine(directory, "registered.json") : gates[index]));
        }
        Require(positive.Platform == "windows" && positive.Pid == Environment.ProcessId && positive.Registration is not null &&
            positive.OwnershipEvidence is not null && positive.Scope == "registered-task-execution" && positive.Started is not null,
            "Cross-OS negative fixtures require the genuine current Windows identity first.");
        var registration = BoundBytes(positive.Registration!.Path, budget);
        var ownership = BoundBytes(positive.OwnershipEvidence!.Path, budget);
        Require(registration.Pin == positive.Registration && ownership.Pin == positive.OwnershipEvidence,
            "Cross-OS identity originals differ before negative fixture copies.");
        for (var index = 0; index < 4; index++)
        {
            budget.Check(); var result = results[index]; var directory = result.FixtureDirectory;
            results[index] = result with { Status = "FAIL_fixture_preparation_incomplete" };
            SafePaths.NoLinks(directory, budget);
            Require(!Path.Exists(directory) && Directory.Exists(Path.GetDirectoryName(directory)),
                "Cross-OS identity negative fixture requires a fresh owned directory and existing parent.");
            Directory.CreateDirectory(directory); SafePaths.NoLinks(directory, budget);
            var registrationPath = Path.Combine(directory, "registered.json"); var ownershipPath = Path.Combine(directory, "ownership.json");
            using (var copy = new FileStream(registrationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { copy.Write(registration.Bytes); copy.Flush(true); }
            using (var copy = new FileStream(ownershipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { copy.Write(ownership.Bytes); copy.Flush(true); }
            var copiedRegistration = Pin(registrationPath, budget); var copiedOwnership = Pin(ownershipPath, budget);
            Require(copiedRegistration.Bytes == registration.Pin.Bytes && copiedRegistration.Sha256 == registration.Pin.Sha256 &&
                copiedOwnership.Bytes == ownership.Pin.Bytes && copiedOwnership.Sha256 == ownership.Pin.Sha256,
                "Cross-OS genuine identity fixture copies differ before mutation.");
            if (index == 0)
            {
                Require(!SamePath(registrationPath, registration.Pin.Path), "Cross-OS fixture removal must exclude the original registration.");
                File.Delete(registrationPath);
                Require(!Path.Exists(registrationPath), "Cross-OS missing registration fixture must retain its absence.");
            }
            else if (index == 2)
            {
                var scope = Encoding.ASCII.GetString(ownership.Bytes);
                var mutant = (scope[0] == '0' ? "1" : "0") + scope[1..];
                Require(Guid.TryParseExact(mutant, "N", out _) && mutant != scope, "Cross-OS fixture ownership mutation requires another valid32-hex scope.");
                using var copy = new FileStream(ownershipPath, FileMode.Truncate, FileAccess.Write, FileShare.None);
                copy.Write(Encoding.ASCII.GetBytes(mutant)); copy.Flush(true);
            }
            else
            {
                var identities = JsonSerializer.Deserialize(registration.Bytes, ExecutionJsonContext.Default.ExecutionIdentityArray)
                    ?? throw new InvalidDataException("Cross-OS fixture original typed registration is null.");
                var current = identities.Single(item => item.Pid == positive.Pid);
                if (index == 1) current.Started = current.Started!.Value.AddTicks(1);
                else current.ParentChain = [];
                var mutant = JsonSerializer.SerializeToUtf8Bytes(identities, ExecutionJsonContext.Default.ExecutionIdentityArray);
                using var copy = new FileStream(registrationPath, FileMode.Truncate, FileAccess.Write, FileShare.None);
                copy.Write(mutant); copy.Flush(true);
            }
            CrossOsFilePin? fixtureRegistration = index == 0 ? null : Pin(registrationPath, budget);
            var fixtureOwnership = Pin(ownershipPath, budget);
            Require(index == 2
                ? fixtureRegistration is not null && fixtureRegistration.Bytes == registration.Pin.Bytes &&
                    fixtureRegistration.Sha256 == registration.Pin.Sha256 && fixtureOwnership.Bytes == 32 && fixtureOwnership.Sha256 != ownership.Pin.Sha256
                : fixtureOwnership.Bytes == ownership.Pin.Bytes && fixtureOwnership.Sha256 == ownership.Pin.Sha256 &&
                    (index == 0 || fixtureRegistration is not null && fixtureRegistration.Sha256 != registration.Pin.Sha256),
                "Cross-OS identity fixture must mutate only its named registration or ownership input.");
            result = result with { Status = "FAIL_identity_not_rejected", Registration = fixtureRegistration, OwnershipEvidence = fixtureOwnership };
            results[index] = result;
            var observed = "UNEXPECTED_ACCEPTANCE"; string? observedError = null;
            try { _ = Identity(directory, budget); }
            catch (Exception error) when (ExpectedFailure(error))
            {
                observedError = Error(error);
                observed = error is FileNotFoundException missing && missing.FileName is not null && SamePath(missing.FileName, registrationPath)
                    ? "FileNotFoundException:" + registrationPath : observedError;
                results[index] = result with { ObservedGate = observed, ObservedError = observedError };
                if (error is OperationCanceledException or TimeoutException) throw;
            }
            results[index] = result with { ObservedGate = observed, ObservedError = observedError };
            if (fixtureRegistration is null) Require(!Path.Exists(registrationPath), "Cross-OS missing registration fixture changed during rejection.");
            else Verify(fixtureRegistration, budget);
            Verify(fixtureOwnership, budget);
            results[index] = result with { Status = observed == result.ExpectedGate ? "PASS" : "FAIL", ObservedGate = observed, ObservedError = observedError };
            Console.WriteLine("crossos-identity-negative " + (index + 1) + "/4 " + names[index] + " " + results[index].Status);
        }
    }

    private static CrossOsIdentity Identity(string evidenceDirectory, Budget budget)
    {
        Require(!string.IsNullOrWhiteSpace(evidenceDirectory) && Path.IsPathFullyQualified(evidenceDirectory) &&
            !evidenceDirectory.StartsWith("\\\\", StringComparison.Ordinal), "Cross-OS explicit local process evidence directory required.");
        SafePaths.NoLinks(evidenceDirectory, budget);
        var self = ProcessObserver.Observe(Environment.ProcessId);
        Require(self.State == "Present" && self.Pid == Environment.ProcessId && self.ParentPid > 0 &&
            !string.IsNullOrEmpty(self.CommandLine), "Cross-OS self native identity unavailable.");
        var registration = BoundBytes(Path.Combine(evidenceDirectory, "registered.json"), budget);
        var ownership = BoundBytes(Path.Combine(evidenceDirectory, "ownership.json"), budget);
        Require(ownership.Bytes.Length == 32 && ownership.Bytes.All(value => value is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F'),
            "Cross-OS ownership scope must retain exactly32 hexadecimal raw bytes.");
        var scope = Encoding.ASCII.GetString(ownership.Bytes);
        Require(Guid.TryParseExact(scope, "N", out _) && scope == Environment.GetEnvironmentVariable("SEZIKA_EXECUTION_ID"),
            "Cross-OS ownership file and actual execution environment scope differ.");
        var registered = Read(registration.Pin, ExecutionJsonContext.Default.ExecutionIdentityArray, budget);
        Require(registered.Length is > 0 and <= 256 && registered.All(item => item is not null && item.Pid > 0) &&
            registered.Select(item => item.Pid).Distinct().Count() == registered.Length && registered.Count(item => item.Pid == self.Pid) == 1,
            "Cross-OS execution registration requires exactly one current PID and unique positive task identities.");
        var entry = registered.Single(item => item.Pid == self.Pid);
        Require(entry.Platform == self.Platform && entry.ParentPid == self.ParentPid && entry.ParentChain is { Length: > 0 and <= 64 } &&
            entry.ParentChain[^1] == self.ParentPid && entry.ParentChain.All(pid => pid > 0 && pid != self.Pid) &&
            entry.ParentChain.Distinct().Count() == entry.ParentChain.Length && entry.ScopeId == scope && !string.IsNullOrWhiteSpace(entry.Ownership) && entry.Ownership != "launcher" &&
            !string.IsNullOrEmpty(entry.CommandLine), "Cross-OS registered task parent chain/ownership/platform/argv differs.");
        if (self.Platform == "windows")
            Require(entry.Started is not null && self.Started is not null && entry.Started == self.Started &&
                self.CommandLine == entry.CommandLine, "Cross-OS native Windows birth or actual argv differs from registration.");
        else if (self.Platform == "linux")
            Require(entry.StartTicks is > 0 && entry.StartTicks == self.StartTicks && Guid.TryParseExact(entry.BootId, "D", out _) &&
                entry.BootId == self.BootId && self.CommandLine!.EndsWith('\0') &&
                (self.CommandLine == entry.CommandLine || !string.IsNullOrEmpty(entry.ExpectedExecCommandLine) &&
                    entry.ExpectedExecCommandLine.EndsWith('\0') && self.CommandLine == entry.ExpectedExecCommandLine),
                "Cross-OS native Linux birth/boot or actually observed registered exec argv differs.");
        else throw new InvalidDataException("Cross-OS native identity platform is unsupported.");
        // Observe only self again. The complete task chain comes from the
        // executor; reading unrelated OS ancestors would cross its ownership.
        var after = ProcessObserver.Observe(Environment.ProcessId);
        Require(after.State == "Present" && after.Pid == self.Pid && after.Platform == self.Platform && after.ParentPid == self.ParentPid &&
            after.Started == self.Started && after.StartTicks == self.StartTicks && after.BootId == self.BootId && after.CommandLine == self.CommandLine,
            "Cross-OS native self identity changed during registered task admission.");
        Verify(registration.Pin, budget); Verify(ownership.Pin, budget);
        Require(Environment.GetEnvironmentVariable("SEZIKA_EXECUTION_ID") == scope, "Cross-OS execution scope changed during admission.");
        return new(self.Pid, self.ParentPid, entry.ParentChain.ToArray(), self.Platform, self.Started, self.StartTicks, self.BootId, self.CommandLine!)
        {
            Scope = "registered-task-execution", ScopeId = scope, Registration = registration.Pin, OwnershipEvidence = ownership.Pin,
            RegisteredCommandLine = entry.CommandLine, ExpectedExecCommandLine = entry.ExpectedExecCommandLine
        };
    }

    private static CrossOsBirth[] Readback(ExecutionResult execution, Budget budget)
    {
        Require(execution.Processes is { Length: > 0 and <= 16 }, "Cross-OS native identity count must retain bound16.");
        var identities = execution.Processes.Append(execution.Launcher ?? throw new InvalidDataException("Actual native worker launcher missing."));
        var result = new List<CrossOsBirth>();
        // The checked complete array contains at most16 children plus its one real launcher.
        foreach (var identity in identities)
        {
            budget.Check(); var observed = ProcessObserver.Observe(identity.Pid);
            var different = observed.State == "Present" && observed.StartTicks is not null && observed.BootId is not null &&
                identity.StartTicks is not null && identity.BootId is not null && (observed.StartTicks != identity.StartTicks || observed.BootId != identity.BootId);
            result.Add(new(identity.Pid, identity.StartTicks, identity.BootId,
                observed.State == "Absent" ? "PASS_original_birth_absent" : different ? "PASS_reused_different_birth_preserved" : "UNKNOWN_or_original_birth_present", identity.State));
        }
        return result.ToArray();
    }

    private static void RequireExecution(ExecutionResult result) => Require(result.Status == "PASS" && result.Platform == "linux" &&
        result.ExecutionStatus == "SUCCEEDED" && result.ExitCode == 0 && result.CleanupStatus == "PASS" && result.RemainingProcesses == 0 &&
        result.InputComplete && result.OutputComplete && !result.OutputLimitExceeded && result.Errors is { Length: 0 } &&
        result.Processes is { Length: > 0 and <= 16 }, "Complete actual native EOF/exit/cleanup/count16 proof differs.");
    private static Sezika.RemoteTool.BundleFile Entry(BundlePin pin) => pin.Manifest.Files.Single(item => item.RelativePath == pin.Manifest.EntryPoint);
    private static void CopyBundle(string source, BundlePin pin, Budget budget)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Cross-OS complete bundle copy requires Linux permissions.");
        BundleProof.VerifyLocal(pin, budget, source); var destination = pin.Manifest.Directory; Directory.CreateDirectory(destination);
        for (var index = 0; index < pin.Manifest.Files.Length && index < 512; index++)
        {
            budget.Check(); var member = pin.Manifest.Files[index];
            var input = SafePaths.Within(source, member.RelativePath, budget); var target = SafePaths.Within(destination, member.RelativePath, budget);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(input, target, overwrite: false);
            Require(new FileInfo(target).Length == member.Bytes && AssetTransport.FileHash(target, budget) == member.Sha256, "Cross-OS frozen complete bundle copy differs.");
            File.SetUnixFileMode(target, File.GetUnixFileMode(input));
        }
        BundleProof.VerifyLocal(pin, budget, source); BundleProof.VerifyLocal(pin, budget);
    }
    private static CrossOsFilePin Pin(string path, Budget budget)
        => BoundBytes(path, budget).Pin;
    private static void Verify(CrossOsFilePin expected, Budget budget)
    { var actual = Pin(expected.Path, budget); Require(actual.Bytes == expected.Bytes && actual.Sha256 == expected.Sha256, "Cross-OS metadata bytes/SHA differ."); }
    private static T Read<T>(CrossOsFilePin pin, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, Budget budget)
    {
        var bound = BoundBytes(pin.Path, budget);
        Require(bound.Pin.Bytes == pin.Bytes && bound.Pin.Sha256 == pin.Sha256, "Cross-OS same-read metadata bytes/SHA differ.");
        var reader = new Utf8JsonReader(bound.Bytes, new JsonReaderOptions { MaxDepth = 32 });
        var scopes = new Stack<HashSet<string>?>(); var tokens = 0;
        while (reader.Read())
        {
            budget.Check(); Require(++tokens <= 100000, "Cross-OS JSON token bound100000 exceeded.");
            if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.StartArray) scopes.Push(null);
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) scopes.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
                Require(scopes.Peek()?.Add(reader.GetString()!) == true, "Cross-OS duplicate JSON metadata property.");
        }
        Require(scopes.Count == 0, "Cross-OS incomplete JSON metadata.");
        return JsonSerializer.Deserialize(bound.Bytes, type) ?? throw new InvalidDataException("Cross-OS null typed metadata.");
    }
    private static (byte[] Bytes, CrossOsFilePin Pin) BoundBytes(string path, Budget budget)
    {
        budget.Check(); Require(Path.IsPathFullyQualified(path), "Absolute cross-OS pin path required.");
        SafePaths.NoLinks(path, budget); RegularFiles.Require(path, budget);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length is > 0 and <= 1048576, "Cross-OS metadata pin byte bound1MiB/nonempty differs.");
        var bytes = new byte[(int)stream.Length]; var offset = 0;
        for (var block = 0; block < 1024 && offset < bytes.Length; block++)
        {
            budget.Check(); var count = stream.Read(bytes.AsSpan(offset, Math.Min(1024, bytes.Length - offset)));
            if (count == 0) throw new EndOfStreamException(); offset += count;
        }
        Require(offset == bytes.Length && stream.Length == bytes.Length && stream.ReadByte() == -1, "Cross-OS metadata changed during same-handle read.");
        budget.Check(); return (bytes, new(Path.GetFullPath(path), bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))));
    }
    private static void WriteStop(string path, string session)
    {
        var budget = new Budget(2, null, CancellationToken.None); SafePaths.NoLinks(path, budget);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); stream.Write(Encoding.ASCII.GetBytes(session)); stream.Flush(true);
    }
    private static string WindowsShare(string path)
    { Require(path.StartsWith("/mnt/c/", StringComparison.Ordinal), "Explicit C share mapping required."); return "C:\\" + path[7..].Replace('/', '\\'); }
    private static bool SamePath(string left, string right) => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
    private static void Mpint(byte[] value, WireWriter writer)
    { var leading = value[0] >= 128; var bytes = new byte[value.Length + (leading ? 1 : 0)]; value.CopyTo(bytes, leading ? 1 : 0); writer.Bytes(bytes); CryptographicOperations.ZeroMemory(bytes); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static bool ExpectedFailure(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
        InvalidOperationException or TimeoutException or OperationCanceledException or PlatformNotSupportedException or
        System.Net.Sockets.SocketException or Renci.SshNet.Common.SshException or JsonException or CryptographicException;
    private static string Error(Exception error) => (error.GetType().Name + ":" + error.Message).Replace(FixtureServer.Password, "[fixture-auth-redacted]", StringComparison.Ordinal);
}
