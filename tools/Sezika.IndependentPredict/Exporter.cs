using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.IndependentPredict;

internal static class Exporter
{
    // The digest identifies this versioned protocol contract, not measured
    // quality. Implementation and model identities remain separately visible.
    internal const string ProtocolContract = "sezika.decision-v1.contract.v1\nschema=1\nlength=strict\nmax_tokens=1024\nbos=2\neos=1\nmarker=4\ninput=typed-json-segments-v1\nboolean=false,true\nscore=0..K-1\n";

    internal static ExportResult Run(Options options, CancellationToken token)
    {
        var manifestBytes = BoundedFiles.Read(options.Manifest, token, 65536);
        BoundedFiles.Require(BoundedFiles.Same(BoundedFiles.Hash(manifestBytes), options.ManifestSha256), "Export manifest SHA-256 differs from the supplied commitment.");
        var manifest = BoundedFiles.Json(manifestBytes, ExportJsonContext.Default.ExportManifest, token);
        BoundedFiles.Require(manifest.SchemaVersion == 1 && !string.IsNullOrWhiteSpace(manifest.DatasetId) && manifest.DatasetId.Length <= 128 &&
            manifest.Primitive is "choice" or "score" or "boolean" && double.IsFinite(manifest.AbstainBelow) && manifest.AbstainBelow is >= 0 and <= 1,
            "Export manifest schema, primitive, dataset or frozen abstention threshold is invalid.");
        var root = Path.GetDirectoryName(options.Manifest)!;
        var headBytes = BoundedFiles.Verify(root, manifest.HeadBinding, token, 65536);
        var head = BoundedFiles.Json(headBytes, ExportJsonContext.Default.HeadBinding, token);
        ValidateHead(head);
        var (admission, rows) = Admission.ReadApproved(root, manifest, options.MaximumRecords, token);
        return ExportRecords(options, manifest, rows, head, BoundedFiles.Hash(manifestBytes), BoundedFiles.Hash(headBytes),
            manifest.Requests.Sha256, admission, fixture: false, LoadProduction, token);
    }

    private static IndependentDecisionV1Runtime LoadProduction(string package, string head, HeadBinding binding,
        IndependentDecisionRuntimeOptions options, CancellationToken token) =>
        IndependentDecisionV1Runtime.Load(package, head, binding.Identity, binding.FeatureSha256, binding.HeadSha256, options, token);

    internal static void ValidateHead(HeadBinding head)
    {
        var id = head.Identity;
        BoundedFiles.Require(head.SchemaVersion == 1 && id is not null && id.ModelId.StartsWith("sezika/", StringComparison.Ordinal) &&
            id.HiddenSize is >= 1 and <= 2048 && id.Protocol == "decision-v1" && id.LengthPolicy == "strict" &&
            BoundedFiles.IsHash(id.EncoderSha256) && BoundedFiles.IsHash(id.TokenizerSha256) && BoundedFiles.IsHash(id.DataManifestSha256) &&
            BoundedFiles.IsHash(head.FeatureSha256) && BoundedFiles.IsHash(head.HeadSha256), "Independent head binding identity is invalid.");
    }

    // No CLI option can supply a loader or enable fixture execution. SelfTest
    // calls this exact export loop with a tiny C# runtime and fixture=true.
    internal static ExportResult ExportRecords(Options options, ExportManifest manifest, RequestRow[] rows, HeadBinding head,
        string exportHash, string bindingHash, string requestsHash, AdmissionBinding? admission, bool fixture, RuntimeLoader loader, CancellationToken token,
        Action<string>? beforePublish = null)
    {
        var destination = Path.GetFullPath(options.Output);
        BoundedFiles.CheckLinks(destination, token);
        BoundedFiles.Require(!Directory.Exists(destination) && !File.Exists(destination), "Output destination already exists.");
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var staging = Path.GetFullPath(Path.Combine(parent, $".{Path.GetFileName(destination)}.export-{Guid.NewGuid():N}"));
        BoundedFiles.Require(Path.GetDirectoryName(staging) == parent && !Directory.Exists(staging) && !File.Exists(staging), "Staging ownership boundary differs.");
        var journal = new OutputJournal(staging);
        try
        {
            var result = ExportStaging(options with { Output = staging }, manifest, rows, head, exportHash, bindingHash,
                requestsHash, admission, fixture, loader, token, journal);
            // A report is the final commit marker inside staging. Verify every
            // file against the exact bytes this invocation wrote before exposing
            // any calibration manifest at the requested destination.
            journal.Validate();
            beforePublish?.Invoke(destination);
            if (token.IsCancellationRequested && result.Report.CalibrationManifestSha256 is not null)
                throw new OperationCanceledException(token);
            Directory.Move(staging, destination);
            return result with { OutputDirectory = destination };
        }
        catch
        {
            journal.Cleanup();
            throw;
        }
    }

