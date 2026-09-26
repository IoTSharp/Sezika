using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

return Evaluation.Run(args);

internal static class Evaluation
{
    private const string NimbleSha256 = "8e9e48b8de5206593912ae01ddc95bd77e40ad2ecf4c9292c1711290eca0d896";

    public static int Run(string[] args)
    {
        if (args is ["--self-test"]) return EvaluationChecks.Run();
        if (args.Length > 0 && args[0] is "--prepare-oracle" or "--prepare-oracle-batches") return EvaluationOracle.Prepare(args);
        if (args.Length > 0 && args[0] is "--score-capture" or "--score-captures") return EvaluationOracle.Score(args);
        var namedDataset = args.Length is 9 or 10;
        var datasetName = namedDataset ? args[6] : "nimble-holdout";
        var validDatasetTotal = !namedDataset || int.TryParse(args[7], out _);
        var datasetRecords = namedDataset && validDatasetTotal ? int.Parse(args[7], CultureInfo.InvariantCulture) : 324;
        var expectedSha256 = namedDataset ? args[8] : NimbleSha256;
        var policyName = args.Length == 10 ? args[9] : "strict";
        if (args.Length is not (6 or 9 or 10) || args[3] is not ("cuda" or "simd" or "scalar") ||
            !int.TryParse(args[4], out var maxRecords) || maxRecords < 1 || maxRecords > datasetRecords ||
            !int.TryParse(args[5], out var timeoutSeconds) || timeoutSeconds is < 1 or > 1800 ||
            !validDatasetTotal || datasetRecords is < 1 or > 10000 ||
            string.IsNullOrWhiteSpace(datasetName) || datasetName.Length > 80 ||
            expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit) ||
            policyName is not ("strict" or "laya_compatible"))
        {
            Console.Error.WriteLine("Usage: Sezika.Evaluation <model-dir> <eval.jsonl> <new-output.json> <cuda|simd|scalar> <records> <1..1800 timeout-seconds> [dataset-name dataset-total dataset-sha256 [strict|laya_compatible]]; --self-test");
            return 2;
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; deadline.Cancel(); };
        Console.CancelKeyPress += cancel;
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        try
        {
            var datasetPath = Path.GetFullPath(args[1]);
            var datasetSha256 = EvaluationInputs.FileHash(datasetPath, deadline.Token);
            if (!datasetSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The frozen evaluation dataset SHA-256 does not match.");
            var outputPath = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var policy = policyName == "strict" ? PromptLengthPolicy.Strict : PromptLengthPolicy.LayaCompatible;

            var mode = args[3] == "simd" ? EncoderKernelMode.Simd : EncoderKernelMode.Scalar;
            using var model = ModernBertModelLoader.Load(args[0], new EncoderExecutionOptions { Kernel = mode }, deadline.Token);
            using CudaDevice? device = args[3] == "cuda" ? CudaDevice.Open() : null;
            using CudaModernBertEncoder? gpuEncoder = device is null ? null :
                new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, deadline.Token);
            IMarkerDecisionPipeline innerPipeline = device is null ?
                new ModernBertDecisionPipeline(model.Encoder, model.Head, deadline.Token) :
                new CudaDecisionPipeline(device, gpuEncoder!, model.Head, deadline.Token);
            using var pipeline = new EvaluationPipeline(innerPipeline);
            using var engine = new ModernBertDecisionEngine(model, pipeline, args[3] + "-modernbert-marker-head",
                budget: new DecisionResourceBudget
                {
                    MaxTokens = 32768,
                    Deadline = TimeSpan.FromSeconds(30),
                    MaxResidentBytes = 4L * 1024 * 1024 * 1024,
                });

            var rows = new List<EvaluationRow>(maxRecords);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            using var lines = File.ReadLines(datasetPath).GetEnumerator();
            for (var index = 0; index < maxRecords; index++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (!lines.MoveNext()) throw new InvalidDataException("The frozen dataset has fewer rows than requested.");
                using var document = EvaluationInputs.ParseRow(lines.Current, deadline.Token);
                var root = document.RootElement;
                var id = root.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing record ID.");
                if (id.Length is < 1 or > 256 || !identifiers.Add(id)) throw new InvalidDataException("Invalid or duplicate evaluation ID.");
                var family = root.GetProperty("family").GetString() ?? throw new InvalidDataException("Missing family.");
                var sourceFamily = root.GetProperty("source_family").GetString() ?? throw new InvalidDataException("Missing source family.");
                var domain = root.GetProperty("domain").GetString() ?? throw new InvalidDataException("Missing domain.");
                var input = root.GetProperty("input");
                var question = input.GetProperty("questions").GetProperty("decision");
                var kind = question.GetProperty("type").GetString() ?? throw new InvalidDataException("Missing question type.");
                if (kind == "boolean") kind = "noul";
                var target = ReferenceLabel(root.GetProperty("reference").GetProperty("target"), kind);
                var language = EvaluationInputs.Language(root);
                var inputHash = EvaluationInputs.TextHash(input.GetRawText());
                var questionWatch = Stopwatch.StartNew();
                pipeline.Begin();
                EvaluationRow row;
                try
                {
                    var request = EvaluationInputs.Request(input, model.ModelId, policy);
                    var response = engine.Evaluate(request, deadline.Token);
                    var answer = response.Answers["decision"];
                    if (pipeline.Calls != 1 || pipeline.RawLogits is null || answer.InputDiagnostics is null)
                        throw new InvalidDataException("Answered row is missing actual forward evidence.");
                    row = ScoreAnswer(id, family, sourceFamily, domain, kind, target, answer,
                        response.Usage?.TokenCount ?? 0, questionWatch.Elapsed.TotalMilliseconds) with
                    {
                        InputDiagnostics = answer.InputDiagnostics,
                        CandidateLabels = request.Questions["decision"] switch
                        {
                            ChoiceQuestion choice => choice.Criteria.Keys.ToArray(),
                            ScoreQuestion score => Enumerable.Range(0, score.Criteria.Length).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray(),
                            BooleanQuestion => ["false", "true"],
                            _ => throw new InvalidDataException("Unknown primitive."),
                        },
                        Logits = pipeline.RawLogits,
                    };
                }
                catch (DecisionException exception) when (exception.Code is not ("decision_cancelled" or "decision_deadline_exceeded"))
                {
                    row = new EvaluationRow(id, family, sourceFamily, domain, kind, target, null, null,
                        exception.Code, null, 0, questionWatch.Elapsed.TotalMilliseconds, null, null, null, null, null)
                    {
                        RejectedInput = exception is PromptTruncationException truncation
                            ? new RejectedInput(truncation.Diagnostics.OriginalTotalTokens,
                                truncation.Diagnostics.InstructionTruncated, truncation.Diagnostics.OptionsTruncated,
                                truncation.Diagnostics.StateTruncated, truncation.Diagnostics.DroppedStateTokens,
                                truncation.Diagnostics.StateTruncationDirection) : null,
                    };
                }
                rows.Add(row with { Language = language, InputSha256 = inputHash,
                    TokenIdsSha256 = pipeline.TokenIdsSha256, MarkerPositions = pipeline.MarkerPositions,
                    ForwardCalls = pipeline.Calls });
                if (rows.Count % 10 == 0 || rows.Count == maxRecords)
                    Console.WriteLine($"Evaluated {rows.Count}/{maxRecords} rows; correct {rows.Count(row => row.Correct == true)}, failed {rows.Count(row => row.ErrorCode is not null)}; elapsed {watch.Elapsed.TotalSeconds:F1}s");
            }
            if (rows.Count != maxRecords) throw new InvalidDataException("The frozen holdout has fewer rows than requested.");
            if (maxRecords == datasetRecords && lines.MoveNext()) throw new InvalidDataException("Dataset has more rows than its declared total.");
            if (EvaluationInputs.FileHash(datasetPath, deadline.Token) != datasetSha256)
                throw new InvalidDataException("Dataset changed during evaluation.");

            var report = Summarize(rows, args[3], datasetName, datasetRecords, datasetSha256, started,
                watch.Elapsed.TotalMilliseconds, policyName, deadline.Token);
            JsonSerializer.Serialize(output, report, EvaluationJsonContext.Default.EvaluationReport);
            Console.WriteLine($"Wrote {outputPath}: {report.Correct}/{report.Answered} answered correctly; coverage {report.Answered}/{report.Processed}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"evaluation: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static string ReferenceLabel(JsonElement value, string kind) => kind switch
    {
        "choice" => value.GetString() ?? throw new InvalidDataException("Missing choice target."),
        "score" => value.GetInt32().ToString(CultureInfo.InvariantCulture),
        "noul" => value.GetBoolean() ? "true" : "false",
        _ => throw new InvalidDataException($"Unsupported holdout question type: {kind}"),
    };

    internal static EvaluationRow ScoreAnswer(string id, string family, string sourceFamily, string domain, string kind,
        string target, Answer answer, int tokens, double milliseconds)
    {
        var accepted = answer.Status switch
        {
            "answered" when answer.AbstentionReason is null => true,
            "abstained" when !string.IsNullOrWhiteSpace(answer.AbstentionReason) => false,
            _ => throw new InvalidDataException($"Invalid answer status or abstention reason for {id}."),
        };
        string selected;
        double targetProbability;
        double topProbability;
        double brier;
        double? expectedScore = null;
        double? probabilityTrue = null;
        switch (answer)
        {
            case ChoiceAnswer choice:
                selected = choice.Choice;
                targetProbability = choice.Probabilities[target];
                topProbability = choice.Probabilities[selected];
                brier = choice.Probabilities.Sum(item => Math.Pow(item.Value - (item.Key == target ? 1d : 0d), 2));
                break;
            case ScoreAnswer score:
                selected = score.Probabilities.OrderByDescending(item => item.Value)
                    .ThenBy(item => int.Parse(item.Key, CultureInfo.InvariantCulture)).First().Key;
                targetProbability = score.Probabilities[target];
                topProbability = score.Probabilities[selected];
                brier = score.Probabilities.Sum(item => Math.Pow(item.Value - (item.Key == target ? 1d : 0d), 2));
                expectedScore = score.Score;
                break;
            case BooleanAnswer boolean:
                probabilityTrue = boolean.ProbabilityTrue;
                selected = boolean.ProbabilityTrue >= 0.5 ? "true" : "false";
                targetProbability = target == "true" ? boolean.ProbabilityTrue : 1d - boolean.ProbabilityTrue;
                topProbability = Math.Max(boolean.ProbabilityTrue, 1d - boolean.ProbabilityTrue);
                brier = 2d * Math.Pow(1d - targetProbability, 2);
                break;
            default:
                throw new InvalidDataException($"Unexpected answer type for {kind}.");
        }
        return new EvaluationRow(id, family, sourceFamily, domain, kind, target, selected, answer.Status,
            null, accepted ? selected == target : null, tokens, milliseconds, targetProbability, topProbability, brier,
            -Math.Log(Math.Max(targetProbability, 1e-15)), expectedScore, probabilityTrue, answer.AbstentionReason);
    }

    internal static EvaluationReport Summarize(List<EvaluationRow> rows, string backend,
        string datasetName, int datasetRecords, string datasetSha256, DateTimeOffset started, double elapsedMilliseconds,
        string lengthPolicy, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var answered = rows.Where(row => row.Correct is not null).ToArray();
        var correct = answered.Count(row => row.Correct == true);
        var pairs = rows.GroupBy(row => row.Family).Where(group => group.Count() == 2 && group.Select(row => row.Target).Distinct().Count() == 2).ToArray();
        var pairedAnswered = pairs.Where(group => group.All(row => row.Correct is not null)).ToArray();
        return new EvaluationReport
        {
            StartedUtc = started,
            ElapsedMilliseconds = elapsedMilliseconds,
            Model = ModernBertModelLoader.PinnedModelId,
            ModelRevision = ModernBertModelLoader.PinnedRevision,
            TokenizerRevision = ModernBertModelLoader.PinnedRevision,
            Backend = backend,
            LengthPolicy = lengthPolicy,
            WeightsSha256 = ModernBertModelLoader.PinnedWeightsSha256,
            TokenizerSha256 = ModernBertModelLoader.PinnedTokenizerSha256,
            CodeArtifacts = EvaluationInputs.CodeHashes(cancellationToken),
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            DatasetName = datasetName,
            DatasetSha256 = datasetSha256,
            DatasetTotal = datasetRecords,
            Processed = rows.Count,
            Answered = answered.Length,
            Abstained = rows.Count(row => row.Status == "abstained"),
            Correct = correct,
            Coverage = Ratio(answered.Length, rows.Count),
            AccuracyOnAnswered = Ratio(correct, answered.Length),
            AccuracyOverProcessed = Ratio(correct, rows.Count),
            Boolean = BooleanMetrics.Compute(rows, cancellationToken),
            MeanBrier = answered.Length == 0 ? null : answered.Average(row => row.Brier!.Value),
            MeanNll = answered.Length == 0 ? null : answered.Average(row => row.Nll!.Value),
            Ece10 = Ece(answered),
            ScoreMae = answered.Any(row => row.ExpectedScore is not null) ? answered.Where(row => row.ExpectedScore is not null)
                .Average(row => Math.Abs(row.ExpectedScore!.Value - int.Parse(row.Target, CultureInfo.InvariantCulture))) : null,
            EligibleCounterfactualPairs = pairs.Length,
            AnsweredCounterfactualPairs = pairedAnswered.Length,
            PredictionFlips = pairedAnswered.Count(group => group.Select(row => row.Selected).Distinct().Count() == 2),
            BothCorrectPairs = pairedAnswered.Count(group => group.All(row => row.Correct == true)),
            ByType = Groups(rows, row => row.Type, cancellationToken),
            ByTarget = Groups(rows, row => row.Target, cancellationToken),
            ByDomain = Groups(rows, row => row.Domain, cancellationToken),
            BySourceFamily = Groups(rows, row => row.SourceFamily, cancellationToken),
            ByLanguage = Groups(rows, row => row.Language, cancellationToken),
            ByLanguageAndType = rows.GroupBy(row => (row.Language, row.Type)).OrderBy(group => group.Key.Language, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Type, StringComparer.Ordinal).Select(group => new EvaluationSlice(group.Key.Language,
                    group.Key.Type, Group(group.Key.Type, group.ToArray(), cancellationToken))).ToList(),
            ByFailure = rows.GroupBy(row => row.ErrorCode ?? row.Status ?? "unknown").OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            Rows = rows,
        };
    }

    private static List<EvaluationGroup> Groups(List<EvaluationRow> rows, Func<EvaluationRow, string> key, CancellationToken token) =>
        rows.GroupBy(key).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Group(group.Key, group.ToArray(), token)).ToList();

    internal static EvaluationGroup Group(string name, EvaluationRow[] rows, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var answered = rows.Where(row => row.Correct is not null).ToArray();
        var correct = answered.Count(row => row.Correct == true);
        return new(name, rows.Length, answered.Length, correct, Ratio(correct, answered.Length))
        {
            Coverage = Ratio(answered.Length, rows.Length), AccuracyOverProcessed = Ratio(correct, rows.Length),
            Boolean = BooleanMetrics.Compute(rows, token),
            MeanBrier = answered.Length == 0 ? null : answered.Average(row => row.Brier!.Value),
            MeanNll = answered.Length == 0 ? null : answered.Average(row => row.Nll!.Value), Ece10 = Ece(answered),
            ScoreMae = answered.Any(row => row.ExpectedScore is not null) ? answered.Where(row => row.ExpectedScore is not null)
                .Average(row => Math.Abs(row.ExpectedScore!.Value - int.Parse(row.Target, CultureInfo.InvariantCulture))) : null,
        };
    }

    private static double? Ratio(int numerator, int denominator) => denominator == 0 ? null : (double)numerator / denominator;

    private static double? Ece(EvaluationRow[] rows)
    {
        if (rows.Length == 0) return null;
        var sum = 0d;
        for (var bin = 0; bin < 10; bin++)
        {
            var members = rows.Where(row => Math.Min(9, (int)(row.TopProbability!.Value * 10)) == bin).ToArray();
            if (members.Length == 0) continue;
            sum += (double)members.Length / rows.Length * Math.Abs(members.Average(row => row.TopProbability!.Value) -
                members.Count(row => row.Correct == true) / (double)members.Length);
        }
        return sum;
    }
}

internal sealed record EvaluationRow(string Id, string Family, string SourceFamily, string Domain, string Type,
    string Target, string? Selected, string? Status, string? ErrorCode, bool? Correct, int Tokens,
    double Milliseconds, double? ProbabilityOfReference, double? TopProbability, double? Brier, double? Nll,
    double? ExpectedScore, double? ProbabilityTrue = null, string? AbstentionReason = null)
{
    public string Language { get; init; } = "unspecified";
    public string? InputSha256 { get; init; }
    public string? TokenIdsSha256 { get; init; }
    public int[]? MarkerPositions { get; init; }
    public string[]? CandidateLabels { get; init; }
    public float[]? Logits { get; init; }
    public int? ForwardCalls { get; init; }
    public PromptInputDiagnostics? InputDiagnostics { get; init; }
    public RejectedInput? RejectedInput { get; init; }
}

internal sealed record RejectedInput(int OriginalTotalTokens, bool InstructionTruncated, bool OptionsTruncated,
    bool StateTruncated, int DroppedStateTokens, string StateTruncationDirection);

internal sealed record EvaluationGroup(string Name, int Total, int Answered, int Correct, double? AccuracyOnAnswered)
{
    public double? Coverage { get; init; }
    public double? AccuracyOverProcessed { get; init; }
    public BooleanMetrics? Boolean { get; init; }
    public double? MeanBrier { get; init; }
    public double? MeanNll { get; init; }
    public double? Ece10 { get; init; }
    public double? ScoreMae { get; init; }
}
internal sealed record EvaluationSlice(string Language, string Type, EvaluationGroup Metrics);

internal sealed class BooleanMetrics
{
    public required int Processed { get; init; }
    public required int Answered { get; init; }
    public required int Unanswered { get; init; }
    public required int Abstained { get; init; }
    public required int ReferencePositive { get; init; }
    public required int ReferenceNegative { get; init; }
    public required int ReferencePositiveAnswered { get; init; }
    public required int ReferenceNegativeAnswered { get; init; }
    public required int TruePositive { get; init; }
    public required int FalsePositive { get; init; }
    public required int TrueNegative { get; init; }
    public required int FalseNegative { get; init; }
    public required double? Coverage { get; init; }
    public required double? AccuracyOnAnswered { get; init; }
    public required double? AccuracyOverProcessed { get; init; }
    public required double? PositiveRecallOnAnswered { get; init; }
    public required double? NegativeRecallOnAnswered { get; init; }
    public required double? BalancedAccuracyOnAnswered { get; init; }
    public required double? AurocOnAnswered { get; init; }
    public required double? PredictedPositiveRateOnAnswered { get; init; }

