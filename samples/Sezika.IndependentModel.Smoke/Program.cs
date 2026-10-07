using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;

namespace Sezika.IndependentSmoke;

internal sealed record Options(string Command, string Backend, string? Package, string? Capture, string? SourceFreeze,
    string? Output, bool RequireAot, int Device, int Timeout, int MaxChecks)
{
    internal static Options Parse(string[] args)
    {
        ArtifactIO.Require(args.Length is >= 1 and <= 24, "Bounded command required");
        var command = args[0]; ArtifactIO.Require(command is "self-check" or "probe" or "run", "Command must be self-check, probe or run");
        var seen = new HashSet<string>(); string backend = "cuda"; string? package = null, capture = null, freeze = null, output = null;
        bool requireAot = false; int device = 0, timeout = command == "run" ? 100 : 30, maximum = 8;
        var clock = Stopwatch.StartNew();
        for (var i = 1; i < args.Length; i++)
        {
            ArtifactIO.Require(clock.Elapsed < TimeSpan.FromSeconds(5) && seen.Add(args[i]), "Repeated option or parser deadline"); var option = args[i];
            if (option == "--require-aot") { requireAot = true; continue; }
            ArtifactIO.Require(i + 1 < args.Length, "Missing option value"); var value = args[++i];
            switch (option)
            {
                case "--backend": backend = value; break;
                case "--package": package = value; break;
                case "--capture": capture = value; break;
                case "--source-freeze": freeze = value; break;
                case "--output": output = value; break;
                case "--device": ArtifactIO.Require(int.TryParse(value, out device), "Invalid device integer"); break;
                case "--timeout-seconds": ArtifactIO.Require(int.TryParse(value, out timeout), "Invalid timeout integer"); break;
                case "--max-checks": ArtifactIO.Require(int.TryParse(value, out maximum), "Invalid check integer"); break;
                default: throw new InvalidDataException("Unknown option: " + option);
            }
        }
        ArtifactIO.Require(backend is "cpu" or "cuda" && device is >= 0 and <= 16 && timeout is >= 1 and <= 100 && maximum is >= 1 and <= 8, "Option bounds differ");
        ArtifactIO.Require(command == "run" || timeout <= 30, "Probe/self-check deadline must be <=30s");
        if (command == "run") ArtifactIO.Require(package is not null && capture is not null && freeze is not null && output is not null, "run requires package/capture/source-freeze/output");
        if (command == "probe") ArtifactIO.Require(backend == "cuda" && output is not null, "probe requires CUDA and output");
        return new(command, backend, package, capture, freeze, output, requireAot, device, timeout, maximum);
    }
}

internal static class Program
{
    internal static ResourceClosure? LastClosure;
    public static int Main(string[] args)
    {
        Options? options = null; var started = DateTimeOffset.UtcNow;
        try
        {
            options = Options.Parse(args); using var budget = new Budget(options.Timeout);
            ConsoleCancelEventHandler handler = (_, eventArgs) => { eventArgs.Cancel = true; budget.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                if (options.RequireAot) ArtifactIO.Require(NativeIdentity.Current().IsNative, "--require-aot rejected managed execution");
                switch (options.Command)
                {
                    case "self-check": Checks.Run(options.MaxChecks, budget); break;
                    case "probe":
                        var probe = Probe(options, budget); ArtifactIO.Publish(options.Output!, JsonSerializer.SerializeToUtf8Bytes(probe, SmokeJson.Default.ProbeReport), budget); break;
                    case "run":
                        var report = Run(options, budget, started); ArtifactIO.Publish(options.Output!, JsonSerializer.SerializeToUtf8Bytes(report, SmokeJson.Default.SmokeReport), budget);
                        Console.WriteLine($"PASS diagnostic smoke: backend={report.Backend}; tokens=46; training=false; quality=false; parity=false"); break;
                }
                return 0;
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"independent diagnostic smoke failed: {error}");
            if (options?.Output is not null)
            {
                try
                {
                    using var auditBudget = new Budget(5, observeMemory: false);
                    var failure = new FailureReport("failed", error.GetType().Name, error.ToString()[..Math.Min(error.ToString().Length, 8192)], LastClosure, Environment.ProcessId, started);
                    ArtifactIO.Publish(options.Output + ".failed-" + Guid.NewGuid().ToString("N") + ".json",
                        JsonSerializer.SerializeToUtf8Bytes(failure, SmokeJson.Default.FailureReport), auditBudget);
                }
                catch (Exception auditError) { Console.Error.WriteLine("Failure audit could not be saved: " + auditError); }
            }
            return error is OperationCanceledException or TimeoutException ? 124 : 1;
        }
    }

