using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sezika.Tooling;

internal static class NewMigrationCommandChecks
{
    internal const int CheckCount = 10;
    private const string Revision = "1111111111111111111111111111111111111111";

    // Actual in-process CLI dispatch over inert, complete local caches. There
    // are no SDK calls, child processes, hosted validation or download fixtures.
    internal static void Run(string root, Action<string, Action> check)
    {
        var owned = Path.Combine(root, "new-migration-command-synthetic");
        if (Path.Exists(owned)) throw new IOException("Preserve existing migration-command fixtures.");
        Directory.CreateDirectory(owned);
        var cancelFile = Path.Combine(owned, "SUITE-CANCEL");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cancelRegistration = cancellation.Token.Register(() =>
        {
            try { using var stream = new FileStream(cancelFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read); stream.WriteByte(1); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        });
        var clock = Stopwatch.StartNew(); var areas = 0; var commands = 0;
        void Gate()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 30) throw new TimeoutException("Migration-command synthetic suite deadline30s.");
        }
        void Case(string name, Action action) => check(name, () => { Gate(); action(); Gate(); });
        static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        string Area(string name)
        {
            Gate(); if (++areas > 16) throw new InvalidOperationException("Migration-command fixture count16 exceeded.");
            var directory = Path.Combine(owned, name);
            if (Path.Exists(directory)) throw new IOException("Preserve existing named command fixture.");
            Directory.CreateDirectory(directory); return directory;
        }
        void Bytes(string path, byte[] bytes)
        {
            Gate(); if (bytes.Length > 65536) throw new InvalidOperationException("Synthetic input byte bound64KiB.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); output.Write(bytes);
        }
        string Hash(string path)
        {
            Gate(); using var input = File.OpenRead(path);
            if (input.Length > 65536) throw new InvalidOperationException("Synthetic hash byte bound64KiB.");
            return Convert.ToHexStringLower(SHA256.HashData(input));
        }
        static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        void Write<T>(string path, T value, JsonTypeInfo<T> type)
        {
            Gate(); using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, value, type);
        }
        T Read<T>(string path, JsonTypeInfo<T> type)
        {
            Gate(); using var input = File.OpenRead(path);
            if (input.Length > 65536) throw new InvalidOperationException("Synthetic receipt byte bound64KiB.");
            return JsonSerializer.Deserialize(input, type) ?? throw new InvalidDataException("Typed CLI receipt missing.");
        }
        (string Path, string Sha256)[] Pins(params string[] paths)
        {
            if (paths.Length > 16) throw new InvalidOperationException("Synthetic pin count16.");
            return paths.Select(path => (Path: path, Sha256: Hash(path))).ToArray();
        }
        void Unchanged((string Path, string Sha256)[] pins)
        {
            if (pins.Length > 16) throw new InvalidOperationException("Synthetic readback count16.");
            foreach (var pin in pins) { Gate(); Assert(Hash(pin.Path) == pin.Sha256, "CLI changed a pinned fixture input/cache."); }
        }
        int Invoke<T>(string area, string name, string command, T request, JsonTypeInfo<T> type, string output)
        {
            Gate(); if (++commands > 20) throw new InvalidOperationException("Synthetic CLI invocation count20.");
            var seconds = Math.Min(3, (int)Math.Floor(30 - clock.Elapsed.TotalSeconds));
            if (seconds < 1) throw new TimeoutException("Insufficient whole-second CLI budget.");
            var requestFile = Path.Combine(area, name + "-request.json"); Write(requestFile, request, type);
            var before = Hash(requestFile); var parts = command.Split(' ');
            Assert(parts.Length == 2, "Explicit two-part CLI command required.");
            var exit = ToolingCommand.Run([parts[0], parts[1], "--request", requestFile, "--output", output,
                "--timeout-seconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "--cancel-file", cancelFile]);
            Gate(); Assert(Hash(requestFile) == before, "CLI changed its typed input request."); return exit;
        }
        SourceFixture Source(string area, bool corruptFirst = false)
        {
            Gate(); var metadata = Path.Combine(area, "metadata"); Directory.CreateDirectory(metadata);
            var metadataFile = Path.Combine(metadata, "publisher.json");
            var raw = Encoding.UTF8.GetBytes("{\"source\":\"fixture/data\",\"revision\":\"" + Revision + "\",\"license\":\"cc-by-4.0\",\"synthetic\":true}");
            Bytes(metadataFile, raw);
            var content = "abcdef"u8.ToArray();
            var assets = new[] { "a", "b" }.Select(id => new SourceAcquisitionAsset(id, "fixture/data", "raw/" + id + ".bin",
                "data/" + id + ".bin", "source-train", content.Length, new("sha256", Sha(content)),
                "https://huggingface.co/datasets/fixture/data/resolve/" + Revision + "/data/" + id + ".bin", RangeBytes: 4)).ToArray();
            var plan = new SourceAcquisitionPlan(1, [new("fixture/data", "huggingface-dataset", Revision, "cc-by-4.0",
                [new("publisher.json", raw.Length, Sha(raw))])], assets);
            var planFile = Path.Combine(area, "source-plan.json"); Write(planFile, plan, SourceAcquisitionJsonContext.Default.SourceAcquisitionPlan);
            var request = new SourceAcquisitionRequest(planFile, Hash(planFile), metadata, Path.Combine(area, "cache"), Path.Combine(area, "inner-receipts"));
            var cached = assets.Select(asset => Path.Combine(request.OutputRoot, asset.Path)).ToArray();
            Bytes(cached[0], corruptFirst ? "ghijkl"u8.ToArray() : content); Bytes(cached[1], content);
            return new(request, [planFile, metadataFile, .. cached]);
        }
        ModelFixture Model(string area)
        {
            Gate(); var modelRoot = Path.Combine(area, "model"); Directory.CreateDirectory(modelRoot);
            var names = new[] { "config.json", "tokenizer/tokenizer.json", "encoder.safetensors", "LICENSE" };
            var roles = new[] { ModelAssetRole.Config, ModelAssetRole.Tokenizer, ModelAssetRole.Weights, ModelAssetRole.License };
            byte[][] data = ["{\"synthetic\":\"config\"}\n"u8.ToArray(), "{\"synthetic\":\"tokenizer\"}\n"u8.ToArray(),
                "Synthetic inert weight bytes; no inference.\n"u8.ToArray(), "Synthetic license bytes.\n"u8.ToArray()];
            var assets = names.Select((name, index) => new ModelMirrorAsset(name, roles[index], data[index].Length, "sha256", Sha(data[index]))).ToArray();
            var manifest = new ModelMirrorManifest(1, "http://127.0.0.1:1/models/fixture", Revision, "Apache-2.0", assets);
            var manifestFile = Path.Combine(modelRoot, "input-manifest.json"); Write(manifestFile, manifest, ModelMirrorJsonContext.Default.ModelMirrorManifest);
            var expected = Hash(manifestFile); var cache = Path.Combine(modelRoot, "cache"); var entry = Path.Combine(cache, expected);
            var cachedManifest = Path.Combine(entry, "model-source-manifest.json");
            Bytes(cachedManifest, File.ReadAllBytes(manifestFile));
            var cached = new List<string> { manifestFile, cachedManifest };
            for (var index = 0; index < names.Length; index++)
            {
                Gate(); var path = Path.Combine(entry, names[index]); Bytes(path, data[index]); cached.Add(path);
            }
            var request = new ModelMirrorRequest(1, modelRoot, manifestFile, expected, cache, Path.Combine(modelRoot, "bundle"),
                OperatingSystem.IsWindows() ? "win-x64" : "linux-x64");
            return new(request, cached.ToArray());
        }
        SourceAcquisitionReport SourceReport(string output) => Read(output, SourceAcquisitionJsonContext.Default.SourceAcquisitionReport);
        void SourceLedger(SourceFixture fixture, int complete, int failed)
        {
            var inner = SourceReport(Path.Combine(fixture.Request.ReceiptDirectory, "source-acquisition-report.json"));
            var ledgerPath = Path.Combine(fixture.Request.ReceiptDirectory, "source-ledger.jsonl");
            Gate(); if (new FileInfo(ledgerPath).Length > 65536) throw new InvalidOperationException("Synthetic ledger byte bound64KiB.");
            var lines = File.ReadLines(ledgerPath).Take(3).ToArray(); Assert(lines.Length == 2, "Inner ledger lost planned asset denominator.");
            var entries = lines.Select(line => JsonSerializer.Deserialize(line, SourceAcquisitionJsonContext.Default.SourceAcquisitionEntry)!).ToArray();
            Assert(inner.PlannedAssets == 2 && inner.CompleteAssets == complete && inner.FailedAssets == failed && inner.Entries.Length == 2 &&
                entries.Select(entry => entry.Id).SequenceEqual(new[] { "a", "b" }) && entries.All(entry => entry.Attempts.Length == 0),
                "Cached source ledger counts, identities or zero-HTTP path differ.");
        }

        Case("migration CLI synthetic snapshot preserves exact original failure bytes", () =>
        {
            var area = Area("snapshot-success"); var source = Path.Combine(area, "source"); var file = Path.Combine(source, "failed.json");
            var bytes = "{\"Status\":\"FAIL\",\"RemainingProcesses\":3}\r\n"u8.ToArray(); Bytes(file, bytes); var pins = Pins(file);
            var output = Path.Combine(area, "outer.json");
            Assert(Invoke(area, "snapshot", "task snapshot", new ReceiptSnapshotRequest(source, ["failed.json"]), ReceiptSnapshotJson.Default.ReceiptSnapshotRequest, output) == 0,
                "Snapshot CLI failed to capture a complete named byte set.");
            var report = Read(output, ReceiptSnapshotJson.Default.ReceiptSnapshotReport);
            Assert(report.Status == "PASS_named_small_receipt_snapshot_only" && report.CompletedFiles == 1 && report.TotalBytes == bytes.Length && report.Files[0].Sha256 == Sha(bytes) &&
                Convert.FromBase64String(report.Files[0].Base64).SequenceEqual(bytes) && !report.TrainingAccepted && !report.PublicReleaseAccepted,
                "Snapshot changed raw failure data or promoted collection to business acceptance."); Unchanged(pins);
        });
        Case("migration CLI synthetic snapshot missing second file writes typed partial", () =>
        {
            var area = Area("snapshot-partial"); var source = Path.Combine(area, "source"); var file = Path.Combine(source, "first.txt");
            Bytes(file, "abc"u8.ToArray()); var pins = Pins(file); var output = Path.Combine(area, "outer.json");
            Assert(Invoke(area, "snapshot", "task snapshot", new ReceiptSnapshotRequest(source, ["first.txt", "missing.json"]), ReceiptSnapshotJson.Default.ReceiptSnapshotRequest, output) != 0,
                "Partial snapshot returned success.");
            var report = Read(output, ReceiptSnapshotJson.Default.ReceiptSnapshotReport);
            Assert(report.Status == "FAIL_named_receipt_snapshot_partial" && report.RequestedFiles == 2 && report.CompletedFiles == 1 &&
                report.TotalBytes == 3 && report.Files.Single().Base64 == "YWJj", "Typed partial denominator or bytes were lost."); Unchanged(pins);
        });
        Case("migration CLI synthetic snapshot refuses output under source root", () =>
        {
            var area = Area("snapshot-protected"); var source = Path.Combine(area, "source"); var file = Path.Combine(source, "first.txt");
            Bytes(file, "abc"u8.ToArray()); var pins = Pins(file); var output = Path.Combine(source, "refused.json");
            Assert(Invoke(area, "snapshot", "task snapshot", new ReceiptSnapshotRequest(source, ["first.txt"]), ReceiptSnapshotJson.Default.ReceiptSnapshotRequest, output) != 0 &&
                !Path.Exists(output), "Protected snapshot root received a success or generic failure receipt."); Unchanged(pins);
        });
        Case("migration CLI synthetic card wrong SHA writes zero-request typed failure", () =>
        {
            var area = Area("card-failure"); var card = Path.Combine(area, "card", "README.md"); Bytes(card, "---\nlicense: mit\n---\nSynthetic card.\n"u8.ToArray());
            var pins = Pins(card); var request = new ModelCardCommandRequest(new(1, card, new FileInfo(card).Length, new string('0', 64)), CredentialSource: "none");
            Assert(Hash(card) != request.Validation.ExpectedSha256, "Wrong-pin fixture accidentally matches."); var output = Path.Combine(area, "outer.json");
            Assert(Invoke(area, "card", "publication validate-card", request, MigrationJsonContext.Default.ModelCardCommandRequest, output) != 0, "Wrong card SHA returned success.");
            var report = Read(output, ModelCardValidationJsonContext.Default.ModelCardValidationReport);
            Assert(report.Status == "FAIL" && report.Outcome == "INPUT_REJECTED" && report.Requests == 0 && report.HttpStatusCode is null &&
                !report.ProviderMessagesIncluded, "Invalid card reached hosted HTTP or lost its typed result."); Unchanged(pins);
        });
        Case("migration CLI synthetic card refuses output beside its input", () =>
        {
            var area = Area("card-protected"); var card = Path.Combine(area, "card", "README.md"); Bytes(card, "Synthetic no-network wrong-pin card.\n"u8.ToArray());
            var pins = Pins(card); var request = new ModelCardCommandRequest(new(1, card, new FileInfo(card).Length, new string('0', 64)), CredentialSource: "none");
            var output = Path.Combine(Path.GetDirectoryName(card)!, "refused.json");
            Assert(Invoke(area, "card", "publication validate-card", request, MigrationJsonContext.Default.ModelCardCommandRequest, output) != 0 &&
                !Path.Exists(output), "Protected card directory received a receipt."); Unchanged(pins);
        });
        Case("migration CLI synthetic complete source cache succeeds with full inner ledger", () =>
        {
            var area = Area("source-success"); var fixture = Source(area); var pins = Pins(fixture.Inputs); var output = Path.Combine(area, "outer.json");
            Assert(Invoke(area, "source", "source acquire", new SourceAcquisitionCommandRequest(fixture.Request, CredentialSource: "none"),
                MigrationJsonContext.Default.SourceAcquisitionCommandRequest, output) == 0, "Complete source cache CLI failed.");
            var report = SourceReport(output);
            Assert(report.AllSelectedAssetsComplete && report.AllPlannedAssetsComplete && report.PlannedAssets == 2 && report.CompleteAssets == 2 &&
                report.Entries.All(entry => entry.Status == "CACHED_VERIFIED" && entry.Attempts.Length == 0) &&
                !report.TeacherCalled && !report.ModelTrainingRun && !report.FormalTrainingAdmitted, "Cache path issued HTTP or overclaimed training/admission.");
            SourceLedger(fixture, 2, 0); Unchanged(pins);
        });
        Case("migration CLI synthetic corrupt source cache keeps typed partial and complete ledger", () =>
        {
            var area = Area("source-partial"); var fixture = Source(area, corruptFirst: true); var pins = Pins(fixture.Inputs); var output = Path.Combine(area, "outer.json");
            Assert(Invoke(area, "source", "source acquire", new SourceAcquisitionCommandRequest(fixture.Request, CredentialSource: "none"),
                MigrationJsonContext.Default.SourceAcquisitionCommandRequest, output) != 0, "Corrupt complete cache returned success.");
            var report = SourceReport(output);
            Assert(!report.AllSelectedAssetsComplete && report.PlannedAssets == 2 && report.CompleteAssets == 1 && report.FailedAssets == 1 &&
                report.Entries.Single(entry => entry.Id == "a").Status == "FAILED" && report.Entries.All(entry => entry.Attempts.Length == 0),
                "Corrupt cache lost its full partial denominator or entered a download route.");
            SourceLedger(fixture, 1, 1); Unchanged(pins);
        });
        Case("migration CLI synthetic source protects metadata cache and receipt roots", () =>
        {
            var area = Area("source-protected"); var fixture = Source(area); var pins = Pins(fixture.Inputs);
            Directory.CreateDirectory(fixture.Request.ReceiptDirectory);
            var roots = new[] { fixture.Request.MetadataRoot, fixture.Request.OutputRoot, fixture.Request.ReceiptDirectory };
            for (var index = 0; index < roots.Length; index++)
            {
                Gate(); var output = Path.Combine(roots[index], "refused.json");
                Assert(Invoke(area, "source-" + index, "source acquire", new SourceAcquisitionCommandRequest(fixture.Request, CredentialSource: "none"),
                    MigrationJsonContext.Default.SourceAcquisitionCommandRequest, output) != 0 && !Path.Exists(output),
                    "A protected source root received a receipt."); Unchanged(pins);
            }
        });
        Case("migration CLI synthetic cached model mirror and verify both succeed", () =>
        {
            var area = Area("model-success"); var fixture = Model(area); var pins = Pins(fixture.Inputs); var output = Path.Combine(area, "mirror.json");
            Assert(Invoke(area, "mirror", "model mirror", new ModelMirrorCommandRequest(fixture.Request, CredentialSource: "none"),
                MigrationJsonContext.Default.ModelMirrorCommandRequest, output) == 0, "Cached model mirror CLI failed.");
            var mirrored = Read(output, ModelMirrorJsonContext.Default.ModelMirrorReport);
            Assert(mirrored.Status == "PASS_model_download_cache_bundle_and_complete_manifest_readback" && mirrored.Verification.AllManifestAssetsVerified && mirrored.Assets.Length == 4 && mirrored.Assets.All(asset => asset.Requests == 0 &&
                asset.CacheStatus == "PASS_COMPLETE_CACHE_SHA_READBACK") && mirrored.Verification.Publication == "NOT_RUN" && mirrored.Verification.Quality == "NOT_ASSESSED",
                "Cached model requested HTTP or overclaimed model quality/publication.");
            var verifiedOutput = Path.Combine(area, "verify.json"); var verify = new ModelMirrorVerifyRequest(1, fixture.Request.OutputDirectory,
                fixture.Request.ManifestSha256, fixture.Request.RuntimeIdentifier);
            Assert(Invoke(area, "verify", "model verify", verify, ModelMirrorJsonContext.Default.ModelMirrorVerifyRequest, verifiedOutput) == 0,
                "Actual model verify CLI failed.");
            var verified = Read(verifiedOutput, ModelMirrorJsonContext.Default.ModelMirrorVerification);
            Assert(verified.Status == "PASS_pinned_model_manifest_and_all_asset_bytes_sha256" && verified.AllManifestAssetsVerified && verified.Assets == 4 && verified.ManifestSha256 == fixture.Request.ManifestSha256 &&
                verified.Coverage == "CALLER_PINNED_ASSET_SET_ONLY", "CLI verification lost its pinned asset scope."); Unchanged(pins);
        });
        Case("migration CLI synthetic model roots stay protected and bad asset fails verify", () =>
        {
            var area = Area("model-failure"); var fixture = Model(area); var pins = Pins(fixture.Inputs);
            Assert(Invoke(area, "prepare-mirror", "model mirror", new ModelMirrorCommandRequest(fixture.Request, CredentialSource: "none"),
                MigrationJsonContext.Default.ModelMirrorCommandRequest, Path.Combine(area, "prepared.json")) == 0, "Cached fixture bundle creation failed.");
            var blockedMirror = fixture.Request with { OutputDirectory = Path.Combine(fixture.Request.Root, "blocked-bundle") };
            var blockedMirrorReceipt = Path.Combine(fixture.Request.Root, "refused-mirror.json");
            Assert(Invoke(area, "blocked-mirror", "model mirror", new ModelMirrorCommandRequest(blockedMirror, CredentialSource: "none"),
                MigrationJsonContext.Default.ModelMirrorCommandRequest, blockedMirrorReceipt) != 0 && !Path.Exists(blockedMirrorReceipt) &&
                !Path.Exists(blockedMirror.OutputDirectory), "Protected model root was mutated by mirror dispatch.");
            var verify = new ModelMirrorVerifyRequest(1, fixture.Request.OutputDirectory, fixture.Request.ManifestSha256, fixture.Request.RuntimeIdentifier);
            var blockedVerifyReceipt = Path.Combine(fixture.Request.OutputDirectory, "refused-verify.json");
            Assert(Invoke(area, "blocked-verify", "model verify", verify, ModelMirrorJsonContext.Default.ModelMirrorVerifyRequest, blockedVerifyReceipt) != 0 &&
                !Path.Exists(blockedVerifyReceipt), "Protected model bundle received a verification receipt.");
            var bad = Path.Combine(fixture.Request.OutputDirectory, "config.json"); var changed = File.ReadAllBytes(bad); changed[0] ^= 1;
            File.WriteAllBytes(bad, changed); var badSha = Hash(bad); var badOutput = Path.Combine(area, "bad-verify.json");
            Assert(Invoke(area, "bad-verify", "model verify", verify, ModelMirrorJsonContext.Default.ModelMirrorVerifyRequest, badOutput) != 0,
                "Changed model asset passed actual CLI verification.");
            using var failure = JsonDocument.Parse(File.ReadAllBytes(badOutput));
            Assert(failure.RootElement.GetProperty("Status").GetString() == "FAIL" && Hash(bad) == badSha,
                "Bad asset failure receipt or preservation differs."); Unchanged(pins);
        });
    }

    private sealed record SourceFixture(SourceAcquisitionRequest Request, string[] Inputs);
    private sealed record ModelFixture(ModelMirrorRequest Request, string[] Inputs);
}
