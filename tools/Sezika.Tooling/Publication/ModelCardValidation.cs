using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sezika.Tooling;

internal static class ModelCardValidation
{
    internal const int MaximumCardBytes = 1024 * 1024;
    internal const int MaximumResponseBytes = 1024 * 1024;

    // Official primary source, read without invoking the validation service:
    // huggingface/huggingface_hub commit 9feb0f8eae93b6698c78adc9d697133be6dad84c,
    // src/huggingface_hub/hf_api.py:11445-11485, SHA256
    // 5ac896338db48115a13222be9f46cc088bbdbedaf95b1b2f371d94b4b1912311.
    // _validate_yaml posts full README content + repoType to /api/validate-yaml.
    // Its consumed response fields are warnings[]/errors[], each with message.
    // This operation deliberately rejects malformed response contracts instead of
    // silently accepting a changed provider contract. It never returns messages.
    internal static async Task<ModelCardValidationReport> ValidateAsync(ModelCardValidationRequest request,
        Budget budget, CancellationToken cancellation, string? bearerToken = null)
    {
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            // Preserve per-command HTTPS_PROXY support without changing global settings.
            UseProxy = true,
            Credentials = null,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return await ValidateCoreAsync(request, client, budget, cancellation, bearerToken, fixtureOnly: false);
    }

    // Test-only injection: the root must be a literal loopback address. Results
    // always retain FixtureOnly=true and cannot be hosted HF acceptance evidence.
    internal static Task<ModelCardValidationReport> ValidateFixtureAsync(ModelCardValidationRequest request,
        HttpClient client, Budget budget, CancellationToken cancellation, string? bearerToken = null)
    {
        _ = Endpoint(request, fixtureOnly: true);
        return ValidateCoreAsync(request, client, budget, cancellation, bearerToken, fixtureOnly: true);
    }

