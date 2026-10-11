using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.Benchmarks;

internal static class DiagnosticProfileChecks
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length is < 2 or > 6 || args.Length % 2 != 0)
                throw new ArgumentException("--artifact-directory既有目录 [--max-checks 1–8] [--timeout-seconds 1–60]");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var parseClock = Stopwatch.StartNew();
            for (var index = 0; index < args.Length && index < 6; index += 2)
                if (parseClock.Elapsed >= TimeSpan.FromSeconds(5) || args[index] is not ("--artifact-directory" or "--max-checks" or "--timeout-seconds") ||
                    !options.TryAdd(args[index], args[index + 1])) throw new ArgumentException("检查参数重复、未知或解析超时。");
            var root = Path.GetFullPath(options.GetValueOrDefault("--artifact-directory") ?? throw new ArgumentException("缺artifact目录。"));
            var maximum = int.Parse(options.GetValueOrDefault("--max-checks", "8"), System.Globalization.CultureInfo.InvariantCulture);
            var seconds = int.Parse(options.GetValueOrDefault("--timeout-seconds", "30"), System.Globalization.CultureInfo.InvariantCulture);
            if (!Directory.Exists(root) || maximum is < 1 or > 8 || seconds is < 1 or > 60) throw new ArgumentException("检查数量/目录/墙钟边界不符。");
            using var budget = new ProfileBudget(seconds);
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; budget.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                for (var index = 0; index < maximum && index < 8; index++)
                {
                    budget.Check(); Check(index + 1, root, budget); budget.Check();
                    Console.WriteLine($"PASS independent-diagnostic-profile-check {index + 1}/{maximum}; fixture-only, synthetic logits, formal-samples=0");
                }
            }
            finally { Console.CancelKeyPress -= cancel; }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(IndependentProfile.Describe(error)); return 1; }
    }

    private static void Check(int number, string root, ProfileBudget budget)
    {
        using var fixture = new Fixture(root, budget);
        var report = DiagnosticIndependentProfile.CreateReport(number == 4 ? 2 : 1, 0, true);
        var plans = DiagnosticIndependentProfile.Discover(fixture.Model, fixture.Request, 1024, 4096, budget, report);
        void Execute(ProfileBudget executionBudget) => DiagnosticIndependentProfile.Execute(fixture.Model, fixture.Head, fixture.Pipeline,
            fixture.Request, plans, report.PlannedRequests, 0, 1024, 4096, executionBudget, report);
        switch (number)
        {
            case 1:
                Execute(budget);
                Require(report.Status == "diagnostic_complete" && report.Requests.Count == 1 && report.Requests[0].PipelineScoreEntered == 3 &&
                    report.Requests[0].PipelineScoreReturned == 3 && report.Requests[0].ValidatedTypedAnswers == 3 && report.Requests[0].TypedRequestReturned &&
                    report.Requests[0].E2eMilliseconds is >= 0 && report.ModelDisposed == true && report.WorkspaceBytes == 0,
                    "Actual tiny runtime Evaluate/observation/closure failed.");
                Require(report.Requests[0].Answers[0].CandidateIds.SequenceEqual(["a", "b"]) &&
                    report.Requests[0].Answers[2].CandidateIds.SequenceEqual(["false", "true"]), "Actual candidate order differs.");
                {
                    var path = fixture.Output("typed-report.json"); DiagnosticIndependentProfile.Publish(path, report); fixture.Record(path);
                    using var document = JsonDocument.Parse(File.ReadAllBytes(path)); var value = document.RootElement;
                    Require(value.GetProperty("contract").GetString() == "untrained-independent-e2e-diagnostic-v1" &&
                        value.GetProperty("fixture_only").GetBoolean() && value.GetProperty("formal_samples").GetInt32() == 0 &&
                        !value.GetProperty("performance_accepted").GetBoolean() && !value.GetProperty("training_accepted").GetBoolean() &&
                        !value.GetProperty("quality_accepted").GetBoolean() && !value.GetProperty("calibrated").GetBoolean() &&
                        !value.GetProperty("license_approved").GetBoolean() && !value.GetProperty("sealed_test_accepted").GetBoolean() &&
                        value.GetProperty("head_identity").ValueKind == JsonValueKind.Null && value.GetProperty("feature_sha256").ValueKind == JsonValueKind.Null &&
                        value.GetProperty("observed_loss").ValueKind == JsonValueKind.Null && value.GetProperty("completed_updates").GetInt32() == 0 &&
                        value.GetProperty("encoder_milliseconds").ValueKind == JsonValueKind.Null && value.GetProperty("head_milliseconds").ValueKind == JsonValueKind.Null &&
                        value.GetProperty("encoder_forwards_completed").ValueKind == JsonValueKind.Null &&
                        value.GetProperty("diagnostic_asset_sha256").GetString() == fixture.Head.FileSha256 &&
                        value.GetProperty("initialization_descriptor_sha256").GetString() == fixture.Head.InitializationDescriptorSha256 &&
                        value.GetProperty("seed").GetInt32() == 17 && value.GetProperty("initialization_algorithm").GetString() == DiagnosticHeadAssetStore.Algorithm,
                        "Independent diagnostic published identity/acceptance semantics differ.");
                }
                break;
            case 2:
                Expect<DecisionException>(() => DiagnosticHeadAssetStore.Load(fixture.Output("head.asset"), fixture.Head.Identity, 18,
                    fixture.Head.InitializationDescriptorSha256, fixture.Head.FileSha256, budget.Token));
                Expect<InvalidDataException>(() => DiagnosticIndependentProfile.ValidateAssets(fixture.Model, fixture.Head, 1));
                var badPath = fixture.Output("mismatched.asset");
                var bad = DiagnosticHeadAssetStore.CreateNew(badPath, fixture.Head.Identity with { EncoderSha256 = new string('F', 64) }, 17, budget.Token);
                fixture.Record(badPath);
                Expect<InvalidDataException>(() => DiagnosticIndependentProfile.ValidateAssets(fixture.Model, bad, 1024));
                Require(fixture.Pipeline.Calls == 0, "Invalid identity entered Score.");
                var formalIdentity = "{\"model_id\":\"sezika/x\",\"encoder_sha256\":\"" + new string('A',64) +
                    "\",\"tokenizer_sha256\":\"" + new string('B',64) + "\",\"hidden_size\":2,\"protocol\":\"decision-v1\",\"length_policy\":\"strict\",\"data_manifest_sha256\":\"" + new string('C',64) + "\"}";
                Expect<JsonException>(() => JsonSerializer.Deserialize(Encoding.UTF8.GetBytes(formalIdentity), DiagnosticProfileJson.Default.DiagnosticProfileExpectedIdentity));
                break;
            case 3:
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel(); using var cancelled = new ProfileBudget(5, cancellation.Token); Execute(cancelled);
                }
                Require(report.Status == "failed" && report.Requests.Count == 1 && report.PlannedQuestions == 3 &&
                    report.Requests[0].PipelineScoreEntered == 0 && !report.Requests[0].TypedRequestReturned && report.Requests[0].Status == "not_started" &&
                    fixture.Model.IsDisposed, "Precancel work lost denominator or entered Score.");
                break;
            case 4:
                fixture.Pipeline.FailAt = 2; Execute(budget);
                Require(report.Status == "failed" && report.PlannedQuestions == 6 && report.Requests.Count == 2 &&
                    report.Requests[0].PipelineScoreEntered == 2 && report.Requests[0].PipelineScoreReturned == 1 &&
                    report.Requests[0].Answers.Length == 0 && !report.Requests[0].TypedRequestReturned && report.Requests[1].Status == "not_started",
                    "Midrequest failure fabricated typed answers or lost denominator.");
                break;
            case 5:
                plans[0] = plans[0] with { QuestionId = "wrong-expected-id" }; Execute(budget);
                Require(report.Status == "failed" && report.Requests[0].Status == "failed" && report.Requests[0].Error is not null &&
                    report.Requests[0].TypedRequestReturned && report.Requests[0].E2eMilliseconds is >= 0 &&
                    report.Requests[0].PipelineScoreReturned == 3 && report.Requests[0].ValidatedTypedAnswers == 0 && fixture.Model.IsDisposed,
                    "Typed validation failure lost actual Evaluate return/time or row failure.");
                break;
            case 6:
                var args = Arguments("unused", "unused");
                Expect<ArgumentException>(() => DiagnosticProfileOptions.Parse([.. args, "--backend", "cpu"]));
                Expect<ArgumentException>(() => DiagnosticProfileOptions.Parse([.. args, "--feature-sha256", new string('B',64)]));
                Expect<ArgumentException>(() => DiagnosticProfileOptions.Parse([.. args, "--samples", "6"]));
                Expect<ArgumentException>(() => DiagnosticProfileOptions.Parse([.. args, "--warmup", "2"]));
                Expect<ArgumentException>(() => DiagnosticProfileOptions.Parse([.. args, "--timeout-seconds", "301"]));
                var empty = new IndependentDecisionRequest(1, fixture.Model.ModelId, fixture.Request.State, []);
                Expect<InvalidDataException>(() => DiagnosticIndependentProfile.Discover(fixture.Model, empty, 1024, 4096, budget,
                    DiagnosticIndependentProfile.CreateReport(1, 0, true)));
                var early = DiagnosticIndependentProfile.CreateReport(2, 1, true);
                Require(early.Requests.Count == 3 && early.RequestQuestions is null && early.PlannedQuestions is null &&
                    early.Requests.All(row => row.PlannedQuestions is null && row.Status == "not_started"), "Early plan guessed questions or lost requests.");
                DiagnosticIndependentProfile.BindAssetIdentity(early, fixture.Model, fixture.Head);
                Require(early.DiagnosticAssetSha256 == fixture.Head.FileSha256 && early.HeadIdentity is null && early.FeatureSha256 is null &&
                    early.Seed == 17 && early.ObservedLoss is null, "Diagnostic identity was not available before GPU creation.");
                var output = fixture.Output("preflight-failed.json");
                // Acquire a real input lease, then fail its hash. Fixture disposal opens this same
                // tokenizer with FileShare.None, so it also proves that the failed Run released it.
                Require(DiagnosticIndependentProfile.Run([.. Arguments(fixture.Output("tokenizer.json"), output), "--samples", "2", "--warmup", "1"]) == 2,
                    "Invalid preflight succeeded."); fixture.Record(output);
                using (var failureDocument = JsonDocument.Parse(File.ReadAllBytes(output)))
                {
                    var failed = failureDocument.RootElement;
                    Require(!failed.GetProperty("gpu_resources_started").GetBoolean() && failed.GetProperty("requests").GetArrayLength() == 3 &&
                        failed.GetProperty("planned_questions").ValueKind == JsonValueKind.Null && failed.GetProperty("request_questions").ValueKind == JsonValueKind.Null &&
                        failed.GetProperty("head_identity").ValueKind == JsonValueKind.Null && failed.GetProperty("feature_sha256").ValueKind == JsonValueKind.Null,
                        "Preflight failure touched GPU or lost unknown/full request denominator.");
                }
                break;
            case 7:
                fixture.Pipeline.ThrowOnDispose = true; Execute(budget);
                Require(report.Status == "failed" && report.CleanupErrors.Any(item => item.Contains("injected cleanup", StringComparison.Ordinal)) &&
                    fixture.Model.IsDisposed && report.Requests[0].ValidatedTypedAnswers == 3,
                    "Cleanup failure hid execution or owned model closure.");
                break;
            case 8:
                var conflict = fixture.Output("existing.json"); fixture.Write(conflict, [1,2,3]);
                Expect<IOException>(() => DiagnosticIndependentProfile.Publish(conflict, report));
                Require(File.ReadAllBytes(conflict).SequenceEqual(new byte[] {1,2,3}) && !fixture.HasPartial(), "Collision overwrote input or retained owned partial.");
                Expect<IOException>(() => DiagnosticIndependentProfile.Publish(fixture.Output("write-failed.json"), report,
                    afterCreate: stream => { stream.WriteByte(1); throw new IOException("injected partial write failure"); }));
                Require(!fixture.HasPartial(), "Failed prefix write retained proved-owned partial.");
                using (var cancellation = new CancellationTokenSource())
                    Expect<OperationCanceledException>(() => DiagnosticIndependentProfile.Publish(fixture.Output("cancelled-audit.json"), report,
                        cancellation.Token, afterWrite: cancellation.Cancel));
                Require(!fixture.HasPartial() && !File.Exists(fixture.Output("cancelled-audit.json")), "Cancelled audit prevented independent cleanup or moved output.");
                break;
            default: throw new InvalidDataException("Unknown bounded check.");
        }
    }

    private static string[] Arguments(string identity, string output) => ["--package", "unused", "--head", "unused", "--identity", identity,
        "--identity-sha256", new string('A',64), "--descriptor-sha256", new string('B',64), "--head-sha256", new string('C',64), "--seed", "17",
        "--request", "unused", "--request-sha256", new string('D',64), "--output", output];
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException("Expected " + typeof(T).Name); }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private readonly DateTime _directoryCreated;
        private readonly List<(string Path, DateTime Created, long Bytes, string Hash)> _files = new(8);
        private readonly HashSet<string> _unverifiedCreated = new(StringComparer.OrdinalIgnoreCase);
        private ModernBertEncoder? _pendingEncoder;
        private readonly ProfileBudget _budget;
        internal string Directory { get; }
        internal IndependentEncoderModelPackage Model { get; }
        internal DiagnosticHeadAsset Head { get; }
        internal FaultPipeline Pipeline { get; }
        internal IndependentDecisionRequest Request { get; }

        internal Fixture(string root, ProfileBudget budget)
        {
            _budget = budget; _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            Directory = Path.GetFullPath(Path.Combine(_root, "independent-diagnostic-profile-fixture-" + Guid.NewGuid().ToString("N")));
            System.IO.Directory.CreateDirectory(Directory); _directoryCreated = System.IO.Directory.GetCreationTimeUtc(Directory);
            try
            {
                var json = new StringBuilder("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<pad>\":0,\"<eos>\":1,\"<bos>\":2,\"<unk>\":3,\"<mask>\":4");
                for (var value = 0; value < 256; value++) { budget.Check(); json.Append($",\"<0x{value:X2}>\":{1000 + value}"); }
                json.Append("},\"merges\":[]}}"); var bytes = Encoding.UTF8.GetBytes(json.ToString());
                var path = Output("tokenizer.json"); Write(path, bytes);
                using var state = JsonDocument.Parse("\"x\"");
                Request = new(1, "sezika/diagnostic-profile-fixture", state.RootElement.Clone(),
                    [new IndependentChoiceQuestion("choice", "choose", [new("a", "first"), new("b", "second")]),
                     new IndependentScoreQuestion("score", "rate", ["low", "high"]), new IndependentBooleanQuestion("boolean", "judge", "x", "no", "yes")]);
                var config = new ModernBertConfig { VocabularySize = 2000, HiddenSize = 2, IntermediateSize = 2, LayerCount = 1,
                    HeadCount = 1, MaxTokens = 1024, GlobalAttentionEvery = 1, LocalAttention = 2, GlobalRopeTheta = 16000, LocalRopeTheta = 16000 };
                var encoder = new ModernBertEncoder(config, new ModernBertWeights { TokenEmbeddings = new float[4000], EmbeddingNorm = [1f,1f], FinalNorm = [1f,1f],
                    Layers = [new ModernBertLayerWeights { AttentionNorm = null, Qkv = new float[12], AttentionOutput = new float[4], MlpNorm = [1f,1f],
                        MlpUp = new float[8], MlpDown = new float[4] }] }, new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = budget.Remaining }, budget.Token);
                _pendingEncoder = encoder;
                Model = new() { ModelId = Request.ModelId, SourceModelId = "synthetic/diagnostic-profile-contract", Revision = "fixture-v1", License = "Apache-2.0",
                    EncoderSha256 = IndependentProfile.Hash("synthetic-diagnostic-profile-H2-V2000-I2-L1-H1-T1024-GA1-LA2-R16000;norm-ones;all-other-weights-zero"u8),
                    TokenizerSha256 = IndependentProfile.Hash(bytes), Tokenizer = new TokenizerJson(path, budget.Token), EstimatedResidentBytes = 0, Encoder = encoder };
                _pendingEncoder = null;
                path = Output("head.asset");
                Head = DiagnosticHeadAssetStore.CreateNew(path, new(Model.ModelId, Model.EncoderSha256, Model.TokenizerSha256, 2), 17, budget.Token);
                Record(path); Pipeline = new(Head);
            }
            catch (Exception error)
            {
                try { Dispose(); } catch (Exception cleanup) { throw new AggregateException("Fixture construction and cleanup failed", error, cleanup); }
                throw;
            }
        }
        internal string Output(string name) => Path.Combine(Directory, name);
        internal bool HasPartial()
        {
            _budget.Check();
            using var enumerator = System.IO.Directory.EnumerateFiles(Directory, ".independent-diagnostic-profile-*.partial", SearchOption.TopDirectoryOnly).GetEnumerator();
            return enumerator.MoveNext();
        }
        internal void Write(string path, byte[] bytes)
        {
            _budget.Check(); _unverifiedCreated.Add(path);
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                var created = File.GetCreationTimeUtc(path); Exception? failure = null;
                try { stream.Write(bytes); stream.Flush(true); } catch (Exception error) { failure = error; }
                stream.Position = 0; AddRecord(path, created, stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            }
            catch { if (!File.Exists(path)) _unverifiedCreated.Remove(path); throw; }
        }
        internal void Record(string path)
        {
            _unverifiedCreated.Add(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            AddRecord(path, File.GetCreationTimeUtc(path), stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
        }
        private void AddRecord(string path, DateTime created, long bytes, string hash)
        {
            if (_files.Count >= 8) throw new IOException("Fixture file bound exceeded; retained: " + path);
            _files.Add((path, created, bytes, hash)); _unverifiedCreated.Remove(path);
        }
        public void Dispose()
        {
            var errors = new List<Exception>();
            try { Pipeline?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { Model?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _pendingEncoder?.Dispose(); } catch (Exception error) { errors.Add(error); }
            var clock = Stopwatch.StartNew();
            for (var index = 0; index < _files.Count && index < 8; index++)
            {
                try
                {
                    if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Fixture cleanup deadline.");
                    var item = _files[index]; var path = Path.GetFullPath(item.Path);
                    if (Path.GetDirectoryName(path) != Directory || !Directory.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Fixture ownership path mismatch.");
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                        if (File.GetCreationTimeUtc(path) != item.Created || stream.Length != item.Bytes || Convert.ToHexString(SHA256.HashData(stream)) != item.Hash)
                            throw new IOException("Fixture file identity changed; retained.");
                    File.Delete(path);
                }
                catch (Exception error) { errors.Add(error); }
            }
            foreach (var path in _unverifiedCreated.Take(8)) errors.Add(new IOException("Fixture file ownership unknown; retained: " + path));
            try
            {
                if (clock.Elapsed >= TimeSpan.FromSeconds(5) || System.IO.Directory.GetCreationTimeUtc(Directory) != _directoryCreated)
                    throw new IOException("Fixture directory identity/deadline differs; retained: " + Directory);
                System.IO.Directory.Delete(Directory, recursive: false);
            }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Fixture cleanup failed", errors);
        }
    }

    // Only observation/typed-runtime fixtures: no encoder or head forward is claimed by these logits.
    private sealed class FaultPipeline(DiagnosticHeadAsset head) : IDiagnosticDecisionPipeline
    {
        private bool _disposed;
        internal int Calls { get; private set; }
        internal int FailAt { get; set; }
        internal bool ThrowOnDispose { get; set; }
        public DiagnosticHeadIdentity Identity => head.Identity;
        public string DiagnosticAssetSha256 => head.FileSha256;
        public string InitializationDescriptorSha256 => head.InitializationDescriptorSha256;
        public string Backend => "synthetic-diagnostic-profile-observation-fixture";
        public int MaxTokens => 1024;
        public double[] Score(ReadOnlySpan<int> ids, MarkerQuestionType type, ReadOnlySpan<int> markers, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested();
            Calls++; if (Calls == FailAt) throw new InvalidOperationException("injected midrequest failure");
            if (ids.Length is < 2 or > 1024 || markers.Length is < 2 or > 32) throw new InvalidDataException("Tiny fixture input bound differs.");
            var result = new double[markers.Length];
            for (var index = 0; index < result.Length && index < 32; index++) { token.ThrowIfCancellationRequested(); result[index] = index * 0.1; }
            return result;
        }
        public void Dispose() { if (_disposed) return; _disposed = true; if (ThrowOnDispose) throw new InvalidOperationException("injected cleanup failure"); }
    }
}
