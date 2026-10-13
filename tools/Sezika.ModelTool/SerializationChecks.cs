using System.Text.Json;
using System.Text.Json.Serialization;

internal static class SerializationChecks
{
    internal static void Run(string output, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (Path.Exists(output)) throw new IOException("Serializer evidence exists; preserve it.");
        var hash = new string('a', 64); var revision = new string('b', 40);
        var tensor = new TensorEntry("fixture.weight", "model.safetensors", hash, "F32", [2, 2], [0, 16]);
        var mapping = new TensorMappingEntry("embedding", "embedding_norm", "final_norm", [new(0, "qkv", "attention_output", null, "mlp_input", "mlp_output", "mlp_norm")]);
        var manifest = new ModelManifest(1, "fixture/model", revision, "ModernBertForSequenceClassification", "Apache-2.0", "https://example.invalid/fixture/model", revision, revision, "tokenizer/tokenizer.json", "model.safetensors", hash, hash, "F32", "row-major pytorch [out,in]", [tensor],
            new("huggingface-tokenizers-json", "Replace(space->▁)+Metaspace", "Metaspace", "BPE", "TemplateProcessing"), new() { ["pad"] = 0 }, new(8, 4, 6, 1, 2, 8, 1, 2, 160000, 160000, 1e-5), new(4, 2, 6, 8), new("uncalibrated", [1, 1, 1]), mapping);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson.Default.ModelManifest);
        using var json = JsonDocument.Parse(serialized);
        var root = json.RootElement;
        // Check consumer wire keys and types independently through DOM.
        if (JsonSerializer.IsReflectionEnabledByDefault || root.GetProperty("schema_version").GetInt32() != 1 || root.GetProperty("model_id").GetString() != "fixture/model" || root.GetProperty("encoder").GetProperty("vocab_size").GetInt32() != 8 || root.GetProperty("tensors")[0].GetProperty("offsets")[1].GetUInt64() != 16 || root.GetProperty("tensor_mapping").GetProperty("layers")[0].GetProperty("attention_norm").ValueKind != JsonValueKind.Null || root.GetProperty("calibration").GetProperty("status").GetString() != "uncalibrated") throw new InvalidDataException("Frozen manifest wire contract differs or reflection is enabled.");
        var copy = JsonSerializer.Deserialize(serialized, ManifestJson.Default.ModelManifest) ?? throw new InvalidDataException("Generated manifest reader returned null.");
        using var roundtrip = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(copy, ManifestJson.Default.ModelManifest));
        if (!JsonElement.DeepEquals(root, roundtrip.RootElement)) throw new InvalidDataException("Manifest generated roundtrip differs.");
        var source = new ModelSourceLock(new("fixture/model", revision, "Apache-2.0", "https://example.invalid/fixture/model", hash, hash), new("fixture/base", revision, "MIT", "https://example.invalid/fixture/base"), new() { ["encoder/config.json"] = hash, ["tokenizer/tokenizer.json"] = hash }, "Sezika.ModelTool");
        using var lockJson = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(source, ManifestJson.Default.ModelSourceLock));
        if (lockJson.RootElement.GetProperty("base_encoder").GetProperty("license").GetString() != "MIT" || lockJson.RootElement.GetProperty("files").GetProperty("encoder/config.json").GetString() != hash) throw new InvalidDataException("Source lock separate license/asset identity differs.");
        cancellation.ThrowIfCancellationRequested();
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, new SerializerReceipt("PASS_frozen_wire_contract_only", 3, false, false, !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported), SerializerCheckJson.Default.SerializerReceipt);
        file.Flush(true);
        Console.WriteLine("PASS 3 generated JSON/wire checks; actual pinned model generation NOT_RUN");
    }
}

internal sealed record SerializerReceipt(string Status, int Checks, bool RealPinnedModelRead, bool QualityAccepted, bool NativeAot);
[JsonSerializable(typeof(SerializerReceipt))]
internal partial class SerializerCheckJson : JsonSerializerContext;
