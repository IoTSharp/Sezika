using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sezika.ReferenceTool;

internal sealed class Budget(int seconds, long maximumResident, string? cancelFile, CancellationToken cancellation)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _nextMemory;
    internal double Elapsed => _clock.Elapsed.TotalSeconds;
    internal long PeakBytes { get; private set; }
    internal void Check()
    {
        cancellation.ThrowIfCancellationRequested();
        if (_clock.Elapsed.TotalSeconds >= seconds) throw new TimeoutException("Reference wall bound exceeded.");
        if (cancelFile is not null && File.Exists(cancelFile)) throw new OperationCanceledException("Reference cancellation file exists.");
        if (Elapsed >= _nextMemory)
        {
            using var process = Process.GetCurrentProcess();
            PeakBytes = Math.Max(PeakBytes, process.WorkingSet64); _nextMemory = Elapsed + .5;
            if (PeakBytes > maximumResident) throw new InvalidDataException("Reference observed resident bound exceeded.");
        }
    }
    internal static void Safe(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)) throw new InvalidDataException("Absolute local path required.");
        var cursor = Path.GetFullPath(path);
        for (var depth = 0; depth < 64; depth++)
        {
            if (Path.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reference paths must not traverse links.");
            var parent = Path.GetDirectoryName(cursor); if (string.IsNullOrEmpty(parent) || parent == cursor) return; cursor = parent;
        }
        throw new InvalidDataException("Reference ancestor bound64 exceeded.");
    }
    internal FilePin Pin(string path, long maximum = 2147483648)
    {
        Safe(path); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 0 || stream.Length > maximum) throw new InvalidDataException("Reference input byte bound exceeded.");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1048576]; long readBytes = 0;
        for (var block = 0; block < 2049 && readBytes < stream.Length; block++)
        {
            Check(); var count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, stream.Length - readBytes));
            if (count == 0) throw new EndOfStreamException(); sha.AppendData(buffer, 0, count); readBytes += count;
        }
        if (readBytes != stream.Length) throw new InvalidDataException("Reference hash block bound2049 exceeded.");
        return new(Path.GetFullPath(path), stream.Length, Convert.ToHexStringLower(sha.GetHashAndReset()));
    }
    internal FilePin Verify(FilePin expected, long maximum = 2147483648)
    {
        var actual = Pin(expected.Path, maximum);
        if (actual.Bytes != expected.Bytes || actual.Sha256 != expected.Sha256.ToLowerInvariant()) throw new InvalidDataException("Input bytes/SHA differ: " + expected.Path);
        return actual;
    }
    internal byte[] Bytes(FilePin pin, int maximum)
    {
        Verify(pin, maximum); using var stream = File.OpenRead(pin.Path); var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); Check();
        if (bytes.Length != pin.Bytes || Sha(bytes) != pin.Sha256.ToLowerInvariant()) throw new InvalidDataException("Input changed while reading its bound bytes."); return bytes;
    }
    internal T Read<T>(FilePin pin, JsonTypeInfo<T> type, int maximum = 1048576)
    {
        var bytes = Bytes(pin, maximum); ValidateJson(bytes); return JsonSerializer.Deserialize(bytes, type) ?? throw new InvalidDataException("Missing reference JSON.");
    }
    internal void ValidateJson(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 }); var scopes = new Stack<HashSet<string>?>(); var tokens = 0;
        while (reader.Read())
        {
            if (++tokens > 200000) throw new InvalidDataException("Reference JSON token bound200000.");
            if ((tokens & 255) == 0) Check();
            if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.StartArray) scopes.Push(null);
            else if (reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject) scopes.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !(scopes.Peek()?.Add(reader.GetString()!) ?? false)) throw new InvalidDataException("Duplicate reference JSON property.");
            else if (reader.TokenType == JsonTokenType.Number && (!reader.TryGetDouble(out var value) || !double.IsFinite(value))) throw new InvalidDataException("Non-finite reference JSON number.");
        }
        if (scopes.Count != 0) throw new InvalidDataException("Incomplete reference JSON.");
    }
    internal static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static void WriteNew(string path, byte[] bytes)
    {
        Safe(path); using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); stream.Write(bytes); stream.Flush(true);
    }
}
