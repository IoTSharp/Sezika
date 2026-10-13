using System.Diagnostics;
using System.Security.Cryptography;

namespace Sezika.Tooling;

internal sealed class Budget(int seconds, string? cancelFile, CancellationToken cancellation)
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double nextProgress = 10;
    internal double ElapsedSeconds => clock.Elapsed.TotalSeconds;

    internal void Check()
    {
        cancellation.ThrowIfCancellationRequested();
        if (seconds is < 1 or > 1800) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (cancelFile is not null && File.Exists(cancelFile)) throw new OperationCanceledException("Cancellation file observed.");
        if (clock.Elapsed.TotalSeconds >= seconds) throw new TimeoutException("Operation deadline exceeded.");
        if (clock.Elapsed.TotalSeconds >= nextProgress)
        {
            Console.Error.WriteLine($"tooling elapsed={clock.Elapsed.TotalSeconds:F1}s budget={seconds}s");
            nextProgress = clock.Elapsed.TotalSeconds + 10;
        }
    }

    internal string Hash(Stream stream, long length)
    {
        Check();
        if (length is < 0 or > ArchiveOperations.MaximumBytes) throw new InvalidDataException("File byte bound exceeded.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        var maximumBlocks = length / buffer.Length + 2; // include an EOF read after a short final block
        for (long block = 0; block < maximumBlocks; block++)
        {
            Check();
            var size = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length - total + 1));
            if (size == 0)
            {
                if (total != length) throw new InvalidDataException("File ended before its declared length.");
                return Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            total += size;
            if (total > length) throw new InvalidDataException("File grew during verification.");
            hash.AppendData(buffer, 0, size);
        }
        throw new InvalidDataException("File read iteration bound exceeded.");
    }
}

internal static class SafePaths
{
    internal static void NoLinks(string path, Budget budget)
    {
        var cursor = Path.GetFullPath(path);
        for (var depth = 0; depth < 64; depth++)
        {
            budget.Check();
            try
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked path ancestry is not allowed.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            var parent = Path.GetDirectoryName(cursor);
            if (parent is null) return;
            cursor = parent;
        }
        throw new InvalidDataException("Path ancestry exceeds 64 levels.");
    }

    internal static string Relative(string name)
    {
        if (name.Length is < 1 or > 1024 || Path.IsPathRooted(name) || name.Contains('\\'))
            throw new InvalidDataException("Unsafe relative path.");
        var parts = name.Split('/');
        if (parts.Length > 64) throw new InvalidDataException("Relative path depth exceeded.");
        foreach (var part in parts)
        {
            if (part.Length is < 1 or > 255 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
                throw new InvalidDataException("Archive paths must use portable ASCII components.");
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9'))
                throw new InvalidDataException("Reserved filename rejected.");
        }
        return string.Join('/', parts);
    }

    internal static string Within(string root, string relative, Budget budget)
    {
        var full = Path.GetFullPath(Path.Combine(root, Relative(relative)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Path escaped the owned directory.");
        NoLinks(full, budget);
        return full;
    }
}
