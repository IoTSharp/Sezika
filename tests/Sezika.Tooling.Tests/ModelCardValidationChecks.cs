using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class ModelCardValidationChecks
{
    // All handlers are synthetic and perform zero network operations. Root may
    // register this Run method as a separately bounded suite; no native fixture
    // process, SDK, token store or hosted HF endpoint is required by these checks.
    internal static void Run(string root, Action<string, Action> check)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var owned = Path.Combine(root, "model-card-synthetic"); Directory.CreateDirectory(owned);
        var fixtureIndex = 0;
        Budget Budget() => new(20, null, cancellation.Token);
        static void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        ModelCardValidationRequest Card(string text = "---\nlanguage: zh\nlicense: mit\n---\n中文 model card\n")
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (++fixtureIndex > 64) throw new InvalidOperationException("Model-card fixture bound exceeded.");
            var path = Path.Combine(owned, "README-" + fixtureIndex + ".md"); var bytes = Encoding.UTF8.GetBytes(text);
            using (var stream = new FileStream(path, FileMode.CreateNew)) stream.Write(bytes);
            return new(1, path, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), ServiceRoot: "http://127.0.0.1:1/");
        }
        static HttpResponseMessage Reply(HttpRequestMessage message, string json, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { RequestMessage = message, Content = new StringContent(json, Encoding.UTF8, "application/json") };
        ModelCardValidationReport Execute(ModelCardValidationRequest request, FakeHandler handler, string? token = null, Budget? budget = null,
            CancellationToken? tokenOverride = null)
        {
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            return ModelCardValidation.ValidateFixtureAsync(request, client, budget ?? Budget(), tokenOverride ?? cancellation.Token, token).GetAwaiter().GetResult();
        }
        static void Reject(Action action)
        {
            var rejected = false;
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or ArgumentException) { rejected = true; }
            Assert(rejected, "Synthetic model-card expected strict rejection absent.");
        }

        check("model-card synthetic exact UTF8 wire and one scoped POST", () =>
        {
            var card = Card(); const string secret = "hf_synthetic_only_memory_token";
            var handler = new FakeHandler(async (message, token) =>
            {
                Assert(message.Method == HttpMethod.Post && message.RequestUri!.AbsoluteUri == "http://127.0.0.1:1/api/validate-yaml", "Provider method or exact endpoint differs.");
                Assert(message.Headers.Authorization?.Scheme == "Bearer" && message.Headers.Authorization.Parameter == secret, "Request-scoped auth missing.");
                Assert(message.Content!.Headers.ContentType?.MediaType == "application/json", "Wire content type differs.");
                using var json = JsonDocument.Parse(await message.Content.ReadAsByteArrayAsync(token));
                var fields = json.RootElement.EnumerateObject().ToArray();
                Assert(fields.Length == 2 && json.RootElement.GetProperty("repoType").GetString() == "model" &&
                    json.RootElement.GetProperty("content").GetString() == File.ReadAllText(card.CardPath), "Exact official input fields or complete UTF8 card differ.");
                return Reply(message, "{}");
            });
            var report = Execute(card, handler, secret);
            Assert(report.Status == "PASS" && report.Outcome == "VALID" && report.FixtureOnly && report.InputUnchanged &&
                report.HttpStatusCode == 200 && report.Warnings == 0 && report.Errors == 0 && report.Requests == 1 && handler.Calls == 1,
                "Synthetic valid-card result or operation bound differs.");
        });
        check("model-card synthetic warning counts preserve no raw messages", () =>
        {
            const string secret = "hf_synthetic_message_echo";
            var report = Execute(Card(), new FakeHandler((message, _) => Task.FromResult(Reply(message,
                "{\"warnings\":[{\"message\":\"" + secret + "\"},{\"message\":\"empty metadata\"}],\"errors\":[]}"))), secret);
            var durable = JsonSerializer.Serialize(report, ModelCardValidationJsonContext.Default.ModelCardValidationReport);
            Assert(report.Status == "PASS" && report.Warnings == 2 && report.Errors == 0 && !report.ProviderMessagesIncluded &&
                !durable.Contains(secret, StringComparison.Ordinal) && !durable.Contains("empty metadata", StringComparison.Ordinal), "Provider message echoed into result.");
        });
        check("model-card synthetic invalid metadata preserves400 and input", () =>
        {
            var card = Card(); var before = File.ReadAllBytes(card.CardPath);
            var report = Execute(card, new FakeHandler((message, _) => Task.FromResult(Reply(message,
                "{\"warnings\":[],\"errors\":[{\"message\":\"synthetic invalid field\"}]}", HttpStatusCode.BadRequest))));
            Assert(report.Status == "FAIL" && report.Outcome == "INVALID" && report.HttpStatusCode == 400 && report.Errors == 1 &&
                report.InputUnchanged && File.ReadAllBytes(card.CardPath).SequenceEqual(before), "400 invalid result or source preservation differs.");
        });
        check("model-card synthetic HTTP failures never retry or leak bodies", () =>
        {
            foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.ServiceUnavailable })
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var handler = new FakeHandler((message, _) => Task.FromResult(Reply(message,
                    "{\"errors\":[{\"message\":\"provider-secret-body\"}]}", status)));
                var report = Execute(Card(), handler);
                Assert(report.Status == "FAIL" && report.Outcome == "HTTP_ERROR" && report.HttpStatusCode == (int)status &&
                    report.Requests == 1 && handler.Calls == 1 && !JsonSerializer.Serialize(report, ModelCardValidationJsonContext.Default.ModelCardValidationReport)
                    .Contains("provider-secret-body", StringComparison.Ordinal), "HTTP original state, one POST or redaction differs.");
            }
        });
        check("model-card synthetic malformed response schema fails closed", () =>
        {
            var invalid = new[] { "not-json", "[]", "{\"warnings\":null}", "{\"errors\":[{}]}",
                "{\"warnings\":[{\"message\":1}]}", "{\"warnings\":[],\"warnings\":[]}", "{\"valid\":true}" };
            foreach (var json in invalid)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var report = Execute(Card(), new FakeHandler((message, _) => Task.FromResult(Reply(message, json))));
                Assert(report.Status == "FAIL" && report.Outcome == "INVALID_RESPONSE" && report.HttpStatusCode == 200 && report.Requests == 1,
                    "Malformed schema accepted or original200 lost.");
            }
        });
        check("model-card synthetic redirects and response host drift rejected", () =>
        {
            var handler = new FakeHandler((message, _) =>
            {
                var response = Reply(message, "{}", HttpStatusCode.TemporaryRedirect);
                response.Headers.Location = new Uri("https://other.invalid/api/validate-yaml"); return Task.FromResult(response);
            });
            var report = Execute(Card(), handler, "hf_synthetic_redirect_secret");
            Assert(report.Outcome == "REDIRECT_REJECTED" && report.HttpStatusCode == 307 && handler.Calls == 1, "Redirect followed or status lost.");
            using var driftRequest = new HttpRequestMessage(HttpMethod.Post, "https://other.invalid/api/validate-yaml");
            var drift = Execute(Card(), new FakeHandler((_, _) => Task.FromResult(Reply(driftRequest, "{}"))));
            Assert(drift.Status == "FAIL" && drift.Outcome == "RESPONSE_TARGET_REJECTED", "Response target drift accepted.");
        });
        check("model-card synthetic input mutation invalidates otherwise valid response", () =>
        {
            var card = Card();
            var report = Execute(card, new FakeHandler((message, _) =>
            {
                File.WriteAllText(card.CardPath, "changed synthetic source after POST"); return Task.FromResult(Reply(message, "{}"));
            }));
            Assert(report.Status == "FAIL" && report.Outcome == "INPUT_DRIFT" && report.HttpStatusCode == 200 && !report.InputUnchanged && report.Requests == 1,
                "Source drift was accepted or HTTP state lost.");
            Assert(File.ReadAllText(card.CardPath) == "changed synthetic source after POST", "Mutated evidence was overwritten.");
        });
        check("model-card synthetic cancellation deadline and budget stop requests", () =>
        {
            using var alreadyCancelled = new CancellationTokenSource(); alreadyCancelled.Cancel();
            var never = new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}")));
            var cancelled = Execute(Card(), never, tokenOverride: alreadyCancelled.Token);
            Assert(cancelled.Outcome == "CANCELLED" && cancelled.Requests == 0 && never.Calls == 0, "Pre-cancel performed POST.");
            var delayed = new FakeHandler(async (message, token) => { await Task.Delay(TimeSpan.FromSeconds(3), token); return Reply(message, "{}"); });
            var timeout = Execute(Card() with { TimeoutSeconds = 1 }, delayed);
            Assert(timeout.Status == "FAIL" && timeout.Outcome == "TIMED_OUT" && timeout.Requests == 1 && delayed.Calls == 1, "Deadline did not stop one synthetic request.");
            var cancelPath = Path.Combine(owned, "CANCEL"); File.WriteAllText(cancelPath, "synthetic cancel");
            var budgetHandler = new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}")));
            var budgetResult = Execute(Card(), budgetHandler, budget: new Budget(1, cancelPath, cancellation.Token));
            Assert(budgetResult.Status == "FAIL" && budgetResult.Outcome == "CANCELLED" && budgetResult.Requests == 0 && budgetHandler.Calls == 0,
                "Cancel-file state was lost or allowed POST.");
        });
        check("model-card synthetic bounds encoding and trusted identity required", () =>
        {
            var card = Card();
            var sha = Execute(card with { ExpectedSha256 = new string('0', 64) }, new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}"))));
            Assert(sha.Outcome == "INPUT_REJECTED" && sha.Requests == 0, "Wrong trusted SHA sent.");
            var length = Execute(card with { ExpectedBytes = card.ExpectedBytes + 1 }, new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}"))));
            Assert(length.Outcome == "INPUT_REJECTED" && length.Requests == 0, "Wrong byte identity sent.");
            var directory = Execute(card with { CardPath = owned }, new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}"))));
            Assert(directory.Outcome == "INPUT_REJECTED" && directory.Requests == 0, "Directory accepted as a regular card.");
            var invalidUtf8Path = Path.Combine(owned, "invalid-utf8.md"); var invalidUtf8 = new byte[] { 0xc3, 0x28 };
            File.WriteAllBytes(invalidUtf8Path, invalidUtf8);
            var encoding = Execute(card with { CardPath = invalidUtf8Path, ExpectedBytes = 2, ExpectedSha256 = Convert.ToHexStringLower(SHA256.HashData(invalidUtf8)) },
                new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}"))));
            Assert(encoding.Outcome == "INVALID_UTF8" && encoding.Requests == 0, "Non-UTF8 card sent.");
            Reject(() => Execute(card with { RepositoryType = "dataset" }, new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}")))));
            Reject(() => Execute(card with { TimeoutSeconds = 51 }, new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}")))));
            Reject(() => Execute(card with { ExpectedBytes = ModelCardValidation.MaximumCardBytes + 1L }, new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}")))));
        });
        check("model-card synthetic endpoint auth response byte caps and no client auth", () =>
        {
            foreach (var rootUri in new[] { "http://localhost/", "https://huggingface.co/", "http://127.0.0.1:1/other/", "http://127.0.0.1:1/?x=1" })
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Reject(() => Execute(Card() with { ServiceRoot = rootUri }, new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}")))));
            }
            // These production calls fail before SendAsync; no live service is contacted.
            foreach (var rootUri in new[] { "https://other.invalid/", "http://huggingface.co/", "https://huggingface.co:444/", "http://127.0.0.1:1/" })
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Reject(() => ModelCardValidation.ValidateAsync(Card() with { ServiceRoot = rootUri }, Budget(), cancellation.Token).GetAwaiter().GetResult());
            }
            Reject(() => Execute(Card(), new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}"))), "invalid\nsecret"));
            var handler = new FakeHandler((message, _) => Task.FromResult(Reply(message, "{}")));
            using var client = new HttpClient(handler); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "hf_client_wide_synthetic");
            Reject(() => ModelCardValidation.ValidateFixtureAsync(Card(), client, Budget(), cancellation.Token).GetAwaiter().GetResult());
            Assert(handler.Calls == 0, "Client-wide credential sent.");
            var oversized = Execute(Card(), new FakeHandler((message, _) =>
            {
                var response = Reply(message, "{}"); response.Content.Headers.ContentLength = ModelCardValidation.MaximumResponseBytes + 1L;
                return Task.FromResult(response);
            }));
            Assert(oversized.Status == "FAIL" && oversized.Outcome == "INVALID_RESPONSE" && oversized.HttpStatusCode == 200, "Response byte cap accepted or original status lost.");
            var streamed = Execute(Card(), new FakeHandler((message, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = message,
                    Content = new UnknownLengthContent(new byte[ModelCardValidation.MaximumResponseBytes + 1]) };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return Task.FromResult(response);
            }));
            Assert(streamed.Status == "FAIL" && streamed.Outcome == "INVALID_RESPONSE" && streamed.HttpStatusCode == 200,
                "Unknown-length response escaped the streaming cap.");
        });
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (++Calls > 1) throw new InvalidOperationException("Synthetic model-card handler observed more than one POST.");
            cancellationToken.ThrowIfCancellationRequested(); return handle(request, cancellationToken);
        }
    }

    private sealed class UnknownLengthContent(byte[] payload) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(payload).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(payload, writable: false));
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); return CreateContentReadStreamAsync();
        }
    }
}
