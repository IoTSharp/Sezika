using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika;

/// <summary>One file in a model release. Relative paths are always package relative.</summary>
public sealed record ModelAssetFile
{
    public required string RelativePath { get; init; }
    public required string Sha256 { get; init; }
    public long Length { get; init; }
}

/// <summary>A release file and its source URL used by the bounded installer.</summary>
public sealed record ModelDownloadFile
{
    public required string RelativePath { get; init; }
    public required Uri Source { get; init; }
    public long ExpectedLength { get; init; }
    public string? Sha256 { get; init; }
}

/// <summary>Input to <see cref="ModelPackageStore.InstallAsync(ModelPackageSpec, CancellationToken)"/>.</summary>
public sealed record ModelPackageSpec
{
    public required string ModelId { get; init; }
    public required string Revision { get; init; }
    public required IReadOnlyList<ModelDownloadFile> Files { get; init; }
    public string ManifestPath { get; init; } = "model.json";
}

/// <summary>Durable installation inventory written next to an installed package.</summary>
public sealed record InstalledModelRecord
{
    public int SchemaVersion { get; init; } = 1;
    public required string ModelId { get; init; }
    public required string Revision { get; init; }
    public required string PackagePath { get; init; }
    public required DateTimeOffset InstalledAtUtc { get; init; }
    public required ModelAssetFile[] Files { get; init; }
}

/// <summary>Progress for one bounded download chunk.</summary>
public readonly record struct ModelDownloadProgress(string RelativePath, long CompletedBytes, long ExpectedBytes);

public sealed record ModelDownloadOptions
{
    public int MaxAttempts { get; init; } = 2;
    public int MaxChunks { get; init; } = 4096;
    public int ChunkBytes { get; init; } = 16 * 1024 * 1024;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(20);
    public bool UseProxyFallback { get; init; } = true;
    public Uri ProxyAddress { get; init; } = new("http://127.0.0.1:7890");
}

/// <summary>Stable error for model package operations.</summary>
public sealed class ModelPackageException : DecisionException
{
    public ModelPackageException(string code, string message, Exception? innerException = null)
        : base(code, message, innerException) { }
}

/// <summary>
/// Resumable, hash-checked HTTP downloader. Partial data is kept in a sibling
/// <c>.part</c> file and is never exposed as an installed asset.
/// </summary>
public sealed class ModelAssetDownloader
{
    private readonly ModelDownloadOptions _options;
    private readonly Func<bool, HttpClient> _clientFactory;

