using System.Buffers.Binary;
using System.Text;

namespace Sezika.ReferenceTool;

internal sealed record TensorDescriptor(string Name, string StorageKey, long Offset, long[] Shape, long[] Stride, long Numel);

// Static data decoder adapted from IndependentModelTool's audited PickleReader.
// No Python globals are executed: only FloatStorage and tensor descriptors exist.
internal sealed class RestrictedPickle(byte[] bytes, Budget budget)
{
    private int _position;
    private readonly List<object?> _stack = [];
    private readonly Dictionary<int, object?> _memo = [];
    private sealed record Mark;
    private sealed record Text(string Value);
    private sealed record Integer(long Value);
    private sealed record Global(string Module, string Name);
    private sealed record Tuple(object?[] Values);
    private sealed record Storage(string Key, long Count);
    private sealed record Tensor(Storage Storage, long Offset, long[] Shape, long[] Stride);

    internal Dictionary<string, TensorDescriptor> Read()
    {
        if (bytes.Length is < 1 or > 4194304) throw Bad("Pickle byte bound4MiB.");
        object? result = null; var stopped = false;
        for (var operation = 0; operation < 1048576 && _position < bytes.Length; operation++)
        {
            if ((operation & 255) == 0) budget.Check();
            switch (Byte())
            {
                case 0x80: if (Byte() is not (2 or 3 or 4)) throw Bad("Unsupported pickle protocol."); break;
                case 0x2e: result = Top(); stopped = true; break;
                case 0x28: Push(new Mark()); break;
                case 0x29: Push(new Tuple([])); break;
                case 0x7d: Push(new Dictionary<object, object?>()); break;
                case 0x5d: Push(new List<object?>()); break;
                case 0x58: Push(new Text(Utf8(checked((int)U32())))); break;
                case 0x55: Push(new Text(Utf8(Byte()))); break;
                case 0x63: Push(new Global(Line(), Line())); break;
                case 0x4b: Push(new Integer(Byte())); break;
                case 0x4d: Push(new Integer(U16())); break;
                case 0x4a: Push(new Integer(unchecked((int)U32()))); break;
                case 0x8a: Push(new Integer(Long())); break;
                case 0x4e: Push(null); break;
                case 0x88: Push(new Integer(1)); break;
                case 0x89: Push(new Integer(0)); break;
                case 0x71: Memo(Byte()); break;
                case 0x72: Memo(checked((int)U32())); break;
                case 0x68: Push(_memo[Byte()]); break;
                case 0x6a: Push(_memo[checked((int)U32())]); break;
                case 0x74: Push(new Tuple(ToMark())); break;
                case 0x85: Push(new Tuple([Pop()])); break;
                case 0x86: { var b = Pop(); var a = Pop(); Push(new Tuple([a, b])); break; }
                case 0x87: { var c = Pop(); var b = Pop(); var a = Pop(); Push(new Tuple([a, b, c])); break; }
                case 0x51: Push(DecodeStorage(Pop())); break;
                case 0x52: Reduce(); break;
                case 0x62: _ = Pop(); break; // Data-only state, never __setstate__.
                case 0x73: { var value = Pop(); var key = Pop(); Set(key, value); break; }
                case 0x75: { var values = ToMark(); if ((values.Length & 1) != 0) throw Bad("Odd SETITEMS."); for (var i = 0; i < values.Length; i += 2) Set(values[i], values[i + 1]); break; }
                case 0x61: { var value = Pop(); if (Top() is not List<object?> list || list.Count >= 65536) throw Bad("APPEND bound."); list.Add(value); break; }
                case 0x65: { var values = ToMark(); if (Top() is not List<object?> list || list.Count + values.Length > 65536) throw Bad("APPENDS bound."); list.AddRange(values); break; }
                case 0x95: { var frame = U64(); if (frame > (ulong)(bytes.Length - _position)) throw Bad("FRAME bound."); break; }
                default: throw Bad("Unsupported pickle opcode.");
            }
            if (stopped) break;
        }
        if (!stopped || result is not Dictionary<object, object?> dictionary || dictionary.Count is < 1 or > 300) throw Bad("Incomplete/oversized state dictionary.");
        var output = new Dictionary<string, TensorDescriptor>(StringComparer.Ordinal);
        foreach (var entry in dictionary)
        {
            budget.Check();
            if (entry.Key is not Text name || entry.Value is not Tensor tensor || name.Value.Length > 256) throw Bad("Unsupported state item.");
            long count = 1; for (var axis = 0; axis < tensor.Shape.Length; axis++) count = checked(count * tensor.Shape[axis]);
            if (tensor.Offset < 0 || count < 1 || tensor.Offset > tensor.Storage.Count - count) throw Bad("Tensor storage extent differs.");
            output.Add(name.Value, new(name.Value, tensor.Storage.Key, tensor.Offset, tensor.Shape, tensor.Stride, count));
        }
        return output;
    }
    private void Reduce()
    {
        var arguments = Pop(); var callable = Pop();
        if (arguments is not Tuple tuple || callable is not Global function) throw Bad("Invalid REDUCE.");
        if (function is { Module: "collections", Name: "OrderedDict" }) { Push(new Dictionary<object, object?>()); return; }
        if (function is not { Module: "torch._utils", Name: "_rebuild_tensor_v2" } || tuple.Values.Length is < 4 or > 8 ||
            tuple.Values[0] is not Storage storage || tuple.Values[1] is not Integer offset || tuple.Values[2] is not Tuple shape || tuple.Values[3] is not Tuple stride)
            throw Bad("Executable/unsupported pickle REDUCE rejected.");
        if (shape.Values.Length is < 1 or > 3 || stride.Values.Length != shape.Values.Length) throw Bad("Tensor rank bound3.");
        Push(new Tensor(storage, offset.Value, shape.Values.Select(Positive).ToArray(), stride.Values.Select(Positive).ToArray()));
    }
    private static Storage DecodeStorage(object? value)
    {
        if (value is not Tuple { Values.Length: 5 } tuple || tuple.Values[0] is not Text { Value: "storage" } ||
            tuple.Values[1] is not Global { Module: "torch", Name: "FloatStorage" } || tuple.Values[2] is not Text key ||
            tuple.Values[3] is not Text { Value: "cpu" } || tuple.Values[4] is not Integer count || count.Value is < 1 or > 536870912 ||
            key.Value.Length is < 1 or > 12 || !key.Value.All(char.IsAsciiDigit)) throw Bad("Unsupported FloatStorage descriptor.");
        return new(key.Value, count.Value);
    }
    private void Set(object? key, object? value)
    {
        if (Top() is not Dictionary<object, object?> dictionary || key is null || dictionary.Count >= 300 || !dictionary.TryAdd(key, value)) throw Bad("Duplicate/oversized SETITEM.");
    }
    private void Memo(int index) { if (index is < 0 or >= 65536 || !_memo.TryAdd(index, Top())) throw Bad("Pickle memo bound/duplicate."); }
    private void Push(object? value) { if (_stack.Count >= 65536) throw Bad("Pickle stack bound."); _stack.Add(value); }
    private object? Pop() { if (_stack.Count == 0) throw Bad("Pickle stack underflow."); var value = _stack[^1]; _stack.RemoveAt(_stack.Count - 1); return value; }
    private object? Top() => _stack.Count == 0 ? throw Bad("Pickle stack underflow.") : _stack[^1];
    private object?[] ToMark()
    {
        var result = new List<object?>();
        for (var index = 0; index < 65536 && _stack.Count > 0; index++) { var value = Pop(); if (value is Mark) { result.Reverse(); return result.ToArray(); } result.Add(value); }
        throw Bad("Unbalanced pickle MARK.");
    }
    private byte Byte() => _position < bytes.Length ? bytes[_position++] : throw Bad("Truncated pickle.");
    private uint U32() { if (_position > bytes.Length - 4) throw Bad("Truncated pickle."); var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(_position, 4)); _position += 4; return value; }
    private ushort U16() { if (_position > bytes.Length - 2) throw Bad("Truncated pickle."); var value = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(_position, 2)); _position += 2; return value; }
    private ulong U64() { if (_position > bytes.Length - 8) throw Bad("Truncated pickle."); var value = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(_position, 8)); _position += 8; return value; }
    private long Long() { var size = Byte(); if (size is < 1 or > 8 || _position > bytes.Length - size) throw Bad("LONG1 bound."); long result = 0; for (var i = 0; i < size; i++) result |= (long)Byte() << (8 * i); return result; }
    private string Utf8(int length) { if (length is < 0 or > 65536 || _position > bytes.Length - length) throw Bad("Pickle text bound."); var result = new UTF8Encoding(false, true).GetString(bytes, _position, length); _position += length; return result; }
    private string Line() { var start = _position; for (var i = 0; i < 1024 && _position < bytes.Length; i++) if (Byte() == 10) return Encoding.ASCII.GetString(bytes, start, _position - start - 1); throw Bad("Pickle global line bound."); }
    private static long Positive(object? value) => value is Integer number && number.Value is > 0 and <= 256000 ? number.Value : throw Bad("Shape/stride bound.");
    private static InvalidDataException Bad(string message) => new(message);
}
