using System.Buffers.Binary;
using System.Text.Json;

namespace Sezika.ReferenceTool.Laya;

internal sealed class LayaWeights : IDisposable
{
    internal const string Sha256 = "9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204";
    private sealed record Tensor(string Dtype, int[] Shape, long Offset, long Bytes);
    private readonly Dictionary<string, Tensor> _tensors = new(StringComparer.Ordinal);
    private readonly FileStream _stream;
    private readonly Budget _budget;
    internal LayaWeights(FilePin pin, Budget budget)
    {
        _budget = budget; if (pin.Sha256 != Sha256) throw new InvalidDataException("Original Laya safetensors SHA differs."); budget.Verify(pin);
        _stream = new(pin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> prefix = stackalloc byte[8]; _stream.ReadExactly(prefix); var headerLength = BinaryPrimitives.ReadUInt64LittleEndian(prefix);
            if (headerLength is < 2 or > 8388608 || headerLength > (ulong)(_stream.Length - 8)) throw new InvalidDataException("Safetensors header bound8MiB.");
            var bytes = new byte[(int)headerLength]; _stream.ReadExactly(bytes); budget.ValidateJson(bytes); using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var ranges = new List<(long Start, long End)>(); var entries = 0;
            foreach (var item in document.RootElement.EnumerateObject())
            {
                budget.Check(); if (++entries > 512) throw new InvalidDataException("Safetensors tensor/header entry bound512."); if (item.Name == "__metadata__") continue;
                if (!item.Name.StartsWith("encoder.", StringComparison.Ordinal) && !item.Name.StartsWith("head.layers.", StringComparison.Ordinal) &&
                    !item.Name.StartsWith("scorer.", StringComparison.Ordinal) && !item.Name.StartsWith("act_head.", StringComparison.Ordinal) && item.Name is not ("type_emb.weight" or "temperature")) throw new InvalidDataException("Unadmitted Laya tensor family.");
                var data = item.Value; var dtype = data.GetProperty("dtype").GetString()!; var width = dtype switch { "F32" => 4, "F16" or "BF16" => 2, _ => throw new InvalidDataException("Only original floating tensors are supported.") };
                var shape = data.GetProperty("shape").EnumerateArray().Select(axis => axis.GetInt32()).ToArray(); long cells = 1;
                if (shape.Length is < 1 or > 3 || shape.Any(axis => axis is < 1 or > 256000)) throw new InvalidDataException("Tensor rank/dimension differs.");
                foreach (var axis in shape) cells = checked(cells * axis);
                var offsets = data.GetProperty("data_offsets"); if (offsets.GetArrayLength() != 2) throw new InvalidDataException("Tensor offset pair absent.");
                var start = offsets[0].GetInt64(); var end = offsets[1].GetInt64(); var extent = checked(cells * width);
                if (start < 0 || end - start != extent || end > _stream.Length - 8 - (long)headerLength || !_tensors.TryAdd(item.Name, new(dtype, shape, 8 + (long)headerLength + start, extent))) throw new InvalidDataException("Tensor offset/shape/bytes/duplicate differs.");
                ranges.Add((start, end));
            }
            long cursor = 0; foreach (var range in ranges.OrderBy(range => range.Start)) { budget.Check(); if (range.Start != cursor) throw new InvalidDataException("Safetensors overlap/gap differs."); cursor = range.End; }
            if (cursor != _stream.Length - 8 - (long)headerLength || _tensors.Keys.Count(name => name.StartsWith("encoder.", StringComparison.Ordinal)) != 134) throw new InvalidDataException("Complete original safetensors extent/encoder inventory differs.");
        }
        catch { _stream.Dispose(); throw; }
    }
    internal float[] Read(string name, params int[] shape)
    {
        _budget.Check(); var tensor = Require(name, shape); var count = shape.Aggregate(1L, (total, axis) => checked(total * axis));
        if (count > 16777216) throw new InvalidDataException("Single loaded tensor cell bound16M; embeddings use selected rows.");
        var bytes = new byte[checked((int)tensor.Bytes)]; _stream.Position = tensor.Offset; _stream.ReadExactly(bytes); var result = Decode(bytes, tensor.Dtype, _budget); return result;
    }
    internal float[] Embeddings(int[] ids)
    {
        if (ids.Length is < 1 or > 1024 || ids.Any(id => id is < 0 or >= 256000)) throw new InvalidDataException("Laya embedding row scope differs.");
        var tensor = Require("encoder.embeddings.tok_embeddings.weight", [256000, 768]); var width = tensor.Dtype == "F32" ? 4 : 2;
        var result = new float[ids.Length * 768]; var bytes = new byte[768 * width];
        for (var row = 0; row < ids.Length; row++) { _budget.Check(); _stream.Position = tensor.Offset + (long)ids[row] * bytes.Length; _stream.ReadExactly(bytes); Decode(bytes, tensor.Dtype, _budget).CopyTo(result, row * 768); }
        return result;
    }
    private Tensor Require(string name, int[] shape) => _tensors.TryGetValue(name, out var tensor) && tensor.Shape.SequenceEqual(shape) ? tensor : throw new InvalidDataException("Original tensor name/shape mismatch: " + name);
    private static float[] Decode(byte[] bytes, string dtype, Budget budget)
    {
        var width = dtype == "F32" ? 4 : 2; var result = new float[bytes.Length / width];
        for (var cell = 0; cell < result.Length; cell++)
        {
            if ((cell & 4095) == 0) budget.Check(); var span = bytes.AsSpan(cell * width, width);
            result[cell] = dtype switch { "F32" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span)), "BF16" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadUInt16LittleEndian(span) << 16), "F16" => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(span)), _ => throw new InvalidDataException("Unsupported original float dtype.") };
            if (!float.IsFinite(result[cell])) throw new InvalidDataException("Non-finite original Laya weight.");
        }
        return result;
    }
    public void Dispose() => _stream.Dispose();
}
