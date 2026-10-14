using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sezika.Tooling;

internal static partial class AssetTransport
{
    internal static async Task<DownloadReport> DownloadAsync(DownloadSpec spec, string destination, Budget budget,
        CancellationToken cancellation, string? bearerToken = null, HttpClient? client = null, AuthenticationHeaderValue? authentication = null)
    {
        var result = await DownloadCoreAsync(new(spec.Url, spec.Bytes, new("sha256", spec.Sha256), spec.ETag, spec.RangeBytes),
            destination, budget, cancellation, bearerToken, client, authentication, legacySha256State: true);
        return new(result.Status, result.Path, result.Bytes, result.ContentSha256, result.ETag, result.ResumedBytes, result.Requests, result.IndependentlyReadBack);
    }

    internal static Task<DigestDownloadReport> DownloadDigestAsync(DigestDownloadSpec spec, string destination, Budget budget,
        CancellationToken cancellation, string? bearerToken = null, HttpClient? client = null, AuthenticationHeaderValue? authentication = null) =>
        DownloadCoreAsync(spec, destination, budget, cancellation, bearerToken, client, authentication, legacySha256State: false);

    private static async Task<DigestDownloadReport> DownloadCoreAsync(DigestDownloadSpec spec, string destination, Budget budget,
        CancellationToken cancellation, string? bearerToken, HttpClient? client, AuthenticationHeaderValue? authentication, bool legacySha256State)
    {
        budget.Check(); cancellation.ThrowIfCancellationRequested(); ValidateContentDigest(spec.ExpectedDigest);
        // Both caller cancellation sources govern disk verification as well as HTTP.
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation, budget.Cancellation);
        cancellation = linkedCancellation.Token;
        budget = new Budget(86400, null, cancellation, budget);
        var uri = ValidateUri(spec.Url, allowQuery: false);
        ValidateAuthentication(bearerToken, authentication);
        if ((bearerToken is not null || authentication is not null) && !uri.IsLoopback && uri.Host is not ("huggingface.co" or "moark.com") &&
            (legacySha256State || uri.Host is not ("raw.githubusercontent.com" or "api.github.com")))
            throw new InvalidDataException("Stored model credentials may only be sent to the explicit HF/MoArk endpoint.");
        if (client?.DefaultRequestHeaders.Authorization is not null) throw new InvalidDataException("Shared client authentication headers are forbidden; use per-request scoped authentication.");
        if (spec.Bytes is <= 0 or > MaximumFileBytes || spec.RangeBytes is < 1 or > 64L * 1024 * 1024 ||
            (spec.Bytes + spec.RangeBytes - 1) / spec.RangeBytes > 4096)
            throw new InvalidDataException("Download byte/range/request bound exceeded.");
        if (spec.ETag is not null) ValidateETag(spec.ETag);
        destination = Path.GetFullPath(destination); SafePaths.NoLinks(destination, budget);
        if (Path.Exists(destination)) throw new IOException("Completed destination exists; preserve it.");
        if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new DirectoryNotFoundException("Destination parent must exist.");
        var partial = destination + ".sezika-part";
        var statePath = destination + ".sezika-download.json";
        SafePaths.NoLinks(partial, budget); SafePaths.NoLinks(statePath, budget);
        DigestDownloadState state;
        if (Path.Exists(partial) || Path.Exists(statePath))
        {
            if (!File.Exists(partial) || !File.Exists(statePath)) throw new InvalidDataException("Incomplete resume ownership state.");
            RegularFiles.Require(partial, budget);
            if (legacySha256State)
            {
                var legacy = ReadJson(statePath, TransportJsonContext.Default.DownloadState, budget);
                state = new(legacy.SchemaVersion, legacy.Url, legacy.Bytes, new("sha256", legacy.Sha256), legacy.ETag);
            }
            else state = ReadJson(statePath, TransportJsonContext.Default.DigestDownloadState, budget);
            if (state.SchemaVersion != 1 || state.Url != spec.Url || state.Bytes != spec.Bytes || state.ExpectedDigest != spec.ExpectedDigest ||
                state.ETag is null || (spec.ETag is not null && state.ETag != spec.ETag))
                throw new InvalidDataException("Resume URL/length/SHA/ETag identity differs.");
            ValidateETag(state.ETag);
        }
        else state = new(1, spec.Url, spec.Bytes, spec.ExpectedDigest, spec.ETag);
        var resumed = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (resumed > spec.Bytes) throw new InvalidDataException("Partial asset exceeds trusted byte length.");
        var requests = 0;
        string contentSha256;
        HttpClient? owned = null;
        try
        {
            var http = client ?? (owned = CreateClient(useProxy: false, seconds: ClientSeconds(budget)));
            using (var target = new FileStream(partial, resumed > 0 || File.Exists(partial) ? FileMode.Open : FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None))
            {
                target.Position = resumed;
                for (var range = 0; range < 4096 && target.Length < spec.Bytes; range++)
                {
                    budget.Check(); cancellation.ThrowIfCancellationRequested();
                    var start = target.Length; var end = Math.Min(spec.Bytes - 1, start + spec.RangeBytes - 1);
                    HttpResponseMessage response;
                    try { response = await GetRangeAsync(http, uri, start, end, state.ETag, bearerToken, budget, cancellation, authentication); requests++; }
                    catch (HttpRequestException) when (client is null && owned is not null)
                    {
                        // A GET has no remote mutation. Retry this request once through the machine-local proxy.
                        owned.Dispose(); owned = CreateClient(useProxy: true, seconds: ClientSeconds(budget)); http = owned;
                        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation);
                        response = await GetRangeAsync(http, uri, start, end, state.ETag, bearerToken, budget, cancellation, authentication); requests += 2;
                    }
                    using (response)
                    {
                        if (response.StatusCode != HttpStatusCode.PartialContent)
                        {
                            if (legacySha256State) throw new InvalidDataException("Server did not honor bounded HTTP Range with 206.");
                            throw new DownloadHttpException((int)response.StatusCode);
                        }
                        var contentRange = response.Content.Headers.ContentRange;
                        var count = end - start + 1;
                        if (contentRange is null || contentRange.Unit != "bytes" || contentRange.From != start || contentRange.To != end ||
                            contentRange.Length != spec.Bytes || response.Content.Headers.ContentLength is not null && response.Content.Headers.ContentLength != count ||
                            response.Content.Headers.ContentEncoding.Count != 0)
                            throw new InvalidDataException("HTTP Content-Range/length/encoding differs from the trusted object.");
                        var tag = response.Headers.ETag?.ToString() ?? throw new InvalidDataException("Resume requires a strong ETag.");
                        ValidateETag(tag);
                        if (state.ETag is not null && state.ETag != tag) throw new InvalidDataException("HTTP object ETag changed; preserve partial bytes for inspection.");
                        if (!File.Exists(statePath))
                        {
                            state = state with { ETag = tag };
                            if (legacySha256State)
                                WriteJson(statePath, new DownloadState(state.SchemaVersion, state.Url, state.Bytes, state.ExpectedDigest.Value, state.ETag), TransportJsonContext.Default.DownloadState, budget);
                            else WriteJson(statePath, state, TransportJsonContext.Default.DigestDownloadState, budget);
                        }
                        else if (state.ETag != tag) throw new InvalidDataException("Download state ETag differs.");
                        using var requestDeadline = HttpDeadline(budget, cancellation, 1800);
                        await using var source = await response.Content.ReadAsStreamAsync(requestDeadline.Token);
                        var buffer = new byte[BlockBytes]; long remaining = count;
                        // A stream is permitted to return one byte at a time, so cap reads by requested bytes, not block count.
                        for (long readIndex = 0; readIndex < count + 1; readIndex++)
                        {
                            budget.Check();
                            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining + 1)), requestDeadline.Token);
                            if (read == 0)
                            {
                                if (remaining != 0) throw new EndOfStreamException("HTTP Range truncated; partial file retained for resume.");
                                break;
                            }
                            if (read > remaining) throw new InvalidDataException("HTTP Range returned excess bytes.");
                            await target.WriteAsync(buffer.AsMemory(0, read), requestDeadline.Token); remaining -= read;
                        }
                        if (remaining != 0) throw new InvalidDataException("HTTP Range read cap exceeded.");
                        target.Flush(flushToDisk: true);
                    }
                }
                if (target.Length != spec.Bytes) throw new InvalidDataException("Download maximum range count reached.");
                target.Position = 0;
                contentSha256 = VerifyContentDigest(target, spec.Bytes, spec.ExpectedDigest, budget);
                target.Flush(flushToDisk: true);
            }
            budget.Check(); SafePaths.NoLinks(destination, budget);
            File.Move(partial, destination, overwrite: false);
            if (VerifyDownloadedFile(destination, spec.Bytes, spec.ExpectedDigest, budget) != contentSha256)
                throw new InvalidDataException("Completed asset independent disk readback differs.");
            var status = spec.ExpectedDigest.Kind == "sha256" ? "PASS_complete_download_bytes_sha256" : "PASS_complete_download_bytes_git_blob_sha1";
            return new(status, destination, spec.Bytes, spec.ExpectedDigest, contentSha256, state.ETag!, resumed, requests, true);
        }
        finally { owned?.Dispose(); }
    }

    internal static void ValidateContentDigest(ContentDigest digest)
    {
        if (digest is null || digest.Value is null) throw new InvalidDataException("Expected content digest is required.");
        if (digest.Kind == "sha256") ValidateSha(digest.Value);
        else if (digest.Kind == "git-blob-sha1") ValidateCommit(digest.Value);
        else throw new InvalidDataException("Expected digest kind must be sha256 or git-blob-sha1.");
    }

    internal static string VerifyDownloadedFile(string path, long bytes, ContentDigest expected, Budget budget)
    {
        budget.Check(); ValidateContentDigest(expected); RegularFiles.Require(path, budget);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != bytes) throw new InvalidDataException("Completed asset trusted byte length differs.");
        return VerifyContentDigest(input, bytes, expected, budget);
    }

    private static string VerifyContentDigest(Stream input, long bytes, ContentDigest expected, Budget budget)
    {
        if (bytes is < 0 or > MaximumFileBytes) throw new InvalidDataException("Digest byte bound exceeded.");
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var blob = expected.Kind == "git-blob-sha1" ? IncrementalHash.CreateHash(HashAlgorithmName.SHA1) : null;
        blob?.AppendData(System.Text.Encoding.ASCII.GetBytes("blob " + bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + '\0'));
        var buffer = new byte[BlockBytes]; long total = 0;
        for (long readIndex = 0; readIndex < bytes + 1; readIndex++)
        {
            budget.Check();
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes - total + 1));
            budget.Check();
            if (read == 0)
            {
                if (total != bytes) throw new InvalidDataException("Digest source truncated.");
                var contentSha256 = Convert.ToHexStringLower(sha256.GetHashAndReset());
                var actual = blob is null ? contentSha256 : Convert.ToHexStringLower(blob.GetHashAndReset());
                if (actual != expected.Value) throw new InvalidDataException("Downloaded bytes differ from the frozen expected digest.");
                return contentSha256;
            }
            total += read;
            if (total > bytes) throw new InvalidDataException("Digest source grew.");
            sha256.AppendData(buffer, 0, read); blob?.AppendData(buffer, 0, read);
        }
        throw new InvalidDataException("Digest read iteration bound exceeded.");
    }

    internal static HttpClient CreateClient(bool useProxy, int seconds = 30)
    {
        if (seconds is < 1 or > 1800) throw new ArgumentOutOfRangeException(nameof(seconds));
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None, AllowAutoRedirect = false, UseProxy = useProxy };
        if (useProxy) handler.Proxy = new WebProxy("http://127.0.0.1:7890");
        return new(handler) { Timeout = TimeSpan.FromSeconds(seconds) };
    }

    internal static int ClientSeconds(Budget budget)
    {
        budget.Check(); return Math.Clamp((int)Math.Ceiling(budget.RemainingSeconds), 1, 1800);
    }

    private static CancellationTokenSource HttpDeadline(Budget budget, CancellationToken cancellation, int maximumSeconds)
    {
        budget.Check(); var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, budget.Cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(.01, Math.Min(maximumSeconds, budget.RemainingSeconds)))); return deadline;
    }

    internal static Uri ValidateUri(string raw, bool allowQuery)
    {
        if (raw.Length > 8192 || !Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            (!allowQuery && uri.Query.Length != 0) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidDataException("Only HTTPS or explicit loopback HTTP without embedded credentials is allowed.");
        return uri;
    }

    private static void ValidateETag(string value)
    {
        if (!EntityTagHeaderValue.TryParse(value, out var tag) || tag.IsWeak || tag.Tag == "*" || value.Length > 1024)
            throw new InvalidDataException("Strong finite ETag identity required.");
    }

    private static void ValidateAuthentication(string? bearer, AuthenticationHeaderValue? authentication)
    {
        if (bearer is not null && authentication is not null) throw new InvalidDataException("Only one explicit HTTP authentication source is allowed.");
        if (bearer is not null && (bearer.Length is < 1 or > 8192 || bearer.Any(char.IsControl))) throw new InvalidDataException("Bearer credential text bound differs.");
        if (authentication is not null && (!(authentication.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || authentication.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase)) ||
            authentication.Parameter is null || authentication.Parameter.Length is < 1 or > 8192 || authentication.Parameter.Any(char.IsControl)))
            throw new InvalidDataException("Explicit bounded Basic or Bearer authentication required.");
    }

    private static void Authenticate(HttpRequestMessage request, string? bearer, AuthenticationHeaderValue? authentication)
    {
        ValidateAuthentication(bearer, authentication);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        else if (authentication is not null) request.Headers.Authorization = new(authentication.Scheme, authentication.Parameter);
    }

    private static bool RedirectHostAllowed(Uri original, Uri next) => next.Host == original.Host ||
        original.Host == "huggingface.co" && (next.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase) ||
        next.Host.EndsWith(".hf.co", StringComparison.OrdinalIgnoreCase) || next.Host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase));

    private static async Task<HttpResponseMessage> GetRangeAsync(HttpClient client, Uri original, long start, long end, string? tag,
        string? token, Budget budget, CancellationToken cancellation, AuthenticationHeaderValue? authentication)
    {
        var current = original;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            budget.Check();
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Range = new RangeHeaderValue(start, end);
            if (tag is not null) request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(tag));
            if (current.Authority == original.Authority) Authenticate(request, token, authentication);
            using var deadline = HttpDeadline(budget, cancellation, 30);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode is not (HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location is null) throw new InvalidDataException("HTTP redirect omitted location.");
            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            _ = ValidateUri(next.AbsoluteUri, allowQuery: true);
            if (!RedirectHostAllowed(original, next) || next.Scheme != original.Scheme) throw new InvalidDataException("HTTP redirect escaped approved download hosts.");
            current = next;
        }
        throw new InvalidDataException("HTTP redirect count exceeded.");
    }
}
