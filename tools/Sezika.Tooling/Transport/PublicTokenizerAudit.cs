using System.Text.Json;

namespace Sezika.Tooling;

internal static partial class AssetTransport
{
    private static void ReviewPublicTokenizer(string path, long bytes, Budget budget)
    {
        Require(bytes is >= 1 and <= 64 * 1024 * 1024, "Public tokenizer JSON byte cap exceeded.");
        RegularFiles.Require(path, budget); using var file = File.OpenRead(path);
        using var document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        Require(root.ValueKind == JsonValueKind.Object, "Tokenizer root must be a JSON object.");
        var permitted = new HashSet<string>(["version", "truncation", "padding", "added_tokens", "normalizer", "pre_tokenizer", "post_processor", "decoder", "model"], StringComparer.Ordinal);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            budget.Check(); Require(permitted.Contains(property.Name) && roots.Add(property.Name), "Tokenizer root contains duplicate or unsupported content.");
        }
        Require(roots.Contains("model"), "Tokenizer model schema omitted.");
        var model = root.GetProperty("model");
        Require(model.ValueKind == JsonValueKind.Object && model.TryGetProperty("type", out var kind) && kind.GetString() is "BPE" or "WordPiece" or "Unigram" or "WordLevel", "Unsupported public tokenizer model schema.");
        Require(model.TryGetProperty("vocab", out var vocab), "Tokenizer vocabulary omitted.");
        var ids = new HashSet<long>(); var tokens = 0;
        if (vocab.ValueKind == JsonValueKind.Object)
        {
            foreach (var token in vocab.EnumerateObject())
            {
                budget.Check(); Require(++tokens <= 300000 && token.Value.ValueKind == JsonValueKind.Number && token.Value.TryGetInt64(out var id) &&
                    id is >= 0 and <= 2000000 && ids.Add(id), "Tokenizer vocabulary count/ID identity differs."); ReviewPublicText(token.Name);
            }
        }
        else if (vocab.ValueKind == JsonValueKind.Array)
        {
            Require(vocab.GetArrayLength() is >= 1 and <= 300000, "Unigram vocabulary count cap exceeded.");
            foreach (var token in vocab.EnumerateArray())
            {
                budget.Check(); Require(token.ValueKind == JsonValueKind.Array && token.GetArrayLength() == 2 && token[0].ValueKind == JsonValueKind.String &&
                    token[1].ValueKind == JsonValueKind.Number && double.IsFinite(token[1].GetDouble()), "Unigram vocabulary tuple differs."); tokens++; ReviewPublicText(token[0].GetString()!);
            }
        }
        else throw new InvalidDataException("Tokenizer vocab must be an ID map or Unigram tuples.");
        Require(tokens > 0, "Empty public vocabulary.");
        var pending = new Stack<(JsonElement Value, bool Vocabulary)>(); pending.Push((root, false));
        for (var node = 0; node < 1500000; node++)
        {
            budget.Check(); if (pending.Count == 0) return;
            var item = pending.Pop(); var current = item.Value;
            if (current.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in current.EnumerateObject())
                {
                    budget.Check(); Require(names.Count < (item.Vocabulary ? 300000 : 16384) && names.Add(property.Name) && pending.Count < 1500000,
                        "Tokenizer duplicate/object/node cap exceeded.");
                    if (!item.Vocabulary)
                    {
                        var normalized = property.Name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
                        Require(normalized is not ("recordid" or "candidateids" or "target" or "probability" or "probabilities" or "password" or "token" or "secret" or "privatekey" or "sourceroot" or "rows"),
                            "Tokenizer contains report-level or secret content.");
                    }
                    pending.Push((property.Value, property.Name == "vocab"));
                }
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                Require(current.GetArrayLength() <= 300000 && pending.Count + current.GetArrayLength() <= 1500000, "Tokenizer array/node cap exceeded.");
                foreach (var value in current.EnumerateArray()) pending.Push((value, false));
            }
            else if (current.ValueKind == JsonValueKind.String) ReviewPublicText(current.GetString()!);
        }
        throw new InvalidDataException("Tokenizer node count cap exceeded.");
    }
}
