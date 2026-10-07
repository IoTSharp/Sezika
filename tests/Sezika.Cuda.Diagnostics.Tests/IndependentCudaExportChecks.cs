using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika;
using Sezika.Cuda;
using Sezika.IndependentModelTool;

namespace Sezika.Cuda.Diagnostics.Tests;

internal static class IndependentCudaExportChecks
{
    public static void Run(CudaDevice device, Action<bool, string> check, CancellationToken cancellationToken, bool tiny = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(80));
        var token = deadline.Token; var clock = Stopwatch.StartNew();
        void Guard() { token.ThrowIfCancellationRequested(); if (clock.Elapsed >= TimeSpan.FromSeconds(80)) throw new TimeoutException("CUDA exporter checks deadline."); }
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sezika-cuda-export-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(directory);
        var initial = device.GetMemorySnapshot();
        TrainingOutputPaths Paths(string name) => new(Path.Combine(directory, name + ".checkpoint"), Path.Combine(directory, name + ".head"), Path.Combine(directory, name + ".json"));
        try
        {
            var json = new StringBuilder("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<pad>\":0,\"<eos>\":1,\"<bos>\":2,\"<unk>\":3,\"<mask>\":4");
            for (var value = 0; value < 256; value++) { Guard(); json.Append($",\"<0x{value:X2}>\":{5 + value}"); }
            json.Append("},\"merges\":[]}}");
            var tokenizerPath = Path.Combine(directory, "tokenizer.json"); File.WriteAllText(tokenizerPath, json.ToString());
            float[] Values(int count, float scale)
            {
                if (count is < 1 or > 4096) throw new InvalidOperationException("Fixture tensor bound.");
                var result = new float[count];
                for (var index = 0; index < count && index < 4096; index++) { Guard(); result[index] = MathF.Sin((index + 1) * 0.11f) * scale; }
                return result;
            }
            var config = new ModernBertConfig { VocabularySize = 261, HiddenSize = 8, IntermediateSize = 8, LayerCount = 1, HeadCount = 2,
                MaxTokens = 256, GlobalAttentionEvery = 1, LocalAttention = 256 };
            using var model = new IndependentEncoderModelPackage { ModelId = "sezika/cuda-export-fixture", SourceModelId = "synthetic/tiny-frozen-encoder",
                Revision = "fixture-v1", License = "Apache-2.0", EncoderSha256 = new string('A', 64), TokenizerSha256 = new string('B', 64),
                Tokenizer = new TokenizerJson(tokenizerPath, token), EstimatedResidentBytes = 0,
                Encoder = new ModernBertEncoder(config, new ModernBertWeights { TokenEmbeddings = Values(261 * 8, 0.1f), EmbeddingNorm = Enumerable.Repeat(1f, 8).ToArray(),
                    FinalNorm = Enumerable.Repeat(1f, 8).ToArray(), Layers = [new ModernBertLayerWeights { AttentionNorm = null, Qkv = Values(192, 0.05f),
                        AttentionOutput = Values(64, 0.02f), MlpNorm = Enumerable.Repeat(1f, 8).ToArray(), MlpUp = Values(128, 0.03f), MlpDown = Values(64, 0.02f) }] },
                    new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(5) }, token) };
            using var state = JsonDocument.Parse("\"x\"");
            IndependentDecisionQuestion[] questions = [new IndependentChoiceQuestion("choice", "choose", [new("b", "second"), new("a", "first")]),
                new IndependentBooleanQuestion("boolean", "judge", "x", "no", "yes"), new IndependentScoreQuestion("score", "rate", ["low", "high"])];
            var request = new IndependentDecisionRequest(1, model.ModelId, state.RootElement.Clone(), questions);
            var identity = new MarkerFeatureIdentity(model.ModelId, model.EncoderSha256, model.TokenizerSha256, new string('C', 64), 8);
            var features = new List<MarkerFeatureExample>(3);
            using (var encoder = new CudaModernBertEncoder(device, config, model.Encoder.Weights, token))
            {
                for (var index = 0; index < (tiny ? 2 : 3) && index < 3; index++)
                {
                    Guard(); device.ResetTelemetry();
                    var feature = IndependentCudaFeatureExporter.Export(model.Tokenizer, encoder, identity, request, questions[index], "record-" + index, "en", 0, token);
                    check(feature.TokenIds.Length > 6 && feature.Features.Length == 2 && feature.Features.All(row => row.Length == 8 && row.All(float.IsFinite)) &&
                        device.GetTelemetry().KernelLaunchCount > 0 && device.GetTelemetry().DeviceToHostBytes == (ulong)(feature.TokenIds.Length * 8 * sizeof(float)),
                        $"CUDA exporter computes and copies complete sequence for {feature.Type} before extracting marker rows");
                    var expected = IndependentMarkerFeatureExporter.Export(model.Tokenizer, model.Encoder, identity, request, questions[index], "record-" + index, "en", 0, cancellationToken: token);
                    check(feature.TokenIds.SequenceEqual(expected.TokenIds) && feature.MarkerPositions.SequenceEqual(expected.MarkerPositions) &&
                        feature.Features.SelectMany(row => row).Zip(expected.Features.SelectMany(row => row)).All(pair => Math.Abs(pair.First - pair.Second) <= 1e-4 + 1e-4 * Math.Abs(pair.Second)),
                        $"CUDA exporter {feature.Type} marker rows match tiny scalar oracle and preserve candidate order");
                    features.Add(feature);
                }
                if (!tiny)
                {
                    using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); device.ResetTelemetry();
                    try { _ = IndependentCudaFeatureExporter.Export(model.Tokenizer, encoder, identity, request, questions[0], "cancel", "en", 0, cancelled.Token); throw new InvalidOperationException("Cancelled export returned."); }
                    catch (OperationCanceledException) { check(device.GetTelemetry().KernelLaunchCount == 0, "Pre-cancelled CUDA exporter launches no kernels"); }
                }
            }
            var releasedEncoder = device.GetMemorySnapshot();
            check(releasedEncoder.OwnedBytes == initial.OwnedBytes && releasedEncoder.LoadedModuleCount == initial.LoadedModuleCount,
                "Frozen CUDA encoder is released before GPU head training begins");
            var train = new MarkerFeatureSet(identity, tiny ? [features[0]] : [features[0], features[2]]);
            var development = new[] { features[1] }; var firstPaths = Paths("first");
            var options = new MarkerTrainingOptions(17, 2, 0.05f, TimeSpan.FromSeconds(10));
            var first = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, new string('D', 64), options, firstPaths, cancellationToken: token);
            check(first.Report.Execution.DataMode == "synthetic_fixture" && !first.Report.Training.UsesRealIndependentEncoder && first.State.CompletedSteps == 2,
                "Tiny synthetic CUDA encoder features reach GPU SGD and schema3 without claiming original-data approval");
            _ = MarkerTrainingCheckpoint.Load(firstPaths.Checkpoint, train, first.Report.Training.CheckpointSha256, token);
            _ = IndependentMarkerHeadAssetStore.Load(firstPaths.Head, identity, first.State.FeatureSha256, first.Report.Training.HeadSha256, token);
            if (!tiny)
            {
                var resume = new CudaTrainingResumePaths(firstPaths.Checkpoint, first.Report.Training.CheckpointSha256, firstPaths.Head, firstPaths.Report, first.ReportSha256);
                var continued = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, new string('D', 64), options with { MaxSteps = 4 }, Paths("continued"), resume, cancellationToken: token);
                var full = IndependentCudaTrainingWorkflow.RunFixture(device, train, development, new string('D', 64), options with { MaxSteps = 4 }, Paths("full"), cancellationToken: token);
                check(continued.Report.Training.CheckpointSha256 == full.Report.Training.CheckpointSha256 && continued.Report.Training.HeadSha256 == full.Report.Training.HeadSha256 &&
                    continued.State.LastLoss == full.State.LastLoss, "CUDA complete-sequence export followed by schema3 2+2 resume equals continuous four steps");
            }
            var final = device.GetMemorySnapshot();
            check(final.OwnedBytes == initial.OwnedBytes && final.OwnedAllocationCount == initial.OwnedAllocationCount &&
                final.LoadedModuleCount == initial.LoadedModuleCount && final.ReleaseFailureCount == initial.ReleaseFailureCount, "CUDA exporter/workflow leaves no owned resources");
            Guard();
        }
        finally
        {
            var owned = Path.GetFullPath(directory);
            if (!owned.StartsWith(Path.GetFullPath(Path.GetTempPath()) + "sezika-cuda-export-", StringComparison.Ordinal)) throw new InvalidOperationException("CUDA fixture cleanup escaped its GUID root.");
            Directory.Delete(owned, recursive: true);
        }
    }
}
