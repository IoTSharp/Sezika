using System.Security.Cryptography;
using System.Text.Json;

namespace Sezika;

public sealed class ModernBertModelPackage : IDisposable
{
    private int _disposed;

    public required string ModelId { get; init; }
    public required string Revision { get; init; }
    public string TokenizerRevision { get; init; } = string.Empty;
    public required string License { get; init; }
    public required TokenizerJson Tokenizer { get; init; }
    public required ModernBertEncoder Encoder { get; init; }
    public required DecisionHeadWeights Head { get; init; }
    public required int HeadMaxTokens { get; init; }
    public required float[] Temperature { get; init; }
    public long EstimatedResidentBytes { get; init; }
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Unload model tensors from the managed session and make future use fail closed.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Clear(Encoder.Weights.TokenEmbeddings);
        Clear(Encoder.Weights.EmbeddingNorm);
        Clear(Encoder.Weights.FinalNorm);
        foreach (var layer in Encoder.Weights.Layers)
        {
            Clear(layer.AttentionNorm); Clear(layer.Qkv); Clear(layer.AttentionOutput);
            Clear(layer.MlpNorm); Clear(layer.MlpUp); Clear(layer.MlpDown);
        }
        Clear(Head.TypeEmbeddings); Clear(Head.ScorerNorm); Clear(Head.ScorerNormBias);
        Clear(Head.ScorerDense); Clear(Head.ScorerDenseBias); Clear(Head.ScorerOutput); Clear(Head.ScorerOutputBias);
        foreach (var layer in Head.Layers)
        {
            Clear(layer.Qkv); Clear(layer.QkvBias); Clear(layer.AttentionOutput); Clear(layer.AttentionOutputBias);
            Clear(layer.AttentionNorm); Clear(layer.AttentionNormBias); Clear(layer.MlpUp); Clear(layer.MlpUpBias);
            Clear(layer.MlpDown); Clear(layer.MlpDownBias); Clear(layer.MlpNorm); Clear(layer.MlpNormBias);
        }
        Clear(Temperature);
        Encoder.Dispose();
    }

    private static void Clear(float[]? values)
    {
        if (values is not null) Array.Clear(values);
    }
}

/// <summary>Loads the pinned Laya/mmBERT package from verified JSON + SafeTensors only.</summary>
public static class ModernBertModelLoader
{
    private const int MaxManifestBytes = 1 * 1024 * 1024;
    public const string PinnedModelId = "convaiinnovations/laya-multilingual";
    public const string PinnedRevision = "052592a15d198d9ad47da779604259b10b47b7aa";
    public const string PinnedWeightsSha256 = "9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204";
    public const string PinnedTokenizerSha256 = "609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f";

    public static ModernBertModelPackage Load(string packageDirectory, CancellationToken cancellationToken = default) =>
        Load(packageDirectory, EncoderExecutionOptions.Default, cancellationToken);