    private static ExportResult ExportStaging(Options options, ExportManifest manifest, RequestRow[] rows, HeadBinding head,
        string exportHash, string bindingHash, string requestsHash, AdmissionBinding? admission, bool fixture, RuntimeLoader loader, CancellationToken token,
        OutputJournal journal)
    {
        var clock = Stopwatch.StartNew();
        ValidateHead(head);
        BoundedFiles.Require(rows.Length is >= 2 && rows.Length <= options.MaximumRecords && rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() == rows.Length &&
            rows.Any(row => row.Split == "calibration") && rows.Any(row => row.Split == "sealed_test") && (fixture || admission is not null),
            "Export requires both accepted splits, unique records and admission provenance.");
        BoundedFiles.CheckLinks(options.Output, token);
        BoundedFiles.Require(!Directory.Exists(options.Output) && !File.Exists(options.Output), "Output directory already exists; choose a new destination.");
        Directory.CreateDirectory(options.Output);
        var release = new ReleaseIdentity(1, "sezika.encoder-head-identity.v1", head.Identity.ModelId,
            fixture ? "synthetic-fixture-v1" : IndependentModelLoader.SourceRevision,
            head.Identity.EncoderSha256.ToLowerInvariant(), head.HeadSha256.ToLowerInvariant(), head.Identity.TokenizerSha256.ToLowerInvariant(),
            "decision-v1", "strict", BoundedFiles.Hash(Encoding.UTF8.GetBytes(ProtocolContract)));
        var releaseBytes = JsonSerializer.SerializeToUtf8Bytes(release, ExportJsonContext.Default.ReleaseIdentity);
        var releaseHash = BoundedFiles.Hash(releaseBytes);
        var identity = new QualityCalibrationIdentity { ModelId = release.ModelId, ModelRevision = "head-" + release.HeadSha256,
            ModelWeightsSha256 = releaseHash, TokenizerRevision = release.SourceRevision, TokenizerSha256 = release.TokenizerSha256,
            PromptSchemaId = release.Protocol, PromptSchemaSha256 = release.ProtocolSha256 };
        WriteNew(options.Output, "identity.json", releaseBytes, journal);
        var predictions = new List<PredictionRow>();
        var outcomes = new List<Outcome>();
        var expectedClasses = 0;
        string? terminalError = null;
        try
        {
            using var runtime = loader(options.Package, options.Head, head,
                new IndependentDecisionRuntimeOptions { Deadline = TimeSpan.FromSeconds(options.RequestTimeoutSeconds), MaxTokens = 1024 }, token);
            BoundedFiles.Require(runtime.ModelId == release.ModelId && BoundedFiles.Same(runtime.EncoderSha256, release.EncoderSha256) &&
                BoundedFiles.Same(runtime.TokenizerSha256, release.TokenizerSha256), "Runtime identity differs from export identity.");
            for (var index = 0; index < rows.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var row = rows[index];
                Console.Error.WriteLine($"independent-predict: {index + 1}/{rows.Length} id={row.Id}");
                try
                {
                    BoundedFiles.Require(row.Primitive == manifest.Primitive && row.Split is "calibration" or "sealed_test" &&
                        row.Language is "zh" or "en" && !string.IsNullOrWhiteSpace(row.Domain) && row.Domain.Length <= 128,
                        "Record primitive/split/language/domain is invalid.");
                    var request = IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes(row.Request.GetRawText()));
                    BoundedFiles.Require(request.Questions.Count == 1 && request.Questions[0].Id == row.Id, "Each record must contain exactly its own question ID.");
                    var type = request.Questions[0] switch { IndependentChoiceQuestion => "choice", IndependentScoreQuestion => "score", IndependentBooleanQuestion => "boolean", _ => "unsupported" };
                    BoundedFiles.Require(type == manifest.Primitive, "Request primitive differs from export primitive.");
                    var response = runtime.Evaluate(request, token);
                    var answer = response.Answers.Single();
                    var probabilities = answer.Probabilities.ToArray();
                    BoundedFiles.Require(answer.Status == "uncalibrated" && probabilities.Length is >= 2 and <= 32 && row.Label >= 0 && row.Label < probabilities.Length &&
                        probabilities.All(value => double.IsFinite(value) && value is >= 0 and <= 1) && Math.Abs(probabilities.Sum() - 1) <= 1e-6,
                        "Model returned an invalid distribution or label mapping.");
                    expectedClasses = expectedClasses == 0 ? probabilities.Length : expectedClasses;
                    BoundedFiles.Require(probabilities.Length == expectedClasses, "CalibrationTool requires a single candidate dimension per artifact.");
                    predictions.Add(new PredictionRow(row.Id, row.Split, row.Primitive, row.Label, row.Language, row.Domain, probabilities));
                    outcomes.Add(new Outcome(row.Id, row.Split, "answered", null, probabilities.Length));
                }
                catch (DecisionException exception)
                {
                    var rejection = exception.Code.StartsWith("decision_v1_", StringComparison.Ordinal) || exception.Code.StartsWith("decision_", StringComparison.Ordinal) && exception.Code.Contains("limit", StringComparison.Ordinal);
                    outcomes.Add(new Outcome(row.Id, row.Split, rejection ? "rejected" : "failed", exception.Code, null));
                }
                catch (Exception exception) when (exception is InvalidDataException or JsonException or InvalidOperationException or ArgumentException)
                { outcomes.Add(new Outcome(row.Id, row.Split, "failed", "prediction_contract_invalid", null)); }
            }
        }
        catch (OperationCanceledException) { terminalError = "export_cancelled_or_deadline_exceeded"; }
        catch (Exception exception) when (exception is DecisionException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            terminalError = exception is DecisionException decisionError ? decisionError.Code : "runtime_load_failed";
            if (outcomes.Count == 0) outcomes.Add(new Outcome(rows[0].Id, rows[0].Split, "failed", exception is DecisionException decision ? decision.Code : "runtime_load_failed", null));
        }
        var processed = outcomes.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
            if (!processed.Contains(row.Id)) outcomes.Add(new Outcome(row.Id, row.Split, "unprocessed", terminalError ?? "export_incomplete", null));
        var output = new StringBuilder();
        foreach (var row in predictions)
            output.Append(JsonSerializer.Serialize(row, PredictionJsonContext.Default.PredictionRow)).Append('\n');
        var predictionBytes = Encoding.UTF8.GetBytes(output.ToString());
        WriteNew(options.Output, "predictions.jsonl", predictionBytes, journal);
        var predictionHash = BoundedFiles.Hash(predictionBytes);
        string? qualityHash = null;
        if (token.IsCancellationRequested) terminalError ??= "export_cancelled_or_deadline_exceeded";
        var complete = predictions.Count == rows.Length && terminalError is null;
        if (complete)
        {
            string SplitHash(string split) => BoundedFiles.Hash(Encoding.UTF8.GetBytes(string.Concat(predictions.Where(row => row.Split == split)
                .Select(row => JsonSerializer.Serialize(row, PredictionJsonContext.Default.PredictionRow) + "\n"))));
            var quality = new QualityCalibrationManifest { SchemaVersion = 1, DatasetId = manifest.DatasetId,
                Status = fixture ? "fixture_only" : "accepted_for_quality", RepresentsModelQuality = !fixture,
                RecordCount = rows.Length, CalibrationCount = rows.Count(row => row.Split == "calibration"), SealedTestCount = rows.Count(row => row.Split == "sealed_test"),
                AbstainBelow = manifest.AbstainBelow, RecordsSha256 = predictionHash,
                CalibrationSplitSha256 = SplitHash("calibration"), SealedTestSplitSha256 = SplitHash("sealed_test") };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(quality, ExportJsonContext.Default.QualityCalibrationManifest);
            WriteNew(options.Output, "calibration-manifest.json", bytes, journal);
            qualityHash = BoundedFiles.Hash(bytes);
        }
        var report = new ExportReport { Status = complete && !fixture ? "exported_pending_quality_evaluation" : "not_eligible", FixtureOnly = fixture,
            DatasetId = manifest.DatasetId, Primitive = manifest.Primitive, Total = rows.Length, Answered = predictions.Count,
            Rejected = outcomes.Count(row => row.Status == "rejected"), Failed = outcomes.Count(row => row.Status == "failed"), Unprocessed = outcomes.Count(row => row.Status == "unprocessed"),
            Outcomes = outcomes.ToArray(), Identity = identity, Release = release, ExportManifestSha256 = exportHash, RequestsSha256 = requestsHash,
            TrainingDataManifestSha256 = head.Identity.DataManifestSha256, FeatureSha256 = head.FeatureSha256, HeadBindingSha256 = bindingHash,
            AdmissionManifestSha256 = admission?.ManifestHash, AdmissionReportSha256 = admission?.ReportHash, AdmissionRecordsSha256 = admission?.RecordsHash,
            HumanAcceptanceSha256 = admission?.AcceptanceHash, SealSha256 = admission?.SealHash, PredictionsSha256 = predictionHash,
            CalibrationManifestSha256 = qualityHash, ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds, TerminalError = terminalError };
        BoundedFiles.Require(report.Total == report.Answered + report.Rejected + report.Failed + report.Unprocessed, "Export denominator is inconsistent.");
        WriteNew(options.Output, "export-report.json", JsonSerializer.SerializeToUtf8Bytes(report, ExportJsonContext.Default.ExportReport), journal);
        return new ExportResult(report, options.Output);
    }

    private static void WriteNew(string root, string name, byte[] bytes, OutputJournal journal)
    {
        var path = Path.GetFullPath(Path.Combine(root, name));
        BoundedFiles.Require(Path.GetDirectoryName(path) == root && bytes.Length <= BoundedFiles.MaxBytes, "Output path or byte bound is invalid.");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(flushToDisk: true);
        journal.Record(path, bytes);
    }

    private sealed class OutputJournal(string root)
    {
        private readonly Dictionary<string, (string Hash, int Length)> _files = new(StringComparer.Ordinal);

        internal void Record(string path, byte[] bytes)
        {
            BoundedFiles.Require(_files.Count < 4 && Path.GetDirectoryName(path) == root, "Output journal boundary exceeded.");
            _files.Add(path, (BoundedFiles.Hash(bytes), bytes.Length));
        }

        internal void Validate()
        {
            var clock = Stopwatch.StartNew();
            BoundedFiles.Require(_files.Count is 3 or 4 && _files.ContainsKey(Path.Combine(root, "export-report.json")), "Incomplete staged export cannot be published.");
            foreach (var file in _files)
            {
                BoundedFiles.Require(clock.Elapsed < TimeSpan.FromSeconds(5) && Matches(file.Key, file.Value), "Staged output changed before publication.");
            }
        }

        private static bool Matches(string path, (string Hash, int Length) identity)
        {
            BoundedFiles.CheckLinks(path, CancellationToken.None);
            if (!File.Exists(path) || new FileInfo(path).Length != identity.Length) return false;
            // Empty prediction artifacts are valid failure evidence, so the
            // normal nonempty-input reader does not apply to output snapshots.
            var bytes = BoundedFiles.Read(path, CancellationToken.None, allowEmpty: true);
            return BoundedFiles.Same(BoundedFiles.Hash(bytes), identity.Hash);
        }

        internal void Cleanup()
        {
            var clock = Stopwatch.StartNew();
            foreach (var file in _files)
            {
                try
                {
                    BoundedFiles.Require(clock.Elapsed < TimeSpan.FromSeconds(5) && Path.GetDirectoryName(Path.GetFullPath(file.Key)) == root,
                        "Staged cleanup path or time boundary differs.");
                    if (Matches(file.Key, file.Value)) File.Delete(file.Key);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                { Console.Error.WriteLine($"staged cleanup retained {file.Key}: {exception.Message}"); }
            }
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { Console.Error.WriteLine($"staged cleanup retained owned directory {root}: {exception.Message}"); }
        }
    }
}
