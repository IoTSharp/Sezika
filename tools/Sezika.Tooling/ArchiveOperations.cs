using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;

namespace Sezika.Tooling;

internal sealed record ArchiveReport(string Status, string Directory, string ArchiveSha256, int Files, long Bytes,
    double ElapsedSeconds, bool PartialOutputsPreservedOnFailure = true);

internal static class ArchiveOperations
{
    internal const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    internal const int MaximumFiles = 1000;
    internal const int MaximumEntries = 1006;

    internal static ArchiveReport Extract(string archive, string expectedHash, string destination, Budget budget)
    {
        budget.Check();
        if (expectedHash.Length != 64 || expectedHash.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Expected SHA256 must contain exactly 64 hex characters.");
        archive = Path.GetFullPath(archive);
        destination = Path.GetFullPath(destination);
        RegularFiles.Require(archive, budget);
        SafePaths.NoLinks(destination, budget);
        if (Path.Exists(destination)) throw new IOException("Destination already exists; preserve it.");
        using var input = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = input.Length;
        var archiveHash = budget.Hash(input, length);
        if (!archiveHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive SHA256 mismatch.");
        budget.Check();
        input.Position = 0;
        // Output is intentionally retained on any failure; it is never accepted without the final receipt.
        Directory.CreateDirectory(destination);
        using var ownership = new FileStream(Path.Combine(destination, ".sezika-extraction-in-progress"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        var ended = false;
        var buffer = new byte[1024 * 1024];
        using (var reader = new TarReader(input, leaveOpen: true))
        {
            for (var index = 0; index < MaximumEntries; index++)
            {
                budget.Check();
                var entry = reader.GetNextEntry(copyData: false);
                if (entry is null) { ended = true; break; }
                var name = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;
                if (name is "" or ".")
                {
                    if (entry.EntryType != TarEntryType.Directory) throw new InvalidDataException("Non-directory root entry.");
                    continue;
                }
                name = entry.EntryType == TarEntryType.Directory ? name.TrimEnd('/') : name;
                var target = SafePaths.Within(destination, name, budget);
                if (!names.Add(name)) throw new InvalidDataException("Duplicate or case-colliding archive member.");
                if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(target); continue; }
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new InvalidDataException("Only directories and regular files are allowed; links/devices are rejected.");
                if (seen.Count >= MaximumFiles || entry.Length < 0 || entry.Length > MaximumBytes - total)
                    throw new InvalidDataException("Archive file/byte bound exceeded.");
                total += entry.Length;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                SafePaths.NoLinks(target, budget);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var remaining = entry.Length;
                var maximumBlocks = (remaining + buffer.Length - 1) / buffer.Length;
                for (long block = 0; block < maximumBlocks; block++)
                {
                    budget.Check();
                    var size = (int)Math.Min(buffer.Length, remaining);
                    if (entry.DataStream is null) throw new InvalidDataException("Missing member data stream.");
                    entry.DataStream.ReadExactly(buffer.AsSpan(0, size));
                    output.Write(buffer, 0, size);
                    hash.AppendData(buffer, 0, size);
                    remaining -= size;
                }
                if (remaining != 0) throw new InvalidDataException("Truncated archive member.");
                output.Flush(flushToDisk: true);
                seen.Add(name, Convert.ToHexStringLower(hash.GetHashAndReset()));
            }
        }
        if (!ended) throw new InvalidDataException("Archive entry bound exceeded.");
        var manifestPath = SafePaths.Within(destination, "files.sha256", budget);
        if (!seen.ContainsKey("files.sha256") || new FileInfo(manifestPath).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Missing or oversized files.sha256.");
        var checkedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var manifest = new StreamReader(manifestPath, new UTF8Encoding(false, true)))
        {
            for (var lineIndex = 0; lineIndex <= MaximumFiles; lineIndex++)
            {
                budget.Check();
                var line = manifest.ReadLine();
                if (line is null) break;
                if (lineIndex == MaximumFiles || line.Length < 67 || line.Length > 1090 || line[64..66] != "  " ||
                    line[..64].Any(c => !char.IsAsciiHexDigit(c) || char.IsAsciiLetterUpper(c)))
                    throw new InvalidDataException("Invalid manifest line or count.");
                var name = SafePaths.Relative(line[66..]);
                if (name.Equals("files.sha256", StringComparison.OrdinalIgnoreCase) || !checkedFiles.Add(name) ||
                    !seen.TryGetValue(name, out var expected) || !line.StartsWith(expected, StringComparison.Ordinal))
                    throw new InvalidDataException("Manifest hash, membership or uniqueness mismatch.");
                using var file = File.OpenRead(SafePaths.Within(destination, name, budget));
                if (budget.Hash(file, file.Length) != expected) throw new InvalidDataException("Extracted bytes changed.");
            }
        }
        if (checkedFiles.Count == 0 || checkedFiles.Count + 1 != seen.Count)
            throw new InvalidDataException("Manifest must cover the complete extracted regular file set.");
        input.Position = 0;
        if (input.Length != length || budget.Hash(input, length) != archiveHash)
            throw new InvalidDataException("Archive changed during extraction.");
        ownership.Dispose();
        File.Delete(Path.Combine(destination, ".sezika-extraction-in-progress"));
        return new("PASS_safe_extract_and_full_file_sha", destination, archiveHash, seen.Count, total, budget.ElapsedSeconds);
    }
}