    public static ModernBertModelPackage Load(string packageDirectory, EncoderExecutionOptions executionOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Validate();
        if (string.IsNullOrWhiteSpace(packageDirectory)) throw new ArgumentException("Package directory is required.", nameof(packageDirectory));
        var root = Path.GetFullPath(packageDirectory);
        var manifestPath = Within(root, "model.json"); var weightsPath = Within(root, "model.safetensors"); var tokenizerPath = Within(root, "tokenizer/tokenizer.json");
        if (!File.Exists(manifestPath) || !File.Exists(weightsPath) || !File.Exists(tokenizerPath)) throw new DecisionException("decision_model_not_installed", "Pinned model package is incomplete.");
        byte[] manifestBytes;
        using (var manifestStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (manifestStream.Length > MaxManifestBytes)
                throw new DecisionException("decision_manifest_limit_exceeded", "model.json exceeds the 1 MiB metadata limit.");

            manifestBytes = new byte[(int)manifestStream.Length];
            manifestStream.ReadExactly(manifestBytes);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var manifest = JsonDocument.Parse(manifestBytes, new JsonDocumentOptions { MaxDepth = 32 });
        var json = manifest.RootElement;
        if (json.GetProperty("schema_version").GetInt32() != 1 || json.GetProperty("model_id").GetString() != PinnedModelId ||
            json.GetProperty("revision").GetString() != PinnedRevision || json.GetProperty("tokenizer_revision").GetString() != PinnedRevision ||
            json.GetProperty("license").GetString() != "Apache-2.0")
            throw new DecisionException("decision_manifest_invalid", "The model manifest is not the pinned Apache-2.0 Laya revision.");
        VerifyHash(weightsPath, PinnedWeightsSha256, cancellationToken); VerifyHash(tokenizerPath, PinnedTokenizerSha256, cancellationToken);
        var tensors = SafeTensorReader.Read(weightsPath, maxElements: 300_000_000, cancellationToken: cancellationToken);
        VerifyManifestTensors(json, tensors);
        var encoderJson = json.GetProperty("encoder");
        var config = new ModernBertConfig
        {
            VocabularySize = encoderJson.GetProperty("vocab_size").GetInt32(), HiddenSize = encoderJson.GetProperty("hidden_size").GetInt32(),
            IntermediateSize = encoderJson.GetProperty("intermediate_size").GetInt32(), LayerCount = encoderJson.GetProperty("layer_count").GetInt32(),
            HeadCount = encoderJson.GetProperty("head_count").GetInt32(), MaxTokens = encoderJson.GetProperty("max_tokens").GetInt32(),
            GlobalAttentionEvery = encoderJson.GetProperty("global_attention_every").GetInt32(), LocalAttention = encoderJson.GetProperty("local_attention").GetInt32(),
            GlobalRopeTheta = encoderJson.GetProperty("global_rope_theta").GetSingle(), LocalRopeTheta = encoderJson.GetProperty("local_rope_theta").GetSingle(),
            NormEpsilon = encoderJson.GetProperty("norm_epsilon").GetSingle(),
        };
        config.Validate();
        var headMaxTokens = json.GetProperty("head").GetProperty("max_tokens").GetInt32();
        if (headMaxTokens < 2 || headMaxTokens > config.MaxTokens)
            throw new DecisionException("decision_manifest_invalid", "The decision head token budget is outside the encoder budget.");
        var embedding = Tensor(tensors, "encoder.embeddings.tok_embeddings.weight"); var embeddingNorm = Tensor(tensors, "encoder.embeddings.norm.weight"); var finalNorm = Tensor(tensors, "encoder.final_norm.weight");
        var layers = new ModernBertLayerWeights[config.LayerCount];
        for (var index = 0; index < layers.Length; index++)
        {
            var prefix = $"encoder.layers.{index}.";
            layers[index] = new ModernBertLayerWeights
            {
                AttentionNorm = index == 0 ? null : Tensor(tensors, prefix + "attn_norm.weight"), Qkv = Tensor(tensors, prefix + "attn.Wqkv.weight"),
                AttentionOutput = Tensor(tensors, prefix + "attn.Wo.weight"), MlpNorm = Tensor(tensors, prefix + "mlp_norm.weight"),
                MlpUp = Tensor(tensors, prefix + "mlp.Wi.weight"), MlpDown = Tensor(tensors, prefix + "mlp.Wo.weight"),
            };
        }
        var encoder = new ModernBertEncoder(config, new ModernBertWeights { TokenEmbeddings = embedding, EmbeddingNorm = embeddingNorm, FinalNorm = finalNorm, Layers = layers }, executionOptions, cancellationToken);
        try
        {
            var headLayers = new DecisionHeadLayerWeights[2];
            for (var index = 0; index < 2; index++)
            {
                var prefix = $"head.layers.{index}.";
                headLayers[index] = new DecisionHeadLayerWeights
                {
                    Qkv = Tensor(tensors, prefix + "self_attn.in_proj_weight"), QkvBias = Tensor(tensors, prefix + "self_attn.in_proj_bias"),
                    AttentionOutput = Tensor(tensors, prefix + "self_attn.out_proj.weight"), AttentionOutputBias = Tensor(tensors, prefix + "self_attn.out_proj.bias"),
                    AttentionNorm = Tensor(tensors, prefix + "norm1.weight"), AttentionNormBias = Tensor(tensors, prefix + "norm1.bias"),
                    MlpUp = Tensor(tensors, prefix + "linear1.weight"), MlpUpBias = Tensor(tensors, prefix + "linear1.bias"),
                    MlpDown = Tensor(tensors, prefix + "linear2.weight"), MlpDownBias = Tensor(tensors, prefix + "linear2.bias"),
                    MlpNorm = Tensor(tensors, prefix + "norm2.weight"), MlpNormBias = Tensor(tensors, prefix + "norm2.bias"),
                };
            }
            var head = new DecisionHeadWeights
            {
                TypeEmbeddings = Tensor(tensors, "type_emb.weight"), Layers = headLayers,
                ScorerNorm = Tensor(tensors, "scorer.0.weight"), ScorerNormBias = Tensor(tensors, "scorer.0.bias"),
                ScorerDense = Tensor(tensors, "scorer.1.weight"), ScorerDenseBias = Tensor(tensors, "scorer.1.bias"),
                ScorerOutput = Tensor(tensors, "scorer.3.weight"), ScorerOutputBias = Tensor(tensors, "scorer.3.bias"),
            };
            // Validate before GPU allocation without preparing a redundant head cache.
            ModernBertDecisionPipeline.ValidateWeights(config, head);
            var temperature = Tensor(tensors, "temperature");
            if (temperature.Length != 3 || temperature.Any(value => !float.IsFinite(value) || value <= 0)) throw new DecisionException("model_calibration_invalid", "The pinned temperature tensor is invalid.");
            // W8A32 currently retains original FP32 tensors for oracle/CUDA use.
            // A runtime head's additional cache is accounted by its owning session.
            var residentBytes = checked(tensors.Values.Sum(tensor => checked((long)tensor.Values.Length * sizeof(float))) + encoder.QuantizedWeightBytes);
            return new ModernBertModelPackage { ModelId = PinnedModelId, Revision = PinnedRevision, TokenizerRevision = PinnedRevision, License = "Apache-2.0", Tokenizer = new TokenizerJson(tokenizerPath, cancellationToken), Encoder = encoder, Head = head, HeadMaxTokens = headMaxTokens, Temperature = temperature, EstimatedResidentBytes = residentBytes };
        }
        catch
        {
            encoder.Dispose();
            throw;
        }
    }

    private static float[] Tensor(IReadOnlyDictionary<string, SafeTensor> tensors, string name)
    {
        if (!tensors.TryGetValue(name, out var tensor)) throw new DecisionException("model_tensor_missing", $"Pinned model tensor '{name}' is missing.");
        return tensor.Values;
    }

    private static void VerifyManifestTensors(JsonElement manifest, IReadOnlyDictionary<string, SafeTensor> tensors)
    {
        if (!manifest.TryGetProperty("tensors", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new DecisionException("decision_manifest_invalid", "The pinned model manifest has no tensor mapping.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString();
            var expectedHash = entry.GetProperty("sha256").GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(expectedHash) || !seen.Add(name) ||
                !tensors.TryGetValue(name, out var tensor) || !tensor.Sha256.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new DecisionException("decision_manifest_tensor_mismatch", $"Pinned tensor '{name ?? "<missing>"}' does not match its manifest hash.");
            }
        }
        if (seen.Count != tensors.Count)
            throw new DecisionException("decision_manifest_tensor_mismatch", "The pinned tensor manifest does not cover every SafeTensors entry.");
    }

    private static string Within(string root, string relative)
    {
        if (!Directory.Exists(root)) throw new DecisionException("decision_model_not_installed", "Model package directory does not exist.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new DecisionException("decision_manifest_path_invalid", "Model asset path escapes its package directory.");
        return path;
    }

    private static void VerifyHash(string path, string expected, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1024 * 1024]; int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0) { cancellationToken.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new DecisionException("decision_asset_hash_mismatch", $"Pinned model asset '{Path.GetFileName(path)}' failed SHA-256 verification.");
    }
}