    public ModelAssetDownloader(ModelDownloadOptions? options = null, Func<bool, HttpClient>? clientFactory = null)
    {
        _options = options ?? new ModelDownloadOptions();
        if (_options.MaxAttempts is < 1 or > 4 || _options.MaxChunks is < 1 or > 1_000_000 ||
            _options.ChunkBytes is < 4 * 1024 or > 64 * 1024 * 1024 || _options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Download limits are outside the bounded range.");
        _clientFactory = clientFactory ?? (proxy =>
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None };
            if (proxy) handler.Proxy = new WebProxy(_options.ProxyAddress);
            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        });
    }

    public async Task DownloadAsync(
        ModelDownloadFile file,
        string destination,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        var target = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(target) ?? throw new ArgumentException("Destination has no parent directory.", nameof(destination));
        Directory.CreateDirectory(parent);
        if (file.ExpectedLength < 0) throw new ModelPackageException("decision_asset_size_invalid", "Expected asset length cannot be negative.");
        var boundedBytes = checked((long)_options.MaxChunks * _options.ChunkBytes);
        if (file.ExpectedLength > boundedBytes) throw new ModelPackageException("decision_asset_size_invalid", "Expected asset length exceeds the bounded download budget.");
        var part = target + ".part";
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DownloadAttemptAsync(file, target, part, progress, cancellationToken, useProxy: attempt > 1).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or ModelPackageException)
            {
                if (attempt >= _options.MaxAttempts || (attempt > 1 && !_options.UseProxyFallback))
                    throw new ModelPackageException("decision_asset_download_failed", $"Unable to download '{file.RelativePath}' after {attempt} attempt(s).", exception);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException("The bounded download loop terminated unexpectedly.");
    }

    private async Task DownloadAttemptAsync(ModelDownloadFile file, string target, string part, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken, bool useProxy)
    {
        if (useProxy && !_options.UseProxyFallback) throw new IOException("Proxy fallback is disabled.");
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_options.Timeout);
        try
        {
            var expected = file.ExpectedLength;
            var existing = File.Exists(part) ? new FileInfo(part).Length : 0L;
            if (existing > expected) throw new InvalidDataException("Partial asset is larger than the pinned size.");
            if (expected == 0)
            {
                await File.WriteAllBytesAsync(part, [], linked.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(file.Sha256))
                {
                    var emptyHash = await HashFileAsync(part, linked.Token).ConfigureAwait(false);
                    if (!emptyHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(part);
                        throw new InvalidDataException("Downloaded empty asset SHA-256 does not match the pinned hash.");
                    }
                }
                ReplacePart(part, target);
                return;
            }

            await using var output = new FileStream(part, existing == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
            using var client = _clientFactory(useProxy);
            var completed = existing;
            var chunks = 0;
            while (completed < expected)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (++chunks > _options.MaxChunks) throw new InvalidDataException("Bounded download chunk count exceeded.");
                var end = Math.Min(expected - 1, checked(completed + _options.ChunkBytes - 1));
                using var request = new HttpRequestMessage(HttpMethod.Get, file.Source);
                request.Headers.Range = new RangeHeaderValue(completed, end);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (completed > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                    throw new InvalidDataException("The asset server ignored a resume range request.");
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var contentRange = response.Content.Headers.ContentRange;
                    if (contentRange?.From != completed || contentRange.To is null || contentRange.To.Value != end ||
                        contentRange.Length is not null && contentRange.Length.Value != expected)
                        throw new InvalidDataException("The asset server returned an invalid Content-Range response.");
                }
                if (response.Content.Headers.ContentLength is long contentLength && contentLength != end - completed + 1)
                    throw new InvalidDataException("The asset server returned a byte count different from its Range response.");
                await using var input = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
                var remaining = end - completed + 1;
                var buffer = new byte[Math.Min(1024 * 1024, _options.ChunkBytes)];
                while (remaining > 0)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), linked.Token).ConfigureAwait(false);
                    if (read == 0) throw new EndOfStreamException("The asset server ended a range before its requested end.");
                    await output.WriteAsync(buffer.AsMemory(0, read), linked.Token).ConfigureAwait(false);
                    completed = checked(completed + read);
                    remaining -= read;
                    progress?.Report(new ModelDownloadProgress(file.RelativePath, completed, expected));
                }
            }
            await output.FlushAsync(linked.Token).ConfigureAwait(false);
            await output.DisposeAsync().ConfigureAwait(false);
            if (new FileInfo(part).Length != expected) throw new InvalidDataException("Downloaded asset length does not match the pinned size.");
            if (!string.IsNullOrWhiteSpace(file.Sha256))
            {
                var actual = await HashFileAsync(part, linked.Token).ConfigureAwait(false);
                if (!actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(part);
                    throw new InvalidDataException("Downloaded asset SHA-256 does not match the pinned hash.");
                }
            }
            ReplacePart(part, target);
        }
        finally { linked.Dispose(); }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void ReplacePart(string part, string target)
    {
        File.Move(part, target, overwrite: true);
    }
}

/// <summary>
/// Owns the local release directory and durable installation inventory. A lease
/// is required while a model is loaded; uninstall and repair refuse active leases.
/// </summary>
public sealed class ModelPackageStore
{
    private const int MaxPackages = 1024;
    private const int MaxFilesPerPackage = 4096;
    private const int MaxModelIdSegments = 8;
    private readonly ConcurrentDictionary<string, int> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly ModelAssetDownloader _downloader;

    public ModelPackageStore(string rootDirectory, ModelAssetDownloader? downloader = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("Model root is required.", nameof(rootDirectory));
        RootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(RootDirectory);
        _downloader = downloader ?? new ModelAssetDownloader();
    }

    public string RootDirectory { get; }

    public string GetPackageDirectory(string modelId, string revision)
    {
        ValidateIdentity(modelId, revision);
        var path = RootDirectory;
        foreach (var segment in modelId.Split('/', StringSplitOptions.RemoveEmptyEntries)) path = Path.Combine(path, segment);
        path = Path.Combine(path, revision);
        var full = Path.GetFullPath(path);
        var prefix = RootDirectory.EndsWith(Path.DirectorySeparatorChar) ? RootDirectory : RootDirectory + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ModelPackageException("decision_manifest_path_invalid", "Model package path escapes the model root.");
        return full;
    }

