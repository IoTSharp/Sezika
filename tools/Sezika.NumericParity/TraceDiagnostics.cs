using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;

internal static class TraceDiagnostics
{
    public static int Run(string[] args)
    {
        if (args is ["--trace-self-test"]) return TraceChecks.Run();
        if (args.Length != 10 || args[0] != "--trace-oracle" ||
            !int.TryParse(args[7], CultureInfo.InvariantCulture, out var seconds) || seconds is < 1 or > 1800 ||
            !IsSha(args[8]) || !IsSha(args[9]))
        {
            Console.Error.WriteLine("Usage: --trace-oracle <model-dir> <reference.json> <contract.json> <output.json> <1..3 case-ids CSV> <scalar,simd,cuda> <1..1800 seconds> <reference-sha256> <contract-sha256>; or --trace-self-test");
            return 2;
        }
        var ids = args[5].Split(',');
        if (ids.Length is < 1 or > 3 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || args[6] != "scalar,simd,cuda")
        {
            Console.Error.WriteLine("Select 1..3 distinct case IDs and all three backends in scalar,simd,cuda order.");
            return 2;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; deadline.Cancel(); };
        Console.CancelKeyPress += cancel;
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        try
        {
            var token = deadline.Token;
            var output = Path.GetFullPath(args[4]);
            if (File.Exists(output)) throw new IOException("Output already exists; use a new evidence path.");
            var referenceBytes = ReadFrozen(args[2], args[8], 32 * 1024 * 1024, token);
            var contractBytes = ReadFrozen(args[3], args[9], 1024 * 1024, token);
            var reference = JsonSerializer.Deserialize(referenceBytes, TraceJsonContext.Default.TraceReference)
                ?? throw new InvalidDataException("Reference missing.");
            var tolerances = ReadContract(contractBytes);
            ValidateReference(reference, args[9]);
            var selected = ids.Select(id => reference.Cases.SingleOrDefault(row => row.Id == id)
                ?? throw new InvalidDataException($"Case ID not found: {id}.")).ToArray();
            var rows = new List<TraceCaseReport>();
            for (var index = 0; index < selected.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var source = selected[index];
                ValidateAnswered(source);
                var request = Request(source);
                var snapshots = new Dictionary<string, float[]>(StringComparer.Ordinal);
                var backends = new List<TraceBackendReport>();
                Console.WriteLine($"trace case {index + 1}/{selected.Length}: {source.Id}");
                foreach (var backend in new[] { "scalar", "simd", "cuda" })
                {
                    token.ThrowIfCancellationRequested();
                    Console.WriteLine($"trace backend {backend}: {source.Id}, elapsed {watch.Elapsed.TotalSeconds:F1}/{seconds}s");
                    backends.Add(RunBackend(args[1], backend, source, request, tolerances, snapshots, token));
                }
                rows.Add(new(source.Id, source.Primitive, source.TokenIds!.Length, source.TokenIds,
                    source.MarkerPositions!, source.CandidateLabels!, backends));
                snapshots.Clear();
            }
            var passed = rows.Count == ids.Length && rows.All(row => row.Backends.Count == 3 && row.Backends.All(backend =>
                backend.Status == "compared" && backend.OracleComparison is { Passed: true } && backend.MissingTraces.Count == 0));
            var report = new TraceReport
            {
                StartedUtc = started, ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                ReferenceSha256 = args[8].ToLowerInvariant(), ContractSha256 = args[9].ToLowerInvariant(),
                Provenance = reference.Provenance, Tolerances = tolerances,
                Framework = RuntimeInformation.FrameworkDescription, RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                BinarySha256 = BinaryHashes(token), SelectedCaseIds = ids, ReferenceCases = reference.Cases.Count,
                NearTieCases = rows.Count(row => row.Backends.Any(backend => backend.OracleComparison is { NearTie: true })),
                SelectedDiagnosticsPassed = passed, Cases = rows,
            };
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, report, TraceJsonContext.Default.TraceReport);
            Console.WriteLine($"Wrote {output}; selected diagnostics passed={passed}; near-tie cases={report.NearTieCases}; full S3-06 gate remains separately assessed.");
            return passed ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidOperationException or
            DecisionException or CudaException or OperationCanceledException or DllNotFoundException or BadImageFormatException)
        {
            Console.Error.WriteLine($"trace-diagnostics: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static TraceBackendReport RunBackend(string modelDirectory, string backend, TraceReferenceCase reference,
        DecisionRequest request, TraceTolerances tolerances, Dictionary<string, float[]> snapshots, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        TraceCollector? collector = null;
        try
        {
            using var model = ModernBertModelLoader.Load(modelDirectory, new EncoderExecutionOptions
            {
                Kernel = backend == "simd" ? EncoderKernelMode.Simd : EncoderKernelMode.Scalar,
                Deadline = TimeSpan.FromMinutes(5), MaxConcurrentRequests = 1,
            }, token);
            if (model.Temperature.Length != 3 || model.Temperature.Any(value => value != 1f) || model.HeadMaxTokens != 256)
                throw new InvalidDataException("Model temperature or prefix budget differs from the frozen contract.");
            var sequence = PromptSequenceBuilder.Build(model.Tokenizer, request.State, request.Questions["q"],
                new PromptSequenceOptions { LengthPolicy = PromptLengthPolicy.LayaCompatible }, token);
            if (!sequence.TokenIds.SequenceEqual(reference.TokenIds!) || !sequence.MarkerPositions.SequenceEqual(reference.MarkerPositions!) ||
                !sequence.CandidateLabels.SequenceEqual(reference.CandidateLabels!))
                throw new InvalidDataException("Generated tokens, markers or ordered candidate labels differ from the reference.");
            collector = new TraceCollector(sequence.TokenIds.Length, model.Encoder.Config.HiddenSize, model.Encoder.Config.LayerCount,
                model.Head.Layers.Length, snapshots, backend == "scalar", token);
            using CudaDevice? device = backend == "cuda" ? CudaDevice.Open() : null;
            using CudaModernBertEncoder? gpu = device is null ? null : new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, token);
            using IMarkerDecisionPipeline pipeline = device is null ? new ModernBertDecisionPipeline(model.Encoder, model.Head, token) :
                new CudaDecisionPipeline(device, gpu!, model.Head, token);
            if (gpu is null) model.Encoder.Trace = collector.Observe; else gpu.Trace = collector.Observe;
            if (pipeline is ModernBertDecisionPipeline cpuHead) cpuHead.Trace = collector.Observe;
            if (pipeline is CudaDecisionPipeline gpuHead) gpuHead.Trace = collector.Observe;
            using var recording = new TraceRecordingPipeline(pipeline, sequence);
            using var engine = new ModernBertDecisionEngine(model, recording, backend + "-modernbert-marker-head", budget:
                new DecisionResourceBudget { MaxQuestions = 1, MaxTokens = 1024, MaxResidentBytes = 4L * 1024 * 1024 * 1024, Deadline = TimeSpan.FromMinutes(5) });
            var response = engine.Evaluate(request, token);
            if (recording.Calls != 1 || recording.Logits is not { } logits || response.Answers.Count != 1 ||
                !response.Answers.TryGetValue("q", out var answer) || answer.Status != "answered")
                throw new InvalidDataException("Typed engine did not return one recorded answer.");
            var probabilities = DecisionMath.Softmax(logits);
            ValidateTypedAnswer(answer, sequence.CandidateLabels, logits, probabilities);
            var comparison = TraceComparison.Compare(reference, logits.Select(value => (double)value).ToArray(), probabilities, tolerances);
            if (collector.Missing.Count != 0) throw new InvalidDataException("Expected layer snapshots were not all observed.");
            return new(backend, "compared", null, logits.Select(value => (double)value).ToArray(), probabilities,
                comparison, collector.Differences, [], snapshots.Values.Sum(values => values.LongLength * sizeof(float)), watch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) { throw; }
        catch (DecisionException exception) when (exception.Code is "decision_cancelled" or "decision_deadline_exceeded") { throw; }
        catch (Exception exception) when (exception is IOException or DecisionException or CudaException or DllNotFoundException or BadImageFormatException)
        {
            return new(backend, "failed", $"{exception.GetType().Name}: {exception.Message}", null, null, null,
                collector?.Differences ?? [], collector?.Missing ?? [], snapshots.Values.Sum(values => values.LongLength * sizeof(float)), watch.Elapsed.TotalMilliseconds);
        }
    }

    internal static DecisionRequest Request(TraceReferenceCase reference)
    {
        var input = reference.Input;
        if (input.GetProperty("max_len").GetInt32() != 1024 || input.GetProperty("head_max_len").GetInt32() != 256)
            throw new InvalidDataException("Reference length budgets differ from the frozen contract.");
        var question = input.GetProperty("question");
        var sourceType = question.GetProperty("type").GetString();
        var primitive = sourceType == "noul" ? "boolean" : sourceType;
        if (primitive != reference.Primitive || primitive is not ("choice" or "score" or "boolean"))
            throw new InvalidDataException("Reference primitive and expanded input type differ.");
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", ModernBertModelLoader.PinnedModelId);
            writer.WriteString("length_policy", "laya_compatible");
            writer.WritePropertyName("state"); input.GetProperty("state").WriteTo(writer);
            writer.WriteStartObject("questions"); writer.WriteStartObject("q"); writer.WriteString("type", primitive);
            var properties = 0;
            foreach (var property in question.EnumerateObject())
            {
                if (++properties > 8) throw new InvalidDataException("Reference question has too many fields.");
                if (property.Name != "type") property.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        return DecisionRequestParser.Parse(buffer.ToArray());
    }

    private static void ValidateTypedAnswer(Answer answer, string[] labels, float[] logits, double[] probabilities)
    {
        Dictionary<string, double>? actual;
        switch (answer)
        {
            case ChoiceAnswer choice:
                actual = choice.Logits;
                if (choice.Choice != labels[DecisionMath.ArgMax(probabilities)]) throw new InvalidDataException("Typed Choice prediction mismatch.");
                break;
            case BooleanAnswer boolean:
                actual = boolean.Logits;
                if (labels.Length != 2 || boolean.ProbabilityTrue != probabilities[1]) throw new InvalidDataException("Typed Boolean probability mismatch.");
                break;
            case ScoreAnswer score:
                actual = score.Logits;
                double value = 0;
                for (var index = 0; index < probabilities.Length; index++) value += index * probabilities[index];
                if (score.Score != value) throw new InvalidDataException("Typed Score prediction mismatch.");
                break;
            default: throw new InvalidDataException("Unknown typed answer.");
        }
        if (actual is null || actual.Count != labels.Length) throw new InvalidDataException("Typed candidate count mismatch.");
        for (var index = 0; index < labels.Length; index++)
            if (!actual.TryGetValue(labels[index], out var observed) || observed != logits[index]) throw new InvalidDataException("Typed candidate logit mismatch.");
    }

    private static TraceTolerances ReadContract(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetString() != "sezika.laya-oracle-contract.v1" || root.GetProperty("status").GetString() != "frozen_before_measurement")
            throw new InvalidDataException("Expected a frozen v1 oracle contract.");
        var values = JsonSerializer.Deserialize(root.GetProperty("tolerances"), TraceJsonContext.Default.TraceTolerances)
            ?? throw new InvalidDataException("Contract tolerances missing.");
        if (values != new TraceTolerances(new(0.0005, 0.0001), new(0.0001, 0.0001), new(0.001, 0.0001), 0.001))
            throw new InvalidDataException("This diagnostic version requires the existing frozen numerical tolerances.");
        return values;
    }

    private static void ValidateReference(TraceReference reference, string contractSha)
    {
        var provenance = reference.Provenance;
        if (reference.SchemaVersion != "sezika.laya-oracle.v1" || reference.Cases.Count is < 1 or > 64 ||
            reference.Cases.Any(row => string.IsNullOrWhiteSpace(row.Id)) || reference.Cases.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != reference.Cases.Count ||
            provenance.GetProperty("model_id").GetString() != ModernBertModelLoader.PinnedModelId ||
            provenance.GetProperty("model_revision").GetString() != ModernBertModelLoader.PinnedRevision ||
            provenance.GetProperty("weights_sha256").GetString() != ModernBertModelLoader.PinnedWeightsSha256 ||
            provenance.GetProperty("tokenizer_sha256").GetString() != ModernBertModelLoader.PinnedTokenizerSha256 ||
            !string.Equals(provenance.GetProperty("contract_sha256").GetString(), contractSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Reference identity or bounded case inventory is invalid.");
    }

    private static void ValidateAnswered(TraceReferenceCase source)
    {
        if (source.Status != "answered" || source.TokenIds is not { Length: >= 2 and <= 1024 } ||
            source.MarkerPositions is not { Length: >= 2 and <= 32 } || source.CandidateLabels is null ||
            source.MarkerPositions.Length != source.CandidateLabels.Length || source.RawLogits is null || source.Probabilities is null)
            throw new InvalidDataException("Select only answered reference cases within the supported input contract.");
        if (source.MarkerPositions.Any(index => index < 0 || index >= source.TokenIds.Length) ||
            source.MarkerPositions.Distinct().Count() != source.MarkerPositions.Length)
            throw new InvalidDataException("Reference marker positions are invalid.");
    }

    private static byte[] ReadFrozen(string path, string expectedSha, int maximumBytes, CancellationToken token)
    {
        using var input = File.OpenRead(Path.GetFullPath(path));
        if (input.Length > maximumBytes) throw new InvalidDataException("Frozen input exceeds its byte limit.");
        var bytes = new byte[checked((int)input.Length)];
        input.ReadExactlyAsync(bytes, token).AsTask().GetAwaiter().GetResult();
        token.ThrowIfCancellationRequested();
        if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Frozen input SHA-256 mismatch.");
        return bytes;
    }

    private static Dictionary<string, string> BinaryHashes(CancellationToken token)
    {
        var paths = new[] { Environment.ProcessPath, Path.Combine(AppContext.BaseDirectory, "Sezika.NumericParity.dll"),
            Path.Combine(AppContext.BaseDirectory, "Sezika.dll"), Path.Combine(AppContext.BaseDirectory, "Sezika.Cuda.dll") };
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            if (path is null || !File.Exists(path)) continue;
            using var stream = File.OpenRead(path);
            if (stream.Length > 128L * 1024 * 1024) throw new InvalidDataException("Binary identity file exceeds 128 MiB.");
            result[Path.GetFileName(path)] = Convert.ToHexStringLower(SHA256.HashDataAsync(stream, token).GetAwaiter().GetResult());
        }
        return result;
    }

    private static bool IsSha(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}

internal sealed class TraceRecordingPipeline(IMarkerDecisionPipeline inner, PromptSequence expected) : IMarkerDecisionPipeline
{
    public int Calls { get; private set; }
    public float[]? Logits { get; private set; }
    public float[] Score(ReadOnlySpan<int> tokens, int typeId, ReadOnlySpan<int> markers, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++Calls != 1 || typeId != expected.TypeId || !tokens.SequenceEqual(expected.TokenIds) || !markers.SequenceEqual(expected.MarkerPositions))
            throw new InvalidDataException("Production backend input differs from the generated sequence.");
        var logits = inner.Score(tokens, typeId, markers, cancellationToken);
        Logits = (float[])logits.Clone();
        return logits;
    }
    public void Dispose() => inner.Dispose();
}
