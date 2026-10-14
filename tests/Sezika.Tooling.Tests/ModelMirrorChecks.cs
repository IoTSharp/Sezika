using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class ModelMirrorChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        Budget Limit() => new(20, null, CancellationToken.None);
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Reject(Action action)
        {
            var rejected = false;
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or OperationCanceledException or TimeoutException or HttpRequestException or JsonException)
            { rejected = true; }
            Assert(rejected, "Expected model mirror contract rejection was absent.");
        }
        Fixture Make(string name)
        {
            var directory = Path.Combine(root, "model-mirror-" + name); Directory.CreateDirectory(directory);
            var data = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["config.json"] = Encoding.UTF8.GetBytes("{\"fixture\":\"config\"}\n"),
                ["tokenizer/tokenizer.json"] = Encoding.UTF8.GetBytes("{\"fixture\":\"tokenizer\"}\n"),
                ["encoder.safetensors"] = Encoding.UTF8.GetBytes("Independent inert weight fixture; no inference.\n"),
                ["LICENSE"] = Encoding.UTF8.GetBytes("Independent license byte fixture.\n")
            };
            var roles = new[] { ModelAssetRole.Config, ModelAssetRole.Tokenizer, ModelAssetRole.Weights, ModelAssetRole.License };
            var names = new[] { "config.json", "tokenizer/tokenizer.json", "encoder.safetensors", "LICENSE" };
            var assets = names.Select((name, index) => new ModelMirrorAsset(name, roles[index], data[name].Length,
                "sha256", Convert.ToHexStringLower(SHA256.HashData(data[name])))).ToArray();
            return new(directory, Path.Combine(directory, "input-manifest.json"),
                new(1, "http://127.0.0.1/models/fixture", new('a', 40), "Apache-2.0", assets), data);
        }
        ModelMirrorRequest Request(Fixture fixture, ModelMirrorManifest? manifest = null)
        {
            File.WriteAllText(fixture.ManifestPath, JsonSerializer.Serialize(manifest ?? fixture.Manifest,
                ModelMirrorJsonContext.Default.ModelMirrorManifest), new UTF8Encoding(false));
            return new(1, fixture.Root, fixture.ManifestPath, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture.ManifestPath))),
                Path.Combine(fixture.Root, "cache"), Path.Combine(fixture.Root, "output"), OperatingSystem.IsWindows() ? "win-x64" : "linux-x64");
        }
        ModelMirrorReport Run(ModelMirrorRequest request, HttpClient client, CancellationToken cancellation = default) =>
            ModelMirror.MirrorAsync(request, Limit(), cancellation, "fixture-secret-only-in-memory", client).GetAwaiter().GetResult();

        check("model mirror tiny composes all four roles through fake ranged HTTP and bundle readback", () =>
        {
            var f = Make("tiny"); var request = Request(f); using var handler = new ModelHandler(f.Data); using var client = new HttpClient(handler);
            var result = Run(request, client);
            Assert(result.Verification.AllManifestAssetsVerified && result.Assets.Length == 4 && handler.Requests == 4 && handler.SawAuthentication,
                "Model role/download/readback denominator differs.");
            Assert(result.Verification.Coverage == "CALLER_PINNED_ASSET_SET_ONLY" && result.Verification.Publication == "NOT_RUN",
                "Model asset preservation overclaimed upstream or publication acceptance.");
            Assert(!JsonSerializer.Serialize(result, ModelMirrorJsonContext.Default.ModelMirrorReport).Contains("fixture-secret", StringComparison.Ordinal),
                "In-memory credential escaped into a model receipt.");
        });
        check("model mirror complete cache is rehashed and reused without HTTP", () =>
        {
            var f = Make("cache"); var request = Request(f); using var handler = new ModelHandler(f.Data); using var client = new HttpClient(handler);
            _ = Run(request, client); var before = handler.Requests;
            var second = Run(request with { OutputDirectory = Path.Combine(f.Root, "second-output") }, client);
            Assert(handler.Requests == before && second.Assets.All(a => a.CacheStatus == "PASS_COMPLETE_CACHE_SHA_READBACK" && a.Requests == 0),
                "Completed cache was not fully verified/reused.");
            var cached = Path.Combine(request.CacheDirectory, request.ManifestSha256, "encoder.safetensors");
            var changed = File.ReadAllBytes(cached); changed[0] ^= 1; File.WriteAllBytes(cached, changed);
            Reject(() => Run(request with { OutputDirectory = Path.Combine(f.Root, "corrupt-output") }, client));
            Assert(!Directory.Exists(Path.Combine(f.Root, "corrupt-output")), "Corrupt cache created a complete output.");
        });
        check("model mirror truncated partial preserves state then resumes exact external pins", () =>
        {
            var f = Make("partial"); var request = Request(f); using var handler = new ModelHandler(f.Data) { TruncateFirst = true }; using var client = new HttpClient(handler);
            Reject(() => Run(request, client));
            var partial = Path.Combine(request.CacheDirectory, request.ManifestSha256, "config.json.sezika-part");
            Assert(File.Exists(partial) && new FileInfo(partial).Length == 3 && !Directory.Exists(request.OutputDirectory),
                "Failed model partial was lost or accepted as complete.");
            var result = Run(request, client);
            Assert(result.Assets[0].ResumedBytes == 3 && result.Verification.AllManifestAssetsVerified, "Model exact partial resume failed.");
        });
        check("model mirror changed ETag cannot resume a preserved partial", () =>
        {
            var f = Make("etag"); var request = Request(f); using var handler = new ModelHandler(f.Data) { TruncateFirst = true }; using var client = new HttpClient(handler);
            Reject(() => Run(request, client)); handler.EntityTag = "\"changed-model-object\"";
            Reject(() => Run(request, client));
            Assert(new FileInfo(Path.Combine(request.CacheDirectory, request.ManifestSha256, "config.json.sezika-part")).Length == 3 &&
                !Directory.Exists(request.OutputDirectory), "Changed object identity replaced or accepted the partial.");
        });
        check("model mirror wrong downloaded asset SHA cannot become expected provenance", () =>
        {
            var f = Make("wrong-sha"); var assets = f.Manifest.Assets.ToArray(); assets[0] = assets[0] with { Digest = new('0', 64) };
            var request = Request(f, f.Manifest with { Assets = assets }); using var handler = new ModelHandler(f.Data); using var client = new HttpClient(handler);
            Reject(() => Run(request, client));
            Assert(!Directory.Exists(request.OutputDirectory) && File.Exists(Path.Combine(request.CacheDirectory, request.ManifestSha256, "config.json.sezika-part")),
                "Wrong downloaded bytes were accepted or their partial evidence was deleted.");
        });
        check("model mirror external manifest pin and Git blob SHA1 confusion reject before requests", () =>
        {
            var f = Make("pins"); var request = Request(f); using var handler = new ModelHandler(f.Data); using var client = new HttpClient(handler);
            Reject(() => Run(request with { ManifestSha256 = new('0', 64) }, client));
            var assets = f.Manifest.Assets.ToArray(); assets[0] = assets[0] with { DigestKind = "git-blob-sha1", Digest = new('b', 40) };
            Reject(() => Run(Request(f, f.Manifest with { Assets = assets }), client));
            assets[0] = assets[0] with { Digest = f.Manifest.Assets[0].Digest };
            Reject(() => Run(Request(f, f.Manifest with { Assets = assets }), client));
            Assert(handler.Requests == 0 && !Directory.Exists(request.CacheDirectory), "Untrusted manifest/digest admitted download/cache mutation.");
        });
        check("model mirror duplicate JSON manifest properties reject before mutation", () =>
        {
            var f = Make("duplicate-json"); var request = Request(f);
            File.WriteAllText(f.ManifestPath, "{\"schema_version\":1,\"schema_version\":1}");
            request = request with { ManifestSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f.ManifestPath))) };
            using var handler = new ModelHandler(f.Data); using var client = new HttpClient(handler);
            Reject(() => Run(request, client));
            Assert(handler.Requests == 0 && !Directory.Exists(request.CacheDirectory), "Duplicate manifest JSON reached storage/download.");
        });
        check("model mirror unknown manifest fields cannot be copied into cache or bundle", () =>
        {
            var f = Make("unknown-json"); var request = Request(f); var json = File.ReadAllText(f.ManifestPath);
            File.WriteAllText(f.ManifestPath, "{\"authorization\":\"inert-fixture-value\"," + json[1..]);
            request = request with { ManifestSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f.ManifestPath))) };
            using var handler = new ModelHandler(f.Data); using var client = new HttpClient(handler);
            Reject(() => Run(request, client));
            Assert(handler.Requests == 0 && !Directory.Exists(request.CacheDirectory) && !Directory.Exists(request.OutputDirectory),
                "Noncontract input fields were persisted by the model composition.");
        });
        check("model mirror mutable revision missing license role and path collisions reject before HTTP", () =>
        {
            var f = Make("invalid"); using var handler = new ModelHandler(f.Data); using var client = new HttpClient(handler);
            Reject(() => Run(Request(f, f.Manifest with { Revision = "main" }), client));
            var assets = f.Manifest.Assets.ToArray(); assets[3] = assets[3] with { Role = ModelAssetRole.Config };
            Reject(() => Run(Request(f, f.Manifest with { Assets = assets }), client));
            assets = f.Manifest.Assets.ToArray(); assets[0] = assets[0] with { Path = "../escape.json" };
            Reject(() => Run(Request(f, f.Manifest with { Assets = assets }), client));
            assets = f.Manifest.Assets.ToArray(); assets[1] = assets[1] with { Path = "config.json/child" };
            Reject(() => Run(Request(f, f.Manifest with { Assets = assets }), client));
            assets = f.Manifest.Assets.ToArray(); assets[1] = assets[1] with { Path = "tokenizer/remote.py" };
            Reject(() => Run(Request(f, f.Manifest with { Assets = assets }), client));
            Assert(handler.Requests == 0, "Invalid fixed model contract reached HTTP.");
        });
        check("model mirror cancelled request creates no cache or output", () =>
        {
            var f = Make("cancel"); var request = Request(f); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            using var handler = new ModelHandler(f.Data); using var client = new HttpClient(handler);
            Reject(() => Run(request, client, cancelled.Token));
            Assert(handler.Requests == 0 && !Directory.Exists(request.CacheDirectory) && !Directory.Exists(request.OutputDirectory), "Cancelled model scope mutated storage.");
        });
        check("model mirror cancellation during HTTP preserves owned partial evidence", () =>
        {
            var f = Make("cancel-active"); var request = Request(f); using var cancelled = new CancellationTokenSource();
            using var handler = new ModelHandler(f.Data) { CancelDuringFirst = cancelled }; using var client = new HttpClient(handler);
            Reject(() => Run(request, client, cancelled.Token));
            Assert(!Directory.Exists(request.OutputDirectory) && File.Exists(Path.Combine(request.CacheDirectory, request.ManifestSha256, "config.json.sezika-part")),
                "In-flight cancellation accepted a complete model or erased owned evidence.");
        });
        check("model mirror full manifest rejects missing extra and changed files", () =>
        {
            var f = Make("verify"); var request = Request(f); using var handler = new ModelHandler(f.Data); using var client = new HttpClient(handler);
            _ = Run(request, client); var verify = new ModelMirrorVerifyRequest(1, request.OutputDirectory, request.ManifestSha256, request.RuntimeIdentifier);
            var extra = Path.Combine(request.OutputDirectory, "foreign.bin"); File.WriteAllText(extra, "foreign");
            Reject(() => ModelMirror.Verify(verify, Limit())); File.Delete(extra);
            var weights = Path.Combine(request.OutputDirectory, "encoder.safetensors"); var original = File.ReadAllBytes(weights);
            var changed = original.ToArray(); changed[0] ^= 1; File.WriteAllBytes(weights, changed);
            Reject(() => ModelMirror.Verify(verify, Limit())); File.WriteAllBytes(weights, original);
            File.Delete(Path.Combine(request.OutputDirectory, "LICENSE")); Reject(() => ModelMirror.Verify(verify, Limit()));
        });
        check("model mirror foreign output/cache and escaped scope are preserved", () =>
        {
            var f = Make("foreign"); var request = Request(f); using var handler = new ModelHandler(f.Data); using var client = new HttpClient(handler);
            Directory.CreateDirectory(request.OutputDirectory); var sentinel = Path.Combine(request.OutputDirectory, "sentinel"); File.WriteAllText(sentinel, "keep");
            Reject(() => Run(request, client)); Assert(File.ReadAllText(sentinel) == "keep", "Foreign output changed.");
            Reject(() => Run(request with { OutputDirectory = Path.Combine(Path.GetDirectoryName(f.Root)!, "escaped-output") }, client));
            var entry = Path.Combine(request.CacheDirectory, request.ManifestSha256); Directory.CreateDirectory(entry); File.WriteAllText(Path.Combine(entry, "foreign"), "keep");
            Reject(() => Run(request with { OutputDirectory = Path.Combine(f.Root, "fresh-output") }, client));
            Assert(handler.Requests == 0 && File.ReadAllText(Path.Combine(entry, "foreign")) == "keep", "Foreign cache changed or downloaded.");
        });
    }

    private sealed record Fixture(string Root, string ManifestPath, ModelMirrorManifest Manifest, Dictionary<string, byte[]> Data);

    private sealed class ModelHandler(Dictionary<string, byte[]> data) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        internal bool SawAuthentication { get; private set; }
        internal bool TruncateFirst { get; set; }
        internal string EntityTag { get; set; } = "\"model-fixed-fixture\"";
        internal CancellationTokenSource? CancelDuringFirst { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++Requests > 64) throw new InvalidDataException("Fake model HTTP request bound64.");
            var prefix = "/models/fixture/resolve/" + new string('a', 40) + "/";
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (!path.StartsWith(prefix, StringComparison.Ordinal) || !data.TryGetValue(path[prefix.Length..], out var bytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            SawAuthentication |= request.Headers.Authorization?.Scheme == "Bearer";
            var range = request.Headers.Range?.Ranges.Single() ?? throw new InvalidDataException("Expected bounded model Range.");
            var from = checked((int)(range.From ?? 0)); var to = checked((int)(range.To ?? bytes.Length - 1));
            if (from < 0 || to < from || to >= bytes.Length) throw new InvalidDataException("Fake model range differs.");
            var content = bytes[from..(to + 1)];
            if (TruncateFirst && Requests == 1) content = content[..3];
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(content) };
            response.Headers.ETag = new EntityTagHeaderValue(EntityTag);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, bytes.Length);
            response.Content.Headers.ContentLength = to - from + 1;
            if (Requests == 1) CancelDuringFirst?.Cancel();
            return Task.FromResult(response);
        }
    }
}
