using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;
using Sezika.OracleCapture;

return await CaptureRunner.RunAsync(args);

internal static class CaptureRunner
{
    private const int MaxJsonBytes = 32 * 1024 * 1024;
    private const string UpstreamRevision = "4066d5d5fbf08b66c6757ddeedbd797bd7655bc0";

    public static async Task<int> RunAsync(string[] args)
    {
        Options options;
        try { options = Options.Parse(args); }
        catch (ArgumentException exception) { Console.Error.WriteLine(exception.Message); return 2; }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; deadline.Cancel(); };
        Console.CancelKeyPress += cancel;
        var watch = Stopwatch.StartNew();
        Capture? capture = null;
        var ownsOutput = false;
        try
        {
            var token = deadline.Token;
            if (options.RequireAot && (RuntimeFeature.IsDynamicCodeSupported || RuntimeFeature.IsDynamicCodeCompiled))
                throw new InvalidOperationException("--require-aot requires a Native AOT executable with dynamic code disabled.");
            Console.Error.WriteLine("Read bounded local reference inputs and frozen identities; reference numerical outputs are not deserialized.");
            var referenceBytes = await ReadAsync(options.Reference, token);
            var casesBytes = await ReadAsync(options.Cases, token);
            var contractBytes = await ReadAsync(options.Contract, token);
            var reference = JsonSerializer.Deserialize(referenceBytes, CaptureJsonContext.Default.ReferenceCapture)
                ?? throw new InvalidDataException("Reference capture is null.");
            var manifest = JsonSerializer.Deserialize(casesBytes, CaptureJsonContext.Default.InputManifest)
                ?? throw new InvalidDataException("Input manifest is null.");
            using var contract = JsonDocument.Parse(contractBytes);
            if (contract.RootElement.GetProperty("schema_version").GetString() != "sezika.laya-oracle-contract.v1" ||
                contract.RootElement.GetProperty("output_schema_version").GetString() != "sezika.laya-oracle.v1")
                throw new InvalidDataException("Unsupported frozen contract.");
            var identity = CreateIdentity(casesBytes, contractBytes);
            ValidateReference(reference, manifest, identity, token);
            var selected = Select(reference, options);
            var selectedSet = selected.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            if (Directory.Exists(options.Output) || File.Exists(options.Output))
                throw new IOException("Output must be a new directory; existing evidence is never overwritten.");
            if (Within(options.Output, options.Model))
                throw new IOException("Output must be outside the model asset directory.");
            Directory.CreateDirectory(options.Output);
            ownsOutput = true;
            using var process = Process.GetCurrentProcess();
            capture = new Capture
            {
                Provenance = identity, ReferenceSha256 = Hash(referenceBytes), StartedUtc = DateTimeOffset.UtcNow,
                Implementation = new()
                {
                    Backend = options.Backend + "_fp32", LengthPolicy = options.LengthPolicy,
                    Framework = RuntimeInformation.FrameworkDescription, OperatingSystem = RuntimeInformation.OSDescription,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(), BinarySha256 = BinaryHashes(token),
                    RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier, RequireAot = options.RequireAot,
                    IsDynamicCodeSupported = RuntimeFeature.IsDynamicCodeSupported,
                    IsDynamicCodeCompiled = RuntimeFeature.IsDynamicCodeCompiled,
                    ProcessId = Environment.ProcessId, ProcessStartedUtc = process.StartTime.ToUniversalTime(),
                    Arguments = Environment.GetCommandLineArgs(), TimeoutSeconds = options.TimeoutSeconds, MaxCases = options.MaxCases,
                },
                SelectedCaseIds = selected.Select(row => row.Id).ToArray(), TotalManifestCases = manifest.Cases.Length,
                MissingManifestCaseIds = manifest.Cases.Where(row => !selectedSet.Contains(row.Id)).Select(row => row.Id).ToArray(),
                ReferenceCaseCount = reference.Cases.Length,
            };
            Console.Error.WriteLine($"Loading verified local model for {selected.Length} case(s), backend={options.Backend}, length_policy={options.LengthPolicy}.");
            RunInference(options, selected, capture, token);
            capture.MeasurementStatus = "completed";
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"capture_failed: {exception.GetType().Name}: {exception.Message}");
            if (capture is not null)
            {
                capture.MeasurementStatus = "incomplete";
                capture.FatalError = $"{exception.GetType().Name}: {exception.Message}";
            }
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            if (capture is not null && ownsOutput)
            {
                capture.EndedUtc = DateTimeOffset.UtcNow;
                capture.ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
                var completed = capture.Cases.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
                capture.UnprocessedCaseIds = capture.SelectedCaseIds.Where(id => !completed.Contains(id)).ToArray();
                try
                {
                    // Writing a bounded report after cancellation retains the completed observations.
                    await using var output = new FileStream(Path.Combine(options.Output, "capture.json"), FileMode.CreateNew, FileAccess.Write);
                    using var outputDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await JsonSerializer.SerializeAsync(output, capture, CaptureJsonContext.Default.Capture, outputDeadline.Token);
                    Console.Error.WriteLine($"{capture.MeasurementStatus}: {capture.Cases.Count}/{capture.SelectedCaseIds.Length} selected rows; {Path.Combine(options.Output, "capture.json")}");
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or OperationCanceledException)
                {
                    capture.MeasurementStatus = "report_write_failed";
                    Console.Error.WriteLine($"capture_report_write_failed: {exception.Message}");
                }
            }
        }
        return capture?.MeasurementStatus == "completed" ? 0 : 2;
    }

    private static void RunInference(Options options, ReferenceCase[] selected, Capture capture, CancellationToken token)
    {
        var execution = new EncoderExecutionOptions { Kernel = options.Backend == "simd" ? EncoderKernelMode.Simd : EncoderKernelMode.Scalar };
        using var model = ModernBertModelLoader.Load(options.Model, execution, token);
        if (model.ModelId != capture.Provenance.ModelId || model.Revision != capture.Provenance.ModelRevision ||
            model.HeadMaxTokens != 256 || model.Temperature.Length != 3 || model.Temperature.Any(value => value != 1f))
            throw new InvalidDataException("Loaded model does not satisfy the frozen identity, prefix budget or fixed temperature contract.");
        CudaDevice? device = null;
        CudaModernBertEncoder? gpu = null;
        RecordingPipeline? recording = null;
        ModernBertDecisionEngine? engine = null;
        try
        {
            IMarkerDecisionPipeline backend;
            if (options.Backend == "cuda")
            {
                device = CudaDevice.Open(); // Explicit CUDA selection never falls back to CPU.
                gpu = new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, token);
                backend = new CudaDecisionPipeline(device, gpu, model.Head, token);
            }
            else backend = new ModernBertDecisionPipeline(model.Encoder, model.Head, token);
            recording = new RecordingPipeline(backend);
            engine = new ModernBertDecisionEngine(model, recording, options.Backend + "-modernbert-marker-head",
                budget: new DecisionResourceBudget { MaxQuestions = 1, MaxTokens = 1024, MaxResidentBytes = 4L * 1024 * 1024 * 1024,
                    Deadline = TimeSpan.FromMinutes(5) });
            for (var index = 0; index < selected.Length; index++) // <= 46 selected rows, outer cancellation and per-request deadline.
            {
                token.ThrowIfCancellationRequested();
                Console.Error.WriteLine($"capture {index + 1}/{selected.Length}: {selected[index].Id}");
                capture.Cases.Add(Evaluate(selected[index], options, model, engine, recording, token));
            }
        }
        finally
        {
            try { if (engine is not null) engine.Dispose(); else recording?.Dispose(); }
            finally { try { gpu?.Dispose(); } finally { device?.Dispose(); } }
        }
    }

    private static CapturedCase Evaluate(ReferenceCase source, Options options, ModernBertModelPackage model,
        ModernBertDecisionEngine engine, RecordingPipeline recording, CancellationToken token)
    {
        var row = new CapturedCase { Id = source.Id, Primitive = source.Primitive, Input = source.Input.Clone() };
        var watch = Stopwatch.StartNew();
        var stage = "validate";
        try
        {
            var request = InputAdapter.Request(source, options.LengthPolicy, row.ContractDifferences, token);
            DecisionRequestValidator.Validate(request);
            stage = "encode";
            var sequence = PromptSequenceBuilder.Build(model.Tokenizer, request.State, request.Questions["q"],
                new PromptSequenceOptions { PrefixTokenBudget = 256, TotalTokenBudget = 1024, LengthPolicy = options.LengthPolicy }, token);
            row.TokenIds = sequence.TokenIds;
            row.MarkerPositions = sequence.MarkerPositions;
            row.CandidateLabels = sequence.CandidateLabels;
            row.SequenceDiagnostics = sequence.Diagnostics;
            recording.Begin(sequence);
            stage = "infer";
            var response = engine.Evaluate(request, token);
            stage = "decode";
            if (recording.Calls != 1 || recording.Logits is not { } raw || raw.Length != sequence.CandidateLabels.Length ||
                response.Answers.Count != 1 || !response.Answers.TryGetValue("q", out var answer) || answer.Status != "answered")
                throw new InvalidDataException("Actual typed evaluation did not produce one answered backend observation.");
            var probabilities = DecisionMath.Softmax(raw, model.Temperature[sequence.TypeId]);
            var prediction = FromAnswer(answer, sequence.CandidateLabels, raw, probabilities);
            row.RawLogits = raw.Select(value => (double)value).ToArray();
            row.Probabilities = probabilities;
            row.Prediction = prediction;
            row.CalibrationStatus = answer.Calibration.Status;
            row.ActualBackend = response.Backend;
            row.Status = "answered";
        }
        catch (OperationCanceledException) { throw; }
        catch (DecisionException exception) when (exception.Code is "decision_cancelled" or "decision_deadline_exceeded") { throw; }
        catch (Exception exception) when (exception is DecisionException or CudaException or OutOfMemoryException)
        {
            if (exception is PromptTruncationException truncation)
            {
                row.SequenceDiagnostics = truncation.Diagnostics;
                row.ContractDifferences.Add(new("length_policy", "strict_rejects_token_loss", source.Status,
                    "The strict request rejects the diagnosed clipping; laya_compatible must be explicitly selected for upstream parity."));
            }
            var code = exception switch { DecisionException decision => decision.Code, CudaException cuda => cuda.Code, _ => exception.GetType().Name };
            var category = exception is OutOfMemoryException || code.Contains("memory", StringComparison.Ordinal) ? "resource_exhausted" :
                code == "decision_numeric_invalid" ? "non_finite_output" : stage == "validate" ? "invalid_question" :
                stage == "encode" ? "sequence_budget_exceeded" : "inference_failed";
            row.Failure = new(stage, category, exception.Message, code);
            // A failed row never retains valid numeric or prediction evidence.
            row.RawLogits = null; row.Probabilities = null; row.Prediction = null;
        }
        finally { row.ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds; }
        return row;
    }

    private static Prediction FromAnswer(Answer answer, string[] labels, float[] logits, double[] probabilities)
    {
        Dictionary<string, double>? recorded;
        Prediction prediction;
        switch (answer)
        {
            case ChoiceAnswer choice:
                recorded = choice.Logits;
                RequireMapped(labels, probabilities, choice.Probabilities);
                prediction = new() { ChoiceLabel = choice.Choice };
                break;
            case ScoreAnswer score:
                recorded = score.Logits;
                RequireMapped(labels, probabilities, score.Probabilities);
                prediction = new() { Score = score.Score };
                break;
            case BooleanAnswer boolean:
                recorded = boolean.Logits;
                if (!labels.SequenceEqual(new[] { "false", "true" }) || boolean.ProbabilityTrue != probabilities[1])
                    throw new InvalidDataException("Typed Boolean response does not map P(true) to the actual true candidate.");
                prediction = new() { Boolean = boolean.ProbabilityTrue >= 0.5, ProbabilityTrue = boolean.ProbabilityTrue };
                break;
            default: throw new InvalidDataException("Unsupported typed answer.");
        }
        RequireMapped(labels, logits.Select(value => (double)value).ToArray(), recorded);
        return prediction;
    }

    private static void RequireMapped(string[] labels, double[] values, Dictionary<string, double>? actual)
    {
        if (actual is null || actual.Count != labels.Length) throw new InvalidDataException("Typed answer candidate count differs from the backend observation.");
        for (var index = 0; index < labels.Length; index++) // <= 32 supported candidates.
            if (!actual.TryGetValue(labels[index], out var value) || value != values[index])
                throw new InvalidDataException("Typed answer does not map the actual backend observations to candidate labels.");
    }

    private static Identity CreateIdentity(byte[] cases, byte[] contract) => new()
    {
        ModelId = ModernBertModelLoader.PinnedModelId, ModelRevision = ModernBertModelLoader.PinnedRevision,
        WeightsSha256 = ModernBertModelLoader.PinnedWeightsSha256, TokenizerSha256 = ModernBertModelLoader.PinnedTokenizerSha256,
        UpstreamSourceRevision = UpstreamRevision, CasesSha256 = Hash(cases), ContractSha256 = Hash(contract),
        MaxLen = 1024, HeadMaxLen = 256, TemperaturePolicy = "checkpoint_fixed_1_no_overrides",
    };

    private static void ValidateReference(ReferenceCapture reference, InputManifest manifest, Identity identity, CancellationToken token)
    {
        if (reference.SchemaVersion != "sezika.laya-oracle.v1" || reference.Provenance != identity ||
            reference.Cases is null || reference.Cases.Length is < 1 or > 46 || manifest.SchemaVersion != "sezika.laya-oracle-inputs.v1" ||
            manifest.Cases is null || manifest.Cases.Length is < 1 or > 46)
            throw new InvalidDataException("Reference schema, asset/source identity, exact contract/input hashes or case count is invalid.");
        var manifestById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in manifest.Cases)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 128 || item.Primitive is not ("choice" or "score" or "boolean") ||
                !manifestById.TryAdd(item.Id, item.Primitive)) throw new InvalidDataException("Manifest identity is invalid or duplicated.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in reference.Cases)
        {
            token.ThrowIfCancellationRequested();
            if (!ids.Add(item.Id) || !manifestById.TryGetValue(item.Id, out var primitive) || item.Primitive != primitive ||
                item.Input.ValueKind != JsonValueKind.Object || item.Status is not ("answered" or "failed"))
                throw new InvalidDataException("Reference case identity, primitive, status or expanded input is invalid.");
        }
    }

    private static ReferenceCase[] Select(ReferenceCapture reference, Options options)
    {
        if (options.CaseIds.Length == 0) return reference.Cases.Take(options.MaxCases).ToArray();
        var byId = reference.Cases.ToDictionary(row => row.Id, StringComparer.Ordinal);
        return options.CaseIds.Select(id => byId.TryGetValue(id, out var row) ? row :
            throw new InvalidDataException($"Selected case is missing from the reference: {id}")).ToArray();
    }

    private static async Task<byte[]> ReadAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        if (input.Length is < 1 or > MaxJsonBytes) throw new InvalidDataException("JSON must be 1 byte to 32 MiB.");
        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, token);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var nodes = 0;
        Inspect(document.RootElement, token, ref nodes);
        return bytes;
    }

    private static void Inspect(JsonElement element, CancellationToken token, ref int nodes)
    {
        token.ThrowIfCancellationRequested();
        if (++nodes > 500_000) throw new InvalidDataException("JSON node limit exceeded.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property: {property.Name}");
                Inspect(property.Value, token, ref nodes);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) Inspect(value, token, ref nodes);
    }

    private static Dictionary<string, string> BinaryHashes(CancellationToken token)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "Sezika.OracleCapture.dll", "Sezika.dll", "Sezika.Cuda.dll" })
        {
            token.ThrowIfCancellationRequested();
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(path)) files.Add(name, FileHash(path, token));
        }
        if (Environment.ProcessPath is { } executable) files.Add("process_executable", FileHash(executable, token));
        return files;
    }

    private static string FileHash(string path, CancellationToken token)
    {
        using var input = File.OpenRead(path);
        if (input.Length > 512L * 1024 * 1024) throw new InvalidDataException("Implementation binary exceeds the 512 MiB identity limit.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        for (var chunk = 0; chunk <= 512; chunk++)
        {
            token.ThrowIfCancellationRequested();
            var read = input.Read(buffer);
            if (read == 0) return Convert.ToHexStringLower(hash.GetHashAndReset());
            hash.AppendData(buffer, 0, read);
        }
        throw new InvalidDataException("Implementation identity hashing exceeded its bounded chunk count.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static bool Within(string path, string directory) => path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
