using System.Text;
using System.Text.Json;

namespace Sezika.ReferenceTool.Tokenization;

internal sealed class FrozenTokenizer
{
    internal const string Sha256 = "609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f";
    private sealed record Added(int Id, string Content, bool LeftStrip);
    private readonly Added[] _added;
    private readonly BpeModel _model;
    internal string[] AddedContents => _added.Select(item => item.Content).ToArray();
    internal int TokenId(string content) => _model.Id(content);
    internal const int Bos = 2, Eos = 1, Mask = 4, Pad = 0;
    internal FrozenTokenizer(FilePin pin, Budget budget)
    {
        if (pin.Sha256 != Sha256) throw new InvalidDataException("Fixed Laya tokenizer SHA differs.");
        var bytes = budget.Bytes(pin, 67108864);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 16 }); var tokens = 0; var scopes = new Stack<HashSet<string>?>();
        while (reader.Read())
        {
            if (++tokens > 4000000) throw new InvalidDataException("Tokenizer JSON token bound4M."); if ((tokens & 1023) == 0) budget.Check();
            if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.StartArray) scopes.Push(null);
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) scopes.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !(scopes.Peek()?.Add(reader.GetString()!) ?? false)) throw new InvalidDataException("Duplicate tokenizer property.");
        }
        if (scopes.Count != 0) throw new InvalidDataException("Incomplete tokenizer JSON.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }); var root = document.RootElement;
        var normalizer = root.GetProperty("normalizer"); var pre = root.GetProperty("pre_tokenizer");
        if (root.GetProperty("version").GetString() != "1.0" || root.GetProperty("truncation").ValueKind != JsonValueKind.Null || root.GetProperty("padding").ValueKind != JsonValueKind.Null ||
            normalizer.GetProperty("type").GetString() != "Replace" || normalizer.GetProperty("pattern").GetProperty("String").GetString() != " " || normalizer.GetProperty("content").GetString() != "▁" ||
            pre.GetProperty("type").GetString() != "Metaspace" || pre.GetProperty("replacement").GetString() != "▁" || pre.GetProperty("prepend_scheme").GetString() != "always" || !pre.GetProperty("split").GetBoolean())
            throw new InvalidDataException("Frozen whitespace/metaspace pipeline differs.");
        var decoder = root.GetProperty("decoder"); var decoders = decoder.GetProperty("decoders");
        if (decoder.GetProperty("type").GetString() != "Sequence" || decoders.GetArrayLength() != 3 ||
            decoders[0].GetProperty("type").GetString() != "Replace" || decoders[0].GetProperty("pattern").GetProperty("String").GetString() != "▁" ||
            decoders[0].GetProperty("content").GetString() != " " || decoders[1].GetProperty("type").GetString() != "ByteFallback" || decoders[2].GetProperty("type").GetString() != "Fuse")
            throw new InvalidDataException("Frozen replacement/byte-fallback/fuse decoder sequence differs.");
        var added = new List<Added>(); var names = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<int>();
        foreach (var token in root.GetProperty("added_tokens").EnumerateArray())
        {
            budget.Check(); var content = token.GetProperty("content").GetString()!; var id = token.GetProperty("id").GetInt32();
            if (added.Count >= 249 || content.Length is < 1 or > 256 || !names.Add(content) || !ids.Add(id) || token.GetProperty("normalized").GetBoolean() ||
                token.GetProperty("single_word").GetBoolean() || token.GetProperty("rstrip").GetBoolean()) throw new InvalidDataException("Frozen added-token inventory/flags differ.");
            added.Add(new(id, content, token.GetProperty("lstrip").GetBoolean()));
        }
        if (added.Count != 249) throw new InvalidDataException("Exactly249 added tokens required."); _added = added.ToArray(); _model = new(root.GetProperty("model"), budget);
        foreach (var token in _added) if (_model.Id(token.Content) != token.Id) throw new InvalidDataException("Added token/model ID mismatch.");
        if (_model.Id("<bos>") != Bos || _model.Id("<eos>") != Eos || _model.Id("<mask>") != Mask || _model.Id("<pad>") != Pad) throw new InvalidDataException("Special IDs differ.");
    }
    internal int[] Encode(string text, bool special, Budget budget)
    {
        if (text.Length > 131072) throw new InvalidDataException("Tokenizer text bound131072 UTF16 units.");
        // Reject malformed UTF16 rather than silently introducing replacement characters.
        for (var index = 0; index < text.Length; index++)
        {
            if ((index & 255) == 0) budget.Check(); if (!char.IsSurrogate(text[index])) continue;
            if (!char.IsHighSurrogate(text[index]) || index + 1 >= text.Length || !char.IsLowSurrogate(text[++index])) throw new InvalidDataException("Unpaired Unicode surrogate.");
        }
        var result = new List<int>(); if (special) result.Add(Bos); var start = 0;
        for (var position = 0; position < text.Length; position++)
        {
            if ((position & 255) == 0) budget.Check(); Added? found = null;
            foreach (var token in _added)
                if (text.AsSpan(position).StartsWith(token.Content, StringComparison.Ordinal) && (found is null || token.Content.Length > found.Content.Length)) found = token;
            if (found is null) continue;
            var end = position; if (found.LeftStrip) for (var trim = 0; trim < text.Length && end > start && char.IsWhiteSpace(text[end - 1]); trim++) end--;
            Ordinary(text[start..end], result, budget); result.Add(found.Id); position += found.Content.Length - 1; start = position + 1;
        }
        Ordinary(text[start..], result, budget); if (special) result.Add(Eos); if (result.Count > 65536) throw new InvalidDataException("Tokenizer output token bound65536."); return result.ToArray();
    }
    private void Ordinary(string text, List<int> target, Budget budget)
    {
        if (text.Length == 0) return; text = text.Replace(' ', '▁'); if (text[0] != '▁') text = "▁" + text;
        var start = 0;
        for (var index = 1; index <= text.Length; index++)
        {
            if ((index & 255) == 0) budget.Check(); if (index != text.Length && text[index] != '▁') continue;
            target.AddRange(_model.EncodeWord(text[start..index], budget)); if (target.Count > 65536) throw new InvalidDataException("Tokenizer output token bound65536."); start = index;
        }
    }
    internal string Decode(int[] ids, Budget budget)
    {
        if (ids.Length > 65536) throw new InvalidDataException("Decode token bound65536."); var text = new StringBuilder(); var bytes = new List<byte>();
        void Flush()
        {
            if (bytes.Count == 0) return;
            try { text.Append(new UTF8Encoding(false, true).GetString(bytes.ToArray())); }
            catch (DecoderFallbackException) { text.Append('\ufffd', bytes.Count); } // tokenizers0.22.2 replaces every byte in an invalid contiguous group.
            bytes.Clear();
        }
        foreach (var id in ids)
        {
            budget.Check(); var word = _model.Word(id).Replace('▁', ' ');
            if (word.Length == 6 && word.StartsWith("<0x", StringComparison.Ordinal) && word[5] == '>' && byte.TryParse(word.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value)) bytes.Add(value);
            else { Flush(); text.Append(word); }
            if (text.Length + bytes.Count > 262144) throw new InvalidDataException("Decode character bound262144.");
        }
        Flush(); return text.ToString();
    }
}
