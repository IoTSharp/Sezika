using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;
using Sezika.Benchmarks;

return Benchmark.Run(args);

internal static class Benchmark
{
    public static int Run(string[] args)
    {
        var report = new BenchmarkReport { Arguments = args };
        var watch = Stopwatch.StartNew();
        string? output = null;
        try
        {
            var options = Options.Parse(args);
            if (options.SelfTest) { SelfTest(); return 0; }
            if (options.Mode == "profile") report.Profile = new PerformanceProfile
            {
                SelectedLengths = options.Lengths, SelectedQuestions = options.Questions,
            };
            var destination = Path.GetFullPath(options.Output);
            if (File.Exists(destination)) throw new ArgumentException("Report already exists; choose a new output path.");
            output = destination;
            report.Backend = options.Backend;
            report.Precision = options.Backend == "int8" ? "W8A32 row-symmetric int8 linear weights, FP32 activations/accumulation; embeddings/norm FP32" : "FP32";
            report.Runtime = RuntimeInformation.FrameworkDescription;
            report.Rid = RuntimeInformation.RuntimeIdentifier;
            report.OperatingSystem = RuntimeInformation.OSDescription;
            report.Cpu = options.Cpu;
            report.EnvironmentLabel = options.EnvironmentLabel;
            report.NativeAot = !RuntimeFeature.IsDynamicCodeSupported;
            report.LogicalProcessors = Environment.ProcessorCount;
            report.VectorFloatWidth = Vector<float>.Count;
            report.Samples = options.Samples;
            report.Warmup = options.Warmup;
            report.TimeoutSeconds = options.TimeoutSeconds;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; timeout.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                if (report.Profile is not null) PerformanceProfiler.PreparePlan(options, report.Profile, timeout.Token);
                if (options.RequireAot && !report.NativeAot) throw new InvalidOperationException("Native AOT was required but this process supports dynamic code.");
                report.Phase = "identity";
                report.ManifestSha256 = FileHash(Path.Combine(options.Model, "model.json"));
                report.ExecutableSha256 = FileHash(Environment.ProcessPath!);
                report.CodeArtifacts.Add(new("process_executable", Environment.ProcessPath!, report.ExecutableSha256));
                if (!report.NativeAot)
                {
                    // Hash the three fixed application assemblies before loading the model.
                    string[] assemblyFiles = ["Sezika.Benchmarks.dll", "Sezika.dll", "Sezika.Cuda.dll"];
                    foreach (var assemblyFile in assemblyFiles)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        var assemblyPath = Path.Combine(AppContext.BaseDirectory, assemblyFile);
                        report.CodeArtifacts.Add(new("managed_assembly", assemblyPath, FileHash(assemblyPath)));
                    }
                }
                report.ManagedBytesBefore = GC.GetTotalMemory(true);
                // At most two complete load/unload cycles, all calls share a wall-clock cancellation token.
                var references = new List<WeakReference>();
                for (var cycle = 0; cycle < options.Cycles; cycle++)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    references.AddRange(RunCycle(options, report, cycle, timeout.Token));
                }
                report.Phase = "collection";
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                report.ManagedBytesAfterCollection = GC.GetTotalMemory(true);
                report.ModelObjectsCollected = references.All(reference => !reference.IsAlive);
                Require(report.ModelObjectsCollected, "Disposed model/session objects remain rooted.");
                report.Diagnostics.Add("model_encoder_head_pipeline_embedding_sentinels_collected_after_unload");
                report.Status = "passed";
                report.Phase = "complete";
            }
            finally { Console.CancelKeyPress -= cancel; }
        }
        catch (Exception exception)
        {
            report.Status = "failed";
            if (report.Profile is not null) report.Profile.Status = "incomplete";
            report.ErrorCode = exception switch
            {
                DecisionException decision => decision.Code,
                CudaException cuda => cuda.Code,
                OperationCanceledException => "benchmark_cancelled_or_timeout",
                _ => exception.GetType().Name,
            };
            report.Error = exception.Message;
            Console.Error.WriteLine($"{report.Phase}: {report.ErrorCode}: {report.Error}");
        }
        finally
        {
            report.ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
            try
            {
                using var process = Process.GetCurrentProcess();
                report.PeakWorkingSetBytes = process.PeakWorkingSet64;
            }
            catch (Exception exception)
            {
                report.Diagnostics.Add($"process_peak_observation_failed: {exception.GetType().Name}: {exception.Message}");
                if (report.Status != "failed")
                {
                    report.Status = "failed";
                    report.ErrorCode = "process_peak_observation_failed";
                    report.Error = exception.Message;
                }
                if (report.Profile is not null) report.Profile.Status = "incomplete";
            }
            if (output is not null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    // Never overwrite another run's evidence, including on failure.
                    using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
                    JsonSerializer.Serialize(stream, report, ReportJsonContext.Default.BenchmarkReport);
                    Console.WriteLine($"{report.Status}: {output}");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    report.Status = "failed";
                    Console.Error.WriteLine($"report_write_failed: {exception.Message}");
                }
            }
        }
        return report.Status == "passed" ? 0 : 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] RunCycle(Options options, BenchmarkReport report, int cycle, CancellationToken token)
    {
        report.Phase = $"load_cycle_{cycle}";
        Console.WriteLine($"{report.Phase}: {options.Backend}");
        var total = Stopwatch.StartNew();
        var watch = Stopwatch.StartNew();
        var mode = options.Backend switch { "simd" => EncoderKernelMode.Simd, "int8" => EncoderKernelMode.QuantizedInt8, _ => EncoderKernelMode.Scalar };
        using var model = ModernBertModelLoader.Load(options.Model, new EncoderExecutionOptions { Kernel = mode }, token);
        var loadMilliseconds = watch.Elapsed.TotalMilliseconds;
        var weak = new WeakReference(model.Encoder.Weights.TokenEmbeddings);
        report.QuantizedEncoderBytes = model.Encoder.QuantizedWeightBytes;
        CudaDevice? device = null;
        CudaModernBertEncoder? gpuEncoder = null;
        IMarkerDecisionPipeline? pipeline = null;
        ModernBertDecisionEngine? engine = null;
        ProfiledPipeline? profiledPipeline = null;
        try
        {
            watch.Restart();
            if (options.Backend == "cuda")
            {
                // Explicit CUDA selection: an absent driver/device is a failed row, never a CPU fallback.
                device = CudaDevice.Open(enableProfiling: true);
                report.Gpu = device.Info;
                gpuEncoder = new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, token);
                pipeline = new CudaDecisionPipeline(device, gpuEncoder, model.Head, token);
                if (cycle == 0) report.CudaLoad = device.GetTelemetry();
                device.ProfilingEnabled = false;
            }
            else
            {
                var cpu = new ModernBertDecisionPipeline(model.Encoder, model.Head, token);
                report.QuantizedHeadBytes = cpu.QuantizedWeightBytes;
                pipeline = cpu;
            }
            if (options.Mode == "profile")
            {
                profiledPipeline = new ProfiledPipeline(pipeline);
                pipeline = profiledPipeline;
            }
            var requestBudget = new DecisionResourceBudget
            {
                MaxTokens = 32768, Deadline = TimeSpan.FromMinutes(5), MaxResidentBytes = 4L * 1024 * 1024 * 1024,
            };
            report.RequestTokenBudget = requestBudget.MaxTokens;
            report.RequestDeadlineSeconds = requestBudget.Deadline.TotalSeconds;
            engine = new ModernBertDecisionEngine(model, budget: requestBudget,
                pipeline: pipeline, backend: options.Backend + "-modernbert-marker-head");
            var backendMilliseconds = watch.Elapsed.TotalMilliseconds;
            var coldJson = RequestJson(3);
            watch.Restart();
            var cold = Evaluate(engine, coldJson, token);
            var coldMilliseconds = watch.Elapsed.TotalMilliseconds;
            report.Loads.Add(new(cycle, loadMilliseconds, backendMilliseconds, coldMilliseconds, total.Elapsed.TotalMilliseconds));
            Validate(cold, 3);
            if (cycle == 0)
            {
                if (profiledPipeline is not null)
                    PerformanceProfiler.Run(options, report, model, engine, profiledPipeline, device, token);
                else
                {
                    foreach (var count in options.Questions)
                    {
                        var json = RequestJson(count);
                        report.Phase = $"benchmark_{count}_questions";
                        for (var iteration = 0; iteration < options.Warmup; iteration++)
                        {
                            token.ThrowIfCancellationRequested();
                            _ = Evaluate(engine, json, token);
                        }
                        var times = new double[options.Samples];
                        var allocations = new long[options.Samples];
                        DecisionResponse response = cold;
                        for (var iteration = 0; iteration < options.Samples; iteration++)
                        {
                            token.ThrowIfCancellationRequested();
                            Console.WriteLine($"{report.Phase}: sample {iteration + 1}/{options.Samples}");
                            var before = GC.GetTotalAllocatedBytes(true);
                            watch.Restart();
                            response = Evaluate(engine, json, token);
                            times[iteration] = watch.Elapsed.TotalMilliseconds;
                            allocations[iteration] = GC.GetTotalAllocatedBytes(true) - before;
                            Validate(response, count);
                        }
                        var requestsPerSecond = times.Length * 1000 / times.Sum();
                        report.Requests.Add(new(count, json, Hash(Encoding.UTF8.GetBytes(json)), response.Usage!.TokenCount,
                            2, times, allocations, Distribution.From(times), requestsPerSecond, requestsPerSecond * count, response));
                    }
                }
                report.Phase = "alignment";
                report.Alignment = Align(model, profiledPipeline?.Inner ?? pipeline, gpuEncoder, options.Backend, token);
                var alignment = report.Alignment;
                Require(alignment.EncoderMaxAbsoluteError <= alignment.EncoderTolerance &&
                    alignment.LogitsMaxAbsoluteError <= alignment.LogitsTolerance &&
                    alignment.ProbabilityMaxAbsoluteError <= alignment.ProbabilityTolerance &&
                    (alignment.HeadOnlyMaxAbsoluteError is null || alignment.HeadOnlyMaxAbsoluteError <= alignment.LogitsTolerance),
                    $"Numerical smoke failed: encoder={alignment.EncoderMaxAbsoluteError:R}, logits={alignment.LogitsMaxAbsoluteError:R}, probabilities={alignment.ProbabilityMaxAbsoluteError:R}.");
                if (device is not null)
                {
                    report.Phase = "cuda_profile";
                    report.CudaBeforeUnload = device.GetMemorySnapshot();
                    device.ResetTelemetry(); device.ProfilingEnabled = true;
                    watch.Restart();
                    Validate(Evaluate(engine, coldJson, token), 3);
                    report.ProfiledForwardWallMilliseconds = watch.Elapsed.TotalMilliseconds;
                    report.CudaProfiledForward = device.GetTelemetry();
                    device.ProfilingEnabled = false;
                }
                report.Phase = "diagnostics";
                Diagnostics(model, engine, pipeline, gpuEncoder, device, report, token);
            }
            report.Phase = "unload";
            if (device is not null && cycle == 0)
            {
                var currentMemory = device.GetMemorySnapshot();
                if (report.CudaBeforeUnload is null || currentMemory.PeakOwnedBytes > report.CudaBeforeUnload.PeakOwnedBytes)
                    report.CudaBeforeUnload = currentMemory;
            }
            engine.Dispose();
            gpuEncoder?.Dispose();
            Require(model.Encoder.WorkspacePool.ActiveCount == 0 && model.Encoder.WorkspacePool.OutstandingBytes == 0,
                "Encoder workspace was not returned.");
            if (device is not null)
            {
                report.CudaAfterUnload = device.GetMemorySnapshot();
                Require(report.CudaAfterUnload.OwnedBytes == 0 && report.CudaAfterUnload.OwnedAllocationCount == 0 &&
                    report.CudaAfterUnload.LoadedModuleCount == 0 && report.CudaAfterUnload.ReleaseFailureCount == 0,
                    "CUDA resources remain after unload or a driver release failed.");
            }
            try { _ = engine.Evaluate(Parse(coldJson), token); throw new InvalidOperationException("Disposed engine accepted a request."); }
            catch (ObjectDisposedException) { report.Diagnostics.Add($"cycle_{cycle}_unload_fail_closed"); }
            return [weak, new(model), new(model.Encoder), new(model.Head), new(pipeline), new(engine)];
        }
        finally
        {
            try { engine?.Dispose(); }
            finally
            {
                try { pipeline?.Dispose(); }
                finally { try { gpuEncoder?.Dispose(); } finally { device?.Dispose(); } }
            }
        }
    }

    internal static DecisionResponse Evaluate(ModernBertDecisionEngine engine, string json, CancellationToken token)
    {
        var response = engine.Evaluate(Parse(json), token);
        _ = JsonSerializer.Serialize(response, DecisionJsonContext.Default.DecisionResponse);
        return response;
    }

    private static DecisionRequest Parse(string json) => DecisionRequestParser.Parse(Encoding.UTF8.GetBytes(json));

    private static AlignmentMeasurement Align(ModernBertModelPackage model, IMarkerDecisionPipeline pipeline,
        CudaModernBertEncoder? gpu, string backend, CancellationToken token)
    {
        int[] tokens = [model.Tokenizer.BosId, model.Tokenizer.MaskId, model.Tokenizer.MaskId, model.Tokenizer.EosId];
        int[] markers = [1, 2];
        using var scalar = new ModernBertEncoder(model.Encoder.Config, model.Encoder.Weights);
        using var oracle = new ModernBertDecisionPipeline(scalar, model.Head);
        var expectedHidden = scalar.Encode(tokens, token);
        var actualHidden = gpu is null ? model.Encoder.Encode(tokens, token) : gpu.Encode(tokens, token);
        var encoderError = MaxError(expectedHidden, actualHidden);
        var expected = new List<float>(); var actual = new List<float>();
        float probabilityError = 0;
        float? headOnlyError = pipeline is ModernBertDecisionPipeline ? 0 : null;
        for (var type = 0; type < 3; type++)
        {
            token.ThrowIfCancellationRequested();
            var e = oracle.Score(tokens, type, markers, token);
            var a = pipeline.Score(tokens, type, markers, token);
            expected.AddRange(e); actual.AddRange(a);
            if (pipeline is ModernBertDecisionPipeline cpu)
                headOnlyError = Math.Max(headOnlyError!.Value, MaxError(oracle.ScoreEncoded(expectedHidden, type, markers, token),
                    cpu.ScoreEncoded(expectedHidden, type, markers, token)));
            probabilityError = Math.Max(probabilityError, MaxError(DecisionMath.Softmax(e, model.Temperature[type]),
                DecisionMath.Softmax(a, model.Temperature[type])));
        }
        var logitError = MaxError(expected.ToArray(), actual.ToArray());
        // Frozen numerical smoke tolerances, separate from any multilingual quality gate.
        var encoderTolerance = backend == "int8" ? 2f : 0.05f;
        var logitTolerance = backend == "int8" ? 0.5f : 0.005f;
        var probabilityTolerance = backend == "int8" ? 0.1f : 0.002f;
        var measurement = new AlignmentMeasurement(tokens, expected.ToArray(), actual.ToArray(), encoderError,
            logitError, probabilityError, headOnlyError, encoderTolerance, logitTolerance, probabilityTolerance);
        return measurement;
    }

    private static void Diagnostics(ModernBertModelPackage model, ModernBertDecisionEngine engine,
        IMarkerDecisionPipeline pipeline, CudaModernBertEncoder? gpu, CudaDevice? device,
        BenchmarkReport report, CancellationToken token)
    {
        int[] tokens = [model.Tokenizer.BosId, model.Tokenizer.MaskId, model.Tokenizer.MaskId, model.Tokenizer.EosId];
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { _ = engine.Evaluate(Parse(RequestJson(1)), cancelled.Token); throw new InvalidOperationException("Cancellation not observed."); }
        catch (DecisionException exception) when (exception.Code == "decision_cancelled") { report.Diagnostics.Add(exception.Code); }
        using var inFlight = CancellationTokenSource.CreateLinkedTokenSource(token);
        var watch = new Stopwatch();
        var entered = false;
        void CancelAtTrace(string _, float[] __) { entered = true; watch.Start(); inFlight.Cancel(); }
        if (gpu is null) model.Encoder.Trace = CancelAtTrace; else gpu.Trace = CancelAtTrace;
        try { _ = pipeline.Score(tokens, 0, [1, 2], inFlight.Token); throw new InvalidOperationException("In-flight cancellation not observed."); }
        catch (OperationCanceledException) when (entered)
        { report.Diagnostics.Add($"cancel_after_first_encoder_trace_ms={watch.Elapsed.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture)}"); }
        finally { if (gpu is null) model.Encoder.Trace = null; else gpu.Trace = null; }
        Require(model.Encoder.WorkspacePool.ActiveCount == 0, "Cancellation leaked CPU workspace.");
        try { _ = pipeline.Score(tokens, 9, [1, 2], token); throw new InvalidOperationException("Invalid type accepted."); }
        catch (DecisionException exception) when (exception.Code == "decision_head_input_invalid") { report.Diagnostics.Add(exception.Code); }
        if (device is not null && gpu is not null)
        {
            var before = device.GetMemorySnapshot();
            try
            {
                using var rejected = new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, cancelled.Token);
                throw new InvalidOperationException("Cancelled CUDA upload accepted.");
            }
            catch (OperationCanceledException) { report.Diagnostics.Add("cuda_cancelled_constructor"); }
            var after = device.GetMemorySnapshot();
            Require(before.OwnedBytes == after.OwnedBytes && before.OwnedAllocationCount == after.OwnedAllocationCount &&
                before.LoadedModuleCount == after.LoadedModuleCount, "Cancelled CUDA construction leaked resources.");
        }
        // Proves cancellation/invalid input did not poison a live session.
        Validate(Evaluate(engine, RequestJson(3), token), 3);
        report.Diagnostics.Add("recovery_after_cancel_and_invalid_input");
    }

    internal static string RequestJson(int count, string? stateText = null)
    {
        using var state = JsonDocument.Parse(JsonSerializer.Serialize(stateText ?? "help", ReportJsonContext.Default.String));
        using var instruction = JsonDocument.Parse("\"type\"");
        using var yes = JsonDocument.Parse("\"yes\"");
        using var no = JsonDocument.Parse("\"no\"");
        var questions = new Dictionary<string, Question>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            Question question = (i % 3) switch
            {
                0 => new ChoiceQuestion { Instructions = instruction.RootElement.Clone(), Criteria = new(StringComparer.Ordinal)
                    { ["a"] = yes.RootElement.Clone(), ["b"] = no.RootElement.Clone() } },
                1 => new ScoreQuestion { Instructions = instruction.RootElement.Clone(), Criteria = [yes.RootElement.Clone(), no.RootElement.Clone()] },
                _ => new BooleanQuestion { Instructions = instruction.RootElement.Clone(), Criteria = new()
                    { WhenTrue = yes.RootElement.Clone(), WhenFalse = no.RootElement.Clone() } },
            };
            questions.Add("q" + i.ToString(CultureInfo.InvariantCulture), question);
        }
        return JsonSerializer.Serialize(new DecisionRequest { Model = ModernBertModelLoader.PinnedModelId,
            State = state.RootElement.Clone(), Questions = questions }, DecisionJsonContext.Default.DecisionRequest);
    }

    internal static void Validate(DecisionResponse response, int count)
    {
        Require(response.Answers.Count == count && response.Usage?.MicroBatchCount == count && response.Usage.TokenCount > 0,
            "Question/token/micro-batch accounting is inconsistent.");
        foreach (var answer in response.Answers.Values)
        {
            Require(answer.Status == "answered" && answer.Calibration.Status == "uncalibrated", "Unexpected decision semantics.");
            switch (answer)
            {
                case ChoiceAnswer choice:
                    Require(choice.Probabilities.ContainsKey(choice.Choice), "Unknown choice."); CheckProbabilities(choice.Probabilities); break;
                case ScoreAnswer score:
                    Require(double.IsFinite(score.Score) && score.Score is >= 0 and <= 1 && score.Legend.Count == 2, "Invalid score."); CheckProbabilities(score.Probabilities); break;
                case BooleanAnswer boolean:
                    Require(double.IsFinite(boolean.ProbabilityTrue) && boolean.ProbabilityTrue is >= 0 and <= 1, "Invalid probability."); break;
                default: throw new InvalidOperationException("Unknown answer type.");
            }
        }
    }
    private static void CheckProbabilities(Dictionary<string, double> values) => Require(values.Count == 2 &&
        values.Values.All(value => double.IsFinite(value) && value is >= 0 and <= 1) && Math.Abs(values.Values.Sum() - 1) < 1e-5,
        "Probability distribution is invalid.");
    private static float MaxError(float[] expected, float[] actual)
    {
        Require(expected.Length == actual.Length && expected.Length > 0, "Alignment shapes differ.");
        float error = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            Require(float.IsFinite(expected[i]) && float.IsFinite(actual[i]), "Nonfinite alignment value.");
            error = Math.Max(error, Math.Abs(expected[i] - actual[i]));
        }
        return error;
    }
    private static float MaxError(double[] expected, double[] actual)
    {
        Require(expected.Length == actual.Length && expected.Length > 0, "Alignment shapes differ.");
        double error = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            Require(double.IsFinite(expected[i]) && double.IsFinite(actual[i]), "Nonfinite alignment value.");
            error = Math.Max(error, Math.Abs(expected[i] - actual[i]));
        }
        return (float)error;
    }
    private static string FileHash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void SelfTest()
    {
        var d = Distribution.From([5, 1, 4, 2, 3]);
        Require(d.P50 == 3 && d.P95 == 5 && d.P99 == 5, "Nearest-rank percentiles failed.");
        var singleton = Distribution.From([7]); Require(singleton.P50 == 7 && singleton.P95 == 7, "Single sample percentile failed.");
        var request = Parse(RequestJson(3));
        Require(request.Questions.Count == 3 && request.Questions["q2"] is BooleanQuestion, "Source-generated request contract failed.");
        try { _ = Options.Parse(["--samples", "0"]); throw new InvalidOperationException("Unbounded samples accepted."); }
        catch (ArgumentException) { }
        Console.WriteLine("benchmark_self_test passed: percentiles, singleton, typed request, input bounds");
    }
}

