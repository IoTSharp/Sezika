using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.Tests;

public static class DecisionModelRuntimeChecks
{
    public static bool Run()
    {
        var tokenizerPath = Path.Combine(Path.GetTempPath(), $"sezika-runtime-tokenizer-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tokenizerPath, "{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"vocab\":{\"<unk>\":0,\"<bos>\":1,\"<eos>\":2,\"<mask>\":3},\"merges\":[],\"unk_token\":\"<unk>\"}}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            using var runtime = new DecisionModelRuntime(CreatePackage(tokenizerPath));
            using var state = JsonDocument.Parse("{\"message\":\"hello\"}");
            using var instructions = JsonDocument.Parse("\"choose\"");
            using var first = JsonDocument.Parse("\"one\"");
            using var second = JsonDocument.Parse("\"two\"");
            var request = new DecisionRequest
            {
                Model = "fixture-modernbert",
                State = state.RootElement.Clone(),
                Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
                {
                    ["choice"] = new ChoiceQuestion
                    {
                        Instructions = instructions.RootElement.Clone(),
                        Criteria = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                        {
                            ["one"] = first.RootElement.Clone(), ["two"] = second.RootElement.Clone(),
                        },
                    },
                },
            };
            var response = runtime.Evaluate(request);
            if (response.Model != "fixture-modernbert" || response.Answers.Count != 1 || response.Answers["choice"] is not ChoiceAnswer)
                return false;
            var wire = Encoding.UTF8.GetBytes("{\"model\":\"fixture-modernbert\",\"state\":{\"message\":\"hello\"},\"questions\":{\"choice\":{\"type\":\"choice\",\"instructions\":\"choose\",\"criteria\":{\"one\":\"one\",\"two\":\"two\"}}}}");
            var parsedResponse = runtime.Evaluate(wire);
            if (parsedResponse.Answers.Count != 1 || parsedResponse.Answers["choice"] is not ChoiceAnswer)
                return false;
            if (!CheckConcurrentDispose(tokenizerPath, request))
                return false;

            var rejectedModel = CreatePackage(tokenizerPath);
            try
            {
                _ = new DecisionModelRuntime(rejectedModel, new DecisionLimits { MaxRequestBytes = 0 });
                return false;
            }
            catch (ArgumentOutOfRangeException) when (rejectedModel.IsDisposed)
            {
            }
            runtime.Dispose();
            try
            {
                _ = runtime.Evaluate(request);
                return false;
            }
            catch (ObjectDisposedException)
            {
                return runtime.IsDisposed;
            }
        }
        finally
        {
            if (File.Exists(tokenizerPath)) File.Delete(tokenizerPath);
        }
    }

    private static bool CheckConcurrentDispose(string tokenizerPath, DecisionRequest request)
    {
        using var model = CreatePackage(tokenizerPath);
        using var engine = new ModernBertDecisionEngine(model, budget: new DecisionResourceBudget { Deadline = TimeSpan.FromSeconds(5) });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var paused = 0;
        model.Encoder.Trace = (_, _) =>
        {
            if (Interlocked.Exchange(ref paused, 1) == 0)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5), cancellation.Token))
                    throw new TimeoutException("The in-flight dispose fixture was not released.");
            }
        };
        var evaluation = Task.Run(() => engine.Evaluate(request, cancellation.Token), cancellation.Token);
        try
        {
            if (!entered.Wait(TimeSpan.FromSeconds(5), cancellation.Token))
                throw new TimeoutException("The in-flight dispose fixture did not enter its encoder.");
            try
            {
                _ = engine.Evaluate(request);
                return false;
            }
            catch (DecisionException exception) when (exception.Code == "decision_session_busy")
            {
            }

            // Unload must reject new work immediately while keeping the model
            // and gate alive until the bounded in-flight evaluation exits.
            engine.Dispose();
            if (model.IsDisposed) return false;
            try
            {
                _ = engine.Evaluate(request);
                return false;
            }
            catch (ObjectDisposedException)
            {
            }
            release.Set();
            var response = evaluation.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token).GetAwaiter().GetResult();
            return response.Answers.Count == 1 && model.IsDisposed && model.Encoder.WorkspacePool.ActiveCount == 0;
        }
        finally
        {
            release.Set();
            cancellation.Cancel();
            try { _ = evaluation.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch (DecisionException exception) when (exception.Code is "decision_cancelled" or "decision_deadline_exceeded") { }
            catch (OperationCanceledException) { }
            model.Encoder.Trace = null;
        }
    }

    private static ModernBertModelPackage CreatePackage(string tokenizerPath)
    {
        const int vocabulary = 16;
        const int hidden = 4;
        const int intermediate = 8;
        var config = new ModernBertConfig
        {
            VocabularySize = vocabulary, HiddenSize = hidden, IntermediateSize = intermediate,
            LayerCount = 1, HeadCount = 2, MaxTokens = 256,
        };
        var encoderLayer = new ModernBertLayerWeights
        {
            AttentionNorm = null,
            Qkv = new float[3 * hidden * hidden], AttentionOutput = new float[hidden * hidden],
            MlpNorm = Ones(hidden), MlpUp = new float[2 * intermediate * hidden],
            MlpDown = new float[hidden * intermediate],
        };
        var encoder = new ModernBertEncoder(config, new ModernBertWeights
        {
            TokenEmbeddings = new float[vocabulary * hidden], EmbeddingNorm = Ones(hidden), FinalNorm = Ones(hidden),
            Layers = [encoderLayer],
        });

        DecisionHeadLayerWeights HeadLayer() => new()
        {
            Qkv = new float[3 * hidden * hidden], QkvBias = new float[3 * hidden],
            AttentionOutput = new float[hidden * hidden], AttentionOutputBias = new float[hidden],
            AttentionNorm = Ones(hidden), AttentionNormBias = new float[hidden],
            MlpUp = new float[4 * hidden * hidden], MlpUpBias = new float[4 * hidden],
            MlpDown = new float[hidden * 4 * hidden], MlpDownBias = new float[hidden],
            MlpNorm = Ones(hidden), MlpNormBias = new float[hidden],
        };
        var head = new DecisionHeadWeights
        {
            TypeEmbeddings = new float[3 * hidden], Layers = [HeadLayer(), HeadLayer()],
            ScorerNorm = Ones(hidden), ScorerNormBias = new float[hidden],
            ScorerDense = new float[hidden * hidden], ScorerDenseBias = new float[hidden],
            ScorerOutput = new float[hidden], ScorerOutputBias = new float[1],
        };
        return new ModernBertModelPackage
        {
            ModelId = "fixture-modernbert", Revision = "fixture-revision", TokenizerRevision = "fixture-tokenizer",
            License = "Apache-2.0", Tokenizer = new TokenizerJson(tokenizerPath), Encoder = encoder, Head = head,
            HeadMaxTokens = 256, Temperature = [1f, 1f, 1f], EstimatedResidentBytes = 0,
        };
    }

    private static float[] Ones(int count) => Enumerable.Repeat(1f, count).ToArray();
}
