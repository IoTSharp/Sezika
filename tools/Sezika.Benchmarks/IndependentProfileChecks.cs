using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.Benchmarks;

internal static class IndependentProfileChecks
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length is < 2 or > 6 || args.Length % 2 != 0) throw new ArgumentException("--artifact-directory既有目录 [--max-checks 1–8] [--timeout-seconds 1–60]");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2)
                if (args[index] is not ("--artifact-directory" or "--max-checks" or "--timeout-seconds") || !options.TryAdd(args[index], args[index + 1]))
                    throw new ArgumentException("检查参数重复或未知。");
            var root = Path.GetFullPath(options.GetValueOrDefault("--artifact-directory") ?? throw new ArgumentException("缺artifact目录。"));
            var maximum = int.Parse(options.GetValueOrDefault("--max-checks", "8"), System.Globalization.CultureInfo.InvariantCulture);
            var seconds = int.Parse(options.GetValueOrDefault("--timeout-seconds", "30"), System.Globalization.CultureInfo.InvariantCulture);
            if (!Directory.Exists(root) || maximum is < 1 or > 8 || seconds is < 1 or > 60) throw new ArgumentException("检查数量/目录/墙钟边界不符。");
            using var budget = new ProfileBudget(seconds);
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; budget.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                for (var index = 0; index < maximum; index++)
                {
                    budget.Check(); Check(index + 1, root, budget); budget.Check();
                    Console.WriteLine($"PASS independent-profile-check {index + 1}/{maximum}; fixture-only, formal-samples=0");
                }
            }
            finally { Console.CancelKeyPress -= cancel; }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(IndependentProfile.Describe(error)); return 1; }
    }

    private static void Check(int number, string root, ProfileBudget budget)
    {
        using var fixture = new Fixture(root, budget.Token);
        var report = Report(number == 4 ? 2 : 1);
        var plans = IndependentProfile.Discover(fixture.Model, fixture.Request, 1024, 4096, budget, report);
        switch (number)
        {
            case 1:
                Execute(fixture, fixture.Pipeline, plans, budget, report);
                Require(report.Status == "diagnostic_complete" && report.Requests.Count == 1 && report.Requests[0].PipelineScoreEntered == 3 &&
                    report.Requests[0].PipelineScoreReturned == 3 && report.Requests[0].ValidatedTypedAnswers == 3 && report.Requests[0].TypedRequestReturned &&
                    report.Requests[0].E2eMilliseconds is >= 0 && report.ModelDisposed == true && report.WorkspaceBytes == 0, "Actual tiny Evaluate/E2E/closure failed.");
                Require(report.Requests[0].Answers[0].CandidateIds.SequenceEqual(["a", "b"]) &&
                    report.Requests[0].Answers[2].CandidateIds.SequenceEqual(["false", "true"]), "Actual candidate order differs.");
                {
                    var path = fixture.NewOutput("typed-report.json"); IndependentProfile.Publish(path, report); fixture.RecordOutput(path);
                    using var document = JsonDocument.Parse(File.ReadAllBytes(path)); var value = document.RootElement;
                    Require(value.GetProperty("fixture_only").GetBoolean() && value.GetProperty("formal_samples").GetInt32() == 0 &&
                        !value.GetProperty("performance_accepted").GetBoolean() && value.GetProperty("encoder_milliseconds").ValueKind == JsonValueKind.Null &&
                        value.GetProperty("encoder_forwards_completed").ValueKind == JsonValueKind.Null, "Fixture published acceptance semantics differ.");
                }
                break;
            case 2:
                var bad = fixture.Head with { Identity = fixture.Head.Identity with { EncoderSha256 = new string('F', 64) } };
                Expect<InvalidDataException>(() => IndependentProfile.ValidateAssets(fixture.Model, bad, 1024));
                Require(fixture.Pipeline.Calls == 0, "Invalid identity entered Score.");
                break;
            case 3:
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel(); using var cancelled = new ProfileBudget(5, cancellation.Token);
                    Execute(fixture, fixture.Pipeline, plans, cancelled, report);
                }
                Require(report.Status == "failed" && report.Requests.Count == 1 && report.PlannedQuestions == 3 &&
                    report.Requests[0].PipelineScoreEntered == 0 && !report.Requests[0].TypedRequestReturned && report.Requests[0].Status == "not_started" &&
                    fixture.Model.IsDisposed, "Precancel work lost denominator or entered forward.");
                break;
            case 4:
                fixture.Pipeline.FailAt = 2;
                Execute(fixture, fixture.Pipeline, plans, budget, report);
                Require(report.Status == "failed" && report.PlannedQuestions == 6 && report.Requests.Count == 2 &&
                    report.Requests[0].PipelineScoreEntered == 2 && report.Requests[0].PipelineScoreReturned == 1 &&
                    report.Requests[0].Answers.Length == 0 && !report.Requests[0].TypedRequestReturned && report.Requests[1].Status == "not_started",
                    "Midrequest failure fabricated typed answers or lost denominator.");
                break;
            case 5:
                // Mutate the expected identity, never fabricate the actual runtime response.
                plans[0] = plans[0] with { QuestionId = "wrong-expected-id" };
                Execute(fixture, fixture.Pipeline, plans, budget, report);
                Require(report.Status == "failed" && report.Requests[0].Status == "failed" && report.Requests[0].Error is not null &&
                    report.Requests[0].TypedRequestReturned && report.Requests[0].E2eMilliseconds is >= 0 &&
                    report.Requests[0].PipelineScoreReturned == 3 && report.Requests[0].ValidatedTypedAnswers == 0 && fixture.Model.IsDisposed,
                    "Typed validation failure lost actual Evaluate return/time or row failure state.");
                break;
            case 6:
                var args = new[] { "--package", "unused", "--head", "unused", "--identity", "unused", "--identity-sha256", new string('A',64),
                    "--feature-sha256", new string('B',64), "--head-sha256", new string('C',64), "--request", "unused", "--request-sha256", new string('D',64), "--output", "unused" };
                Expect<ArgumentException>(() => IndependentProfileOptions.Parse([.. args, "--backend", "cpu"]));
                Expect<ArgumentException>(() => IndependentProfileOptions.Parse([.. args, "--samples", "6"]));
                Expect<ArgumentException>(() => IndependentProfileOptions.Parse([.. args, "--timeout-seconds", "301"]));
                var empty = new IndependentDecisionRequest(1, fixture.Model.ModelId, fixture.State, []);
                Expect<InvalidDataException>(() => IndependentProfile.Discover(fixture.Model, empty, 1024, 4096, budget, Report(1)));
                Require(fixture.Pipeline.Calls == 0, "Invalid options/request entered Score.");
                var early = IndependentProfile.CreateReport(2, 1, true);
                Require(early.Requests.Count == 3 && early.RequestQuestions is null && early.PlannedQuestions is null &&
                    early.Requests.All(row => row.PlannedQuestions is null && row.Status == "not_started"), "Early plan guessed questions or dropped requests.");
                IndependentProfile.ValidateAssets(fixture.Model, fixture.Head, 1024);
                IndependentProfile.BindAssetIdentity(early, fixture.Model, fixture.Head);
                Require(early.ModelId == fixture.Model.ModelId && early.HeadSha256 == fixture.Head.FileSha256 &&
                    early.Requests.All(row => row.Status == "not_started"), "Validated assets not available before runtime/GPU creation.");
                var failedOutput = fixture.NewOutput("preflight-failed.json");
                var invalidInputArgs = new[] { "--package", "unused", "--head", "unused", "--identity", fixture.NewOutput("missing-identity.json"),
                    "--identity-sha256", new string('A',64), "--feature-sha256", new string('B',64), "--head-sha256", new string('C',64),
                    "--request", "unused", "--request-sha256", new string('D',64), "--output", failedOutput, "--samples", "2", "--warmup", "1" };
                Require(IndependentProfile.Run(invalidInputArgs) == 2, "Invalid preflight succeeded."); fixture.RecordOutput(failedOutput);
                using (var failureDocument = JsonDocument.Parse(File.ReadAllBytes(failedOutput)))
                {
                    var failed = failureDocument.RootElement;
                    Require(!failed.GetProperty("gpu_resources_started").GetBoolean() && failed.GetProperty("requests").GetArrayLength() == 3 &&
                        failed.GetProperty("planned_questions").ValueKind == JsonValueKind.Null && failed.GetProperty("request_questions").ValueKind == JsonValueKind.Null,
                        "Actual preflight failure touched GPU or lost unknown/full request denominator.");
                }
                break;
            case 7:
                fixture.Pipeline.ThrowOnDispose = true;
                Execute(fixture, fixture.Pipeline, plans, budget, report);
                Require(report.Status == "failed" && report.CleanupErrors.Any(item => item.Contains("injected cleanup", StringComparison.Ordinal)) &&
                    fixture.Model.IsDisposed && report.Requests[0].ValidatedTypedAnswers == 3, "Cleanup failure hid execution/owned model closure.");
                break;
            case 8:
                var conflict = fixture.NewOutput("existing.json"); File.WriteAllBytes(conflict, [1,2,3]); fixture.RecordOutput(conflict);
                Expect<IOException>(() => IndependentProfile.Publish(conflict, report));
                Require(File.ReadAllBytes(conflict).SequenceEqual(new byte[] {1,2,3}) && Directory.GetFiles(fixture.Directory, ".independent-profile-*.partial").Length == 0,
                    "Report collision overwrote input or retained owned partial.");
                Expect<IOException>(() => IndependentProfile.Publish(fixture.NewOutput("write-failed.json"), report,
                    afterCreate: stream => { stream.WriteByte(1); throw new IOException("injected partial write failure"); }));
                Require(Directory.GetFiles(fixture.Directory, ".independent-profile-*.partial").Length == 0, "Failed prefix write retained proved-owned partial.");
                using (var cancellation = new CancellationTokenSource())
                    Expect<OperationCanceledException>(() => IndependentProfile.Publish(fixture.NewOutput("cancelled-audit.json"), report,
                        cancellation.Token, afterWrite: cancellation.Cancel));
                Require(Directory.GetFiles(fixture.Directory, ".independent-profile-*.partial").Length == 0 &&
                    !File.Exists(fixture.NewOutput("cancelled-audit.json")), "Cancelled audit prevented independent partial cleanup or moved output.");
                break;
            default: throw new InvalidDataException("Unknown bounded check.");
        }
    }

    private static void Execute(Fixture fixture, IIndependentDecisionPipeline pipeline, ProfilePreparedSequence[] plans,
        ProfileBudget budget, IndependentProfileReport report) =>
        IndependentProfile.Execute(fixture.Model, fixture.Head, pipeline, fixture.Request, plans, report.PlannedRequests, 0, 1024, 4096, budget, report);

    private static IndependentProfileReport Report(int requests) => IndependentProfile.CreateReport(requests, 0, true);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidDataException("Expected " + typeof(T).Name); }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private readonly List<(string Path, DateTime Created, long Bytes, string Hash)> _files = [];
        private readonly HashSet<string> _unverifiedCreated = new(StringComparer.OrdinalIgnoreCase);
        private ModernBertEncoder? _pendingEncoder;
        internal string Directory { get; }
        internal JsonElement State { get; }
        internal IndependentEncoderModelPackage Model { get; }
        internal IndependentMarkerHeadAsset Head { get; }
        internal FaultPipeline Pipeline { get; }
        internal IndependentDecisionRequest Request { get; }
        internal Fixture(string root, CancellationToken token)
        {
            _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            Directory = Path.GetFullPath(Path.Combine(_root, "independent-profile-fixture-" + Guid.NewGuid().ToString("N")));
            System.IO.Directory.CreateDirectory(Directory);
            try
            {
                var json = new StringBuilder("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<pad>\":0,\"<eos>\":1,\"<bos>\":2,\"<unk>\":3,\"<mask>\":4");
                for (var value = 0; value < 256; value++) { token.ThrowIfCancellationRequested(); json.Append($",\"<0x{value:X2}>\":{1000 + value}"); }
                json.Append("},\"merges\":[]}}"); var bytes = Encoding.UTF8.GetBytes(json.ToString());
                var path = NewOutput("tokenizer.json");
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    _unverifiedCreated.Add(path); var created = File.GetCreationTimeUtc(path); Exception? writeFailure = null;
                    try { stream.Write(bytes); stream.Flush(true); } catch (Exception error) { writeFailure = error; }
                    try
                    {
                        stream.Position = 0;
                        _files.Add((path, created, stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
                        _unverifiedCreated.Remove(path);
                    }
                    catch (Exception error)
                    {
                        if (writeFailure is not null) throw new AggregateException("Fixture tokenizer write/ownership snapshot failed", writeFailure, error);
                        throw;
                    }
                    if (writeFailure is not null) ExceptionDispatchInfo.Capture(writeFailure).Throw();
                }
                using var state = JsonDocument.Parse("\"x\""); State = state.RootElement.Clone();
                Request = new(1, "sezika/profile-fixture", State,
                    [new IndependentChoiceQuestion("choice", "choose", [new("a", "first"), new("b", "second")]),
                     new IndependentScoreQuestion("score", "rate", ["low", "high"]), new IndependentBooleanQuestion("boolean", "judge", "x", "no", "yes")]);
                var config = new ModernBertConfig { VocabularySize = 2000, HiddenSize = 2, IntermediateSize = 2, LayerCount = 1,
                    HeadCount = 1, MaxTokens = 1024, GlobalAttentionEvery = 1, LocalAttention = 2, GlobalRopeTheta = 16000, LocalRopeTheta = 16000 };
                var embeddings = new float[4000]; embeddings[8] = 0.5f; embeddings[9] = -0.5f;
                var encoderDescriptor = Encoding.UTF8.GetBytes("synthetic-profile-H2-v1;V2000,I2,L1,H1,T1024,GA1,LA2,R16000;embedding[8]=.5,[9]=-.5;norm=ones;QKV/attention/MLP=zeros");
                var encoder = new ModernBertEncoder(config, new ModernBertWeights { TokenEmbeddings = embeddings, EmbeddingNorm = [1f,1f], FinalNorm = [1f,1f],
                    Layers = [new ModernBertLayerWeights { AttentionNorm = null, Qkv = new float[12], AttentionOutput = new float[4], MlpNorm = [1f,1f],
                        MlpUp = new float[8], MlpDown = new float[4] }] }, new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(3) }, token);
                _pendingEncoder = encoder;
                Model = new() { ModelId = Request.ModelId, SourceModelId = "synthetic/profile-contract", Revision = "fixture-v1", License = "Apache-2.0",
                    EncoderSha256 = IndependentProfile.Hash(encoderDescriptor), TokenizerSha256 = IndependentProfile.Hash(bytes),
                    Tokenizer = new TokenizerJson(path, token), EstimatedResidentBytes = 0,
                    Encoder = encoder };
                _pendingEncoder = null;
                var descriptor = IndependentProfile.Hash(Encoding.UTF8.GetBytes("fixture-head-F32:[.1,-.1,-.2,.3,.4,-.5];not-asset-file"));
                Head = new(new MarkerFeatureIdentity(Model.ModelId, Model.EncoderSha256, Model.TokenizerSha256,
                    IndependentProfile.Hash("synthetic fixture data; no admission"u8), 2), descriptor, 7, 0.1f, 0, 0,
                    new LinearMarkerHead(2, [0.1f,-0.1f,-0.2f,0.3f,0.4f,-0.5f]), descriptor);
                Pipeline = new(Model.Encoder, Head);
            }
            catch (Exception error)
            {
                try { Dispose(); } catch (Exception cleanup) { throw new AggregateException("Fixture construction and cleanup failed", error, cleanup); }
                throw;
            }
        }
        internal string NewOutput(string name) => Path.Combine(Directory, name);
        internal void RecordOutput(string path)
        {
            _unverifiedCreated.Add(path);
            if (_files.Count >= 3) throw new IOException("Fixture owned file item bound exceeded; retained: " + path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _files.Add((path, File.GetCreationTimeUtc(path), stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
            _unverifiedCreated.Remove(path);
        }
        public void Dispose()
        {
            var errors = new List<Exception>();
            try { Pipeline?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { Model?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _pendingEncoder?.Dispose(); } catch (Exception error) { errors.Add(error); }
            var clock = Stopwatch.StartNew();
            for (var index = 0; index < _files.Count && index < 3; index++)
            {
                try
                {
                    if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Fixture cleanup deadline.");
                    var item = _files[index]; var path = Path.GetFullPath(item.Path);
                    if (Path.GetDirectoryName(path) != Directory || !Directory.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Fixture ownership path mismatch.");
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        if (File.GetCreationTimeUtc(path) != item.Created || stream.Length != item.Bytes || Convert.ToHexString(SHA256.HashData(stream)) != item.Hash) throw new IOException("Fixture file identity changed; retained.");
                    File.Delete(path);
                }
                catch (Exception error) { errors.Add(error); }
            }
            foreach (var path in _unverifiedCreated.Take(3)) errors.Add(new IOException("Fixture created file ownership unknown; retained: " + path));
            if (_unverifiedCreated.Count > 3) errors.Add(new IOException("Fixture unknown-created item bound exceeded; retained directory: " + Directory));
            try
            {
                if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Fixture directory cleanup deadline; retained: " + Directory);
                System.IO.Directory.Delete(Directory, recursive: false);
            }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Fixture cleanup failed", errors);
        }
    }

    private sealed class FaultPipeline(ModernBertEncoder encoder, IndependentMarkerHeadAsset head) : IIndependentDecisionPipeline
    {
        private readonly IndependentCpuDecisionPipeline _inner = new(encoder, head); private bool _disposed;
        internal int Calls { get; private set; }
        internal int FailAt { get; set; }
        internal bool ThrowOnDispose { get; set; }
        public MarkerFeatureIdentity Identity => _inner.Identity;
        public string FeatureSha256 => _inner.FeatureSha256;
        public string HeadSha256 => _inner.HeadSha256;
        public string Backend => _inner.Backend;
        public int MaxTokens => _inner.MaxTokens;
        public double[] Score(ReadOnlySpan<int> ids, MarkerQuestionType type, ReadOnlySpan<int> markers, CancellationToken token = default)
        { Calls++; if (Calls == FailAt) throw new InvalidOperationException("injected midrequest failure"); return _inner.Score(ids, type, markers, token); }
        public void Dispose() { if (_disposed) return; _disposed = true; _inner.Dispose(); if (ThrowOnDispose) throw new InvalidOperationException("injected cleanup failure"); }
    }
}