    private static async Task<ModelCardValidationReport> ValidateCoreAsync(ModelCardValidationRequest request,
        HttpClient client, Budget budget, CancellationToken cancellation, string? bearerToken, bool fixtureOnly)
    {
        ValidateInputContract(request);
        var endpoint = Endpoint(request, fixtureOnly);
        if (client.DefaultRequestHeaders.Authorization is not null || client.DefaultRequestHeaders.Host is not null ||
            client.DefaultRequestHeaders.Contains("Proxy-Authorization"))
            throw new InvalidDataException("Model-card validation forbids client-wide credentials or host overrides.");
        ValidateToken(bearerToken);

        var requests = 0; int? status = null; int? warnings = null; int? errors = null;
        string? responseSha = null; var unchanged = false;
        ModelCardValidationReport Report(string outcome) => new(outcome == "VALID" ? "PASS" : "FAIL", outcome,
            request.ExpectedBytes, request.ExpectedSha256, requests, status, warnings, errors, responseSha, unchanged, fixtureOnly);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, budget.Cancellation);
        try
        {
            budget.Check(); cancellation.ThrowIfCancellationRequested();
            deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(.001, Math.Min(request.TimeoutSeconds, budget.RemainingSeconds))));
            var input = await ReadPinnedCardAsync(request, budget, deadline.Token);
            string content;
            try { content = new UTF8Encoding(false, true).GetString(input); }
            catch (DecoderFallbackException) { return Report("INVALID_UTF8"); }
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
            message.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new ModelCardValidationBody(content, "model"),
                ModelCardValidationJsonContext.Default.ModelCardValidationBody));
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (bearerToken is not null) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            budget.Check(); deadline.Token.ThrowIfCancellationRequested();
            requests = 1; // No retry, proxy fallback, redirect or second POST is performed.
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            status = (int)response.StatusCode;
            if (status is >= 300 and <= 399) return Report("REDIRECT_REJECTED");
            if (response.RequestMessage?.RequestUri != endpoint) return Report("RESPONSE_TARGET_REJECTED");
            if (response.Content.Headers.ContentEncoding.Count != 0 ||
                response.Content.Headers.ContentLength is > MaximumResponseBytes)
                return Report("INVALID_RESPONSE");
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not ("application/json" or "application/problem+json")) return Report("INVALID_RESPONSE");
            var bytes = await ReadResponseAsync(response.Content, budget, deadline.Token);
            responseSha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            try { (warnings, errors) = ResponseCounts(bytes, budget); }
            catch (Exception error) when (error is JsonException or InvalidDataException) { return Report("INVALID_RESPONSE"); }

            // Check current source again, including failures: an HTTP status is
            // retained even if the source changed during the request.
            try { _ = await ReadPinnedCardAsync(request, budget, deadline.Token); unchanged = true; }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException) { return Report("INPUT_DRIFT"); }
            budget.Check(); deadline.Token.ThrowIfCancellationRequested();
            if (status == 400) return Report("INVALID");
            if (!response.IsSuccessStatusCode) return Report("HTTP_ERROR");
            return Report(errors == 0 ? "VALID" : "INVALID");
        }
        catch (OperationCanceledException)
        {
            return Report(cancellation.IsCancellationRequested || budget.Cancellation.IsCancellationRequested || !deadline.IsCancellationRequested
                ? "CANCELLED" : "TIMED_OUT");
        }
        catch (TimeoutException) { return Report("TIMED_OUT"); }
        catch (HttpRequestException) { return Report("TRANSPORT_ERROR"); }
        catch (InvalidDataException) { return Report(requests == 0 ? "INPUT_REJECTED" : "INVALID_RESPONSE"); }
        catch (IOException) { return Report(requests == 0 ? "INPUT_REJECTED" : "RESPONSE_IO_ERROR"); }
        catch (UnauthorizedAccessException) { return Report("INPUT_REJECTED"); }
    }

    private static void ValidateInputContract(ModelCardValidationRequest request)
    {
        if (request.SchemaVersion != 1 || request.RepositoryType != "model" || request.TimeoutSeconds is < 1 or > 50 ||
            request.ExpectedBytes is < 1 or > MaximumCardBytes || string.IsNullOrEmpty(request.CardPath) || request.CardPath.Length > 32768 ||
            request.CardPath.StartsWith("\\\\", StringComparison.Ordinal) || request.CardPath.StartsWith("//", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(request.CardPath) || request.ExpectedSha256 is not { Length: 64 } ||
            request.ExpectedSha256.Any(value => !(value is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Model-card input schema, type, bounds or identity differs.");
    }

    private static Uri Endpoint(ModelCardValidationRequest request, bool fixtureOnly)
    {
        if (request.ServiceRoot is null || request.ServiceRoot.Length > 2048 ||
            !Uri.TryCreate(request.ServiceRoot, UriKind.Absolute, out var root) || root.UserInfo.Length != 0 ||
            root.Query.Length != 0 || root.Fragment.Length != 0 || root.AbsolutePath != "/")
            throw new InvalidDataException("Model-card service root differs from the fixed provider or explicit loopback.");
        var literalLoopback = IPAddress.TryParse(root.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
        var provider = root.Scheme == "https" && root.Host == "huggingface.co" && root.Port == 443;
        if (fixtureOnly ? !literalLoopback || root.Scheme is not ("http" or "https") : !provider)
            throw new InvalidDataException("Model-card service root differs from the fixed provider or explicit loopback.");
        return new Uri(root, "api/validate-yaml");
    }

    private static void ValidateToken(string? token)
    {
        if (token is not null && (token.Length is < 1 or > 8192 ||
            token.Any(value => !(char.IsAsciiLetterOrDigit(value) || value is '-' or '.' or '_' or '~' or '+' or '/' or '='))))
            throw new InvalidDataException("Model-card bearer credential format differs.");
    }

    private static async Task<byte[]> ReadPinnedCardAsync(ModelCardValidationRequest request, Budget budget, CancellationToken cancellation)
    {
        budget.Check(); cancellation.ThrowIfCancellationRequested();
        SafePaths.NoLinks(request.CardPath, budget); RegularFiles.Require(request.CardPath, budget);
        await using var stream = new FileStream(request.CardPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != request.ExpectedBytes) throw new InvalidDataException("Model-card source byte identity changed.");
        var bytes = new byte[(int)request.ExpectedBytes];
        await stream.ReadExactlyAsync(bytes, cancellation);
        var extra = new byte[1];
        if (await stream.ReadAsync(extra, cancellation) != 0 ||
            Convert.ToHexStringLower(SHA256.HashData(bytes)) != request.ExpectedSha256)
            throw new InvalidDataException("Model-card source byte/SHA identity changed.");
        SafePaths.NoLinks(request.CardPath, budget); budget.Check();
        return bytes;
    }

    private static async Task<byte[]> ReadResponseAsync(HttpContent content, Budget budget, CancellationToken cancellation)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellation);
        using var result = new MemoryStream(); var buffer = new byte[65536];
        // Even a one-byte stream terminates by byte bound + EOF; wall time and
        // cancellation also apply to every read. No unbounded provider stream.
        for (var index = 0; index <= MaximumResponseBytes; index++)
        {
            budget.Check(); cancellation.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer, cancellation);
            if (read == 0)
            {
                if (result.Length == 0 || content.Headers.ContentLength is { } length && length != result.Length)
                    throw new InvalidDataException("Model-card response length differs.");
                return result.ToArray();
            }
            if (result.Length + read > MaximumResponseBytes) throw new InvalidDataException("Model-card response byte cap exceeded.");
            result.Write(buffer, 0, read);
        }
        throw new InvalidDataException("Model-card response read cap exceeded.");
    }

    private static (int Warnings, int Errors) ResponseCounts(byte[] bytes, Budget budget)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Model-card response object missing.");
        var names = new HashSet<string>(StringComparer.Ordinal); var warnings = 0; var errors = 0;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            budget.Check();
            if (names.Count >= 2 || !names.Add(property.Name) || property.Name is not ("warnings" or "errors") ||
                property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 64)
                throw new InvalidDataException("Model-card response schema differs.");
            foreach (var item in property.Value.EnumerateArray())
            {
                budget.Check();
                if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Model-card diagnostic object missing.");
                var messageFound = false; var fields = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in item.EnumerateObject())
                {
                    budget.Check();
                    if (fields.Count >= 16 || !fields.Add(field.Name)) throw new InvalidDataException("Model-card diagnostic schema differs.");
                    if (field.Name == "message")
                    {
                        if (field.Value.ValueKind != JsonValueKind.String || field.Value.GetString()!.Length > 16384)
                            throw new InvalidDataException("Model-card diagnostic message schema differs.");
                        messageFound = true;
                    }
                }
                if (!messageFound) throw new InvalidDataException("Model-card diagnostic message missing.");
            }
            if (property.Name == "warnings") warnings = property.Value.GetArrayLength(); else errors = property.Value.GetArrayLength();
        }
        return (warnings, errors);
    }
}
