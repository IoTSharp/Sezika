using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sezika.Tooling;

internal static partial class AssetTransport
{
    internal static PublicationPreview Preview(string sourceRoot, PublicationPlan plan, Budget budget)
    {
        budget.Check(); ValidateCommit(plan.SourceCommit); ValidateCommit(plan.ExpectedRef);
        if (plan.SchemaVersion != 1 || plan.Provider is not ("huggingface" or "moark") || plan.Visibility is not ("private" or "public") ||
            plan.Kind is not ("model" or "dataset") || plan.Files.Length is < 1 or > MaximumFiles || plan.MaximumObjectBytes is < 1 or > MaximumFileBytes ||
            plan.Repository.Split('/').Length != 2 || SafePaths.Relative(plan.Repository) != plan.Repository ||
            SafePaths.Relative(plan.Branch).Contains('/') || SafePaths.Relative(plan.RunId).Contains('/'))
            throw new InvalidDataException("Publication target/visibility/path/schema differs.");
        if (plan.Visibility == "public" && plan.Kind != "model") throw new InvalidDataException("Private datasets have no public mirror entry.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long bytes = 0;
        foreach (var file in plan.Files)
        {
            budget.Check(); ValidateAsset(file);
            if (!names.Add(file.Path) || file.Bytes > plan.MaximumObjectBytes) throw new InvalidDataException("Publication duplicate/object limit exceeded.");
            var path = AssetPath(sourceRoot, file.Path, budget);
            if (new FileInfo(path).Length != file.Bytes || FileHash(path, budget) != file.Sha256)
                throw new InvalidDataException("Publication source byte/SHA differs.");
            bytes = checked(bytes + file.Bytes);
            if (bytes > MaximumTotalBytes) throw new InvalidDataException("Publication total exceeded.");
            if (plan.Visibility == "public") ReviewPublicFile(path, file, budget);
        }
        var serialized = JsonSerializer.SerializeToUtf8Bytes(plan, TransportJsonContext.Default.PublicationPlan);
        return new("PREPARING_NOT_PUSHED", plan, plan.Files.Length, bytes, Convert.ToHexStringLower(SHA256.HashData(serialized)), plan.Visibility == "public");
    }

    private static void ReviewPublicFile(string path, AssetFile file, Budget budget)
    {
        var lower = file.Path.ToLowerInvariant();
        if (lower.Split('/').Any(p => p is "raw" or "derived" or "ledger" or ".git" or "cache" or "processes") ||
            lower.Contains("credential", StringComparison.Ordinal) || lower.Contains("identity", StringComparison.Ordinal) ||
            lower.EndsWith("validation.json", StringComparison.Ordinal) || lower.Contains("input-source", StringComparison.Ordinal) ||
            lower.Contains("corpus-provenance", StringComparison.Ordinal))
            throw new InvalidDataException("Private record/provenance/credential path cannot enter the public model.");
        if (lower.EndsWith(".json", StringComparison.Ordinal))
        {
            if (lower == "tokenizer.json" || lower.EndsWith("/tokenizer.json", StringComparison.Ordinal))
            { ReviewPublicTokenizer(path, file.Bytes, budget); return; }
            if (file.Bytes > MaximumJsonBytes) throw new InvalidDataException("Public JSON exceeds content review cap.");
            using var input = File.OpenRead(path); using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 32 });
            ValidateJson(document.RootElement, budget);
            var pending = new Stack<JsonElement>(); pending.Push(document.RootElement);
            for (var index = 0; index < 131072; index++)
            {
                budget.Check(); if (pending.Count == 0) return;
                var current = pending.Pop();
                if (current.ValueKind == JsonValueKind.Object)
                    foreach (var property in current.EnumerateObject())
                    {
                        var field = property.Name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
                        if (field is "recordid" or "candidateids" or "target" or "probability" or "probabilities" or "predictedcandidateid" or
                            "teacherargmaxcandidateid" or "tokenids" or "password" or "token" or "secret" or "privatekey" or "sourceroot")
                            throw new InvalidDataException("Public JSON contains record-level or secret fields.");
                        pending.Push(property.Value);
                    }
                else if (current.ValueKind == JsonValueKind.Array)
                    foreach (var value in current.EnumerateArray()) pending.Push(value);
                else if (current.ValueKind == JsonValueKind.String) ReviewPublicText(current.GetString() ?? "");
            }
            throw new InvalidDataException("Public content node cap exceeded.");
        }
        if (lower.EndsWith(".md", StringComparison.Ordinal) || lower.EndsWith(".txt", StringComparison.Ordinal) || lower.EndsWith(".html", StringComparison.Ordinal))
        {
            if (file.Bytes > MaximumJsonBytes) throw new InvalidDataException("Public text exceeds content review cap.");
            budget.Check(); ReviewPublicText(File.ReadAllText(path));
        }
    }

    private static void ReviewPublicText(string text)
    {
        if (text.Contains("-----BEGIN", StringComparison.Ordinal) && text.Contains("PRIVATE KEY-----", StringComparison.Ordinal) ||
            text.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) || HfSecretPattern().IsMatch(text) ||
            text.Contains("root+vm-", StringComparison.Ordinal) || text.Contains("token_ids", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Public text contains credential or record content.");
    }

    [System.Text.RegularExpressions.GeneratedRegex("hf_[A-Za-z0-9]{20,}", System.Text.RegularExpressions.RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial System.Text.RegularExpressions.Regex HfSecretPattern();

    internal static void ValidateAuthorization(PublicationPreview preview, PublicationAuthorization authorization, Budget budget)
    {
        budget.Check(); ValidateSha(authorization.PlanSha256);
        var age = DateTimeOffset.UtcNow - authorization.ApprovedUtc;
        var actualPlanSha = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(preview.Plan, TransportJsonContext.Default.PublicationPlan)));
        if (preview.Status != "PREPARING_NOT_PUSHED" || preview.PlanSha256 != actualPlanSha || preview.PlanSha256 != authorization.PlanSha256 ||
            preview.Plan.Repository != authorization.Repository || preview.Plan.ExpectedRef != authorization.ExpectedRef ||
            preview.Plan.Visibility != authorization.Visibility || string.IsNullOrWhiteSpace(authorization.ApprovedBy) || authorization.ApprovedBy.Length > 256 ||
            age < TimeSpan.Zero || age > TimeSpan.FromHours(24) || preview.Plan.Visibility == "public" && !preview.PublicContentReviewed)
            throw new InvalidDataException("Explicit publication authorization does not match the frozen payload/target/ref/visibility.");
    }

    private static Uri Service(PublicationPlan plan, Uri root)
    {
        _ = ValidateUri(root.AbsoluteUri, allowQuery: false);
        if (root.AbsolutePath != "/" || root.Host != (plan.Provider == "huggingface" ? "huggingface.co" : "moark.com") && !root.IsLoopback)
            throw new InvalidDataException("Unexpected publication service root.");
        return root;
    }

    private static Uri BatchEndpoint(PublicationPlan plan, Uri root)
    {
        Service(plan, root);
        var prefix = plan.Kind == "dataset" && plan.Provider == "huggingface" ? "datasets/" : "";
        return new(root, prefix + plan.Repository + ".git/info/lfs/objects/batch");
    }

    private static void ActionScope(Uri endpoint, Uri action, string provider)
    {
        _ = ValidateUri(action.AbsoluteUri, allowQuery: true);
        if (endpoint.IsLoopback)
        {
            if (action.Authority != endpoint.Authority) throw new InvalidDataException("Fixture action escaped loopback endpoint.");
            return;
        }
        var host = action.Host;
        var allowed = host == endpoint.Host || provider == "huggingface" && (host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".hf.co", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase)) ||
            provider == "moark" && (host.EndsWith(".bcebos.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".baidubce.com", StringComparison.OrdinalIgnoreCase));
        if (!allowed) throw new InvalidDataException("Server upload instruction escaped approved storage hosts.");
    }

    private static async Task<JsonDocument> SendApiAsync(HttpClient http, HttpRequestMessage request, Budget budget, CancellationToken cancellation)
    {
        budget.Check();
        using var deadline = HttpDeadline(budget, cancellation, 30);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidDataException("Platform protocol returned HTTP " + (int)response.StatusCode + "; reconcile remote state before writes.");
            if (response.Content.Headers.ContentLength > MaximumJsonBytes) throw new InvalidDataException("Platform JSON response exceeds byte cap.");
            using var content = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var memory = new MemoryStream(); var buffer = new byte[65536];
            for (var readIndex = 0; readIndex <= MaximumJsonBytes; readIndex++)
            {
                budget.Check(); var read = await content.ReadAsync(buffer, deadline.Token);
                if (read == 0)
                {
                    if (memory.Length == 0) return JsonDocument.Parse("{}");
                    var document = JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
                    try { ValidateJson(document.RootElement, budget); return document; } catch { document.Dispose(); throw; }
                }
                if (memory.Length + read > MaximumJsonBytes) throw new InvalidDataException("Platform response exceeded byte cap.");
                memory.Write(buffer, 0, read);
            }
            throw new InvalidDataException("Platform response read cap exceeded.");
        }
        catch (HttpRequestException) { throw new IOException("Platform request failed; preserve remote state and inspect before retry."); }
    }

    private static HttpRequestMessage JsonRequest<T>(Uri endpoint, T body, JsonTypeInfo<T> type, string? token, AuthenticationHeaderValue? authentication = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, type));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.git-lfs+json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.git-lfs+json"));
        Authenticate(request, token, authentication);
        return request;
    }

    private static string Compact<T>(T value, JsonTypeInfo<T> type)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory, new JsonWriterOptions { Indented = false })) JsonSerializer.Serialize(writer, value, type);
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static async Task<LfsResponseObject> BatchAsync(PublicationPlan plan, AssetFile file, Uri root, HttpClient client, string? token, Budget budget, CancellationToken cancellation,
        AuthenticationHeaderValue? authentication = null)
    {
        var body = new LfsBatchRequest("upload", ["basic", "multipart"], [new(file.Sha256, file.Bytes)], new(plan.Branch));
        if (client.DefaultRequestHeaders.Authorization is not null) throw new InvalidDataException("Client-wide authorization can leak to signed storage URLs; use scoped request authentication.");
        using var request = JsonRequest(BatchEndpoint(plan, root), body, TransportJsonContext.Default.LfsBatchRequest, token, authentication);
        using var document = await SendApiAsync(client, request, budget, cancellation);
        var response = document.RootElement.Deserialize(TransportJsonContext.Default.LfsBatchResponse) ?? throw new InvalidDataException("LFS batch omitted objects.");
        if (response.Objects is null || response.Objects.Length != 1 || response.Objects[0].Oid != file.Sha256 || response.Objects[0].Size != file.Bytes || response.Objects[0].Error is not null)
            throw new InvalidDataException("LFS server object identity/error differs.");
        return response.Objects[0];
    }

    internal static async Task<ObjectUploadReport> UploadLfsObjectAsync(PublicationPreview preview, PublicationAuthorization authorization,
        string sourceRoot, AssetFile file, Uri serviceRoot, HttpClient client, string? bearerToken, Budget budget, CancellationToken cancellation,
        string? resumeCandidate = null, string? checkpointRoot = null, AuthenticationHeaderValue? authentication = null)
    {
        ValidateAuthentication(bearerToken, authentication);
        try { return await UploadLfsObjectCoreAsync(preview, authorization, sourceRoot, file, serviceRoot, client, bearerToken, budget, cancellation, resumeCandidate, checkpointRoot, authentication); }
        catch (HttpRequestException) { throw new IOException("Upload request failed; preserve remote state and inspect before retry."); }
    }

    private static async Task<ObjectUploadReport> UploadLfsObjectCoreAsync(PublicationPreview preview, PublicationAuthorization authorization,
        string sourceRoot, AssetFile file, Uri serviceRoot, HttpClient client, string? bearerToken, Budget budget, CancellationToken cancellation,
        string? resumeCandidate, string? checkpointRoot, AuthenticationHeaderValue? authentication)
    {
        ValidateAuthorization(preview, authorization, budget);
        if (!preview.Plan.Files.Contains(file)) throw new InvalidDataException("Upload file is outside the authorized payload.");
        var refreshed = Preview(sourceRoot, preview.Plan, budget);
        if (refreshed.PlanSha256 != preview.PlanSha256) throw new InvalidDataException("Publication payload changed.");
        var endpoint = BatchEndpoint(preview.Plan, serviceRoot);
        var instruction = await BatchAsync(preview.Plan, file, serviceRoot, client, bearerToken, budget, cancellation, authentication);
        var parts = 0; var priorParts = 0; var replacedParts = 0; string? checkpoint = null;
        if (instruction.Actions?.Upload is { } upload)
        {
            var href = ValidateUri(upload.Href, allowQuery: true); ActionScope(endpoint, href, preview.Plan.Provider);
            if (upload.Header is not null && upload.Header.TryGetValue("chunk_size", out var chunkText))
            {
                if (!long.TryParse(chunkText, System.Globalization.CultureInfo.InvariantCulture, out var chunk) || chunk is < 1 or > 64 * 1024 * 1024 ||
                    (file.Bytes + chunk - 1) / chunk is < 1 or > 128) throw new InvalidDataException("Multipart chunk/count cap exceeded.");
                var count = (file.Bytes + chunk - 1) / chunk;
                if (upload.Header.Count != count + 1 || upload.Header.Keys.Any(k => k != "chunk_size" && (!int.TryParse(k, out var n) || n < 1 || n > count)))
                    throw new InvalidDataException("Multipart instruction contains missing or extra part numbers.");
                (parts, priorParts, replacedParts, checkpoint) = await UploadMultipartAsync(preview.Plan, sourceRoot, file, endpoint,
                    upload, chunk, client, budget, cancellation, resumeCandidate, checkpointRoot);
            }
            else
            {
                using var input = File.OpenRead(AssetPath(sourceRoot, file.Path, budget));
                using var request = new HttpRequestMessage(HttpMethod.Put, href) { Content = new StreamContent(input) };
                request.Content.Headers.ContentLength = file.Bytes;
                if (upload.Header is not null)
                    foreach (var header in upload.Header)
                    {
                        budget.Check();
                        if (upload.Header.Count > 64 || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                            header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                            header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && href.Authority != endpoint.Authority)
                            throw new InvalidDataException("Unsafe upload instruction headers.");
                        if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value) && !request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
                            throw new InvalidDataException("Invalid upload instruction header.");
                    }
                using var deadline = HttpDeadline(budget, cancellation, 1800);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode) throw new InvalidDataException("LFS PUT failed; inspect remote state before retry.");
                parts = 1;
            }
            if (instruction.Actions.Verify is { } verify)
            {
                var uri = ValidateUri(verify.Href, true); ActionScope(endpoint, uri, preview.Plan.Provider);
                using var request = JsonRequest(uri, new LfsObject(file.Sha256, file.Bytes), TransportJsonContext.Default.LfsObject,
                    uri.Authority == endpoint.Authority ? bearerToken : null, uri.Authority == endpoint.Authority ? authentication : null);
                using var verified = await SendApiAsync(client, request, budget, cancellation);
            }
        }
        if (FileHash(AssetPath(sourceRoot, file.Path, budget), budget) != file.Sha256) throw new InvalidDataException("Upload source SHA changed.");
        var readBack = await BatchAsync(preview.Plan, file, serviceRoot, client, bearerToken, budget, cancellation, authentication);
        if (readBack.Actions?.Upload is not null) throw new InvalidDataException("Uploaded object is not independently server-present.");
        if (checkpoint is not null)
        {
            var prior = ReadJson(checkpoint, TransportJsonContext.Default.MultipartResumeState, budget);
            checkpoint = Path.Combine(Path.GetDirectoryName(checkpoint)!, "object-server-verified.json");
            WriteJson(checkpoint, prior with { Status = "PASS_preuploaded_object_server_verified_not_committed" }, TransportJsonContext.Default.MultipartResumeState, budget);
        }
        return new("PASS_preuploaded_object_server_verified_not_committed", file.Sha256, file.Bytes, parts, true,
            PriorCandidateParts: priorParts, ReplacedPriorParts: replacedParts, ResumeCheckpoint: checkpoint);
    }

    internal static async Task<HubCommitReceipt> CommitHuggingFaceAsync(PublicationPreview preview, PublicationAuthorization authorization,
        string sourceRoot, Uri serviceRoot, HttpClient client, string? bearerToken, Budget budget, CancellationToken cancellation, AuthenticationHeaderValue? authentication = null)
    {
        ValidateAuthentication(bearerToken, authentication);
        ValidateAuthorization(preview, authorization, budget); Service(preview.Plan, serviceRoot);
        if (preview.Plan.Provider != "huggingface") throw new InvalidDataException("MoArk ref publication uses the ordinary Git/LFS adapter.");
        _ = Preview(sourceRoot, preview.Plan, budget);
        foreach (var file in preview.Plan.Files)
        {
            budget.Check();
            var verified = await BatchAsync(preview.Plan, file, serviceRoot, client, bearerToken, budget, cancellation, authentication);
            if (verified.Actions?.Upload is not null) throw new InvalidDataException("Commit requires every LFS object already present.");
        }
        var apiKind = preview.Plan.Kind == "model" ? "models" : "datasets";
        var infoUri = new Uri(serviceRoot, "api/" + apiKind + "/" + preview.Plan.Repository + "/revision/" + preview.Plan.Branch);
        using (var info = new HttpRequestMessage(HttpMethod.Get, infoUri))
        {
            Authenticate(info, bearerToken, authentication);
            using var metadata = await SendApiAsync(client, info, budget, cancellation);
            if (metadata.RootElement.GetProperty("sha").GetString() != preview.Plan.ExpectedRef ||
                metadata.RootElement.GetProperty("private").GetBoolean() != (preview.Plan.Visibility == "private"))
                throw new InvalidDataException("HF target ref/visibility changed before commit.");
        }
        var lines = new StringBuilder();
        lines.Append("{\"key\":\"header\",\"value\":").Append(Compact(new HubCommitHeader("Append reviewed Sezika run " + preview.Plan.RunId,
            "Frozen byte/SHA payload; formal quality remains unaccepted.", preview.Plan.ExpectedRef), TransportJsonContext.Default.HubCommitHeader)).Append("}\n");
        foreach (var file in preview.Plan.Files)
        {
            budget.Check(); lines.Append("{\"key\":\"lfsFile\",\"value\":").Append(Compact(new HubCommitLfs(file.Path, "sha256", file.Sha256, file.Bytes),
                TransportJsonContext.Default.HubCommitLfs)).Append("}\n");
        }
        if (Encoding.UTF8.GetByteCount(lines.ToString()) > MaximumJsonBytes) throw new InvalidDataException("HF commit body exceeded cap.");
        var uri = new Uri(serviceRoot, "api/" + apiKind + "/" + preview.Plan.Repository + "/commit/" + preview.Plan.Branch);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(lines.ToString(), Encoding.UTF8, "application/x-ndjson") };
        Authenticate(request, bearerToken, authentication);
        using var result = await SendApiAsync(client, request, budget, cancellation);
        var commit = result.RootElement.GetProperty("commitOid").GetString() ?? throw new InvalidDataException("HF omitted commit identity."); ValidateCommit(commit);
        return new("PUSHED_READBACK_PENDING", preview.Plan.Repository, commit, preview.Plan.Visibility);
    }
}