    public IReadOnlyList<InstalledModelRecord> ListInstalled(CancellationToken cancellationToken = default)
    {
        var records = new List<InstalledModelRecord>();
        var directories = 0;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((RootDirectory, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            if (++directories > MaxPackages * (MaxModelIdSegments + 2)) throw new ModelPackageException("decision_asset_limit_exceeded", "The model directory count exceeds the bounded limit.");
            var recordPath = Path.Combine(directory, "installation.json");
            if (File.Exists(recordPath))
            {
                if (records.Count >= MaxPackages) throw new ModelPackageException("decision_asset_limit_exceeded", "The installed package count exceeds the bounded limit.");
                records.Add(ReadInstallationRecord(recordPath));
                continue;
            }
            if (depth >= MaxModelIdSegments + 1) continue;
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                var name = Path.GetFileName(child);
                if (name.Contains(".staging-", StringComparison.OrdinalIgnoreCase) || name.Contains(".previous-", StringComparison.OrdinalIgnoreCase)) continue;
                pending.Push((child, depth + 1));
            }
        }
        return records;
    }

    public ModelPackageLease Acquire(string modelId, string revision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var package = GetPackageDirectory(modelId, revision);
        var recordPath = Path.Combine(package, "installation.json");
        if (!File.Exists(recordPath)) throw new ModelPackageException("decision_model_not_installed", $"Model '{modelId}@{revision}' is not installed.");
        var record = ReadInstallationRecord(recordPath);
        if (Path.IsPathRooted(record.PackagePath) || record.PackagePath.Contains("..", StringComparison.Ordinal) ||
            !record.ModelId.Equals(modelId, StringComparison.Ordinal) || !record.Revision.Equals(revision, StringComparison.Ordinal) ||
            !Path.GetFullPath(Path.Combine(RootDirectory, record.PackagePath)).Equals(package, StringComparison.OrdinalIgnoreCase))
            throw new ModelPackageException("decision_installation_invalid", "Installation record identity does not match its package directory.");
        var key = Key(modelId, revision);
        _leases.AddOrUpdate(key, 1, static (_, count) => checked(count + 1));
        return new ModelPackageLease(this, key, modelId, revision, package);
    }

    public void Unload(string modelId, string revision)
    {
        ValidateIdentity(modelId, revision);
        if (_leases.TryGetValue(Key(modelId, revision), out var count) && count > 0)
            throw new ModelPackageException("decision_session_busy", "Cannot unload a model while a package lease is active.");
    }

