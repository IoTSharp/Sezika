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
        var namedDataset = args.Length == 9;
        var datasetName = namedDataset ? args[6] : "nimble-holdout";
        var validDatasetTotal = !namedDataset || int.TryParse(args[7], out _);
        var datasetRecords = namedDataset && validDatasetTotal ? int.Parse(args[7], CultureInfo.InvariantCulture) : 324;
        var expectedSha256 = namedDataset ? args[8] : NimbleSha256;
        if (args.Length is not (6 or 9) || args[3] is not ("cuda" or "simd") ||
            !int.TryParse(args[4], out var maxRecords) || maxRecords < 1 || maxRecords > datasetRecords ||
            !int.TryParse(args[5], out var timeoutSeconds) || timeoutSeconds is < 1 or > 1800 ||
            !validDatasetTotal || datasetRecords is < 1 or > 10000 ||
            string.IsNullOrWhiteSpace(datasetName) || datasetName.Length > 80 ||
            expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
        {
            Console.Error.WriteLine("Usage: Sezika.Evaluation <model-dir> <eval.jsonl> <output.json> <cuda|simd> <records> <1..1800 timeout-seconds> [dataset-name dataset-total dataset-sha256]");
            return 2;
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        try
        {
            var datasetPath = Path.GetFullPath(args[1]);
            using var datasetStream = File.OpenRead(datasetPath);
            var datasetSha256 = Convert.ToHexString(SHA256.HashData(datasetStream)).ToLowerInvariant();
            if (!datasetSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The frozen evaluation dataset SHA-256 does not match.");

            var mode = args[3] == "simd" ? EncoderKernelMode.Simd : EncoderKernelMode.Scalar;
            using var model = ModernBertModelLoader.Load(args[0], new EncoderExecutionOptions { Kernel = mode }, deadline.Token);
            using CudaDevice? device = args[3] == "cuda" ? CudaDevice.Open() : null;
            using CudaModernBertEncoder? gpuEncoder = device is null ? null :
                new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, deadline.Token);
            using IMarkerDecisionPipeline pipeline = device is null ?
                new ModernBertDecisionPipeline(model.Encoder, model.Head, deadline.Token) :
                new CudaDecisionPipeline(device, gpuEncoder!, model.Head, deadline.Token);
            using var engine = new ModernBertDecisionEngine(model, pipeline, args[3] + "-modernbert-marker-head",
                budget: new DecisionResourceBudget
                {
                    MaxTokens = 32768,
                    Deadline = TimeSpan.FromSeconds(30),
                    MaxResidentBytes = 4L * 1024 * 1024 * 1024,
                });

            var rows = new List<EvaluationRow>(maxRecords);
            foreach (var line in File.ReadLines(datasetPath))
            {
                if (rows.Count == maxRecords) break;
                deadline.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("Unexpected blank holdout row.");
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var id = root.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing record ID.");
                var family = root.GetProperty("family").GetString() ?? throw new InvalidDataException("Missing family.");
                var sourceFamily = root.GetProperty("source_family").GetString() ?? throw new InvalidDataException("Missing source family.");
                var domain = root.GetProperty("domain").GetString() ?? throw new InvalidDataException("Missing domain.");
                var question = root.GetProperty("input").GetProperty("questions").GetProperty("decision");
                var kind = question.GetProperty("type").GetString() ?? throw new InvalidDataException("Missing question type.");
                var target = ReferenceLabel(root.GetProperty("reference").GetProperty("target"), kind);
                var questionWatch = Stopwatch.StartNew();
                try
                {
                    var request = CreateRequest(root.GetProperty("input"), question, kind, model.ModelId);
                    var response = engine.Evaluate(request, deadline.Token);
                    var answer = response.Answers["decision"];
                    rows.Add(ScoreAnswer(id, family, sourceFamily, domain, kind, target, answer,
                        response.Usage?.TokenCount ?? 0, questionWatch.Elapsed.TotalMilliseconds));
                }
                catch (DecisionException exception)
                {
                    rows.Add(new EvaluationRow(id, family, sourceFamily, domain, kind, target, null, null,
                        exception.Code, null, 0, questionWatch.Elapsed.TotalMilliseconds, null, null, null, null, null));
                }
                if (rows.Count % 10 == 0 || rows.Count == maxRecords)
                    Console.WriteLine($"Evaluated {rows.Count}/{maxRecords} rows; correct {rows.Count(row => row.Correct == true)}, failed {rows.Count(row => row.ErrorCode is not null)}; elapsed {watch.Elapsed.TotalSeconds:F1}s");
            }
            if (rows.Count != maxRecords) throw new InvalidDataException("The frozen holdout has fewer rows than requested.");

            var report = Summarize(rows, model, args[3], datasetName, datasetRecords, datasetSha256, started,
                watch.Elapsed.TotalMilliseconds, deadline.Token);
            var outputPath = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(report, EvaluationJsonContext.Default.EvaluationReport));
            Console.WriteLine($"Wrote {outputPath}: {report.Correct}/{report.Answered} answered correctly; coverage {report.Answered}/{report.Processed}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"evaluation: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static DecisionRequest CreateRequest(JsonElement input, JsonElement question, string kind, string modelId)
    {
        var instructions = question.GetProperty("instructions").Clone();
        var criteria = question.GetProperty("criteria");
        Question mapped = kind switch
        {
            "choice" => new ChoiceQuestion
            {
                Instructions = instructions,
                Criteria = criteria.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal),
            },
            "score" => new ScoreQuestion
            {
                Instructions = instructions,
                Criteria = criteria.EnumerateArray().Select(item => item.Clone()).ToArray(),
            },
            "noul" => new BooleanQuestion
            {
                Instructions = instructions,
                Criteria = new BooleanCriteria
                {
                    WhenTrue = criteria.GetProperty("true").Clone(),
                    WhenFalse = criteria.GetProperty("false").Clone(),
                },
            },
            _ => throw new InvalidDataException($"Unsupported holdout question type: {kind}"),
        };
        return new DecisionRequest
        {
            Model = modelId,
            State = input.GetProperty("state").Clone(),
            Questions = new Dictionary<string, Question>(StringComparer.Ordinal) { ["decision"] = mapped },
        };
    }

    private static string ReferenceLabel(JsonElement value, string kind) => kind switch
    {
        "choice" => value.GetString() ?? throw new InvalidDataException("Missing choice target."),
        "score" => value.GetInt32().ToString(CultureInfo.InvariantCulture),
        "noul" => value.GetBoolean() ? "true" : "false",
        _ => throw new InvalidDataException($"Unsupported holdout question type: {kind}"),
    };

    private static EvaluationRow ScoreAnswer(string id, string family, string sourceFamily, string domain, string kind,
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

    private static EvaluationReport Summarize(List<EvaluationRow> rows, ModernBertModelPackage model, string backend,
        string datasetName, int datasetRecords, string datasetSha256, DateTimeOffset started, double elapsedMilliseconds,
        CancellationToken cancellationToken)
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
            Model = model.ModelId,
            ModelRevision = model.Revision,
            TokenizerRevision = model.TokenizerRevision,
            Backend = backend,
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
            ByType = Groups(rows, row => row.Type),
            ByTarget = Groups(rows, row => row.Target),
            ByDomain = Groups(rows, row => row.Domain),
            BySourceFamily = Groups(rows, row => row.SourceFamily),
            Rows = rows,
        };
    }

    private static List<EvaluationGroup> Groups(List<EvaluationRow> rows, Func<EvaluationRow, string> key) =>
        rows.GroupBy(key).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new EvaluationGroup(group.Key, group.Count(), group.Count(row => row.Correct is not null),
                group.Count(row => row.Correct == true), Ratio(group.Count(row => row.Correct == true),
                    group.Count(row => row.Correct is not null)))).ToList();

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
    double? ExpectedScore, double? ProbabilityTrue = null, string? AbstentionReason = null);

internal sealed record EvaluationGroup(string Name, int Total, int Answered, int Correct, double? AccuracyOnAnswered);

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
    public required List<EvaluationRow> Rows { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EvaluationReport))]
internal partial class EvaluationJsonContext : JsonSerializerContext { }
