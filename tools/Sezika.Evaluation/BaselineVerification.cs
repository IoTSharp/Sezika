using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

// This verifies archived evidence. It never loads a model, scores raw captures, or calibrates probabilities.
internal static class BaselineVerification
{
    private const string Model = "convaiinnovations/laya-multilingual";
    private const string Revision = "052592a15d198d9ad47da779604259b10b47b7aa";
    private const string Weights = "9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204";
    private const string Tokenizer = "609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f";
    private const string Source = "4066d5d5fbf08b66c6757ddeedbd797bd7655bc0";
    private const string Contract = "771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee";
    private const string Rendering = "sezika.prompt.laya-4066d5d5.v2";
    private const string Temperature = "checkpoint_fixed_1_no_overrides";
    private const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxTotalBytes = 256 * 1024 * 1024;

    public static int Run(string[] args)
    {
        if (args.Length != 4 || args[0] != "--verify-baseline" ||
            !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds is < 1 or > 300)
        {
            Console.Error.WriteLine("Usage: Sezika.Evaluation --verify-baseline <plan.json> <new-report.json> <seconds:1..300>");
            return 2;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; deadline.Cancel(); };
        Console.CancelKeyPress += cancel;
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        string? createdOutput = null;
        var completed = false;
        try
        {
            using var snapshots = new Snapshots(deadline.Token);
            var planPath = Path.GetFullPath(args[1]);
            var planFile = snapshots.Read(planPath, null);
            var plan = JsonSerializer.Deserialize(planFile.Document.RootElement, BaselineJsonContext.Default.AuditBaselinePlan)
                ?? throw new InvalidDataException("Missing baseline plan.");
            Require(plan.SchemaVersion == "sezika.audit-baseline-plan.v1", "Unsupported baseline plan schema.");
            Require(plan.Datasets is { Length: >= 1 and <= 8 }, "Plan must contain 1..8 datasets.");
            var planDirectory = Path.GetDirectoryName(planPath)!;
            var archivePath = Resolve(planDirectory, plan.ArchiveIndex.Path);
            var archiveFile = snapshots.Read(archivePath, plan.ArchiveIndex.Sha256);
            var archive = Archive(archiveFile.Document.RootElement, Path.GetDirectoryName(archivePath)!, deadline.Token);
            var datasetNames = new HashSet<string>(StringComparer.Ordinal);
            var datasetHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var comparisonFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var globalIds = new HashSet<string>(StringComparer.Ordinal);
            var reports = new List<AuditDatasetReport>(plan.Datasets.Length);
            string? actualEnvironment = null;
            string? referenceEnvironment = null;
            for (var index = 0; index < plan.Datasets.Length; index++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var dataset = plan.Datasets[index];
                Require(dataset is not null && ValidName(dataset.Name) && datasetNames.Add(dataset.Name), "Invalid or repeated dataset name.");
                Require(dataset.EvidenceKind is "external_audit" or "original_fixture", "Unsupported dataset evidence kind.");
                Require(IsHash(dataset.DatasetSha256) && datasetHashes.Add(dataset.DatasetSha256), "Invalid or repeated dataset hash.");
                Require(dataset.DatasetTotal is >= 1 and <= 10000, "Dataset total must be 1..10000.");
                Require(dataset.Comparisons is { Length: >= 1 and <= 256 }, "A dataset requires 1..256 comparisons.");
                Console.WriteLine($"Verifying archived baseline {index + 1}/{plan.Datasets.Length}: {dataset.Name} ({dataset.DatasetTotal} cases).");
                var referenceFile = ReadEvidence(dataset.Reference, planDirectory, snapshots, archive, "aggregate_without_external_rows_and_labels");
                var actualFile = ReadEvidence(dataset.Actual, planDirectory, snapshots, archive, "aggregate_without_external_rows_and_labels");
                Require(!referenceFile.Path.Equals(actualFile.Path, StringComparison.OrdinalIgnoreCase), "Reference and actual evidence must differ.");
                var reference = Quality(referenceFile.Document.RootElement, dataset, "pytorch_cpu_fp32", deadline.Token);
                var actual = Quality(actualFile.Document.RootElement, dataset, "cuda_fp32", deadline.Token);
                Require(reference.Batches.Length == dataset.Comparisons.Length && actual.Batches.Length == dataset.Comparisons.Length,
                    "Quality batch counts do not equal the comparison count.");
                EqualCounts(reference.Overall, actual.Overall, "reference/actual overall");
                EqualGroups(reference.ByType, actual.ByType, "type");
                EqualGroups(reference.ByLanguage, actual.ByLanguage, "language");
                EqualGroups(reference.ByLanguageAndType, actual.ByLanguageAndType, "language/type");
                var comparisons = new List<AuditBatchReport>(dataset.Comparisons.Length);
                var processed = 0;
                var localIds = new HashSet<string>(StringComparer.Ordinal);
                for (var batchIndex = 0; batchIndex < dataset.Comparisons.Length; batchIndex++)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    var comparisonFile = ReadEvidence(dataset.Comparisons[batchIndex], planDirectory, snapshots, archive, "byte_preserving_copy");
                    Require(comparisonFiles.Add(comparisonFile.Path), "Comparison file is repeated in the plan.");
                    var referenceBatch = reference.Batches[batchIndex];
                    var actualBatch = actual.Batches[batchIndex];
                    var comparison = Comparison(comparisonFile.Document.RootElement, referenceBatch, actualBatch, deadline.Token);
                    foreach (var id in comparison.SelectedCaseIds)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        Require(localIds.Add(id) && globalIds.Add(id), "Case ID is repeated across batches or datasets.");
                    }
                    var actualIdentity = ActualEnvironment(actualBatch.GetProperty("Implementation"));
                    var referenceIdentity = ReferenceEnvironment(referenceBatch.GetProperty("Provenance"));
                    actualEnvironment ??= actualIdentity;
                    referenceEnvironment ??= referenceIdentity;
                    Require(actualEnvironment == actualIdentity, "Actual capture build/environment differs across batches or datasets.");
                    Require(referenceEnvironment == referenceIdentity, "Pinned reference environment differs across batches or datasets.");
                    processed = checked(processed + comparison.SelectedCaseIds.Length);
                    comparisons.Add(comparison with { Comparison = Evidence(comparisonFile, archive) });
                }
                Require(processed == dataset.DatasetTotal && localIds.Count == dataset.DatasetTotal, "Full ordered comparison coverage is missing.");
                reports.Add(new AuditDatasetReport(dataset.Name, dataset.EvidenceKind, dataset.DatasetSha256.ToLowerInvariant(), dataset.DatasetTotal,
                    Evidence(referenceFile, archive), Evidence(actualFile, archive), reference.Overall, actual.Overall,
                    Delta(reference.Overall, actual.Overall), PairedGroups(reference.ByType, actual.ByType),
                    PairedGroups(reference.ByLanguage, actual.ByLanguage), PairedGroups(reference.ByLanguageAndType, actual.ByLanguageAndType),
                    comparisons.ToArray(), BooleanWeakness(actual.Overall.Boolean),
                    dataset.EvidenceKind == "external_audit" ? "Archived language metadata only; unspecified remains unspecified." :
                    "Original fixture labels only; these cases do not establish representative language quality."));
                Console.WriteLine($"Verified {dataset.Name}: {processed}/{dataset.DatasetTotal}; actual accuracy {actual.Overall.Correct}/{actual.Overall.Answered}.");
            }
            snapshots.Revalidate();
            deadline.Token.ThrowIfCancellationRequested();
            var report = new AuditBaselineReport(started, watch.Elapsed.TotalMilliseconds, planFile.Sha256, archiveFile.Sha256,
                Model, Revision, Weights, Tokenizer, Source, Contract, Rendering, Temperature,
                actualEnvironment!, referenceEnvironment!, reports.ToArray());
            var bytes = JsonSerializer.SerializeToUtf8Bytes(report, BaselineJsonContext.Default.AuditBaselineReport);
            deadline.Token.ThrowIfCancellationRequested();
            var outputPath = Path.GetFullPath(args[2]);
            Require(!snapshots.Contains(outputPath), "Output cannot overwrite a baseline input.");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                createdOutput = outputPath;
                output.Write(bytes);
                output.Flush(true);
                deadline.Token.ThrowIfCancellationRequested();
            }
            completed = true;
            Console.WriteLine($"Wrote verified archived audit baseline: {outputPath}");
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"baseline verification: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            // Only this invocation's CreateNew output is eligible for removal; inputs and prior reports are untouched.
            if (!completed && createdOutput is not null) File.Delete(createdOutput);
        }
    }

    private static Dictionary<string, ArchiveEntry> Archive(JsonElement root, string directory, CancellationToken token)
    {
        Require(Text(root, "schema_version") == "sezika.continuation-evidence-index.v1", "Unexpected archive index schema.");
        var artifacts = Array(root, "artifacts", 1, 2048);
        Require(Number(root, "files") == artifacts.Length && Number(root, "available") == artifacts.Length, "Archive index is incomplete.");
        var result = new Dictionary<string, ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in artifacts)
        {
            token.ThrowIfCancellationRequested();
            var path = Resolve(directory, Text(item, "artifact"));
            var relative = Path.GetRelativePath(directory, path);
            Require(!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative), "Archive artifact escapes its directory.");
            var hash = Text(item, "artifact_sha256");
            var sourceHash = Text(item, "source_sha256");
            Require(IsHash(hash) && IsHash(sourceHash), "Invalid archive artifact/source hash.");
            Require(result.TryAdd(path, new ArchiveEntry(hash, sourceHash, Text(item, "transformation"))), "Duplicate archive artifact path.");
        }
        return result;
    }

    private static Snapshot ReadEvidence(AuditFile file, string directory, Snapshots snapshots,
        Dictionary<string, ArchiveEntry> archive, string transformation)
    {
        Require(file is not null, "Missing evidence file.");
        var path = Resolve(directory, file.Path);
        Require(archive.TryGetValue(path, out var entry), "Evidence file is absent from the pinned archive index.");
        Require(entry!.Transformation == transformation, "Evidence has the wrong archive transformation.");
        Require(HashEqual(entry.Hash, file.Sha256), "Plan hash differs from archived artifact hash.");
        if (transformation == "byte_preserving_copy") Require(HashEqual(entry.Hash, entry.SourceHash), "Comparison archive is not byte preserving.");
        return snapshots.Read(path, file.Sha256);
    }

    private static AuditEvidenceFile Evidence(Snapshot file, Dictionary<string, ArchiveEntry> archive) =>
        new(file.Path, file.Sha256, archive[file.Path].SourceHash.ToLowerInvariant(), archive[file.Path].Transformation);

    private static QualityView Quality(JsonElement root, AuditDatasetPlan dataset, string backend, CancellationToken token)
    {
        Require(Number(root, "SchemaVersion") == 2 && Text(root, "RenderingVersion") == Rendering &&
            Text(root, "LengthPolicy") == "laya_compatible", "Unexpected quality schema/rendering/length policy.");
        Require(Text(root, "Model") == Model && Text(root, "ModelRevision") == Revision && Text(root, "TokenizerRevision") == Revision,
            "Quality model/tokenizer revision is not the fixed baseline.");
        Require(HashEqual(Text(root, "WeightsSha256"), Weights) && HashEqual(Text(root, "TokenizerSha256"), Tokenizer), "Quality model asset identity differs.");
        Require(HashEqual(Text(root, "DatasetSha256"), dataset.DatasetSha256) && Number(root, "DatasetTotal") == dataset.DatasetTotal,
            "Quality dataset identity differs from the plan.");
        Require(Text(root, "Backend") == backend && Text(root, "CaptureStatus") == "complete_dataset" &&
            Number(root, "CaptureSelectedCases") == dataset.DatasetTotal && Number(root, "Unprocessed") == 0 && Flag(root, "FullDatasetProcessed"),
            "Quality does not describe a complete dataset capture.");
        foreach (var field in new[] { "CaptureSha256", "CaptureManifestCases", "CaptureProvenance", "CaptureImplementation", "CaptureCasesSha256", "CaptureContractSha256" })
            Require(root.GetProperty(field).ValueKind == JsonValueKind.Null, "Aggregate archive must identify captures through batches only.");
        Require(Text(root, "MeasurementOrigin") == "offline_scoring_of_existing_capture_batches_not_new_inference_or_parity_proof" &&
            Text(root, "EvaluationUse") == "audit_only_not_training_or_calibration", "Quality measurement origin/use is unsupported.");
        Require(!root.TryGetProperty("Rows", out _) && !root.TryGetProperty("ByTarget", out _), "This command accepts derived aggregate archives only.");
        var overall = Metrics(root, "Processed", dataset.DatasetTotal);
        Require(overall.Total == dataset.DatasetTotal, "Quality has unprocessed dataset rows.");
        Require(overall.Answered == dataset.DatasetTotal && Number(root, "Abstained") == 0,
            "Quality answer counts differ from the fully answered comparison baseline.");
        Ratio(root, "DatasetAnswerCoverage", overall.Answered, dataset.DatasetTotal);
        var failures = root.GetProperty("ByFailure");
        Require(failures.ValueKind == JsonValueKind.Object && failures.EnumerateObject().Count() is >= 1 and <= 128, "Invalid failure partition.");
        var failureTotal = 0;
        var answeredFailures = 0;
        var abstainedFailures = 0;
        foreach (var failure in failures.EnumerateObject())
        {
            token.ThrowIfCancellationRequested();
            var count = failure.Value.GetInt32();
            Require(count >= 0 && count <= dataset.DatasetTotal && ValidName(failure.Name), "Invalid failure count/name.");
            failureTotal = checked(failureTotal + count);
            if (failure.Name == "answered") answeredFailures = count;
            if (failure.Name == "abstained") abstainedFailures += count;
        }
        Require(failureTotal == overall.Total && answeredFailures == overall.Answered && abstainedFailures == Number(root, "Abstained"),
            "Failure/answer/abstention partition differs from total counts.");
        var eligible = Number(root, "EligibleCounterfactualPairs");
        var answeredPairs = Number(root, "AnsweredCounterfactualPairs");
        Require(eligible >= 0 && eligible <= overall.Total / 2 && answeredPairs >= 0 && answeredPairs <= eligible &&
            Number(root, "PredictionFlips") >= 0 && Number(root, "PredictionFlips") <= answeredPairs &&
            Number(root, "BothCorrectPairs") >= 0 && Number(root, "BothCorrectPairs") <= answeredPairs, "Invalid counterfactual counts.");
        var byType = Groups(root, "ByType", dataset.DatasetTotal, token);
        Require(byType.All(group => group.Name is "choice" or "noul" or "score"), "Unknown quality primitive.");
        var byLanguage = Groups(root, "ByLanguage", dataset.DatasetTotal, token);
        var cells = Array(root, "ByLanguageAndType", 1, 384);
        var byLanguageAndType = new List<AuditMetricGroup>(cells.Length);
        var cellNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in cells)
        {
            token.ThrowIfCancellationRequested();
            var language = Text(cell, "Language");
            var type = Text(cell, "Type");
            Require(ValidName(language) && type is "choice" or "noul" or "score", "Invalid language/type cell.");
            var name = language + "/" + type;
            Require(cellNames.Add(name) && Text(cell.GetProperty("Metrics"), "Name") == type, "Duplicate or mislabeled language/type cell.");
            byLanguageAndType.Add(new(name, Metrics(cell.GetProperty("Metrics"), "Total", dataset.DatasetTotal)));
        }
        Partition(overall, byType.Select(group => group.Metrics), "type");
        Partition(overall, byLanguage.Select(group => group.Metrics), "language");
        Partition(overall, byLanguageAndType.Select(group => group.Metrics), "language/type");
        foreach (var group in byType)
            Partition(group.Metrics, byLanguageAndType.Where(cell => cell.Name.EndsWith("/" + group.Name, StringComparison.Ordinal)).Select(cell => cell.Metrics), "type cells");
        foreach (var group in byLanguage)
            Partition(group.Metrics, byLanguageAndType.Where(cell => cell.Name.StartsWith(group.Name + "/", StringComparison.Ordinal)).Select(cell => cell.Metrics), "language cells");
        Require(byLanguageAndType.All(cell => byLanguage.Any(group => cell.Name.StartsWith(group.Name + "/", StringComparison.Ordinal)) &&
            byType.Any(group => cell.Name.EndsWith("/" + group.Name, StringComparison.Ordinal))), "Language/type cell has no matching marginal.");
        foreach (var group in byType)
            ScoreMetric(group.Metrics, group.Name == "score" ? group.Metrics : null, "type ScoreMae");
        foreach (var cell in byLanguageAndType)
            ScoreMetric(cell.Metrics, cell.Name.EndsWith("/score", StringComparison.Ordinal) ? cell.Metrics : null, "language/type ScoreMae");
        ScoreMetric(overall, byType.SingleOrDefault(group => group.Name == "score")?.Metrics, "overall ScoreMae");
        foreach (var language in byLanguage)
            ScoreMetric(language.Metrics, byLanguageAndType.SingleOrDefault(cell => cell.Name == language.Name + "/score")?.Metrics, "language ScoreMae");
        var score = byType.SingleOrDefault(group => group.Name == "score");
        if (score is not null)
            WeightedMean(score.Metrics.ScoreMae, byLanguageAndType.Where(cell => cell.Name.EndsWith("/score", StringComparison.Ordinal))
                .Select(cell => cell.Metrics), metric => metric.ScoreMae, "score cell MAE");
        var batches = Array(root, "CaptureBatches", 1, 256);
        var batchRows = 0;
        var batchHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in batches)
        {
            token.ThrowIfCancellationRequested();
            var count = Number(batch, "Processed");
            Require(count is >= 1 and <= 10000, "Invalid capture batch count.");
            Require(IsHash(Text(batch, "CaptureSha256")) && batchHashes.Add(Text(batch, "CaptureSha256")), "Invalid or repeated capture hash.");
            Identity(batch.GetProperty("Provenance"), Text(batch, "ManifestSha256"));
            batchRows = checked(batchRows + count);
            if (backend == "pytorch_cpu_fp32")
            {
                Require(batch.GetProperty("Implementation").ValueKind == JsonValueKind.Null, "Unexpected reference capture implementation.");
                _ = ReferenceEnvironment(batch.GetProperty("Provenance"));
            }
            else _ = ActualEnvironment(batch.GetProperty("Implementation"));
        }
        Require(batchRows == overall.Total, "Capture batch counts differ from quality denominator.");
        return new(overall, byType, byLanguage, byLanguageAndType.ToArray(), batches);
    }

    private static AuditMetricGroup[] Groups(JsonElement root, string field, int max, CancellationToken token)
    {
        var items = Array(root, field, 1, 128);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var groups = new AuditMetricGroup[items.Length];
        for (var index = 0; index < items.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var name = Text(items[index], "Name");
            Require(ValidName(name) && !name.Contains('/') && names.Add(name), "Invalid or duplicated quality stratum.");
            groups[index] = new(name, Metrics(items[index], "Total", max));
            if (field == "ByType") Require(groups[index].Metrics.Boolean.Processed == (name == "noul" ? groups[index].Metrics.Total : 0),
                "Boolean denominator differs from its primitive stratum.");
        }
        return groups;
    }

    private static AuditMetrics Metrics(JsonElement root, string totalField, int max)
    {
        var total = Number(root, totalField);
        var answered = Number(root, "Answered");
        var correct = Number(root, "Correct");
        Require(total >= 0 && total <= max && answered >= 0 && answered <= total && correct >= 0 && correct <= answered, "Invalid quality counts.");
        int? abstainedCount = null;
        if (root.TryGetProperty("Abstained", out var abstained))
        {
            abstainedCount = abstained.GetInt32();
            Require(abstainedCount >= 0 && abstainedCount <= total - answered, "Invalid abstention count.");
        }
        Ratio(root, "Coverage", answered, total);
        Ratio(root, "AccuracyOnAnswered", correct, answered);
        Ratio(root, "AccuracyOverProcessed", correct, total);
        var boolean = BooleanMetrics(root.GetProperty("Boolean"), total, answered);
        Require(boolean.Processed != total || boolean.Answered == answered && boolean.TruePositive + boolean.TrueNegative == correct,
            "Pure Boolean stratum outer counts differ from its confusion matrix.");
        var brier = Metric(root, "MeanBrier", 0, 2);
        var nll = Metric(root, "MeanNll", 0, double.MaxValue);
        var ece = Metric(root, "Ece10", 0, 1);
        var mae = Metric(root, "ScoreMae", 0, 9);
        Require((answered == 0) == (brier is null) && (answered == 0) == (nll is null) && (answered == 0) == (ece is null), "Probability metric denominator is missing or inconsistent.");
        return new(total, answered, total - answered, abstainedCount, correct, answered == 0 ? null : (double)correct / answered,
            total == 0 ? null : (double)answered / total, total == 0 ? null : (double)correct / total,
            Wilson(correct, answered), brier, nll, ece, mae, boolean);
    }

    private static AuditBooleanMetrics BooleanMetrics(JsonElement root, int total, int answered)
    {
        var p = Number(root, "Processed");
        var a = Number(root, "Answered");
        var u = Number(root, "Unanswered");
        var abstained = Number(root, "Abstained");
        var positive = Number(root, "ReferencePositive");
        var negative = Number(root, "ReferenceNegative");
        var pa = Number(root, "ReferencePositiveAnswered");
        var na = Number(root, "ReferenceNegativeAnswered");
        var tp = Number(root, "TruePositive");
        var fp = Number(root, "FalsePositive");
        var tn = Number(root, "TrueNegative");
        var fn = Number(root, "FalseNegative");
        int[] counts = [p, a, u, abstained, positive, negative, pa, na, tp, fp, tn, fn];
        Require(counts.All(count => count >= 0 && count <= total) && p <= total && a <= answered && a + u == p && abstained <= u &&
            positive + negative == p && pa + na == a && pa <= positive && na <= negative &&
            tp + fn == pa && tn + fp == na && tp + fp + tn + fn == a, "Invalid Boolean confusion matrix/denominators.");
        Ratio(root, "Coverage", a, p);
        Ratio(root, "AccuracyOnAnswered", tp + tn, a);
        Ratio(root, "AccuracyOverProcessed", tp + tn, p);
        Ratio(root, "PositiveRecallOnAnswered", tp, pa);
        Ratio(root, "NegativeRecallOnAnswered", tn, na);
        Ratio(root, "PredictedPositiveRateOnAnswered", tp + fp, a);
        var balanced = pa == 0 || na == 0 ? (double?)null : ((double)tp / pa + (double)tn / na) / 2;
        EqualOptional(Optional(root, "BalancedAccuracyOnAnswered"), balanced, "Boolean balanced accuracy");
        var auroc = Metric(root, "AurocOnAnswered", 0, 1);
        Require((pa == 0 || na == 0) == (auroc is null), "Boolean AUROC denominator is inconsistent.");
        return new(p, a, u, abstained, positive, negative, pa, na, tp, fp, tn, fn,
            pa == 0 ? null : (double)tp / pa, na == 0 ? null : (double)tn / na,
            balanced, auroc, a == 0 ? null : (double)(tp + fp) / a);
    }

    private static void Partition(AuditMetrics expected, IEnumerable<AuditMetrics> partition, string name)
    {
        var rows = partition.ToArray();
        Require(rows.Length is >= 1 and <= 384 && rows.Sum(row => row.Total) == expected.Total &&
            rows.Sum(row => row.Answered) == expected.Answered && rows.Sum(row => row.Correct) == expected.Correct, $"{name} denominators differ from their marginal.");
        var boolean = rows.Select(row => row.Boolean).ToArray();
        int[] sum = [boolean.Sum(row => row.Processed), boolean.Sum(row => row.Answered), boolean.Sum(row => row.Unanswered), boolean.Sum(row => row.Abstained),
            boolean.Sum(row => row.ReferencePositive), boolean.Sum(row => row.ReferenceNegative), boolean.Sum(row => row.ReferencePositiveAnswered),
            boolean.Sum(row => row.ReferenceNegativeAnswered), boolean.Sum(row => row.TruePositive), boolean.Sum(row => row.FalsePositive),
            boolean.Sum(row => row.TrueNegative), boolean.Sum(row => row.FalseNegative)];
        Require(sum.SequenceEqual(BooleanCounts(expected.Boolean)), $"{name} Boolean denominators differ from their marginal.");
        WeightedMean(expected.MeanBrier, rows, metric => metric.MeanBrier, name + " MeanBrier");
        WeightedMean(expected.MeanNll, rows, metric => metric.MeanNll, name + " MeanNll");
    }

    private static void WeightedMean(double? expected, IEnumerable<AuditMetrics> metrics, Func<AuditMetrics, double?> selector, string field)
    {
        var rows = metrics.ToArray();
        Require(rows.Length <= 384, "Weighted metric partition exceeds its bound.");
        var denominator = rows.Sum(row => row.Answered);
        var actual = denominator == 0 ? (double?)null : rows.Sum(row => (selector(row) ?? 0) * row.Answered) / denominator;
        Require(expected.HasValue == actual.HasValue && (actual is null || double.IsFinite(actual.Value) &&
            Math.Abs(expected!.Value - actual.Value) <= 1e-10), "Invalid weighted arithmetic: " + field);
    }

    private static void ScoreMetric(AuditMetrics marginal, AuditMetrics? score, string field)
    {
        var expected = score is { Answered: > 0 } ? score.ScoreMae : null;
        Require(score is not { Answered: > 0 } || expected is not null, "Answered score stratum requires ScoreMae.");
        EqualOptional(marginal.ScoreMae, expected, field);
    }

    private static void EqualCounts(AuditMetrics reference, AuditMetrics actual, string name) =>
        Require(reference.Total == actual.Total && reference.Answered == actual.Answered && reference.Abstained == actual.Abstained && reference.Correct == actual.Correct &&
            BooleanCounts(reference.Boolean).SequenceEqual(BooleanCounts(actual.Boolean)), $"{name} counts differ despite complete parity reports.");

    private static int[] BooleanCounts(AuditBooleanMetrics value) => [value.Processed, value.Answered, value.Unanswered, value.Abstained,
        value.ReferencePositive, value.ReferenceNegative, value.ReferencePositiveAnswered, value.ReferenceNegativeAnswered,
        value.TruePositive, value.FalsePositive, value.TrueNegative, value.FalseNegative];

    private static void EqualGroups(AuditMetricGroup[] reference, AuditMetricGroup[] actual, string field)
    {
        Require(reference.Length == actual.Length, $"Reference/actual {field} strata counts differ.");
        foreach (var group in reference)
        {
            var matching = actual.SingleOrDefault(item => item.Name == group.Name);
            Require(matching is not null, $"Reference/actual {field} strata names differ.");
            EqualCounts(group.Metrics, matching!.Metrics, field + "/" + group.Name);
        }
    }

    private static AuditPairedMetricGroup[] PairedGroups(AuditMetricGroup[] reference, AuditMetricGroup[] actual) =>
        reference.Select(group =>
        {
            var paired = actual.Single(item => item.Name == group.Name);
            return new AuditPairedMetricGroup(group.Name, group.Metrics, paired.Metrics, Delta(group.Metrics, paired.Metrics));
        }).ToArray();

    private static AuditBatchReport Comparison(JsonElement root, JsonElement reference, JsonElement actual, CancellationToken token)
    {
        Require(Text(root, "schema_version") == "sezika.oracle-comparison.v1", "Unsupported comparison schema.");
        var casesHash = Text(root, "cases_sha256");
        Identity(root.GetProperty("identity"), casesHash);
        Require(HashEqual(Text(root, "contract_sha256"), Contract) && HashEqual(Text(reference, "ManifestSha256"), casesHash) &&
            HashEqual(Text(actual, "ManifestSha256"), casesHash), "Comparison and capture manifest/contract hashes differ.");
        Require(HashEqual(Text(root, "reference_sha256"), Text(reference, "CaptureSha256")) &&
            HashEqual(Text(root, "actual_sha256"), Text(actual, "CaptureSha256")), "Comparison capture hashes differ from quality batches.");
        var ids = Array(root, "selected_case_ids", 1, 10000).Select(item => item.GetString() ?? throw new InvalidDataException("Null case ID.")).ToArray();
        foreach (var id in ids)
        {
            token.ThrowIfCancellationRequested();
            Require(id.Length is >= 1 and <= 256 && !id.Any(char.IsControl), "Invalid comparison case ID.");
        }
        Require(Number(root, "manifest_count") == ids.Length && Number(root, "reference_count") == ids.Length &&
            Number(root, "actual_count") == ids.Length && Number(root, "compared_count") == ids.Length &&
            Number(reference, "Processed") == ids.Length && Number(actual, "Processed") == ids.Length &&
            Number(root, "answered_pairs") == ids.Length && Number(root, "failure_pairs") == 0,
            "Comparison full counts differ or contain failure pairs.");
        Require(Text(root, "reference_measurement_status") is "complete" or "completed" &&
            Text(root, "actual_measurement_status") is "complete" or "completed" &&
            Text(root, "selection_mode") == "all_captured_cases" && root.GetProperty("requested_case_ids").ValueKind == JsonValueKind.Null,
            "Comparison is incomplete or selects a subset.");
        Require(Flag(root, "passed") && Flag(root, "full_manifest_passed") && Flag(root, "complete_input_coverage"), "Comparison did not fully pass.");
        Ratio(root, "input_coverage", ids.Length, ids.Length);
        foreach (var field in new[] { "missing_case_ids", "excluded_reference_case_ids", "excluded_actual_case_ids", "issues" })
            Require(Array(root, field, 0, 10000).Length == 0, "Comparison has missing/excluded cases or issues.");
        var nearTies = Number(root, "near_tie_cases");
        Require(nearTies >= 0 && nearTies <= ids.Length, "Invalid near-tie count.");
        var tolerances = root.GetProperty("tolerances");
        Tolerance(tolerances, "raw_logits", 0.0005, 0.0001);
        Tolerance(tolerances, "probabilities", 0.0001, 0.0001);
        Tolerance(tolerances, "score", 0.001, 0.0001);
        EqualOptional(Optional(tolerances, "near_tie_logit_margin"), 0.001, "near-tie tolerance");
        var logitError = Metric(root, "max_logit_error", 0, double.MaxValue) ?? throw new InvalidDataException("Missing comparison logit error.");
        var probabilityError = Metric(root, "max_probability_error", 0, 1) ?? throw new InvalidDataException("Missing comparison probability error.");
        return new(null!, Text(root, "reference_sha256"), Text(root, "actual_sha256"), casesHash, ids,
            nearTies, logitError, probabilityError, true);
    }

    private static void Identity(JsonElement root, string casesHash)
    {
        Require(IsHash(casesHash) && Text(root, "model_id") == Model && Text(root, "model_revision") == Revision &&
            HashEqual(Text(root, "weights_sha256"), Weights) && HashEqual(Text(root, "tokenizer_sha256"), Tokenizer) &&
            Text(root, "upstream_source_revision") == Source && HashEqual(Text(root, "cases_sha256"), casesHash) &&
            HashEqual(Text(root, "contract_sha256"), Contract) && Number(root, "max_len") == 1024 && Number(root, "head_max_len") == 256 &&
            Text(root, "temperature_policy") == Temperature, "Capture/comparison does not use the fixed baseline identity/policy.");
    }

    private static string ActualEnvironment(JsonElement root)
    {
        Require(Text(root, "name") == "sezika_typed_modernbert_marker_engine" && Text(root, "backend") == "cuda_fp32" &&
            Text(root, "length_policy") == "laya_compatible" && Text(root, "architecture") == "X64" &&
            Text(root, "runtime_identifier") == "win-x64" && !Flag(root, "require_aot") &&
            Flag(root, "is_dynamic_code_supported") && Flag(root, "is_dynamic_code_compiled") && Flag(root, "managed_host_detected"),
            "Unexpected actual capture environment; this historical baseline is managed CUDA, not AOT.");
        var hashes = root.GetProperty("binary_sha256");
        Require(hashes.ValueKind == JsonValueKind.Object && hashes.EnumerateObject().Count() == 4, "Actual capture binary hash set differs.");
        string[] keys = ["Sezika.OracleCapture.dll", "Sezika.dll", "Sezika.Cuda.dll", "process_executable"];
        foreach (var key in keys) Require(IsHash(Text(hashes, key)), "Invalid actual capture binary hash.");
        Require(ValidName(Text(root, "framework")) && ValidName(Text(root, "operating_system")), "Empty actual capture runtime/OS identity.");
        return Text(root, "framework") + "|" + Text(root, "operating_system") + "|" + string.Join('|', keys.Select(key => Text(hashes, key).ToLowerInvariant()));
    }

    private static string ReferenceEnvironment(JsonElement root)
    {
        Require(Text(root, "backend") == "pytorch_cpu_fp32" && Text(root, "implementation") == "pinned_upstream_laya_agent" &&
            Text(root, "upstream_version") == "0.3.20" && Number(root, "batch_size") == 1 && Number(root, "threads") == 1 &&
            Number(root, "seed") == 0 && Flag(root, "deterministic_algorithms"), "Unexpected pinned reference environment.");
        string[] hashes = ["source_lock_sha256", "exporter_sha256", "upstream_agent_sha256", "upstream_common_sha256"];
        foreach (var key in hashes) Require(IsHash(Text(root, key)), "Invalid pinned reference source hash.");
        foreach (var field in new[] { "python", "platform", "machine", "torch_version" })
            Require(ValidName(Text(root, field)), "Empty pinned reference runtime/platform identity.");
        var dependencies = DependencyIdentity(root.GetProperty("direct_dependencies"), 32);
        var allDependencies = DependencyIdentity(root.GetProperty("dependencies"), 128);
        return string.Join('|', hashes.Select(key => Text(root, key).ToLowerInvariant())) + "|" + Text(root, "python") + "|" +
            Text(root, "platform") + "|" + Text(root, "machine") + "|" + Text(root, "torch_version") + "|threads=1|seed=0|deterministic=true|" +
            dependencies + "|" + allDependencies;
    }

    private static string DependencyIdentity(JsonElement dependencies, int maximum)
    {
        Require(dependencies.ValueKind == JsonValueKind.Object && dependencies.EnumerateObject().Count() >= 1 &&
            dependencies.EnumerateObject().Count() <= maximum, "Invalid pinned reference dependencies.");
        foreach (var property in dependencies.EnumerateObject())
            Require(ValidName(property.Name) && property.Value.ValueKind == JsonValueKind.String &&
                property.Value.GetString() is { } version && ValidName(version), "Null/empty pinned reference dependency identity.");
        return string.Join('|', dependencies.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => property.Name + "=" + property.Value.GetString()));
    }

    private static void Tolerance(JsonElement root, string field, double absolute, double relative)
    {
        var value = root.GetProperty(field);
        EqualOptional(Optional(value, "absolute"), absolute, field + " absolute tolerance");
        EqualOptional(Optional(value, "relative"), relative, field + " relative tolerance");
    }

    private static AuditMetricDelta Delta(AuditMetrics reference, AuditMetrics actual) => new(
        Difference(reference.AccuracyOnAnswered, actual.AccuracyOnAnswered), Difference(reference.Coverage, actual.Coverage),
        Difference(reference.MeanBrier, actual.MeanBrier), Difference(reference.MeanNll, actual.MeanNll),
        Difference(reference.Ece10, actual.Ece10), Difference(reference.ScoreMae, actual.ScoreMae));

    private static double? Difference(double? reference, double? actual) => reference.HasValue && actual.HasValue ? actual.Value - reference.Value : null;

    private static string[] BooleanWeakness(AuditBooleanMetrics boolean)
    {
        var findings = new List<string>(3);
        if (boolean.Processed == 0) findings.Add("No Boolean cases in this dataset.");
        if (boolean.PositiveRecallOnAnswered is < 0.5) findings.Add("Observed positive recall is below 0.5; this is a descriptive weakness, not an acceptance threshold.");
        if (boolean.NegativeRecallOnAnswered is < 0.5) findings.Add("Observed negative recall is below 0.5; this is a descriptive weakness, not an acceptance threshold.");
        if (boolean.AurocOnAnswered is < 0.5) findings.Add("Observed Boolean AUROC is below 0.5; probabilities are not established as calibrated correctness.");
        return findings.ToArray();
    }

    private static AuditWilsonInterval? Wilson(int correct, int answered)
    {
        if (answered == 0) return null;
        var interval = CalibrationConfidenceIntervals.Wilson(correct, answered);
        return new(interval.Lower, interval.Upper);
    }

    private static string Resolve(string directory, string path)
    {
        Require(!string.IsNullOrWhiteSpace(path) && path.Length <= 1024 && !Path.IsPathRooted(path), "Evidence paths must be relative to their plan/index.");
        return Path.GetFullPath(path, directory);
    }

    private static bool ValidName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
    private static bool IsHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool HashEqual(string left, string right) => IsHash(left) && IsHash(right) && left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static void Require([DoesNotReturnIf(false)] bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static string Text(JsonElement root, string field) => root.GetProperty(field).GetString() ?? throw new InvalidDataException($"Missing string: {field}.");
    private static int Number(JsonElement root, string field) => root.GetProperty(field).GetInt32();
    private static bool Flag(JsonElement root, string field) => root.GetProperty(field).GetBoolean();
    private static JsonElement[] Array(JsonElement root, string field, int minimum, int maximum)
    {
        var array = root.GetProperty(field);
        Require(array.ValueKind == JsonValueKind.Array && array.GetArrayLength() >= minimum && array.GetArrayLength() <= maximum, $"Invalid {field} array size.");
        return array.EnumerateArray().ToArray();
    }
    private static double? Optional(JsonElement root, string field)
    {
        var value = root.GetProperty(field);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetDouble();
    }
    private static double? Metric(JsonElement root, string field, double minimum, double maximum)
    {
        var value = Optional(root, field);
        Require(value is null || double.IsFinite(value.Value) && value.Value >= minimum && value.Value <= maximum, $"Invalid numeric metric: {field}.");
        return value;
    }
    private static void Ratio(JsonElement root, string field, int numerator, int denominator) =>
        EqualOptional(Optional(root, field), denominator == 0 ? null : (double)numerator / denominator, field);
    private static void EqualOptional(double? actual, double? expected, string field) =>
        Require(actual.HasValue == expected.HasValue && (actual is null || double.IsFinite(actual.Value) && Math.Abs(actual.Value - expected!.Value) <= 1e-12), $"Invalid arithmetic: {field}.");

    private sealed record ArchiveEntry(string Hash, string SourceHash, string Transformation);
    private sealed record QualityView(AuditMetrics Overall, AuditMetricGroup[] ByType, AuditMetricGroup[] ByLanguage,
        AuditMetricGroup[] ByLanguageAndType, JsonElement[] Batches);
    private sealed record Snapshot(string Path, string Sha256, JsonDocument Document);

    private sealed class Snapshots(CancellationToken token) : IDisposable
    {
        private readonly Dictionary<string, Snapshot> files = new(StringComparer.OrdinalIgnoreCase);
        private long totalBytes;
        public bool Contains(string path) => files.ContainsKey(path);
        public Snapshot Read(string path, string? expectedHash)
        {
            token.ThrowIfCancellationRequested();
            if (files.TryGetValue(path, out var existing))
            {
                Require(expectedHash is null || HashEqual(expectedHash, existing.Sha256), "Repeated input hash differs.");
                return existing;
            }
            Require(files.Count < 2066, "Baseline input file count exceeds the bounded limit.");
            byte[] bytes;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Require(input.Length is >= 2 and <= MaxFileBytes && totalBytes + input.Length <= MaxTotalBytes, "Baseline JSON byte budget exceeded.");
                bytes = new byte[checked((int)input.Length)];
                // ReadExactly is bounded by the declared file size; cancellation is checked per 64 KiB block.
                for (var offset = 0; offset < bytes.Length; offset += 65536)
                {
                    token.ThrowIfCancellationRequested();
                    input.ReadExactly(bytes.AsSpan(offset, Math.Min(65536, bytes.Length - offset)));
                }
                Require(input.ReadByte() == -1, "Input changed size while being read.");
            }
            totalBytes += bytes.Length;
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Require(expectedHash is null || HashEqual(expectedHash, hash), "Baseline input hash mismatch: " + path);
            var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            try
            {
                var nodes = 0;
                Unique(document.RootElement, ref nodes);
                var snapshot = new Snapshot(path, hash, document);
                files.Add(path, snapshot);
                return snapshot;
            }
            catch { document.Dispose(); throw; }
        }
        public void Revalidate()
        {
            foreach (var file in files.Values)
            {
                token.ThrowIfCancellationRequested();
                using var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Require(input.Length is >= 2 and <= MaxFileBytes, "Input size changed during verification.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[65536];
                var length = input.Length;
                for (long offset = 0; offset < length; offset += buffer.Length)
                {
                    token.ThrowIfCancellationRequested();
                    var count = (int)Math.Min(buffer.Length, length - offset);
                    input.ReadExactly(buffer.AsSpan(0, count));
                    hash.AppendData(buffer, 0, count);
                }
                Require(input.ReadByte() == -1 && HashEqual(Convert.ToHexStringLower(hash.GetHashAndReset()), file.Sha256), "Input changed during verification.");
            }
        }
        private void Unique(JsonElement element, ref int nodes)
        {
            token.ThrowIfCancellationRequested();
            Require(++nodes <= 250000, "JSON node limit exceeded.");
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    Require(names.Add(property.Name), "Duplicate JSON property: " + property.Name);
                    Unique(property.Value, ref nodes);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Unique(item, ref nodes);
        }
        public void Dispose() { foreach (var file in files.Values) file.Document.Dispose(); }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AuditBaselinePlan
{
    public required string SchemaVersion { get; init; }
    public required AuditFile ArchiveIndex { get; init; }
    public required AuditDatasetPlan[] Datasets { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AuditFile
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AuditDatasetPlan
{
    public required string Name { get; init; }
    public required string EvidenceKind { get; init; }
    public required string DatasetSha256 { get; init; }
    public required int DatasetTotal { get; init; }
    public required AuditFile Reference { get; init; }
    public required AuditFile Actual { get; init; }
    public required AuditFile[] Comparisons { get; init; }
}

internal sealed record AuditBaselineReport(DateTimeOffset StartedUtc, double ElapsedMilliseconds, string PlanSha256, string ArchiveIndexSha256,
    string ModelId, string ModelRevision, string WeightsSha256, string TokenizerSha256, string UpstreamSourceRevision,
    string ContractSha256, string RenderingVersion, string TemperaturePolicy, string ActualCaptureBuildAndEnvironmentIdentity,
    string PinnedReferenceEnvironmentIdentity, AuditDatasetReport[] Datasets)
{
    public string SchemaVersion => "sezika.audit-baseline-verification.v1";
    public string Status => "verified";
    public string MeasurementOrigin => "archived_evidence_verification_no_inference";
    public string RawCaptureRevalidation => "not_performed";
    public string RawManifestRevalidation => "not_performed";
    public bool Passed => true;
    public bool CalibrationEstablished => false;
    public bool RepresentativeLanguageQualityEstablished => false;
    public bool NativeAotEstablished => false;
    public string AcceptanceMeaning => "Integrity, archived full-batch comparison linkage, complete denominators and fixed capture identities only. Poor quality remains visible and does not fail this audit baseline. No new inference, raw re-scoring, language qualification, calibration, AOT or performance acceptance.";
    public string IntervalMeaning => "95% Wilson intervals describe answered-case accuracy in each archived dataset; correlated audit/fixture cases do not establish population accuracy.";
    public string AggregateMetricVerification => "Brier, NLL and score MAE have checked weighted denominators. ECE and AUROC remain archived aggregate measurements; their finite range and eligible denominators are checked, and they cannot be recomputed from aggregate slices.";
}
internal sealed record AuditDatasetReport(string Name, string EvidenceKind, string DatasetSha256, int DatasetTotal,
    AuditEvidenceFile ReferenceEvidence, AuditEvidenceFile ActualEvidence, AuditMetrics Reference, AuditMetrics Actual,
    AuditMetricDelta ActualMinusReference, AuditPairedMetricGroup[] ByType, AuditPairedMetricGroup[] ByLanguage,
    AuditPairedMetricGroup[] ByLanguageAndType, AuditBatchReport[] Batches, string[] BooleanWeaknesses, string LanguageScope);
internal sealed record AuditEvidenceFile(string Path, string Sha256, string OriginalSourceSha256, string Transformation);
internal sealed record AuditBatchReport(AuditEvidenceFile Comparison, string ReferenceCaptureSha256, string ActualCaptureSha256,
    string ManifestSha256, string[] SelectedCaseIds, int NearTieCases, double MaxLogitError, double MaxProbabilityError, bool FullManifestPassed);
internal sealed record AuditMetricGroup(string Name, AuditMetrics Metrics);
internal sealed record AuditPairedMetricGroup(string Name, AuditMetrics Reference, AuditMetrics Actual, AuditMetricDelta ActualMinusReference);
internal sealed record AuditMetrics(int Total, int Answered, int Unanswered, int? Abstained, int Correct, double? AccuracyOnAnswered,
    double? Coverage, double? AccuracyOverProcessed, AuditWilsonInterval? AccuracyWilson95, double? MeanBrier,
    double? MeanNll, double? Ece10, double? ScoreMae, AuditBooleanMetrics Boolean);
internal sealed record AuditWilsonInterval(double Lower, double Upper);
internal sealed record AuditMetricDelta(double? AccuracyOnAnswered, double? Coverage, double? MeanBrier,
    double? MeanNll, double? Ece10, double? ScoreMae);
internal sealed record AuditBooleanMetrics(int Processed, int Answered, int Unanswered, int Abstained,
    int ReferencePositive, int ReferenceNegative, int ReferencePositiveAnswered, int ReferenceNegativeAnswered,
    int TruePositive, int FalsePositive, int TrueNegative, int FalseNegative, double? PositiveRecallOnAnswered,
    double? NegativeRecallOnAnswered, double? BalancedAccuracyOnAnswered, double? AurocOnAnswered, double? PredictedPositiveRateOnAnswered);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(AuditBaselinePlan))]
[JsonSerializable(typeof(AuditBaselineReport))]
internal partial class BaselineJsonContext : JsonSerializerContext;
