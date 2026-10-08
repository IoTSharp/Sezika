using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.IndependentModelTool;

// This command consumes diagnostic results only. It never opens original records,
// model assets or question text, and an external review declaration is not proof
// of reviewer identity or permission to train, calibrate or execute an action.
internal static class ReviewedSliceDiagnostics
{
    internal const int InputLimit = 1_048_576;
    internal const int OutputLimit = 2_097_152;
    internal const string Contract = "reviewed-development-slices-v1";
    internal static readonly string[] Dimensions = ["role", "negation", "lexical_similarity", "candidate_order"];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Run(string[] args, Action<Utf8JsonWriter, IReadOnlyList<OriginalEvaluationRow>> metrics, CancellationToken token)
    {
        var options = ParseOptions(args, ["--evaluation", "--evaluation-sha256", "--labels", "--labels-sha256", "--output"]);
        var evaluationPath = Path.GetFullPath(options["--evaluation"]);
        var labelsPath = Path.GetFullPath(options["--labels"]);
        var output = Path.GetFullPath(options["--output"]);
        if (SamePath(output, evaluationPath) || SamePath(output, labelsPath) || File.Exists(output))
            throw new IOException("Output must be a new file distinct from both inputs.");
        using var evaluation = new ReadLease(evaluationPath, options["--evaluation-sha256"], token);
        using var labels = new ReadLease(labelsPath, options["--labels-sha256"], token);
        Console.WriteLine("group-development-report: bounded input identity verified; validating 1-32 diagnostic rows");
        var joined = Parse(evaluation.Bytes, evaluation.Sha256, labels.Bytes, labels.Sha256, token);
        var bytes = Render(joined, metrics, token);
        Publish(output, bytes, token);
        Console.WriteLine($"group-development-report: rows={joined.Rows.Length} labels={joined.Labels.Rows.Length} bytes={bytes.Length} sha256={Hash(bytes)} output={output}; diagnostic only");
    }

