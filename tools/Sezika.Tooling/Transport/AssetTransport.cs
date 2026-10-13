using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sezika.Tooling;

internal static partial class AssetTransport
{
    internal const int MaximumFiles = 4096;
    internal const long MaximumFileBytes = 64L * 1024 * 1024 * 1024;
    internal const long MaximumTotalBytes = 256L * 1024 * 1024 * 1024;
    internal const int MaximumJsonBytes = 16 * 1024 * 1024;
    private const int BlockBytes = 1024 * 1024;

    internal static string FileHash(string path, Budget budget)
    {
        RegularFiles.Require(path, budget);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Hash(input, input.Length, budget);
    }

    private static string Hash(Stream input, long bytes, Budget budget)
    {
        if (bytes is < 0 or > MaximumFileBytes) throw new InvalidDataException("Asset byte bound exceeded.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BlockBytes];
        long total = 0;
        for (long block = 0; block < bytes / BlockBytes + 2; block++)
        {
            budget.Check();
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes - total + 1));
            if (read == 0)
            {
                if (total != bytes) throw new InvalidDataException("Asset truncated.");
                return Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            total += read;
            if (total > bytes) throw new InvalidDataException("Asset grew.");
            hash.AppendData(buffer, 0, read);
        }
        throw new InvalidDataException("Asset read iteration bound exceeded.");
    }

    internal static void ValidateSha(string sha)
    {
        if (sha.Length != 64 || sha.Any(c => !char.IsAsciiHexDigit(c) || char.IsAsciiLetterUpper(c)))
            throw new InvalidDataException("Expected lowercase SHA-256.");
    }

    internal static void ValidateCommit(string commit)
    {
        if (commit.Length != 40 || commit.Any(c => !char.IsAsciiHexDigit(c) || char.IsAsciiLetterUpper(c)))
            throw new InvalidDataException("Expected complete lowercase commit identity.");
    }

    internal static string AssetPath(string root, string relative, Budget budget)
    {
        var name = SafePaths.Relative(relative);
        if (name.Split('/').Any(p => p.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Git metadata is not an asset.");
        return SafePaths.Within(root, name, budget);
    }

    internal static void ValidateAsset(AssetFile file)
    {
        _ = SafePaths.Relative(file.Path);
        ValidateSha(file.Sha256);
        if (file.Bytes is < 0 or > MaximumFileBytes) throw new InvalidDataException("File length outside bound.");
    }

    internal static T ReadJson<T>(string path, JsonTypeInfo<T> type, Budget budget)
    {
        RegularFiles.Require(path, budget);
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumJsonBytes) throw new InvalidDataException("JSON byte cap exceeded.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        ValidateJson(document.RootElement, budget);
        return document.RootElement.Deserialize(type) ?? throw new InvalidDataException("JSON value missing.");
    }

    internal static void ValidateJson(JsonElement value, Budget budget)
    {
        var pending = new Stack<JsonElement>(); pending.Push(value);
        for (var nodes = 0; nodes < 131072; nodes++)
        {
            budget.Check();
            if (pending.Count == 0) return;
            var current = pending.Pop();
            if (current.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in current.EnumerateObject())
                {
                    budget.Check();
                    if (!names.Add(property.Name) || names.Count > 16384 || pending.Count >= 131072)
                        throw new InvalidDataException("Duplicate JSON property or object/node cap exceeded.");
                    pending.Push(property.Value);
                }
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                if (current.GetArrayLength() > 16384) throw new InvalidDataException("JSON array cap exceeded.");
                foreach (var element in current.EnumerateArray()) pending.Push(element);
            }
        }
        throw new InvalidDataException("JSON node cap exceeded.");
    }

    internal static void WriteJson<T>(string path, T value, JsonTypeInfo<T> type, Budget budget)
    {
        budget.Check(); SafePaths.NoLinks(path, budget);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, type); stream.Flush(flushToDisk: true);
    }