    public async Task<InstalledModelRecord> InstallAsync(ModelPackageSpec specification, IProgress<ModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ValidateIdentity(specification.ModelId, specification.Revision);
        if (specification.Files is null || specification.Files.Count == 0 || specification.Files.Count > MaxFilesPerPackage)
            throw new ModelPackageException("decision_asset_limit_exceeded", "A package must contain a bounded number of files.");
        var manifestRelative = NormalizeAssetPath(specification.ManifestPath);
        foreach (var file in specification.Files)
        {
            var normalized = NormalizeAssetPath(file.RelativePath);
            if (normalized.Equals("installation.json", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                throw new ModelPackageException("decision_asset_reserved", $"Asset path '{normalized}' is reserved for package lifecycle state.");
        }
        if (!specification.Files.Any(file => string.Equals(NormalizeAssetPath(file.RelativePath), manifestRelative, StringComparison.OrdinalIgnoreCase)))
            throw new ModelPackageException("decision_manifest_missing", "The declared model manifest must be included in the package file list.");
        var target = GetPackageDirectory(specification.ModelId, specification.Revision);
        EnsureNotLeased(specification.ModelId, specification.Revision);
        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in specification.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = CombineWithin(staging, file.RelativePath);
                await _downloader.DownloadAsync(file, destination, progress, cancellationToken).ConfigureAwait(false);
            }
            var assets = BuildAssetInventory(staging, specification.Files.Select(file => new ModelAssetFile { RelativePath = file.RelativePath, Sha256 = file.Sha256 ?? HashFile(CombineWithin(staging, file.RelativePath), cancellationToken), Length = new FileInfo(CombineWithin(staging, file.RelativePath)).Length }).ToArray(), cancellationToken);
            ValidateManifestFile(staging, specification.ManifestPath, specification.ModelId, specification.Revision, cancellationToken);
            EnsureNotLeased(specification.ModelId, specification.Revision);
            var record = new InstalledModelRecord { ModelId = specification.ModelId, Revision = specification.Revision, PackagePath = Path.GetRelativePath(RootDirectory, target), InstalledAtUtc = DateTimeOffset.UtcNow, Files = assets };
            WriteRecord(staging, record);
            CommitStaging(staging, target);
            return record;
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    public async Task<InstalledModelRecord> InstallDirectoryAsync(string sourceDirectory, string modelId, string revision, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory)) throw new ModelPackageException("decision_model_not_installed", "Source model package directory does not exist.");
        ValidateIdentity(modelId, revision);
        EnsureNotLeased(modelId, revision);
        var target = GetPackageDirectory(modelId, revision);
        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            var files = new List<ModelAssetFile>();
            var count = 0;
            foreach (var source in Directory.EnumerateFiles(Path.GetFullPath(sourceDirectory), "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > MaxFilesPerPackage) throw new ModelPackageException("decision_asset_limit_exceeded", "The package file count exceeds the bounded limit.");
                var attributes = File.GetAttributes(source);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ModelPackageException("decision_manifest_path_invalid", "Model packages cannot contain symlinked files.");
                var relative = Path.GetRelativePath(sourceDirectory, source);
                var normalizedRelative = NormalizeAssetPath(relative);
                if (normalizedRelative.Equals("installation.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (normalizedRelative.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) throw new ModelPackageException("decision_asset_reserved", "Partial download files cannot be installed.");
                var destination = CombineWithin(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyFileAsync(source, destination, cancellationToken).ConfigureAwait(false);
                files.Add(new ModelAssetFile { RelativePath = normalizedRelative, Sha256 = HashFile(destination, cancellationToken), Length = new FileInfo(destination).Length });
            }
            ValidateManifestFile(staging, "model.json", modelId, revision, cancellationToken);
            EnsureNotLeased(modelId, revision);
            var record = new InstalledModelRecord { ModelId = modelId, Revision = revision, PackagePath = Path.GetRelativePath(RootDirectory, target), InstalledAtUtc = DateTimeOffset.UtcNow, Files = files.ToArray() };
            WriteRecord(staging, record);
            CommitStaging(staging, target);
            return record;
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    public void Uninstall(string modelId, string revision)
    {
        ValidateIdentity(modelId, revision);
        EnsureNotLeased(modelId, revision);
        var target = GetPackageDirectory(modelId, revision);
        if (!Directory.Exists(target)) throw new ModelPackageException("decision_model_not_installed", $"Model '{modelId}@{revision}' is not installed.");
        Directory.Delete(target, recursive: true);
        var parent = Directory.GetParent(target)?.FullName;
        if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }

    internal void Release(string key)
    {
        while (_leases.TryGetValue(key, out var count))
        {
            if (count <= 1) { _leases.TryRemove(key, out _); return; }
            if (_leases.TryUpdate(key, count - 1, count)) return;
        }
    }

    private static string Key(string modelId, string revision) => modelId + "@" + revision;

    private void EnsureNotLeased(string modelId, string revision)
    {
        if (_leases.TryGetValue(Key(modelId, revision), out var count) && count > 0)
            throw new ModelPackageException("decision_session_busy", "Cannot replace or uninstall a model while a package lease is active.");
    }

    private static void ValidateIdentity(string modelId, string revision)
    {
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Length > 256 || modelId.StartsWith('/') || modelId.EndsWith('/') || modelId.Contains("..", StringComparison.Ordinal))
            throw new ModelPackageException("decision_model_identity_invalid", "Model id is invalid.");
        var segments = modelId.Split('/');
        if (segments.Length > MaxModelIdSegments) throw new ModelPackageException("decision_model_identity_invalid", "Model id contains too many path segments.");
        foreach (var segment in segments) if (segment.Length == 0 || segment is "." or ".." || segment.IndexOfAny(['\\', ':', '\0']) >= 0) throw new ModelPackageException("decision_model_identity_invalid", "Model id contains an invalid path segment.");
        if (string.IsNullOrWhiteSpace(revision) || revision.Length > 128 || revision is "." or ".." || revision.IndexOfAny(['/', '\\', ':', '\0']) >= 0) throw new ModelPackageException("decision_model_identity_invalid", "Model revision is invalid.");
    }

    private string CombineWithin(string package, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new ModelPackageException("decision_manifest_path_invalid", "Asset paths must be relative.");
        var full = Path.GetFullPath(Path.Combine(package, relative));
        var prefix = package.EndsWith(Path.DirectorySeparatorChar) ? package : package + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ModelPackageException("decision_manifest_path_invalid", "Asset path escapes the package directory.");
        return full;
    }

    private ModelAssetFile[] BuildAssetInventory(string staging, ModelAssetFile[] requested, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in requested)
        {
            var normalized = NormalizeAssetPath(file.RelativePath);
            if (normalized.Equals("installation.json", StringComparison.OrdinalIgnoreCase)) throw new ModelPackageException("decision_asset_reserved", "installation.json is reserved for the installation inventory.");
            if (!seen.Add(normalized)) throw new ModelPackageException("decision_asset_duplicate", $"Asset '{normalized}' is listed more than once.");
            var path = CombineWithin(staging, normalized);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Length) throw new ModelPackageException("decision_asset_missing", $"Asset '{normalized}' is incomplete.");
            cancellationToken.ThrowIfCancellationRequested();
        }
        return requested.Select(file => file with { RelativePath = NormalizeAssetPath(file.RelativePath) }).ToArray();
    }

    private static string NormalizeAssetPath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new ModelPackageException("decision_manifest_path_invalid", "Asset paths must be relative.");
        var normalized = relative.Replace('\\', '/');
        foreach (var segment in normalized.Split('/', StringSplitOptions.None))
            if (segment.Length == 0 || segment is "." or ".." || segment.IndexOfAny([':', '\0']) >= 0)
                throw new ModelPackageException("decision_manifest_path_invalid", "Asset paths cannot contain empty or traversal segments.");
        return normalized;
    }

    private void ValidateManifestFile(string staging, string relative, string modelId, string revision, CancellationToken cancellationToken)
    {
        var path = CombineWithin(staging, relative);
        if (!File.Exists(path)) throw new ModelPackageException("decision_manifest_missing", "model.json is missing from the package.");
        try
        {
            var bytes = File.ReadAllBytes(path);
            // UTF-8 BOM is common in Windows-created manifests; JSON readers otherwise
            // report it as an invalid first value. Keep the accepted encoding explicit.
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ModelPackageException("decision_manifest_invalid", "Model manifest must be a JSON object.");
            if (document.RootElement.TryGetProperty("model_id", out var id) && id.ValueKind == JsonValueKind.String && !string.Equals(id.GetString(), modelId, StringComparison.Ordinal)) throw new ModelPackageException("decision_manifest_invalid", "Manifest model id does not match the requested package.");
            if (document.RootElement.TryGetProperty("revision", out var rev) && rev.ValueKind == JsonValueKind.String && !string.Equals(rev.GetString(), revision, StringComparison.Ordinal)) throw new ModelPackageException("decision_manifest_invalid", "Manifest revision does not match the requested package.");
        }
        catch (JsonException exception) { throw new ModelPackageException("decision_manifest_invalid", "Model manifest is not valid JSON.", exception); }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0) { cancellationToken.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        await input.CopyToAsync(output, 1024 * 1024, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteRecord(string staging, InstalledModelRecord record)
    {
        var path = Path.Combine(staging, "installation.json");
        File.WriteAllText(path, JsonSerializer.Serialize(record, DecisionJsonContext.Default.InstalledModelRecord) + Environment.NewLine);
    }

    private static InstalledModelRecord ReadInstallationRecord(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
            return JsonSerializer.Deserialize(bytes, DecisionJsonContext.Default.InstalledModelRecord)
                ?? throw new ModelPackageException("decision_installation_invalid", $"Installation record '{path}' is empty.");
        }
        catch (JsonException exception) { throw new ModelPackageException("decision_installation_invalid", $"Installation record '{path}' is invalid.", exception); }
    }

    private static void CommitStaging(string staging, string target)
    {
        var parent = Directory.GetParent(target)?.FullName ?? throw new InvalidOperationException("Package target has no parent.");
        Directory.CreateDirectory(parent);
        var backup = target + ".previous-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(target)) Directory.Move(target, backup);
        try { Directory.Move(staging, target); }
        catch { if (Directory.Exists(backup)) Directory.Move(backup, target); throw; }
        finally { if (Directory.Exists(backup)) TryDeleteDirectory(backup); }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* preserve the original install error */ }
    }
}

public sealed class ModelPackageLease : IDisposable
{
    private ModelPackageStore? _store;
    internal ModelPackageLease(ModelPackageStore store, string key, string modelId, string revision, string directoryPath)
    { _store = store; Key = key; ModelId = modelId; Revision = revision; DirectoryPath = directoryPath; }
    public string Key { get; }
    public string ModelId { get; }
    public string Revision { get; }
    public string DirectoryPath { get; }
    public void Dispose() => Interlocked.Exchange(ref _store, null)?.Release(Key);
}
