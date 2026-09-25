using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

return Coverage.Run(args);

internal static class Coverage
{
    private const int DiagnosticTokenCap = 32768;

    public static int Run(string[] args)
    {
        if (args.Length != 6 || !int.TryParse(args[4], out var count) || count is < 1 or > 10000 ||
            !int.TryParse(args[5], out var timeoutSeconds) || timeoutSeconds is < 1 or > 1800 ||
            args[3].Length != 64 || !args[3].All(Uri.IsHexDigit))
        {
            Console.Error.WriteLine("Usage: Sezika.Coverage <model-dir> <eval.jsonl> <output.json> <dataset-sha256> <records> <1..1800 timeout-seconds>");
            return 2;
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var watch = Stopwatch.StartNew();
        try
        {
            var modelDirectory = Path.GetFullPath(args[0]);
            var datasetPath = Path.GetFullPath(args[1]);
            var outputPath = Path.GetFullPath(args[2]);
            var modelPath = Path.Combine(modelDirectory, "model.json");
            var tokenizerPath = Path.Combine(modelDirectory, "tokenizer", "tokenizer.json");
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(modelPath), new JsonDocumentOptions { MaxDepth = 32 });
            var model = manifest.RootElement;
            if (model.GetProperty("model_id").GetString() != ModernBertModelLoader.PinnedModelId ||
                model.GetProperty("revision").GetString() != ModernBertModelLoader.PinnedRevision ||
                model.GetProperty("tokenizer_revision").GetString() != ModernBertModelLoader.PinnedRevision ||
                !string.Equals(model.GetProperty("weights_sha256").GetString(),
                    ModernBertModelLoader.PinnedWeightsSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Model manifest is not the pinned Laya revision.");
            var tokenizerSha = Sha256(tokenizerPath);
            if (!tokenizerSha.Equals(ModernBertModelLoader.PinnedTokenizerSha256, StringComparison.OrdinalIgnoreCase) ||
                !tokenizerSha.Equals(model.GetProperty("tokenizer_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pinned tokenizer hash mismatch.");
            var datasetSha = Sha256(datasetPath);
            if (!datasetSha.Equals(args[3], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Frozen dataset SHA-256 mismatch.");

            var encoderLimit = model.GetProperty("encoder").GetProperty("max_tokens").GetInt32();
            var headLimit = model.GetProperty("head").GetProperty("max_tokens").GetInt32();
            if (headLimit != 256 || encoderLimit != 1024)
                throw new InvalidDataException("Pinned model token limits differ from the validated 256/1024 contract.");
            var tokenizer = new TokenizerJson(tokenizerPath, deadline.Token);
            var rows = new List<CoverageRow>(count);
            foreach (var line in File.ReadLines(datasetPath))
            {
                if (rows.Count == count) break;
                deadline.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line) || line.Length > 1_048_576)
                    throw new InvalidDataException("Blank or oversized dataset row.");
                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
                var root = document.RootElement;
                var input = root.GetProperty("input");
                var question = input.GetProperty("questions").GetProperty("decision");
                var kind = question.GetProperty("type").GetString() ?? throw new InvalidDataException("Question type missing.");
                var request = CreateRequest(input, question, kind);
                var criteria = request.Questions["decision"] switch
                {
                    ChoiceQuestion choice => choice.Criteria.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value).ToArray(),
                    ScoreQuestion score => score.Criteria,
                    BooleanQuestion boolean => [boolean.Criteria!.WhenTrue, boolean.Criteria.WhenFalse],
                    _ => throw new InvalidDataException("Unsupported question type."),
                };
                string? failure = null;
                try
                {
                    DecisionRequestValidator.Validate(request);
                    MarkerSequenceBuilder.Build(tokenizer, request.State, request.Questions["decision"].Instructions,
                        criteria, headLimit, deadline.Token);
                }
                catch (DecisionException exception) { failure = exception.Code; }

                int? requiredTokens = null;
                string? measurementFailure = null;
                try
                {
                    requiredTokens = MarkerSequenceBuilder.Build(tokenizer, request.State,
                        request.Questions["decision"].Instructions, criteria, DiagnosticTokenCap, deadline.Token).Tokens.Length;
                }
                catch (DecisionException exception) when (exception.Code is "decision_token_limit_exceeded" or "decision_token_budget_exceeded")
                {
                    measurementFailure = "diagnostic_token_cap_exceeded";
                }
                rows.Add(new CoverageRow(root.GetProperty("id").GetString() ?? throw new InvalidDataException("ID missing."),
                    kind == "noul" ? "boolean" : kind, criteria.Length, requiredTokens, measurementFailure,
                    requiredTokens is int tokens ? tokens <= headLimit : null,
                    requiredTokens is int encoderTokens ? encoderTokens <= encoderLimit : null, failure));
                if (rows.Count % 10 == 0 || rows.Count == count)
                    Console.WriteLine($"Measured {rows.Count}/{count}; token eligible {rows.Count(row => row.FailureCode is null)}; elapsed {watch.Elapsed.TotalSeconds:F1}s");
            }
            if (rows.Count != count) throw new InvalidDataException("Dataset has fewer rows than requested.");
            var report = new CoverageReport(
                ModernBertModelLoader.PinnedModelId, ModernBertModelLoader.PinnedRevision,
                tokenizerSha, datasetSha, count, headLimit, encoderLimit, DiagnosticTokenCap,
                rows.Count(row => row.FailureCode is null), rows.Count(row => row.HeadFits == true),
                rows.Count(row => row.EncoderFits == true), rows.Count(row => row.RequiredTokens is null),
                rows.GroupBy(row => row.Type).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new CoverageGroup(group.Key, group.Count(), group.Count(row => row.FailureCode is null),
                        group.Count(row => row.HeadFits == true), group.Count(row => row.EncoderFits == true))).ToArray(),
                rows.GroupBy(row => row.FailureCode ?? "eligible").OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal), rows);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(report, CoverageJsonContext.Default.CoverageReport));
            Console.WriteLine($"Wrote {outputPath}; token eligible {report.TokenEligible}/{count}, exact lengths {count - report.Unmeasured}/{count}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"coverage: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static DecisionRequest CreateRequest(JsonElement input, JsonElement question, string kind)
    {
        var instructions = question.GetProperty("instructions").Clone();
        var criteria = question.GetProperty("criteria");
        Question mapped = kind switch
        {
            "choice" => new ChoiceQuestion { Instructions = instructions,
                Criteria = criteria.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal) },
            "score" => new ScoreQuestion { Instructions = instructions,
                Criteria = criteria.EnumerateArray().Select(item => item.Clone()).ToArray() },
            "noul" => new BooleanQuestion { Instructions = instructions,
                Criteria = new BooleanCriteria { WhenTrue = criteria.GetProperty("true").Clone(),
                    WhenFalse = criteria.GetProperty("false").Clone() } },
            _ => throw new InvalidDataException($"Unsupported dataset question type: {kind}"),
        };
        return new DecisionRequest { Model = ModernBertModelLoader.PinnedModelId, State = input.GetProperty("state").Clone(),
            Questions = new Dictionary<string, Question>(StringComparer.Ordinal) { ["decision"] = mapped } };
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

internal sealed record CoverageRow(string Id, string Type, int Candidates, int? RequiredTokens,
    string? MeasurementFailure, bool? HeadFits, bool? EncoderFits, string? FailureCode);

internal sealed record CoverageGroup(string Type, int Total, int TokenEligible, int HeadFits, int EncoderFits);

internal sealed record CoverageReport(string ModelId, string ModelRevision, string TokenizerSha256,
    string DatasetSha256, int Processed, int HeadMaxTokens, int EncoderMaxTokens, int DiagnosticTokenCap,
    int TokenEligible, int HeadFits, int EncoderFits, int Unmeasured,
    CoverageGroup[] ByType, Dictionary<string, int> ByFailureCode, List<CoverageRow> Rows);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CoverageReport))]
internal partial class CoverageJsonContext : JsonSerializerContext { }