    private static Dictionary<string, AssetFile> Inventory(string root, Budget budget)
    {
        SafePaths.NoLinks(root, budget);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Asset root absent.");
        var pending = new Queue<string>(); pending.Enqueue(Path.GetFullPath(root));
        var files = new Dictionary<string, AssetFile>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        for (var directoryIndex = 0; directoryIndex < MaximumFiles; directoryIndex++)
        {
            budget.Check();
            if (pending.Count == 0) return files;
            var directory = pending.Dequeue();
            var items = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                budget.Check();
                if (++items > MaximumFiles || files.Count + pending.Count >= MaximumFiles)
                    throw new InvalidDataException("Asset inventory cap exceeded.");
                SafePaths.NoLinks(entry, budget);
                var rel = Path.GetRelativePath(root, entry).Replace('\\', '/');
                _ = AssetPath(root, rel, budget);
                if (Directory.Exists(entry)) pending.Enqueue(entry);
                else
                {
                    var size = new FileInfo(entry).Length; total = checked(total + size);
                    if (size > MaximumFileBytes || total > MaximumTotalBytes || !files.TryAdd(rel, new(rel, size, FileHash(entry, budget))))
                        throw new InvalidDataException("Asset inventory bytes/case collision exceeded.");
                }
            }
        }
        throw new InvalidDataException("Asset directory cap exceeded.");
    }

    private static void VerifyEntries(string root, AssetFile[] expected, Budget budget)
    {
        if (expected.Length is < 1 or > MaximumFiles) throw new InvalidDataException("Asset count outside bound.");
        var inventory = Inventory(root, budget);
        if (inventory.Count != expected.Length) throw new InvalidDataException("Actual asset inventory differs.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in expected)
        {
            budget.Check(); ValidateAsset(file);
            if (!seen.Add(file.Path) || !inventory.TryGetValue(file.Path, out var actual) || actual != file)
                throw new InvalidDataException("Asset bytes/SHA/path identity differs: " + file.Path);
        }
    }

    internal static LogicalManifest VerifyLogical(string root, string manifestPath, Budget budget)
    {
        var manifest = ReadJson(manifestPath, TransportJsonContext.Default.LogicalManifest, budget);
        ValidateLogical(manifest, budget); VerifyEntries(root, manifest.Files, budget);
        return manifest;
    }

    private static void ValidateLogical(LogicalManifest manifest, Budget budget)
    {
        if (manifest.SchemaVersion != 1 || manifest.Kind != "full-corpus-model" ||
            manifest.Status != "PASS_offline_full_corpus_publication_contract" || manifest.RunId.Length is < 3 or > 64 ||
            manifest.RunId.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')) ||
            manifest.OwnedPrefix != "runs/" + manifest.RunId + "/" || !manifest.ExperimentalOnly || manifest.FormalQuality ||
            manifest.GlobalUpdates < 1 || manifest.Completion is null || !manifest.Completion.FullCorpusEpochCoverage ||
            !manifest.Completion.GlobalUpdatesComplete || !manifest.Completion.SourceEofVerifiedByTraining ||
            !manifest.Completion.CompleteCheckpointVerified || !manifest.Completion.AllAdamwMomentsVerified || !manifest.Completion.IndependentValidationVerified)
            throw new InvalidDataException("Incomplete logical publication contract.");
        ValidateSha(manifest.PackageSha256); ValidateCommit(manifest.PublishedDatasetCommit); ValidateCommit(manifest.OriginalDatasetCommit);
        if (manifest.Files.Length is < 1 or > 2048) throw new InvalidDataException("Logical file count outside bound.");
        long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            budget.Check(); ValidateAsset(file);
            if (file.Path.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase) || file.Path.StartsWith("_transport/", StringComparison.OrdinalIgnoreCase) ||
                !names.Add(file.Path)) throw new InvalidDataException("Reserved or duplicate logical asset.");
            total = checked(total + file.Bytes);
        }
        if (total != manifest.TotalBytes || total > MaximumTotalBytes) throw new InvalidDataException("Logical byte denominator differs.");
    }

    private static string FreshDirectory(string path, string source, Budget budget)
    {
        var full = Path.GetFullPath(path); SafePaths.NoLinks(full, budget);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)) + Path.DirectorySeparatorChar;
        if (Path.Exists(full) || full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Destination must be fresh and outside the source.");
        Directory.CreateDirectory(full);
        return full;
    }

    private static AssetFile Copy(string source, string destination, string relative, Budget budget)
    {
        RegularFiles.Require(source, budget); SafePaths.NoLinks(destination, budget);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); SafePaths.NoLinks(destination, budget);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var size = input.Length;
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            CopyExact(input, output, size, budget);
            if (input.ReadByte() != -1) throw new InvalidDataException("Source grew during copy.");
            output.Flush(flushToDisk: true);
        }
        input.Position = 0;
        var original = Hash(input, size, budget);
        var actual = FileHash(destination, budget);
        if (actual != original) throw new InvalidDataException("Independent copied-file readback differs.");
        return new(relative, size, actual);
    }

    private static void CopyExact(Stream input, Stream output, long bytes, Budget budget)
    {
        if (bytes is < 0 or > MaximumFileBytes) throw new InvalidDataException("Copy size outside bound.");
        var buffer = new byte[BlockBytes]; long remaining = bytes;
        for (long block = 0; block < (bytes + BlockBytes - 1) / BlockBytes; block++)
        {
            budget.Check();
            var size = (int)Math.Min(buffer.Length, remaining);
            input.ReadExactly(buffer.AsSpan(0, size)); output.Write(buffer, 0, size); remaining -= size;
        }
        if (remaining != 0) throw new InvalidDataException("Copy length differs.");
    }

    internal static TransportPreparationReport Prepare(string sourceRoot, string logicalManifestPath, string outputRoot, long chunkBytes, Budget budget)
    {
        var logical = VerifyLogical(sourceRoot, logicalManifestPath, budget);
        if (chunkBytes < (logical.TestOnly ? 16 : 1024 * 1024) || chunkBytes > 1024L * 1024 * 1024)
            throw new InvalidDataException("Physical chunk size outside reviewed bound.");
        foreach (var file in logical.Files)
            if ((file.Bytes + chunkBytes - 1) / chunkBytes > 128) throw new InvalidDataException("Logical asset exceeds 128 physical parts.");
        var destination = FreshDirectory(outputRoot, sourceRoot, budget);
        var marker = Path.Combine(destination, ".sezika-transport-in-progress"); File.WriteAllText(marker, logical.RunId);
        var run = Path.Combine(destination, "run"); Directory.CreateDirectory(Path.Combine(run, "_transport"));
        var copiedManifest = Copy(logicalManifestPath, Path.Combine(run, "_transport/publication-manifest.json"), "_transport/publication-manifest.json", budget);
        var mappings = new List<TransportMapping>(); var physical = new List<AssetFile>();
        foreach (var file in logical.Files)
        {
            budget.Check(); var source = AssetPath(sourceRoot, file.Path, budget); var parts = new List<AssetFile>();
            if (file.Bytes <= chunkBytes)
            {
                var copied = Copy(source, AssetPath(run, file.Path, budget), file.Path, budget);
                if (copied != file) throw new InvalidDataException("Logical copy source identity changed.");
                parts.Add(copied);
            }
            else
            {
                var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file.Path)))[..16];
                using var input = File.OpenRead(source); long remaining = file.Bytes;
                for (var part = 0; part < (file.Bytes + chunkBytes - 1) / chunkBytes; part++)
                {
                    budget.Check(); var rel = $"_transport/chunks/{key}/part-{part:0000}";
                    var target = AssetPath(run, rel, budget); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var bytes = Math.Min(remaining, chunkBytes);
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { CopyExact(input, output, bytes, budget); output.Flush(flushToDisk: true); }
                    parts.Add(new(rel, bytes, FileHash(target, budget))); remaining -= bytes;
                }
                if (remaining != 0 || input.ReadByte() != -1) throw new InvalidDataException("Split source length changed.");
                input.Position = 0;
                if (Hash(input, file.Bytes, budget) != file.Sha256) throw new InvalidDataException("Split source SHA changed.");
            }
            mappings.Add(new(file, parts.ToArray())); physical.AddRange(parts);
            if (physical.Count + 3 > MaximumFiles) throw new InvalidDataException("Physical object count exceeded.");
        }
        physical.Add(copiedManifest);
        var transport = new TransportManifest(1, "full-corpus-model-transport", logical.RunId, logical.TestOnly, copiedManifest.Sha256,
            chunkBytes, logical.TotalBytes, mappings.ToArray(), physical.ToArray());
        var transportPath = Path.Combine(run, "_transport/manifest.json"); WriteJson(transportPath, transport, TransportJsonContext.Default.TransportManifest, budget);
        var attrs = new List<string> { "# Exact run-local transport paths; preserve original bytes." };
        foreach (var file in physical.OrderBy(p => p.Path, StringComparer.Ordinal))
        { budget.Check(); attrs.Add("/" + file.Path + (file.Bytes >= (logical.TestOnly ? 16 : 1024 * 1024) ? " filter=lfs diff=lfs merge=lfs -text" : " -filter diff -merge -text")); }
        attrs.Add("/_transport/manifest.json -filter diff -merge -text"); attrs.Add("/.gitattributes -filter diff -merge -text");
        var attributesPath = Path.Combine(run, ".gitattributes");
        using (var output = new FileStream(attributesPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { var text = Encoding.UTF8.GetBytes(string.Join('\n', attrs) + "\n"); output.Write(text); output.Flush(flushToDisk: true); }
        var report = new TransportPreparationReport("PASS_offline_transport_preparation", logical.RunId, run, logical.Files.Length, logical.TotalBytes,
            physical.Count + 2, FileHash(transportPath, budget), FileHash(attributesPath, budget), copiedManifest.Sha256);
        _ = VerifyPhysical(run, report.TransportManifestSha256, report.AttributesSha256, budget);
        WriteJson(Path.Combine(destination, "preparation-receipt.json"), report, TransportJsonContext.Default.TransportPreparationReport, budget);
        File.Delete(marker); return report;
    }

    internal static TransportManifest VerifyPhysical(string root, string transportSha, string attributesSha, Budget budget)
    {
        ValidateSha(transportSha); ValidateSha(attributesSha);
        var manifestPath = AssetPath(root, "_transport/manifest.json", budget); var attributesPath = AssetPath(root, ".gitattributes", budget);
        if (FileHash(manifestPath, budget) != transportSha || FileHash(attributesPath, budget) != attributesSha)
            throw new InvalidDataException("Transport manifest/attributes SHA differs.");
        var transport = ReadJson(manifestPath, TransportJsonContext.Default.TransportManifest, budget);
        if (transport.SchemaVersion != 1 || transport.Kind != "full-corpus-model-transport" || transport.Mappings.Length is < 1 or > 2048 ||
            transport.PhysicalFiles.Length is < 1 or > MaximumFiles - 2 || transport.MaximumPhysicalObjectBytes is < 16 or > 1024L * 1024 * 1024)
            throw new InvalidDataException("Transport schema/count/chunk identity differs.");
        VerifyEntries(root, [.. transport.PhysicalFiles,
            new("_transport/manifest.json", new FileInfo(manifestPath).Length, transportSha), new(".gitattributes", new FileInfo(attributesPath).Length, attributesSha)], budget);
        var logicalPath = AssetPath(root, "_transport/publication-manifest.json", budget);
        if (FileHash(logicalPath, budget) != transport.LogicalManifestSha256) throw new InvalidDataException("Logical manifest identity differs.");
        var logical = ReadJson(logicalPath, TransportJsonContext.Default.LogicalManifest, budget); ValidateLogical(logical, budget);
        if (transport.RunId != logical.RunId || transport.FixtureOnly != logical.TestOnly || transport.LogicalTotalBytes != logical.TotalBytes ||
            transport.Mappings.Length != logical.Files.Length) throw new InvalidDataException("Logical/physical run binding differs.");
        var mapped = new List<AssetFile>();
        for (var index = 0; index < transport.Mappings.Length; index++)
        {
            budget.Check(); var mapping = transport.Mappings[index];
            if (mapping.Logical != logical.Files[index] || mapping.Parts.Length is < 1 or > 128) throw new InvalidDataException("Logical mapping order/count differs.");
            long total = 0;
            foreach (var part in mapping.Parts)
            {
                budget.Check(); ValidateAsset(part); total = checked(total + part.Bytes);
                if (part.Bytes > transport.MaximumPhysicalObjectBytes) throw new InvalidDataException("Physical chunk exceeded platform limit.");
                mapped.Add(part);
            }
            if (total != mapping.Logical.Bytes) throw new InvalidDataException("Mapped part byte denominator differs.");
        }
        if (mapped.Count + 1 != transport.PhysicalFiles.Length || !mapped.SequenceEqual(transport.PhysicalFiles[..^1]) ||
            transport.PhysicalFiles[^1].Path != "_transport/publication-manifest.json") throw new InvalidDataException("Physical inventory/mappings differ.");
        return transport;
    }

    internal static TransportReadbackReport Reassemble(string physicalRoot, string destination, string transportSha, string attributesSha, Budget budget)
    {
        var transport = VerifyPhysical(physicalRoot, transportSha, attributesSha, budget);
        var outputRoot = FreshDirectory(destination, physicalRoot, budget);
        foreach (var mapping in transport.Mappings)
        {
            budget.Check(); var target = AssetPath(outputRoot, mapping.Logical.Path, budget); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                foreach (var part in mapping.Parts)
                {
                    budget.Check(); using var input = File.OpenRead(AssetPath(physicalRoot, part.Path, budget));
                    CopyExact(input, output, part.Bytes, budget);
                    if (input.ReadByte() != -1) throw new InvalidDataException("Physical part grew during reassembly.");
                }
                output.Flush(flushToDisk: true);
            }
            if (new FileInfo(target).Length != mapping.Logical.Bytes || FileHash(target, budget) != mapping.Logical.Sha256)
                throw new InvalidDataException("Reassembled logical SHA differs.");
        }
        var logical = VerifyLogical(outputRoot, AssetPath(physicalRoot, "_transport/publication-manifest.json", budget), budget);
        return new("PASS_complete_logical_run_bytes_sha256_only", logical.RunId, outputRoot, logical.Files.Length, logical.TotalBytes, true);
    }
}
