using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.IndependentPredict;

internal static class SelfTest
{
    internal static int Run(string[] args, CancellationToken cancellationToken)
    {
        BoundedFiles.Require(args.Length is 3 or 5 && args[1] == "--output", "self-test requires --output <new-directory> [--maximum-cases 1..8].");
        var maximumCases = 8;
        if (args.Length == 5)
            BoundedFiles.Require(args[3] == "--maximum-cases" && int.TryParse(args[4], out maximumCases) && maximumCases is >= 1 and <= 8, "Invalid self-test case bound.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var clock = Stopwatch.StartNew();
        var checks = 0;
        void Check(bool condition, string message)
        {
            token.ThrowIfCancellationRequested();
            BoundedFiles.Require(++checks <= 32 && clock.Elapsed < TimeSpan.FromSeconds(30) && condition, message);
            Console.WriteLine($"PASS {checks}: {message}");
        }
        var output = Path.GetFullPath(args[2]);
        BoundedFiles.Require(!Directory.Exists(output) && !File.Exists(output), "Self-test output already exists.");
        BoundedFiles.CheckLinks(output, token);
        Directory.CreateDirectory(output);
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var owned = Path.GetFullPath(Path.Combine(tempRoot, "sezika-independent-predict-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(owned);
        var ownedFiles = new List<string>();
        try
        {
            var tokenizerPath = Path.Combine(owned, "tokenizer.json");
            ownedFiles.Add(tokenizerPath);
            var json = new StringBuilder("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<pad>\":0,\"<eos>\":1,\"<bos>\":2,\"<unk>\":3,\"<mask>\":4");
            for (var value = 0; value < 256; value++)
            {
                token.ThrowIfCancellationRequested();
                json.Append($",\"<0x{value:X2}>\":{1000 + value}");
            }
            json.Append("},\"merges\":[]}}");
            File.WriteAllText(tokenizerPath, json.ToString(), new UTF8Encoding(false));
            var identity = new MarkerFeatureIdentity("sezika/prediction-fixture", new string('a', 64), BoundedFiles.Hash(File.ReadAllBytes(tokenizerPath)), new string('d', 64), 2);
            var featureSet = new MarkerFeatureSet(identity,
                [new MarkerFeatureExample("train-fixture", MarkerQuestionType.Boolean, "en", [2, 4, 4, 1], [1, 2], [[1f, 0f], [-1f, 0f]], 0)]);
            var training = IndependentMarkerHeadTrainer.Train(featureSet, new MarkerTrainingOptions(7, 1, 0.1f, TimeSpan.FromSeconds(2)), cancellationToken: token);
            var headPath = Path.Combine(owned, "head.asset"); ownedFiles.Add(headPath);
            var headHash = IndependentMarkerHeadAssetStore.SaveNew(headPath, training, token);
            var head = new HeadBinding(1, identity, training.FeatureSha256, headHash);
            IndependentDecisionV1Runtime Tiny(string package, string path, HeadBinding expected, IndependentDecisionRuntimeOptions options, CancellationToken ct)
            {
                var asset = IndependentMarkerHeadAssetStore.Load(path, expected.Identity, expected.FeatureSha256, expected.HeadSha256, ct);
                var config = new ModernBertConfig { VocabularySize = 2000, HiddenSize = 2, IntermediateSize = 2, LayerCount = 1, HeadCount = 1,
                    MaxTokens = 1024, GlobalAttentionEvery = 1, LocalAttention = 2, GlobalRopeTheta = 16000, LocalRopeTheta = 16000, NormEpsilon = 1e-5f };
                var model = new IndependentEncoderModelPackage { ModelId = identity.ModelId, SourceModelId = "synthetic/prediction-fixture", Revision = "fixture-v1", License = "Apache-2.0",
                    EncoderSha256 = identity.EncoderSha256, TokenizerSha256 = identity.TokenizerSha256, Tokenizer = new TokenizerJson(tokenizerPath, ct), EstimatedResidentBytes = 0,
                    Encoder = new ModernBertEncoder(config, new ModernBertWeights { TokenEmbeddings = new float[4000], EmbeddingNorm = [1f, 1f], FinalNorm = [1f, 1f],
                        Layers = [new ModernBertLayerWeights { AttentionNorm = null, Qkv = new float[12], AttentionOutput = new float[4], MlpNorm = [1f, 1f], MlpUp = new float[8], MlpDown = new float[4] }] },
                        new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(3) }, ct) };
                return new IndependentDecisionV1Runtime(model, asset, options);
            }
            var placeholder = new ArtifactReference { Path = "unused-fixture-reference", Sha256 = new string('0', 64) };
            var manifest = new ExportManifest { SchemaVersion = 1, DatasetId = "synthetic-prediction-fixture", Primitive = "boolean", AbstainBelow = 0,
                Requests = placeholder, AdmissionManifest = placeholder, AdmissionReport = placeholder, HumanAcceptance = placeholder, HeadBinding = placeholder };
            RequestRow Row(string id, string split, int label, string state = "x")
            {
                using var document = JsonDocument.Parse($$"""
                    {"schema_version":1,"model_id":"sezika/prediction-fixture","state":"{{state}}","questions":[{"id":"{{id}}","type":"boolean","instruction":"judge","statement":"x","when_false":"no","when_true":"yes"}]}
                    """);
                return new RequestRow(id, split, "boolean", label, "en", "synthetic-fixture", document.RootElement.Clone());
            }
            var rows = new[] { Row("fixture-cal-0", "calibration", 0), Row("fixture-cal-1", "calibration", 1), Row("fixture-test-0", "sealed_test", 0), Row("fixture-test-1", "sealed_test", 1) };
            var options = new Options("unused", new string('0', 64), owned, headPath, Path.Combine(output, "complete"), 32, 3, 30);
            var complete = Exporter.ExportRecords(options, manifest, rows, head, new string('0', 64), new string('1', 64), new string('2', 64), null, true, Tiny, token);
            Check(complete.Report.Status == "not_eligible" && complete.Report.FixtureOnly && complete.Report.Total == 4 && complete.Report.Answered == 4 &&
                complete.Report.Failed + complete.Report.Rejected + complete.Report.Unprocessed == 0, "exact export loop runs tiny C# runtime and fixture stays not_eligible");
            if (maximumCases == 1) return 0;
            var predictionBytes = File.ReadAllBytes(Path.Combine(options.Output, "predictions.jsonl"));
            var quality = BoundedFiles.Json(File.ReadAllBytes(Path.Combine(options.Output, "calibration-manifest.json")), ExportJsonContext.Default.QualityCalibrationManifest, token);
            Check(quality.Status == "fixture_only" && !quality.RepresentsModelQuality && BoundedFiles.Same(quality.RecordsSha256, BoundedFiles.Hash(predictionBytes)) &&
                quality.CalibrationCount == 2 && quality.SealedTestCount == 2, "consumer manifest has exact prediction byte hash and fixed split counts");
            if (maximumCases == 2) return 0;
            var lines = Admission.Lines(predictionBytes, 32, token);
            string SplitHash(string split) => BoundedFiles.Hash(Encoding.UTF8.GetBytes(string.Concat(lines.Where(line =>
            {
                using var document = JsonDocument.Parse(line); return document.RootElement.GetProperty("split").GetString() == split;
            }).Select(line => line + "\n"))));
            Check(BoundedFiles.Same(quality.CalibrationSplitSha256, SplitHash("calibration")) && BoundedFiles.Same(quality.SealedTestSplitSha256, SplitHash("sealed_test")), "split hashes match CalibrationTool LF canonicalization");
            if (maximumCases == 3) return 0;
            Check(BoundedFiles.Same(complete.Report.Identity.ModelWeightsSha256, BoundedFiles.Hash(File.ReadAllBytes(Path.Combine(options.Output, "identity.json")))) &&
                complete.Report.Release.EncoderSha256 == identity.EncoderSha256 && complete.Report.Release.HeadSha256 == headHash.ToLowerInvariant(), "versioned consumer identity binds encoder and separately exported head");
            if (maximumCases == 4) return 0;
            var rejected = Exporter.ExportRecords(options with { Output = Path.Combine(output, "rejected") }, manifest,
                [rows[0], Row("oversized-test", "sealed_test", 1, new string('x', 2000))], head, new string('0', 64), new string('1', 64), new string('2', 64), null, true, Tiny, token);
            Check(rejected.Report.Total == 2 && rejected.Report.Answered == 1 && rejected.Report.Rejected == 1 && rejected.Report.Status == "not_eligible" &&
                !File.Exists(Path.Combine(rejected.OutputDirectory, "calibration-manifest.json")), "real token-budget rejection remains in denominator and blocks consumer manifest");
            if (maximumCases == 5) return 0;
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
            IndependentDecisionV1Runtime CancelOnLoad(string package, string path, HeadBinding expected, IndependentDecisionRuntimeOptions runtimeOptions, CancellationToken ct)
            {
                var runtime = Tiny(package, path, expected, runtimeOptions, ct);
                cancelled.Cancel();
                return runtime;
            }
            var incomplete = Exporter.ExportRecords(options with { Output = Path.Combine(output, "cancelled") }, manifest, rows, head,
                new string('0', 64), new string('1', 64), new string('2', 64), null, true, CancelOnLoad, cancelled.Token);
            Check(incomplete.Report.Unprocessed == 4 && incomplete.Report.Total == 4 && !File.Exists(Path.Combine(incomplete.OutputDirectory, "calibration-manifest.json")), "cancellation cannot publish an eligible subset");
            if (maximumCases == 6) return 0;
            try
            {
                _ = BoundedFiles.Json("{\"schema_version\":1,\"schema_version\":1}"u8, ExportJsonContext.Default.ExportManifest, token);
                Check(false, "duplicate metadata must fail");
            }
            catch (InvalidDataException) { Check(true, "duplicate metadata is rejected"); }
            if (maximumCases == 7) return 0;
            try
            {
                _ = Options.Parse(["export", "--manifest", "x", "--manifest-sha256", new string('a', 64), "--package", "x", "--head", "x", "--output", "x", "--max-records", "33"]);
                Check(false, "33 records must fail");
            }
            catch (InvalidDataException) { Check(true, "record maximum cannot exceed 32"); }
            // Pending, explicitly synthetic metadata only. No acceptance or
            // seal is fabricated, and the referenced text files do not exist.
            string Metadata(string name, byte[] bytes)
            {
                var path = Path.Combine(owned, name); ownedFiles.Add(path); File.WriteAllBytes(path, bytes); return BoundedFiles.Hash(bytes);
            }
            var candidate = new AuditManifest { SchemaVersion = 1, DatasetId = manifest.DatasetId, Stage = DatasetStage.Candidate,
                IntendedUse = IntendedUse.Research, Records = placeholder, Sources = [], TestSeal = new TestSeal { Status = SealStatus.Unsealed } };
            var auditHash = Metadata("candidate.json", JsonSerializer.SerializeToUtf8Bytes(candidate, SplitAuditJsonContext.Default.AuditManifest));
            var blockedReport = new AuditReport(1, manifest.DatasetId, "blocked", false, true, false, "s4-05-admission-v1", auditHash,
                placeholder.Sha256, "fixture-only", 0.82, 4, 6, [], [], [], DateTimeOffset.UtcNow, 0);
            var blockedHash = Metadata("blocked.json", JsonSerializer.SerializeToUtf8Bytes(blockedReport, SplitAuditJsonContext.Default.AuditReport));
            var pending = new HumanAcceptance { SchemaVersion = 1, DatasetId = manifest.DatasetId, Status = "pending", IntendedUse = "Research", Reviewer = "", AcceptedUtc = default,
                Note = "synthetic pending fixture; not an approval", InputContractAndLabelsReviewed = false, AdmissionManifestSha256 = auditHash, AdmissionReportSha256 = blockedHash,
                AdmissionRecordsSha256 = placeholder.Sha256, RequestsSha256 = placeholder.Sha256, SealSha256 = placeholder.Sha256, RecordIds = [] };
            var pendingHash = Metadata("pending.json", JsonSerializer.SerializeToUtf8Bytes(pending, ExportJsonContext.Default.HumanAcceptance));
            try
            {
                _ = Admission.ReadApproved(owned, manifest with { AdmissionManifest = new ArtifactReference { Path = "candidate.json", Sha256 = auditHash },
                    AdmissionReport = new ArtifactReference { Path = "blocked.json", Sha256 = blockedHash }, HumanAcceptance = new ArtifactReference { Path = "pending.json", Sha256 = pendingHash } }, 32, token);
                Check(false, "blocked admission must fail before opening text");
            }
            catch (InvalidDataException) { Check(true, "blocked/pending admission rejects before nonexistent inventory/request text is opened"); }
            var failedDestination = Path.Combine(output, "publication-failure");
            try
            {
                _ = Exporter.ExportRecords(options with { Output = failedDestination }, manifest, rows, head,
                    new string('0', 64), new string('1', 64), new string('2', 64), null, true, Tiny, token,
                    beforePublish: destination =>
                    {
                        // A foreign owner occupies the final path only after all
                        // producer files/report were successfully staged.
                        Directory.CreateDirectory(destination);
                        File.WriteAllText(Path.Combine(destination, "foreign.txt"), "preserve-other-owner");
                    });
                Check(false, "publication must fail when another owner occupies destination");
            }
            catch (IOException)
            {
                Check(File.ReadAllText(Path.Combine(failedDestination, "foreign.txt")) == "preserve-other-owner" &&
                    !File.Exists(Path.Combine(failedDestination, "calibration-manifest.json")) &&
                    !File.Exists(Path.Combine(failedDestination, "export-report.json")) &&
                    Directory.EnumerateDirectories(output, ".publication-failure.export-*", SearchOption.TopDirectoryOnly).Take(2).Count() == 0,
                    "final publication failure exposes no consumer manifest, recovers owned staging and preserves foreign output");
            }
            using var cancelledParse = new CancellationTokenSource(); cancelledParse.Cancel();
            try
            {
                _ = Options.Parse(["export", "--manifest", "x", "--manifest-sha256", new string('a', 64), "--package", "x", "--head", "x", "--output", "x"], cancelledParse.Token);
                Check(false, "option parser must honor cancellation");
            }
            catch (OperationCanceledException) { Check(true, "option parsing observes its cancellation and fixed 5 second budget"); }
            Console.WriteLine($"self-test passed: {checks}; outputs are fixture-only; run actual CalibrationTool against complete/ artifacts for consumer integration.");
            return 0;
        }
        finally
        {
            BoundedFiles.Require(Path.GetDirectoryName(owned) == tempRoot && Path.GetFileName(owned).StartsWith("sezika-independent-predict-", StringComparison.Ordinal) && ownedFiles.Count <= 8,
                "Temporary fixture cleanup ownership differs.");
            var cleanupClock = Stopwatch.StartNew();
            foreach (var path in ownedFiles)
            {
                BoundedFiles.Require(Path.GetDirectoryName(Path.GetFullPath(path)) == owned && cleanupClock.Elapsed < TimeSpan.FromSeconds(5), "Cleanup path/time bound exceeded.");
                if (File.Exists(path)) File.Delete(path);
            }
            Directory.Delete(owned, false);
        }
    }
}