internal sealed record Options(string Model, string Backend, string Output, int Samples, int Warmup, int Cycles,
    int TimeoutSeconds, int[] Questions, string Cpu, string EnvironmentLabel, bool RequireAot, bool SelfTest,
    string Mode, string[] Lengths)
{
    public static Options Parse(string[] args)
    {
        if (args.Length > 40) throw new ArgumentException("At most 40 arguments are accepted.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var requireAot = false; var selfTest = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--require-aot") { requireAot = true; continue; }
            if (args[i] == "--self-test") { selfTest = true; continue; }
            var key = args[i];
            if (key is not ("--model" or "--backend" or "--output" or "--samples" or "--warmup" or "--cycles" or
                "--timeout-seconds" or "--questions" or "--cpu" or "--environment" or "--mode" or "--lengths") || ++i >= args.Length || !values.TryAdd(key, args[i]))
                throw new ArgumentException($"Unknown, duplicate or incomplete option: {key}.");
        }
        string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);
        int Number(string key, int fallback, int min, int max)
        {
            if (!int.TryParse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture, out var n) || n < min || n > max)
                throw new ArgumentException($"{key} must be in [{min},{max}].");
            return n;
        }
        var backend = Get("--backend", "simd");
        if (backend is not ("scalar" or "simd" or "int8" or "cuda")) throw new ArgumentException("Backend must be scalar, simd, int8 or cuda.");
        var parts = Get("--questions", "1,8,32").Split(',');
        if (parts.Length is < 1 or > 3) throw new ArgumentException("Supply at most three question counts.");
        var counts = parts.Select(part => int.TryParse(part, out var n) && n is >= 1 and <= 32 ? n : throw new ArgumentException("Questions must be 1..32.")).ToArray();
        var mode = Get("--mode", "benchmark");
        if (mode is not ("benchmark" or "profile")) throw new ArgumentException("Mode must be benchmark or profile.");
        var lengths = Get("--lengths", "short,medium,long").Split(',');
        if (lengths.Length is < 1 or > 3 || lengths.Distinct(StringComparer.Ordinal).Count() != lengths.Length ||
            lengths.Any(length => length is not ("short" or "medium" or "long")))
            throw new ArgumentException("Lengths must be unique short,medium,long entries (at most three).");
        if (mode == "benchmark" && values.ContainsKey("--lengths")) throw new ArgumentException("--lengths requires --mode profile.");
        if (mode == "profile" && (counts.Distinct().Count() != counts.Length || counts.Any(count => count is not (1 or 8 or 32))))
            throw new ArgumentException("Profile question counts must be unique entries from 1,8,32.");
        return new(Get("--model", ".artifacts/models/laya-mmbert"), backend, Get("--output", ".artifacts/s5/report.json"),
            Number("--samples", 5, 1, 30), Number("--warmup", 1, 0, 5), Number("--cycles", 2, 1, 2),
            Number("--timeout-seconds", 1200, 1, 1800), counts, Get("--cpu", "unspecified"), Get("--environment", "unspecified"), requireAot, selfTest, mode, lengths);
    }
}
