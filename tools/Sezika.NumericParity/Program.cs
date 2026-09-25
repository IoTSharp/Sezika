using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

return NumericParity.Run(args);

internal static class NumericParity
{
    public static int Run(string[] args)
    {
        var validSkip = args.Length != 8 || int.TryParse(args[7], CultureInfo.InvariantCulture, out _);
        var skipRecords = args.Length == 8 && validSkip ? int.Parse(args[7], CultureInfo.InvariantCulture) : 0;
        if (args.Length is not (7 or 8) || !validSkip || !int.TryParse(args[3], CultureInfo.InvariantCulture, out var maxRecords) ||
            maxRecords is < 1 or > 64 || !int.TryParse(args[4], CultureInfo.InvariantCulture, out var timeoutSeconds) ||
            timeoutSeconds is < 1 or > 1800 || !int.TryParse(args[5], CultureInfo.InvariantCulture, out var longInputTokens) ||
            longInputTokens is < 1 or > 256 || skipRecords is < 0 or > 9999 ||
            skipRecords + maxRecords > 10000 || args[6].Length != 64 || !args[6].All(Uri.IsHexDigit))
        {
            Console.Error.WriteLine("Usage: Sezika.NumericParity <model-dir> <eval.jsonl> <output.json> <1..64 records> <1..1800 timeout-seconds> <1..256 long-input-tokens> <dataset-sha256> [0..9999 skip-records]");
            return 2;
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        try
        {
            var datasetPath = Path.GetFullPath(args[1]);
            if (new FileInfo(datasetPath).Length > 128L * 1024 * 1024)
                throw new InvalidDataException("Dataset exceeds the 128 MiB diagnostic limit.");
            using var stream = File.OpenRead(datasetPath);
            var datasetSha256 = Convert.ToHexString(SHA256.HashDataAsync(stream, deadline.Token).GetAwaiter().GetResult()).ToLowerInvariant();
            if (!datasetSha256.Equals(args[6], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Dataset SHA-256 does not match the required frozen input.");

            var samples = ReadSamples(datasetPath, skipRecords, maxRecords, deadline.Token);
            RunBackend(samples, args[0], "scalar", deadline.Token);
            RunBackend(samples, args[0], "simd", deadline.Token);
            try { RunBackend(samples, args[0], "cuda", deadline.Token); }
            catch (CudaException exception) { MarkBackendFailure(samples, "cuda", exception.Code); }
            catch (DllNotFoundException) { MarkBackendFailure(samples, "cuda", "cuda_driver_unavailable"); }
            catch (BadImageFormatException) { MarkBackendFailure(samples, "cuda", "cuda_driver_incompatible"); }

            var rows = samples.Select(sample => Compare(sample)).ToList();
            var longRows = rows.Where(row => row.TokenCount >= longInputTokens &&
                row.Simd.Status == "compared" && row.Cuda.Status == "compared").ToArray();
            var report = new ParityReport(
                started, watch.Elapsed.TotalMilliseconds, ModernBertModelLoader.PinnedModelId,
                ModernBertModelLoader.PinnedRevision, ModernBertModelLoader.PinnedWeightsSha256,
                ModernBertModelLoader.PinnedTokenizerSha256, datasetSha256, samples.Count,
                longInputTokens, longRows.Length,
                rows.Count(row => row.Simd is { Status: "compared" }),
                rows.Count(row => row.Cuda is { Status: "compared" }),
                MaxError(rows.Select(row => row.Simd)), MaxError(rows.Select(row => row.Cuda)), rows);
            var outputPath = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(report, ParityJsonContext.Default.ParityReport));
            Console.WriteLine($"Wrote {outputPath}; scalar/SIMD compared {report.SimdCompared}/{report.SampleCount}, scalar/CUDA compared {report.CudaCompared}/{report.SampleCount}; long inputs {report.LongInputCount}.");
            return report.SimdCompared > 0 && report.CudaCompared > 0 && report.LongInputCount > 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or JsonException or DecisionException or CudaException or OperationCanceledException or ArgumentException)
        {
            Console.Error.WriteLine($"numeric-parity: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static List<Sample> ReadSamples(string path, int skipRecords, int maxRecords, CancellationToken cancellationToken)
    {
        var samples = new List<Sample>(maxRecords);
        var rowNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (samples.Count >= maxRecords) break;
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            if (line.Length > 1_048_576) throw new InvalidDataException("A dataset line exceeds 1 MiB.");
            if (rowNumber <= skipRecords)
            {
                if (rowNumber % 1000 == 0) Console.WriteLine($"Skipped {rowNumber}/{skipRecords} frozen rows.");
                continue;
            }
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var input = root.GetProperty("input");
            var question = input.GetProperty("questions").GetProperty("decision");
            var kind = question.GetProperty("type").GetString();
            var criteria = question.GetProperty("criteria");
            Question mapped = kind switch
            {
                "choice" => new ChoiceQuestion
                {
                    Instructions = question.GetProperty("instructions").Clone(),
                    Criteria = criteria.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal),
                },
                "score" => new ScoreQuestion
                {
                    Instructions = question.GetProperty("instructions").Clone(),
                    Criteria = criteria.EnumerateArray().Select(item => item.Clone()).ToArray(),
                },
                "noul" => new BooleanQuestion
                {
                    Instructions = question.GetProperty("instructions").Clone(),
                    Criteria = new BooleanCriteria
                    {
                        WhenTrue = criteria.GetProperty("true").Clone(),
                        WhenFalse = criteria.GetProperty("false").Clone(),
                    },
                },
                _ => throw new InvalidDataException($"Unsupported question type: {kind}"),
            };
            var id = root.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing sample ID.");
            if (samples.Any(sample => sample.Id == id)) throw new InvalidDataException($"Duplicate sample ID: {id}");
            samples.Add(new Sample(id, rowNumber, new DecisionRequest
            {
                Model = ModernBertModelLoader.PinnedModelId,
                State = input.GetProperty("state").Clone(),
                Questions = new Dictionary<string, Question>(StringComparer.Ordinal) { ["decision"] = mapped },
            }));
            Console.WriteLine($"Selected {samples.Count}/{maxRecords}: row {rowNumber}, id {id}.");
        }
        if (samples.Count != maxRecords) throw new InvalidDataException("Dataset has fewer rows than requested.");
        return samples;
    }

    private static void RunBackend(List<Sample> samples, string modelDirectory, string backend, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = backend == "simd" ? EncoderKernelMode.Simd : EncoderKernelMode.Scalar;
        using var model = ModernBertModelLoader.Load(modelDirectory, new EncoderExecutionOptions { Kernel = mode }, cancellationToken);
        using CudaDevice? device = backend == "cuda" ? CudaDevice.Open() : null;
        using CudaModernBertEncoder? gpuEncoder = device is null ? null :
            new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, cancellationToken);
        using IMarkerDecisionPipeline pipeline = device is null ?
            new ModernBertDecisionPipeline(model.Encoder, model.Head, cancellationToken) :
            new CudaDecisionPipeline(device, gpuEncoder!, model.Head, cancellationToken);
        using var engine = new ModernBertDecisionEngine(model, pipeline, backend + "-modernbert-marker-head",
            budget: new DecisionResourceBudget
            {
                MaxTokens = 32768,
                Deadline = TimeSpan.FromSeconds(30),
                MaxResidentBytes = 4L * 1024 * 1024 * 1024,
            });
        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BackendResult result;
            try
            {
                var response = engine.Evaluate(sample.Request, cancellationToken);
                var answer = response.Answers["decision"];
                var logits = answer switch
                {
                    ChoiceAnswer choice => choice.Logits,
                    ScoreAnswer score => score.Logits,
                    BooleanAnswer boolean => boolean.Logits,
                    _ => null,
                };
                result = logits is null ? new BackendResult("failed", "raw_logits_missing", null, null) :
                    new BackendResult("answered", null, response.Usage?.TokenCount,
                        logits.OrderBy(item => item.Key, StringComparer.Ordinal).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
            }
            catch (DecisionException exception) { result = new BackendResult("failed", exception.Code, null, null); }
            catch (CudaException exception) { result = new BackendResult("failed", exception.Code, null, null); }
            sample.Results.Add(backend, result);
            Console.WriteLine($"{backend}: {sample.Results.Count}/{3} backend(s), sample {samples.IndexOf(sample) + 1}/{samples.Count}, {result.Status}{(result.ErrorCode is null ? string.Empty : ": " + result.ErrorCode)}");
        }
    }

    private static void MarkBackendFailure(List<Sample> samples, string backend, string code)
    {
        foreach (var sample in samples)
            sample.Results.TryAdd(backend, new BackendResult("failed", code, null, null));
        Console.WriteLine($"{backend}: unavailable ({code}); no logits compared.");
    }

    private static ParityRow Compare(Sample sample)
    {
        var scalar = sample.Results["scalar"];
        var simd = sample.Results["simd"];
        var cuda = sample.Results["cuda"];
        return new ParityRow(sample.Id, sample.SourceRowNumber, scalar.TokenCount ?? simd.TokenCount ?? cuda.TokenCount,
            scalar, simd, cuda, ComparePair(scalar, simd), ComparePair(scalar, cuda));
    }

    private static PairResult ComparePair(BackendResult reference, BackendResult actual)
    {
        if (reference.Logits is null || actual.Logits is null)
            return new PairResult("unavailable", reference.ErrorCode ?? actual.ErrorCode ?? "raw_logits_missing", null, null, null);
        if (reference.TokenCount != actual.TokenCount)
            return new PairResult("invalid", "token_count_mismatch", null, null, null);
        if (!reference.Logits.Keys.Order(StringComparer.Ordinal).SequenceEqual(actual.Logits.Keys.Order(StringComparer.Ordinal)))
            return new PairResult("invalid", "candidate_keys_mismatch", null, null, null);
        var errors = reference.Logits.Keys.Order(StringComparer.Ordinal).Select(key =>
        {
            var expected = reference.Logits[key];
            var observed = actual.Logits[key];
            var absolute = Math.Abs(expected - observed);
            return new CandidateError(key, expected, observed, absolute,
                absolute / Math.Max(1e-12, Math.Abs(expected)));
        }).ToList();
        if (errors.Any(error => !double.IsFinite(error.AbsoluteError) || !double.IsFinite(error.RelativeError)))
            return new PairResult("invalid", "non_finite_logit", null, null, null);
        return new PairResult("compared", null, errors.Max(error => error.AbsoluteError),
            errors.Max(error => error.RelativeError), errors);
    }

    private static double? MaxError(IEnumerable<PairResult> pairs)
    {
        var values = pairs.Where(pair => pair.Status == "compared").Select(pair => pair.MaxAbsoluteError!.Value).ToArray();
        return values.Length == 0 ? null : values.Max();
    }
}

internal sealed class Sample(string id, int sourceRowNumber, DecisionRequest request)
{
    public string Id { get; } = id;
    public int SourceRowNumber { get; } = sourceRowNumber;
    public DecisionRequest Request { get; } = request;
    public Dictionary<string, BackendResult> Results { get; } = new(StringComparer.Ordinal);
}

internal sealed record BackendResult(string Status, string? ErrorCode, int? TokenCount, Dictionary<string, double>? Logits);
internal sealed record CandidateError(string Candidate, double ScalarLogit, double BackendLogit, double AbsoluteError, double RelativeError);
internal sealed record PairResult(string Status, string? ErrorCode, double? MaxAbsoluteError, double? MaxRelativeError, List<CandidateError>? Candidates);
internal sealed record ParityRow(string Id, int SourceRowNumber, int? TokenCount, BackendResult ScalarResult, BackendResult SimdResult,
    BackendResult CudaResult, PairResult Simd, PairResult Cuda);
internal sealed record ParityReport(DateTimeOffset StartedUtc, double ElapsedMilliseconds, string ModelId,
    string ModelRevision, string WeightsSha256, string TokenizerSha256, string DatasetSha256, int SampleCount,
    int LongInputThresholdTokens, int LongInputCount, int SimdCompared, int CudaCompared,
    double? MaxSimdAbsoluteError, double? MaxCudaAbsoluteError, List<ParityRow> Rows);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ParityReport))]
internal partial class ParityJsonContext : JsonSerializerContext
{
}
