using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.Tests;

internal static class DiagnosticRuntimeChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string, string> expectCode, bool tiny = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        using var fixture = new Fixture(token);
        using var state = JsonDocument.Parse("\"x\"");
        var request = new IndependentDecisionRequest(1, Fixture.ModelId, state.RootElement.Clone(),
        [
            new IndependentChoiceQuestion("choice", "choose", [new("a", "first"), new("b", "second")]),
            new IndependentScoreQuestion("score", "rate", ["low", "high"]),
            new IndependentBooleanQuestion("boolean", "judge", "x", "no", "yes")
        ]);
        Action[] cases =
        [
            () =>
            {
                var model = fixture.Package(token);
                var pipeline = new ReferencePipeline(model.Encoder, fixture.Asset);
                using var runtime = new DiagnosticDecisionV1Runtime(model, fixture.Asset, pipeline);
                var response = runtime.Evaluate(request, token);
                check(response.Answers.Count == 3 && pipeline.Types.SequenceEqual(
                    [MarkerQuestionType.Choice, MarkerQuestionType.Score, MarkerQuestionType.Boolean]),
                    "diagnostic real tiny encoder/head dispatches all three primitives");
                check(response.Origin == DiagnosticHeadOrigin.UntrainedDiagnostic && response.CompletedUpdates == 0 &&
                    response.ObservedLoss is null && response.Status == "untrained-diagnostic" &&
                    response.Identity == fixture.Asset.Identity && response.DiagnosticAssetSha256 == fixture.Asset.FileSha256 &&
                    response.InitializationDescriptorSha256 == fixture.Asset.InitializationDescriptorSha256 &&
                    response.Backend == pipeline.Backend && response.EncoderSha256 == model.EncoderSha256 &&
                    response.TokenizerSha256 == model.TokenizerSha256,
                    "diagnostic response retains actual asset/descriptor binding and explicit untrained provenance");
                check(response.Answers.All(answer => answer.Status == "uncalibrated" && answer.Probabilities.All(double.IsFinite) &&
                    Math.Abs(answer.Probabilities.Sum() - 1) < 1e-12 &&
                    answer.Concentration == answer.Probabilities.Max() && answer.SelectedIndex >= 0 &&
                    answer.SelectedIndex < answer.Probabilities.Count) && response.Answers[0].CandidateIds.SequenceEqual(["a", "b"]) &&
                    response.Answers[2].CandidateIds.SequenceEqual(["false", "true"]),
                    "diagnostic fixture distributions are finite and preserve candidate order; no quality claim");
                runtime.Dispose();
                check(model.IsDisposed && pipeline.DisposeCount == 1, "diagnostic runtime releases its explicit owned pipeline/model");
            },
            () =>
            {
                var model = fixture.Package(token);
                try
                {
                    _ = new DiagnosticDecisionV1Runtime(model, fixture.Asset, null!);
                    check(false, "diagnostic pipeline must be explicit");
                }
                catch (ArgumentNullException exception)
                {
                    check(exception.ParamName == "pipeline" && model.IsDisposed,
                        "diagnostic null pipeline is rejected and owned model released without CPU fallback");
                }
            },
            () =>
            {
                for (var index = 0; index < 6; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var model = fixture.Package(token);
                    var pipeline = new ReferencePipeline(model.Encoder, fixture.Asset);
                    switch (index)
                    {
                        case 0: pipeline.IdentityOverride = fixture.Asset.Identity with { HiddenSize = 3 }; break;
                        case 1: pipeline.IdentityOverride = fixture.Asset.Identity with { ModelId = "synthetic/other" }; break;
                        case 2: pipeline.AssetHashOverride = new string('C', 64); break;
                        case 3: pipeline.DescriptorHashOverride = new string('D', 64); break;
                        case 4: pipeline.BackendOverride = " "; break;
                        case 5: pipeline.MaxTokensOverride = 2; break;
                    }
                    expectCode(() => new DiagnosticDecisionV1Runtime(model, fixture.Asset, pipeline),
                        "diagnostic_runtime_identity_mismatch", $"diagnostic binding rejects mismatch variant {index}");
                    check(model.IsDisposed && pipeline.DisposeCount == 1, $"diagnostic mismatch {index} releases owned resources");
                }
            },
            () =>
            {
                var model = fixture.Package(token);
                var pipeline = new ReferencePipeline(model.Encoder, fixture.Asset);
                using var runtime = new DiagnosticDecisionV1Runtime(model, fixture.Asset, pipeline,
                    new IndependentDecisionRuntimeOptions { Deadline = TimeSpan.FromMilliseconds(100) });
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                try { runtime.Evaluate(request, cancelled.Token); check(false, "diagnostic pre-cancellation must throw"); }
                catch (OperationCanceledException) { check(pipeline.ScoreCount == 0, "diagnostic pre-cancellation prevents scoring"); }
                pipeline.BeforeScore = scoreToken =>
                {
                    _ = scoreToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
                    scoreToken.ThrowIfCancellationRequested();
                };
                expectCode(() => runtime.Evaluate(request, token), "independent_runtime_deadline_exceeded",
                    "diagnostic shared session converts only its internal bounded deadline");
                pipeline.BeforeScore = null;
                check(runtime.Evaluate(request, token).Answers.Count == 3, "diagnostic deadline failure releases the active gate");
            },
            () =>
            {
                var model = fixture.Package(token);
                var pipeline = new ReferencePipeline(model.Encoder, fixture.Asset);
                using var runtime = new DiagnosticDecisionV1Runtime(model, fixture.Asset, pipeline);
                pipeline.BeforeScore = _ =>
                {
                    pipeline.BeforeScore = null;
                    expectCode(() => runtime.Evaluate(request, token), "decision_session_busy", "diagnostic same-thread reentry is busy");
                    runtime.Dispose();
                    check(runtime.IsDisposed && !model.IsDisposed && pipeline.DisposeCount == 0,
                        "diagnostic same-thread dispose defers release until evaluation finally");
                };
                check(runtime.Evaluate(request, token).Answers.Count == 3 && model.IsDisposed && pipeline.DisposeCount == 1,
                    "diagnostic deferred dispose completes response and releases exactly once");
                try { runtime.Evaluate(request, token); check(false, "closed diagnostic runtime rejects evaluation"); }
                catch (ObjectDisposedException exception)
                {
                    check(exception.ObjectName == typeof(DiagnosticDecisionV1Runtime).FullName,
                        "diagnostic disposed exception names the public facade");
                }
            },
            () =>
            {
                var model = fixture.Package(token);
                var pipeline = new ReferencePipeline(model.Encoder, fixture.Asset);
                using var runtime = new DiagnosticDecisionV1Runtime(model, fixture.Asset, pipeline);
                pipeline.LogitOverride = [double.NaN, 0];
                expectCode(() => runtime.Evaluate(request, token), "independent_runtime_shape_invalid", "diagnostic rejects NaN fixture seam");
                pipeline.LogitOverride = [0];
                expectCode(() => runtime.Evaluate(request, token), "independent_runtime_shape_invalid", "diagnostic rejects wrong logit count");
                pipeline.LogitOverride = null;
                expectCode(() => runtime.Evaluate(request with { ModelId = "synthetic/other" }, token),
                    "independent_runtime_model_mismatch", "diagnostic request must bind the loaded model");
                expectCode(() => runtime.Evaluate(request with { Questions = [] }, token),
                    "decision_v1_question_invalid", "diagnostic request requires bounded questions");
                expectCode(() => runtime.Evaluate(request with { Questions = [request.Questions[0], request.Questions[0]] }, token),
                    "decision_v1_question_invalid", "diagnostic question ids cannot repeat");
            },
            () =>
            {
                var model = fixture.Package(token);
                var pipeline = new ReferencePipeline(model.Encoder, fixture.Asset) { ThrowOnDispose = true };
                var runtime = new DiagnosticDecisionV1Runtime(model, fixture.Asset, pipeline);
                try { runtime.Dispose(); check(false, "diagnostic cleanup failure propagates"); }
                catch (InvalidOperationException)
                {
                    check(model.IsDisposed && pipeline.DisposeCount == 1 && runtime.IsDisposed,
                        "diagnostic pipeline cleanup failure still clears owned encoder");
                }
                runtime.Dispose();
                check(pipeline.DisposeCount == 1, "diagnostic failed dispose remains idempotent");
                var rejectedModel = fixture.Package(token);
                var rejectedPipeline = new ReferencePipeline(rejectedModel.Encoder, fixture.Asset)
                { ThrowOnDispose = true, AssetHashOverride = new string('E', 64) };
                try
                {
                    _ = new DiagnosticDecisionV1Runtime(rejectedModel, fixture.Asset, rejectedPipeline);
                    check(false, "diagnostic constructor identity and cleanup failure must propagate");
                }
                catch (AggregateException exception)
                {
                    check(exception.InnerExceptions.Count == 2 && exception.InnerExceptions[0] is DecisionException &&
                        exception.InnerExceptions[1] is InvalidOperationException && rejectedModel.IsDisposed && rejectedPipeline.DisposeCount == 1,
                        "diagnostic rejected constructor retains primary and cleanup failures while clearing model");
                }
            },
            () =>
            {
                var model = fixture.Package(token);
                var pipeline = new ReferencePipeline(model.Encoder, fixture.Asset) { BackendFailureAtScore = 3 };
                using var runtime = new DiagnosticDecisionV1Runtime(model, fixture.Asset, pipeline);
                pipeline.BeforeScore = _ => { pipeline.BeforeScore = null; runtime.Dispose(); };
                try { runtime.Evaluate(request, token); check(false, "diagnostic response metadata failure must propagate"); }
                catch (InvalidOperationException exception)
                {
                    check(exception.Message == "fixture backend response failure" && model.IsDisposed && pipeline.DisposeCount == 1,
                        "diagnostic response factory remains inside active evaluation finally and owned lifetime");
                }
            }
        ];
        var maximum = tiny ? 1 : 8;
        for (var index = 0; index < maximum; index++)
        {
            token.ThrowIfCancellationRequested();
            cases[index]();
            check(true, $"diagnostic runtime bounded case {index + 1}/{maximum} completed");
        }
    }

    /// <summary>Private H=2 CPU reference only; injected failures are explicitly fixture seams.</summary>
    private sealed class ReferencePipeline(ModernBertEncoder encoder, DiagnosticHeadAsset asset) : IDiagnosticDecisionPipeline
    {
        private readonly LinearMarkerHead _head = new(asset.Identity.HiddenSize, asset.Head.CopyWeights());
        public DiagnosticHeadIdentity? IdentityOverride { get; set; }
        public string? AssetHashOverride { get; set; }
        public string? DescriptorHashOverride { get; set; }
        public string? BackendOverride { get; set; }
        public int? MaxTokensOverride { get; set; }
        public bool ThrowOnDispose { get; init; }
        public int BackendFailureAtScore { get; init; } = int.MaxValue;
        public double[]? LogitOverride { get; set; }
        public Action<CancellationToken>? BeforeScore { get; set; }
        public int DisposeCount { get; private set; }
        public int ScoreCount { get; private set; }
        public List<MarkerQuestionType> Types { get; } = [];
        public DiagnosticHeadIdentity Identity => IdentityOverride ?? asset.Identity;
        public string DiagnosticAssetSha256 => AssetHashOverride ?? asset.FileSha256;
        public string InitializationDescriptorSha256 => DescriptorHashOverride ?? asset.InitializationDescriptorSha256;
        public string Backend => ScoreCount >= BackendFailureAtScore
            ? throw new InvalidOperationException("fixture backend response failure") : BackendOverride ?? "fixture-diagnostic-real-tiny-cpu";
        public int MaxTokens => MaxTokensOverride ?? encoder.Config.MaxTokens;
        public double[] Score(ReadOnlySpan<int> ids, MarkerQuestionType type, ReadOnlySpan<int> markers,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            ScoreCount++; Types.Add(type); BeforeScore?.Invoke(cancellationToken);
            if (LogitOverride is not null) return LogitOverride;
            IndependentCpuDecisionPipeline.ValidateMarkers(ids.Length, MaxTokens, type, markers, cancellationToken);
            var hidden = encoder.Encode(ids, cancellationToken);
            var features = new float[markers.Length][];
            for (var index = 0; index < features.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                features[index] = hidden.AsSpan(checked(markers[index] * asset.Identity.HiddenSize), asset.Identity.HiddenSize).ToArray();
            }
            return _head.Score(type, features, cancellationToken.ThrowIfCancellationRequested);
        }
        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose) throw new InvalidOperationException("fixture cleanup failure");
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal const string ModelId = "sezika/diagnostic-runtime-fixture";
        private readonly string _directory;
        private readonly DateTime _directoryCreation;
        private readonly List<OwnedFile> _files = [];
        private readonly List<IndependentEncoderModelPackage> _models = [];
        private readonly string _tokenizerPath;
        public DiagnosticHeadAsset Asset { get; private set; } = null!;

        public Fixture(CancellationToken token)
        {
            _directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"sezika-diagnostic-runtime-{Guid.NewGuid():N}"));
            _tokenizerPath = Path.Combine(_directory, "tokenizer.json");
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(_directory)) throw new IOException("Diagnostic fixture directory already exists.");
            Directory.CreateDirectory(_directory);
            _directoryCreation = Directory.GetCreationTimeUtc(_directory);
            try
            {
                var json = new StringBuilder("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<pad>\":0,\"<eos>\":1,\"<bos>\":2,\"<unk>\":3,\"<mask>\":4");
                for (var value = 0; value < 256; value++)
                { token.ThrowIfCancellationRequested(); json.Append($",\"<0x{value:X2}>\":{1000 + value}"); }
                json.Append("},\"merges\":[]}}");
                var tokenizerCreated = false;
                try
                {
                    using var stream = new FileStream(_tokenizerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    tokenizerCreated = true;
                    stream.Write(Encoding.UTF8.GetBytes(json.ToString()));
                }
                finally
                {
                    if (tokenizerCreated) _files.Add(Stamp(_tokenizerPath));
                }
                Asset = DiagnosticHeadAssetStore.CreateNew(Path.Combine(_directory, "head.szdh"),
                    new DiagnosticHeadIdentity(ModelId, new string('A', 64), new string('B', 64), 2), 7, token);
                _files.Add(Stamp(Path.Combine(_directory, "head.szdh")));
            }
            catch (Exception primary)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("Diagnostic fixture construction/cleanup failed.", primary, cleanup); }
                throw;
            }
        }

        public IndependentEncoderModelPackage Package(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_models.Count >= 16) throw new InvalidOperationException("Diagnostic fixture model budget exceeded.");
            var config = new ModernBertConfig { VocabularySize = 2000, HiddenSize = 2, IntermediateSize = 2, LayerCount = 1,
                HeadCount = 1, MaxTokens = 1024, GlobalAttentionEvery = 1, LocalAttention = 2, GlobalRopeTheta = 16000, LocalRopeTheta = 16000 };
            var embeddings = new float[4000]; embeddings[8] = 0.5f; embeddings[9] = -0.5f;
            var model = new IndependentEncoderModelPackage { ModelId = ModelId, SourceModelId = "synthetic/diagnostic-runtime-contract",
                Revision = "fixture-v1", License = "Apache-2.0", EncoderSha256 = new string('A', 64), TokenizerSha256 = new string('B', 64),
                Tokenizer = new TokenizerJson(_tokenizerPath, token), EstimatedResidentBytes = 0,
                Encoder = new ModernBertEncoder(config, new ModernBertWeights { TokenEmbeddings = embeddings, EmbeddingNorm = [1f, 1f], FinalNorm = [1f, 1f],
                    Layers = [new ModernBertLayerWeights { AttentionNorm = null, Qkv = new float[12], AttentionOutput = new float[4], MlpNorm = [1f, 1f],
                        MlpUp = new float[8], MlpDown = new float[4] }] },
                    new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(3) }, token) };
            _models.Add(model);
            return model;
        }

        private static OwnedFile Stamp(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > 131_072) throw new IOException("Diagnostic fixture file budget exceeded.");
            return new OwnedFile(Path.GetFullPath(path), info.CreationTimeUtc, info.Length,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }

        public void Dispose()
        {
            var cleanup = Stopwatch.StartNew();
            for (var index = 0; index < _models.Count && index < 16; index++)
            {
                if (cleanup.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException($"Diagnostic fixture cleanup timed out: {_directory}");
                _models[index].Dispose();
            }
            for (var index = 0; index < _files.Count && index < 2; index++)
            {
                if (cleanup.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException($"Diagnostic fixture cleanup timed out: {_directory}");
                var owned = _files[index];
                if (!File.Exists(owned.Path)) continue;
                var info = new FileInfo(owned.Path);
                if (Path.GetDirectoryName(owned.Path) != _directory || info.CreationTimeUtc != owned.CreationUtc ||
                    info.Length != owned.Length || (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(owned.Path))) != owned.Sha256)
                    throw new IOException($"Diagnostic fixture ownership changed; retained: {owned.Path}");
                File.Delete(owned.Path);
            }
            if (Directory.Exists(_directory))
            {
                if (cleanup.Elapsed > TimeSpan.FromSeconds(5) || Directory.GetCreationTimeUtc(_directory) != _directoryCreation ||
                    (File.GetAttributes(_directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Diagnostic fixture directory ownership changed; retained: {_directory}");
                Directory.Delete(_directory, recursive: false);
            }
        }

        private sealed record OwnedFile(string Path, DateTime CreationUtc, long Length, string Sha256);
    }
}
