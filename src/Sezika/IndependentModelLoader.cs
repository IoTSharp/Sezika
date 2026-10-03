using System.Security.Cryptography;

namespace Sezika;

/// <summary>Independent encoder package. It never falls back to the legacy Laya package.</summary>
public sealed class IndependentEncoderModelPackage : IDisposable
{
    private int _disposed;
    public required string ModelId { get; init; }
    public required string SourceModelId { get; init; }
    public required string Revision { get; init; }
    public required string License { get; init; }
    public required string EncoderSha256 { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required TokenizerJson Tokenizer { get; init; }
    public required ModernBertEncoder Encoder { get; init; }
    public required long EstimatedResidentBytes { get; init; }
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Clear(Encoder.Weights.TokenEmbeddings); Clear(Encoder.Weights.EmbeddingNorm); Clear(Encoder.Weights.FinalNorm);
        foreach (var layer in Encoder.Weights.Layers)
        {
            Clear(layer.AttentionNorm); Clear(layer.Qkv); Clear(layer.AttentionOutput);
            Clear(layer.MlpNorm); Clear(layer.MlpUp); Clear(layer.MlpDown);
        }
        Encoder.Dispose();
    }

    private static void Clear(float[]? values) { if (values is not null) Array.Clear(values); }
}

/// <summary>Loads a converted, hash-bound mmBERT encoder under the independent identity.</summary>
public static class IndependentModelLoader
{
    public const string ModelId = "sezika/mmbert-base-independent-s4";
    public const string SourceModelId = "jhu-clsp/mmBERT-base";
    public const string SourceRevision = "c5955035435e2bf121cde7f3c8863ef52ff35d82";
    public const string SourceWeightsSha256 = "8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e";
    public const string TokenizerSha256 = "197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356";
    public const string ConvertedEncoderSha256 = "6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528";

    public static IndependentEncoderModelPackage Load(string packageDirectory, CancellationToken cancellationToken = default) =>
        Load(packageDirectory, EncoderExecutionOptions.Default, cancellationToken);

