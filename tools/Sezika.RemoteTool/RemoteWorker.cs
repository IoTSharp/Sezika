using System.Text.Json;
using Sezika.Tooling;

namespace Sezika.RemoteTool;

internal static class RemoteWorker
{
    internal static void Validate(WorkerRequest request, Budget budget, string? expectedRoot = null)
    {
        budget.Check(); RemotePaths.Identity(request.RequestId); RemotePaths.Sha(request.WorkerSha256); RemotePaths.Sha(request.ExecutableSha256);
        _ = RemotePaths.Absolute(request.Root); _ = RemotePaths.Absolute(request.WorkerPath); _ = RemotePaths.Absolute(request.Executable);
        _ = RemotePaths.Absolute(request.WorkingDirectory); _ = RemotePaths.Absolute(request.OutputDirectory);
        if (request.SchemaVersion != 1 || (expectedRoot is not null && request.Root != expectedRoot) ||
            !RemotePaths.IsWithin(request.Root, request.WorkerPath) || !RemotePaths.IsWithin(request.Root, request.Executable) ||
            !RemotePaths.IsWithin(request.Root, request.OutputDirectory) || (request.WorkingDirectory != request.Root && !RemotePaths.IsWithin(request.Root, request.WorkingDirectory)) ||
            request.TimeoutSeconds is < 1 or > 86400 || request.CleanupSeconds is < 1 or > 30 || request.MaximumProcesses is < 1 or > 256 ||
            request.MaximumOutputBytes is < 1 or > 67108864 || request.StandardInputBytes is < 0 or > 16777216 || request.Arguments.Length > 256 ||
            request.Arguments.Sum(a => (long)a.Length + 1) > 30000 || request.Arguments.Any(a => a.Contains('\0')) || request.AllowedExecutables.Length is < 1 or > 64)
            throw new InvalidDataException("Worker request path/resource/schema scope differs.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pin in request.AllowedExecutables)
        {
            budget.Check(); RemotePaths.Sha(pin.Sha256);
            if (!RemotePaths.IsWithin(request.Root, pin.Path) || !paths.Add(pin.Path)) throw new InvalidDataException("Worker native allowlist path differs.");
        }
        if (!request.AllowedExecutables.Any(p => p.Path == request.Executable && p.Sha256 == request.ExecutableSha256))
            throw new InvalidDataException("Worker payload is outside the frozen native executable allowlist.");
        foreach (var argument in request.Arguments)
        {
            budget.Check();
            if (new[] { ".ps1", ".sh", ".py", ".cmd", ".bat", ".js", ".rb", ".pl", ".vbs" }.Any(extension => argument.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Worker script/interpreter arguments are forbidden.");
        }
        BundleProof.ValidateBinding(request.WorkerBundle, request.WorkerPath, request.WorkerSha256, request.Root, budget, nativeWorker: true);
        BundleProof.ValidateBinding(request.PayloadBundle, request.Executable, request.ExecutableSha256, request.Root, budget, nativeWorker: false);
        if (request.OutputDirectory == request.WorkerBundle!.Manifest.Directory || RemotePaths.IsWithin(request.WorkerBundle.Manifest.Directory, request.OutputDirectory) ||
            request.OutputDirectory == request.PayloadBundle!.Manifest.Directory || RemotePaths.IsWithin(request.PayloadBundle.Manifest.Directory, request.OutputDirectory))
            throw new InvalidDataException("Worker execution evidence must remain outside frozen code/runtime bundles.");
    }

    internal static async Task<int> RunAsync(string path, string expectedSha, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            throw new PlatformNotSupportedException("The fixed remote worker requires its actual Linux NativeAOT image; controller and payload may use JIT.");
        using var admission = new CancellationTokenSource(TimeSpan.FromSeconds(15)); using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, admission.Token);
        var admissionBudget = new Budget(15, null, linked.Token); RemotePaths.Sha(expectedSha); RegularFiles.Require(path, admissionBudget);
        if (new FileInfo(path).Length > 1048576 || AssetTransport.FileHash(path, admissionBudget) != expectedSha) throw new InvalidDataException("Worker request byte/SHA differs.");
        var request = AssetTransport.ReadJson(path, RemoteJsonContext.Default.WorkerRequest, admissionBudget); Validate(request, admissionBudget);
        if (!RemotePaths.IsWithin(request.Root, path)) throw new InvalidDataException("Worker request file escaped its admitted root.");
        var self = Environment.ProcessPath ?? throw new IOException("Worker apphost path unavailable.");
        if (Path.GetFullPath(self) != request.WorkerPath || AssetTransport.FileHash(self, admissionBudget) != request.WorkerSha256 ||
            AssetTransport.FileHash(request.Executable, admissionBudget) != request.ExecutableSha256) throw new InvalidDataException("Worker/payload native byte identity differs.");
        ProcessExecutor.ValidateExecutable(self, admissionBudget); ProcessExecutor.ValidateExecutable(request.Executable, admissionBudget);
        BundleProof.VerifyLocal(request.WorkerBundle!, admissionBudget); BundleProof.VerifyLocal(request.PayloadBundle!, admissionBudget);
        SafePaths.NoLinks(request.WorkingDirectory, admissionBudget); SafePaths.NoLinks(request.OutputDirectory, admissionBudget);
        var input = new byte[request.StandardInputBytes];
        try
        {
            if (input.Length > 0) await Console.OpenStandardInput().ReadExactlyAsync(input, linked.Token);
            using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellation); work.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
            var cancelFile = request.OutputDirectory + ".cancel"; var stopFile = request.OutputDirectory + ".stop";
            var execution = await ProcessExecutor.ExecuteAsync(new ExecutionRequest
            {
                Executable = request.Executable, ArgumentList = request.Arguments, WorkingDirectory = request.WorkingDirectory, OutputDirectory = request.OutputDirectory,
                TimeoutSeconds = request.TimeoutSeconds, CleanupSeconds = request.CleanupSeconds, MaximumProcesses = request.MaximumProcesses,
                MaximumOutputBytes = request.MaximumOutputBytes, MaximumInputBytes = request.StandardInputBytes, StandardInput = input,
                CancelFile = cancelFile, StopFile = stopFile, BrokerExecutable = self, BrokerArgumentPrefix = ["process", "broker"], WatchdogArgumentPrefix = ["process", "watchdog"]
            }, new Budget(request.TimeoutSeconds, cancelFile, work.Token), work.Token);
            var proofBudget = new Budget(15, null, cancellation);
            BundleProof.VerifyLocal(request.WorkerBundle!, proofBudget); BundleProof.VerifyLocal(request.PayloadBundle!, proofBudget);
            var executionFile = Path.Combine(request.OutputDirectory, "result.json");
            var bound = new WorkerResult(execution.Status, request.RequestId, expectedSha, request.WorkerBundle!.Sha256, request.PayloadBundle!.Sha256,
                AssetTransport.FileHash(executionFile, proofBudget), "PASS_COMPLETE_CODE_RUNTIME_BUNDLES_BEFORE_AND_AFTER", execution);
            AssetTransport.WriteJson(Path.Combine(request.OutputDirectory, "worker-result.json"), bound, RemoteJsonContext.Default.WorkerResult, proofBudget);
            Console.WriteLine(execution.Status == "PASS" ? "PASS_remote_native_worker_execution_receipt" : "FAIL_remote_native_worker_execution_receipt");
            return execution.Status == "PASS" ? 0 : 1;
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(input); }
    }
}
