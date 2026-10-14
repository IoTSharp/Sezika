using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace Sezika.Tooling;

internal static class CorpusSourceAcquisition
{
    private const int MaximumSources = 96;
    private const int MaximumAssets = 4096;

    internal static async Task<SourceAcquisitionReport> AcquireAsync(SourceAcquisitionRequest request, Budget budget,
        CancellationToken cancellation, HttpClient? client = null, AuthenticationHeaderValue? authentication = null)
    {
        budget.Check(); cancellation.ThrowIfCancellationRequested(); AssetTransport.ValidateSha(request.PlanSha256);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation, budget.Cancellation);
        cancellation = linkedCancellation.Token;
        budget = new Budget(86400, null, cancellation, budget);
        if (client?.DefaultRequestHeaders.Authorization is not null) throw new InvalidDataException("Client-wide authentication is forbidden.");
        var plan = ReadPlan(request.PlanPath, request.PlanSha256, budget);
        var sources = ValidatePlan(plan, request.MetadataRoot, budget);
        var output = Path.GetFullPath(request.OutputRoot); SafePaths.NoLinks(output, budget);
        var receipt = Path.GetFullPath(request.ReceiptDirectory); SafePaths.NoLinks(receipt, budget);
        if (Path.Exists(receipt)) throw new IOException("Preserve existing acquisition receipt directory.");
        if (Within(output, receipt) || Within(receipt, output)) throw new InvalidDataException("Cache and receipt directories must be disjoint.");
        var selected = Selection(plan, request.SelectedAssetIds, budget);
        if (authentication is not null)
        {
            // One memory credential has one admitted origin. A mixed public
            // plan is valid without authentication or with a selected subset.
            string? authenticationOrigin = null;
            foreach (var asset in plan.Assets)
            {
                budget.Check();
                if (!selected.Contains(asset.Id)) continue;
                var origin = new Uri(asset.OfficialUrl).GetLeftPart(UriPartial.Authority);
                authenticationOrigin ??= origin;
                if (origin != authenticationOrigin)
                    throw new InvalidDataException("A source acquisition credential requires one selected official origin.");
            }
        }
        var entries = new SourceAcquisitionEntry[plan.Assets.Length];
        for (var index = 0; index < plan.Assets.Length; index++)
        {
            budget.Check(); var asset = plan.Assets[index]; var source = sources[asset.SourceId];
            entries[index] = new(asset.Id, source.Id, source.Revision, source.DeclaredLicense, asset.Path, asset.Split, asset.OfficialUrl, request.PlanSha256, asset.Bytes,
                asset.ExpectedDigest, selected.Contains(asset.Id) ? "NOT_ATTEMPTED" : "NOT_SELECTED", 0, null, 0, [],
                MirrorDecision: !asset.PreferMirror ? "NOT_REQUESTED" : source.Provider != "huggingface-dataset" ? "INELIGIBLE_PROVIDER" :
                    authentication is not null ? "DISABLED_AUTHENTICATION" : "ELIGIBLE_PINNED_REVISION_AND_DIGEST");
        }
        Directory.CreateDirectory(output); Directory.CreateDirectory(receipt);
        Exception? stopped = null;
        for (var index = 0; index < plan.Assets.Length; index++)
        {
            var asset = plan.Assets[index];
            if (!selected.Contains(asset.Id)) continue;
            try
            {
                budget.Check(); cancellation.ThrowIfCancellationRequested();
                var outcome = await AcquireAsset(asset, entries[index], output, budget, cancellation, client, authentication);
                entries[index] = outcome.Entry;
                if (outcome.Stopped is not null) { stopped = outcome.Stopped; break; }
            }
            catch (Exception error) when (error is OperationCanceledException or TimeoutException)
            {
                entries[index] = entries[index] with { Status = error is OperationCanceledException ? "CANCELLED" : "TIMED_OUT", FailureType = error.GetType().Name };
                stopped = error; break;
            }
            catch (Exception error) when (Failure(error))
            {
                entries[index] = entries[index] with { Status = "FAILED", FailureType = error.GetType().Name };
            }
        }
        // Receipt-only cleanup has its own explicit two-second limit. It never
        // extends the download budget or authorizes another request after cancel.
        var receiptBudget = new Budget(2, null, CancellationToken.None);
        var report = Report(plan, request.PlanSha256, selected, entries, stopped, receiptBudget);
        var researchPlan = new SourceResearchPlanEntry[plan.Assets.Length];
        for (var index = 0; index < plan.Assets.Length; index++)
        {
            receiptBudget.Check(); var asset = plan.Assets[index]; var source = sources[asset.SourceId];
            researchPlan[index] = new(source.Id, source.Revision, asset.Split, asset.OfficialUrl, asset.Path, asset.Bytes,
                asset.ExpectedDigest.Kind == "sha256" ? asset.ExpectedDigest.Value : null, asset.ExpectedDigest, request.PlanSha256);
        }
        CorpusJson.Write(Path.Combine(receipt, "research-download-plan.json"), researchPlan, SourceAcquisitionJsonContext.Default.SourceResearchPlanEntryArray, receiptBudget);
        using (var writer = CorpusJson.Writer(Path.Combine(receipt, "source-ledger.jsonl")))
            for (var index = 0; index < entries.Length; index++)
            {
                receiptBudget.Check(); CorpusJson.Line(writer, entries[index], SourceAcquisitionJsonContext.Default.SourceAcquisitionEntry);
            }
        CorpusJson.Write(Path.Combine(receipt, "source-acquisition-report.json"), report, SourceAcquisitionJsonContext.Default.SourceAcquisitionReport, receiptBudget);
        receiptBudget.Check();
        if (stopped is not null) ExceptionDispatchInfo.Capture(stopped).Throw();
        return report;
    }

    private sealed record AcquisitionOutcome(SourceAcquisitionEntry Entry, Exception? Stopped = null);

    private static async Task<AcquisitionOutcome> AcquireAsset(SourceAcquisitionAsset asset, SourceAcquisitionEntry entry,
        string root, Budget budget, CancellationToken cancellation, HttpClient? client, AuthenticationHeaderValue? authentication)
    {
        var destination = AssetTransport.AssetPath(root, asset.Path, budget);
        if (File.Exists(destination))
        {
            var sha = AssetTransport.VerifyDownloadedFile(destination, asset.Bytes, asset.ExpectedDigest, budget);
            return new(entry with { Status = "CACHED_VERIFIED", Bytes = asset.Bytes, ContentSha256 = sha, RetainedPartialBytes = RetainedBytes(destination, budget) });
        }
        if (Path.Exists(destination)) throw new IOException("Destination is not a complete regular asset.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); SafePaths.NoLinks(destination, budget);
        var mirror = asset.PreferMirror && authentication is null && new Uri(asset.OfficialUrl).Host == "huggingface.co";
        var attempts = new List<SourceAcquisitionAttempt>(2);
        var routes = mirror ? new[] { "mirror", "official" } : new[] { "official" };
        for (var routeIndex = 0; routeIndex < routes.Length; routeIndex++)
        {
            var route = routes[routeIndex];
            var url = route == "mirror" ? "https://hf-mirror.com" + new Uri(asset.OfficialUrl).AbsolutePath : asset.OfficialUrl;
            var stage = destination + ".sezika-source-" + route;
            try
            {
                budget.Check(); cancellation.ThrowIfCancellationRequested();
                DigestDownloadReport? downloaded = null;
                string sha;
                if (File.Exists(stage)) sha = AssetTransport.VerifyDownloadedFile(stage, asset.Bytes, asset.ExpectedDigest, budget);
                else
                {
                    downloaded = await AssetTransport.DownloadDigestAsync(new(url, asset.Bytes, asset.ExpectedDigest, RangeBytes: asset.RangeBytes),
                        stage, budget, cancellation, client: client, authentication: route == "official" ? authentication : null);
                    sha = downloaded.ContentSha256;
                }
                budget.Check(); cancellation.ThrowIfCancellationRequested(); SafePaths.NoLinks(destination, budget);
                File.Move(stage, destination, overwrite: false);
                if (AssetTransport.VerifyDownloadedFile(destination, asset.Bytes, asset.ExpectedDigest, budget) != sha)
                    throw new InvalidDataException("Canonical source asset independent readback differs.");
                attempts.Add(new(route, "COMPLETE", null, downloaded?.Requests ?? 0, downloaded?.ResumedBytes ?? asset.Bytes));
                return new(entry with { Status = "DOWNLOADED_VERIFIED", Bytes = asset.Bytes, ContentSha256 = sha,
                    RetainedPartialBytes = RetainedBytes(destination, budget), Attempts = attempts.ToArray() });
            }
            catch (Exception error) when (Failure(error) || error is OperationCanceledException or TimeoutException)
            {
                Exception? stop = null;
                try { budget.Check(); cancellation.ThrowIfCancellationRequested(); }
                catch (Exception gate) when (gate is OperationCanceledException or TimeoutException) { stop = gate; }
                // A request-local timeout may use the one official fallback.
                // A cancelled caller or expired parent budget never does.
                var failureType = stop?.GetType().Name ?? (error is OperationCanceledException ? nameof(TimeoutException) : error.GetType().Name);
                attempts.Add(new(route, stop is OperationCanceledException ? "CANCELLED" : stop is TimeoutException ? "TIMED_OUT" : "FAILED", failureType, null, null,
                    error is DownloadHttpException httpFailure ? httpFailure.StatusCode : null));
                var partialBytes = RetainedBytes(destination, new Budget(1, null, CancellationToken.None));
                if (stop is not null)
                    return new(entry with { Status = stop is OperationCanceledException ? "CANCELLED" : "TIMED_OUT", FailureType = failureType,
                        RetainedPartialBytes = partialBytes, Attempts = attempts.ToArray() }, stop);
                if (routeIndex + 1 == routes.Length)
                    return new(entry with { Status = "FAILED", FailureType = failureType, RetainedPartialBytes = partialBytes, Attempts = attempts.ToArray() });
                // At most one mirror attempt and one official fallback; their
                // distinct sidecars never merge URL/ETag/partial identities.
            }
        }
        throw new InvalidDataException("Finite source route bound exhausted.");
    }

    private static SourceAcquisitionPlan ReadPlan(string path, string expectedSha256, Budget budget)
    {
        RegularFiles.Require(path, budget);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 1 or > AssetTransport.MaximumJsonBytes) throw new InvalidDataException("Source plan byte bound differs.");
        var expectedBytes = checked((int)input.Length); var buffer = new byte[expectedBytes + 1]; var count = 0;
        for (var readIndex = 0; readIndex <= expectedBytes; readIndex++)
        {
            budget.Check(); var read = input.Read(buffer.AsSpan(count)); budget.Check();
            count += read;
            if (count > expectedBytes) throw new InvalidDataException("Source plan grew during pinned read.");
            if (read != 0) continue;
            if (count != expectedBytes || CorpusJson.Sha(buffer.AsSpan(0, count)) != expectedSha256)
                throw new InvalidDataException("Frozen source acquisition plan bytes/SHA-256 differ.");
            using var json = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 32, AllowDuplicateProperties = false });
            return JsonSerializer.Deserialize(json.RootElement, SourceAcquisitionJsonContext.Default.SourceAcquisitionPlan)
                ?? throw new InvalidDataException("Source acquisition plan is missing.");
        }
        throw new InvalidDataException("Source plan read iteration bound exceeded.");
    }

    private static Dictionary<string, SourceAcquisitionSource> ValidatePlan(SourceAcquisitionPlan plan, string metadataRoot, Budget budget)
    {
        if (plan.SchemaVersion != 1 || plan.Sources.Length is < 1 or > MaximumSources || plan.Assets.Length is < 1 or > MaximumAssets)
            throw new InvalidDataException("Frozen source/asset denominator bound differs.");
        var sources = new Dictionary<string, SourceAcquisitionSource>(StringComparer.Ordinal);
        long metadataBytes = 0;
        foreach (var source in plan.Sources)
        {
            budget.Check();
            if (source is null || source.Id.Length is < 3 or > 256 || source.Id.Split('/').Length != 2 ||
                source.Id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')) ||
                source.Provider is not ("huggingface-dataset" or "github-raw") ||
                string.IsNullOrWhiteSpace(source.DeclaredLicense) || source.DeclaredLicense.Length > 256 || source.DeclaredLicense.Any(char.IsControl) ||
                source.MetadataPins.Length is < 1 or > 8 || !sources.TryAdd(source.Id, source))
                throw new InvalidDataException("Source identity/provider/license/metadata pins differ.");
            _ = SafePaths.Relative(source.Id);
            AssetTransport.ValidateCommit(source.Revision);
            var metadataPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in source.MetadataPins)
            {
                budget.Check(); if (pin is null) throw new InvalidDataException("Null metadata pin."); AssetTransport.ValidateSha(pin.Sha256);
                metadataBytes = checked(metadataBytes + pin.Bytes);
                if (pin.Bytes is < 1 or > 16 * 1024 * 1024 || metadataBytes > 64 * 1024 * 1024 || !metadataPaths.Add(pin.Path))
                    throw new InvalidDataException("Metadata pin byte/path bound differs.");
                _ = AssetTransport.VerifyDownloadedFile(AssetTransport.AssetPath(metadataRoot, pin.Path, budget), pin.Bytes, new("sha256", pin.Sha256), budget);
            }
        }
        var ids = new HashSet<string>(StringComparer.Ordinal); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var asset in plan.Assets)
        {
            budget.Check();
            if (asset is null || asset.Id.Length is < 1 or > 128 || asset.Id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) ||
                !ids.Add(asset.Id) || !sources.TryGetValue(asset.SourceId, out var source) || !paths.Add(asset.Path) ||
                asset.Path.Contains(".sezika-", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(asset.Split) || asset.Split.Length > 128 || asset.Split.Any(char.IsControl))
                throw new InvalidDataException("Asset identity/path/split differs.");
            _ = SafePaths.Relative(asset.Path); var remote = SafePaths.Relative(asset.RemotePath);
            AssetTransport.ValidateContentDigest(asset.ExpectedDigest); _ = AssetTransport.ValidateUri(asset.OfficialUrl, allowQuery: false);
            var prefix = source.Provider == "huggingface-dataset" ? "https://huggingface.co/datasets/" + source.Id + "/resolve/" : "https://raw.githubusercontent.com/" + source.Id + "/";
            var expectedUrl = prefix + source.Revision + "/" + string.Join('/', remote.Split('/').Select(Uri.EscapeDataString));
            if (asset.OfficialUrl != expectedUrl || asset.Bytes is < 1 or > AssetTransport.MaximumFileBytes ||
                asset.RangeBytes is < 1 or > 64L * 1024 * 1024 || (asset.Bytes + asset.RangeBytes - 1) / asset.RangeBytes > 4096)
                throw new InvalidDataException("Asset official URL/revision/length/range pin differs.");
            total = checked(total + asset.Bytes);
            if (total > AssetTransport.MaximumTotalBytes) throw new InvalidDataException("Source acquisition total byte bound exceeded.");
        }
        return sources;
    }

    private static HashSet<string> Selection(SourceAcquisitionPlan plan, string[]? requested, Budget budget)
    {
        if (requested is null) return plan.Assets.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        if (requested.Length is < 1 or > MaximumAssets) throw new InvalidDataException("Selected asset count bound differs.");
        var planned = plan.Assets.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal); var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in requested)
        {
            budget.Check(); if (!planned.Contains(id) || !selected.Add(id)) throw new InvalidDataException("Selected asset ID is absent or repeated.");
        }
        return selected;
    }

    private static SourceAcquisitionReport Report(SourceAcquisitionPlan plan, string planSha, HashSet<string> selected,
        SourceAcquisitionEntry[] entries, Exception? stopped, Budget budget)
    {
        var sourceResults = new List<SourceAcquisitionSourceResult>(plan.Sources.Length);
        foreach (var source in plan.Sources)
        {
            budget.Check(); var rows = entries.Where(entry => entry.SourceId == source.Id).ToArray();
            sourceResults.Add(new(source.Id, source.Revision, source.DeclaredLicense, source.MetadataPins, rows.Length,
                rows.Count(row => selected.Contains(row.Id)), rows.Count(Complete), rows.Count(row => row.Status is "FAILED" or "CANCELLED" or "TIMED_OUT"), rows.All(Complete)));
        }
        var complete = entries.Count(Complete); var failed = entries.Count(entry => entry.Status is "FAILED" or "CANCELLED" or "TIMED_OUT");
        return new(stopped is not null ? "STOPPED_WITH_LEDGER" : complete == selected.Count ? "PASS_SELECTED_SOURCE_ASSETS_VERIFIED" : "PARTIAL_SOURCE_ACQUISITION",
            planSha, plan.Sources.Length, plan.Assets.Length, selected.Count, complete, failed, entries.Count(entry => entry.Status == "NOT_ATTEMPTED"),
            entries.Count(entry => entry.Status == "NOT_SELECTED"), complete == selected.Count, complete == plan.Assets.Length, sourceResults.ToArray(), entries);
    }

    private static bool Complete(SourceAcquisitionEntry entry) => entry.Status is "CACHED_VERIFIED" or "DOWNLOADED_VERIFIED";
    private static bool Failure(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or ArgumentException or JsonException;
    private static bool Within(string root, string path) => string.Equals(path, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static long RetainedBytes(string destination, Budget budget)
    {
        budget.Check(); var mirror = destination + ".sezika-source-mirror.sezika-part"; var official = destination + ".sezika-source-official.sezika-part";
        SafePaths.NoLinks(mirror, budget); SafePaths.NoLinks(official, budget);
        return checked((File.Exists(mirror) ? new FileInfo(mirror).Length : 0) + (File.Exists(official) ? new FileInfo(official).Length : 0));
    }
}
