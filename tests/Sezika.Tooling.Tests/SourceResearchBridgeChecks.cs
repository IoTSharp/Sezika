using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class SourceResearchBridgeChecks
{
    internal const int CheckCount = 6;
    private const string Revision = "1111111111111111111111111111111111111111";
    private static readonly byte[] Body = "{}\n{}\n"u8.ToArray();

    internal static void Run(string root, Action<string, Action> check)
    {
        var fixtures = Fresh(Path.Combine(root, "source-research-bridge"));
        using var suiteCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var clock = Stopwatch.StartNew();
        Budget Limit() => new(10, null, suiteCancellation.Token);
        void Gate()
        {
            suiteCancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 20) throw new TimeoutException("Source/research bridge group deadline20s.");
        }
        void Case(string name, Action action) => check(name, () => { Gate(); action(); Gate(); });

        Case("source research bridge real fake-handler acquisition preserves SHA256 and Git blob pins", () =>
        {
            var fixture = Fixture(fixtures, "complete");
            using var handler = new Handler(request => Response(request), suiteCancellation.Token); using var client = new HttpClient(handler);
            var acquired = Acquire(fixture.Request, client, Limit()); var report = Research(fixture, "research.json", Limit());
            var plan = Plan(fixture, Limit());
            Assert(acquired.CompleteAssets == 2 && report.PlannedFiles == 2 && report.DownloadedFiles == 2 && report.MissingFiles == 0 &&
                report.PhysicalRows == 4 && report.AllSelectedFilesDownloaded && !report.RowsAreUniqueQuestions && !report.FormalTrainingAdmitted,
                "Typed acquisition did not reach complete independent research readback.");
            Assert(plan.Length == 2 && plan[0].ExpectedDigest == new ContentDigest("sha256", Sha(Body)) && plan[0].ExpectedSha256 == Sha(Body) &&
                plan[1].ExpectedDigest == new ContentDigest("git-blob-sha1", Blob(Body)) && plan[1].ExpectedSha256 is null &&
                plan.All(row => row.SourceAcquisitionPlanSha256 == fixture.Request.PlanSha256 && row.Revision == Revision) &&
                report.Inventory.All(row => row.Sha256 == Sha(Body)), "Observed content SHA replaced an original expected digest.");
        });
        Case("source research bridge selected subset retains every planned asset and source denominator", () =>
        {
            var fixture = Fixture(fixtures, "selected", secondSource: true);
            using var handler = new Handler(request => Response(request), suiteCancellation.Token); using var client = new HttpClient(handler);
            var acquired = Acquire(fixture.Request with { SelectedAssetIds = ["sha"] }, client, Limit());
            var report = Research(fixture, "research.json", Limit()); var ledger = Ledger(fixture, Limit());
            Assert(acquired.AllSelectedAssetsComplete && !acquired.AllPlannedAssetsComplete && Plan(fixture, Limit()).Length == 2 && ledger.Length == 2 &&
                report.PlannedFiles == 2 && report.DownloadedFiles == 1 && report.MissingFiles == 1 && !report.AllSelectedFilesDownloaded &&
                acquired.PlannedSources == 2 && report.Sources.Length == 2 && report.Sources.Sum(source => source.PlannedFiles) == 2 &&
                report.Missing.Single().LatestAttemptStatus == "NOT_SELECTED" && report.Missing.Single().ExpectedSha256 is null,
                "Selection shrank compatibility plan or invented a Git blob SHA256.");
        });
        Case("source research bridge partial and wrong digest failures retain physical evidence", () =>
        {
            var fixture = Fixture(fixtures, "partials", mirror: true);
            using var handler = new Handler(request => Response(request, request.RequestUri!.Host == "hf-mirror.com" ? Body[..2] : Body[..1]), suiteCancellation.Token);
            using var client = new HttpClient(handler);
            _ = Acquire(fixture.Request with { SelectedAssetIds = ["sha"] }, client, Limit());
            var ledger = Ledger(fixture, Limit()); ledger[0] = ledger[0] with { RetainedPartialBytes = 999999 };
            WriteLedger(fixture, ledger); var report = Research(fixture, "research.json", Limit());
            Assert(report.DownloadedFiles == 0 && report.MissingFiles == 2 && report.Missing.Single(row => row.Path == "raw/sha.jsonl").PartialBytes == 3 &&
                report.Sources.Single().ResumablePartialBytes == 3 && report.Missing.Single(row => row.Path == "raw/sha.jsonl").LatestAttemptStatus == "FAILED",
                "Research trusted claimed partial bytes or ignored an endpoint partial.");
            var wrong = Fixture(fixtures, "wrong-digest", wrongSha: true);
            using var wrongHandler = new Handler(request => Response(request), suiteCancellation.Token); using var wrongClient = new HttpClient(wrongHandler);
            _ = Acquire(wrong.Request with { SelectedAssetIds = ["sha"] }, wrongClient, Limit());
            var failed = Research(wrong, "research.json", Limit()); var wrongPlan = Plan(wrong, Limit());
            Assert(failed.DownloadedFiles == 0 && failed.Missing.Single(row => row.Path == "raw/sha.jsonl").PartialBytes == Body.Length &&
                wrongPlan[0].ExpectedSha256 == new string('0', 64) && wrongPlan[0].ExpectedDigest.Value == new string('0', 64),
                "Failed bytes became expected gold or their partial disappeared.");
        });
        Case("source research bridge cancellation writes complete plan and ledger before rethrow", () =>
        {
            var fixture = Fixture(fixtures, "cancel"); using var cancellation = new CancellationTokenSource();
            using var handler = new Handler(request => { cancellation.Cancel(); return Response(request); }, suiteCancellation.Token); using var client = new HttpClient(handler);
            Cancelled(() => CorpusSourceAcquisition.AcquireAsync(fixture.Request, Limit(), cancellation.Token, client).GetAwaiter().GetResult());
            var plan = Plan(fixture, Limit()); var ledger = Ledger(fixture, Limit()); var report = Research(fixture, "research.json", Limit());
            Assert(handler.Requests == 1 && plan.Length == 2 && ledger.Length == 2 && ledger[0].Status == "CANCELLED" && ledger[1].Status == "NOT_ATTEMPTED" &&
                report.PlannedFiles == 2 && report.MissingFiles == 2 && report.DownloadedFiles == 0, "Cancellation lost full plan/ledger or retried a request.");
        });
        Case("source research bridge latest failure preserves earlier success and unknown status rejects", () =>
        {
            var fixture = Fixture(fixtures, "latest"); using var handler = new Handler(request => Response(request), suiteCancellation.Token); using var client = new HttpClient(handler);
            _ = Acquire(fixture.Request, client, Limit()); var rows = Ledger(fixture, Limit());
            SourceAcquisitionEntry[] later = [rows[0], rows[1], rows[0] with { Status = "FAILED", Bytes = 0, ContentSha256 = null, FailureType = "HttpRequestException" }];
            WriteLedger(fixture, later); var report = Research(fixture, "research.json", Limit());
            Assert(report.DownloadedFiles == 2 && report.MissingFiles == 0, "Latest failure erased an independently verifiable earlier success.");
            later[2] = later[2] with { Status = "UNRECOGNIZED_SUCCESS" }; WriteLedger(fixture, later);
            Reject(() => Research(fixture, "unknown-status.json", Limit()));
        });
        Case("source research bridge plan SHA path source revision and original digest tampering rejects", () =>
        {
            var fixture = Fixture(fixtures, "identity"); using var handler = new Handler(request => Response(request), suiteCancellation.Token); using var client = new HttpClient(handler);
            _ = Acquire(fixture.Request, client, Limit()); var rows = Ledger(fixture, Limit());
            SourceAcquisitionEntry[] invalid = [rows[0] with { PlanSha256 = new string('0', 64) }, rows[0] with { Path = "raw/other.jsonl" },
                rows[0] with { SourceId = "fixture/other" }, rows[0] with { Revision = new string('2', 40) }, rows[0] with { ExpectedDigest = new("sha256", new string('0', 64)) }];
            for (var index = 0; index < invalid.Length && index < 5; index++)
            {
                Gate(); WriteLedger(fixture, [invalid[index], rows[1]]); var output = "invalid-" + index + ".json";
                Reject(() => Research(fixture, output, Limit())); Assert(!File.Exists(Path.Combine(fixture.Root, output)), "Forged identity produced a report.");
            }
            // Even matching content-SHA receipts cannot stand in for the original
            // Git blob digest. The compatibility plan never gains a SHA256 gold.
            var plan = Plan(fixture, Limit()); var badBlob = new ContentDigest("git-blob-sha1", new string('0', 40));
            plan[1] = plan[1] with { ExpectedDigest = badBlob };
            File.WriteAllBytes(CompatibilityPath(fixture), JsonSerializer.SerializeToUtf8Bytes(plan, SourceAcquisitionJsonContext.Default.SourceResearchPlanEntryArray));
            WriteLedger(fixture, [rows[0], rows[1] with { ExpectedDigest = badBlob }]);
            Reject(() => Research(fixture, "wrong-original-blob.json", Limit()));
            Assert(plan[1].ExpectedSha256 is null, "Negative fixture filled Git blob SHA256 gold.");
        });
    }

    private sealed record BridgeFixture(string Root, SourceAcquisitionRequest Request);
    private static BridgeFixture Fixture(string root, string name, bool mirror = false, bool wrongSha = false, bool secondSource = false)
    {
        var folder = Fresh(Path.Combine(root, name)); var metadata = Fresh(Path.Combine(folder, "metadata")); var bytes = "fixed source and license metadata"u8.ToArray();
        File.WriteAllBytes(Path.Combine(metadata, "source.txt"), bytes);
        SourceAcquisitionSource source = new("fixture/data", "huggingface-dataset", Revision, "cc-by-4.0", [new("source.txt", bytes.Length, Sha(bytes))]);
        var alternate = secondSource ? source with { Id = "fixture/other" } : source;
        SourceAcquisitionAsset Asset(string id, ContentDigest digest, SourceAcquisitionSource assetSource) => new(id, assetSource.Id, "raw/" + id + ".jsonl", "data/" + id + ".jsonl", "source-train", Body.Length,
            digest, "https://huggingface.co/datasets/" + assetSource.Id + "/resolve/" + Revision + "/data/" + id + ".jsonl", mirror, 4);
        SourceAcquisitionSource[] sources = secondSource ? [source, alternate] : [source];
        SourceAcquisitionPlan plan = new(1, sources, [Asset("sha", new("sha256", wrongSha ? new string('0', 64) : Sha(Body)), source), Asset("blob", new("git-blob-sha1", Blob(Body)), alternate)]);
        var raw = JsonSerializer.SerializeToUtf8Bytes(plan, SourceAcquisitionJsonContext.Default.SourceAcquisitionPlan); var path = Path.Combine(folder, "plan.json"); File.WriteAllBytes(path, raw);
        return new(folder, new(path, Sha(raw), metadata, Path.Combine(folder, "cache"), Path.Combine(folder, "receipts")));
    }
    private static SourceAcquisitionReport Acquire(SourceAcquisitionRequest request, HttpClient client, Budget budget) =>
        CorpusSourceAcquisition.AcquireAsync(request, budget, budget.Cancellation, client).GetAwaiter().GetResult();
    private static string CompatibilityPath(BridgeFixture fixture) => Path.Combine(fixture.Request.ReceiptDirectory, "research-download-plan.json");
    private static SourceResearchPlanEntry[] Plan(BridgeFixture fixture, Budget budget) =>
        CorpusJson.Read(CompatibilityPath(fixture), SourceAcquisitionJsonContext.Default.SourceResearchPlanEntryArray, budget);
    private static SourceAcquisitionEntry[] Ledger(BridgeFixture fixture, Budget budget) => CorpusJson.Lines(Path.Combine(fixture.Request.ReceiptDirectory, "source-ledger.jsonl"), 4, budget)
        .Select(row => row.Deserialize(SourceAcquisitionJsonContext.Default.SourceAcquisitionEntry) ?? throw new InvalidDataException("Fixture ledger row missing.")).ToArray();
    private static void WriteLedger(BridgeFixture fixture, SourceAcquisitionEntry[] rows)
    {
        if (rows.Length is < 1 or > 4) throw new InvalidDataException("Fixture ledger row bound4.");
        var text = string.Join('\n', rows.Select(row => JsonSerializer.Serialize(row, SourceAcquisitionJsonContext.Default.SourceAcquisitionEntry))) + "\n";
        File.WriteAllText(Path.Combine(fixture.Request.ReceiptDirectory, "source-ledger.jsonl"), text, CorpusJson.Utf8);
    }
    private static CorpusResearchCheckReport Research(BridgeFixture fixture, string name, Budget budget) => CorpusOperations.CheckResearch(
        new(fixture.Request.OutputRoot, [CompatibilityPath(fixture)], [Path.Combine(fixture.Request.ReceiptDirectory, "source-ledger.jsonl")], Path.Combine(fixture.Root, name), 4, 8, true), budget);
    private static HttpResponseMessage Response(HttpRequestMessage request, byte[]? body = null)
    {
        var range = request.Headers.Range!.Ranges.Single(); var start = checked((int)range.From!.Value); var end = checked((int)range.To!.Value);
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(body ?? Body[start..(end + 1)]) };
        response.Headers.ETag = new EntityTagHeaderValue(request.RequestUri!.Host == "hf-mirror.com" ? "\"mirror-v1\"" : "\"official-v1\"");
        response.Content.Headers.ContentRange = new(start, end, Body.Length); response.Content.Headers.ContentLength = end - start + 1; return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler, CancellationToken suiteToken) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        private readonly Stopwatch clock = Stopwatch.StartNew();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); suiteToken.ThrowIfCancellationRequested();
            if (++Requests > 64 || clock.Elapsed.TotalSeconds >= 10) throw new TimeoutException("Bridge fake handler request64/deadline10s.");
            return Task.FromResult(handler(request));
        }
    }
    private static string Fresh(string path) { path = Path.GetFullPath(path); if (Path.Exists(path)) throw new IOException("Preserve existing bridge fixture."); Directory.CreateDirectory(path); return path; }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Blob(byte[] bytes)
    {
        byte[] data = [.. Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + '\0'), .. bytes];
        return Convert.ToHexStringLower(SHA1.HashData(data));
    }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Expected bridge identity/digest rejection."); }
    private static void Cancelled(Action action) { try { action(); } catch (OperationCanceledException) { return; } throw new InvalidOperationException("Expected bridge cancellation."); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
