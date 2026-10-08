using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Sezika.IndependentModelTool;

internal static class ReviewedSliceChecks
{
    private static readonly string Identity = new('a', 64);
    internal static void Run(string[] args, Action<Utf8JsonWriter, IReadOnlyList<OriginalEvaluationRow>> metrics, CancellationToken token)
    {
        var options = ReviewedSliceDiagnostics.ParseOptions(args, ["--max-checks", "--artifact-directory"]);
        if (!int.TryParse(options["--max-checks"], out var maximum) || maximum is < 1 or > 12) throw new ArgumentException("max-checks must be 1-12.");
        var artifact = Path.GetFullPath(options["--artifact-directory"]);
        if (!Directory.Exists(artifact)) throw new IOException("An existing explicit artifact directory is required.");
        var directory = Path.Combine(artifact, "synthetic-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var owned = new Dictionary<string, string>(StringComparer.Ordinal);
        var watch = Stopwatch.StartNew();
        var cases = new (string Name, Action Run)[]
        {
            ("reviewed-group-conservation-and-recomputed-metrics", Baseline),
            ("missing-pending-rejected-remain-unknown", Unknown),
            ("all-rejected-null-metrics-and-negative-denominator", Rejected),
            ("heterogeneous-choice-auroc-unavailable", Choice),
            ("duplicate-properties-record-ids-and-unknown-ids", Duplicates),
            ("pinned-bytes-and-sidecar-data-identity", IdentityChecks),
            ("probability-answer-rejection-and-candidate-validation", Numerics),
            ("external-review-vocabulary-time-and-evidence-validation", Reviews),
            ("row-byte-depth-and-vocabulary-bounds", Bounds),
            ("cancellation-before-publication-cleans-owned-partial", CancelBefore),
            ("cancellation-after-actual-move-rolls-back", CancelAfter),
            ("new-file-conflict-and-changed-output-preservation", Conflict),
        };
        try
        {
            for (var index = 0; index < maximum; index++)
            {
                token.ThrowIfCancellationRequested();
                if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new OperationCanceledException("Synthetic check deadline exceeded.");
                Console.WriteLine($"check-reviewed-slices: {index + 1}/{maximum} {cases[index].Name}");
                cases[index].Run();
                Console.WriteLine($"PASS: {cases[index].Name}");
            }
            Console.WriteLine($"PASS: {maximum} bounded synthetic checks; no model inference, real labels, reviewer verification or quality acceptance");
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            foreach (var entry in owned)
            {
                cleanup.Token.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(entry.Key);
                if (!Path.GetDirectoryName(path)!.Equals(directory, StringComparison.Ordinal) || !File.Exists(path)) continue;
                using (var lease = new ReviewedSliceDiagnostics.ReadLease(path, entry.Value, cleanup.Token, ReviewedSliceDiagnostics.OutputLimit)) { }
                File.Delete(path);
            }
            if (Directory.EnumerateFileSystemEntries(directory).Take(17).Any()) throw new IOException("Synthetic task directory contains an unowned or leaked file; preserved for review.");
            Directory.Delete(directory, recursive: false);
        }

        JoinedReport Join(EvaluationInput? evaluation = null, LabelSidecar? labels = null)
        {
            var input = evaluation ?? Fixture();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(input, SliceJsonContext.Default.EvaluationInput);
            var sidecar = labels ?? Sidecar();
            sidecar = sidecar with { EvaluationSha256 = ReviewedSliceDiagnostics.Hash(bytes) };
            var sidecarBytes = JsonSerializer.SerializeToUtf8Bytes(sidecar, SliceJsonContext.Default.LabelSidecar);
            return ReviewedSliceDiagnostics.Parse(bytes, ReviewedSliceDiagnostics.Hash(bytes), sidecarBytes, ReviewedSliceDiagnostics.Hash(sidecarBytes), token);
        }
        JsonDocument Output(JoinedReport joined) => JsonDocument.Parse(ReviewedSliceDiagnostics.Render(joined, metrics, token));
        void Baseline()
        {
            using var output = Output(Join());
            var root = output.RootElement;
            Assert(root.GetProperty("metrics").GetProperty("accuracy_on_all").GetDouble() == 0.5, "Aggregate input metrics must not be trusted.");
            var role = root.GetProperty("dimensions")[0];
            Assert(role.GetProperty("source_total").GetInt32() == 2 && role.GetProperty("reviewed_labeled").GetInt32() == 1 && role.GetProperty("unknown").GetInt32() == 1, "Group denominator conservation.");
            Assert(role.GetProperty("groups")[0].GetProperty("record_ids")[0].GetString() == "synthetic-negative", "Reviewed identity mapping.");
            Assert(root.GetProperty("acceptance").EnumerateObject().All(property => property.Value.ValueKind == JsonValueKind.False), "Every formal acceptance stays false.");
            Assert(root.GetProperty("review_origin").GetString() == "external_declared_human_review_not_identity_verified", "External reviewer declaration boundary.");
            var evaluationPath = Path.Combine(directory, "synthetic-evaluation.json");
            var labelsPath = Path.Combine(directory, "synthetic-labels.json");
            var reportPath = Path.Combine(directory, "synthetic-report.json");
            var evaluationBytes = JsonSerializer.SerializeToUtf8Bytes(Fixture(), SliceJsonContext.Default.EvaluationInput);
            var labelsBytes = JsonSerializer.SerializeToUtf8Bytes(Sidecar() with { EvaluationSha256 = ReviewedSliceDiagnostics.Hash(evaluationBytes) }, SliceJsonContext.Default.LabelSidecar);
            File.WriteAllBytes(evaluationPath, evaluationBytes); owned.Add(evaluationPath, ReviewedSliceDiagnostics.Hash(evaluationBytes));
            File.WriteAllBytes(labelsPath, labelsBytes); owned.Add(labelsPath, ReviewedSliceDiagnostics.Hash(labelsBytes));
            using (var lease = new ReviewedSliceDiagnostics.ReadLease(evaluationPath, ReviewedSliceDiagnostics.Hash(evaluationBytes), token))
            {
                if (OperatingSystem.IsWindows()) Rejects(() => { using var attempt = new FileStream(evaluationPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
            }
            ReviewedSliceDiagnostics.Run(["group-development-report", "--evaluation", evaluationPath, "--evaluation-sha256", ReviewedSliceDiagnostics.Hash(evaluationBytes),
                "--labels", labelsPath, "--labels-sha256", ReviewedSliceDiagnostics.Hash(labelsBytes), "--output", reportPath], metrics, token);
            var rendered = ReviewedSliceDiagnostics.Render(Join(), metrics, token); owned.Add(reportPath, ReviewedSliceDiagnostics.Hash(rendered));
            Assert(File.ReadAllBytes(reportPath).SequenceEqual(rendered), "Real command dispatch, input leases and successful atomic publication agree with checked metrics.");
        }
        void Unknown()
        {
            var labels = Sidecar() with { Rows = [Sidecar().Rows[0] with { Reviews = [
                new SliceReview { Dimension = "role", Status = "pending" },
                Completed("negation", "rejected", null),
            ] }] };
            labels = labels with { Vocabularies = [labels.Vocabularies[0], Vocabulary("negation", "no")] };
            using var output = Output(Join(labels: labels));
            var dimensions = output.RootElement.GetProperty("dimensions");
            Assert(dimensions[0].GetProperty("pending").GetInt32() == 1 && dimensions[0].GetProperty("missing").GetInt32() == 1 && dimensions[0].GetProperty("unknown").GetInt32() == 2, "Pending and missing stay unknown.");
            Assert(dimensions[1].GetProperty("rejected").GetInt32() == 1 && dimensions[1].GetProperty("unknown").GetInt32() == 2, "Rejected review stays unknown.");
            Assert(dimensions[2].GetProperty("missing").GetInt32() == 2, "Unprovided dimensions stay unknown.");
            Assert(dimensions[0].GetProperty("groups")[1].GetProperty("metrics").GetProperty("boolean_negative_recall_on_all").GetDouble() == 0.5, "Unknown retains rejected negative denominator.");
        }
        void Rejected()
        {
            var fixture = Fixture(); fixture = fixture with { Rows = fixture.Rows.Select(row => Reject(row)).ToArray(), Answered = 0, Rejected = 2, Coverage = 0 };
            using var output = Output(Join(fixture)); var value = output.RootElement.GetProperty("metrics");
            Assert(value.GetProperty("accuracy_on_answered").ValueKind == JsonValueKind.Null && value.GetProperty("mean_nll").ValueKind == JsonValueKind.Null && value.GetProperty("auroc_macro_ovr").ValueKind == JsonValueKind.Null, "All rejected metrics null.");
            Assert(value.GetProperty("boolean_negative_total").GetInt32() == 2 && value.GetProperty("boolean_negative_recall_on_all").GetDouble() == 0, "All rejected negative denominator retained.");
        }
        void Choice()
        {
            var fixture = Fixture(); fixture = fixture with { Rows = [Answer("synthetic-negative", "choice", ["a", "b"], [0.8, 0.2]), Answer("synthetic-rejected", "choice", ["b", "a"], [0.2, 0.8])], Answered = 2, Rejected = 0, Coverage = 1 };
            var labels = Sidecar() with { Rows = [Sidecar().Rows[0] with { CandidateIds = ["a", "b"] }] };
            using var output = Output(Join(fixture, labels));
            Assert(output.RootElement.GetProperty("metrics").GetProperty("auroc_macro_ovr").ValueKind == JsonValueKind.Null, "Choice candidate position is not a common semantic class.");
        }
        void Duplicates()
        {
            var fixture = Fixture(); var labels = Sidecar();
            Rejects(() => Join(fixture with { Rows = [fixture.Rows[0], fixture.Rows[0]] }));
            Rejects(() => Join(labels: labels with { Rows = [labels.Rows[0], labels.Rows[0]] }));
            Rejects(() => Join(labels: labels with { Rows = [labels.Rows[0] with { RecordId = "unknown-synthetic-id" }] }));
            Rejects(() => ReviewedSliceDiagnostics.ValidateJson(Encoding.UTF8.GetBytes("{\"nested\":{\"id\":1,\"id\":2}}"), token));
            Rejects(() => ReviewedSliceDiagnostics.ValidateJson([0xff, 0xfe], token));
        }
        void IdentityChecks()
        {
            var fixture = Fixture(); var bytes = JsonSerializer.SerializeToUtf8Bytes(fixture, SliceJsonContext.Default.EvaluationInput);
            var labels = Sidecar(); var labelBytes = JsonSerializer.SerializeToUtf8Bytes(labels, SliceJsonContext.Default.LabelSidecar);
            Rejects(() => ReviewedSliceDiagnostics.Parse(bytes, new string('b', 64), labelBytes, ReviewedSliceDiagnostics.Hash(labelBytes), token));
            Rejects(() => ReviewedSliceDiagnostics.Parse(bytes, ReviewedSliceDiagnostics.Hash(bytes), labelBytes, new string('b', 64), token));
            Rejects(() => ReviewedSliceDiagnostics.Parse(bytes, ReviewedSliceDiagnostics.Hash(bytes), labelBytes, ReviewedSliceDiagnostics.Hash(labelBytes), token));
            Rejects(() => Join(labels: labels with { DataManifestSha256 = new string('b', 64) }));
            Rejects(() => Join(labels: labels with { RecordsSha256 = new string('b', 64) }));
            Rejects(() => Join(labels: labels with { Split = "sealed_test" }));
        }
        void Numerics()
        {
            var fixture = Fixture(); var row = fixture.Rows[0];
            Rejects(() => Join(fixture with { Rows = [row with { Probabilities = [0.8, 0.8] }, fixture.Rows[1]] }));
            Rejects(() => Join(fixture with { Rows = [row with { Correct = false }, fixture.Rows[1]] }));
            Rejects(() => Join(fixture with { Rows = [row with { CandidateIds = ["true", "false"] }, fixture.Rows[1]] }));
            Rejects(() => Join(fixture with { Rows = [row with { Nll = 4 }, fixture.Rows[1]] }));
            Rejects(() => Join(fixture with { Rows = [row with { TargetIndex = 2 }, fixture.Rows[1]] }));
            Rejects(() => Join(fixture with { Rows = [row, fixture.Rows[1] with { PredictedIndex = 0 }] }));
            Rejects(() => Join(fixture with { Total = 1 }));
            Rejects(() => ReviewedSliceDiagnostics.ValidateJson(Encoding.UTF8.GetBytes("{\"number\":1e999}"), token));
        }
        void Reviews()
        {
            var labels = Sidecar(); var row = labels.Rows[0]; var review = row.Reviews[0];
            void Bad(SliceReview value) => Rejects(() => Join(labels: labels with { Rows = [row with { Reviews = [value] }] }));
            Bad(review with { Value = "unregistered" }); Bad(review with { Reviewer = null }); Bad(review with { ReviewedAt = "2026-10-08T00:00:00+08:00" });
            Bad(review with { EvidenceSha256 = new string('b', 64) }); Bad(review with { Status = "pending" });
            Rejects(() => Join(labels: labels with { Rows = [row with { CandidateIds = ["true", "false"] }] }));
            Rejects(() => Join(labels: labels with { Vocabularies = [Vocabulary("role", "unknown")] }));
            Rejects(() => Join(labels: labels with { Rows = [row with { Reviews = [review, review] }] }));
        }
        void Bounds()
        {
            var fixture = Fixture();
            Rejects(() => Join(fixture with { Rows = Enumerable.Range(0, 33).Select(index => fixture.Rows[0] with { RecordId = "synthetic-" + index }).ToArray() }));
            Rejects(() => ReviewedSliceDiagnostics.ValidateJson(new byte[ReviewedSliceDiagnostics.InputLimit + 1], token));
            Rejects(() => ReviewedSliceDiagnostics.ValidateJson(Encoding.UTF8.GetBytes(new string('[', 33) + "0" + new string(']', 33)), token));
            Rejects(() => Join(labels: Sidecar() with { Vocabularies = [Vocabulary("role", Enumerable.Range(0, 17).Select(index => "v" + index).ToArray())] }));
            Rejects(() => Join(labels: Sidecar() with { Vocabularies = Enumerable.Range(0, 5).Select(_ => Vocabulary("role", "v")).ToArray() }));
        }
        void CancelBefore()
        {
            var output = Path.Combine(directory, "cancel-before.json"); var bytes = Encoding.UTF8.GetBytes("synthetic publication bytes");
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Cancels(() => ReviewedSliceDiagnostics.Publish(output, bytes, cancellation.Token, stage => { if (stage == ReviewedSliceDiagnostics.PublicationStage.BeforeMove) cancellation.Cancel(); }));
            Assert(!File.Exists(output) && !Directory.EnumerateFiles(directory).Take(17).Any(path => Path.GetFileName(path).StartsWith("cancel-before", StringComparison.Ordinal)), "No output or partial after pre-move cancellation.");
        }
        void CancelAfter()
        {
            var output = Path.Combine(directory, "cancel-after.json"); var bytes = Encoding.UTF8.GetBytes("synthetic publication bytes"); var observedMove = false;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Cancels(() => ReviewedSliceDiagnostics.Publish(output, bytes, cancellation.Token, stage =>
            {
                if (stage != ReviewedSliceDiagnostics.PublicationStage.AfterMove) return;
                observedMove = File.Exists(output); cancellation.Cancel();
            }));
            Assert(observedMove && !File.Exists(output), "Actual move observed and owned final output rolled back.");
            Cancels(() => ReviewedSliceDiagnostics.Publish(output, bytes, cancellation.Token));
            Assert(!File.Exists(output), "Already-cancelled publication creates no output.");
        }
        void Conflict()
        {
            var output = Path.Combine(directory, "conflict.json"); var original = Encoding.UTF8.GetBytes("synthetic existing output");
            File.WriteAllBytes(output, original); owned.Add(output, ReviewedSliceDiagnostics.Hash(original));
            Rejects(() => ReviewedSliceDiagnostics.Publish(output, Encoding.UTF8.GetBytes("replacement"), token));
            Assert(File.ReadAllBytes(output).SequenceEqual(original), "Existing output preserved without overwrite.");
            var changed = Path.Combine(directory, "changed-after-move.json"); var foreign = Encoding.UTF8.GetBytes("synthetic externally changed output");
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Rejects(() => ReviewedSliceDiagnostics.Publish(changed, original, cancellation.Token, stage =>
            {
                if (stage != ReviewedSliceDiagnostics.PublicationStage.AfterMove) return;
                File.WriteAllBytes(changed, foreign); owned.Add(changed, ReviewedSliceDiagnostics.Hash(foreign)); cancellation.Cancel();
            }));
            Assert(File.ReadAllBytes(changed).SequenceEqual(foreign), "Hash-changed output rollback refused and file preserved.");
        }
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Rejects(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException or IOException or DecoderFallbackException) { return; }
        throw new InvalidOperationException("Invalid synthetic input/publication was not rejected.");
    }
    private static void Cancels(Action action)
    {
        try { action(); } catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Synthetic cancellation was not observed.");
    }
    private static EvaluationInput Fixture() => new()
    {
        SchemaVersion = 1, Status = "development_diagnostic_only", EvaluationBackend = "synthetic_fixture_no_inference",
        EvaluationUse = "independent_development_diagnostics_not_training_calibration_or_sealed_test", ModelId = "synthetic-fixture-only",
        EncoderSha256 = Identity, TokenizerSha256 = Identity, DataManifestSha256 = Identity, RecordsSha256 = Identity,
        TrainingReportSha256 = Identity, FeatureSha256 = Identity, HeadSha256 = Identity, Split = "development", Total = 2, Answered = 1, Rejected = 1, Coverage = 0.5,
        DiagnosticDimensions = Element("{}"), Metrics = Element("{\"accuracy_on_all\":0.999}"), ByLanguageAndType = Element("[]"),
        Rows = [Answer("synthetic-negative", "boolean", ["false", "true"], [0.8, 0.2]), Reject(Answer("synthetic-rejected", "boolean", ["false", "true"], [0.7, 0.3]))],
    };
    private static EvaluationRow Answer(string id, string type, string[] candidates, double[] probabilities)
    {
        var predicted = Array.IndexOf(probabilities, probabilities.Max()); var top = probabilities[predicted];
        return new EvaluationRow
        {
            RecordId = id, Language = "en", QuestionType = type, CandidateIds = candidates, CandidateCount = candidates.Length,
            TargetIndex = 0, Status = "answered", PredictedIndex = predicted, Correct = predicted == 0, TargetProbability = probabilities[0], TopProbability = top,
            Margin = top - probabilities.Where((_, index) => index != predicted).Max(), Nll = -Math.Log(probabilities[0]),
            Brier = probabilities.Select((value, index) => Math.Pow(value - (index == 0 ? 1 : 0), 2)).Sum(), Probabilities = probabilities,
        };
    }
    private static EvaluationRow Reject(EvaluationRow row) => row with
    {
        CandidateCount = 0, Status = "rejected", PredictedIndex = null, Correct = null, TargetProbability = null, TopProbability = null, Margin = null,
        Nll = null, Brier = null, Probabilities = null, ErrorCode = "synthetic_rejection_no_inference",
    };
    private static LabelSidecar Sidecar() => new()
    {
        SchemaVersion = 1, Contract = ReviewedSliceDiagnostics.Contract, ReviewOrigin = "external_declared_human_review", EvaluationSha256 = Identity,
        DataManifestSha256 = Identity, RecordsSha256 = Identity, Split = "development", Vocabularies = [Vocabulary("role", "synthetic-role")],
        Rows = [new SliceReviewRow { RecordId = "synthetic-negative", CandidateIds = ["false", "true"], Reviews = [Completed("role", "approved", "synthetic-role")] }],
    };
    private static SliceVocabulary Vocabulary(string dimension, params string[] values) => new() { Dimension = dimension, Version = "synthetic-fixture-v1", Values = values };
    private static SliceReview Completed(string dimension, string status, string? value) => new()
    {
        Dimension = dimension, Status = status, Value = value, Reviewer = "__synthetic_fixture_not_a_real_reviewer__",
        ReviewedAt = "2026-10-08T00:00:00Z", Evidence = "synthetic evidence; no real human review or model output",
        EvidenceSha256 = ReviewedSliceDiagnostics.Hash(Encoding.UTF8.GetBytes("synthetic evidence; no real human review or model output")),
    };
    private static JsonElement Element(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
}