    internal static Dictionary<string, string> ParseOptions(string[] args, string[] names)
    {
        if (args.Length != 1 + names.Length * 2) throw new ArgumentException("Every named option must occur exactly once with a value.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
            if (!names.Contains(args[index], StringComparer.Ordinal) || string.IsNullOrWhiteSpace(args[index + 1]) || !options.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("Unknown, duplicate or empty option.");
        if (names.Any(name => !options.ContainsKey(name))) throw new ArgumentException("Required option is missing.");
        return options;
    }

    internal static JoinedReport Parse(byte[] evaluationBytes, string evaluationHash, byte[] labelsBytes, string labelsHash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequireHash(evaluationHash); RequireHash(labelsHash);
        if (!Hash(evaluationBytes).Equals(evaluationHash, StringComparison.OrdinalIgnoreCase) || !Hash(labelsBytes).Equals(labelsHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Input bytes do not match their pinned SHA-256.");
        ValidateJson(evaluationBytes, token); ValidateJson(labelsBytes, token);
        var evaluation = JsonSerializer.Deserialize(evaluationBytes, SliceJsonContext.Default.EvaluationInput) ?? throw new InvalidDataException("Evaluation is null.");
        var labels = JsonSerializer.Deserialize(labelsBytes, SliceJsonContext.Default.LabelSidecar) ?? throw new InvalidDataException("Labels are null.");
        if (evaluation.SchemaVersion != 1 || evaluation.Status != "development_diagnostic_only" || evaluation.Split != "development" ||
            evaluation.EvaluationUse != "independent_development_diagnostics_not_training_calibration_or_sealed_test")
            throw new InvalidDataException("Only schema1 development diagnostic evaluations are accepted.");
        RequireText(evaluation.ModelId, 128); RequireText(evaluation.EvaluationBackend, 128);
        if (evaluation.TrainingBackend is not null) RequireText(evaluation.TrainingBackend, 128);
        foreach (var hash in new[] { evaluation.EncoderSha256, evaluation.TokenizerSha256, evaluation.DataManifestSha256, evaluation.RecordsSha256,
                     evaluation.TrainingReportSha256, evaluation.FeatureSha256, evaluation.HeadSha256 }) RequireHash(hash);
        if (evaluation.Rows is null || evaluation.Rows.Length is < 1 or > 32) throw new InvalidDataException("Evaluation requires 1-32 rows.");
        var rows = new OriginalEvaluationRow[evaluation.Rows.Length];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            rows[index] = ValidateRow(evaluation.Rows[index]);
            if (!seen.Add(rows[index].RecordId)) throw new InvalidDataException("Duplicate evaluation record ID.");
        }
        var answered = rows.Count(row => row.Status == "answered");
        if (evaluation.Total != rows.Length || evaluation.Answered != answered || evaluation.Rejected != rows.Length - answered ||
            !Near(evaluation.Coverage, answered / (double)rows.Length)) throw new InvalidDataException("Evaluation header denominator is inconsistent.");
        if (evaluation.Metrics.ValueKind != JsonValueKind.Object || evaluation.DiagnosticDimensions.ValueKind != JsonValueKind.Object ||
            evaluation.ByLanguageAndType.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Evaluation diagnostic metadata is missing.");
        // Upstream aggregate metrics are untrusted: all output metrics use the
        // recomputed, candidate-mapped rows above, never the input aggregates.
        if (labels.SchemaVersion != 1 || labels.Contract != Contract || labels.ReviewOrigin != "external_declared_human_review" || labels.Split != "development" ||
            !EqualHash(labels.EvaluationSha256, evaluationHash) || !EqualHash(labels.DataManifestSha256, evaluation.DataManifestSha256) ||
            !EqualHash(labels.RecordsSha256, evaluation.RecordsSha256)) throw new InvalidDataException("Sidecar contract, split or bound input identity differs.");
        if (labels.Vocabularies is null || labels.Vocabularies.Length is < 1 or > 4 || labels.Rows is null || labels.Rows.Length > 32)
            throw new InvalidDataException("Sidecar vocabulary/row bounds are invalid.");
        var vocabularies = new Dictionary<string, SliceVocabulary>(StringComparer.Ordinal);
        foreach (var vocabulary in labels.Vocabularies)
        {
            token.ThrowIfCancellationRequested();
            if (vocabulary is null || !Dimensions.Contains(vocabulary.Dimension, StringComparer.Ordinal) || !vocabularies.TryAdd(vocabulary.Dimension, vocabulary))
                throw new InvalidDataException("Unknown or duplicate review dimension.");
            RequireText(vocabulary.Version, 64);
            if (vocabulary.Values is null || vocabulary.Values.Length is < 1 or > 16) throw new InvalidDataException("Vocabulary requires 1-16 values.");
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in vocabulary.Values)
            {
                RequireText(value, 64);
                if (value == "unknown" || !values.Add(value)) throw new InvalidDataException("Duplicate or reserved vocabulary value.");
            }
        }
        var byId = rows.ToDictionary(row => row.RecordId, StringComparer.Ordinal);
        var reviewRows = new Dictionary<string, SliceReviewRow>(StringComparer.Ordinal);
        var evidenceBytes = 0;
        foreach (var reviewRow in labels.Rows)
        {
            token.ThrowIfCancellationRequested();
            if (reviewRow is null || reviewRow.RecordId is null || !byId.TryGetValue(reviewRow.RecordId, out var row) || !reviewRows.TryAdd(reviewRow.RecordId, reviewRow))
                throw new InvalidDataException("Duplicate or unknown sidecar record ID.");
            if (reviewRow.CandidateIds is null || !reviewRow.CandidateIds.SequenceEqual(row.CandidateIds, StringComparer.Ordinal) ||
                reviewRow.Reviews is null || reviewRow.Reviews.Length is < 1 or > 4) throw new InvalidDataException("Sidecar candidate order or review count differs.");
            var reviewDimensions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var review in reviewRow.Reviews)
            {
                token.ThrowIfCancellationRequested();
                if (review is null || review.Dimension is null || !vocabularies.TryGetValue(review.Dimension, out var vocabulary) || !reviewDimensions.Add(review.Dimension))
                    throw new InvalidDataException("Review dimension is duplicate or lacks a versioned vocabulary.");
                if (review.Status is not ("approved" or "pending" or "rejected")) throw new InvalidDataException("Review status is unsupported.");
                if (review.Status == "approved")
                {
                    if (review.Value is null || !vocabulary.Values.Contains(review.Value, StringComparer.Ordinal)) throw new InvalidDataException("Approved label is outside its vocabulary.");
                }
                else if (review.Value is not null) throw new InvalidDataException("Unapproved reviews must not assign a label value.");
                if (review.Status == "pending")
                {
                    if (review.Reviewer is not null || review.ReviewedAt is not null || review.Evidence is not null || review.EvidenceSha256 is not null)
                        throw new InvalidDataException("Pending review cannot claim completed review evidence.");
                }
                else
                {
                    RequireText(review.Reviewer, 128); RequireText(review.ReviewedAt, 64); RequireText(review.Evidence, 65_536);
                    if (!(review.ReviewedAt!.EndsWith('Z') || review.ReviewedAt.EndsWith("+00:00", StringComparison.Ordinal)) ||
                        !DateTimeOffset.TryParse(review.ReviewedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) || timestamp.Offset != TimeSpan.Zero)
                        throw new InvalidDataException("Completed review requires an explicit UTC timestamp.");
                    var evidence = StrictUtf8.GetBytes(review.Evidence!);
                    evidenceBytes = checked(evidenceBytes + evidence.Length);
                    if (evidenceBytes > 65_536 || !EqualHash(review.EvidenceSha256, Hash(evidence))) throw new InvalidDataException("Review evidence exceeds 64 KiB or its SHA-256 differs.");
                }
            }
        }
        return new JoinedReport(evaluation, labels, rows, reviewRows, evaluationHash.ToLowerInvariant(), labelsHash.ToLowerInvariant());
    }

    private static OriginalEvaluationRow ValidateRow(EvaluationRow row)
    {
        if (row is null) throw new InvalidDataException("Evaluation row is null.");
        RequireText(row.RecordId, 128);
        if (row.Language is not ("zh" or "en") || row.QuestionType is not ("choice" or "score" or "boolean") ||
            row.CandidateIds is null || row.CandidateIds.Length is < 2 or > 32 || row.TargetIndex < 0 || row.TargetIndex >= row.CandidateIds.Length)
            throw new InvalidDataException("Evaluation row type, language, candidates or target is invalid.");
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in row.CandidateIds) { RequireText(id, 128); if (!candidates.Add(id)) throw new InvalidDataException("Duplicate candidate ID."); }
        if ((row.QuestionType == "boolean" && !row.CandidateIds.SequenceEqual(["false", "true"], StringComparer.Ordinal)) ||
            (row.QuestionType == "score" && (row.CandidateIds.Length > 10 || !row.CandidateIds.SequenceEqual(Enumerable.Range(0, row.CandidateIds.Length).Select(index => index.ToString(CultureInfo.InvariantCulture)), StringComparer.Ordinal))))
            throw new InvalidDataException("Boolean/score candidate mapping differs from the primitive contract.");
        if (row.Status == "rejected")
        {
            if (row.CandidateCount is not 0 && row.CandidateCount != row.CandidateIds.Length || row.PredictedIndex is not null || row.Correct is not null ||
                row.TargetProbability is not null || row.TopProbability is not null || row.Margin is not null || row.Nll is not null || row.Brier is not null || row.Probabilities is { Length: > 0 })
                throw new InvalidDataException("Rejected row contains an answer or probability.");
            RequireText(row.ErrorCode, 128);
            return new OriginalEvaluationRow(row.RecordId, row.Language, row.QuestionType, row.CandidateIds, row.TargetIndex, row.CandidateCount,
                row.Status, null, null, null, null, null, null, null, [], row.ErrorCode);
        }
        if (row.Status != "answered" || row.CandidateCount != row.CandidateIds.Length || row.Probabilities is null || row.Probabilities.Length != row.CandidateCount || row.ErrorCode is not null)
            throw new InvalidDataException("Answered row candidate/probability/status contract is invalid.");
        var probabilities = row.Probabilities;
        if (probabilities.Any(value => !double.IsFinite(value) || value < 0 || value > 1) || !Near(probabilities.Sum(), 1))
            throw new InvalidDataException("Probabilities must be finite, in [0,1], and sum to one.");
        var predicted = 0;
        for (var index = 1; index < probabilities.Length; index++) if (probabilities[index] > probabilities[predicted]) predicted = index;
        var top = probabilities[predicted];
        var second = probabilities.Where((_, index) => index != predicted).Max();
        var nll = -Math.Log(Math.Max(probabilities[row.TargetIndex], double.Epsilon));
        var brier = probabilities.Select((value, index) => Math.Pow(value - (index == row.TargetIndex ? 1 : 0), 2)).Sum();
        if (row.PredictedIndex != predicted || row.Correct != (predicted == row.TargetIndex) || !Near(row.TargetProbability, probabilities[row.TargetIndex]) ||
            !Near(row.TopProbability, top) || !Near(row.Margin, top - second) || !Near(row.Nll, nll) || !Near(row.Brier, brier))
            throw new InvalidDataException("Self-reported answer or derived numeric values disagree with candidate probabilities.");
        return new OriginalEvaluationRow(row.RecordId, row.Language, row.QuestionType, row.CandidateIds, row.TargetIndex, row.CandidateCount,
            row.Status, predicted, predicted == row.TargetIndex, probabilities[row.TargetIndex], top, top - second, nll, brier, probabilities, null);
    }

    internal static byte[] Render(JoinedReport report, Action<Utf8JsonWriter, IReadOnlyList<OriginalEvaluationRow>> metrics, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteNumber("schema_version", 1); writer.WriteString("status", "development_reviewed_slice_diagnostic_only");
            writer.WriteString("split", "development"); writer.WriteString("review_origin", "external_declared_human_review_not_identity_verified");
            writer.WriteString("evaluation_sha256", report.EvaluationSha256); writer.WriteString("labels_sha256", report.LabelsSha256);
            var input = report.Evaluation;
            writer.WriteString("model_id", input.ModelId); writer.WriteString("evaluation_backend", input.EvaluationBackend);
            if (input.TrainingBackend is not null) writer.WriteString("training_backend", input.TrainingBackend);
            writer.WriteString("encoder_sha256", input.EncoderSha256); writer.WriteString("tokenizer_sha256", input.TokenizerSha256);
            writer.WriteString("data_manifest_sha256", input.DataManifestSha256); writer.WriteString("records_sha256", input.RecordsSha256);
            writer.WriteString("training_report_sha256", input.TrainingReportSha256); writer.WriteString("feature_sha256", input.FeatureSha256); writer.WriteString("head_sha256", input.HeadSha256);
            writer.WritePropertyName("acceptance"); writer.WriteStartObject();
            foreach (var flag in new[] { "language_quality", "slice_quality", "calibration", "sealed_test", "training_admission", "license_approval", "performance" }) writer.WriteBoolean(flag, false);
            writer.WriteEndObject();
            writer.WriteString("metrics_policy", "recomputed_from_validated_probabilities_full_source_denominator_unknown_preserved");
            writer.WritePropertyName("metrics"); metrics(writer, report.Rows);
            writer.WritePropertyName("vocabularies"); JsonSerializer.Serialize(writer, report.Labels.Vocabularies, SliceJsonContext.Default.SliceVocabularyArray);
            writer.WritePropertyName("dimensions"); writer.WriteStartArray();
            foreach (var dimension in Dimensions)
            {
                token.ThrowIfCancellationRequested();
                var vocabulary = report.Labels.Vocabularies.SingleOrDefault(value => value.Dimension == dimension);
                var buckets = new Dictionary<string, List<OriginalEvaluationRow>>(StringComparer.Ordinal);
                if (vocabulary is not null) foreach (var value in vocabulary.Values) buckets.Add(value, []);
                var unknown = new List<OriginalEvaluationRow>(); var missing = 0; var pending = 0; var rejected = 0; var approved = 0;
                foreach (var row in report.Rows)
                {
                    token.ThrowIfCancellationRequested();
                    var review = ReviewFor(report, row.RecordId, dimension);
                    if (review?.Status == "approved") { approved++; buckets[review.Value!].Add(row); }
                    else
                    {
                        unknown.Add(row);
                        if (review is null) missing++; else if (review.Status == "pending") pending++; else rejected++;
                    }
                }
                if (approved + missing + pending + rejected != report.Rows.Length || unknown.Count + approved != report.Rows.Length)
                    throw new InvalidDataException("Review slice denominator conservation failed.");
                writer.WriteStartObject(); writer.WriteString("dimension", dimension); writer.WriteString("vocabulary_version", vocabulary?.Version);
                writer.WriteNumber("source_total", report.Rows.Length); writer.WriteNumber("reviewed_labeled", approved); writer.WriteNumber("missing", missing);
                writer.WriteNumber("pending", pending); writer.WriteNumber("rejected", rejected); writer.WriteNumber("unknown", unknown.Count);
                writer.WritePropertyName("groups"); writer.WriteStartArray();
                foreach (var bucket in buckets) WriteGroup(writer, bucket.Key, bucket.Value, metrics);
                WriteGroup(writer, null, unknown, metrics); writer.WriteEndArray(); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("rows"); writer.WriteStartArray();
            foreach (var row in report.Rows)
            {
                token.ThrowIfCancellationRequested();
                writer.WriteStartObject(); writer.WriteString("record_id", row.RecordId); writer.WriteString("language", row.Language); writer.WriteString("question_type", row.QuestionType);
                writer.WriteNumber("target_index", row.TargetIndex); writer.WriteString("status", row.Status);
                writer.WritePropertyName("candidate_ids"); writer.WriteStartArray(); foreach (var id in row.CandidateIds) writer.WriteStringValue(id); writer.WriteEndArray();
                writer.WritePropertyName("reviews"); writer.WriteStartArray();
                foreach (var dimension in Dimensions)
                {
                    var review = ReviewFor(report, row.RecordId, dimension);
                    writer.WriteStartObject(); writer.WriteString("dimension", dimension); writer.WriteString("status", review?.Status ?? "missing"); writer.WriteString("value", review?.Value);
                    if (review is not null)
                    {
                        writer.WriteString("reviewer", review.Reviewer); writer.WriteString("reviewed_at", review.ReviewedAt); writer.WriteString("evidence_sha256", review.EvidenceSha256);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
        }
        token.ThrowIfCancellationRequested();
        if (stream.Length > OutputLimit) throw new InvalidDataException("Slice output exceeds 2 MiB.");
        return stream.ToArray();
    }

    private static SliceReview? ReviewFor(JoinedReport report, string id, string dimension) =>
        report.Reviews.TryGetValue(id, out var row) ? row.Reviews.SingleOrDefault(value => value.Dimension == dimension) : null;

    private static void WriteGroup(Utf8JsonWriter writer, string? label, IReadOnlyList<OriginalEvaluationRow> rows, Action<Utf8JsonWriter, IReadOnlyList<OriginalEvaluationRow>> metrics)
    {
        writer.WriteStartObject(); writer.WriteString("label", label); writer.WriteString("review_status", label is null ? "unknown" : "approved");
        writer.WritePropertyName("record_ids"); writer.WriteStartArray(); foreach (var row in rows) writer.WriteStringValue(row.RecordId); writer.WriteEndArray();
        writer.WritePropertyName("metrics"); metrics(writer, rows); writer.WriteEndObject();
    }

    internal enum PublicationStage { BeforeMove, AfterMove }

    internal static void Publish(string outputPath, byte[] bytes, CancellationToken token, Action<PublicationStage>? checkHook = null)
    {
        token.ThrowIfCancellationRequested();
        var output = Path.GetFullPath(outputPath); var parent = Path.GetDirectoryName(output)!;
        if (bytes.Length is < 1 or > OutputLimit || !Directory.Exists(parent) || File.Exists(output)) throw new IOException("Output size, directory or new-file condition failed.");
        var partial = output + ".reviewed-slices-" + Guid.NewGuid().ToString("N") + ".partial";
        var expected = Hash(bytes); var owned = false; var moved = false; var success = false; var written = 0;
        var ownedCreation = DateTime.MinValue;
        try
        {
            using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true; ownedCreation = File.GetCreationTimeUtc(partial);
                for (var offset = 0; offset < bytes.Length; offset += 65_536)
                {
                    token.ThrowIfCancellationRequested(); var count = Math.Min(65_536, bytes.Length - offset);
                    stream.Write(bytes.AsSpan(offset, count)); written += count;
                }
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested(); checkHook?.Invoke(PublicationStage.BeforeMove); token.ThrowIfCancellationRequested();
            File.Move(partial, output, overwrite: false); moved = true;
            checkHook?.Invoke(PublicationStage.AfterMove); token.ThrowIfCancellationRequested(); success = true;
        }
        finally
        {
            // Recovery has an independent five-second budget: cancellation of
            // the operation must not cancel its precise owned-file rollback.
            if (owned && !success)
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var target = moved ? output : partial;
                if (File.Exists(target))
                {
                    if (File.GetCreationTimeUtc(target) != ownedCreation) throw new IOException("Owned file creation identity changed; rollback refused.");
                    using (var lease = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var expectedLength = moved ? bytes.Length : written;
                        if (lease.Length != expectedLength) throw new IOException("Owned file length changed; rollback refused.");
                        var actual = new byte[expectedLength];
                        for (var offset = 0; offset < actual.Length; offset += 65_536)
                        {
                            recovery.Token.ThrowIfCancellationRequested(); lease.ReadExactly(actual.AsSpan(offset, Math.Min(65_536, actual.Length - offset)));
                        }
                        var expectedContent = moved ? expected : Hash(bytes.AsSpan(0, written).ToArray());
                        if (Hash(actual) != expectedContent) throw new IOException("Owned file bytes changed; rollback refused.");
                    }
                    recovery.Token.ThrowIfCancellationRequested(); File.Delete(target);
                }
            }
        }
    }

    internal static void ValidateJson(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length is < 1 or > InputLimit) throw new InvalidDataException("Each input must be 1 byte to 1 MiB.");
        _ = StrictUtf8.GetString(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        var scopes = new Stack<HashSet<string>>(); var count = 0;
        while (reader.Read())
        {
            if (++count > InputLimit) throw new InvalidDataException("JSON token bound exceeded.");
            if ((count & 255) == 0) token.ThrowIfCancellationRequested();
            if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) scopes.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !scopes.Peek().Add(reader.GetString()!)) throw new InvalidDataException("Duplicate JSON property.");
            else if (reader.TokenType == JsonTokenType.Number && (!reader.TryGetDouble(out var number) || !double.IsFinite(number))) throw new InvalidDataException("JSON numbers must be finite.");
        }
        token.ThrowIfCancellationRequested();
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool SamePath(string left, string right) => left.Equals(right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool EqualHash(string? left, string right) { RequireHash(left); return left!.Equals(right, StringComparison.OrdinalIgnoreCase); }
    private static bool Near(double? left, double right) => left is double value && double.IsFinite(value) && Math.Abs(value - right) <= 1e-9 * Math.Max(1, Math.Abs(right));
    private static void RequireHash(string? value)
    {
        if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character))) throw new InvalidDataException("Identity must be a SHA-256 hexadecimal string.");
    }
    private static void RequireText(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new InvalidDataException("Text identity must be bounded, nonempty and contain no control characters.");
    }

