using System.Text;
using System.Text.Json;

namespace Sezika.ReferenceTool.Tokenization;

// Independent implementation of the frozen tokenizer.json BPE model, Apache-2.0.
internal sealed class BpeModel
{
    private readonly Dictionary<string, int> _vocabulary = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Left, int Right), (int Rank, int Id)> _merges = [];
    private readonly string[] _words = new string[256000];
    private readonly int[] _bytes = new int[256];
    private readonly int _unknown;
    private readonly record struct Merge(int Left, int Right, int LeftVersion, int RightVersion, int Id);
    internal BpeModel(JsonElement model, Budget budget)
    {
        if (model.GetProperty("type").GetString() != "BPE" || !model.GetProperty("byte_fallback").GetBoolean() ||
            model.GetProperty("ignore_merges").GetBoolean() || !model.GetProperty("fuse_unk").GetBoolean() ||
            model.GetProperty("dropout").ValueKind != JsonValueKind.Null || model.GetProperty("continuing_subword_prefix").ValueKind != JsonValueKind.Null ||
            model.GetProperty("end_of_word_suffix").ValueKind != JsonValueKind.Null || model.GetProperty("unk_token").GetString() != "<unk>")
            throw new InvalidDataException("Frozen BPE configuration differs.");
        var count = 0;
        foreach (var item in model.GetProperty("vocab").EnumerateObject())
        {
            if (++count > 256000) throw new InvalidDataException("Vocabulary count bound256000.");
            if ((count & 255) == 0) budget.Check(); var id = item.Value.GetInt32();
            if (id is < 0 or >= 256000 || _words[id] is not null || !_vocabulary.TryAdd(item.Name, id)) throw new InvalidDataException("Vocabulary ID/name duplicate or range differs.");
            _words[id] = item.Name;
        }
        if (count != 256000 || _words.Any(word => word is null)) throw new InvalidDataException("Complete frozen vocabulary absent.");
        const int frozenMergeCount = 580604;
        var merges = model.GetProperty("merges");
        if (merges.ValueKind != JsonValueKind.Array || merges.GetArrayLength() != frozenMergeCount)
            throw new InvalidDataException("Complete frozen BPE merge denominator580604 differs.");
        var rank = 0;
        foreach (var pair in merges.EnumerateArray())
        {
            if (rank >= frozenMergeCount || pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2) throw new InvalidDataException("Merge format/count differs.");
            if ((rank & 255) == 0) budget.Check(); var left = pair[0].GetString()!; var right = pair[1].GetString()!;
            if (!_vocabulary.TryGetValue(left, out var a) || !_vocabulary.TryGetValue(right, out var b) || !_vocabulary.TryGetValue(left + right, out var merged) ||
                !_merges.TryAdd((a, b), (rank++, merged))) throw new InvalidDataException("Merge operands/result/duplicate differs.");
        }
        if (rank != frozenMergeCount) throw new InvalidDataException("Complete frozen BPE merge iteration differs.");
        _unknown = Id("<unk>");
        for (var value = 0; value < 256; value++)
        {
            budget.Check(); _bytes[value] = _vocabulary.TryGetValue("<0x" + value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + ">", out var byteId) ? byteId : -1;
        }
    }
    internal int Id(string word) => _vocabulary.TryGetValue(word, out var id) ? id : throw new InvalidDataException("Frozen token missing: " + word);
    internal string Word(int id) => id is >= 0 and < 256000 ? _words[id] : throw new InvalidDataException("Token ID out of range.");
    internal int[] EncodeWord(string word, Budget budget)
    {
        if (word.Length > 32768) throw new InvalidDataException("Single BPE piece character bound32768.");
        var cells = new List<int>(); Span<byte> encoded = stackalloc byte[4]; var characters = 0; var pendingUnknown = false;
        foreach (var rune in word.EnumerateRunes())
        {
            if (++characters > 32768) throw new InvalidDataException("BPE rune bound32768."); if ((characters & 255) == 0) budget.Check();
            if (_vocabulary.TryGetValue(rune.ToString(), out var id))
            {
                if (pendingUnknown) { cells.Add(_unknown); pendingUnknown = false; } cells.Add(id);
            }
            else
            {
                var size = rune.EncodeToUtf8(encoded); var complete = true;
                for (var i = 0; i < size; i++) if (_bytes[encoded[i]] < 0) complete = false;
                if (complete)
                {
                    // tokenizers0.22.2 emits a fallback only when every byte token exists.
                    // Its fallback branch continues before flushing a pending fused unknown.
                    for (var i = 0; i < size; i++) cells.Add(_bytes[encoded[i]]);
                }
                else pendingUnknown = true; // fuse_unk=true is part of the admitted frozen model.
            }
        }
        if (pendingUnknown) cells.Add(_unknown);
        if (cells.Count is < 1 or > 131072) throw new InvalidDataException("BPE symbol count differs.");
        var ids = cells.ToArray(); var previous = new int[ids.Length]; var next = new int[ids.Length]; var versions = new int[ids.Length]; var alive = new bool[ids.Length];
        var queue = new PriorityQueue<Merge, (int Rank, int Position)>(); var enqueued = 0;
        void Schedule(int left)
        {
            if (left < 0 || !alive[left] || next[left] < 0 || !_merges.TryGetValue((ids[left], ids[next[left]]), out var merge)) return;
            if (++enqueued > checked(ids.Length * 3)) throw new InvalidDataException("BPE queued-merge bound exceeded.");
            var right = next[left]; queue.Enqueue(new(left, right, versions[left], versions[right], merge.Id), (merge.Rank, left));
        }
        for (var index = 0; index < ids.Length; index++) { previous[index] = index - 1; next[index] = index + 1 < ids.Length ? index + 1 : -1; alive[index] = true; }
        for (var index = 0; index < ids.Length; index++) { if ((index & 255) == 0) budget.Check(); Schedule(index); }
        for (var iteration = 0; iteration < checked(ids.Length * 3); iteration++)
        {
            if ((iteration & 255) == 0) budget.Check(); if (!queue.TryDequeue(out var merge, out _)) break;
            if (!alive[merge.Left] || !alive[merge.Right] || next[merge.Left] != merge.Right || versions[merge.Left] != merge.LeftVersion || versions[merge.Right] != merge.RightVersion) continue;
            ids[merge.Left] = merge.Id; versions[merge.Left]++; alive[merge.Right] = false; next[merge.Left] = next[merge.Right];
            if (next[merge.Left] >= 0) previous[next[merge.Left]] = merge.Left;
            Schedule(previous[merge.Left]); Schedule(merge.Left);
        }
        if (queue.Count != 0) throw new InvalidDataException("BPE merge iteration bound exceeded.");
        var result = new List<int>(); for (var index = 0; index < ids.Length; index++) { if ((index & 255) == 0) budget.Check(); if (alive[index]) result.Add(ids[index]); }
        return result.ToArray();
    }
}
