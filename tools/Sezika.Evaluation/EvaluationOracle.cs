using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Sezika;

internal static class EvaluationOracle
{
    public static int Prepare(string[] args) => Bounded(args, false);
    public static int Score(string[] args) => Bounded(args, true);

    private static int Bounded(string[] args, bool score)
    {
        var batches = args.Length > 0 && args[0] is "--prepare-oracle-batches" or "--score-captures";
        if (args.Length != 7 || !int.TryParse(args[3], out var total) || total is < 1 or > 10000 ||
            !int.TryParse(args[6], out var seconds) || seconds is < 1 or > 300 ||
            args[2].Length != 64 || !args[2].All(Uri.IsHexDigit) ||
            (!score && (!int.TryParse(args[4], out var selected) || selected < 1 || selected > Math.Min(46, total))))
        {
            Console.Error.WriteLine("Usage: --prepare-oracle <dataset.jsonl> <sha256> <dataset-total> <first-records:1..46> <new-manifest.json> <seconds:1..300>");
            Console.Error.WriteLine("   or: --score-capture <dataset.jsonl> <sha256> <dataset-total> <capture.json> <new-report.json> <seconds:1..300>");
            Console.Error.WriteLine("Batch variants: --prepare-oracle-batches uses records-per-batch 1..46 and a new output directory; --score-captures uses a hash-bound capture index and a new report.");
            return 2;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; deadline.Cancel(); };
        Console.CancelKeyPress += cancel;
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        try
        {
            var token = deadline.Token;
            var datasetHash = EvaluationInputs.FileHash(args[1], token);
            if (!datasetHash.Equals(args[2], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Dataset SHA-256 mismatch.");
            var rows = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            using var lines = File.ReadLines(args[1]).GetEnumerator();
            for (var index = 0; index < total; index++)
            {
                token.ThrowIfCancellationRequested();
                if (!lines.MoveNext()) throw new InvalidDataException("Dataset is shorter than its declared total.");
                using var document = EvaluationInputs.ParseRow(lines.Current, token);
                var id = document.RootElement.GetProperty("id").GetString();
                if (id is null || id.Length is < 1 or > 128 || !rows.TryAdd(id, document.RootElement.Clone()))
                    throw new InvalidDataException("Invalid or duplicate dataset ID.");
                if ((index + 1) % 100 == 0 || index + 1 == total) Console.WriteLine($"Read {index + 1}/{total} audit rows.");
            }
            if (lines.MoveNext()) throw new InvalidDataException("Dataset is longer than its declared total.");
            if (EvaluationInputs.FileHash(args[1], token) != datasetHash) throw new InvalidDataException("Dataset changed while reading audit inputs.");
            var output = Path.GetFullPath(args[5]);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (score)
            {
                var report = batches ? EvaluationBatches.Score(args[4], rows, datasetHash, total, started, watch, token)
                    : ReadScore(args[4], rows, datasetHash, total, started, watch, token);
                if (EvaluationInputs.FileHash(args[1], token) != datasetHash) throw new InvalidDataException("Dataset changed while scoring captures.");
                using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(destination, report, EvaluationJsonContext.Default.EvaluationReport);
            }
            else if (batches) EvaluationBatches.Prepare(output, rows, datasetHash, int.Parse(args[4], CultureInfo.InvariantCulture), token);
            else WriteManifest(output, rows, datasetHash, int.Parse(args[4], CultureInfo.InvariantCulture), token);
            Console.WriteLine($"Wrote {output}; no model was executed by this command.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine($"evaluation-oracle: {exception.GetType().Name}: {exception.Message}"); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }

    internal static void WriteManifest(string output, Dictionary<string, JsonElement> rows, string datasetHash, int count, CancellationToken token, int skip = 0)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "sezika.laya-oracle-inputs.v1");
            writer.WriteString("authorship", "External audit inputs; retain original dataset licensing. No redistribution or training permission is asserted.");
            writer.WriteString("measurement_status", "not_executed");
            writer.WriteString("dataset_sha256", datasetHash);
            writer.WriteNumber("dataset_total", rows.Count);
            writer.WriteStartArray("cases");
            foreach (var (id, row) in rows.Skip(skip).Take(count))
            {
                token.ThrowIfCancellationRequested();
                var input = row.GetProperty("input");
                _ = EvaluationInputs.Request(input, ModernBertModelLoader.PinnedModelId, PromptLengthPolicy.LayaCompatible);
                var question = input.GetProperty("questions").GetProperty("decision");
                var kind = question.GetProperty("type").GetString();
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteString("primitive", kind is "noul" or "boolean" ? "boolean" : kind);
                writer.WriteString("language", Language(row));
                writer.WritePropertyName("state"); input.GetProperty("state").WriteTo(writer);
                writer.WriteStartObject("question");
                foreach (var property in question.EnumerateObject())
                {
                    if (property.Name == "type") writer.WriteString("type", kind == "boolean" ? "noul" : kind);
                    else property.WriteTo(writer);
                }
                writer.WriteEndObject();
                writer.WriteStartArray("tags"); writer.WriteStringValue("viewed-evaluation-audit"); writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        if (bytes.Length > 1_048_576) throw new InvalidDataException("Prepared manifest exceeds the 1 MiB oracle input bound; select fewer rows.");
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        bytes.Position = 0; bytes.CopyTo(file);
    }

    internal static EvaluationReport ReadScore(string capturePath, Dictionary<string, JsonElement> source, string datasetHash,
        int total, DateTimeOffset started, Stopwatch watch, CancellationToken token)
    {
        var captureHash = EvaluationInputs.FileHash(capturePath, token);
        using var capture = EvaluationInputs.ParseJson(File.ReadAllBytes(capturePath), 1_000_000, 64, token);
        var root = capture.RootElement;
        var provenance = root.GetProperty("provenance");
        var measurementStatus = root.GetProperty("measurement_status").GetString();
        if (measurementStatus is not ("complete" or "completed" or "selected_cases_only"))
            throw new InvalidDataException("Incomplete or failed capture cannot be scored as a completed selection.");
        if (root.TryGetProperty("fatal_error", out var fatal) && fatal.ValueKind != JsonValueKind.Null ||
            root.TryGetProperty("unprocessed_case_ids", out var unprocessed) && unprocessed.GetArrayLength() != 0)
            throw new InvalidDataException("Capture has a fatal error or unprocessed selected cases.");
        var lengthPolicy = root.TryGetProperty("implementation", out var implementation)
            ? implementation.GetProperty("length_policy").GetString() : "laya_compatible";
        if (lengthPolicy is not ("strict" or "laya_compatible")) throw new InvalidDataException("Unknown capture length policy.");
        if (root.GetProperty("schema_version").GetString() != "sezika.laya-oracle.v1" ||
            provenance.GetProperty("model_id").GetString() != ModernBertModelLoader.PinnedModelId ||
            provenance.GetProperty("model_revision").GetString() != ModernBertModelLoader.PinnedRevision ||
            provenance.GetProperty("weights_sha256").GetString() != ModernBertModelLoader.PinnedWeightsSha256 ||
            provenance.GetProperty("tokenizer_sha256").GetString() != ModernBertModelLoader.PinnedTokenizerSha256 ||
            provenance.GetProperty("upstream_source_revision").GetString() != "4066d5d5fbf08b66c6757ddeedbd797bd7655bc0" ||
            provenance.GetProperty("contract_sha256").GetString() != "771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee" ||
            provenance.GetProperty("temperature_policy").GetString() != "checkpoint_fixed_1_no_overrides" ||
            provenance.GetProperty("head_max_len").GetInt32() != 256 || provenance.GetProperty("max_len").GetInt32() != 1024)
            throw new InvalidDataException("Capture model or input contract identity differs.");
        var cases = root.GetProperty("cases");
        if (cases.GetArrayLength() is < 1 or > 46) throw new InvalidDataException("Score at most 46 captured rows per invocation.");
        var selectedCount = root.TryGetProperty("selected_case_count", out var count) ? count.GetInt32() : root.GetProperty("selected_case_ids").GetArrayLength();
        var manifestCount = root.GetProperty("total_manifest_cases").GetInt32();
        if (selectedCount != cases.GetArrayLength() || manifestCount < selectedCount || manifestCount > 64)
            throw new InvalidDataException("Capture selection and manifest counts are inconsistent.");
        HashSet<string>? selectedIds = null;
        if (root.TryGetProperty("selected_case_ids", out var selectedValues))
        {
            selectedIds = selectedValues.EnumerateArray().Select(value => value.GetString() ?? throw new InvalidDataException("Null selected case ID.")).ToHashSet(StringComparer.Ordinal);
            if (selectedValues.GetArrayLength() != selectedCount || selectedIds.Count != selectedCount)
                throw new InvalidDataException("Capture selected IDs are duplicate or count-inconsistent.");
        }
        var rows = new List<EvaluationRow>(cases.GetArrayLength());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var captured in cases.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var id = captured.GetProperty("id").GetString()!;
            if (!seen.Add(id) || !source.TryGetValue(id, out var original)) throw new InvalidDataException("Unknown or duplicate capture ID.");
            var originalInput = original.GetProperty("input");
            var capturedInput = captured.GetProperty("input");
            if (capturedInput.GetProperty("head_max_len").GetInt32() != 256 || capturedInput.GetProperty("max_len").GetInt32() != 1024)
                throw new InvalidDataException("Per-row token budget differs from the captured contract.");
            using var requestBytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(requestBytes))
            {
                writer.WriteStartObject(); writer.WritePropertyName("state"); capturedInput.GetProperty("state").WriteTo(writer);
                writer.WriteStartObject("questions"); writer.WritePropertyName("decision"); capturedInput.GetProperty("question").WriteTo(writer);
                writer.WriteEndObject(); writer.WriteEndObject();
            }
            using var reconstructed = JsonDocument.Parse(requestBytes.ToArray());
            var originalRequest = EvaluationInputs.Request(originalInput, ModernBertModelLoader.PinnedModelId, PromptLengthPolicy.LayaCompatible);
            var captureRequest = EvaluationInputs.Request(reconstructed.RootElement, ModernBertModelLoader.PinnedModelId, PromptLengthPolicy.LayaCompatible);
            // Serialized candidate order is significant, including for object-valued Choice criteria.
            var originalRequestJson = JsonSerializer.Serialize(originalRequest, DecisionJsonContext.Default.DecisionRequest);
            var captureRequestJson = JsonSerializer.Serialize(captureRequest, DecisionJsonContext.Default.DecisionRequest);
            if (originalRequestJson != captureRequestJson) throw new InvalidDataException("Capture does not contain the original ordered input.");
            var question = originalRequest.Questions["decision"];
            var kind = question switch { ChoiceQuestion => "choice", ScoreQuestion => "score", BooleanQuestion => "noul", _ => throw new InvalidDataException("Unknown question.") };
            if (captured.GetProperty("primitive").GetString() != (kind == "noul" ? "boolean" : kind)) throw new InvalidDataException("Capture primitive mismatch.");
            var targetValue = original.GetProperty("reference").GetProperty("target");
            var target = kind switch { "choice" => targetValue.GetString()!, "score" => targetValue.GetInt32().ToString(CultureInfo.InvariantCulture), _ => targetValue.GetBoolean() ? "true" : "false" };
            var family = original.GetProperty("family").GetString()!;
            var sourceFamily = original.GetProperty("source_family").GetString()!;
            var domain = original.GetProperty("domain").GetString()!;
            EvaluationRow row;
            if (captured.GetProperty("status").GetString() == "failed")
            {
                if (new[] { "raw_logits", "probabilities", "prediction" }.Any(name => captured.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null) ||
                    !captured.TryGetProperty("failure", out var failure) || failure.ValueKind != JsonValueKind.Object ||
                    string.IsNullOrWhiteSpace(failure.GetProperty("type").GetString()))
                    throw new InvalidDataException("Failed capture carries numerical output or lacks failure semantics.");
                row = new(id, family, sourceFamily, domain, kind, target, null, null,
                    failure.GetProperty("type").GetString(), null, 0, 0, null, null, null, null, null);
            }
            else if (captured.GetProperty("status").GetString() == "answered")
            {
                if (captured.TryGetProperty("failure", out var failure) && failure.ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException("Answered capture must not carry failure semantics.");
                var labels = captured.GetProperty("candidate_labels").EnumerateArray().Select(value => value.GetString()!).ToArray();
                var expectedLabels = question switch { ChoiceQuestion choice => choice.Criteria.Keys.ToArray(),
                    ScoreQuestion score => Enumerable.Range(0, score.Criteria.Length).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray(),
                    _ => ["false", "true"] };
                if (!labels.SequenceEqual(expectedLabels)) throw new InvalidDataException("Candidate order differs from the original input.");
                var probabilities = captured.GetProperty("probabilities").EnumerateArray().Select(value => value.GetDouble()).ToArray();
                var logits = captured.GetProperty("raw_logits").EnumerateArray().Select(value => value.GetSingle()).ToArray();
                if (probabilities.Length != labels.Length || logits.Length != labels.Length || probabilities.Any(value => !double.IsFinite(value) || value < 0 || value > 1) ||
                    logits.Any(value => !float.IsFinite(value)) || Math.Abs(probabilities.Sum() - 1) > 0.000002)
                    throw new InvalidDataException("Invalid captured distribution.");
                var softmax = logits.Select(value => Math.Exp(value - (double)logits.Max())).ToArray();
                var softmaxSum = softmax.Sum();
                for (var index = 0; index < logits.Length; index++)
                    if (Math.Abs(softmax[index] / softmaxSum - probabilities[index]) > 0.000002)
                        throw new InvalidDataException("Captured probabilities differ from unit-temperature logits.");
                var predicted = 0;
                for (var index = 1; index < probabilities.Length; index++) if (probabilities[index] > probabilities[predicted]) predicted = index;
                var dictionary = labels.Select((label, index) => new KeyValuePair<string, double>(label, probabilities[index])).ToDictionary();
                var calibration = new CalibrationInfo { Status = "uncalibrated" };
                Answer answer = kind switch
                {
                    "choice" => new ChoiceAnswer { Status = "answered", Calibration = calibration, Choice = labels[predicted], Probabilities = dictionary, Concentration = 0 },
                    "score" => new ScoreAnswer { Status = "answered", Calibration = calibration, Score = probabilities.Select((value, index) => value * index).Sum(), Probabilities = dictionary, Legend = [], Concentration = 0 },
                    _ => new BooleanAnswer { Status = "answered", Calibration = calibration, ProbabilityTrue = probabilities[1] },
                };
                var tokens = captured.GetProperty("token_ids").EnumerateArray().Select(value => value.GetInt32()).ToArray();
                var markers = captured.GetProperty("marker_positions").EnumerateArray().Select(value => value.GetInt32()).ToArray();
                if (tokens.Length is < 1 or > 1024 || tokens.Any(value => value < 0) || markers.Length != labels.Length ||
                    markers.Distinct().Count() != labels.Length || markers.Any(value => value < 0 || value >= tokens.Length))
                    throw new InvalidDataException("Invalid captured token or marker shape.");
                var prediction = captured.GetProperty("prediction");
                var matchesPrediction = answer switch
                {
                    ChoiceAnswer choice => prediction.GetProperty("choice_label").GetString() == choice.Choice,
                    ScoreAnswer score => Math.Abs(prediction.GetProperty("score").GetDouble() - score.Score) <= 0.000002,
                    BooleanAnswer boolean => prediction.GetProperty("boolean").GetBoolean() == (boolean.ProbabilityTrue >= 0.5) &&
                        Math.Abs(prediction.GetProperty("probability_true").GetDouble() - boolean.ProbabilityTrue) <= 0.000002,
                    _ => false,
                };
                if (!matchesPrediction) throw new InvalidDataException("Captured prediction differs from the recorded distribution.");
                row = Evaluation.ScoreAnswer(id, family, sourceFamily, domain, kind, target, answer, tokens.Length, 0) with
                { CandidateLabels = labels, Logits = logits, TokenIdsSha256 = EvaluationInputs.TokenHash(tokens),
                    MarkerPositions = markers, ForwardCalls = null };
            }
            else throw new InvalidDataException("Unknown capture status.");
            rows.Add(row with { Language = Language(original), InputSha256 = EvaluationInputs.TextHash(originalInput.GetRawText()) });
        }
        if (selectedIds is not null && !selectedIds.SetEquals(seen)) throw new InvalidDataException("Selected case IDs differ from captured cases.");
        var backend = implementation.ValueKind == JsonValueKind.Object ? implementation.GetProperty("backend").GetString()! : provenance.GetProperty("backend").GetString()!;
        var report = Evaluation.Summarize(rows, backend,
            Path.GetFileNameWithoutExtension(capturePath), total, datasetHash, started, watch.Elapsed.TotalMilliseconds, lengthPolicy, token);
        report.CaptureSha256 = captureHash;
        report.CaptureStatus = measurementStatus;
        report.CaptureSelectedCases = selectedCount;
        report.CaptureManifestCases = manifestCount;
        report.CaptureProvenance = provenance.Clone();
        report.CaptureImplementation = implementation.ValueKind == JsonValueKind.Object ? implementation.Clone() : null;
        report.CaptureCasesSha256 = provenance.GetProperty("cases_sha256").GetString();
        report.CaptureContractSha256 = provenance.GetProperty("contract_sha256").GetString();
        report.MeasurementOrigin = "offline_scoring_of_existing_capture_not_new_inference_or_parity_proof";
        if (EvaluationInputs.FileHash(capturePath, token) != captureHash) throw new InvalidDataException("Capture changed while scoring.");
        return report;
    }

    private static string Language(JsonElement row) => EvaluationInputs.Language(row);
}
