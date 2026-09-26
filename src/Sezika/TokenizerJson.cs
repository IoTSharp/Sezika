using System.Text;
using System.Text.Json;

namespace Sezika;

/// <summary>Encodes text using a JSON-configured BPE tokenizer with bounded allocations.</summary>
public sealed class TokenizerJson
{
    private const char MetaSpace = '\u2581';
    private readonly Dictionary<string, int> _vocabulary;
    private readonly Dictionary<string, int> _mergeRanks;
    private readonly Dictionary<char, AddedToken[]> _addedTokens;
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
        // Minimal portable fixtures may omit added tokens entirely.
        if (!root.TryGetProperty("added_tokens", out var added)) { _addedTokens = []; return; }
        if (added.GetArrayLength() > 256) throw new DecisionException("tokenizer_schema_invalid", "Added-token inventory exceeds the pinned bound.");
        var entries = new List<AddedToken>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in added.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = item.GetProperty("content").GetString();
            if (string.IsNullOrEmpty(content) || content.Length > 128 || !seen.Add(content) ||
                item.GetProperty("single_word").GetBoolean() || item.GetProperty("normalized").GetBoolean() ||
                item.GetProperty("rstrip").GetBoolean() || IdOf(content) != item.GetProperty("id").GetInt32())
                throw new DecisionException("tokenizer_schema_invalid", "Unsupported added-token contract.");
            entries.Add(new(content, item.GetProperty("id").GetInt32(), item.GetProperty("lstrip").GetBoolean()));
        }
        _addedTokens = entries.GroupBy(item => item.Content[0]).ToDictionary(group => group.Key,
            group => group.OrderByDescending(item => item.Content.Length).ToArray());
    }

    /// <summary>Special token IDs defined by the tokenizer template.</summary>
    public int BosId => _bos;
    public int EosId => _eos;
    public int MaskId => _mask;

    public int[] Encode(string text, int maxTokens, bool addSpecialTokens = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (maxTokens < 2) throw new ArgumentOutOfRangeException(nameof(maxTokens));
        var ids = new List<int>(Math.Min(maxTokens, text.Length + 2));
        if (addSpecialTokens) ids.Add(_bos);
        var limit = maxTokens - (addSpecialTokens ? 1 : 0);
        // Hugging Face extracts raw added tokens before normalization. Each remaining
        // fragment gets its own Metaspace prepend, including text after a newline token.
        var fragmentStart = 0;
        for (var offset = 0; offset < text.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddedToken? matched = null;
            if (_addedTokens.TryGetValue(text[offset], out var candidates))
                foreach (var candidate in candidates)
                    if (text.AsSpan(offset).StartsWith(candidate.Content, StringComparison.Ordinal)) { matched = candidate; break; }
            if (matched is null) { offset++; continue; }
            var fragmentEnd = offset;
            if (matched.LeftStrip)
                while (fragmentEnd > fragmentStart && char.IsWhiteSpace(text[fragmentEnd - 1]))
                { cancellationToken.ThrowIfCancellationRequested(); fragmentEnd--; }
            EncodeFragment(text[fragmentStart..fragmentEnd], ids, limit, cancellationToken);
            ids.Add(matched.Id);
            if (ids.Count > limit) throw new DecisionException("decision_token_limit_exceeded", $"Token count exceeds {maxTokens}.");
            offset += matched.Content.Length;
            fragmentStart = offset;
        }
        EncodeFragment(text[fragmentStart..], ids, limit, cancellationToken);
        if (addSpecialTokens) ids.Add(_eos);
        return ids.ToArray();
    }

    private void EncodeFragment(string text, List<int> ids, int limit, CancellationToken cancellationToken)
    {
        var normalized = text.Replace(' ', MetaSpace);
        if (normalized.Length != 0 && normalized[0] != MetaSpace) normalized = MetaSpace + normalized;
        foreach (var segment in SplitSegments(normalized, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pieces = Bpe(segment, cancellationToken);
            foreach (var piece in pieces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_vocabulary.TryGetValue(piece, out var id))
                {
                    foreach (var rune in piece.EnumerateRunes())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var bytes = Encoding.UTF8.GetBytes(rune.ToString());
                        foreach (var value in bytes)
                        {
                            if (!_vocabulary.TryGetValue($"<0x{value:X2}>", out id)) id = _unknown;
                            ids.Add(id); if (ids.Count > limit) throw new DecisionException("decision_token_limit_exceeded", "Token count exceeds the requested budget.");
                        }
                    }
                    continue;
                }
                ids.Add(id); if (ids.Count > limit) throw new DecisionException("decision_token_limit_exceeded", "Token count exceeds the requested budget.");
            }
        }
    }

    private sealed record AddedToken(string Content, int Id, bool LeftStrip);

    private IEnumerable<string> Bpe(string segment, CancellationToken cancellationToken)
    {
        var symbols = new List<string>(segment.Length);
        foreach (var rune in segment.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            symbols.Add(rune.ToString());
        }
        // Each successful iteration removes one symbol; at most the original
        // segment rune count minus one iterations, with cancellation throughout.
        while (symbols.Count > 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bestIndex = -1; var bestRank = int.MaxValue;
            for (var i = 0; i + 1 < symbols.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_mergeRanks.TryGetValue(symbols[i] + " " + symbols[i + 1], out var rank) && rank < bestRank) { bestRank = rank; bestIndex = i; }
            }
            if (bestIndex < 0) break;
            symbols[bestIndex] += symbols[bestIndex + 1]; symbols.RemoveAt(bestIndex + 1);
        }
        return symbols;
    }

    private static IEnumerable<string> SplitSegments(string normalized, CancellationToken cancellationToken)
    {
        if (normalized.Length == 0) yield break;
        var builder = new StringBuilder();
        foreach (var rune in normalized.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
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
