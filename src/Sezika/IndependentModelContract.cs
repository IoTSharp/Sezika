using System.Diagnostics;
using System.Text.Json;

namespace Sezika;

// A fixed asset hash identifies the numerical contract as well as the bytes.
// Manifest edits must not silently change attention or imply calibration.
internal static class IndependentModelContract
{
    internal static ModernBertConfig ReadManifest(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        if (bytes.Length is < 1 or > 1_048_576) Invalid("Manifest size exceeds the independent contract.");
        var clock = Stopwatch.StartNew();
        void Check()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) Invalid("Manifest validation deadline expired.");
        }
        Check();
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            Object(root, ["schema_version", "model_id", "source_model_id", "source_revision", "license", "source_weights_sha256",
                "weights_file", "weights_sha256", "tokenizer_file", "tokenizer_sha256", "tensor_format", "tensor_count", "tensor_prefix", "encoder", "head", "calibration"], Check);
            if (root.GetProperty("schema_version").GetInt32() != 1 ||
                Text(root, "model_id") != IndependentModelLoader.ModelId ||
                Text(root, "source_model_id") != IndependentModelLoader.SourceModelId ||
                Text(root, "source_revision") != IndependentModelLoader.SourceRevision || Text(root, "license") != "MIT" ||
                Text(root, "source_weights_sha256") != IndependentModelLoader.SourceWeightsSha256 ||
                Text(root, "weights_sha256") != IndependentModelLoader.ConvertedEncoderSha256 ||
                Text(root, "tokenizer_sha256") != IndependentModelLoader.TokenizerSha256 ||
                Text(root, "weights_file") != "encoder.safetensors" || Text(root, "tokenizer_file") != "tokenizer/tokenizer.json" ||
                Text(root, "tensor_format") != "safetensors-f32" || root.GetProperty("tensor_count").GetInt32() != 134 ||
                Text(root, "tensor_prefix") != "model.") Invalid("Independent asset identity or layout differs from the audited revision.");
            var encoder = root.GetProperty("encoder");
            Object(encoder, ["vocab_size", "hidden_size", "intermediate_size", "layer_count", "head_count", "source_max_position_embeddings",
                "runtime_max_tokens", "global_attention_every", "local_attention", "global_rope_theta", "local_rope_theta", "norm_epsilon"], Check);
            var config = new ModernBertConfig
            {
                VocabularySize = encoder.GetProperty("vocab_size").GetInt32(), HiddenSize = encoder.GetProperty("hidden_size").GetInt32(),
                IntermediateSize = encoder.GetProperty("intermediate_size").GetInt32(), LayerCount = encoder.GetProperty("layer_count").GetInt32(),
                HeadCount = encoder.GetProperty("head_count").GetInt32(), MaxTokens = encoder.GetProperty("runtime_max_tokens").GetInt32(),
                GlobalAttentionEvery = encoder.GetProperty("global_attention_every").GetInt32(), LocalAttention = encoder.GetProperty("local_attention").GetInt32(),
                GlobalRopeTheta = encoder.GetProperty("global_rope_theta").GetSingle(), LocalRopeTheta = encoder.GetProperty("local_rope_theta").GetSingle(),
                NormEpsilon = encoder.GetProperty("norm_epsilon").GetSingle(),
            };
            if (config.VocabularySize != 256000 || config.HiddenSize != 768 || config.IntermediateSize != 1152 ||
                config.LayerCount != 22 || config.HeadCount != 12 || config.MaxTokens != 1024 ||
                config.GlobalAttentionEvery != 3 || config.LocalAttention != 128 || config.GlobalRopeTheta != 160000f ||
                config.LocalRopeTheta != 160000f || config.NormEpsilon != 1e-5f ||
                encoder.GetProperty("source_max_position_embeddings").GetInt32() != 8192)
                Invalid("Independent encoder numerical configuration differs from the audited revision.");
            var head = root.GetProperty("head");
            Object(head, ["kind", "hidden_size", "rows", "status"], Check);
            if (Text(head, "kind") != "independent-marker-linear-v1" || head.GetProperty("hidden_size").GetInt32() != 768 ||
                head.GetProperty("rows").GetInt32() != 3 || Text(head, "status") != "separate-asset-required")
                Invalid("Independent head must be a separate, explicitly verified asset.");
            var calibration = root.GetProperty("calibration");
            Object(calibration, ["status"], Check);
            if (Text(calibration, "status") != "uncalibrated") Invalid("This encoder package has no measured calibration profile.");
            Check();
            return config;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new DecisionException("independent_manifest_invalid", "Independent manifest fields are malformed or incomplete.", exception);
        }
    }

    private static string? Text(JsonElement value, string name) => value.GetProperty(name).GetString();

    private static void Object(JsonElement value, string[] allowed, Action check)
    {
        if (value.ValueKind != JsonValueKind.Object) Invalid("Manifest sections must be objects.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            check();
            if (++count > allowed.Length || !seen.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal))
                Invalid("Manifest contains duplicate or unsupported fields.");
        }
        if (count != allowed.Length) Invalid("Manifest is missing required fields.");
    }

    internal static void ValidateTensorDescriptor(string name, string dtype, ReadOnlySpan<int> shape)
    {
        int[] expected;
        if (name == "model.embeddings.tok_embeddings.weight") expected = [256000, 768];
        else if (name is "model.embeddings.norm.weight" or "model.final_norm.weight") expected = [768];
        else
        {
            var parts = name.Split('.');
            if (parts.Length is not (5 or 6) || parts[0] != "model" || parts[1] != "layers" ||
                !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var layer) ||
                layer is < 0 or >= 22 || parts[2] != layer.ToString(System.Globalization.CultureInfo.InvariantCulture) || parts[^1] != "weight")
                throw new DecisionException("independent_manifest_tensor_mismatch", "Unexpected independent encoder tensor name.");
            var suffix = string.Join('.', parts.AsSpan(3).ToArray());
            expected = suffix switch
            {
                "attn_norm.weight" when layer > 0 => [768],
                "mlp_norm.weight" => [768],
                "attn.Wqkv.weight" => [2304, 768],
                "attn.Wo.weight" => [768, 768],
                "mlp.Wi.weight" => [2304, 768],
                "mlp.Wo.weight" => [768, 1152],
                _ => throw new DecisionException("independent_manifest_tensor_mismatch", "Unexpected independent encoder tensor name."),
            };
        }
        if (dtype != "F32" || !shape.SequenceEqual(expected))
            throw new DecisionException("independent_manifest_tensor_mismatch", "Independent tensor dtype or dimensions differ from the fixed layout.");
    }

    private static void Invalid(string message) => throw new DecisionException("independent_manifest_invalid", message);
}