    internal sealed class ReadLease : IDisposable
    {
        private readonly FileStream stream;
        internal byte[] Bytes { get; }
        internal string Sha256 { get; }
        internal ReadLease(string path, string expectedHash, CancellationToken token, int limit = InputLimit)
        {
            RequireHash(expectedHash); token.ThrowIfCancellationRequested();
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (stream.Length is < 1 || stream.Length > limit) throw new InvalidDataException("Read lease file is outside its byte bound.");
                Bytes = new byte[(int)stream.Length];
                for (var offset = 0; offset < Bytes.Length; offset += 65_536)
                {
                    token.ThrowIfCancellationRequested(); stream.ReadExactly(Bytes.AsSpan(offset, Math.Min(65_536, Bytes.Length - offset)));
                }
                token.ThrowIfCancellationRequested(); Sha256 = Hash(Bytes);
                if (!Sha256.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pinned file SHA-256 differs.");
            }
            catch { stream.Dispose(); throw; }
        }
        public void Dispose() => stream.Dispose();
    }
}

internal sealed record JoinedReport(EvaluationInput Evaluation, LabelSidecar Labels, OriginalEvaluationRow[] Rows,
    Dictionary<string, SliceReviewRow> Reviews, string EvaluationSha256, string LabelsSha256);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record EvaluationInput
{
    public required int SchemaVersion { get; init; }
    public required string Status { get; init; }
    public required string EvaluationBackend { get; init; }
    public string? TrainingBackend { get; init; }
    public required string EvaluationUse { get; init; }
    public required string ModelId { get; init; }
    public required string EncoderSha256 { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required string DataManifestSha256 { get; init; }
    public required string RecordsSha256 { get; init; }
    public required string TrainingReportSha256 { get; init; }
    public required string FeatureSha256 { get; init; }
    public required string HeadSha256 { get; init; }
    public required string Split { get; init; }
    public required int Total { get; init; }
    public required int Answered { get; init; }
    public required int Rejected { get; init; }
    public required double Coverage { get; init; }
    public required JsonElement DiagnosticDimensions { get; init; }
    public required JsonElement Metrics { get; init; }
    public required JsonElement ByLanguageAndType { get; init; }
    public required EvaluationRow[] Rows { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record EvaluationRow
{
    public required string RecordId { get; init; }
    public required string Language { get; init; }
    public required string QuestionType { get; init; }
    public required string[] CandidateIds { get; init; }
    public required int TargetIndex { get; init; }
    public required int CandidateCount { get; init; }
    public required string Status { get; init; }
    public int? PredictedIndex { get; init; }
    public bool? Correct { get; init; }
    public required double? TargetProbability { get; init; }
    public required double? TopProbability { get; init; }
    public required double? Margin { get; init; }
    public required double? Nll { get; init; }
    public required double? Brier { get; init; }
    public double[]? Probabilities { get; init; }
    public string? ErrorCode { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LabelSidecar
{
    public required int SchemaVersion { get; init; }
    public required string Contract { get; init; }
    public required string ReviewOrigin { get; init; }
    public required string EvaluationSha256 { get; init; }
    public required string DataManifestSha256 { get; init; }
    public required string RecordsSha256 { get; init; }
    public required string Split { get; init; }
    public required SliceVocabulary[] Vocabularies { get; init; }
    public required SliceReviewRow[] Rows { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SliceVocabulary
{
    public required string Dimension { get; init; }
    public required string Version { get; init; }
    public required string[] Values { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SliceReviewRow
{
    public required string RecordId { get; init; }
    public required string[] CandidateIds { get; init; }
    public required SliceReview[] Reviews { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SliceReview
{
    public required string Dimension { get; init; }
    public required string Status { get; init; }
    public string? Value { get; init; }
    public string? Reviewer { get; init; }
    public string? ReviewedAt { get; init; }
    public string? Evidence { get; init; }
    public string? EvidenceSha256 { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(EvaluationInput))]
[JsonSerializable(typeof(LabelSidecar))]
[JsonSerializable(typeof(SliceVocabulary[]))]
internal partial class SliceJsonContext : JsonSerializerContext;
