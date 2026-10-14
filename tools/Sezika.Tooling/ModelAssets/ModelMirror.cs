using System.Text.Json;

namespace Sezika.Tooling;

/// <summary>Composes an externally pinned model asset set on the typed worker's own filesystem.</summary>
internal static class ModelMirror
{
    internal const int MaximumAssets = 128;
    internal const int MaximumManifestBytes = 1024 * 1024;
    private const long MaximumAssetBytes = 16L * 1024 * 1024 * 1024 - 2L * 1024 * 1024;
    private const string ManifestName = "model-source-manifest.json";

    internal static async Task<ModelMirrorReport> MirrorAsync(ModelMirrorRequest request, Budget budget,
        CancellationToken cancellation, string? bearerToken = null, HttpClient? client = null)
    {
        budget.Check(); cancellation.ThrowIfCancellationRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, budget.Cancellation);
        budget = new Budget(Math.Clamp((int)Math.Ceiling(budget.RemainingSeconds), 1, 86400), null, linked.Token, budget);
        cancellation = linked.Token;
        if (request.SchemaVersion != 1) throw new InvalidDataException("Model mirror request schema differs.");
        Runtime(request.RuntimeIdentifier);
        var root = Absolute(request.Root, budget);
        if (!Directory.Exists(root) || root == Path.GetPathRoot(root)) throw new InvalidDataException("An existing owned model root is required.");
        var cache = Child(root, request.CacheDirectory, budget);
        var output = Child(root, request.OutputDirectory, budget);
        if (Contains(cache, output) || Contains(output, cache) || string.Equals(cache, output,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Model cache and output scopes overlap.");
        if (Path.Exists(output)) throw new IOException("Model output exists; preserve it.");
        if (client?.DefaultRequestHeaders.Authorization is not null)
            throw new InvalidDataException("Model credentials require per-request in-memory authentication.");

        var manifest = ReadManifest(request.ManifestFile, request.ManifestSha256, budget);
        var entry = SafePaths.Within(cache, request.ManifestSha256, budget);
        var cachedManifest = SafePaths.Within(entry, ManifestName, budget);
        if (Directory.Exists(entry))
        {
            if (!File.Exists(cachedManifest)) throw new InvalidDataException("Existing model cache has no exact manifest ownership.");
            _ = ReadManifest(cachedManifest, request.ManifestSha256, budget);
        }
        else
        {
            Directory.CreateDirectory(entry);
            File.Copy(request.ManifestFile, cachedManifest, overwrite: false);
            _ = ReadManifest(cachedManifest, request.ManifestSha256, budget);
        }
        var allowedCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestName };
        foreach (var asset in manifest.Assets)
        {
            budget.Check();
            allowedCache.Add(asset.Path); allowedCache.Add(asset.Path + ".sezika-part");
            allowedCache.Add(asset.Path + ".sezika-download.json");
        }
        Inventory(entry, allowedCache, requireAll: false, budget);

        var results = new List<ModelMirrorAssetResult>();
        var files = new List<BundleFile>();
        foreach (var asset in manifest.Assets)
        {
            budget.Check(); cancellation.ThrowIfCancellationRequested();
            var path = AssetTransport.AssetPath(entry, asset.Path, budget);
            ModelMirrorAssetResult result;
            if (File.Exists(path))
            {
                TaskOperations.VerifyFile(path, asset.Bytes, asset.Digest, budget);
                result = new(asset.Path, asset.Role, asset.Bytes, asset.Digest, "PASS_COMPLETE_CACHE_SHA_READBACK", 0, 0, true);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var downloaded = await AssetTransport.DownloadAsync(new(Url(manifest, asset), asset.Bytes, asset.Digest, asset.ETag),
                    path, budget, cancellation, bearerToken, client);
                if (!downloaded.IndependentlyReadBack || downloaded.Bytes != asset.Bytes || downloaded.Sha256 != asset.Digest)
                    throw new InvalidDataException("Model download independent byte/SHA receipt differs.");
                result = new(asset.Path, asset.Role, asset.Bytes, asset.Digest, downloaded.Status,
                    downloaded.Requests, downloaded.ResumedBytes, downloaded.IndependentlyReadBack);
            }
            files.Add(new(path, asset.Path, asset.Bytes, asset.Digest)); results.Add(result);
        }
        budget.Check(); cancellation.ThrowIfCancellationRequested();
        files.Add(new(cachedManifest, ManifestName, new FileInfo(cachedManifest).Length, request.ManifestSha256));
        _ = TaskOperations.Freeze(new(output, manifest.Revision, request.RuntimeIdentifier, files.ToArray()), budget);
        var verified = Verify(new(1, output, request.ManifestSha256, request.RuntimeIdentifier), budget);
        return new("PASS_model_download_cache_bundle_and_complete_manifest_readback", verified, results.ToArray());
    }

