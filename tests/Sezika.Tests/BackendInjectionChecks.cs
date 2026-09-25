using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.Tests;

/// <summary>Contract fixtures only: fixed logits below are not real model inference evidence.</summary>
public static class BackendInjectionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var tokenizerPath = Path.GetFullPath(Path.Combine(temporaryRoot, $"sezika-backend-fixture-{Guid.NewGuid():N}.json"));
        if (!string.Equals(Path.GetDirectoryName(tokenizerPath), temporaryRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture tokenizer path escaped its temporary directory.");
        var created = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using (var file = new FileStream(tokenizerPath, FileMode.CreateNew, FileAccess.Write))
            {
                created = true;
                file.Write(Encoding.UTF8.GetBytes("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"vocab\":{\"<unk>\":0,\"<bos>\":1,\"<eos>\":2,\"<mask>\":3,\"▁\":4,\"a\":5,\"b\":6,\"s\":7,\"q\":8},\"merges\":[],\"unk_token\":\"<unk>\"}}"));
            }
            var request = Request();
            var model = Package(tokenizerPath, deadline.Token);
            var backend = new FixturePipeline();
            using (var engine = new ModernBertDecisionEngine(model, backend, "fixture-local-marker-backend"))
            {
                var response = engine.Evaluate(request, deadline.Token);
                check(response.Backend == "fixture-local-marker-backend" && response.Model == model.ModelId &&
                    response.ModelRevision == model.Revision && response.TokenizerRevision == model.TokenizerRevision,
                    "injected backend preserves model provenance and backend identity");
                check(backend.Calls.Count == 3 && backend.Calls.Select(call => call.TypeId).SequenceEqual([0, 1, 2]),
                    "shared typed engine dispatches choice/score/boolean type IDs");
                var choice = response.Answers["choice"] as ChoiceAnswer;
                var score = response.Answers["score"] as ScoreAnswer;
                var boolean = response.Answers["boolean"] as BooleanAnswer;
                check(choice is not null && choice.Logits is not null && choice.Choice == "a" && choice.Logits["z"] == 0d && choice.Logits["a"] == 2d &&
                    Near(choice.Probabilities["a"], Math.Exp(2) / (1 + Math.Exp(2))),
                    "injected fixture logits preserve input choice order and choice temperature");
                check(score is not null && Near(score.Score, Math.Exp(1) / (1 + Math.Exp(1))) &&
                    score.Legend["0"].GetString() == "b" && score.Legend["1"].GetString() == "a",
                    "injected fixture logits preserve score order and expected-value semantics");
                check(boolean is not null && boolean.Logits is not null && Near(boolean.ProbabilityTrue, Math.Exp(0.5) / (1 + Math.Exp(0.5))) &&
                    boolean.Logits["false"] == 0d && boolean.Logits["true"] == 2d,
                    "injected fixture logits map false/true markers and true probability through temperature");
                check(response.Answers.Values.All(answer => answer.Calibration.Status == "uncalibrated" && answer.Status == "answered"),
                    "injected fixture predictions remain explicitly uncalibrated");
                check(MatchesPrompt(backend.Calls[0], model.Tokenizer, "choice", "z: b", "a: a") &&
                    MatchesPrompt(backend.Calls[1], model.Tokenizer, "score", "level 0: b", "level 1: a") &&
                    MatchesPrompt(backend.Calls[2], model.Tokenizer, "noul", "false: b", "true: a"),
                    "all injected backend calls receive shared BOS/prompt/markers/criteria/state/EOS tokens");
                check(response.Usage is not null && response.Usage.QuestionCount == 3 && response.Usage.MicroBatchCount == 3 &&
                    response.Usage.TokenCount == backend.Calls.Sum(call => call.Tokens.Length),
                    "shared typed usage accounts injected backend calls");
            }
            check(backend.DisposeCount == 1 && model.IsDisposed && model.Encoder.Weights.EmbeddingNorm.All(value => value == 0f),
                "typed session owns and releases injected pipeline and model tensors");

            CheckThrowingDispose(tokenizerPath, request, check, deadline.Token);
            CheckConstructorRejection(tokenizerPath, check, deadline.Token, throwOnDispose: false);
            CheckConstructorRejection(tokenizerPath, check, deadline.Token, throwOnDispose: true);
            CheckTokenBoundary(tokenizerPath, check, deadline.Token);
        }
        finally
        {
            // Only this exact CreateNew-owned fixture file is removed.
            if (created) File.Delete(tokenizerPath);
        }
    }

    private static void CheckThrowingDispose(string tokenizerPath, DecisionRequest request, Action<bool, string> check, CancellationToken cancellationToken)
    {
        var model = Package(tokenizerPath, cancellationToken);
        var backend = new FixturePipeline { ThrowOnDispose = true };
        var engine = new ModernBertDecisionEngine(model, backend, "fixture-throwing-dispose");
        try
        {
            engine.Dispose();
            throw new InvalidOperationException("Fixture disposal failure was not propagated.");
        }
        catch (FixtureDisposeException)
        {
            check(backend.DisposeCount == 1 && model.IsDisposed && model.Encoder.Weights.EmbeddingNorm.All(value => value == 0f),
                "pipeline disposal failure still unloads owned model tensors");
        }
        finally
        {
            // The first disposal failure must still complete the lifecycle;
            // repeating Dispose must neither rethrow nor call the backend twice.
            engine.Dispose();
        }
        check(backend.DisposeCount == 1, "disposal failure leaves typed session disposal idempotent");
        try
        {
            engine.Evaluate(request, cancellationToken);
            throw new InvalidOperationException("Disposed fixture session accepted a request.");
        }
        catch (ObjectDisposedException)
        {
            check(true, "typed session rejects requests after a backend disposal failure");
        }
    }

    private static void CheckConstructorRejection(string tokenizerPath, Action<bool, string> check, CancellationToken cancellationToken, bool throwOnDispose)
    {
        var model = Package(tokenizerPath, cancellationToken);
        var backend = new FixturePipeline { ThrowOnDispose = throwOnDispose };
        try
        {
            using var unexpected = new ModernBertDecisionEngine(model, backend, "fixture-budget-rejection",
                budget: new DecisionResourceBudget { MaxResidentBytes = 1 });
            throw new InvalidOperationException("Fixture resident budget should have rejected construction.");
        }
        catch (DecisionException exception) when (!throwOnDispose && exception.Code == "decision_model_memory_limit_exceeded")
        {
            check(backend.DisposeCount == 1 && model.IsDisposed, "constructor budget rejection releases injected pipeline and model");
        }
        catch (FixtureDisposeException) when (throwOnDispose)
        {
            check(backend.DisposeCount == 1 && model.IsDisposed, "constructor cleanup unloads model even when injected pipeline disposal throws");
        }
        finally
        {
            model.Dispose();
        }
    }

    private static void CheckTokenBoundary(string tokenizerPath, Action<bool, string> check, CancellationToken cancellationToken)
    {
        var tokenizer = new TokenizerJson(tokenizerPath, cancellationToken);
        var first = BoundaryRequest(1);
        var question = (BooleanQuestion)first.Questions["decision"];
        var options = new PromptSequenceOptions { TotalTokenBudget = 2048 };
        var baseLength = PromptSequenceBuilder.Build(tokenizer, first.State, question, options, cancellationToken).TokenIds.Length;
        var stateLength = 1025 - baseLength;
        if (stateLength is < 1 or > 1024) throw new InvalidOperationException("Unexpected fixture token length.");
        var atLimit = BoundaryRequest(stateLength);
        var oneOver = BoundaryRequest(stateLength + 1);
        var twoOver = BoundaryRequest(stateLength + 2);
        int Length(DecisionRequest request) => PromptSequenceBuilder.Build(tokenizer, request.State,
            request.Questions["decision"], options, cancellationToken).TokenIds.Length;
        check(Length(atLimit) == 1024 && Length(oneOver) == 1025 && Length(twoOver) == 1026,
            "shared marker sequence measures exact 1024/1025/1026 token boundary independently of prefix budget");

        var model = Package(tokenizerPath, cancellationToken);
        var pipeline = new FixturePipeline();
        using var engine = new ModernBertDecisionEngine(model, pipeline, "fixture-token-boundary");
        var response = engine.Evaluate(atLimit, cancellationToken);
        check(response.Usage?.TokenCount == 1024 && pipeline.Calls.Count == 1,
            "production engine accepts exactly 1024 tokens with a 256-token prefix budget");
        try
        {
            engine.Evaluate(oneOver, cancellationToken);
            throw new InvalidOperationException("1025-token strict fixture was accepted.");
        }
        catch (DecisionException exception) when (exception.Code == "decision_token_budget_exceeded")
        {
            check(true, "1025-token strict input reports the production budget code");
        }
        try
        {
            engine.Evaluate(twoOver, cancellationToken);
            throw new InvalidOperationException("1026-token strict fixture was accepted.");
        }
        catch (DecisionException exception) when (exception.Code == "decision_token_budget_exceeded")
        {
            check(pipeline.Calls.Count == 1, "over-budget strict input rejects before backend execution");
        }
        var compatible = engine.Evaluate(oneOver with { LengthPolicy = PromptLengthPolicy.LayaCompatible }, cancellationToken);
        check(compatible.Usage?.TokenCount == 1024 && pipeline.Calls.Count == 2 &&
            compatible.Answers["decision"].InputDiagnostics?.DroppedStateTokens == 1,
            "explicit compatibility mode reports the one discarded state token");
    }

    private static DecisionRequest BoundaryRequest(int stateLength)
    {
        using var state = JsonDocument.Parse($"{{\"s\":\"{new string('a', stateLength)}\"}}");
        using var instruction = JsonDocument.Parse("\"q\"");
        using var a = JsonDocument.Parse("\"a\"");
        using var b = JsonDocument.Parse("\"b\"");
        return new DecisionRequest
        {
            Model = "fixture-backend-injection", State = state.RootElement.Clone(),
            Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
            {
                ["decision"] = new BooleanQuestion
                {
                    Instructions = instruction.RootElement.Clone(),
                    Criteria = new BooleanCriteria { WhenTrue = a.RootElement.Clone(), WhenFalse = b.RootElement.Clone() },
                },
            },
        };
    }

    private static bool MatchesPrompt(Call call, TokenizerJson tokenizer, string type, string first, string second)
    {
        var expected = new List<int> { tokenizer.BosId };
        expected.AddRange(tokenizer.Encode($"{type} question: q", 256, addSpecialTokens: false));
        expected.Add(tokenizer.EosId);
        var firstMarker = expected.Count;
        expected.Add(tokenizer.MaskId);
        expected.AddRange(tokenizer.Encode(" " + first, 256, addSpecialTokens: false));
        var secondMarker = expected.Count;
        expected.Add(tokenizer.MaskId);
        expected.AddRange(tokenizer.Encode(" " + second, 256, addSpecialTokens: false));
        expected.Add(tokenizer.EosId);
        expected.AddRange(tokenizer.Encode("{\"s\": \"a\"}", 256, addSpecialTokens: false));
        expected.Add(tokenizer.EosId);
        return call.Tokens.SequenceEqual(expected) && call.Markers.SequenceEqual([firstMarker, secondMarker]);
    }

    private static DecisionRequest Request()
    {
        using var state = JsonDocument.Parse("{\"s\":\"a\"}");
        using var instruction = JsonDocument.Parse("\"q\"");
        using var a = JsonDocument.Parse("\"a\"");
        using var b = JsonDocument.Parse("\"b\"");
        return new DecisionRequest
        {
            Model = "fixture-backend-injection", State = state.RootElement.Clone(),
            Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
            {
                ["choice"] = new ChoiceQuestion { Instructions = instruction.RootElement.Clone(), Criteria = new Dictionary<string, JsonElement> { ["z"] = b.RootElement.Clone(), ["a"] = a.RootElement.Clone() } },
                ["score"] = new ScoreQuestion { Instructions = instruction.RootElement.Clone(), Criteria = [b.RootElement.Clone(), a.RootElement.Clone()] },
                ["boolean"] = new BooleanQuestion { Instructions = instruction.RootElement.Clone(), Criteria = new BooleanCriteria { WhenTrue = a.RootElement.Clone(), WhenFalse = b.RootElement.Clone() } },
            },
        };
    }

    private static ModernBertModelPackage Package(string tokenizerPath, CancellationToken cancellationToken)
    {
        const int h = 4;
        var config = new ModernBertConfig { VocabularySize = 16, HiddenSize = h, IntermediateSize = 8, LayerCount = 1, HeadCount = 2, MaxTokens = 1024 };
        var tokenizer = new TokenizerJson(tokenizerPath, cancellationToken);
        var encoder = new ModernBertEncoder(config, new ModernBertWeights
        {
            TokenEmbeddings = new float[16 * h], EmbeddingNorm = [1f, 1f, 1f, 1f], FinalNorm = [1f, 1f, 1f, 1f],
            Layers = [new ModernBertLayerWeights { Qkv = new float[3 * h * h], AttentionOutput = new float[h * h], MlpNorm = new float[h], MlpUp = new float[16 * h], MlpDown = new float[h * 8] }],
        });
        DecisionHeadLayerWeights HeadLayer() => new()
        {
            Qkv = new float[3 * h * h], QkvBias = new float[3 * h],
            AttentionOutput = new float[h * h], AttentionOutputBias = new float[h],
            AttentionNorm = [1f, 1f, 1f, 1f], AttentionNormBias = new float[h],
            MlpUp = new float[4 * h * h], MlpUpBias = new float[4 * h], MlpDown = new float[4 * h * h], MlpDownBias = new float[h],
            MlpNorm = [1f, 1f, 1f, 1f], MlpNormBias = new float[h],
        };
        // These are shaped tiny fixture tensors; only the injected test
        // pipeline executes. No real model output or model quality is asserted.
        return new ModernBertModelPackage
        {
            ModelId = "fixture-backend-injection", Revision = "fixture-only", TokenizerRevision = "fixture-tokenizer", License = "Apache-2.0",
            Tokenizer = tokenizer, Encoder = encoder, HeadMaxTokens = 256,
            Temperature = [1f, 2f, 4f], EstimatedResidentBytes = 64,
            Head = new DecisionHeadWeights
            {
                TypeEmbeddings = new float[3 * h], Layers = [HeadLayer(), HeadLayer()], ScorerNorm = [1f, 1f, 1f, 1f], ScorerNormBias = new float[h],
                ScorerDense = new float[h * h], ScorerDenseBias = new float[h], ScorerOutput = new float[h], ScorerOutputBias = new float[1],
            },
        };
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 2e-6;
    private sealed record Call(int[] Tokens, int TypeId, int[] Markers);
    private sealed class FixtureDisposeException : Exception { }

    private sealed class FixturePipeline : IMarkerDecisionPipeline
    {
        public List<Call> Calls { get; } = [];
        public bool ThrowOnDispose { get; init; }
        public int DisposeCount { get; private set; }

        public float[] Score(ReadOnlySpan<int> tokenIds, int typeId, ReadOnlySpan<int> markerPositions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Calls.Count >= 3 || markerPositions.Length != 2 || tokenIds.Length > 1024)
                throw new InvalidOperationException("Injected fixture call exceeded its fixed bounds.");
            Calls.Add(new Call(tokenIds.ToArray(), typeId, markerPositions.ToArray()));
            return [0f, 2f];
        }

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose) throw new FixtureDisposeException();
        }
    }
}
