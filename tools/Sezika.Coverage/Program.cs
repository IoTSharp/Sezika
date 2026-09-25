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
        if (args.Length is not (6 or 7) || !int.TryParse(args[4], out var count) || count is < 1 or > 10000 ||
            !int.TryParse(args[5], out var timeoutSeconds) || timeoutSeconds is < 1 or > 1800 ||
            args[3].Length != 64 || !args[3].All(Uri.IsHexDigit) ||
            (args.Length == 7 && args[6] is not ("strict" or "laya_compatible")))
        {
            Console.Error.WriteLine("Usage: Sezika.Coverage <model-dir> <eval.jsonl> <output.json> <dataset-sha256> <records> <1..1800 timeout-seconds> [strict|laya_compatible]");
            return 2;
        }

        var policy = args.Length == 7 && args[6] == "laya_compatible" ? PromptLengthPolicy.LayaCompatible : PromptLengthPolicy.Strict;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; deadline.Cancel(); };
        Console.CancelKeyPress += cancel;
        var watch = Stopwatch.StartNew();
        try
        {
            var modelDirectory = Path.GetFullPath(args[0]);
            var datasetPath = Path.GetFullPath(args[1]);
            var outputPath = Path.GetFullPath(args[2]);
            var modelPath = Path.Combine(modelDirectory, "model.json");
            var tokenizerPath = Path.Combine(modelDirectory, "tokenizer", "tokenizer.json");
            if (new FileInfo(modelPath).Length > 1_048_576 || new FileInfo(datasetPath).Length > 128L * 1024 * 1024)
                throw new InvalidDataException("Model manifest or dataset exceeds the diagnostic size limit.");
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(modelPath), new JsonDocumentOptions { MaxDepth = 32 });
            var model = manifest.RootElement;
            if (model.GetProperty("model_id").GetString() != ModernBertModelLoader.PinnedModelId ||
                model.GetProperty("revision").GetString() != ModernBertModelLoader.PinnedRevision ||
                model.GetProperty("tokenizer_revision").GetString() != ModernBertModelLoader.PinnedRevision ||
                !string.Equals(model.GetProperty("weights_sha256").GetString(),
                    ModernBertModelLoader.PinnedWeightsSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Model manifest is not the pinned Laya revision.");
            var tokenizerSha = Sha256(tokenizerPath, deadline.Token);
            if (!tokenizerSha.Equals(ModernBertModelLoader.PinnedTokenizerSha256, StringComparison.OrdinalIgnoreCase) ||
                !tokenizerSha.Equals(model.GetProperty("tokenizer_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pinned tokenizer hash mismatch.");
            var datasetSha = Sha256(datasetPath, deadline.Token);
            if (!datasetSha.Equals(args[3], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Frozen dataset SHA-256 mismatch.");

            var totalLimit = model.GetProperty("encoder").GetProperty("max_tokens").GetInt32();
            var prefixBudget = model.GetProperty("head").GetProperty("max_tokens").GetInt32();
            if (prefixBudget != 256 || totalLimit != 1024)
                throw new InvalidDataException("Pinned model limits differ from the independent 256-prefix/1024-total contract.");
            totalLimit = Math.Min(totalLimit, DecisionLimits.Default.MaxTokensPerQuestion);
            var tokenizer = new TokenizerJson(tokenizerPath, deadline.Token);
            var rows = new List<CoverageRow>(count);
            using var lines = File.ReadLines(datasetPath).GetEnumerator();
            for (var index = 0; index < count; index++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (!lines.MoveNext()) throw new InvalidDataException("Dataset has fewer rows than requested.");
                var line = lines.Current;
                if (string.IsNullOrWhiteSpace(line) || line.Length > 1_048_576)
                    throw new InvalidDataException("Blank or oversized dataset row.");
                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
                var root = document.RootElement;
                var input = root.GetProperty("input");
                var question = input.GetProperty("questions").GetProperty("decision");
                var kind = question.GetProperty("type").GetString() ?? throw new InvalidDataException("Question type missing.");
                var request = CreateRequest(input, question, kind, policy);
                string? validationFailure = null;
                try { DecisionRequestValidator.Validate(request); }
                catch (DecisionException exception) { validationFailure = exception.Code; }

                var strict = Measure(PromptLengthPolicy.Strict);
                var compatible = Measure(PromptLengthPolicy.LayaCompatible);
                var diagnostics = compatible.Diagnostics ?? strict.Diagnostics;
                var sequence = compatible.Sequence ?? strict.Sequence;
                var strictFailure = validationFailure ?? strict.FailureCode;
                var compatibleFailure = validationFailure ?? compatible.FailureCode;
                rows.Add(new CoverageRow(
                    root.GetProperty("id").GetString() ?? throw new InvalidDataException("ID missing."),
                    kind == "noul" ? "boolean" : kind,
                    sequence?.CandidateLabels ?? [], diagnostics?.OriginalTotalTokens, sequence?.TokenIds.Length,
                    diagnostics is null ? compatible.FailureCode ?? strict.FailureCode : null,
                    diagnostics is null ? null : !diagnostics.InstructionTruncated && !diagnostics.OptionsTruncated && !diagnostics.FinalSequenceTruncated,
                    diagnostics is null ? null : diagnostics.UntruncatedTotalTokens <= totalLimit && !diagnostics.FinalSequenceTruncated,
                    strictFailure, compatibleFailure,
                    policy == PromptLengthPolicy.Strict ? strictFailure : compatibleFailure, diagnostics));
                if (rows.Count % 10 == 0 || rows.Count == count)
                    Console.WriteLine($"Measured {rows.Count}/{count}; eligible under {policy}: {rows.Count(row => row.FailureCode is null)}; elapsed {watch.Elapsed.TotalSeconds:F1}s");

                Measurement Measure(PromptLengthPolicy lengthPolicy)
                {
                    try
                    {
                        var value = PromptSequenceBuilder.Build(tokenizer, request.State, request.Questions["decision"],
                            new PromptSequenceOptions { PrefixTokenBudget = prefixBudget, TotalTokenBudget = totalLimit,
                                LengthPolicy = lengthPolicy, MaxEncodedTokens = DiagnosticTokenCap }, deadline.Token);
                        return new(value, value.Diagnostics, null);
                    }
                    catch (PromptTruncationException exception) { return new(null, exception.Diagnostics, exception.Code); }
                    catch (DecisionException exception) when (exception.Code is not ("decision_deadline_exceeded" or "decision_cancelled"))
                    {
                        return new(null, null, exception.Code);
                    }
                }
            }
            deadline.Token.ThrowIfCancellationRequested();
            var report = new CoverageReport(
                ModernBertModelLoader.PinnedModelId, ModernBertModelLoader.PinnedRevision,
                tokenizerSha, datasetSha, count, prefixBudget, totalLimit, DiagnosticTokenCap, policy,
                rows.Count(row => row.FailureCode is null), rows.Count(row => row.StrictFailureCode is null),
                rows.Count(row => row.CompatibleFailureCode is null), rows.Count(row => row.PrefixUnclipped == true),
                rows.Count(row => row.TotalFitsAfterPrefixClipping == true), rows.Count(row => row.RequiredTokens is null),
                rows.GroupBy(row => row.Type).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new CoverageGroup(group.Key, group.Count(), group.Count(row => row.FailureCode is null),
                        group.Count(row => row.StrictFailureCode is null), group.Count(row => row.CompatibleFailureCode is null))).ToArray(),
                rows.GroupBy(row => row.FailureCode ?? "eligible").OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal), rows);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(report, CoverageJsonContext.Default.CoverageReport));
            Console.WriteLine($"Wrote {outputPath}; eligible {report.TokenEligible}/{count}, strict {report.StrictEligible}, compatible {report.CompatibleEligible}; original lengths {count - report.Unmeasured}/{count}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"coverage: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static DecisionRequest CreateRequest(JsonElement input, JsonElement question, string kind, PromptLengthPolicy policy)
    {
        var instructions = question.GetProperty("instructions").Clone();
        var hasCriteria = question.TryGetProperty("criteria", out var criteria);
        Question mapped = kind switch
        {
            "choice" => new ChoiceQuestion { Instructions = instructions,
                Criteria = criteria.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal) },
            "score" => new ScoreQuestion { Instructions = instructions,
                Criteria = criteria.EnumerateArray().Select(item => item.Clone()).ToArray() },
            "noul" or "boolean" => new BooleanQuestion { Instructions = instructions,
                Criteria = !hasCriteria || criteria.ValueKind == JsonValueKind.Null ? null : new BooleanCriteria
                {
                    WhenTrue = criteria.TryGetProperty("true", out var whenTrue) ? whenTrue.Clone() : default,
                    WhenFalse = criteria.TryGetProperty("false", out var whenFalse) ? whenFalse.Clone() : default,
                },
                Labels = question.TryGetProperty("labels", out var labels) && labels.ValueKind != JsonValueKind.Null ? new BooleanLabels
                {
                    WhenFalse = labels.GetProperty("false").GetString() ?? throw new InvalidDataException("Invalid false label."),
                    WhenTrue = labels.GetProperty("true").GetString() ?? throw new InvalidDataException("Invalid true label."),
                } : null },
            _ => throw new InvalidDataException($"Unsupported dataset question type: {kind}"),
        };
        if (mapped is BooleanQuestion && hasCriteria && criteria.ValueKind == JsonValueKind.Object &&
            criteria.EnumerateObject().Any(property => property.Name is not ("false" or "true")))
            throw new InvalidDataException("Boolean criteria has an unsupported key; refusing to silently drop it.");
        if (mapped is BooleanQuestion && question.TryGetProperty("labels", out var labelMap) &&
            labelMap.ValueKind == JsonValueKind.Object &&
            labelMap.EnumerateObject().Any(property => property.Name is not ("false" or "true")))
            throw new InvalidDataException("Boolean labels has an unsupported key; refusing to silently drop it.");
        return new DecisionRequest { Model = ModernBertModelLoader.PinnedModelId, State = input.GetProperty("state").Clone(),
            LengthPolicy = policy, Questions = new Dictionary<string, Question>(StringComparer.Ordinal) { ["decision"] = mapped } };
    }

    private static string Sha256(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashDataAsync(stream, cancellationToken).GetAwaiter().GetResult());
    }

    private sealed record Measurement(PromptSequence? Sequence, PromptSequenceDiagnostics? Diagnostics, string? FailureCode);
}

internal sealed record CoverageRow(string Id, string Type, string[] CandidateLabels, int? RequiredTokens, int? RetainedTokens,
    string? MeasurementFailure, bool? PrefixUnclipped, bool? TotalFitsAfterPrefixClipping,
    string? StrictFailureCode, string? CompatibleFailureCode, string? FailureCode, PromptSequenceDiagnostics? Diagnostics);

internal sealed record CoverageGroup(string Type, int Total, int TokenEligible, int StrictEligible, int CompatibleEligible);

internal sealed record CoverageReport(string ModelId, string ModelRevision, string TokenizerSha256,
    string DatasetSha256, int Processed, int PrefixTokenBudget, int TotalTokenBudget, int DiagnosticTokenCap,
    PromptLengthPolicy LengthPolicy, int TokenEligible, int StrictEligible, int CompatibleEligible,
    int PrefixUnclipped, int TotalFitsAfterPrefixClipping, int Unmeasured,
    CoverageGroup[] ByType, Dictionary<string, int> ByFailureCode, List<CoverageRow> Rows)
{
    public int SchemaVersion { get; init; } = 2;
    public string RenderingVersion { get; init; } = "sezika.prompt.laya-4066d5d5.v2";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CoverageReport))]
internal partial class CoverageJsonContext : JsonSerializerContext { }
