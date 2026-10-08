using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Sezika;
using Sezika.Cuda;

namespace Sezika.Cuda.Diagnostics.Tests;

internal static class DiagnosticCudaChecks
{
    // One six-token/eight-channel/one-layer numerical fixture; CUDA unavailability is a failure.
    public static void Run(CudaDevice device, Action<bool, string> check, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        token.ThrowIfCancellationRequested();
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var prefix = $"sezika-diagnostic-cuda-{Guid.NewGuid():N}";
        string[] paths = [Path.Combine(tempRoot, prefix + "-tokenizer.json"), Path.Combine(tempRoot, prefix + "-head.asset"),
            Path.Combine(tempRoot, prefix + "-mismatch.asset")];
        Console.WriteLine("diagnostic CUDA fixture task paths: " + string.Join("; ", paths));
        var created = new bool[3];
        var creationTimes = new DateTime[3];
        var hashes = new string?[3];
        var cleanupErrors = new List<Exception>(6);
        DiagnosticCudaDecisionPipeline? gpu = null;
        IndependentEncoderModelPackage? model = null;
        Exception? executionError = null;
        var initial = Resources(device);
        var originalProfiling = device.ProfilingEnabled;
        try
        {
            device.ProfilingEnabled = true;
            var tokenizerBytes = Encoding.UTF8.GetBytes("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<pad>\":0,\"<eos>\":1,\"<bos>\":2,\"<unk>\":3,\"<mask>\":4,\"a\":5,\"b\":6,\"c\":7},\"merges\":[]}}");
            hashes[0] = Convert.ToHexString(SHA256.HashData(tokenizerBytes));
            using (var file = new FileStream(paths[0], FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created[0] = true;
                creationTimes[0] = File.GetCreationTimeUtc(paths[0]);
                file.Write(tokenizerBytes);
            }
            var config = new ModernBertConfig { VocabularySize = 8, HiddenSize = 8, IntermediateSize = 8, LayerCount = 1, HeadCount = 2,
                MaxTokens = 8, GlobalAttentionEvery = 1, LocalAttention = 8 };
            float[] Values(int count, float scale)
            {
                if (count is < 1 or > 192) throw new ArgumentOutOfRangeException(nameof(count));
                var values = new float[count];
                for (var index = 0; index < count && index < 192; index++)
                { token.ThrowIfCancellationRequested(); values[index] = MathF.Sin((index + 1) * 0.11f) * scale; }
                return values;
            }
            model = new IndependentEncoderModelPackage { ModelId = "sezika/diagnostic-cuda-fixture", SourceModelId = "synthetic/cuda-numerical-contract",
                Revision = "fixture-v1", License = "Apache-2.0", EncoderSha256 = new string('A', 64), TokenizerSha256 = new string('B', 64),
                Tokenizer = new TokenizerJson(paths[0], token), EstimatedResidentBytes = 0,
                Encoder = new ModernBertEncoder(config, new ModernBertWeights { TokenEmbeddings = Values(64, 0.1f),
                    EmbeddingNorm = Enumerable.Repeat(1f, 8).ToArray(), FinalNorm = Enumerable.Repeat(1f, 8).ToArray(),
                    Layers = [new ModernBertLayerWeights { AttentionNorm = null, Qkv = Values(192, 0.05f), AttentionOutput = Values(64, 0.02f),
                        MlpNorm = Enumerable.Repeat(1f, 8).ToArray(), MlpUp = Values(128, 0.03f), MlpDown = Values(64, 0.02f) }] },
                    new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(5) }, token) };
            var identity = new DiagnosticHeadIdentity(model.ModelId, model.EncoderSha256, model.TokenizerSha256, config.HiddenSize);
            var asset = DiagnosticHeadAssetStore.CreateNew(paths[1], identity, 7, token);
            created[1] = true; creationTimes[1] = File.GetCreationTimeUtc(paths[1]); hashes[1] = asset.FileSha256;
            asset = DiagnosticHeadAssetStore.Load(paths[1], identity, 7, asset.InitializationDescriptorSha256, asset.FileSha256, token);
            var mismatch = DiagnosticHeadAssetStore.CreateNew(paths[2], identity with { EncoderSha256 = new string('F', 64) }, 7, token);
            created[2] = true; creationTimes[2] = File.GetCreationTimeUtc(paths[2]); hashes[2] = mismatch.FileSha256;
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            device.ResetTelemetry();
            try { _ = new DiagnosticCudaDecisionPipeline(device, model, asset, cancelled.Token); check(false, "pre-cancelled diagnostic CUDA constructor must fail"); }
            catch (OperationCanceledException) { check(device.GetTelemetry().KernelLaunchCount == 0 && initial == Resources(device),
                "pre-cancelled diagnostic constructor acquires no native resources"); }
            try { _ = new DiagnosticCudaDecisionPipeline(device, model, mismatch, token); check(false, "diagnostic CUDA encoder identity mismatch must fail"); }
            catch (DecisionException error) { check(error.Code == "diagnostic_runtime_identity_mismatch" && initial == Resources(device),
                "diagnostic CUDA identity mismatch fails before native acquisition"); }
            gpu = new DiagnosticCudaDecisionPipeline(device, model, asset, token);
            check(gpu.Identity == asset.Identity && gpu.DiagnosticAssetSha256 == asset.FileSha256 &&
                gpu.InitializationDescriptorSha256 == asset.InitializationDescriptorSha256 &&
                asset.CompletedUpdates == 0 && asset.ObservedLoss is null && asset.Origin == DiagnosticHeadOrigin.UntrainedDiagnostic,
                "diagnostic CUDA retains zero-update typed identity and actual SZDH1 hashes");
            var resident = Resources(device);
            int[] ids = [2, 3, 4, 5, 6, 1];
            int[] markers = [1, 3];
            var hidden = model.Encoder.Encode(ids, token);
            float[][] markerFeatures = [hidden.AsSpan(markers[0] * config.HiddenSize, config.HiddenSize).ToArray(),
                hidden.AsSpan(markers[1] * config.HiddenSize, config.HiddenSize).ToArray()];
            var rows = new List<double[]>(3);
            MarkerQuestionType[] types = [MarkerQuestionType.Choice, MarkerQuestionType.Score, MarkerQuestionType.Boolean];
            for (var index = 0; index < types.Length && index < 3; index++)
            {
                token.ThrowIfCancellationRequested();
                Console.WriteLine($"diagnostic CUDA checks: GPU forward {index + 1}/3 ({types[index]})");
                var expected = asset.Head.Score(types[index], markerFeatures, token.ThrowIfCancellationRequested);
                device.ResetTelemetry();
                var actual = gpu.Score(ids, types[index], markers, token);
                check(actual.Length == 2 && actual.Zip(expected).All(pair => double.IsFinite(pair.First) &&
                    Math.Abs(pair.First - pair.Second) <= 1e-5 + 1e-4 * Math.Abs(pair.Second)),
                    $"diagnostic {types[index]} actual CUDA logits match the tiny scalar encoder/seeded-head reference");
                check(device.GetTelemetry().DeviceToHostBytes == (ulong)(ids.Length * 3 * sizeof(float)),
                    $"diagnostic {types[index]} copies only three logits per token, retaining encoder hidden on GPU");
                check(resident == Resources(device), $"diagnostic {types[index]} releases per-request buffers");
                rows.Add(actual);
            }
            check(rows.SelectMany(row => row).Any(value => Math.Abs(value) > 1e-7) && !rows[0].SequenceEqual(rows[1]),
                "diagnostic GPU fixture exercises nonzero and distinct seeded head rows");
            device.ResetTelemetry();
            try { gpu.Score(ids, MarkerQuestionType.Boolean, markers, cancelled.Token); check(false, "pre-cancelled diagnostic CUDA score must fail"); }
            catch (OperationCanceledException) { check(device.GetTelemetry().KernelLaunchCount == 0, "pre-cancelled diagnostic CUDA score launches nothing"); }
            try { gpu.Score(ids, MarkerQuestionType.Boolean, [3, 1], token); check(false, "unordered diagnostic CUDA markers must fail"); }
            catch (DecisionException error) { check(error.Code == "diagnostic_pipeline_input_invalid" && device.GetTelemetry().KernelLaunchCount == 0,
                "invalid diagnostic markers fail before any kernel launch"); }
            check(gpu.Score(ids, MarkerQuestionType.Boolean, markers, token).SequenceEqual(rows[2]),
                "diagnostic CUDA recovers deterministically after rejected inputs");
            var disposed = gpu;
            disposed.Dispose(); gpu = null;
            check(initial == Resources(device) && !model.IsDisposed,
                "diagnostic CUDA releases its resident owners and retains caller model/device");
            device.Synchronize();
            disposed.Dispose();
            try { disposed.Score(ids, MarkerQuestionType.Boolean, markers, token); check(false, "disposed diagnostic CUDA pipeline must fail"); }
            catch (ObjectDisposedException) { check(true, "disposed diagnostic CUDA pipeline rejects forward and repeated unload is safe"); }
        }
        catch (Exception error) { executionError = error; throw; }
        finally
        {
            // Six explicit owners, independent from the cancelled working token. No recursive cleanup.
            var cleanupClock = Stopwatch.StartNew();
            void Cleanup(string ownerDescription, Action action)
            {
                if (cleanupClock.Elapsed >= TimeSpan.FromSeconds(20))
                { cleanupErrors.Add(new TimeoutException("Diagnostic CUDA fixture cleanup budget expired; retained owner: " + ownerDescription)); return; }
                try { action(); }
                catch (Exception error) { cleanupErrors.Add(error); }
            }
            Cleanup("fixture CUDA pipeline", () => gpu?.Dispose());
            Cleanup("fixture CPU model", () => model?.Dispose());
            Cleanup("caller profiling setting", () => device.ProfilingEnabled = originalProfiling);
            for (var index = 0; index < paths.Length && index < 3; index++)
            {
                var item = index;
                Cleanup(paths[item], () =>
                {
                    if (!created[item]) return;
                    var path = Path.GetFullPath(paths[item]);
                    var info = new FileInfo(path); info.Refresh();
                    if (!info.Exists) return;
                    if (Path.GetDirectoryName(path) != tempRoot || !Path.GetFileName(path).StartsWith(prefix + "-", StringComparison.Ordinal) ||
                        info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        info.CreationTimeUtc != creationTimes[item] || info.Length is < 1 or > 128 * 1024 || hashes[item] is null ||
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != hashes[item])
                        throw new IOException("Diagnostic CUDA fixture path ownership differs; retained path: " + path);
                    File.Delete(path);
                });
            }
            if (cleanupErrors.Count > 0)
                throw new AggregateException("Diagnostic CUDA fixture cleanup failed; inspect owned paths.",
                    executionError is null ? cleanupErrors : new[] { executionError }.Concat(cleanupErrors));
        }
    }

    private static (ulong Bytes, int Buffers, int Modules, long ReleaseFailures) Resources(CudaDevice device)
    {
        var snapshot = device.GetMemorySnapshot();
        return (snapshot.OwnedBytes, snapshot.OwnedAllocationCount, snapshot.LoadedModuleCount, snapshot.ReleaseFailureCount);
    }
}
