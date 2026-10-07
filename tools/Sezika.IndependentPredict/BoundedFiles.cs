using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sezika.IndependentPredict;

internal static class BoundedFiles
{
    internal const int MaxBytes = 4 * 1024 * 1024;
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    internal static bool Same(string? left, string? right) => IsHash(left) && IsHash(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    internal static void Require([DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    internal static byte[] Read(string path, CancellationToken token, int maximum = MaxBytes, bool allowEmpty = false)
    {
        token.ThrowIfCancellationRequested();
        CheckLinks(path, token);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require((allowEmpty || stream.Length > 0) && stream.Length <= maximum, "File exceeds its fixed byte limit.");
        var bytes = new byte[(int)stream.Length];
        var clock = Stopwatch.StartNew();
        for (var offset = 0; offset < bytes.Length;)
        {
            token.ThrowIfCancellationRequested();
            Require(clock.Elapsed < TimeSpan.FromSeconds(20), "File read deadline exceeded.");
            var read = stream.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
            Require(read > 0, "File changed during bounded read.");
            offset += read;
        }
        return bytes;
    }

    internal static string Resolve(string root, ArtifactReference artifact, CancellationToken token)
    {
        Require(artifact is not null && !string.IsNullOrWhiteSpace(artifact.Path) && !Path.IsPathRooted(artifact.Path) && IsHash(artifact.Sha256), "Invalid relative artifact reference.");
        var path = Path.GetFullPath(Path.Combine(root, artifact.Path));
        var relative = Path.GetRelativePath(root, path);
        Require(!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Artifact escapes manifest directory.");
        CheckLinks(path, token);
        return path;
    }

    internal static byte[] Verify(string root, ArtifactReference artifact, CancellationToken token, int maximum = MaxBytes)
    {
        var bytes = Read(Resolve(root, artifact, token), token, maximum);
        Require(Same(Hash(bytes), artifact.Sha256), "Artifact SHA-256 does not match.");
        return bytes;
    }

    internal static void CheckLinks(string path, CancellationToken token)
    {
        var cursor = Path.GetFullPath(path);
        for (var depth = 0; depth < 128; depth++)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(cursor) || Directory.Exists(cursor))
                Require((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) == 0, "Artifact ancestor is a link or junction.");
            var parent = Path.GetDirectoryName(cursor);
            if (parent is null || parent == cursor) return;
            cursor = parent;
        }
        throw new InvalidDataException("Artifact path depth exceeds limit.");
    }

    internal static T Json<T>(ReadOnlySpan<byte> bytes, JsonTypeInfo<T> type, CancellationToken token)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 });
        var scopes = new Stack<HashSet<string>>();
        var clock = Stopwatch.StartNew();
        var nodes = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            Require(++nodes <= 131072 && clock.Elapsed < TimeSpan.FromSeconds(10), "JSON parse budget exceeded.");
            if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) scopes.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName)
                Require(scopes.Peek().Add(reader.GetString()!), "JSON contains duplicate properties.");
        }
        return JsonSerializer.Deserialize(bytes, type) ?? throw new InvalidDataException("JSON is null.");
    }
}
