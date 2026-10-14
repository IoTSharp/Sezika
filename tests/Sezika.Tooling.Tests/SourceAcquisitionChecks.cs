using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class SourceAcquisitionChecks
{
    internal const int CheckCount = 12;
    private const string Revision = "1111111111111111111111111111111111111111";
    private static readonly byte[] Content = "abcdef"u8.ToArray();

    internal static void Run(string root, Action<string, Action> check)
    {
        var fixtures = Path.Combine(root, "source-acquisition-fixtures");
        if (Path.Exists(fixtures)) throw new IOException("Preserve existing source-acquisition fixtures.");
        Directory.CreateDirectory(fixtures);
        using var suiteCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var clock = Stopwatch.StartNew();
        Budget Limit() => new(10, null, suiteCancellation.Token);
        void Case(string name, Action action) => check(name, () =>
        {
            suiteCancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 20) throw new TimeoutException("Source acquisition fixture group deadline20s.");
            action();
            suiteCancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 20) throw new TimeoutException("Source acquisition fixture group deadline20s.");
        });

        Case("source acquisition selected subset retains all source and asset denominators", () =>
        {
            var fixture = Fixture(fixtures, "selection", [Asset("a"), Asset("b", source: "fixture/other")], secondSource: true);
            using var handler = new FixtureHandler(request => Response(request, Content), suiteCancellation.Token);
            using var client = new HttpClient(handler);
            var report = Acquire(fixture.Request with { SelectedAssetIds = ["a"] }, client, Limit());
            Assert(report.PlannedSources == 2 && report.PlannedAssets == 2 && report.SelectedAssets == 1 && report.CompleteAssets == 1 &&
                report.UnselectedAssets == 1 && report.AllSelectedAssetsComplete && !report.AllPlannedAssetsComplete && report.Entries.Length == 2,
                "Selection narrowed the frozen denominator.");
            var unselected = report.Sources.Single(source => source.Id == "fixture/other");
            Assert(unselected.PlannedAssets == 1 && unselected.SelectedAssets == 0 && unselected.CompleteAssets == 0 &&
                !unselected.AllPlannedAssetsComplete && unselected.Revision == Revision && unselected.MetadataPins.Length == 1 && report.Entries[0].PlanSha256 == fixture.Request.PlanSha256 &&
                report.Entries[0].OfficialUrl == Asset("a").OfficialUrl &&
                unselected.DeclaredLicense == "cc-by-4.0" && !report.FormalTrainingAdmitted, "Source provenance or admission scope changed.");
            Assert(File.ReadAllLines(Path.Combine(fixture.Request.ReceiptDirectory, "source-ledger.jsonl")).Length == 2, "Ledger omitted an unselected asset.");
        });
        Case("source acquisition Git blob SHA1 is separate from content SHA256 and legacy state", () =>
        {
            var blob = new ContentDigest("git-blob-sha1", BlobHash(Content));
            var fixture = Fixture(fixtures, "git-blob", [Asset("a", digest: blob), Asset("b", digest: new("git-blob-sha1", new string('0', 40)))]);
            using var handler = new FixtureHandler(request => Response(request, Content), suiteCancellation.Token);
            using var client = new HttpClient(handler);
            var report = Acquire(fixture.Request, client, Limit());
            Assert(report.Entries[0].ExpectedDigest == blob && blob.Value.Length == 40 && report.Entries[0].ContentSha256 == Sha(Content) &&
                report.Entries[0].ContentSha256!.Length == 64, "Git blob SHA1 was substituted for content SHA256.");
            Assert(report.Entries[1].Status == "FAILED" && report.Entries[1].ExpectedDigest.Kind == "git-blob-sha1" &&
                report.Entries[1].ExpectedDigest.Value == new string('0', 40) && report.Entries[1].ContentSha256 is null,
                "A mismatching trusted Git blob digest was accepted or replaced.");
            var legacy = Path.Combine(fixture.Base, "legacy.bin");
            var old = AssetTransport.DownloadAsync(new("https://huggingface.co/fixture", Content.Length, Sha(Content), RangeBytes: 4),
                legacy, Limit(), suiteCancellation.Token, client: client).GetAwaiter().GetResult();
            using var state = JsonDocument.Parse(File.ReadAllBytes(legacy + ".sezika-download.json"));
            Assert(old.Status == "PASS_complete_download_bytes_sha256" && old.Sha256 == Sha(Content) &&
                state.RootElement.GetProperty("sha256").GetString() == Sha(Content) && !state.RootElement.TryGetProperty("expected_digest", out _),
                "Legacy SHA256 report/state semantics changed.");
        });
        Case("source acquisition rejects digest-kind substitution before HTTP", () =>
        {
            var fixture = Fixture(fixtures, "digest-kind", [Asset("a", digest: new("sha256", BlobHash(Content)))]);
            using var handler = new FixtureHandler(_ => throw new InvalidOperationException("HTTP must not run."), suiteCancellation.Token);
            using var client = new HttpClient(handler);
            Reject(() => Acquire(fixture.Request, client, Limit()));
            Assert(handler.Calls == 0 && !Directory.Exists(fixture.Request.ReceiptDirectory), "Malformed expected digest reached HTTP.");
            Reject(() => AssetTransport.ValidateContentDigest(new("sha1", BlobHash(Content))));
            var pinned = CorpusJson.Read(fixture.Request.PlanPath, SourceAcquisitionJsonContext.Default.SourceAcquisitionPlan, Limit());
            var wrongRevisionUrl = pinned with { Assets = [Asset("a") with { OfficialUrl = Asset("a").OfficialUrl.Replace(Revision, "main", StringComparison.Ordinal) }] };
            var raw = JsonSerializer.SerializeToUtf8Bytes(wrongRevisionUrl, SourceAcquisitionJsonContext.Default.SourceAcquisitionPlan);
            File.WriteAllBytes(fixture.Request.PlanPath, raw);
            Reject(() => Acquire(fixture.Request with { PlanSha256 = Sha(raw) }, client, Limit()));
            var missingLicense = pinned with { Sources = [pinned.Sources[0] with { DeclaredLicense = "" }], Assets = [Asset("a")] };
            raw = JsonSerializer.SerializeToUtf8Bytes(missingLicense, SourceAcquisitionJsonContext.Default.SourceAcquisitionPlan);
            File.WriteAllBytes(fixture.Request.PlanPath, raw);
            Reject(() => Acquire(fixture.Request with { PlanSha256 = Sha(raw) }, client, Limit()));
            Assert(handler.Calls == 0, "Inconsistent source revision URL or missing declared license reached HTTP.");
        });
        Case("source acquisition wrong trusted digest preserves partial and failed ledger", () =>
        {
            var expected = new ContentDigest("sha256", new string('0', 64));
            var fixture = Fixture(fixtures, "wrong-digest", [Asset("a", digest: expected)]);
            using var handler = new FixtureHandler(request => Response(request, Content), suiteCancellation.Token);
            using var client = new HttpClient(handler);
            var report = Acquire(fixture.Request, client, Limit()); var row = report.Entries[0];
            Assert(report.FailedAssets == 1 && !report.AllSelectedAssetsComplete && row.Status == "FAILED" && row.ExpectedDigest == expected &&
                row.ContentSha256 is null && row.RetainedPartialBytes == Content.Length && row.Attempts.Length == 1 && row.Attempts[0].Requests is null &&
                !File.Exists(Path.Combine(fixture.Request.OutputRoot, "raw/a.bin")), "Failed downloaded bytes became trusted or partial evidence disappeared.");
        });
        Case("source acquisition complete cache is reverified and corrupt cache is preserved", () =>
        {
            var fixture = Fixture(fixtures, "cache", [Asset("a")]);
            var cached = Path.Combine(fixture.Request.OutputRoot, "raw/a.bin"); Directory.CreateDirectory(Path.GetDirectoryName(cached)!); File.WriteAllBytes(cached, Content);
            using var handler = new FixtureHandler(_ => throw new InvalidOperationException("Cache must not request HTTP."), suiteCancellation.Token);
            using var client = new HttpClient(handler);
            var good = Acquire(fixture.Request, client, Limit());
            File.WriteAllBytes(cached, "ghijkl"u8.ToArray());
            var bad = Acquire(fixture.Request with { ReceiptDirectory = Path.Combine(fixture.Base, "receipts-2") }, client, Limit());
            Assert(good.Entries[0].Status == "CACHED_VERIFIED" && good.Entries[0].Attempts.Length == 0 && bad.Entries[0].Status == "FAILED" &&
                handler.Calls == 0 && File.ReadAllBytes(cached).SequenceEqual("ghijkl"u8.ToArray()), "Cache was trusted from a ledger or overwritten.");
        });
        Case("source acquisition interrupted Range resumes exact same URL ETag and digest", () =>
        {
            var fixture = Fixture(fixtures, "resume", [Asset("a")]); var first = true;
            using var handler = new FixtureHandler(request =>
            {
                if (first) { first = false; return Response(request, Content, body: Content[..2]); }
                return Response(request, Content);
            }, suiteCancellation.Token);
            using var client = new HttpClient(handler);
            var interrupted = Acquire(fixture.Request, client, Limit());
            var resumed = Acquire(fixture.Request with { ReceiptDirectory = Path.Combine(fixture.Base, "receipts-2") }, client, Limit());
            Assert(interrupted.Entries[0].Status == "FAILED" && interrupted.Entries[0].RetainedPartialBytes == 2 &&
                resumed.Entries[0].Status == "DOWNLOADED_VERIFIED" && resumed.Entries[0].Attempts[0].ResumedBytes == 2 &&
                handler.Ranges.SequenceEqual(new[] { "0-3", "2-5" }), "Truncated source did not retain and resume its exact partial identity.");
        });
        Case("source acquisition mirror failure has one isolated official fallback", () =>
        {
            var fixture = Fixture(fixtures, "fallback", [Asset("a", mirror: true), Asset("b", mirror: true)]);
            using var handler = new FixtureHandler(request =>
            {
                if (request.RequestUri!.Host != "hf-mirror.com") return Response(request, Content, etag: "\"official-v1\"");
                if (request.RequestUri.AbsolutePath.EndsWith("/a.bin", StringComparison.Ordinal)) return Response(request, Content, body: Content[..2], etag: "\"mirror-v1\"");
                return new(HttpStatusCode.NotFound);
            }, suiteCancellation.Token);
            using var client = new HttpClient(handler);
            var report = Acquire(fixture.Request, client, Limit()); var row = report.Entries[0];
            Assert(row.Status == "DOWNLOADED_VERIFIED" && row.Attempts.Select(attempt => attempt.Route).SequenceEqual(new[] { "mirror", "official" }) &&
                row.Attempts[0].Status == "FAILED" && handler.Hosts.Count(host => host == "hf-mirror.com") == 2 && handler.Hosts.Count(host => host == "huggingface.co") == 4 &&
                row.MirrorDecision == "ELIGIBLE_PINNED_REVISION_AND_DIGEST" && row.RetainedPartialBytes == 2, "Mirror fallback count or eligibility differs.");
            Assert(report.Entries[1].Attempts.Length == 2 && report.Entries[1].Attempts[0].HttpStatusCode == 404 && report.Entries[1].Status == "DOWNLOADED_VERIFIED",
                "HTTP failure identity or bounded official fallback was lost.");
            var stage = Path.Combine(fixture.Request.OutputRoot, "raw/a.bin.sezika-source-");
            var mirrorState = AssetTransport.ReadJson(stage + "mirror.sezika-download.json", TransportJsonContext.Default.DigestDownloadState, Limit());
            var officialState = AssetTransport.ReadJson(stage + "official.sezika-download.json", TransportJsonContext.Default.DigestDownloadState, Limit());
            Assert(mirrorState.ETag == "\"mirror-v1\"" && officialState.ETag == "\"official-v1\"" &&
                mirrorState.Url.StartsWith("https://hf-mirror.com/", StringComparison.Ordinal) && officialState.Url == Asset("a").OfficialUrl,
                "Mirror/official partial identity was shared or rewritten.");
        });
        Case("source acquisition auth disables mirror and never reaches redirected origin or receipts", () =>
        {
            const string secret = "fixture-memory-only-secret";
            var fixture = Fixture(fixtures, "auth", [Asset("a", mirror: true)]); var authenticated = 0; var leaked = false;
            using var handler = new FixtureHandler(request =>
            {
                if (request.RequestUri!.Host == "huggingface.co")
                {
                    if (request.Headers.Authorization?.Parameter == secret) authenticated++;
                    return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://cdn.huggingface.co/fixture") } };
                }
                leaked |= request.Headers.Authorization is not null; return Response(request, Content);
            }, suiteCancellation.Token);
            using var client = new HttpClient(handler);
            var report = CorpusSourceAcquisition.AcquireAsync(fixture.Request, Limit(), suiteCancellation.Token, client, new("Bearer", secret)).GetAwaiter().GetResult();
            var state = Path.Combine(fixture.Request.OutputRoot, "raw/a.bin.sezika-source-official.sezika-download.json");
            Assert(report.CompleteAssets == 1 && report.Entries[0].MirrorDecision == "DISABLED_AUTHENTICATION" && authenticated == 2 && !leaked &&
                !handler.Hosts.Contains("hf-mirror.com") && !File.ReadAllText(state).Contains(secret, StringComparison.Ordinal) &&
                !JsonSerializer.Serialize(report, SourceAcquisitionJsonContext.Default.SourceAcquisitionReport).Contains(secret, StringComparison.Ordinal),
                "Authentication escaped official origin or appeared in persistent evidence.");
            var calls = handler.Calls; client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
            Reject(() => Acquire(fixture.Request with { ReceiptDirectory = Path.Combine(fixture.Base, "receipts-2") }, client, Limit()));
            Assert(handler.Calls == calls, "Client-wide authentication reached the transport.");
        });
        Case("source acquisition one credential rejects mixed selected official origins before writes or HTTP", () =>
        {
            var fixture = Fixture(fixtures, "mixed-auth", [Asset("a"), Asset("b", source: "fixture/other")], secondSource: true);
            var plan = CorpusJson.Read(fixture.Request.PlanPath, SourceAcquisitionJsonContext.Default.SourceAcquisitionPlan, Limit());
            plan = plan with
            {
                Sources = [plan.Sources[0], plan.Sources[1] with { Provider = "github-raw" }],
                Assets = [plan.Assets[0], plan.Assets[1] with { OfficialUrl = "https://raw.githubusercontent.com/fixture/other/" + Revision + "/data/b.bin" }]
            };
            var raw = JsonSerializer.SerializeToUtf8Bytes(plan, SourceAcquisitionJsonContext.Default.SourceAcquisitionPlan);
            File.WriteAllBytes(fixture.Request.PlanPath, raw);
            var request = fixture.Request with { PlanSha256 = Sha(raw) };
            using var handler = new FixtureHandler(message => Response(message, Content), suiteCancellation.Token);
            using var client = new HttpClient(handler);
            Reject(() => CorpusSourceAcquisition.AcquireAsync(request, Limit(), suiteCancellation.Token, client,
                new("Bearer", "fixture-hf-memory-only")).GetAwaiter().GetResult());
            Assert(handler.Calls == 0 && !Directory.Exists(request.OutputRoot) && !Directory.Exists(request.ReceiptDirectory),
                "One credential crossed selected origins or created output before rejection.");
            var publicReport = Acquire(request, client, Limit());
            Assert(publicReport.CompleteAssets == 2 && handler.Hosts.Contains("huggingface.co") && handler.Hosts.Contains("raw.githubusercontent.com"),
                "A mixed public source plan lost its complete denominator.");
        });
        Case("source acquisition cancellation retains full ledger and prevents official fallback", () =>
        {
            var fixture = Fixture(fixtures, "cancel", [Asset("a", mirror: true), Asset("b")]);
            using var cancellation = new CancellationTokenSource();
            using var handler = new FixtureHandler(_ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }, suiteCancellation.Token);
            using var client = new HttpClient(handler);
            Cancelled(() => CorpusSourceAcquisition.AcquireAsync(fixture.Request, Limit(), cancellation.Token, client).GetAwaiter().GetResult());
            var report = CorpusJson.Read(Path.Combine(fixture.Request.ReceiptDirectory, "source-acquisition-report.json"), SourceAcquisitionJsonContext.Default.SourceAcquisitionReport, Limit());
            Assert(handler.Calls == 1 && handler.Hosts.Single() == "hf-mirror.com" && report.PlannedAssets == 2 && report.Entries.Length == 2 &&
                report.Entries[0].Status == "CANCELLED" && report.Entries[0].Attempts.Length == 1 && report.Entries[1].Status == "NOT_ATTEMPTED" &&
                report.NotAttemptedAssets == 1 && !report.AllSelectedAssetsComplete, "Cancellation retried or discarded denominator/failure evidence.");
        });
        Case("source acquisition and legacy download preserve independent Budget token cancellation during HTTP", () =>
        {
            for (var mode = 0; mode < 2; mode++)
            {
                using var parentCancellation = CancellationTokenSource.CreateLinkedTokenSource(suiteCancellation.Token);
                var limit = new Budget(5, null, parentCancellation.Token);
                using var handler = new BudgetCancellationHandler(parentCancellation);
                using var client = new HttpClient(handler);
                var elapsed = Stopwatch.StartNew();
                if (mode == 0)
                    Cancelled(() => AssetTransport.DownloadAsync(new("https://huggingface.co/fixture", Content.Length, Sha(Content)),
                        Path.Combine(fixtures, "legacy-budget-cancel.bin"), limit, CancellationToken.None, client: client).GetAwaiter().GetResult());
                else
                {
                    var fixture = Fixture(fixtures, "budget-cancel", [Asset("a", mirror: true), Asset("b")]);
                    Cancelled(() => CorpusSourceAcquisition.AcquireAsync(fixture.Request, limit, CancellationToken.None, client).GetAwaiter().GetResult());
                    var report = CorpusJson.Read(Path.Combine(fixture.Request.ReceiptDirectory, "source-acquisition-report.json"),
                        SourceAcquisitionJsonContext.Default.SourceAcquisitionReport, Limit());
                    Assert(report.Entries.Length == 2 && report.Entries[0].Status == "CANCELLED" &&
                        report.Entries[0].Attempts.Length == 1 && report.Entries[1].Status == "NOT_ATTEMPTED",
                        "Independent Budget cancellation retried or lost the full ledger.");
                }
                Assert(handler.Calls == 1 && elapsed.Elapsed.TotalSeconds < 1,
                    "Independent Budget cancellation did not interrupt the in-flight HTTP await.");
            }
        });
        Case("source acquisition pinned plan and metadata mismatch prevent all requests", () =>
        {
            var fixture = Fixture(fixtures, "pins", [Asset("a")]);
            using var handler = new FixtureHandler(_ => throw new InvalidOperationException("Pinned mismatch must not request HTTP."), suiteCancellation.Token);
            using var client = new HttpClient(handler);
            Reject(() => Acquire(fixture.Request with { PlanSha256 = new string('0', 64) }, client, Limit()));
            File.AppendAllText(Path.Combine(fixture.Request.MetadataRoot, "publisher.json"), "tampered");
            Reject(() => Acquire(fixture.Request, client, Limit()));
            Assert(handler.Calls == 0 && !Directory.Exists(fixture.Request.ReceiptDirectory), "Unpinned metadata or plan reached HTTP.");
        });
    }

    private sealed record FixturePaths(string Base, SourceAcquisitionRequest Request);
    private static FixturePaths Fixture(string root, string name, SourceAcquisitionAsset[] assets, bool secondSource = false)
    {
        var directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
        var metadata = Path.Combine(directory, "metadata"); Directory.CreateDirectory(metadata);
        var raw = Encoding.UTF8.GetBytes("{\"sources\":[\"fixture/data\",\"fixture/other\"],\"revision\":\"" + Revision + "\",\"declared_license\":\"cc-by-4.0\"}");
        File.WriteAllBytes(Path.Combine(metadata, "publisher.json"), raw);
        AssetFile[] pins = [new("publisher.json", raw.Length, Sha(raw))];
        SourceAcquisitionSource[] sources = secondSource ? [new("fixture/data", "huggingface-dataset", Revision, "cc-by-4.0", pins), new("fixture/other", "huggingface-dataset", Revision, "cc-by-4.0", pins)] : [new("fixture/data", "huggingface-dataset", Revision, "cc-by-4.0", pins)];
        var plan = JsonSerializer.SerializeToUtf8Bytes(new SourceAcquisitionPlan(1, sources, assets), SourceAcquisitionJsonContext.Default.SourceAcquisitionPlan);
        var planPath = Path.Combine(directory, "plan.json"); File.WriteAllBytes(planPath, plan);
        return new(directory, new(planPath, Sha(plan), metadata, Path.Combine(directory, "cache"), Path.Combine(directory, "receipts")));
    }
    private static SourceAcquisitionAsset Asset(string id, ContentDigest? digest = null, bool mirror = false, string source = "fixture/data") =>
        new(id, source, "raw/" + id + ".bin", "data/" + id + ".bin", "source-train", Content.Length, digest ?? new("sha256", Sha(Content)),
            "https://huggingface.co/datasets/" + source + "/resolve/" + Revision + "/data/" + id + ".bin", mirror, 4);
    private static SourceAcquisitionReport Acquire(SourceAcquisitionRequest request, HttpClient client, Budget budget) =>
        CorpusSourceAcquisition.AcquireAsync(request, budget, budget.Cancellation, client).GetAwaiter().GetResult();
    private static HttpResponseMessage Response(HttpRequestMessage request, byte[] content, byte[]? body = null, string etag = "\"fixture-v1\"")
    {
        var range = request.Headers.Range?.Ranges.Single() ?? throw new InvalidOperationException("Expected a bounded Range request.");
        var start = checked((int)range.From!.Value); var end = checked((int)range.To!.Value);
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(body ?? content[start..(end + 1)]) };
        response.Headers.ETag = new(etag); response.Content.Headers.ContentRange = new(start, end, content.Length);
        response.Content.Headers.ContentLength = end - start + 1; return response;
    }
    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> handler, CancellationToken suiteToken) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal List<string> Hosts { get; } = [];
        internal List<string> Ranges { get; } = [];
        private readonly Stopwatch clock = Stopwatch.StartNew();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); suiteToken.ThrowIfCancellationRequested();
            if (++Calls > 64 || clock.Elapsed.TotalSeconds >= 10) throw new TimeoutException("Fake source handler request64/deadline10s.");
            Hosts.Add(request.RequestUri!.Host); var range = request.Headers.Range!.Ranges.Single(); Ranges.Add(range.From + "-" + range.To);
            return Task.FromResult(handler(request));
        }
    }
    private sealed class BudgetCancellationHandler(CancellationTokenSource budgetCancellation) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (++Calls > 1) throw new InvalidOperationException("Cancellation must prevent fallback HTTP.");
            budgetCancellation.Cancel();
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            throw new InvalidOperationException("HTTP await ignored the independently cancelled Budget token.");
        }
    }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string BlobHash(byte[] bytes)
    {
        byte[] blob = [.. Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + '\0'), .. bytes];
        return Convert.ToHexStringLower(SHA1.HashData(blob));
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or JsonException) { return; }
        throw new InvalidOperationException("Expected source acquisition rejection.");
    }
    private static void Cancelled(Action action)
    {
        try { action(); } catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected source acquisition cancellation.");
    }
}
