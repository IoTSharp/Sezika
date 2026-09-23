using System.Text.Json;

namespace Sezika;

/// <summary>Builds a decision model from a verified model.json/tokenizer/safetensors package.</summary>
public static class DecisionModelAssetLoader
{
    public static DecisionModel Load(string packageDirectory, string backend = "cpu", CancellationToken cancellationToken = default)
    {
        var manifest = ModelAssetVerifier.LoadAndVerify(packageDirectory, cancellationToken);
        var root = Path.GetFullPath(packageDirectory);
        var tokenizerPath = Path.Combine(root, manifest.TokenizerFile);
        var tokenizerSpec = JsonSerializer.Deserialize(File.ReadAllBytes(tokenizerPath), DecisionJsonContext.Default.TokenizerSpec)
            ?? throw new DecisionException("decision_tokenizer_invalid", "Tokenizer asset is empty.");
        var tokenizer = new Tokenizer(tokenizerSpec);
        var tensors = SafeTensorReader.Read(Path.Combine(root, manifest.WeightsFile), cancellationToken: cancellationToken);
        float[] Tensor(string name)
        {
            if (!tensors.TryGetValue(name, out var tensor)) throw new DecisionException("decision_tensor_missing", $"Tensor '{name}' is missing.");
            return tensor.Values;
        }
        var config = manifest.Encoder;
        var layers = new TransformerLayerWeights[config.LayerCount];
        for (var index = 0; index < layers.Length; index++)
        {
            layers[index] = new TransformerLayerWeights
            {
                Query = Tensor($"layers.{index}.query"),
                Key = Tensor($"layers.{index}.key"),
                Value = Tensor($"layers.{index}.value"),
                Output = Tensor($"layers.{index}.output"),
                FeedForwardUp = Tensor($"layers.{index}.ffn_up"),
                FeedForwardDown = Tensor($"layers.{index}.ffn_down"),
                AttentionNorm = Tensor($"layers.{index}.attention_norm"),
                FeedForwardNorm = Tensor($"layers.{index}.ffn_norm"),
            };
        }
        var weights = new TransformerWeights
        {
            TokenEmbeddings = Tensor("embeddings"),
            Layers = layers,
            FinalNorm = manifest.FinalNormTensor is null ? null : Tensor(manifest.FinalNormTensor),
        };
        return new DecisionModel(manifest.ModelId, manifest.Revision, manifest.TokenizerRevision, tokenizer, new TransformerEncoder(config, weights), Tensor(manifest.HeadWeightsTensor), manifest.HeadBias, backend);
    }
}
