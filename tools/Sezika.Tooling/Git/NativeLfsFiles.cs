using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Sezika.Tooling;

internal sealed record LfsStoredObject(string Path, long Bytes, string Sha256, bool Reused);
internal sealed record LfsHydratedFile(string Path, string ObjectPath, long Bytes, string Sha256);

// Git filters remain disabled. Pointer and object-store bytes are produced by
// this bounded C# path; the caller supplies an independently verified Git dir.
internal static class NativeLfsFiles
{
    internal const int MaximumPointerBytes = 256;
    private static ReadOnlySpan<byte> Version => "version https://git-lfs.github.com/spec/v1\n"u8;
    private static ReadOnlySpan<byte> OidPrefix => "oid sha256:"u8;
    private static ReadOnlySpan<byte> SizePrefix => "size "u8;

    internal static bool TryParsePointer(ReadOnlySpan<byte> pointer, out string oid, out long bytes)
    {
        oid = ""; bytes = 0;
        if (pointer.Length > MaximumPointerBytes || !pointer.StartsWith(Version)) return false;
        var remainder = pointer[Version.Length..];
        if (!remainder.StartsWith(OidPrefix)) return false;
        remainder = remainder[OidPrefix.Length..];
        if (remainder.Length < 64 + 1 + SizePrefix.Length + 2 || remainder[64] != (byte)'\n') return false;
        var digest = remainder[..64];
        for (var index = 0; index < 64; index++)
            if (digest[index] is not (>= (byte)'0' and <= (byte)'9') and not (>= (byte)'a' and <= (byte)'f')) return false;
        remainder = remainder[65..];
        if (!remainder.StartsWith(SizePrefix)) return false;
        remainder = remainder[SizePrefix.Length..];
        if (remainder.Length is < 2 or > 20 || remainder[^1] != (byte)'\n') return false;
        var decimalBytes = remainder[..^1];
        if (decimalBytes.Length > 1 && decimalBytes[0] == (byte)'0') return false;
        long size = 0;
        for (var index = 0; index < decimalBytes.Length; index++)
        {
            var digit = decimalBytes[index] - (byte)'0';
            if (digit is < 0 or > 9 || size > (long.MaxValue - digit) / 10) return false;
            size = size * 10 + digit;
        }
        oid = Encoding.ASCII.GetString(digest); bytes = size;
        return true;
    }

    internal static byte[] CreatePointer(long bytes, string oid)
    {
        if (bytes < 0) throw new InvalidDataException("LFS pointer size must be nonnegative.");
        TaskOperations.Sha(oid);
        return Encoding.ASCII.GetBytes("version https://git-lfs.github.com/spec/v1\noid sha256:" + oid + "\nsize " + bytes.ToString(CultureInfo.InvariantCulture) + "\n");
    }

