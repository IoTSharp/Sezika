namespace Sezika;

/// <summary>
/// Creates a deterministic tiny model for integration and smoke tests. It is
/// intentionally not a released checkpoint and must never be used as quality evidence.
/// </summary>
public static class DemoModelFactory
{
    public static DecisionModel CreateTiny(string backend = "cpu")
    {
        const int vocabularySize = 128;
        const int hiddenSize = 4;
        const int intermediateSize = 8;
        var config = new TransformerConfig
        {
            VocabularySize = vocabularySize,
            HiddenSize = hiddenSize,
            IntermediateSize = intermediateSize,
            LayerCount = 1,
            HeadCount = 2,
            MaxTokens = 256,
        };
        var embeddings = new float[vocabularySize * hiddenSize];
        for (var token = 0; token < vocabularySize; token++)
        {
            for (var dimension = 0; dimension < hiddenSize; dimension++) embeddings[token * hiddenSize + dimension] = MathF.Sin((token + 1) * (dimension + 1) * 0.17f);
        }
        var identity = new float[hiddenSize * hiddenSize];
        for (var i = 0; i < hiddenSize; i++) identity[i * hiddenSize + i] = 1f;
        var up = new float[intermediateSize * hiddenSize];
        var down = new float[hiddenSize * intermediateSize];
        for (var i = 0; i < intermediateSize; i++)
        {
            up[i * hiddenSize + (i % hiddenSize)] = 0.5f;
            down[(i % hiddenSize) * intermediateSize + i] = 0.25f;
        }
        var ones = Enumerable.Repeat(1f, hiddenSize).ToArray();
        var layer = new TransformerLayerWeights
        {
            Query = identity.ToArray(),
            Key = identity.ToArray(),
            Value = identity.ToArray(),
            Output = identity.ToArray(),
            FeedForwardUp = up,
            FeedForwardDown = down,
            AttentionNorm = ones.ToArray(),
            FeedForwardNorm = ones.ToArray(),
        };
        var weights = new TransformerWeights { TokenEmbeddings = embeddings, Layers = [layer], FinalNorm = ones };
        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["[UNK]"] = 0, ["[BOS]"] = 1, ["[EOS]"] = 2,
            ["state"] = 3, ["instructions"] = 4, ["criteria"] = 5,
        };
        var tokenizer = new Tokenizer(new TokenizerSpec { VocabularySize = vocabularySize, UnknownTokenId = 0, BeginningOfSequenceTokenId = 1, EndOfSequenceTokenId = 2, Vocabulary = vocabulary });
        return new DecisionModel("sezika-demo-tiny", "demo-1", "demo-tokenizer-1", tokenizer, new TransformerEncoder(config, weights), [0.35f, -0.2f, 0.15f, 0.1f], 0.01f, backend);
    }
}
