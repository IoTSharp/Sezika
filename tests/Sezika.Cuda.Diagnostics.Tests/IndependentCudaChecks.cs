using System.Text;
using Sezika;
using Sezika.Cuda;

namespace Sezika.Cuda.Diagnostics.Tests;

internal static class IndependentCudaChecks
{
    // Fixed six tokens, eight hidden channels and one encoder layer. Actual
    // NVIDIA Driver execution is required; unavailable CUDA is a real failure.
    public static void Run(CudaDevice device, Action<bool, string> check, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        token.ThrowIfCancellationRequested();
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(tempRoot, $"sezika-independent-cuda-{Guid.NewGuid():N}.json"));
        var created = false;
        var initial = device.GetMemorySnapshot();
        var originalProfiling = device.ProfilingEnabled;
        try
        {
            device.ProfilingEnabled = true;
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                // This numerical check supplies token IDs directly. The minimal
                // BPE fixture keeps the required byte-fallback contract; a full
                // byte vocabulary is unnecessary because Encode is never called.
                file.Write(Encoding.UTF8.GetBytes("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<pad>\":0,\"<eos>\":1,\"<bos>\":2,\"<unk>\":3,\"<mask>\":4,\"a\":5,\"b\":6,\"c\":7},\"merges\":[]}}"));
            }
            var config = new ModernBertConfig { VocabularySize = 8, HiddenSize = 8, IntermediateSize = 8, LayerCount = 1, HeadCount = 2,
                MaxTokens = 8, GlobalAttentionEvery = 1, LocalAttention = 8 };
            float[] Values(int count, float scale) => Enumerable.Range(0, count).Select(index =>
            { token.ThrowIfCancellationRequested(); return MathF.Sin((index + 1) * 0.11f) * scale; }).ToArray();
            using var model = new IndependentEncoderModelPackage { ModelId = "sezika/cuda-fixture", SourceModelId = "synthetic/cuda-numerical-contract",
                Revision = "fixture-v1", License = "Apache-2.0", EncoderSha256 = new string('A', 64), TokenizerSha256 = new string('B', 64),
                Tokenizer = new TokenizerJson(path, token), EstimatedResidentBytes = 0,
                Encoder = new ModernBertEncoder(config, new ModernBertWeights { TokenEmbeddings = Values(64, 0.1f),
                    EmbeddingNorm = Enumerable.Repeat(1f, 8).ToArray(), FinalNorm = Enumerable.Repeat(1f, 8).ToArray(),
                    Layers = [new ModernBertLayerWeights { AttentionNorm = null, Qkv = Values(192, 0.05f), AttentionOutput = Values(64, 0.02f),
                        MlpNorm = Enumerable.Repeat(1f, 8).ToArray(), MlpUp = Values(128, 0.03f), MlpDown = Values(64, 0.02f) }] },
                    new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(5) }, token) };
            var head = new IndependentMarkerHeadAsset(new MarkerFeatureIdentity(model.ModelId, model.EncoderSha256, model.TokenizerSha256,
                new string('D', 64), 8), new string('E', 64), 7, 0.1f, 1, 0.5d,
                new LinearMarkerHead(8, Values(24, 0.2f)), new string('C', 64));
            using var cpu = new IndependentCpuDecisionPipeline(model.Encoder, head);
            var gpu = new IndependentCudaDecisionPipeline(device, model, head, token);
            try
            {
                var resident = device.GetMemorySnapshot();
                int[] ids = [2, 3, 4, 5, 6, 1];
                int[] markers = [1, 3];
                var rows = new List<double[]>();
                foreach (var type in new[] { MarkerQuestionType.Choice, MarkerQuestionType.Score, MarkerQuestionType.Boolean })
                {
                    token.ThrowIfCancellationRequested();
                    var expected = cpu.Score(ids, type, markers, token);
                    device.ResetTelemetry();
                    var actual = gpu.Score(ids, type, markers, token);
                    check(actual.Length == 2 && actual.Zip(expected).All(pair => double.IsFinite(pair.First) &&
                        Math.Abs(pair.First - pair.Second) <= 1e-4 + 1e-4 * Math.Abs(pair.Second)), $"independent {type} CUDA logits match scalar CPU tiny encoder/head");
                    check(device.GetTelemetry().DeviceToHostBytes == (ulong)(ids.Length * 3 * sizeof(float)),
                        $"independent {type} CUDA reads back three logits per token and keeps encoder hidden resident");
                    check(device.GetMemorySnapshot().OwnedBytes == resident.OwnedBytes &&
                        device.GetMemorySnapshot().OwnedAllocationCount == resident.OwnedAllocationCount,
                        $"independent {type} CUDA releases request working allocations");
                    rows.Add(actual);
                }
                check(rows.SelectMany(row => row).Any(value => Math.Abs(value) > 1e-4) && !rows[0].SequenceEqual(rows[1]),
                    "independent CUDA fixture exercises nonzero distinct trainable head rows");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                device.ResetTelemetry();
                try { gpu.Score(ids, MarkerQuestionType.Boolean, markers, cancelled.Token); check(false, "pre-cancelled independent CUDA score must fail"); }
                catch (OperationCanceledException) { check(device.GetTelemetry().KernelLaunchCount == 0, "pre-cancelled independent CUDA score launches nothing"); }
                try { gpu.Score(ids, MarkerQuestionType.Boolean, [3, 1], token); check(false, "unordered independent CUDA markers must fail"); }
                catch (DecisionException error) { check(error.Code == "independent_pipeline_input_invalid" && device.GetTelemetry().KernelLaunchCount == 0,
                    "invalid independent CUDA markers fail before launching"); }
                var recovered = gpu.Score(ids, MarkerQuestionType.Boolean, markers, token);
                check(recovered.SequenceEqual(rows[2]), "independent CUDA recovers deterministically after pre-cancellation and invalid markers");
            }
            finally { gpu.Dispose(); }
            var released = device.GetMemorySnapshot();
            check(released.OwnedBytes == initial.OwnedBytes && released.OwnedAllocationCount == initial.OwnedAllocationCount &&
                released.LoadedModuleCount == initial.LoadedModuleCount && released.ReleaseFailureCount == initial.ReleaseFailureCount,
                "independent CUDA unload releases its encoder/head allocations/modules and retains caller device");
            gpu.Dispose();
            try { gpu.Score([2, 4, 1], MarkerQuestionType.Boolean, [0, 1], token); check(false, "disposed independent CUDA pipeline must fail"); }
            catch (ObjectDisposedException) { check(true, "disposed independent CUDA pipeline rejects forward and repeated unload is safe"); }
        }
        finally
        {
            try { device.ProfilingEnabled = originalProfiling; }
            finally
            {
                if (created && Path.GetDirectoryName(path) == tempRoot && Path.GetFileName(path).StartsWith("sezika-independent-cuda-", StringComparison.Ordinal)) File.Delete(path);
            }
        }
    }
}