    public static BooleanMetrics Compute(IReadOnlyList<EvaluationRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count > 10000) throw new ArgumentOutOfRangeException(nameof(rows));
        var processed = 0;
        var unanswered = 0;
        var abstained = 0;
        var referencePositive = 0;
        var referenceNegative = 0;
        var truePositive = 0;
        var falsePositive = 0;
        var trueNegative = 0;
        var falseNegative = 0;
        var scores = new List<(double ProbabilityTrue, bool Positive)>(rows.Count);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Type != "noul") continue;
            processed++;
            var positive = row.Target switch
            {
                "true" => true,
                "false" => false,
                _ => throw new InvalidDataException($"Invalid Boolean reference for {row.Id}."),
            };
            if (positive) referencePositive++;
            else referenceNegative++;
            if (row.Correct is null)
            {
                unanswered++;
                if (row.Status == "abstained") abstained++;
                continue;
            }
            if (row.ProbabilityTrue is not double probabilityTrue || !double.IsFinite(probabilityTrue) ||
                probabilityTrue is < 0d or > 1d)
                throw new InvalidDataException($"Invalid Boolean probability for {row.Id}.");
            scores.Add((probabilityTrue, positive));
            switch (positive, row.Selected)
            {
                case (true, "true"): truePositive++; break;
                case (false, "true"): falsePositive++; break;
                case (false, "false"): trueNegative++; break;
                case (true, "false"): falseNegative++; break;
                default: throw new InvalidDataException($"Invalid Boolean prediction for {row.Id}.");
            }
        }

        var positiveAnswered = truePositive + falseNegative;
        var negativeAnswered = trueNegative + falsePositive;
        double? positiveRecall = positiveAnswered == 0 ? null : (double)truePositive / positiveAnswered;
        double? negativeRecall = negativeAnswered == 0 ? null : (double)trueNegative / negativeAnswered;
        double? auroc = null;
        if (positiveAnswered > 0 && negativeAnswered > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scores.Sort((left, right) => left.ProbabilityTrue.CompareTo(right.ProbabilityTrue));
            var positiveRankSum = 0d;
            var index = 0;
            while (index < scores.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var end = index + 1;
                var positivesAtScore = scores[index].Positive ? 1 : 0;
                while (end < scores.Count && scores[end].ProbabilityTrue == scores[index].ProbabilityTrue)
                {
                    if (scores[end].Positive) positivesAtScore++;
                    end++;
                }
                positiveRankSum += positivesAtScore * (index + 1d + end) / 2d;
                index = end;
            }
            auroc = (positiveRankSum - positiveAnswered * (positiveAnswered + 1d) / 2d) /
                (positiveAnswered * (double)negativeAnswered);
        }

        return new BooleanMetrics
        {
            Processed = processed,
            Answered = scores.Count,
            Unanswered = unanswered,
            Abstained = abstained,
            ReferencePositive = referencePositive,
            ReferenceNegative = referenceNegative,
            ReferencePositiveAnswered = positiveAnswered,
            ReferenceNegativeAnswered = negativeAnswered,
            TruePositive = truePositive,
            FalsePositive = falsePositive,
            TrueNegative = trueNegative,
            FalseNegative = falseNegative,
            Coverage = processed == 0 ? null : (double)scores.Count / processed,
            AccuracyOnAnswered = scores.Count == 0 ? null :
                (double)(truePositive + trueNegative) / scores.Count,
            AccuracyOverProcessed = processed == 0 ? null :
                (double)(truePositive + trueNegative) / processed,
            PositiveRecallOnAnswered = positiveRecall,
            NegativeRecallOnAnswered = negativeRecall,
            BalancedAccuracyOnAnswered = positiveRecall is double positiveRate && negativeRecall is double negativeRate ?
                (positiveRate + negativeRate) / 2d : null,
            AurocOnAnswered = auroc,
            PredictedPositiveRateOnAnswered = scores.Count == 0 ? null :
                (double)(truePositive + falsePositive) / scores.Count,
        };
    }
}