    public static IndependentEncoderModelPackage Load(string packageDirectory, EncoderExecutionOptions executionOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionOptions); executionOptions.Validate();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        cancellationToken = lifetime.Token;
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(packageDirectory)) throw new ArgumentException("Package directory is required.", nameof(packageDirectory));
        var root = Path.GetFullPath(packageDirectory); var manifestPath = Within(root, "model.json");
        var weightsPath = Within(root, "encoder.safetensors"); var tokenizerPath = Within(root, "tokenizer/tokenizer.json");
        if (!File.Exists(manifestPath) || !File.Exists(weightsPath) || !File.Exists(tokenizerPath))
            throw new DecisionException("independent_model_not_installed", "Independent model package is incomplete.");
        var config = IndependentModelContract.ReadManifest(ReadBounded(manifestPath, 1 * 1024 * 1024), cancellationToken);
        VerifyHash(weightsPath, ConvertedEncoderSha256, 1_227_772_549, cancellationToken);
        VerifyHash(tokenizerPath, TokenizerSha256, 17_525_329, cancellationToken);
        var tensors = SafeTensorReader.Read(weightsPath, maxElements: 400_000_000, cancellationToken: cancellationToken);
        VerifyTensorNames(tensors);
        var embedding = Tensor(tensors, "model.embeddings.tok_embeddings.weight");
        var embeddingNorm = Tensor(tensors, "model.embeddings.norm.weight"); var finalNorm = Tensor(tensors, "model.final_norm.weight");
        var layers = new ModernBertLayerWeights[config.LayerCount];
        for (var index = 0; index < layers.Length; index++)
        {
            var prefix = $"model.layers.{index}.";
            layers[index] = new ModernBertLayerWeights
            {
                AttentionNorm = index == 0 ? null : Tensor(tensors, prefix + "attn_norm.weight"),
                Qkv = Tensor(tensors, prefix + "attn.Wqkv.weight"), AttentionOutput = Tensor(tensors, prefix + "attn.Wo.weight"),
                MlpNorm = Tensor(tensors, prefix + "mlp_norm.weight"), MlpUp = Tensor(tensors, prefix + "mlp.Wi.weight"),
                MlpDown = Tensor(tensors, prefix + "mlp.Wo.weight"),
            };
        }
        var encoder = new ModernBertEncoder(config, new ModernBertWeights
        {
            TokenEmbeddings = embedding, EmbeddingNorm = embeddingNorm, FinalNorm = finalNorm, Layers = layers,
        }, executionOptions, cancellationToken);
        try
        {
            var tokenizer = new TokenizerJson(tokenizerPath, cancellationToken);
            if (tokenizer.BosId != 2 || tokenizer.EosId != 1 || tokenizer.MaskId != 4)
                throw new DecisionException("independent_tokenizer_invalid", "Independent tokenizer special token IDs differ from the audited config.");
            var residentBytes = checked(tensors.Values.Sum(tensor => checked((long)tensor.Values.Length * sizeof(float))) + encoder.QuantizedWeightBytes);
            return new IndependentEncoderModelPackage
            {
                ModelId = ModelId, SourceModelId = SourceModelId, Revision = SourceRevision, License = "MIT",
                EncoderSha256 = ConvertedEncoderSha256, TokenizerSha256 = TokenizerSha256, Tokenizer = tokenizer,
                Encoder = encoder, EstimatedResidentBytes = residentBytes,
            };
        }
        catch { encoder.Dispose(); throw; }
    }

    private static void VerifyTensorNames(IReadOnlyDictionary<string, SafeTensor> tensors)
    {
        if (tensors.Count != 134 || tensors.Keys.Any(name => !name.StartsWith("model.", StringComparison.Ordinal)))
            throw new DecisionException("independent_manifest_tensor_mismatch", "Independent tensor file contains an unexpected encoder tensor set.");
        foreach (var tensor in tensors.Values)
            IndependentModelContract.ValidateTensorDescriptor(tensor.Name, tensor.Dtype, tensor.Shape);
    }

    private static float[] Tensor(IReadOnlyDictionary<string, SafeTensor> tensors, string name) =>
        tensors.TryGetValue(name, out var tensor) ? tensor.Values : throw new DecisionException("independent_tensor_missing", $"Independent tensor '{name}' is missing.");

    private static byte[] ReadBounded(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); if (stream.Length > maxBytes) throw new DecisionException("independent_manifest_limit_exceeded", "Independent manifest exceeds its size bound.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }

    internal static string Within(string root, string relative)
    {
        if (!Directory.Exists(root)) throw new DecisionException("independent_model_not_installed", "Independent model directory does not exist.");
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new DecisionException("independent_manifest_path_invalid", "Independent asset paths must be relative.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new DecisionException("independent_manifest_path_invalid", "Independent asset path escapes its package.");
        // Reject links/junctions before reading any asset, including linked ancestors.
        // This is a static package preflight, not protection against concurrent hostile mutation.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string? component = path;
        for (var depth = 0; depth < 64 && component is not null; depth++)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10))
                throw new DecisionException("independent_manifest_path_invalid", "Independent path preflight deadline expired.");
            if ((File.Exists(component) || Directory.Exists(component)) &&
                (File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                throw new DecisionException("independent_manifest_path_invalid", "Independent package links and junctions are not supported.");
            component = Path.GetDirectoryName(component);
        }
        if (component is not null) throw new DecisionException("independent_manifest_path_invalid", "Independent asset path exceeds the depth bound.");
        return path;
    }

    private static void VerifyHash(string path, string expected, long expectedBytes, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        if (stream.Length != expectedBytes)
            throw new DecisionException("independent_asset_hash_mismatch", $"Independent asset '{Path.GetFileName(path)}' size differs from the fixed revision.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1024 * 1024];
        var blocks = checked((int)((expectedBytes + buffer.Length - 1) / buffer.Length));
        var remaining = expectedBytes;
        for (var block = 0; block < blocks; block++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, remaining);
            stream.ReadExactly(buffer.AsSpan(0, count)); hash.AppendData(buffer, 0, count); remaining -= count;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (remaining != 0 || stream.ReadByte() != -1)
            throw new DecisionException("independent_asset_hash_mismatch", "Independent asset size changed during hash verification.");
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new DecisionException("independent_asset_hash_mismatch", $"Independent asset '{Path.GetFileName(path)}' failed hash verification.");
    }
}
