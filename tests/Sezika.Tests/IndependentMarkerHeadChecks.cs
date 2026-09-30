using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.Tests;

internal static class IndependentMarkerHeadChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string, string> expectCode)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var examples = new[]
        {
            Example("choice-zh", MarkerQuestionType.Choice, "zh", [[1f, 0f], [0f, 1f], [-1f, 0f]], 0),
            Example("score-en", MarkerQuestionType.Score, "en", [[0f, -1f], [1f, 0f], [0f, 1f]], 2),
            Example("boolean-zh", MarkerQuestionType.Boolean, "zh", [[-1f, 0f], [1f, 0f]], 1),
        };
        var identity = new MarkerFeatureIdentity("sezika/marker-check", new string('A', 64),
            new string('B', 64), new string('C', 64), 2);
        var set = new MarkerFeatureSet(identity, examples);
        var weights = new[] { 0.25f, -0.1f, -0.2f, 0.3f, 0.15f, -0.05f };
        foreach (var example in examples)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var analytic = new LinearMarkerHead(2, weights).Gradient(example);
            for (var index = 0; index < weights.Length; index++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var plus = (float[])weights.Clone();
                var minus = (float[])weights.Clone();
                plus[index] += 0.001f;
                minus[index] -= 0.001f;
                var numeric = (new LinearMarkerHead(2, plus).Gradient(example).Loss -
                    new LinearMarkerHead(2, minus).Gradient(example).Loss) / 0.002;
                check(Math.Abs(numeric - analytic.WeightGradient[index]) < 0.0001,
                    $"{example.Type} analytic marker gradient {index}");
            }
            check(Math.Abs(analytic.Probabilities.Sum() - 1d) < 1e-12, $"{example.Type} softmax sum");
        }

        var two = new MarkerTrainingOptions(17, 2, 0.05f, TimeSpan.FromSeconds(10));
        var four = two with { MaxSteps = 4 };
        var updates = new List<MarkerTrainingProgress>();
        var first = IndependentMarkerHeadTrainer.Train(set, two, progress: updates.Add, cancellationToken: deadline.Token);
        var full = IndependentMarkerHeadTrainer.Train(set, four, cancellationToken: deadline.Token);
        check(updates.Count == 3 && first.CompletedSteps == 2 && full.CompletedSteps == 4,
            "bounded deterministic training progress");
        check(first.Head.CopyWeights().Any(value => value != 0), "three-row head parameters update");

        var tokenizerDirectory = Path.Combine(Path.GetTempPath(), $"sezika-decision-v1-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tokenizerDirectory);
        try
        {
            var tokenizerPath = Path.Combine(tokenizerDirectory, "tokenizer.json");
            File.WriteAllText(tokenizerPath, TinyTokenizerJson());
            var tokenizer = new TokenizerJson(tokenizerPath, deadline.Token);
            var parsedRequest = IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes("{\"schema_version\":1,\"model_id\":\"sezika/decision-v1-test\",\"state\":{\"text\":\"x\"},\"questions\":[{\"id\":\"intent\",\"type\":\"choice\",\"instruction\":\"choose\",\"candidates\":[{\"id\":\"a\",\"text\":\"first\"},{\"id\":\"b\",\"text\":\"second\"}]}] }"));
            check(parsedRequest.Questions.Count == 1 && parsedRequest.Questions[0] is IndependentChoiceQuestion,
                "decision-v1 strict JSON parser materializes typed question");
            expectCode(() => IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes("{\"schema_version\":1,\"schema_version\":1,\"model_id\":\"sezika/x\",\"state\":\"x\",\"questions\":[]}")),
                "decision_duplicate_property", "decision-v1 duplicate property rejected");
            expectCode(() => IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes("{\"schema_version\":1,\"model_id\":\"sezika/x\",\"state\":\"x\",\"questions\":[],\"extra\":1}")),
                "decision_v1_unknown_property", "decision-v1 unknown property rejected");
            using var stateDocument = JsonDocument.Parse("{\"text\":\"x\"}");
            var choice = new IndependentChoiceQuestion("intent", "choose", [
                new IndependentChoiceCandidate("a", "first"), new IndependentChoiceCandidate("b", "second")]);
            var request = new IndependentDecisionRequest(1, "sezika/decision-v1-test", stateDocument.RootElement.Clone(), [choice]);
            var sequence = IndependentDecisionV1SequenceBuilder.Build(tokenizer, request, choice,
                new IndependentDecisionSequenceOptions { MaxTokens = 512 }, deadline.Token);
            check(sequence.TokenIds[0] == tokenizer.BosId && sequence.TokenIds[^1] == tokenizer.EosId &&
                sequence.MarkerPositions.Length == 2 && sequence.MarkerPositions[0] < sequence.MarkerPositions[1],
                "decision-v1 strict sequence preserves BOS/EOS and marker order");
            check(sequence.TokenIds[sequence.MarkerPositions[0]] == tokenizer.MaskId &&
                sequence.TokenIds[sequence.MarkerPositions[1]] == tokenizer.MaskId,
                "decision-v1 marker IDs are explicit and text-independent");
            var featureIdentity = new MarkerFeatureIdentity("sezika/decision-v1-test", new string('E', 64),
                new string('T', 64), new string('D', 64), 2);
            var exported = IndependentMarkerFeatureExporter.Export(tokenizer, new TestEncoder(), featureIdentity,
                request, choice, "record-1", "en", 1, new IndependentDecisionSequenceOptions { MaxTokens = 512 }, deadline.Token);
            check(exported.TokenIds.SequenceEqual(sequence.TokenIds) && exported.MarkerPositions.SequenceEqual(sequence.MarkerPositions) &&
                exported.Features.Length == 2 && exported.Features[0].Length == 2 && exported.Features[1][0] > 0,
                "complete-sequence marker feature export");
            expectCode(() => IndependentDecisionV1SequenceBuilder.Build(tokenizer,
                request with { ModelId = "convaiinnovations/laya-multilingual" }, choice,
                cancellationToken: deadline.Token), "decision_v1_identity_invalid", "decision-v1 rejects legacy model identity");
            using var longStateDocument = JsonDocument.Parse("\"aaaaaaaaaaaaaaaaaaaaaaaa\"");
            var tooLong = request with { State = longStateDocument.RootElement.Clone() };
            expectCode(() => IndependentDecisionV1SequenceBuilder.Build(tokenizer, tooLong, choice,
                new IndependentDecisionSequenceOptions { MaxTokens = 8 }, deadline.Token),
                "decision_v1_token_budget_exceeded", "decision-v1 strict token budget rejects truncation");
        }
        finally
        {
            if (Path.GetDirectoryName(tokenizerDirectory) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
                Path.GetFileName(tokenizerDirectory).StartsWith("sezika-decision-v1-", StringComparison.Ordinal) &&
                Directory.Exists(tokenizerDirectory)) Directory.Delete(tokenizerDirectory, recursive: true);
        }

        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"sezika-marker-check-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "training.checkpoint");
            var sha256 = MarkerTrainingCheckpoint.SaveNew(path, first, deadline.Token);
            var restored = MarkerTrainingCheckpoint.Load(path, set, sha256, deadline.Token);
            var resumed = IndependentMarkerHeadTrainer.Train(set, four, restored, cancellationToken: deadline.Token);
            check(resumed.Head.CopyWeights().SequenceEqual(full.Head.CopyWeights()),
                "checkpoint restore reproduces uninterrupted SGD");
            var headPath = Path.Combine(directory, "head.asset");
            var headHash = IndependentMarkerHeadAssetStore.SaveNew(headPath, first, deadline.Token);
            var asset = IndependentMarkerHeadAssetStore.Load(headPath, first.Identity, first.FeatureSha256, headHash, deadline.Token);
            check(asset.Head.CopyWeights().SequenceEqual(first.Head.CopyWeights()) && asset.Identity == first.Identity,
                "independent inference head asset round trip");
            var changedIdentity = first.Identity with { EncoderSha256 = new string('Z', 64) };
            expectCode(() => IndependentMarkerHeadAssetStore.Load(headPath, changedIdentity, first.FeatureSha256, headHash, deadline.Token),
                "head_asset_identity_mismatch", "head asset encoder identity mismatch rejected");
            expectCode(() => MarkerTrainingCheckpoint.Load(path, set, new string('0', 64), deadline.Token),
                "training_checkpoint_hash_invalid", "wrong checkpoint hash rejected");
            var wrongIdentity = new MarkerFeatureSet(identity with { DataManifestSha256 = new string('D', 64) }, examples);
            expectCode(() => MarkerTrainingCheckpoint.Load(path, wrongIdentity, sha256, deadline.Token),
                "training_resume_mismatch", "checkpoint data identity mismatch rejected");
            var changed = (byte[])File.ReadAllBytes(path).Clone();
            changed[^33] ^= 1;
            var changedPath = Path.Combine(directory, "tampered.checkpoint");
            File.WriteAllBytes(changedPath, changed);
            expectCode(() => MarkerTrainingCheckpoint.Load(changedPath, set, sha256, deadline.Token),
                "training_checkpoint_hash_invalid", "tampered checkpoint rejected");
            var wrongShape = (byte[])File.ReadAllBytes(path).Clone();
            var countOffset = wrongShape.Length - 32 - first.Head.CopyWeights().Length * sizeof(float) - sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(wrongShape.AsSpan(countOffset, 4), 5);
            SHA256.HashData(wrongShape.AsSpan(0, wrongShape.Length - 32))
                .CopyTo(wrongShape.AsSpan(wrongShape.Length - 32));
            var wrongShapePath = Path.Combine(directory, "wrong-shape.checkpoint");
            File.WriteAllBytes(wrongShapePath, wrongShape);
            var wrongShapeHash = Convert.ToHexString(SHA256.HashData(wrongShape));
            expectCode(() => MarkerTrainingCheckpoint.Load(wrongShapePath, set, wrongShapeHash, deadline.Token),
                "training_checkpoint_invalid", "hash-valid wrong checkpoint shape rejected");
        }
        finally
        {
            // This directory is created under the system temp path with a fresh task-specific GUID.
            if (Path.GetDirectoryName(directory) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
                Path.GetFileName(directory).StartsWith("sezika-marker-check-", StringComparison.Ordinal) &&
                Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }

        var malformed = examples[0] with { Features = [[1f], [0f], [-1f]] };
        expectCode(() => IndependentMarkerHeadTrainer.Train(new MarkerFeatureSet(identity, [malformed]), two,
            cancellationToken: deadline.Token), "training_shape_invalid", "marker width mismatch rejected");
        var nonFinite = examples[1] with { Features = [[float.NaN, 0f], [1f, 0f], [0f, 1f]] };
        expectCode(() => IndependentMarkerHeadTrainer.Train(new MarkerFeatureSet(identity, [nonFinite]), two,
            cancellationToken: deadline.Token), "training_value_invalid", "non-finite marker rejected");
    }

    private static MarkerFeatureExample Example(string id, MarkerQuestionType type, string language,
        float[][] features, int target)
    {
        var tokens = Enumerable.Range(1, features.Length + 2).ToArray();
        var positions = Enumerable.Range(1, features.Length).ToArray();
        return new MarkerFeatureExample(id, type, language, tokens, positions, features, target);
    }

    private sealed class TestEncoder : IEncoder
    {
        public float[] Encode(ReadOnlySpan<int> tokenIds, CancellationToken cancellationToken = default)
        {
            var values = new float[tokenIds.Length * 2];
            for (var index = 0; index < tokenIds.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                values[index * 2] = tokenIds[index]; values[index * 2 + 1] = tokenIds[index] * 2;
            }
            return values;
        }
    }

    private static string TinyTokenizerJson()
    {
        var builder = new StringBuilder("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<pad>\":0,\"<eos>\":1,\"<bos>\":2,\"<unk>\":3,\"<mask>\":4");
        for (var value = 0; value < 256; value++) builder.Append($",\"<0x{value:X2}>\":{1000 + value}");
        builder.Append("},\"merges\":[]}}"); return builder.ToString();
    }
}