    internal static LfsHydratedFile HydrateFreshPointer(string repository, string gitDirectory, string relativePath,
        ReadOnlySpan<byte> committedPointer, long bytes, string sha256, Budget budget)
    {
        budget.Check(); TaskOperations.Sha(sha256);
        if (bytes is < 0 or > ArchiveOperations.MaximumBytes || !TryParsePointer(committedPointer, out var oid, out var pointerBytes) ||
            oid != sha256 || pointerBytes != bytes || !committedPointer.SequenceEqual(CreatePointer(bytes, sha256)))
            throw new InvalidDataException("Fresh LFS hydration requires the exact canonical committed pointer and manifest bytes/SHA.");
        var root = Path.GetFullPath(repository); var gitRoot = Path.GetFullPath(gitDirectory);
        if (gitRoot != Path.Combine(root, ".git")) throw new InvalidDataException("Hydration Git directory differs from the verified fresh clone.");
        var destination = SafePaths.Within(root, relativePath, budget);
        var pointerSha = Convert.ToHexStringLower(SHA256.HashData(committedPointer));
        TaskOperations.VerifyFile(destination, committedPointer.Length, pointerSha, budget);
        var source = SafePaths.Within(gitRoot, "lfs/objects/" + sha256[..2] + "/" + sha256.Substring(2, 2) + "/" + sha256, budget);
        TaskOperations.VerifyFile(source, bytes, sha256, budget);
        var partial = SafePaths.Within(gitRoot, "lfs/tmp/hydrate-" + Guid.NewGuid().ToString("N") + ".partial", budget);
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        // The partial is in this exact clone's Git dir for a same-volume atomic
        // replacement. On failure it remains explicit task-owned evidence.
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (input.Length != bytes) throw new InvalidDataException("Downloaded LFS object length changed.");
            var buffer = new byte[1048576]; long total = 0; var eof = false;
            for (long block = 0, maximum = bytes / buffer.Length + 2; block < maximum; block++)
            {
                budget.Check(); var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes - total + 1));
                if (read == 0) { if (total != bytes) throw new InvalidDataException("Downloaded LFS object ended early."); eof = true; break; }
                total += read; if (total > bytes) throw new InvalidDataException("Downloaded LFS object grew during hydration.");
                output.Write(buffer, 0, read);
            }
            if (!eof) throw new InvalidDataException("LFS hydration block bound reached.");
            output.Flush(flushToDisk: true);
        }
        TaskOperations.VerifyFile(partial, bytes, sha256, budget);
        TaskOperations.VerifyFile(source, bytes, sha256, budget);
        TaskOperations.VerifyFile(destination, committedPointer.Length, pointerSha, budget);
        SafePaths.NoLinks(partial, budget); SafePaths.NoLinks(destination, budget); budget.Check();
        File.Move(partial, destination, overwrite: true);
        TaskOperations.VerifyFile(destination, bytes, sha256, budget);
        return new(destination, source, bytes, sha256);
    }

    internal static LfsStoredObject StoreObject(string gitDirectory, string source, long bytes, string sha256, Budget budget)
    {
        budget.Check(); TaskOperations.Sha(sha256);
        if (bytes is < 0 or > ArchiveOperations.MaximumBytes) throw new InvalidDataException("LFS object byte bound16GiB.");
        var sourcePath = Path.GetFullPath(source);
        // A bad source pin or pre-existing cancellation must precede mutation.
        TaskOperations.VerifyFile(sourcePath, bytes, sha256, budget);
        var gitRoot = Path.GetFullPath(gitDirectory); SafePaths.NoLinks(gitRoot, budget);
        if (!Directory.Exists(gitRoot)) throw new DirectoryNotFoundException("Verified Git directory missing.");
        var relative = "lfs/objects/" + sha256[..2] + "/" + sha256.Substring(2, 2) + "/" + sha256;
        var destination = SafePaths.Within(gitRoot, relative, budget);
        if (Path.Exists(destination))
        {
            TaskOperations.VerifyFile(destination, bytes, sha256, budget);
            return new(destination, bytes, sha256, true);
        }
        budget.Check(); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        SafePaths.NoLinks(destination, budget);
        var partial = SafePaths.Within(gitRoot, relative + "." + Guid.NewGuid().ToString("N") + ".partial", budget);
        // Unique task-owned incomplete objects stay explicit evidence on failure.
        // No existing object or partial is overwritten or removed.
        RegularFiles.Require(sourcePath, budget);
        using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (input.Length != bytes) throw new InvalidDataException("LFS source length changed.");
            budget.Check();
            using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[1024 * 1024]; long total = 0;
            var maximumBlocks = bytes / buffer.Length + 2;
            var eof = false;
            for (long block = 0; block < maximumBlocks; block++)
            {
                budget.Check();
                var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes - total + 1));
                if (read == 0)
                {
                    if (total != bytes) throw new InvalidDataException("LFS source ended before its pinned size.");
                    eof = true; break;
                }
                total += read;
                if (total > bytes) throw new InvalidDataException("LFS source grew during copy.");
                output.Write(buffer, 0, read);
            }
            if (!eof) throw new InvalidDataException("LFS copy block bound reached.");
            output.Flush(true);
        }
        TaskOperations.VerifyFile(partial, bytes, sha256, budget);
        // Rebind both paths immediately before the no-overwrite acceptance.
        TaskOperations.VerifyFile(sourcePath, bytes, sha256, budget);
        SafePaths.NoLinks(destination, budget); SafePaths.NoLinks(partial, budget); budget.Check();
        File.Move(partial, destination, overwrite: false);
        TaskOperations.VerifyFile(destination, bytes, sha256, budget);
        return new(destination, bytes, sha256, false);
    }
}