internal sealed class EvaluationReport
{
    public int SchemaVersion { get; init; } = 2;
    public string RenderingVersion { get; init; } = EvaluationInputs.RenderingVersion;
    public required string LengthPolicy { get; init; }
    public required string WeightsSha256 { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required Dictionary<string, string> CodeArtifacts { get; init; }
    public required string Runtime { get; init; }
    public required string OperatingSystem { get; init; }
    public string? CaptureSha256 { get; set; }
    public string? CaptureStatus { get; set; }
    public int? CaptureSelectedCases { get; set; }
    public int? CaptureManifestCases { get; set; }
    public JsonElement? CaptureProvenance { get; set; }
    public JsonElement? CaptureImplementation { get; set; }
    public string? CaptureCasesSha256 { get; set; }
    public string? CaptureContractSha256 { get; set; }
    public List<CaptureBatchEvidence> CaptureBatches { get; set; } = [];
    public int Unprocessed => DatasetTotal - Processed;
    public double DatasetAnswerCoverage => (double)Answered / DatasetTotal;
    public string MeasurementOrigin { get; set; } = "csharp_runtime";
    public bool FullDatasetProcessed => Processed == DatasetTotal;
    public string EvaluationUse { get; init; } = "audit_only_not_training_or_calibration";
    public required DateTimeOffset StartedUtc { get; init; }
    public required double ElapsedMilliseconds { get; init; }
    public required string Model { get; init; }
    public required string ModelRevision { get; init; }
    public required string TokenizerRevision { get; init; }
    public required string Backend { get; init; }
    public required string DatasetName { get; init; }
    public required string DatasetSha256 { get; init; }
    public required int DatasetTotal { get; init; }
    public required int Processed { get; init; }
    public required int Answered { get; init; }
    public required int Abstained { get; init; }
    public required int Correct { get; init; }
    public required double? Coverage { get; init; }
    public required double? AccuracyOnAnswered { get; init; }
    public required double? AccuracyOverProcessed { get; init; }
    public required BooleanMetrics Boolean { get; init; }
    public required double? MeanBrier { get; init; }
    public required double? MeanNll { get; init; }
    public required double? Ece10 { get; init; }
    public required double? ScoreMae { get; init; }
    public required int EligibleCounterfactualPairs { get; init; }
    public required int AnsweredCounterfactualPairs { get; init; }
    public required int PredictionFlips { get; init; }
    public required int BothCorrectPairs { get; init; }
    public required List<EvaluationGroup> ByType { get; init; }
    public required List<EvaluationGroup> ByTarget { get; init; }
    public required List<EvaluationGroup> ByDomain { get; init; }
    public required List<EvaluationGroup> BySourceFamily { get; init; }
    public required List<EvaluationGroup> ByLanguage { get; init; }
    public List<EvaluationSlice> ByLanguageAndType { get; init; } = [];
    public required Dictionary<string, int> ByFailure { get; init; }
    public required List<EvaluationRow> Rows { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EvaluationReport))]
internal partial class EvaluationJsonContext : JsonSerializerContext { }
