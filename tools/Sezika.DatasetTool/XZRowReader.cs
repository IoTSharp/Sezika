using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SharpCompress.Compressors.Xz;

namespace Sezika.DatasetTool;

// SharpCompress 1.0.0, MIT, fixed upstream commit
// b6cc95af73950c914e0c6ce7ea3511528ede1121. No executable decoder is used.
public static class XZRowReader
{
    public static IEnumerable<JsonElement> Read(string path, int maximumRows, int timeoutSeconds, CancellationToken token,
        long maximumDecodedBytes = 64L * 1024 * 1024 * 1024)
    {
        if (maximumRows is < 1 or > 5_000_001 || timeoutSeconds is < 1 or > 1800 || maximumDecodedBytes is < 1 or > 64L * 1024 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        var clock = Stopwatch.StartNew(); void Check() { token.ThrowIfCancellationRequested(); if (clock.Elapsed.TotalSeconds >= timeoutSeconds) throw new TimeoutException("XZ decode deadline."); }
        using var file = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read); if (file.Length > 64L * 1024 * 1024 * 1024) throw new InvalidDataException("XZ compressed byte bound.");
        var streams = InspectStreams(file, maximumDecodedBytes, Check);
        using var decoded = new DecodedStreams(file, streams, maximumDecodedBytes, Check); using var reader = new StreamReader(decoded, new UTF8Encoding(false, true), false, 4096, true);
        for (var row = 0; row < maximumRows; row++)
        {
            Check(); var line = new StringBuilder(); var ended = false;
            for (var character = 0; character <= 4 * 1024 * 1024; character++)
            { if ((character & 4095) == 0) Check(); var value = reader.Read(); if (value == -1) { ended = true; break; } if (value == '\n') break; line.Append((char)value); if (character == 4 * 1024 * 1024) throw new InvalidDataException("XZ row character bound."); }
            if (ended && line.Length == 0) yield break; var text = line.ToString().TrimEnd('\r'); if (Encoding.UTF8.GetByteCount(text) > 4 * 1024 * 1024) throw new InvalidDataException("XZ row UTF8 byte bound.");
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64, AllowDuplicateProperties = false }); yield return json.RootElement.Clone(); if (ended) yield break;
        }
    }
    private sealed record StreamPart(long Offset, long Bytes, long DecodedBytes);
    private static StreamPart[] InspectStreams(FileStream file, long maximumDecodedBytes, Action check)
    {
        var result = new List<StreamPart>(); var cursor = file.Length; long decoded = 0;
        for (var stream = 0; stream < 128 && cursor > 0; stream++)
        {
            check(); var padding = 0;
            for (var item = 0; item < 4 * 1024 * 1024 && cursor > 0; item++)
            { file.Position = cursor - 1; if (file.ReadByte() != 0) break; cursor--; padding++; }
            if (padding % 4 != 0 || padding >= 4 * 1024 * 1024 || cursor < 24) throw new InvalidDataException("XZ stream padding/length bound.");
            var footer = new byte[12]; file.Position = cursor - 12; file.ReadExactly(footer); if (footer[10] != 0x59 || footer[11] != 0x5A || Crc(footer.AsSpan(4, 6), check) != BinaryPrimitives.ReadUInt32LittleEndian(footer)) throw new InvalidDataException("XZ footer magic or CRC differs.");
            var indexBytes = checked(((long)BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(4, 4)) + 1) * 4); if (indexBytes is < 8 or > 64 * 1024 * 1024 || indexBytes > cursor - 24) throw new InvalidDataException("XZ index byte bound.");
            var index = new byte[indexBytes]; file.Position = cursor - 12 - indexBytes; file.ReadExactly(index); if (index[0] != 0 || Crc(index.AsSpan(0, index.Length - 4), check) != BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(index.Length - 4))) throw new InvalidDataException("XZ index CRC or indicator differs.");
            var position = 1; var count = Varint(index, ref position, check); if (count > 4096) throw new InvalidDataException("XZ block count4096 bound."); var blocks = new List<(long Unpadded, long Decoded)>(); long blockBytes = 0; long streamDecoded = 0;
            for (ulong block = 0; block < count; block++)
            { check(); var size = checked((long)Varint(index, ref position, check)); var length = checked((long)Varint(index, ref position, check)); if (size < 8) throw new InvalidDataException("XZ unpadded block size."); blockBytes = checked(blockBytes + ((size + 3) & ~3L)); streamDecoded = checked(streamDecoded + length); blocks.Add((size, length)); }
            if (position > index.Length - 4 || index.AsSpan(position, index.Length - 4 - position).IndexOfAnyExcept((byte)0) >= 0) throw new InvalidDataException("XZ index padding differs.");
            var total = checked(12 + blockBytes + indexBytes + 12); var start = cursor - total; if (start < 0) throw new InvalidDataException("XZ stream index size exceeds file."); decoded = checked(decoded + streamDecoded); if (decoded > maximumDecodedBytes) throw new InvalidDataException("XZ declared decoded byte bound.");
            var header = new byte[12]; file.Position = start; file.ReadExactly(header); if (!header.AsSpan(0, 6).SequenceEqual(new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0 }) || !header.AsSpan(6, 2).SequenceEqual(footer.AsSpan(8, 2)) || Crc(header.AsSpan(6, 2), check) != BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8))) throw new InvalidDataException("XZ header flags or CRC differs.");
            var offset = start + 12;
            foreach (var block in blocks)
            { check(); InspectBlock(file, offset, block.Unpadded, check); offset = checked(offset + ((block.Unpadded + 3) & ~3L)); }
            result.Add(new(start, total, streamDecoded)); cursor = start;
        }
        if (cursor != 0 || result.Count == 0) throw new InvalidDataException("XZ stream count128 or leading bytes bound."); result.Reverse(); return result.ToArray();
    }
    private static void InspectBlock(FileStream file, long offset, long unpadded, Action check)
    {
        file.Position = offset; var encoded = file.ReadByte(); if (encoded < 1) throw new InvalidDataException("XZ block header size."); var bytes = (encoded + 1) * 4; if (bytes > unpadded || bytes > 1024) throw new InvalidDataException("XZ block header byte bound.");
        var header = new byte[bytes]; header[0] = (byte)encoded; file.ReadExactly(header.AsSpan(1)); if (Crc(header.AsSpan(0, bytes - 4), check) != BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(bytes - 4))) throw new InvalidDataException("XZ block header CRC differs.");
        var flags = header[1]; if ((flags & 0x3C) != 0) throw new InvalidDataException("XZ reserved block flags."); var position = 2; if ((flags & 0x40) != 0) Varint(header, ref position, check); if ((flags & 0x80) != 0) Varint(header, ref position, check);
        for (var filter = 0; filter < (flags & 3) + 1; filter++)
        {
            check(); var id = Varint(header, ref position, check); var properties = Varint(header, ref position, check); if (properties > 1024 || properties > (ulong)(bytes - 4 - position)) throw new InvalidDataException("XZ filter property bound.");
            if (id == 0x21 && (properties != 1 || header[position] > 28)) throw new InvalidDataException("XZ LZMA2 dictionary exceeds64MiB or invalid properties."); position += checked((int)properties);
        }
        if (position > bytes - 4 || header.AsSpan(position, bytes - 4 - position).IndexOfAnyExcept((byte)0) >= 0) throw new InvalidDataException("XZ block header padding differs.");
    }
    private static ulong Varint(byte[] data, ref int position, Action check)
    {
        ulong result = 0;
        for (var index = 0; index < 9; index++) { check(); if (position >= data.Length) throw new InvalidDataException("XZ varint truncated."); var value = data[position++]; if (index > 0 && value == 0) throw new InvalidDataException("XZ noncanonical varint."); result |= (ulong)(value & 127) << (index * 7); if ((value & 128) == 0) return result; }
        throw new InvalidDataException("XZ varint nine-byte bound.");
    }
    private static uint Crc(ReadOnlySpan<byte> bytes, Action? check = null)
    { if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("XZ CRC byte bound."); uint crc = uint.MaxValue; for (var index = 0; index < bytes.Length; index++) { if ((index & 4095) == 0) check?.Invoke(); crc ^= bytes[index]; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0); } return ~crc; }
    private sealed class PartStream(FileStream source, StreamPart part, Action check) : Stream
    {
        private long read;
        public override int Read(byte[] buffer, int offset, int count) { check(); source.Position = part.Offset + read; var size = source.Read(buffer, offset, (int)Math.Min(count, part.Bytes - read)); read += size; return size; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => part.Bytes; public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class DecodedStreams(FileStream source, StreamPart[] parts, long maximum, Action check) : Stream
    {
        private int current; private XZStream? decoder; private PartStream? compressed; private long read; private long streamRead;
        public override int Read(byte[] buffer, int offset, int count)
        {
            for (var attempt = 0; attempt <= parts.Length; attempt++)
            {
                check(); if (current >= parts.Length) return 0; if (decoder is null) { compressed = new(source, parts[current], check); decoder = new XZStream(compressed); streamRead = 0; }
                var bytes = decoder.Read(buffer, offset, count); read += bytes; streamRead += bytes; if (read > maximum || streamRead > parts[current].DecodedBytes) throw new InvalidDataException("XZ actual decoded byte denominator differs.");
                if (bytes > 0) return bytes; if (streamRead != parts[current].DecodedBytes || compressed!.Position != parts[current].Bytes) throw new InvalidDataException("XZ stream compressed/decoded denominator differs."); decoder.Dispose(); decoder = null; compressed.Dispose(); compressed = null; current++;
            }
            throw new InvalidDataException("XZ stream transition bound.");
        }
        protected override void Dispose(bool disposing) { if (disposing) { decoder?.Dispose(); compressed?.Dispose(); } base.Dispose(disposing); }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => throw new NotSupportedException(); public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    public static int CheckFixture(string output)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var root = Path.GetFullPath(output); if (Path.Exists(root)) throw new IOException("Preserve existing XZ checks output."); Directory.CreateDirectory(root);
        var raw = Encoding.UTF8.GetBytes("{\"value\":\"中文 fixture\"}\n"); var fixture = Fixture(raw); var path = Path.Combine(root, "fixture.jsonl.xz"); File.WriteAllBytes(path, fixture);
        var rows = Read(path, 2, 10, deadline.Token, 4096).ToArray(); if (rows.Length != 1 || rows[0].GetProperty("value").GetString() != "中文 fixture") throw new InvalidDataException("XZ frozen raw row differs.");
        var cases = 1;
        void Rejected(Action action) { try { action(); } catch (Exception error) when (error is InvalidDataException or IOException or OperationCanceledException or ArgumentException) { cases++; return; } throw new InvalidDataException("Invalid XZ fixture accepted."); }
        var truncated = Path.Combine(root, "truncated.jsonl.xz"); File.WriteAllBytes(truncated, fixture[..^1]); Rejected(() => Read(truncated, 2, 10, deadline.Token, 4096).ToArray()); Rejected(() => Read(path, 2, 10, deadline.Token, raw.Length - 1).ToArray());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); Rejected(() => Read(path, 2, 10, cancelled.Token, 4096).ToArray());
        var concat = Path.Combine(root, "concat.jsonl.xz"); File.WriteAllBytes(concat, [.. fixture, 0, 0, 0, 0, .. fixture]); if (Read(concat, 3, 10, deadline.Token, 4096).Count() != 2) throw new InvalidDataException("Concatenated XZ stream row denominator differs."); cases++;
        File.WriteAllText(Path.Combine(root, "checks.txt"), "PASS checks=" + cases + "; source=public-domain XZ format1.2.1; pure C# managed decode; no external process; fixtures preserved\n", new UTF8Encoding(false)); return cases;
    }
    private static byte[] Fixture(byte[] raw)
    {
        if (raw.Length is < 1 or > 65_536) throw new ArgumentException("Tiny XZ fixture bound."); using var result = new MemoryStream();
        void CrcWrite(ReadOnlySpan<byte> bytes) { Span<byte> crc = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc(bytes)); result.Write(crc); }
        result.Write(new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0 }); result.Write(new byte[] { 0, 1 }); CrcWrite(new byte[] { 0, 1 });
        var header = new byte[] { 2, 0, 0x21, 1, 0, 0, 0, 0 }; result.Write(header); CrcWrite(header); result.WriteByte(1); result.WriteByte((byte)((raw.Length - 1) >> 8)); result.WriteByte((byte)(raw.Length - 1)); result.Write(raw); result.WriteByte(0);
        for (var padding = 0; padding < (4 - (raw.Length + 4) % 4) % 4; padding++) result.WriteByte(0); CrcWrite(raw);
        using var index = new MemoryStream(); index.WriteByte(0); index.WriteByte(1);
        void Number(ulong value) { for (var byteIndex = 0; byteIndex < 9; byteIndex++) { var next = (byte)(value & 127); value >>= 7; index.WriteByte(value == 0 ? next : (byte)(next | 128)); if (value == 0) return; } throw new InvalidDataException("Fixture index integer bound."); }
        Number((ulong)(raw.Length + 20)); Number((ulong)raw.Length); var indexPadding = (4 - index.Length % 4) % 4; for (var padding = 0; padding < indexPadding; padding++) index.WriteByte(0); var data = index.ToArray(); result.Write(data); CrcWrite(data);
        var footer = new byte[6]; BinaryPrimitives.WriteUInt32LittleEndian(footer, (uint)((data.Length + 4) / 4 - 1)); footer[5] = 1; CrcWrite(footer); result.Write(footer); result.WriteByte(0x59); result.WriteByte(0x5A); return result.ToArray();
    }
}
