using System.Text;
using Sezika;

namespace Sezika.Tests;

internal static class IndependentLoaderContractChecks
{
    internal static void Run(Action<bool, string> check, Action<Action, string, string> expectCode)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var manifest = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "independent-model.json"));
        var config = IndependentModelContract.ReadManifest(Encoding.UTF8.GetBytes(manifest), deadline.Token);
        check(config.HeadCount == 12 && config.LocalAttention == 128 && config.LayerCount == 22,
            "independent manifest accepts fixed numerical contract without allocating weights");
        var mutations = new (string From, string To)[]
        {
            ("\"head_count\": 12", "\"head_count\": 8"),
            ("\"global_attention_every\": 3", "\"global_attention_every\": 1"),
            ("\"local_attention\": 128", "\"local_attention\": 64"),
            ("\"global_rope_theta\": 160000", "\"global_rope_theta\": 10000"),
            ("\"local_rope_theta\": 160000", "\"local_rope_theta\": 10000"),
            ("\"norm_epsilon\": 1e-5", "\"norm_epsilon\": 1e-4"),
            ("\"runtime_max_tokens\": 1024", "\"runtime_max_tokens\": 8192"),
            ("\"hidden_size\": 768", "\"hidden_size\": 512"),
            ("\"vocab_size\": 256000", "\"vocab_size\": 256001"),
            ("\"intermediate_size\": 1152", "\"intermediate_size\": 1536"),
            ("\"layer_count\": 22", "\"layer_count\": 21"),
            ("\"source_max_position_embeddings\": 8192", "\"source_max_position_embeddings\": 16384"),
            ("\"weights_file\": \"encoder.safetensors\"", "\"weights_file\": \"other.safetensors\""),
            ("\"tokenizer_file\": \"tokenizer/tokenizer.json\"", "\"tokenizer_file\": \"../tokenizer.json\""),
            ("\"tensor_prefix\": \"model.\"", "\"tensor_prefix\": \"decoder.\""),
            ("\"rows\": 3", "\"rows\": 2"),
            ("\"status\": \"uncalibrated\"", "\"status\": \"measured\""),
            ("\"head_count\": 12", "\"head_count\": 8, \"head_count\": 12"),
            ("\"schema_version\": 1", "\"schema_version\": 2, \"schema_version\": 1"),
            ("\"schema_version\": 1", "\"schema_version\": \"1\""),
            ("\"schema_version\": 1", "\"unsupported\": true, \"schema_version\": 1"),
            ("\"head_count\": 12,", ""),
        };
        foreach (var (from, to) in mutations)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (!manifest.Contains(from, StringComparison.Ordinal)) throw new InvalidOperationException("Mutation fixture no longer matches.");
            var changed = Encoding.UTF8.GetBytes(manifest.Replace(from, to, StringComparison.Ordinal));
            expectCode(() => IndependentModelContract.ReadManifest(changed, deadline.Token), "independent_manifest_invalid",
                $"independent manifest rejects mutation {from}");
        }
        expectCode(() => IndependentModelContract.ReadManifest("{}"u8.ToArray()), "independent_manifest_invalid", "missing independent identity rejected");
        expectCode(() => IndependentModelContract.ReadManifest("{"u8.ToArray()), "independent_manifest_invalid", "malformed independent manifest returns structured error");
        expectCode(() => IndependentModelContract.ValidateTensorDescriptor("model.layers.1.attn.Wqkv.weight", "F32", [768, 2304]),
            "independent_manifest_tensor_mismatch", "transposed independent matrix rejected even with identical element count");
        expectCode(() => IndependentModelContract.ValidateTensorDescriptor("model.layers.0.attn_norm.weight", "F32", [768]),
            "independent_manifest_tensor_mismatch", "first independent attention norm is identity");
        expectCode(() => IndependentModelContract.ValidateTensorDescriptor("model.layers.01.mlp_norm.weight", "F32", [768]),
            "independent_manifest_tensor_mismatch", "noncanonical independent layer name rejected");
        expectCode(() => IndependentModelContract.ValidateTensorDescriptor("model.final_norm.weight", "F16", [768]),
            "independent_manifest_tensor_mismatch", "independent dtype mismatch rejected");
        var count = 3;
        IndependentModelContract.ValidateTensorDescriptor("model.embeddings.tok_embeddings.weight", "F32", [256000, 768]);
        IndependentModelContract.ValidateTensorDescriptor("model.embeddings.norm.weight", "F32", [768]);
        IndependentModelContract.ValidateTensorDescriptor("model.final_norm.weight", "F32", [768]);
        for (var layer = 0; layer < 22; layer++)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var prefix = $"model.layers.{layer}.";
            if (layer > 0) { IndependentModelContract.ValidateTensorDescriptor(prefix + "attn_norm.weight", "F32", [768]); count++; }
            IndependentModelContract.ValidateTensorDescriptor(prefix + "attn.Wqkv.weight", "F32", [2304, 768]);
            IndependentModelContract.ValidateTensorDescriptor(prefix + "attn.Wo.weight", "F32", [768, 768]);
            IndependentModelContract.ValidateTensorDescriptor(prefix + "mlp_norm.weight", "F32", [768]);
            IndependentModelContract.ValidateTensorDescriptor(prefix + "mlp.Wi.weight", "F32", [2304, 768]);
            IndependentModelContract.ValidateTensorDescriptor(prefix + "mlp.Wo.weight", "F32", [768, 1152]);
            count += 5;
        }
        check(count == 134, "complete independent 134 tensor layout accepted without payload allocation");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { IndependentModelLoader.Load("absent-package", cancelled.Token); throw new InvalidOperationException("Cancellation ignored."); }
        catch (OperationCanceledException) { check(true, "independent load honours pre-cancellation before file access"); }

        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"sezika-independent-contract-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(directory);
        try
        {
            check(IndependentModelLoader.Within(directory, "tokenizer/tokenizer.json").StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "independent path remains within package");
            expectCode(() => IndependentModelLoader.Within(directory, "../outside.json"), "independent_manifest_path_invalid", "independent traversal rejected");
            expectCode(() => IndependentModelLoader.Within(directory, Path.GetFullPath("outside.json")), "independent_manifest_path_invalid", "independent absolute asset path rejected");
        }
        finally
        {
            if (Path.GetDirectoryName(directory) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
                Path.GetFileName(directory).StartsWith("sezika-independent-contract-", StringComparison.Ordinal)) Directory.Delete(directory);
        }
    }
}