    private static ProbeReport Probe(Options options, Budget budget)
    {
        budget.Check(); using var device = CudaDevice.Open(options.Device, enableProfiling: true); var info = device.Info;
        float[] result = new float[2];
        using (var add = device.CreateVectorAdd()) { budget.Check(); add.Execute([1, 2], [3, 4], result); budget.Check(); }
        ArtifactIO.Require(result.SequenceEqual([4f, 6f]), "CUDA allocation/copy/kernel/readback probe differs");
        var after = device.GetMemorySnapshot(); Clean(after);
        Console.WriteLine("PASS CUDA driver probe: allocations/modules/owned bytes=0");
        return new("driver_probe_complete", NativeIdentity.Current(), info, result, after, device.GetTelemetry(), true);
    }

    private static SmokeReport Run(Options options, Budget budget, DateTimeOffset started)
    {
        var package = ArtifactIO.Safe(options.Package!); var capturePath = ArtifactIO.Safe(options.Capture!); var inputRoot = Path.GetDirectoryName(capturePath)!;
        var sourceRoot = ArtifactIO.Safe(Path.Combine(package, "..", "source"));
        var paths = new[] { capturePath, Path.Combine(inputRoot, "request.json"), Path.Combine(inputRoot, "token-ids.i32"), Path.Combine(inputRoot, "marker-positions.i32"),
            Path.Combine(package, "model.json"), Path.Combine(package, "source_lock.json"), Path.Combine(package, "encoder.safetensors"),
            Path.Combine(package, "tokenizer", "tokenizer.json"), Path.Combine(sourceRoot, "config.json"), Path.Combine(sourceRoot, "pytorch_model.bin"), ArtifactIO.Safe(options.SourceFreeze!) };
        var leases = new List<FileStream>(); IndependentEncoderModelPackage? model = null; IndependentCudaDecisionPipeline? pipeline = null;
        IndependentDecisionV1Runtime? runtime = null; CudaDevice? device = null; CudaMemorySnapshot? before = null, after = null; CudaTelemetrySnapshot? telemetry = null;
        CudaDeviceInfo? deviceInfo = null; Exception? failure = null; var cleanup = new List<string>(); bool deviceDisposed = false; long precancelDelta = -1;
        CudaMemorySnapshot? precancelBefore = null, precancelAfter = null;
        CudaMemorySnapshot? activeSnapshot = null;
        Answer[] answers = []; DiagnosticDescriptor? descriptor = null; string descriptorHash = "";
        Artifact? captureIdentity = null, requestIdentity = null, configIdentity = null, lockIdentity = null, sourceIdentity = null, freezeIdentity = null;
        try
        {
            ArtifactIO.Require(paths.Length <= 16, "Lease item bound exceeded");
            foreach (var path in paths) { budget.Check(); leases.Add(ArtifactIO.Lease(path)); }
            var bytes = ArtifactIO.Small(leases[0], budget); ArtifactIO.JsonGuard(bytes, budget);
            var input = JsonSerializer.Deserialize(bytes, SmokeJson.Default.CaptureInput) ?? throw new InvalidDataException("Capture is null");
            ValidateInput(input, budget);
            captureIdentity = new(capturePath, ArtifactIO.Hash(bytes), bytes.Length);
            ArtifactIO.Require(input.Request.Path == "request.json" && input.TokenIdsArtifact.Path == "token-ids.i32" && input.MarkerPositionsArtifact.Path == "marker-positions.i32", "Fixed artifact paths differ");
            requestIdentity = ArtifactIO.Identity(paths[1], leases[1], budget, 1_048_576, Diagnostic.RequestHash, input.Request.Bytes);
            var tokenIdentity = ArtifactIO.Identity(paths[2], leases[2], budget, 184, Diagnostic.TokensHash, 184);
            var markerIdentity = ArtifactIO.Identity(paths[3], leases[3], budget, 8, Diagnostic.MarkersHash, 8);
            ArtifactIO.Require(tokenIdentity.Sha256 == input.TokenIdsArtifact.Sha256 && markerIdentity.Sha256 == input.MarkerPositionsArtifact.Sha256, "Input artifact metadata differs");
            ArtifactIO.Require(ArtifactIO.Small(leases[2], budget).SequenceEqual(IntBytes(input.TokenIds)) && ArtifactIO.Small(leases[3], budget).SequenceEqual(IntBytes(input.MarkerPositions)), "Realized integer bytes differ");
            configIdentity = ArtifactIO.Identity(paths[8], leases[8], budget, 1_048_576, Diagnostic.ConfigHash);
            lockIdentity = ArtifactIO.Identity(paths[5], leases[5], budget, 1_048_576, Diagnostic.LockHash);
            sourceIdentity = ArtifactIO.Identity(paths[9], leases[9], budget, 2_147_483_648, IndependentModelLoader.SourceWeightsSha256, 1_231_188_142);
            freezeIdentity = ArtifactIO.Identity(paths[10], leases[10], budget, 1_048_576);
            Console.WriteLine("Verified fixed source/input; loading production C# encoder (no CPU forward).");
            model = IndependentModelLoader.Load(package, new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(100), MaxWorkspaceBytes = 128L * 1024 * 1024 }, budget.Token);
            budget.Check(); ArtifactIO.Require(model.SourceModelId == IndependentModelLoader.SourceModelId && model.Revision == IndependentModelLoader.SourceRevision && model.License == "MIT" && model.Encoder.Config.HiddenSize == 768, "Loaded original model identity differs");
            var request = IndependentDecisionV1Parser.Parse(ArtifactIO.Small(leases[1], budget));
            ArtifactIO.Require(request.Questions.Count == 1, "Exactly one fixed question is permitted");
            var sequence = IndependentDecisionV1SequenceBuilder.Build(model.Tokenizer, request, request.Questions[0],
                new IndependentDecisionSequenceOptions { MaxTokens = 46, MaxCharacters = 4096, Deadline = TimeSpan.FromSeconds(10) }, budget.Token);
            ArtifactIO.Require(sequence.TokenIds.SequenceEqual(input.TokenIds) && sequence.MarkerPositions.SequenceEqual([25, 35]) && sequence.TokenIds.Length == 46, "Constructed sequence differs from frozen capture");
            var initialized = Diagnostic.Create(budget); descriptor = initialized.Descriptor; var head = initialized.Asset; descriptorHash = head.FileSha256;
            if (options.Backend == "cuda")
            {
                device = CudaDevice.Open(options.Device, enableProfiling: true); deviceInfo = device.Info; before = device.GetMemorySnapshot(); Clean(before);
                ArtifactIO.Require(before.FreeBytes >= 2UL * 1024 * 1024 * 1024, "CUDA free memory below 2GiB");
                pipeline = new IndependentCudaDecisionPipeline(device, model, head, budget.Token);
                ArtifactIO.Require(device.GetMemorySnapshot().PeakOwnedBytes <= 3UL * 1024 * 1024 * 1024, "CUDA owned peak exceeds 3GiB");
            }
            runtime = new IndependentDecisionV1Runtime(model, head, new IndependentDecisionRuntimeOptions { MaxTokens = 46, MaxCharacters = 4096, Deadline = TimeSpan.FromSeconds(100) }, pipeline);
            pipeline = null; // runtime now owns the pipeline and model.
            var launches = device?.GetTelemetry().KernelLaunchCount ?? 0;
            precancelBefore = device?.GetMemorySnapshot();
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); bool rejected = false;
                try { runtime.Evaluate(request, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
                ArtifactIO.Require(rejected, "Precancelled real runtime was not rejected");
            }
            precancelDelta = (device?.GetTelemetry().KernelLaunchCount ?? 0) - launches;
            precancelAfter = device?.GetMemorySnapshot();
            if (precancelBefore is not null && precancelAfter is not null)
                ArtifactIO.Require(precancelBefore.OwnedBytes == precancelAfter.OwnedBytes && precancelBefore.OwnedAllocationCount == precancelAfter.OwnedAllocationCount && precancelBefore.LoadedModuleCount == precancelAfter.LoadedModuleCount && precancelBefore.ReleaseFailureCount == precancelAfter.ReleaseFailureCount, "Precancel changed resident resource counters");
            ArtifactIO.Require(precancelDelta == 0 && model.Encoder.WorkspacePool.OutstandingBytes == 0 && model.Encoder.WorkspacePool.ActiveCount == 0, "Precancel allocated/launched work");
            budget.Check(); Console.WriteLine("Executing exactly one real encoder + diagnostic head forward.");
            var response = runtime.Evaluate(request, budget.Token); budget.Check(); ValidateResponse(response, options.Backend);
            answers = response.Answers.Select(value => new Answer(value.QuestionId, value.Type.ToString(), value.CandidateIds.ToArray(), value.Probabilities.ToArray(), value.SelectedIndex, value.Concentration, value.Status)).ToArray();
            telemetry = device?.GetTelemetry();
            activeSnapshot = device?.GetMemorySnapshot();
            if (activeSnapshot is not null) ArtifactIO.Require(activeSnapshot.PeakOwnedBytes <= 3UL * 1024 * 1024 * 1024, "Active CUDA owned peak exceeds 3GiB");
            if (options.Backend == "cuda") ArtifactIO.Require(telemetry is { ProfilingEnabled: true, KernelLaunchCount: > 0, DeviceToHostBytes: 552 }, "Actual CUDA profiling/launch/readback proof differs");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try { runtime?.Dispose(); } catch (Exception error) { cleanup.Add("runtime: " + error); }
            if (runtime is null)
            {
                try { pipeline?.Dispose(); } catch (Exception error) { cleanup.Add("pipeline: " + error); }
                try { model?.Dispose(); } catch (Exception error) { cleanup.Add("model: " + error); }
            }
            try { if (device is not null) { after = device.GetMemorySnapshot(); Clean(after); } } catch (Exception error) { cleanup.Add("owned CUDA closure: " + error); }
            try { device?.Dispose(); deviceDisposed = device is not null; } catch (Exception error) { cleanup.Add("caller device: " + error); }
            var leaseCleanup = Stopwatch.StartNew(); int released = 0;
            for (; released < leases.Count && released < 16 && leaseCleanup.Elapsed < TimeSpan.FromSeconds(5); released++)
            { try { leases[released].Dispose(); } catch (Exception error) { cleanup.Add("lease: " + error); } }
            if (released != leases.Count) cleanup.Add($"Lease cleanup deadline/item bound: {released}/{leases.Count}; process exit remains required.");
            LastClosure = new(model?.IsDisposed ?? false, model?.Encoder.WorkspacePool.OutstandingBytes ?? 0,
                model?.Encoder.WorkspacePool.ActiveCount ?? 0, before, after, precancelDelta, deviceDisposed, cleanup.ToArray())
                { PrecancelBefore = precancelBefore, PrecancelAfter = precancelAfter, ActiveSnapshot = activeSnapshot };
        }
        if (failure is not null) { if (cleanup.Count > 0) throw new AggregateException("Execution and cleanup failed", failure, new IOException(string.Join("\n", cleanup))); ExceptionDispatchInfo.Capture(failure).Throw(); }
        ArtifactIO.Require(cleanup.Count == 0 && LastClosure!.ModelDisposed && LastClosure.WorkspaceBytes == 0 && LastClosure.ActiveWorkspaces == 0, "Resource closure failed");
        using var executable = ArtifactIO.Lease(Environment.ProcessPath ?? throw new InvalidDataException("Executable path unavailable"));
        var executableIdentity = ArtifactIO.Identity(Environment.ProcessPath!, executable, budget, 64L * 1024 * 1024);
        return new("sezika.independent-diagnostic-smoke.v1", "diagnostic_smoke_complete", options.Backend == "cuda" ? "cuda-independent-linear-marker-head" : "cpu-independent-linear-marker-head",
            model!.ModelId, model.SourceModelId, model.Revision, IndependentModelLoader.SourceWeightsSha256, model.EncoderSha256, model.TokenizerSha256,
            captureIdentity!, requestIdentity!, configIdentity!, lockIdentity!, sourceIdentity!, freezeIdentity!, executableIdentity, descriptor!, descriptorHash,
            NativeIdentity.Current(), 46, [25, 35], answers, deviceInfo, telemetry, LastClosure!, Environment.ProcessId, started, budget.Elapsed);
    }
    internal static byte[] IntBytes(int[] values)
    {
        ArtifactIO.Require(values is not null && values.Length is > 0 and <= 46, "I32 count invalid"); var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), values[i]); return bytes;
    }
    internal static void ValidateInput(CaptureInput input, Budget budget)
    {
        budget.Check(); ArtifactIO.Require(input.SchemaVersion == 1 && input.Status == "cpu_capture_complete" && input.Kind == "csharp_cpu_capture" && !input.FixtureOnly && input.CaseId == "independent-smoke-46-v1", "Actual capture status differs");
        ArtifactIO.Require(input.Request is not null && input.TokenIdsArtifact is not null && input.MarkerPositionsArtifact is not null && input.TokenIdsArtifact.Bytes == 184 && input.MarkerPositionsArtifact.Bytes == 8, "Input artifact metadata is null or has wrong extent");
        ArtifactIO.Require(input.ModelId == IndependentModelLoader.ModelId && input.SourceModelId == IndependentModelLoader.SourceModelId && input.SourceRevision == IndependentModelLoader.SourceRevision && input.Protocol == "decision-v1" && input.LengthPolicy == "strict" && input.IntegerDtype == "I32" && input.IntegerLayout == "little_endian_contiguous", "Capture source/protocol differs");
        ArtifactIO.Require(input.TokenIds is { Length: 46 } && input.MarkerPositions is { Length: 2 } && input.MarkerPositions.SequenceEqual([25, 35]), "Fixed input shape differs");
        ArtifactIO.Require(ArtifactIO.Hash(IntBytes(input.TokenIds)) == Diagnostic.TokensHash && ArtifactIO.Hash(IntBytes(input.MarkerPositions)) == Diagnostic.MarkersHash && input.Request.Sha256 == Diagnostic.RequestHash, "Fixed input hashes differ");
    }
    internal static void ValidateResponse(IndependentDecisionResponse response, string backend)
    {
        ArtifactIO.Require(response.Backend == (backend == "cuda" ? "cuda-independent-linear-marker-head" : "cpu-independent-linear-marker-head") && response.ModelId == IndependentModelLoader.ModelId && response.Protocol == "decision-v1" && response.Status == "uncalibrated" && response.Answers.Count == 1, "Actual typed response/backend differs");
        var answer = response.Answers[0];
        ArtifactIO.Require(answer.QuestionId == "intent" && answer.Type == MarkerQuestionType.Choice && answer.SelectedIndex is >= 0 and <= 1 && answer.CandidateIds.SequenceEqual(["a", "b"]) && answer.Probabilities.Count == 2 && answer.Probabilities.All(value => double.IsFinite(value) && value is >= 0 and <= 1) && Math.Abs(answer.Probabilities.Sum() - 1) <= 1e-9 && double.IsFinite(answer.Concentration) && answer.Concentration is >= 0 and <= 1 && answer.Status == "uncalibrated", "Typed finite choice output differs");
    }
    private static void Clean(CudaMemorySnapshot snapshot) => ArtifactIO.Require(snapshot.OwnedBytes == 0 && snapshot.OwnedAllocationCount == 0 && snapshot.LoadedModuleCount == 0 && snapshot.ReleaseFailureCount == 0 && snapshot.PeakOwnedBytes <= 3UL * 1024 * 1024 * 1024, "CUDA owned resources/release failures/peak exceed bounds");
}
