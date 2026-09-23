using System.Text;
using System.Text.Json;

namespace Sezika;

/// <summary>Small, allocation-bounded implementation of the pinned tokenizers JSON BPE pipeline.</summary>
public sealed class TokenizerJson
{
    private const char MetaSpace = '\u2581';
    private readonly Dictionary<string, int> _vocabulary;
    private readonly Dictionary<string, int> _mergeRanks;
    private readonly int _unknown;
    private readonly int _bos;
    private readonly int _eos;
    private readonly int _mask;

    public TokenizerJson(string tokenizerJsonPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(tokenizerJsonPath)) throw new DecisionException("tokenizer_asset_missing", "tokenizer.json is missing.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(tokenizerJsonPath), new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        var model = root.GetProperty("model");
        if (!string.Equals(model.GetProperty("type").GetString(), "BPE", StringComparison.Ordinal) ||
            !model.GetProperty("byte_fallback").GetBoolean() || !model.GetProperty("fuse_unk").GetBoolean())
            throw new DecisionException("tokenizer_schema_invalid", "Only the pinned BPE byte-fallback tokenizer is supported.");
        _vocabulary = new Dictionary<string, int>(256_000, StringComparer.Ordinal);
        foreach (var property in model.GetProperty("vocab").EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = property.Value.GetInt32();
            if (!_vocabulary.TryAdd(property.Name, id)) throw new DecisionException("tokenizer_schema_invalid", "Tokenizer vocabulary contains duplicate tokens.");
        }
        _mergeRanks = new Dictionary<string, int>(model.GetProperty("merges").GetArrayLength(), StringComparer.Ordinal);
        var rank = 0;
        foreach (var merge in model.GetProperty("merges").EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pair = merge.ValueKind == JsonValueKind.String ? merge.GetString() : string.Join(' ', merge.EnumerateArray().Select(x => x.GetString()));
            if (string.IsNullOrEmpty(pair) || !_mergeRanks.TryAdd(pair, rank++)) throw new DecisionException("tokenizer_schema_invalid", "Tokenizer merges contain duplicates or invalid pairs.");
        }
        var unkToken = model.GetProperty("unk_token").GetString() ?? "<unk>";
        _unknown = IdOf(unkToken); _bos = IdOf("<bos>"); _eos = IdOf("<eos>"); _mask = IdOf("<mask>");
        if (_vocabulary.Count > 256_000 || _unknown < 0 || _bos < 0 || _eos < 0) throw new DecisionException("tokenizer_schema_invalid", "Tokenizer vocabulary or special tokens are invalid.");
    }

    /// <summary>Special token IDs from the pinned tokenizer template.</summary>
    public int BosId => _bos;
    public int EosId => _eos;
    public int MaskId => _mask;

    public int[] Encode(string text, int maxTokens, bool addSpecialTokens = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxTokens < 2) throw new ArgumentOutOfRangeException(nameof(maxTokens));
        var ids = new List<int>(Math.Min(maxTokens, text.Length + 2));
        if (addSpecialTokens) ids.Add(_bos);
        var normalized = text.Replace(' ', MetaSpace);
        if (normalized.Length != 0 && normalized[0] != MetaSpace) normalized = MetaSpace + normalized;
        foreach (var segment in SplitSegments(normalized))
        {
            var pieces = Bpe(segment);
            foreach (var piece in pieces)
            {
                if (!_vocabulary.TryGetValue(piece, out var id))
                {
                    foreach (var rune in piece.EnumerateRunes())
                    {
                        var bytes = Encoding.UTF8.GetBytes(rune.ToString());
                        foreach (var value in bytes)
                        {
                            if (!_vocabulary.TryGetValue($"<0x{value:X2}>", out id)) id = _unknown;
                            ids.Add(id); if (ids.Count > maxTokens - (addSpecialTokens ? 1 : 0)) throw new DecisionException("decision_token_limit_exceeded", $"Token count exceeds {maxTokens}.");
                        }
                    }
                    continue;
                }
                ids.Add(id); if (ids.Count > maxTokens - (addSpecialTokens ? 1 : 0)) throw new DecisionException("decision_token_limit_exceeded", $"Token count exceeds {maxTokens}.");
            }
        }
        if (addSpecialTokens) ids.Add(_eos);
        return ids.ToArray();
    }

    private IEnumerable<string> Bpe(string segment)
    {
        var symbols = segment.EnumerateRunes().Select(r => r.ToString()).ToList();
        while (symbols.Count > 1)
        {
            var bestIndex = -1; var bestRank = int.MaxValue;
            for (var i = 0; i + 1 < symbols.Count; i++)
            {
                if (_mergeRanks.TryGetValue(symbols[i] + " " + symbols[i + 1], out var rank) && rank < bestRank) { bestRank = rank; bestIndex = i; }
            }
            if (bestIndex < 0) break;
            symbols[bestIndex] += symbols[bestIndex + 1]; symbols.RemoveAt(bestIndex + 1);
        }
        return symbols;
    }

    private static IEnumerable<string> SplitSegments(string normalized)
    {
        if (normalized.Length == 0) yield break;
        var builder = new StringBuilder();
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (rune.Value == MetaSpace)
            {
                if (builder.Length != 0) { yield return builder.ToString(); builder.Clear(); }
                builder.Append(MetaSpace);
            }
            else builder.Append(rune.ToString());
        }
        if (builder.Length != 0) yield return builder.ToString();
    }

    private int IdOf(string token) => _vocabulary.TryGetValue(token, out var id) ? id : throw new DecisionException("tokenizer_schema_invalid", $"Special token '{token}' is absent from vocabulary.");
}
