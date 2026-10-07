using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.Tests;

internal static class IndependentPipelineChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string, string> expectCode)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(tempRoot, $"sezika-independent-pipeline-{Guid.NewGuid():N}.json"));
        var created = false;
        try
        {
            var json = new StringBuilder("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<pad>\":0,\"<eos>\":1,\"<bos>\":2,\"<unk>\":3,\"<mask>\":4");
            for (var value = 0; value < 256; value++)
            {
                token.ThrowIfCancellationRequested(); json.Append($",\"<0x{value:X2}>\":{1000 + value}");
            }
            json.Append("},\"merges\":[]}}");
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { created = true; file.Write(Encoding.UTF8.GetBytes(json.ToString())); }
            using var state = JsonDocument.Parse("\"x\"");
            var request = new IndependentDecisionRequest(1, "sezika/pipeline-fixture", state.RootElement.Clone(),
            [
                new IndependentChoiceQuestion("choice", "choose", [new("a", "first"), new("b", "second")]),
                new IndependentScoreQuestion("score", "rate", ["low", "high"]),
                new IndependentBooleanQuestion("boolean", "judge", "x", "no", "yes")
            ]);
            var model = Package(path, token);
            var head = Head();
            var pipeline = new RecordingPipeline(model.Encoder, head);
            using (var runtime = new IndependentDecisionV1Runtime(model, head, new IndependentDecisionRuntimeOptions(), pipeline))
            {
                var answer = runtime.Evaluate(request, token);
                check(answer.Answers.Count == 3 && pipeline.Types.SequenceEqual([MarkerQuestionType.Choice, MarkerQuestionType.Score, MarkerQuestionType.Boolean]),
                    "independent injectable pipeline preserves typed dispatch across three primitives");
                check(answer.Backend == pipeline.Backend && answer.EncoderSha256 == model.EncoderSha256 &&
                    answer.TokenizerSha256 == model.TokenizerSha256 && answer.Answers.All(item => item.Probabilities.All(double.IsFinite) &&
                        Math.Abs(item.Probabilities.Sum() - 1) < 1e-12), "injected real tiny CPU pipeline reports provenance and finite uncalibrated distributions");
                check(answer.Answers[0].CandidateIds.SequenceEqual(["a", "b"]) && answer.Answers[2].CandidateIds.SequenceEqual(["false", "true"]),
                    "injected independent runtime preserves choice and boolean candidate order");
                pipeline.BeforeScore = () =>
                {
                    pipeline.BeforeScore = null;
                    expectCode(() => runtime.Evaluate(request, token), "decision_session_busy", "independent runtime rejects same-thread evaluation reentry");
                };
                _ = runtime.Evaluate(request, token);
                pipeline.Override = [double.NaN, 0];
                expectCode(() => runtime.Evaluate(request, token), "independent_runtime_shape_invalid", "runtime rejects nonfinite injected fixture logits");
                pipeline.Override = [0];
                expectCode(() => runtime.Evaluate(request, token), "independent_runtime_shape_invalid", "runtime rejects wrong injected fixture logit count");
                pipeline.Override = null;
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                try { runtime.Evaluate(request, cancelled.Token); check(false, "pre-cancelled independent runtime must fail"); }
                catch (OperationCanceledException) { check(true, "independent injected runtime honors pre-cancellation"); }
            }
            check(model.IsDisposed && pipeline.DisposeCount == 1 && model.Encoder.Weights.EmbeddingNorm.All(value => value == 0),
                "runtime disposes injected independent pipeline and clears owned model tensors");

            var mismatchModel = Package(path, token);
            var mismatch = new RecordingPipeline(mismatchModel.Encoder, head) { IdentityOverride = head.Identity with { DataManifestSha256 = new string('F', 64) } };
            expectCode(() => new IndependentDecisionV1Runtime(mismatchModel, head, new IndependentDecisionRuntimeOptions(), mismatch),
                "independent_runtime_identity_mismatch", "runtime compares complete injected data identity");
            check(mismatchModel.IsDisposed && mismatch.DisposeCount == 1, "rejected pipeline identity releases both owned resources");

            var throwModel = Package(path, token);
            var throwing = new RecordingPipeline(throwModel.Encoder, head) { ThrowOnDispose = true };
            var throwRuntime = new IndependentDecisionV1Runtime(throwModel, head, new IndependentDecisionRuntimeOptions(), throwing);
            try { throwRuntime.Dispose(); check(false, "fixture cleanup exception must propagate"); }
            catch (InvalidOperationException) { check(throwModel.IsDisposed && throwing.DisposeCount == 1, "pipeline disposal failure still clears the owned independent model"); }
            throwRuntime.Dispose();
            check(throwing.DisposeCount == 1 && throwRuntime.IsDisposed, "failed disposal is idempotent and leaves session closed");

            var deferredModel = Package(path, token);
            var deferredPipeline = new RecordingPipeline(deferredModel.Encoder, head);
            using var deferredRuntime = new IndependentDecisionV1Runtime(deferredModel, head, new IndependentDecisionRuntimeOptions(), deferredPipeline);
            deferredPipeline.BeforeScore = () =>
            {
                deferredPipeline.BeforeScore = null;
                deferredRuntime.Dispose();
                check(deferredRuntime.IsDisposed && !deferredModel.IsDisposed && deferredPipeline.DisposeCount == 0,
                    "same-thread dispose closes runtime and defers active pipeline/model release");
            };
            _ = deferredRuntime.Evaluate(request, token);
            check(deferredModel.IsDisposed && deferredPipeline.DisposeCount == 1,
                "active evaluation finally releases deferred independent pipeline/model exactly once");

            using var standaloneModel = Package(path, token);
            using var standalone = new IndependentCpuDecisionPipeline(standaloneModel.Encoder, head);
            standaloneModel.Encoder.Trace = (_, _) =>
            {
                standaloneModel.Encoder.Trace = null;
                expectCode(standalone.Dispose, "decision_session_busy", "active public CPU pipeline rejects unload without releasing its borrowed encoder");
                expectCode(() => standalone.Score([2, 4, 4, 1], MarkerQuestionType.Boolean, [1, 2], token),
                    "decision_session_busy", "public CPU pipeline rejects nested forward while its gate is held");
            };
            var standaloneResult = standalone.Score([2, 4, 4, 1], MarkerQuestionType.Boolean, [1, 2], token);
            check(standaloneResult.Length == 2 && !standaloneModel.IsDisposed,
                "public CPU pipeline completes and retains caller encoder after busy unload attempt");
        }
        finally
        {
            if (created && Path.GetDirectoryName(path) == tempRoot && Path.GetFileName(path).StartsWith("sezika-independent-pipeline-", StringComparison.Ordinal))
                File.Delete(path);
        }
    }

    private sealed class RecordingPipeline(ModernBertEncoder encoder, IndependentMarkerHeadAsset head) : IIndependentDecisionPipeline
    {
        private readonly IndependentCpuDecisionPipeline _inner = new(encoder, head);
        public MarkerFeatureIdentity? IdentityOverride { get; init; }
        public double[]? Override { get; set; }
        public bool ThrowOnDispose { get; init; }
        public Action? BeforeScore { get; set; }
        public int DisposeCount { get; private set; }
        public List<MarkerQuestionType> Types { get; } = [];
        public MarkerFeatureIdentity Identity => IdentityOverride ?? _inner.Identity;
        public string FeatureSha256 => _inner.FeatureSha256;
        public string HeadSha256 => _inner.HeadSha256;
        public string Backend => "fixture-recording-real-cpu-pipeline";
        public int MaxTokens => _inner.MaxTokens;
        public double[] Score(ReadOnlySpan<int> ids, MarkerQuestionType type, ReadOnlySpan<int> markers, CancellationToken cancellationToken = default)
        {
            Types.Add(type);
            BeforeScore?.Invoke();
            return Override ?? _inner.Score(ids, type, markers, cancellationToken);
        }
        public void Dispose()
        {
            DisposeCount++; _inner.Dispose();
            if (ThrowOnDispose) throw new InvalidOperationException("fixture cleanup failure");
        }
    }

    private static IndependentMarkerHeadAsset Head() => new(new MarkerFeatureIdentity("sezika/pipeline-fixture", new string('A', 64),
        new string('B', 64), new string('D', 64), 2), new string('E', 64), 7, 0.1f, 1, 0.5,
        new LinearMarkerHead(2, [0.1f, -0.1f, -0.2f, 0.3f, 0.4f, -0.5f]), new string('C', 64));

    private static IndependentEncoderModelPackage Package(string tokenizerPath, CancellationToken token)
    {
        var config = new ModernBertConfig { VocabularySize = 2000, HiddenSize = 2, IntermediateSize = 2, LayerCount = 1,
            HeadCount = 1, MaxTokens = 1024, GlobalAttentionEvery = 1, LocalAttention = 2, GlobalRopeTheta = 16000, LocalRopeTheta = 16000 };
        var embeddings = new float[4000];
        embeddings[8] = 0.5f; embeddings[9] = -0.5f;
        return new IndependentEncoderModelPackage { ModelId = "sezika/pipeline-fixture", SourceModelId = "synthetic/pipeline-contract",
            Revision = "fixture-v1", License = "Apache-2.0", EncoderSha256 = new string('A', 64), TokenizerSha256 = new string('B', 64),
            Tokenizer = new TokenizerJson(tokenizerPath, token), EstimatedResidentBytes = 0,
            Encoder = new ModernBertEncoder(config, new ModernBertWeights { TokenEmbeddings = embeddings, EmbeddingNorm = [1f, 1f], FinalNorm = [1f, 1f],
                Layers = [new ModernBertLayerWeights { AttentionNorm = null, Qkv = new float[12], AttentionOutput = new float[4], MlpNorm = [1f, 1f],
                    MlpUp = new float[8], MlpDown = new float[4] }] }, new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(3) }, token) };
    }
}
