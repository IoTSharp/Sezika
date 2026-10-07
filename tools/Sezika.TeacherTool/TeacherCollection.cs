using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sezika;

namespace Sezika.TeacherTool;

/// <summary>Offline, bounded collection of unreviewed hard labels from a local Tomur teacher.</summary>
public static class TeacherCollection
{
    private const int MaximumRecords = 32;
    private const int MaximumResponseBytes = 131_072;
    private const int MaximumInputBytes = 65_536;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static Task<int> RunAsync(string[] args) => RunAsync(args, CancellationToken.None);

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var options = Options.Parse(args);
            cancellation.CancelAfter(TimeSpan.FromSeconds(options.TotalTimeoutSeconds));
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                Credentials = null, PreAuthenticate = false,
                ConnectTimeout = TimeSpan.FromSeconds(Math.Min(10, options.RequestTimeoutSeconds)),
                MaxConnectionsPerServer = 1, MaxResponseHeadersLength = 16,
                AutomaticDecompression = DecompressionMethods.None
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            return await CollectAsync(options, client, cancellation.Token);
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("teacher_collection_cancelled_before_success_publication"); return 124; }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or JsonException or InvalidOperationException or DecisionException or KeyNotFoundException or UnauthorizedAccessException or FormatException or OverflowException)
        { Console.Error.WriteLine($"teacher_collection_failed: {error.Message}"); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }

    /// <summary>In-memory synthetic HTTP contract checks; never starts a server or calls an actual teacher.</summary>
    public static async Task<int> SelfTestAsync(string[] args)
    {
        var maximumCases = 10;
        if (args.Length == 3 && args[0] == "self-test-collection" && args[1] == "--max-cases" &&
            int.TryParse(args[2], out var parsed) && parsed is >= 1 and <= 10) maximumCases = parsed;
        else if (args.Length != 1 || args[0] != "self-test-collection") throw new ArgumentException("self-test-collection [--max-cases 1..10]");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"sezika-teacher-collection-fixture-{Guid.NewGuid():N}"));
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Fixture collision.");
        Directory.CreateDirectory(root);
        var fixtures = new OutputJournal(root);
        var published = new List<OutputJournal>();
        var checks = 0;
        const string revision = "0123456789abcdef0123456789abcdef01234567";
        var modelHash = new string('a', 64);
        var source = $"https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/{revision}/Qwen3.5-9B-Q4_K_M.gguf";
        const string requestJson = """{"schema_version":1,"model_id":"sezika/teacher-fixture","state":{"text":"fixture"},"questions":[{"id":"q","type":"choice","instruction":"fixture only","candidates":[{"id":"a","text":"first"},{"id":"b","text":"second"}]}]}""";
        const string successJson = """{"object":"chat.completion","model":"qwen35-9b-q4km","choices":[{"index":0,"message":{"role":"assistant","content":"{\"question_id\":\"q\",\"status\":\"answered\",\"selected_candidate_id\":\"a\"}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";
        try
        {
            var request = fixtures.Write("request.json", Encoding.UTF8.GetBytes(requestJson));
            var prompt = fixtures.Write("prompt.txt", Encoding.UTF8.GetBytes("Synthetic contract fixture, never license or quality evidence.\n" + requestJson));
            var terms = fixtures.Write("terms.json", Encoding.UTF8.GetBytes($$$"""{"schema_version":1,"model":"qwen35-9b-q4km","revision":"{{{revision}}}","model_sha256":"{{{modelHash}}}","source_url":"{{{source}}}","license":"synthetic-test-only","collection_permitted":true,"use_scope":"offline_teacher_collection","output_training_permitted":false,"output_redistribution_permitted":false,"evidence_text":"SYNTHETIC: no license approval or actual model","reviewed_by":"synthetic-fixture","reviewed_at_utc":"2026-10-08T00:00:00Z"}"""));
            var modelEvidence = fixtures.Write("model.json", Encoding.UTF8.GetBytes($$$"""{"schema_version":1,"model":"qwen35-9b-q4km","revision":"{{{revision}}}","model_sha256":"{{{modelHash}}}","source_url":"{{{source}}}","license":"synthetic-test-only","validation_status":"hash_verified","model_bytes":1,"verification_method":"SYNTHETIC: no model downloaded/hashed","verified_at_utc":"2026-10-08T00:00:00Z"}"""));
            fixtures.Write("catalog.json", Encoding.UTF8.GetBytes($$$"""{"schema_version":1,"candidate":{"id":"qwen35-9b-q4km","service":"IoTSharp/Tomur","model":"qwen35-9b-q4km","revision":"{{{revision}}}","model_sha256":"{{{modelHash}}}","source_url":"{{{source}}}","license":"synthetic-test-only","status":"verified","endpoint":"http://127.0.0.1:5137/v1/chat/completions","terms_evidence":"terms.json","terms_evidence_sha256":"{{{terms.Sha256}}}","model_evidence":"model.json","model_evidence_sha256":"{{{modelEvidence.Sha256}}}"}}"""));
            var candidates = fixtures.Write("candidates.jsonl", Encoding.UTF8.GetBytes($$$"""{"record_id":"fixture-1","language":"en","type":"choice","family_id":"fixture-family","source":"synthetic","origin":"AI drafted","purpose":"pilot","split":"audit","review_status":"pending","training_admission":"pending","teacher_status":"not_called","request_path":"request.json","request_sha256":"{{{request.Sha256}}}","prompt_path":"prompt.txt","prompt_sha256":"{{{prompt.Sha256}}}"}""" + "\n"));
            var manifestText = $$$"""{"schema_version":1,"protocol":"decision-v1","student_model_id":"sezika/teacher-fixture","origin":"AI drafted","purpose":"pilot","split":"audit","human_review":"pending","training_admission":"pending","quality_evidence":false,"teacher_called":false,"total_records":1,"candidates":{"path":"candidates.jsonl","sha256":"{{{candidates.Sha256}}}","bytes":{{{candidates.Bytes}}}},"assets":[{"path":"request.json","sha256":"{{{request.Sha256}}}","bytes":{{{request.Bytes}}}},{"path":"prompt.txt","sha256":"{{{prompt.Sha256}}}","bytes":{{{prompt.Bytes}}}}]}""";
            fixtures.Write("manifest.json", Encoding.UTF8.GetBytes(manifestText));
            var secondRequest = fixtures.Write("request-2.json", Encoding.UTF8.GetBytes(requestJson));
            var secondPrompt = fixtures.Write("prompt-2.txt", Encoding.UTF8.GetBytes("Synthetic contract fixture.\n" + requestJson));
            var secondLine = $$$"""{"record_id":"fixture-2","language":"en","type":"choice","family_id":"fixture-family-2","source":"synthetic","origin":"AI drafted","purpose":"pilot","split":"audit","review_status":"pending","training_admission":"pending","teacher_status":"not_called","request_path":"request-2.json","request_sha256":"{{{secondRequest.Sha256}}}","prompt_path":"prompt-2.txt","prompt_sha256":"{{{secondPrompt.Sha256}}}"}""" + "\n";
            var cancelCandidates = fixtures.Write("cancel-candidates.jsonl", Encoding.UTF8.GetBytes(StrictUtf8.GetString(ReadSnapshot(Path.Combine(root, "candidates.jsonl"), 65_536, token)) + secondLine));
            var addedAssets = $$$""",{"path":"request-2.json","sha256":"{{{secondRequest.Sha256}}}","bytes":{{{secondRequest.Bytes}}}},{"path":"prompt-2.txt","sha256":"{{{secondPrompt.Sha256}}}","bytes":{{{secondPrompt.Bytes}}}}]}""";
            var cancelManifest = manifestText.Replace("\"total_records\":1", "\"total_records\":2", StringComparison.Ordinal)
                .Replace("candidates.jsonl", "cancel-candidates.jsonl", StringComparison.Ordinal).Replace(candidates.Sha256, cancelCandidates.Sha256, StringComparison.Ordinal)
                .Replace($"\"bytes\":{candidates.Bytes}", $"\"bytes\":{cancelCandidates.Bytes}", StringComparison.Ordinal).Replace("]}", addedAssets, StringComparison.Ordinal);
            fixtures.Write("cancel-manifest.json", Encoding.UTF8.GetBytes(cancelManifest));
            fixtures.Write("invalid-manifest.json", Encoding.UTF8.GetBytes(manifestText.Replace(candidates.Sha256, new string('f', 64), StringComparison.Ordinal)));
            // The caller must run --max-cases 1 first, then this bounded full set. Each case contains only one request.
            for (var index = 0; index < maximumCases && index < 10; index++)
            {
                token.ThrowIfCancellationRequested();
                var output = Path.Combine(root, $"case-{index:D2}");
                using var caseCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                var caseIndex = index; var calls = 0;
                if (index == 8) fixtures.Write($"case-{index:D2}/foreign.txt", Encoding.UTF8.GetBytes("do not overwrite"));
                using var handler = new SyntheticHandler((message, requestToken) =>
                {
                    calls++;
                    var uri = message.RequestUri;
                    if (uri is null || uri.Host != "127.0.0.1" || uri.AbsolutePath != "/v1/chat/completions" || message.Method != HttpMethod.Post)
                        throw new InvalidOperationException("Synthetic fixture URI/request contract failed.");
                    if (caseIndex == 4) throw new OperationCanceledException(requestToken);
                    if (caseIndex == 6) throw new HttpRequestException("Synthetic transport failure.");
                    var content = caseIndex switch
                    {
                        1 => Encoding.UTF8.GetBytes(successJson.Replace("\\\"status\\\":\\\"answered\\\"", "\\\"status\\\":\\\"answered\\\",\\\"status\\\":\\\"answered\\\"", StringComparison.Ordinal)),
                        2 => Encoding.UTF8.GetBytes(successJson.Replace("\\\"status\\\":\\\"answered\\\",\\\"selected_candidate_id\\\":\\\"a\\\"", "\\\"status\\\":\\\"abstained\\\",\\\"selected_candidate_id\\\":null", StringComparison.Ordinal)),
                        7 => new byte[MaximumResponseBytes + 1],
                        _ => Encoding.UTF8.GetBytes(successJson)
                    };
                    return new HttpResponseMessage(caseIndex == 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
                });
                using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                var options = new Options(Path.Combine(root, "catalog.json"), Path.Combine(root, index == 9 ? "invalid-manifest.json" : index == 5 ? "cancel-manifest.json" : "manifest.json"), output, index == 5 ? 2 : 1, 1, 10, 16);
                string? failureAudit = null;
                try
                {
                    var exit = await CollectAsync(options, client, caseCancellation.Token, true, journal => published.Add(journal),
                        item => { if (caseIndex == 5 && item == 0) caseCancellation.Cancel(); },
                        (path, journal) => { failureAudit = path; published.Add(journal); });
                    if (index is 8 or 9) throw new InvalidOperationException("Overwrite/hash mismatch fixture unexpectedly published.");
                    using var report = StrictJson(ReadSnapshot(Path.Combine(output, "manifest.json"), 65_536, token), token);
                    var result = report.RootElement;
                    var expectedPending = index == 0 ? 1 : 0; var expectedAbstained = index == 2 ? 1 : 0;
                    var expectedFailed = expectedPending + expectedAbstained == 0 ? 1 : 0;
                    if (calls != 1 || result.GetProperty("total").GetInt32() != 1 || result.GetProperty("pending").GetInt32() != expectedPending ||
                        result.GetProperty("failed").GetInt32() != expectedFailed || result.GetProperty("abstained").GetInt32() != expectedAbstained ||
                        result.GetProperty("training_admission").GetBoolean() || !result.GetProperty("synthetic_transport").GetBoolean() ||
                        (index == 5 ? exit != 124 : expectedFailed == 1 ? exit != 3 : exit != 0)) throw new InvalidOperationException("Synthetic fixture denominator/status contract failed.");
                }
                catch (OperationCanceledException) when (index == 5)
                {
                    if (calls != 1 || Directory.Exists(output) || failureAudit is null || File.Exists(Path.Combine(failureAudit, "manifest.json")))
                        throw new InvalidOperationException("Cancelled collection published success output or lost failure audit.");
                    using var audit = StrictJson(ReadSnapshot(Path.Combine(failureAudit, "audit.json"), 65_536, token), token);
                    var auditRoot = audit.RootElement;
                    if (Text(auditRoot, "status", 32) != "cancelled" || auditRoot.GetProperty("total").GetInt32() != 2 ||
                        auditRoot.GetProperty("attempted").GetInt32() != 1 || auditRoot.GetProperty("not_called").GetInt32() != 1 ||
                        auditRoot.GetProperty("pending").GetInt32() != 1 || auditRoot.GetProperty("failed").GetInt32() != 1 ||
                        auditRoot.GetProperty("training_admission").GetBoolean() || auditRoot.GetProperty("quality_evidence").GetBoolean())
                        throw new InvalidOperationException("Cancelled audit does not retain the complete two-candidate denominator.");
                    var auditRows = StrictUtf8.GetString(ReadSnapshot(Path.Combine(failureAudit, "audit-records.jsonl"), 65_536, token)).TrimEnd('\n').Split('\n');
                    if (auditRows.Length != 2) throw new InvalidOperationException("Cancelled audit lost a candidate row.");
                    using var first = StrictJson(Encoding.UTF8.GetBytes(auditRows[0]), token);
                    using var second = StrictJson(Encoding.UTF8.GetBytes(auditRows[1]), token);
                    var firstRoot = first.RootElement; var rawResponse = firstRoot.GetProperty("response");
                    var retainedResponse = ReadSnapshot(ContainedPath(failureAudit, Text(rawResponse, "path", 1024)), MaximumResponseBytes, token);
                    if (!firstRoot.GetProperty("http_attempted").GetBoolean() || firstRoot.GetProperty("usage").GetProperty("total_tokens").GetInt32() != 2 ||
                        !SameHash(Hash(retainedResponse), HashText(firstRoot, "response_sha256")) || second.RootElement.GetProperty("http_attempted").GetBoolean() ||
                        Text(second.RootElement, "teacher_status", 32) != "not_called")
                        throw new InvalidOperationException("Cancelled audit lost raw response/hash/usage or not-called semantics.");
                }
                catch (Exception error) when ((index is 8 or 9) && (error is IOException or InvalidDataException))
                {
                    if (calls != 0 || index == 8 && File.ReadAllText(Path.Combine(output, "foreign.txt")) != "do not overwrite" ||
                        index == 9 && Directory.Exists(output)) throw new InvalidOperationException("Preflight failure or overwrite fixture modified output/called HTTP.");
                }
                checks++;
                Console.WriteLine($"teacher_collection_synthetic_check {index + 1}/{maximumCases} passed actual_teacher_called=false");
            }
            Console.WriteLine($"teacher_collection_synthetic_checks_passed={checks} actual_teacher_called=false");
            return 0;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            for (var index = published.Count - 1; index >= 0 && index < 10; index--) published[index].Cleanup(cleanup.Token);
            fixtures.Cleanup(cleanup.Token);
        }
    }

    private sealed class SyntheticHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request, cancellationToken));
    }

    private static async Task<int> CollectAsync(Options options, HttpClient client, CancellationToken token,
        bool syntheticTransport = false, Action<OutputJournal>? onPublished = null,
        Action<int>? onItemRecorded = null, Action<string, OutputJournal>? onFailurePublished = null)
    {
        var watch = Stopwatch.StartNew();
        var preflight = Preflight(options, token, syntheticTransport);
        token.ThrowIfCancellationRequested();
        var output = Path.GetFullPath(options.Output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists; collection never overwrites it.");
        var parent = Path.GetDirectoryName(output) ?? throw new ArgumentException("Output must have a parent directory.");
        if (!Directory.Exists(parent)) throw new IOException("Output parent must already exist.");
        var staging = Path.Combine(parent, $".teacher-collection-{Guid.NewGuid():N}");
        if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException("Staging collision.");
        Directory.CreateDirectory(staging);
        var journal = new OutputJournal(staging);
        var published = false;
        var retainedForAudit = false;
        var records = new List<TeacherCollectedRecord>(preflight.Candidates.Length);
        var attempted = new bool[preflight.Candidates.Length];
        var pending = 0; var failed = 0; var abstained = 0; var attempts = 0; var cancelled = false;
        try
        {
            var evidence = new[]
            {
                journal.Write("catalog.json", preflight.CatalogBytes),
                journal.Write("source-manifest.json", preflight.ManifestBytes),
                journal.Write("terms-evidence.json", preflight.TermsBytes),
                journal.Write("model-evidence.json", preflight.ModelEvidenceBytes)
            };
            // Reviewed bounds: <=32 candidates, one total deadline, one <=120s request deadline, no retries.
            for (var index = 0; index < preflight.Candidates.Length && index < options.MaxRecords && index < MaximumRecords; index++)
            {
                var candidate = preflight.Candidates[index];
                var stem = $"items/{index:D2}";
                var requestAsset = journal.Write(stem + ".decision.json", candidate.RequestBytes);
                var promptAsset = journal.Write(stem + ".prompt.txt", candidate.PromptBytes);
                var chat = new TeacherChatRequest(preflight.Identity.Model,
                    [new TeacherChatMessage("user", StrictUtf8.GetString(candidate.PromptBytes))], false, 0, options.MaxOutputTokens);
                var requestBytes = JsonSerializer.SerializeToUtf8Bytes(chat, TeacherCollectionJsonContext.Default.TeacherChatRequest);
                var httpAsset = journal.Write(stem + ".http-request.json", requestBytes);
                var itemWatch = Stopwatch.StartNew();
                TeacherAsset? responseAsset = null; TeacherUsage? usage = null;
                int? statusCode = null; int? selectedIndex = null; string? selectedId = null;
                string? failureCode = null; string? finishReason = null; var status = "failed";
                if (token.IsCancellationRequested) { cancelled = true; failureCode = "collection_cancelled_before_request"; }
                else
                {
                    using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    requestDeadline.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
                    var requestToken = requestDeadline.Token;
                    attempts++;
                    attempted[index] = true;
                    try
                    {
                        using var message = new HttpRequestMessage(HttpMethod.Post, preflight.Identity.Endpoint);
                        message.Headers.ConnectionClose = true;
                        message.Content = new ByteArrayContent(requestBytes);
                        message.Content.Headers.ContentType = new("application/json");
                        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, requestToken);
                        statusCode = (int)response.StatusCode;
                        var bytes = await ReadResponseAsync(response, requestToken);
                        responseAsset = journal.Write(stem + ".http-response.bin", bytes);
                        if (!response.IsSuccessStatusCode) failureCode = "http_status_error";
                        else
                        {
                            var parsed = ParseResponse(bytes, preflight.Identity.Model, candidate, options.MaxOutputTokens, token, out usage, out finishReason);
                            selectedIndex = parsed.Index; selectedId = parsed.Id;
                            status = parsed.Abstained ? "abstained" : "pending";
                        }
                    }
                    catch (ResponseLimitException error)
                    {
                        responseAsset = journal.Write(stem + ".http-response-prefix.bin", error.Prefix);
                        failureCode = "response_byte_limit_exceeded";
                    }
                    catch (OperationCanceledException)
                    { cancelled |= token.IsCancellationRequested; failureCode = token.IsCancellationRequested ? "collection_cancelled" : "request_timeout"; }
                    catch (HttpRequestException) { failureCode = "http_transport_error"; }
                    catch (Exception error) when (error is JsonException or InvalidDataException or DecoderFallbackException or OverflowException or FormatException or InvalidOperationException or KeyNotFoundException)
                    { failureCode = "invalid_teacher_response"; }
                }
                if (status == "pending") pending++; else if (status == "abstained") abstained++; else failed++;
                records.Add(new(candidate.RecordId, candidate.Language, candidate.Type, candidate.FamilyId,
                    candidate.Source, "AI drafted", "pilot", "audit", status, status == "pending" ? "answered" : status,
                    false, preflight.Identity.Model, preflight.Identity.Revision, preflight.Identity.ModelSha256,
                    candidate.QuestionId, candidate.CandidateIds, selectedIndex, selectedId,
                    candidate.RequestSha256, candidate.PromptSha256, httpAsset.Sha256, responseAsset?.Sha256,
                    requestAsset, promptAsset, httpAsset, responseAsset, usage, statusCode, attempted[index], finishReason, failureCode,
                    itemWatch.Elapsed.TotalMilliseconds, true));
                records[^1] = records[^1] with { HttpAttempted = attempted[index], TeacherStatus = !attempted[index] ? "not_called" : records[^1].TeacherStatus };
                onItemRecorded?.Invoke(index);
                Console.WriteLine($"teacher_collection_progress item={index + 1}/{preflight.Candidates.Length} status={status} http_attempts={attempts} elapsed_seconds={watch.Elapsed.TotalSeconds:F3}");
            }
            if (records.Count != preflight.Candidates.Length || pending + failed + abstained != records.Count || attempts > MaximumRecords)
                throw new InvalidDataException("Collection denominator or request bound failed.");
            token.ThrowIfCancellationRequested();
            var lines = new StringBuilder();
            for (var index = 0; index < records.Count && index < MaximumRecords; index++)
                lines.Append(JsonSerializer.Serialize(records[index], TeacherCollectionJsonContext.Default.TeacherCollectedRecord)).Append('\n');
            var recordsAsset = journal.Write("records.jsonl", Encoding.UTF8.GetBytes(lines.ToString()));
            var report = new TeacherCollectionReport(1, "complete_pending_human_review", false,
                preflight.SourceManifestSha256, preflight.CandidatesSha256, preflight.Identity, records.Count,
                pending, failed, abstained, attempts, options.MaxRecords, MaximumRecords,
                options.RequestTimeoutSeconds, options.TotalTimeoutSeconds, options.MaxOutputTokens, MaximumResponseBytes,
                watch.Elapsed.TotalSeconds, cancelled, false, syntheticTransport, recordsAsset, evidence);
            // A completed report includes every failed/abstained row and is never training eligible.
            journal.Write("manifest.json", JsonSerializer.SerializeToUtf8Bytes(report, TeacherCollectionJsonContext.Default.TeacherCollectionReport));
            token.ThrowIfCancellationRequested();
            journal.Publish(output);
            token.ThrowIfCancellationRequested();
            published = true;
            onPublished?.Invoke(journal);
            Console.WriteLine($"teacher_collection_complete total={records.Count} pending={pending} failed={failed} abstained={abstained} training_admission=false output={output}");
            return cancelled ? 124 : failed > 0 ? 3 : 0;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            using var auditDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var auditToken = auditDeadline.Token;
            var auditPath = Path.Combine(parent, $"failure-audit-{Path.GetFileName(output)}-{Guid.NewGuid():N}");
            try
            {
                journal.RemoveOwned("manifest.json", auditToken);
                // If cancellation arrived at the atomic publication boundary, move the owned directory away from the success name first.
                if (journal.Root == output) { journal.Publish(auditPath); retainedForAudit = true; }
                // Work has stopped. Preserve prior evidence and explicitly account for every uncalled/unfinished candidate.
                for (var index = records.Count; index < preflight.Candidates.Length && index < MaximumRecords; index++)
                {
                    auditToken.ThrowIfCancellationRequested();
                    var candidate = preflight.Candidates[index]; var stem = $"items/{index:D2}";
                    var request = journal.Find(stem + ".decision.json") ?? journal.Write(stem + ".decision.json", candidate.RequestBytes);
                    var prompt = journal.Find(stem + ".prompt.txt") ?? journal.Write(stem + ".prompt.txt", candidate.PromptBytes);
                    var chat = new TeacherChatRequest(preflight.Identity.Model, [new("user", StrictUtf8.GetString(candidate.PromptBytes))], false, 0, options.MaxOutputTokens);
                    var httpRequest = journal.Find(stem + ".http-request.json") ?? journal.Write(stem + ".http-request.json", JsonSerializer.SerializeToUtf8Bytes(chat, TeacherCollectionJsonContext.Default.TeacherChatRequest));
                    var response = journal.Find(stem + ".http-response.bin") ?? journal.Find(stem + ".http-response-prefix.bin");
                    records.Add(new(candidate.RecordId, candidate.Language, candidate.Type, candidate.FamilyId, candidate.Source,
                        "AI drafted", "pilot", "audit", "failed", attempted[index] ? "failed" : "not_called", false,
                        preflight.Identity.Model, preflight.Identity.Revision, preflight.Identity.ModelSha256, candidate.QuestionId,
                        candidate.CandidateIds, null, null, candidate.RequestSha256, candidate.PromptSha256, httpRequest.Sha256,
                        response?.Sha256, request, prompt, httpRequest, response, null, null, attempted[index], null,
                        attempted[index] ? "collection_interrupted" : "collection_stopped_not_called", 0, true));
                }
                var auditLines = new StringBuilder();
                for (var index = 0; index < records.Count && index < MaximumRecords; index++)
                {
                    auditToken.ThrowIfCancellationRequested();
                    auditLines.Append(JsonSerializer.Serialize(records[index], TeacherCollectionJsonContext.Default.TeacherCollectedRecord)).Append('\n');
                }
                var auditRecords = journal.Write("audit-records.jsonl", Encoding.UTF8.GetBytes(auditLines.ToString()));
                var pendingAudit = records.Count(row => row.ReviewStatus == "pending");
                var failedAudit = records.Count(row => row.ReviewStatus == "failed");
                var abstainedAudit = records.Count(row => row.ReviewStatus == "abstained");
                var attemptedAudit = records.Count(row => row.HttpAttempted);
                if (records.Count != preflight.Candidates.Length || pendingAudit + failedAudit + abstainedAudit != records.Count || attemptedAudit != attempts)
                    throw new InvalidDataException("Failure audit denominator differs from the preflight candidate set.");
                var audit = new TeacherFailureAudit(1, error is OperationCanceledException ? "cancelled" : "failed", false, false,
                    syntheticTransport, error is OperationCanceledException ? "collection_cancelled" : "collection_operation_failed",
                    output, preflight.Identity, preflight.SourceManifestSha256, preflight.CandidatesSha256, records.Count,
                    pendingAudit, failedAudit, abstainedAudit, attempts, attemptedAudit, records.Count - attemptedAudit,
                    watch.Elapsed.TotalSeconds, auditRecords);
                journal.Write("audit.json", JsonSerializer.SerializeToUtf8Bytes(audit, TeacherCollectionJsonContext.Default.TeacherFailureAudit));
                auditToken.ThrowIfCancellationRequested();
                if (journal.Root != auditPath) journal.Publish(auditPath);
                retainedForAudit = true;
                onFailurePublished?.Invoke(auditPath, journal);
                Console.Error.WriteLine($"teacher_collection_failure_audit status={audit.Status} total={audit.Total} attempted={audit.Attempted} not_called={audit.NotCalled} path={auditPath}");
            }
            catch (Exception auditError) when (auditError is IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
            {
                // A storage/identity failure may prevent a complete audit. Preserve the owned evidence and report its exact path.
                retainedForAudit = true;
                Console.Error.WriteLine($"teacher_collection_incomplete_audit_preserved path={journal.Root} reason={auditError.GetType().Name}");
            }
            throw;
        }
        finally { if (!published && !retainedForAudit) journal.Cleanup(); }
    }

    private static TeacherPreflight Preflight(Options options, CancellationToken token, bool syntheticTransport)
    {
        var catalogPath = Path.GetFullPath(options.Catalog);
        var catalogBytes = ReadSnapshot(catalogPath, 1_048_576, token);
        using var catalog = StrictJson(catalogBytes, token);
        if (catalog.RootElement.GetProperty("schema_version").GetInt32() != 1) throw new InvalidDataException("Catalog schema_version must be 1.");
        var item = catalog.RootElement.GetProperty("candidate");
        var id = Text(item, "id", 128); var service = Text(item, "service", 128); var model = Text(item, "model", 256);
        var revision = Text(item, "revision", 256); var license = Text(item, "license", 256);
        var modelHash = HashText(item, "model_sha256"); var sourceUrl = Text(item, "source_url", 2048);
        if (id != "qwen35-9b-q4km" || model != id || service != "IoTSharp/Tomur" || Text(item, "status", 32) != "verified")
            throw new InvalidDataException("Collection requires the explicitly verified planned Tomur 9B candidate.");
        if (!syntheticTransport && (license != "Apache-2.0" || !SameHash(modelHash, "03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8")))
            throw new InvalidDataException("The real collector requires the planned catalog 9B GGUF license/hash identity; synthetic claims are test-only.");
        if (revision.Length != 40 || !revision.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("The upstream model revision must be a concrete 40-hex HuggingFace commit.");
        RequireSourceUri(sourceUrl);
        var pinnedSource = $"https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/{Uri.EscapeDataString(revision)}/Qwen3.5-9B-Q4_K_M.gguf";
        if (!sourceUrl.Equals(pinnedSource, StringComparison.Ordinal)) throw new InvalidDataException("Source URL must pin the explicit 9B GGUF revision.");
        var endpoint = Endpoint(Text(item, "endpoint", 2048));
        var catalogDirectory = Path.GetDirectoryName(catalogPath)!;
        var termsBytes = ReadSnapshot(ContainedPath(catalogDirectory, Text(item, "terms_evidence", 1024)), 65_536, token);
        var evidenceBytes = ReadSnapshot(ContainedPath(catalogDirectory, Text(item, "model_evidence", 1024)), 65_536, token);
        MatchHash(termsBytes, HashText(item, "terms_evidence_sha256"));
        MatchHash(evidenceBytes, HashText(item, "model_evidence_sha256"));
        using var terms = StrictJson(termsBytes, token);
        using var evidence = StrictJson(evidenceBytes, token);
        foreach (var root in new[] { terms.RootElement, evidence.RootElement })
        {
            token.ThrowIfCancellationRequested();
            if (root.GetProperty("schema_version").GetInt32() != 1 || Text(root, "model", 256) != model ||
                Text(root, "revision", 256) != revision || !SameHash(HashText(root, "model_sha256"), modelHash) ||
                Text(root, "source_url", 2048) != sourceUrl || Text(root, "license", 256) != license)
                throw new InvalidDataException("Terms/model evidence differs from the complete catalog identity.");
        }
        if (!terms.RootElement.GetProperty("collection_permitted").GetBoolean() || Text(terms.RootElement, "use_scope", 128) != "offline_teacher_collection")
            throw new InvalidDataException("Explicit terms evidence must permit offline teacher collection.");
        Text(terms.RootElement, "evidence_text", 32_768); Text(terms.RootElement, "reviewed_by", 256);
        if (!DateTimeOffset.TryParse(Text(terms.RootElement, "reviewed_at_utc", 128), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
            throw new InvalidDataException("Terms evidence requires a review timestamp.");
        var trainingPermitted = terms.RootElement.GetProperty("output_training_permitted").GetBoolean();
        _ = terms.RootElement.GetProperty("output_redistribution_permitted").GetBoolean();
        var modelBytes = evidence.RootElement.GetProperty("model_bytes").GetInt64();
        if (Text(evidence.RootElement, "validation_status", 64) != "hash_verified" || modelBytes <= 0 || !syntheticTransport && modelBytes != 5_680_522_464)
            throw new InvalidDataException("Model evidence must record external byte/hash verification.");
        Text(evidence.RootElement, "verification_method", 2048);
        if (!DateTimeOffset.TryParse(Text(evidence.RootElement, "verified_at_utc", 128), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
            throw new InvalidDataException("Model evidence requires a verification timestamp.");
        // Bounded evidence is an external claim. This tool does not independently rehash or load multi-GB teacher weights.
        var identity = new TeacherIdentity(id, service, model, revision, modelHash, sourceUrl, license, endpoint.AbsoluteUri,
            Hash(catalogBytes), Hash(termsBytes), Hash(evidenceBytes), false, trainingPermitted);
        var manifestPath = Path.GetFullPath(options.Manifest);
        var manifestBytes = ReadSnapshot(manifestPath, 1_048_576, token);
        using var manifest = StrictJson(manifestBytes, token);
        var rootManifest = manifest.RootElement;
        if (rootManifest.GetProperty("schema_version").GetInt32() != 1 || Text(rootManifest, "protocol", 32) != "decision-v1" ||
            Text(rootManifest, "origin", 32) != "AI drafted" || Text(rootManifest, "purpose", 32) != "pilot" ||
            Text(rootManifest, "split", 32) != "audit" || Text(rootManifest, "human_review", 32) != "pending" ||
            Text(rootManifest, "training_admission", 32) != "pending" || rootManifest.GetProperty("quality_evidence").GetBoolean() ||
            rootManifest.GetProperty("teacher_called").GetBoolean()) throw new InvalidDataException("Source package is not an unreviewed, uncalled audit pilot.");
        var count = rootManifest.GetProperty("total_records").GetInt32();
        if (count < 1 || count > options.MaxRecords) throw new InvalidDataException("Manifest exceeds the explicit candidate bound.");
        var package = Path.GetDirectoryName(manifestPath)!;
        var candidateAsset = rootManifest.GetProperty("candidates");
        var candidateBytes = ReadSnapshot(ContainedPath(package, Text(candidateAsset, "path", 1024)), 1_048_576, token);
        CheckAsset(candidateAsset, candidateBytes);
        var text = StrictUtf8.GetString(candidateBytes);
        if (text.StartsWith('\uFEFF')) throw new InvalidDataException("Candidate JSONL must be UTF-8 without BOM.");
        using var lines = new StringReader(text);
        var candidates = new List<TeacherCandidate>(count); var ids = new HashSet<string>(StringComparer.Ordinal);
        var families = new HashSet<string>(StringComparer.Ordinal); var referencedAssets = new HashSet<string>(StringComparer.Ordinal);
        var assets = rootManifest.GetProperty("assets");
        if (assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() != count * 2) throw new InvalidDataException("Manifest must bind exactly one request and prompt per candidate.");
        var assetMap = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var asset in assets.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if (!assetMap.TryAdd(Text(asset, "path", 1024), asset)) throw new InvalidDataException("Duplicate manifest asset.");
        }
        for (var index = 0; index < count && index < MaximumRecords; index++)
        {
            token.ThrowIfCancellationRequested();
            var line = lines.ReadLine() ?? throw new InvalidDataException("Candidate count is below the manifest denominator.");
            if (line.Length == 0 || Encoding.UTF8.GetByteCount(line) > MaximumInputBytes) throw new InvalidDataException("Invalid candidate line byte budget.");
            using var document = StrictJson(Encoding.UTF8.GetBytes(line), token);
            var row = document.RootElement;
            var recordId = Text(row, "record_id", 128); var family = Text(row, "family_id", 128);
            var language = Text(row, "language", 16); var type = Text(row, "type", 32); var source = Text(row, "source", 256);
            if (!ids.Add(recordId) || !families.Add(family) || language is not ("zh" or "en") ||
                Text(row, "origin", 32) != "AI drafted" || Text(row, "purpose", 32) != "pilot" ||
                Text(row, "split", 32) != "audit" || Text(row, "review_status", 32) != "pending" ||
                Text(row, "training_admission", 32) != "pending" || Text(row, "teacher_status", 32) != "not_called")
                throw new InvalidDataException("Candidate identity/provenance/pending contract is invalid.");
            var requestPath = Text(row, "request_path", 1024); var promptPath = Text(row, "prompt_path", 1024);
            var request = ReadBoundAsset(requestPath, HashText(row, "request_sha256"));
            var prompt = ReadBoundAsset(promptPath, HashText(row, "prompt_sha256"));
            var parsed = IndependentDecisionV1Parser.Parse(request);
            using var rawRequest = StrictJson(request, token);
            var rawQuestion = rawRequest.RootElement.GetProperty("questions")[0];
            if (type == "choice") ExactProperties(rawQuestion, "id", "type", "instruction", "candidates");
            else if (type == "score") ExactProperties(rawQuestion, "id", "type", "instruction", "levels");
            else if (type == "boolean") ExactProperties(rawQuestion, "id", "type", "instruction", "statement", "when_false", "when_true");
            else throw new InvalidDataException("Unsupported candidate question type.");
            if (parsed.SchemaVersion != 1 || parsed.ModelId != Text(rootManifest, "student_model_id", 128) || !parsed.ModelId.StartsWith("sezika/", StringComparison.Ordinal) ||
                parsed.State.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array) || parsed.Questions.Count != 1)
                throw new InvalidDataException("Collector requires exactly one strict student question per candidate.");
            var question = parsed.Questions[0];
            var candidateIds = question switch
            {
                IndependentChoiceQuestion q when type == "choice" => q.Candidates.Select(candidate => candidate.Id).ToArray(),
                IndependentScoreQuestion q when type == "score" && q.Levels.All(level => !string.IsNullOrWhiteSpace(level)) => Enumerable.Range(0, q.Levels.Count).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray(),
                IndependentBooleanQuestion when type == "boolean" => ["false", "true"],
                _ => throw new InvalidDataException("Candidate and strict question type differ.")
            };
            if (candidateIds.Distinct(StringComparer.Ordinal).Count() != candidateIds.Length) throw new InvalidDataException("Duplicate candidate identity.");
            var promptText = StrictUtf8.GetString(prompt);
            if (string.IsNullOrWhiteSpace(promptText) || !promptText.Contains(StrictUtf8.GetString(request), StringComparison.Ordinal))
                throw new InvalidDataException("Prompt must contain the exact hash-bound strict request snapshot.");
            candidates.Add(new(recordId, language, type, family, source, request, Hash(request), prompt, Hash(prompt), question.Id, candidateIds));
            Console.WriteLine($"teacher_collection_preflight item={index + 1}/{count}");
        }
        if (lines.ReadLine() is not null || referencedAssets.Count != count * 2) throw new InvalidDataException("Extra candidate rows or unbound assets.");
        return new(identity, candidates.ToArray(), Hash(manifestBytes), Hash(candidateBytes), catalogBytes, manifestBytes, termsBytes, evidenceBytes);

        byte[] ReadBoundAsset(string relative, string expectedHash)
        {
            if (!referencedAssets.Add(relative) || !assetMap.TryGetValue(relative, out var asset)) throw new InvalidDataException("Missing/reused manifest asset.");
            var bytes = ReadSnapshot(ContainedPath(package, relative), MaximumInputBytes, token);
            MatchHash(bytes, expectedHash); CheckAsset(asset, bytes); return bytes;
        }
    }

    private sealed record ParsedResponse(bool Abstained, int? Index, string? Id, TeacherUsage Usage, string FinishReason);

    private static ParsedResponse ParseResponse(byte[] bytes, string model, TeacherCandidate candidate, int maximumOutputTokens, CancellationToken token,
        out TeacherUsage? observedUsage, out string? observedFinishReason)
    {
        observedUsage = null; observedFinishReason = null;
        using var document = StrictJson(bytes, token);
        var root = document.RootElement;
        if (Text(root, "model", 256) != model || Text(root, "object", 64) != "chat.completion") throw new InvalidDataException("Unexpected response model/object.");
        var choices = root.GetProperty("choices");
        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1) throw new InvalidDataException("Exactly one text completion is required.");
        var choice = choices[0]; var message = choice.GetProperty("message");
        if (choice.GetProperty("index").GetInt32() != 0 || Text(message, "role", 32) != "assistant" ||
            message.TryGetProperty("tool_calls", out var tools) && tools.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Unexpected completion role/index or tools.");
        var content = Text(message, "content", MaximumInputBytes);
        var finishReason = Text(choice, "finish_reason", 64);
        observedFinishReason = finishReason;
        var rawUsage = root.GetProperty("usage");
        var usage = new TeacherUsage(rawUsage.GetProperty("prompt_tokens").GetInt32(), rawUsage.GetProperty("completion_tokens").GetInt32(), rawUsage.GetProperty("total_tokens").GetInt32());
        if (usage.PromptTokens < 0 || usage.CompletionTokens < 0 || usage.TotalTokens != checked(usage.PromptTokens + usage.CompletionTokens))
            throw new InvalidDataException("Invalid response usage denominator.");
        observedUsage = usage;
        if (usage.CompletionTokens > maximumOutputTokens) throw new InvalidDataException("Reported completion exceeds the requested output-token budget.");
        if (finishReason != "stop") throw new InvalidDataException("Non-stop completion cannot supply a complete hard label.");
        using var labelDocument = StrictJson(StrictUtf8.GetBytes(content), token);
        var label = labelDocument.RootElement;
        ExactProperties(label, "question_id", "status", "selected_candidate_id");
        if (Text(label, "question_id", 128) != candidate.QuestionId) throw new InvalidDataException("Teacher label question ID differs.");
        var status = Text(label, "status", 32); var selected = label.GetProperty("selected_candidate_id");
        if (status == "abstained" && selected.ValueKind == JsonValueKind.Null) return new(true, null, null, usage, finishReason);
        if (status != "answered" || selected.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid answer/abstention label contract.");
        var id = selected.GetString(); var index = Array.IndexOf(candidate.CandidateIds, id);
        if (index < 0) throw new InvalidDataException("Teacher selected a candidate outside the request.");
        return new(false, index, id, usage, finishReason);
    }

    private static async Task<byte[]> ReadResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        // Each successful read advances >=1 byte. At most 131073 reads plus EOF, with a request deadline.
        for (var read = 0; read <= MaximumResponseBytes + 1; read++)
        {
            token.ThrowIfCancellationRequested();
            var remaining = MaximumResponseBytes + 1 - (int)buffer.Length;
            var count = await stream.ReadAsync(block.AsMemory(0, Math.Min(block.Length, remaining)), token);
            if (count == 0) return buffer.ToArray();
            buffer.Write(block, 0, count);
            if (buffer.Length > MaximumResponseBytes) throw new ResponseLimitException(buffer.ToArray()[..MaximumResponseBytes]);
        }
        // Short reads can exhaust the iteration budget without EOF; partial content must never become a hard label.
        throw new ResponseLimitException(buffer.ToArray());
    }

    private sealed class ResponseLimitException(byte[] prefix) : IOException
    { public byte[] Prefix { get; } = prefix; }

    private static byte[] ReadSnapshot(string path, int maximumBytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > maximumBytes) throw new InvalidDataException($"Asset exceeds its 1..{maximumBytes} byte bound: {path}");
        var bytes = new byte[checked((int)stream.Length)]; var offset = 0;
        for (var read = 0; offset < bytes.Length && read < maximumBytes; read++)
        {
            token.ThrowIfCancellationRequested();
            var count = stream.Read(bytes, offset, bytes.Length - offset);
            if (count == 0) throw new EndOfStreamException("Input snapshot changed or ended.");
            offset += count;
        }
        if (offset != bytes.Length || stream.ReadByte() != -1) throw new IOException("Input snapshot length changed.");
        token.ThrowIfCancellationRequested(); return bytes;
    }

    private static JsonDocument StrictJson(byte[] bytes, CancellationToken token)
    {
        _ = StrictUtf8.GetString(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        var objects = new Stack<HashSet<string>>();
        for (var index = 0; index <= bytes.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if (!reader.Read()) break;
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek().Add(reader.GetString()!)) throw new InvalidDataException("Duplicate JSON property.");
        }
        if (objects.Count != 0 || reader.BytesConsumed != bytes.Length) throw new JsonException("Incomplete JSON.");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static void ExactProperties(JsonElement item, params string[] names)
    {
        if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != names.Length || names.Any(name => !item.TryGetProperty(name, out _)))
            throw new InvalidDataException("Unknown/missing response label fields.");
    }
    private static string Text(JsonElement item, string name, int maximum)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > maximum)
            throw new InvalidDataException($"Missing or unbounded string {name}.");
        return value.GetString()!;
    }
    private static string HashText(JsonElement item, string name)
    {
        var value = Text(item, name, 64);
        if (value.Length != 64 || !value.All(char.IsAsciiHexDigit)) throw new InvalidDataException($"Invalid SHA-256 {name}.");
        return value;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool SameHash(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static void MatchHash(byte[] bytes, string expected) { if (!SameHash(Hash(bytes), expected)) throw new InvalidDataException("Asset hash mismatch."); }
    private static void CheckAsset(JsonElement asset, byte[] bytes)
    { MatchHash(bytes, HashText(asset, "sha256")); if (asset.GetProperty("bytes").GetInt32() != bytes.Length) throw new InvalidDataException("Asset byte count mismatch."); }

    private static string ContainedPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("Evidence/package assets must use relative paths.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Asset escaped its package root.");
        // Reject symlinks/junctions for every existing path component, bounded by the relative path character limit.
        var current = fullRoot.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var component in Path.GetRelativePath(current, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if (File.Exists(current) || Directory.Exists(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse-point evidence assets are unsupported.");
        }
        return path;
    }

    private static Uri Endpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/v1/chat/completions" ||
            !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) || !IPAddress.IsLoopback(address))
            throw new InvalidDataException("Endpoint must be a literal loopback /v1/chat/completions URI without credentials/query/fragment.");
        return uri;
    }
    private static void RequireSourceUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Explicit HTTPS upstream source identity required.");
    }

    private sealed record Options(string Catalog, string Manifest, string Output, int MaxRecords, int RequestTimeoutSeconds, int TotalTimeoutSeconds, int MaxOutputTokens)
    {
        public static Options Parse(string[] args)
        {
            if (args.Length is < 1 or > 15 || args[0] != "collect" || args.Length % 2 != 1) throw new ArgumentException("collect --catalog <file> --manifest <file> --output <new-directory> [--max-records 1..32] [--request-timeout-seconds 1..120] [--total-timeout-seconds 1..1200] [--max-output-tokens 1..256]");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] allowed = ["--catalog", "--manifest", "--output", "--max-records", "--request-timeout-seconds", "--total-timeout-seconds", "--max-output-tokens"];
            for (var index = 1; index + 1 < args.Length && index < 15; index += 2)
                if (!allowed.Contains(args[index], StringComparer.Ordinal) || !values.TryAdd(args[index], args[index + 1])) throw new ArgumentException("Unknown/duplicate collection argument.");
            string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Missing {key}.");
            int Number(string key, int fallback, int maximum) => !values.TryGetValue(key, out var value) ? fallback : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1 && parsed <= maximum ? parsed : throw new ArgumentException($"{key} must be 1..{maximum}.");
            return new(Required("--catalog"), Required("--manifest"), Required("--output"), Number("--max-records", 32, 32),
                Number("--request-timeout-seconds", 120, 120), Number("--total-timeout-seconds", 1200, 1200), Number("--max-output-tokens", 256, 256));
        }
    }

    private sealed class OutputJournal(string root)
    {
        private string _root = Path.GetFullPath(root);
        private readonly List<TeacherAsset> _created = [];
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
        public string Root => _root;
        public TeacherAsset? Find(string relative) => _created.FirstOrDefault(asset => asset.Path == relative);
        public void RemoveOwned(string relative, CancellationToken token)
        {
            var asset = Find(relative);
            if (asset is null) return;
            var path = ContainedPath(_root, relative);
            var bytes = ReadSnapshotAllowEmpty(path, token);
            if (bytes.Length != asset.Bytes || !SameHash(Hash(bytes), asset.Sha256)) throw new InvalidDataException("Refusing to remove a mismatching normal manifest from failure audit.");
            File.Delete(path); _created.Remove(asset);
        }
        public void Publish(string output)
        {
            var target = Path.GetFullPath(output);
            if (Path.GetDirectoryName(target) != Path.GetDirectoryName(_root) || File.Exists(target) || Directory.Exists(target))
                throw new IOException("Atomic output publication requires a new sibling directory.");
            Directory.Move(_root, target);
            _root = target;
        }
        public TeacherAsset Write(string relative, byte[] bytes)
        {
            if (_created.Count >= 137 || bytes.Length > 4_194_304) throw new IOException("Output item/byte bound exceeded.");
            var path = ContainedPath(_root, relative);
            var directory = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(directory)) { Directory.CreateDirectory(directory); _directories.Add(Path.GetRelativePath(_root, directory)); }
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                try { stream.Write(bytes); }
                finally
                {
                    // Position binds any task-created partial write; cleanup preserves mismatching or foreign content.
                    var written = checked((int)stream.Position);
                    _created.Add(new(relative, Hash(bytes.AsSpan(0, written).ToArray()), written));
                }
            }
            return new(relative, Hash(bytes), bytes.Length);
        }
        public void Cleanup(CancellationToken cancellationToken = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            for (var index = _created.Count - 1; index >= 0 && index < 137; index--)
            {
                var asset = _created[index];
                try
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    var path = ContainedPath(_root, asset.Path);
                    if (File.Exists(path))
                    {
                        if (new FileInfo(path).Length == asset.Bytes && SameHash(Hash(ReadSnapshotAllowEmpty(path, deadline.Token)), asset.Sha256)) File.Delete(path);
                        else Console.Error.WriteLine($"teacher_collection_cleanup_preserved path={asset.Path} reason=identity_mismatch");
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
                { Console.Error.WriteLine($"teacher_collection_cleanup_preserved path={asset.Path} reason={error.GetType().Name}"); }
            }
            foreach (var relative in _directories.OrderByDescending(value => value.Length).Take(137))
            {
                try
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    var directory = ContainedPath(_root, relative);
                    if (Directory.Exists(directory))
                    {
                        if (!Directory.EnumerateFileSystemEntries(directory).Take(1).Any()) Directory.Delete(directory);
                        else Console.Error.WriteLine($"teacher_collection_cleanup_preserved directory={relative} reason=nonempty");
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
                { Console.Error.WriteLine($"teacher_collection_cleanup_preserved directory={relative} reason={error.GetType().Name}"); }
            }
            try
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (Directory.Exists(_root))
                {
                    if ((File.GetAttributes(_root) & FileAttributes.ReparsePoint) == 0 && !Directory.EnumerateFileSystemEntries(_root).Take(1).Any()) Directory.Delete(_root);
                    else Console.Error.WriteLine($"teacher_collection_cleanup_preserved root={_root} reason=nonempty_or_reparse");
                }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
            { Console.Error.WriteLine($"teacher_collection_cleanup_preserved root={_root} reason={error.GetType().Name}"); }
        }
        private static byte[] ReadSnapshotAllowEmpty(string path, CancellationToken token) => new FileInfo(path).Length == 0 ? [] : ReadSnapshot(path, 4_194_304, token);
    }
}