    internal static ModelMirrorVerification Verify(ModelMirrorVerifyRequest request, Budget budget)
    {
        budget.Check();
        if (request.SchemaVersion != 1) throw new InvalidDataException("Model verification schema differs.");
        Runtime(request.RuntimeIdentifier);
        var root = Absolute(request.Directory, budget);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Model bundle is absent.");
        var manifestPath = SafePaths.Within(root, ManifestName, budget);
        var manifest = ReadManifest(manifestPath, request.ManifestSha256, budget);
        var expected = new Dictionary<string, AssetFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets) expected.Add(asset.Path, new(asset.Path, asset.Bytes, asset.Digest));
        expected.Add(ManifestName, new(ManifestName, new FileInfo(manifestPath).Length, request.ManifestSha256));
        var engineeringPath = SafePaths.Within(root, "engineering-sources.json", budget);
        var engineering = AssetTransport.ReadJson(engineeringPath, TaskJsonContext.Default.BundleRequest, budget);
        if (engineering.SourceCommit != manifest.Revision || engineering.RuntimeIdentifier != request.RuntimeIdentifier ||
            engineering.Files.Length != expected.Count)
            throw new InvalidDataException("Model bundle engineering manifest source/revision/count differs.");
        var engineeringNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in engineering.Files)
        {
            budget.Check();
            if (file is null || !engineeringNames.Add(file.RelativePath) || !expected.TryGetValue(file.RelativePath, out var pin) ||
                file.Bytes != pin.Bytes || file.Sha256 != pin.Sha256)
                throw new InvalidDataException("Model bundle does not cover the exact pinned asset manifest.");
        }
        expected.Add("engineering-sources.json", new("engineering-sources.json", new FileInfo(engineeringPath).Length,
            AssetTransport.FileHash(engineeringPath, budget)));
        var shaPath = SafePaths.Within(root, "files.sha256", budget);
        RegularFiles.Require(shaPath, budget);
        if (new FileInfo(shaPath).Length > MaximumManifestBytes) throw new InvalidDataException("Model SHA manifest byte bound.");
        var rows = File.ReadLines(shaPath).Take(MaximumAssets + 4).ToArray();
        if (rows.Length != expected.Count) throw new InvalidDataException("Model SHA manifest completeness differs.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            budget.Check();
            if (row.Length < 67 || row.Substring(64, 2) != "  ") throw new InvalidDataException("Model SHA manifest row differs.");
            var name = SafePaths.Relative(row[66..]);
            if (!names.Add(name) || !expected.TryGetValue(name, out var pin) || row[..64] != pin.Sha256)
                throw new InvalidDataException("Model SHA manifest has a duplicate, extra or changed identity.");
            TaskOperations.VerifyFile(AssetTransport.AssetPath(root, name, budget), pin.Bytes, pin.Sha256, budget);
        }
        names.Add("files.sha256"); Inventory(root, names, requireAll: true, budget);
        return new("PASS_pinned_model_manifest_and_all_asset_bytes_sha256", root, manifest.Repository, manifest.Revision,
            manifest.LicenseIdentity, request.ManifestSha256, manifest.Assets.Length, manifest.Assets.Sum(a => a.Bytes), true);
    }

    private static ModelMirrorManifest ReadManifest(string path, string expectedSha256, Budget budget)
    {
        path = Absolute(path, budget);
        AssetTransport.ValidateSha(expectedSha256); RegularFiles.Require(path, budget);
        // Hash and deserialize the same bounded snapshot; two separate file reads
        // could otherwise admit a changed repository/asset list between them.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = input.Length;
        if (length is < 1 or > MaximumManifestBytes) throw new InvalidDataException("Model manifest byte bound1MiB.");
        var bytes = new byte[(int)length]; var total = 0;
        for (var read = 0; read < bytes.Length && total < bytes.Length; read++)
        {
            budget.Check(); var count = input.Read(bytes.AsSpan(total));
            if (count == 0) throw new EndOfStreamException("Model manifest snapshot truncated.");
            total += count;
        }
        budget.Check();
        if (total != bytes.Length || input.ReadByte() != -1) throw new InvalidDataException("Model manifest snapshot length changed.");
        using var snapshot = new MemoryStream(bytes, writable: false);
        if (budget.Hash(snapshot, bytes.Length) != expectedSha256)
            throw new InvalidDataException("Model manifest differs from its external expected SHA-256 pin.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        AssetTransport.ValidateJson(document.RootElement, budget);
        var manifest = document.RootElement.Deserialize(ModelMirrorJsonContext.Default.ModelMirrorManifest) ??
            throw new InvalidDataException("Model manifest value is absent.");
        Validate(manifest, budget);
        return manifest;
    }

    internal static void Validate(ModelMirrorManifest manifest, Budget budget)
    {
        budget.Check(); AssetTransport.ValidateCommit(manifest.Revision);
        var repository = AssetTransport.ValidateUri(manifest.Repository, allowQuery: false);
        var components = repository.AbsolutePath.Trim('/').Split('/');
        if (manifest.SchemaVersion != 1 || repository.AbsoluteUri != manifest.Repository || repository.AbsoluteUri.EndsWith('/') || components.Length != 2 ||
            !repository.IsLoopback && (repository.Host is not ("huggingface.co" or "moark.com") || !repository.IsDefaultPort) ||
            manifest.LicenseIdentity.Length is < 1 or > 256 || manifest.LicenseIdentity.Any(char.IsControl) ||
            manifest.LicenseIdentity != manifest.LicenseIdentity.Trim() || manifest.Assets.Length is < 4 or > MaximumAssets)
            throw new InvalidDataException("Fixed model repository, license identity or asset denominator differs.");
        foreach (var component in components) _ = SafePaths.Relative(component);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roles = new HashSet<ModelAssetRole>(); long bytes = 0, ranges = 0;
        foreach (var asset in manifest.Assets)
        {
            budget.Check();
            if (asset is null) throw new InvalidDataException("Null model asset is not an asset identity.");
            _ = SafePaths.Relative(asset.Path); AssetTransport.ValidateSha(asset.Digest);
            if (!Enum.IsDefined(asset.Role) || asset.DigestKind != "sha256" || !names.Add(asset.Path) ||
                asset.Path.Split('/').Length > 16 || asset.Path.Split('/')[^1].Length > 220 ||
                asset.Bytes is <= 0 or > MaximumAssetBytes || (bytes = checked(bytes + asset.Bytes)) > MaximumAssetBytes ||
                TaskOperations.IsScript(asset.Path) || Path.GetExtension(asset.Path).ToLowerInvariant() is ".js" or ".rb" or ".pl" or ".vbs" or ".psm1" ||
                asset.Path.Split('/').Any(p => p.Equals(".git", StringComparison.OrdinalIgnoreCase)) ||
                asset.Path.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) ||
                asset.Path.Equals("files.sha256", StringComparison.OrdinalIgnoreCase) ||
                asset.Path.Equals("engineering-sources.json", StringComparison.OrdinalIgnoreCase) ||
                asset.Path.EndsWith(".sezika-part", StringComparison.OrdinalIgnoreCase) ||
                asset.Path.EndsWith(".sezika-download.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Model asset role/path/byte/SHA-256 contract differs.");
            ranges = checked(ranges + (asset.Bytes + 16 * 1024 * 1024 - 1) / (16 * 1024 * 1024));
            roles.Add(asset.Role);
        }
        foreach (var name in names)
        {
            budget.Check();
            if (names.Any(other => other.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Model asset file/directory paths collide.");
        }
        if (roles.Count != 4 || ranges > 4096) throw new InvalidDataException("Model required roles or aggregate range bound differs.");
    }

    private static string Url(ModelMirrorManifest manifest, ModelMirrorAsset asset) => manifest.Repository + "/resolve/" +
        manifest.Revision + "/" + string.Join('/', asset.Path.Split('/').Select(Uri.EscapeDataString));

    private static string Absolute(string value, Budget budget)
    {
        if (!Path.IsPathFullyQualified(value) || Path.GetFullPath(value) != value)
            throw new InvalidDataException("Model scope must be an exact absolute path.");
        SafePaths.NoLinks(value, budget); return value;
    }

    private static string Child(string root, string path, Budget budget)
    {
        path = Absolute(path, budget);
        if (!Contains(root, path)) throw new InvalidDataException("Model path escaped its admitted owned root.");
        return path;
    }

    private static bool Contains(string root, string path) => path.StartsWith(Path.TrimEndingDirectorySeparator(root) +
        Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void Runtime(string value)
    {
        if (value is not ("win-x64" or "linux-x64")) throw new InvalidDataException("Model bundle consumer runtime identity differs.");
    }

    private static void Inventory(string root, HashSet<string> allowed, bool requireAll, Budget budget)
    {
        var pending = new Queue<(string Path, int Depth)>(); pending.Enqueue((root, 0));
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var entries = 0;
        for (var directory = 0; directory < 2048 && pending.Count > 0; directory++)
        {
            budget.Check(); var item = pending.Dequeue();
            if (item.Depth > 16) throw new InvalidDataException("Model directory depth bound16.");
            foreach (var path in Directory.EnumerateFileSystemEntries(item.Path))
            {
                budget.Check(); SafePaths.NoLinks(path, budget);
                if (++entries > 4096) throw new InvalidDataException("Model cache/package entry bound4096.");
                if (Directory.Exists(path)) { pending.Enqueue((path, item.Depth + 1)); continue; }
                RegularFiles.Require(path, budget);
                var relative = SafePaths.Relative(Path.GetRelativePath(root, path).Replace('\\', '/'));
                if (!allowed.Contains(relative) || !found.Add(relative)) throw new InvalidDataException("Model unmanifested or colliding file preserved.");
            }
        }
        if (pending.Count > 0 || requireAll && !found.SetEquals(allowed)) throw new InvalidDataException("Model complete inventory bound or coverage differs.");
    }
}
