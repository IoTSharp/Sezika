using System.Buffers.Binary;
using System.Text;

namespace Sezika.SshFixture;

internal sealed class WireReader(byte[] bytes)
{
    private int position;
    internal int Remaining => bytes.Length - position;
    internal byte Byte() { Need(1); return bytes[position++]; }
    internal uint UInt32() { Need(4); var value = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position)); position += 4; return value; }
    internal ulong UInt64() { Need(8); var value = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(position)); position += 8; return value; }
    internal byte[] Bytes(int maximum = 1048576)
    {
        var length = UInt32(); if (length > maximum) throw new InvalidDataException("Fixture wire string cap exceeded."); Need((int)length);
        var result = bytes.AsSpan(position, (int)length).ToArray(); position += (int)length; return result;
    }
    internal string Text(int maximum = 1024) => new UTF8Encoding(false, true).GetString(Bytes(maximum));
    private void Need(int count) { if (count < 0 || count > Remaining) throw new InvalidDataException("Fixture wire packet truncated."); }
}

internal sealed class WireWriter : IDisposable
{
    private readonly MemoryStream memory = new();
    internal void Byte(byte value) => memory.WriteByte(value);
    internal void UInt32(uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); memory.Write(bytes); }
    internal void UInt64(ulong value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); memory.Write(bytes); }
    internal void Bytes(ReadOnlySpan<byte> value) { UInt32((uint)value.Length); memory.Write(value); }
    internal void Text(string value) => Bytes(Encoding.UTF8.GetBytes(value));
    internal byte[] ToArray() => memory.ToArray();
    internal void Clear() { memory.SetLength(0); memory.Position = 0; }
    public void Dispose() => memory.Dispose();
}
