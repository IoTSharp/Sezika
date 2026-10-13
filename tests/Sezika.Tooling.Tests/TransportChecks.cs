using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class TransportChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        Budget Budget() => new(25, null, CancellationToken.None);
        string Fresh(string name) => Path.Combine(root, "transport-" + name);
        string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Reject(Action action)
        {
            var rejected = false;
            try { action(); }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or OperationCanceledException or TimeoutException or HttpRequestException)
            { rejected = true; }
            Assert(rejected, "Transport expected rejection absent.");
        }
        (string Root, string Manifest, AssetFile[] Files) Fixture(string name)
        {
            var folder = Fresh(name); Directory.CreateDirectory(folder);
            var bytes = Encoding.UTF8.GetBytes("Sezika independent asset fixture 中文\n");
            File.WriteAllBytes(Path.Combine(folder, "weights.bin"), bytes);
            File.WriteAllBytes(Path.Combine(folder, "empty.bin"), []);
            var files = new AssetFile[] { new("weights.bin", bytes.Length, Hash(bytes)), new("empty.bin", 0, Hash([])) };
            var manifest = new LogicalManifest(1, "full-corpus-model", "fixture-transport", "PASS_offline_full_corpus_publication_contract",
                "runs/fixture-transport/", files, bytes.Length, Hash(bytes), 2, new('a', 40), new('b', 40), new(true, true, true, true, true, true), true, false, true);
            var path = Fresh(name + ".logical.json");
            AssetTransport.WriteJson(path, manifest, TransportJsonContext.Default.LogicalManifest, Budget());
            return (folder, path, files);
        }
        check("transport tiny logical independent SHA", () =>
        {
            var f = Fixture("tiny"); var verified = AssetTransport.VerifyLogical(f.Root, f.Manifest, Budget());
            Assert(verified.Files.Length == 2 && verified.TotalBytes > 0, "Logical fixture denominator differs.");
        });
        check("transport physical split and logical reconstruction", () =>
        {
            var f = Fixture("split"); var prepared = AssetTransport.Prepare(f.Root, f.Manifest, Fresh("split-output"), 16, Budget());
            var physical = AssetTransport.VerifyPhysical(prepared.Directory, prepared.TransportManifestSha256, prepared.AttributesSha256, Budget());
            Assert(physical.Mappings[0].Parts.Length > 1, "Fixture was not physically chunked.");
            var result = AssetTransport.Reassemble(prepared.Directory, Fresh("reassembled"), prepared.TransportManifestSha256, prepared.AttributesSha256, Budget());
            Assert(result.Files == 2 && result.CompleteOriginalBytesSha256 && File.ReadAllBytes(Path.Combine(result.Directory, "weights.bin")).SequenceEqual(File.ReadAllBytes(Path.Combine(f.Root, "weights.bin"))), "Reassembled disk bytes differ.");
            Reject(() => AssetTransport.Reassemble(prepared.Directory, result.Directory, prepared.TransportManifestSha256, prepared.AttributesSha256, Budget()));
        });
        check("transport physical corruption and unknown inventory rejected", () =>
        {
            var f = Fixture("corrupt"); var prepared = AssetTransport.Prepare(f.Root, f.Manifest, Fresh("corrupt-output"), 16, Budget());
            var manifest = AssetTransport.VerifyPhysical(prepared.Directory, prepared.TransportManifestSha256, prepared.AttributesSha256, Budget());
            File.WriteAllBytes(Path.Combine(prepared.Directory, manifest.Mappings[0].Parts[0].Path), [1, 2]);
            Reject(() => AssetTransport.VerifyPhysical(prepared.Directory, prepared.TransportManifestSha256, prepared.AttributesSha256, Budget()));
            File.WriteAllText(Path.Combine(f.Root, "foreign.txt"), "foreign");
            Reject(() => AssetTransport.VerifyLogical(f.Root, f.Manifest, Budget()));
        });
        check("transport manifest duplicate keys rejected", () =>
        {
            var path = Fresh("duplicate.json"); File.WriteAllText(path, "{\"schema_version\":1,\"schema_version\":1}");
            Reject(() => AssetTransport.ReadJson(path, TransportJsonContext.Default.LogicalManifest, Budget()));
        });
        check("transport path traversal and linked source rejected", () =>
        {
            Reject(() => AssetTransport.AssetPath(root, "../escape", Budget()));
            Reject(() => AssetTransport.AssetPath(root, ".git/config", Budget()));
            var f = Fixture("linked");
            var link = Fresh("source-link"); Directory.CreateSymbolicLink(link, f.Root);
            Reject(() => AssetTransport.VerifyLogical(link, f.Manifest, Budget()));
        });
        check("transport cancellation creates no output", () =>
        {
            var f = Fixture("cancel"); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var target = Fresh("cancelled-output");
            Reject(() => AssetTransport.Prepare(f.Root, f.Manifest, target, 16, new Budget(25, null, cancelled.Token)));
            Assert(!Path.Exists(target), "Cancelled transport created output.");
        });
        LogicalManifest FullPublication(string fixture, string output) => AssetTransport.PreparePublication(Path.Combine(fixture, "package"),
            Path.Combine(fixture, "validation.json"), Path.Combine(fixture, "contract.json"), output, Path.Combine(fixture, "tool"),
            Path.Combine(fixture, "train.tsv"), Path.Combine(fixture, "validate.tsv"), Budget());
        check("transport complete corpus publication frozen independent fixture", () =>
        {
            var f = FrozenCorpusFixture(Fresh("corpus-positive")); var output = Fresh("corpus-prepared");
            var manifest = FullPublication(f, output);
            Assert(manifest.TestOnly && manifest.GlobalUpdates == 4 && manifest.Files.Length >= 27 && !manifest.FormalQuality && manifest.ExperimentalOnly,
                "Synthetic complete-corpus publication boundary differs.");
            var prepared = AssetTransport.Prepare(Path.Combine(output, "run"), Path.Combine(output, "publication-manifest.json"), Fresh("corpus-physical"), 1024, Budget());
            var result = AssetTransport.Reassemble(prepared.Directory, Fresh("corpus-reassembled"), prepared.TransportManifestSha256, prepared.AttributesSha256, Budget());
            Assert(result.CompleteOriginalBytesSha256 && result.Files == manifest.Files.Length, "Publication/transport complete fixture differs.");
        });
        check("transport incomplete epoch and missing optimizer rejected", () =>
        {
            var partial = FrozenCorpusFixture(Fresh("corpus-partial")); var path = Path.Combine(partial, "package/training-report.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"FullCorpusEpochCoverage\":true", "\"FullCorpusEpochCoverage\":false", StringComparison.Ordinal));
            Reject(() => FullPublication(partial, Fresh("corpus-partial-output")));
            var missing = FrozenCorpusFixture(Fresh("corpus-missing")); File.Delete(Path.Combine(missing, "package/checkpoint-0000000004/moments.safetensors"));
            Reject(() => FullPublication(missing, Fresh("corpus-missing-output")));
        });
        check("transport same validation process and failed cleanup rejected", () =>
        {
            var same = FrozenCorpusFixture(Fresh("corpus-same-process")); var path = Path.Combine(same, "validation.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"ProcessId\":101", "\"ProcessId\":100", StringComparison.Ordinal));
            Reject(() => FullPublication(same, Fresh("corpus-same-output")));
            var cleanup = FrozenCorpusFixture(Fresh("corpus-cleanup")); File.WriteAllText(Path.Combine(cleanup, "validate.tsv"), "status\tcompleted\npayload_exit\t0\ncleanup_incomplete\t1\n");
            Reject(() => FullPublication(cleanup, Fresh("corpus-cleanup-output")));
        });
        check("transport source notice and validation rotation rejected", () =>
        {
            var notice = FrozenCorpusFixture(Fresh("corpus-notice"));
            File.WriteAllText(Path.Combine(notice, "package/corpus-provenance/components/root-00/source-notices/UPSTREAM-README.md"), "changed");
            Reject(() => FullPublication(notice, Fresh("corpus-notice-output")));
            var rotation = FrozenCorpusFixture(Fresh("corpus-rotation")); var path = Path.Combine(rotation, "validation.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"CandidateIds\":[\"b\",\"a\"]", "\"CandidateIds\":[\"a\",\"b\"]", StringComparison.Ordinal));
            Reject(() => FullPublication(rotation, Fresh("corpus-rotation-output")));
        });
        check("transport synthetic corpus cannot claim production", () =>
        {
            var f = FrozenCorpusFixture(Fresh("corpus-production-claim")); var path = Path.Combine(f, "contract.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"test_only\":true", "\"test_only\":false", StringComparison.Ordinal));
            Reject(() => FullPublication(f, Fresh("corpus-production-output")));
        });
        check("transport real C# execution receipts bind exact publication PIDs", () =>
        {
            var executable = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_FIXTURE") ??
                throw new InvalidOperationException("Transport C# receipt check requires SEZIKA_PROCESS_FIXTURE native apphost.");
            var broker = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_BROKER");
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            ExecutionResult Execute(string suffix)
            {
                var request = new ExecutionRequest { Executable = executable, ArgumentList = ["success"], WorkingDirectory = Path.GetDirectoryName(executable)!,
                    OutputDirectory = Fresh("publication-native-" + suffix), TimeoutSeconds = 3, CleanupSeconds = 5, BrokerExecutable = broker, MaximumOutputBytes = 65536 };
                var executionResult = ProcessExecutor.ExecuteAsync(request, new Budget(10, null, cancel.Token), cancel.Token).GetAwaiter().GetResult();
                Assert(executionResult.Status == "PASS" && executionResult.ExecutionStatus == "SUCCEEDED" && executionResult.CleanupStatus == "PASS" && executionResult.RootPid is > 0 && executionResult.RemainingProcesses == 0,
                    "Actual C# execution did not produce an acceptable complete receipt.");
                foreach (var identity in executionResult.Processes)
                {
                    var actual = ProcessObserver.Observe(identity.Pid);
                    Assert(actual.State == "Absent" || actual.Started != identity.Started || actual.StartTicks != identity.StartTicks, "Native proof left an owned process alive.");
                }
                return executionResult;
            }
            var training = Execute("training"); var validation = Execute("validation");
            Assert(training.RootPid != validation.RootPid && training.StartedUtc != validation.StartedUtc, "Native execution identities are not independent.");
            var f = FrozenCorpusFixture(Fresh("corpus-native-proofs")); var reportPath = Path.Combine(f, "package/training-report.json");
            File.WriteAllText(reportPath, File.ReadAllText(reportPath).Replace("\"ProcessId\":100", "\"ProcessId\":" + training.RootPid, StringComparison.Ordinal)
                .Replace("2026-10-13T00:00:00Z", training.StartedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            var packagePath = Path.Combine(f, "package/corpus-package.json");
            using (var packageJson = JsonDocument.Parse(File.ReadAllText(packagePath)))
            {
                var oldReportSha = packageJson.RootElement.GetProperty("TrainingReportSha256").GetString()!;
                File.WriteAllText(packagePath, File.ReadAllText(packagePath).Replace(oldReportSha, AssetTransport.FileHash(reportPath, Budget()), StringComparison.Ordinal));
            }
            var validationPath = Path.Combine(f, "validation.json");
            using (var validationJson = JsonDocument.Parse(File.ReadAllText(validationPath)))
            {
                var oldPackageSha = validationJson.RootElement.GetProperty("PackageSha256").GetString()!;
                File.WriteAllText(validationPath, File.ReadAllText(validationPath).Replace(oldPackageSha, AssetTransport.FileHash(packagePath, Budget()), StringComparison.Ordinal)
                    .Replace("\"ProcessId\":101", "\"ProcessId\":" + validation.RootPid, StringComparison.Ordinal)
                    .Replace("2026-10-13T01:00:00Z", validation.StartedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }
            var trainingProof = Path.Combine(training.OutputDirectory, "result.json"); var validationProof = Path.Combine(validation.OutputDirectory, "result.json");
            var result = AssetTransport.PreparePublication(Path.Combine(f, "package"), validationPath, Path.Combine(f, "contract.json"), Fresh("corpus-native-prepared"), Path.Combine(f, "tool"),
                trainingProof, validationProof, Budget());
            Assert(result.TestOnly && result.GlobalUpdates == 4, "Native execution proof incorrectly changed synthetic GPU evidence scope.");
            var wrongProof = Fresh("wrong-native-proof.json");
            File.Copy(trainingProof, wrongProof, overwrite: false);
            Reject(() => AssetTransport.PreparePublication(Path.Combine(f, "package"), validationPath, Path.Combine(f, "contract.json"), Fresh("corpus-wrong-native-proof"), Path.Combine(f, "tool"),
                trainingProof, wrongProof, Budget()));
        });
        var content = Encoding.ASCII.GetBytes("0123456789abcdef");
        Reply Range(Request request, string tag = "\"object-v1\"")
        {
            var range = request.Headers["Range"][6..].Split('-'); var start = int.Parse(range[0], System.Globalization.CultureInfo.InvariantCulture);
            var end = int.Parse(range[1], System.Globalization.CultureInfo.InvariantCulture);
            return new(206, content[start..(end + 1)], new() { ["Content-Range"] = $"bytes {start}-{end}/{content.Length}", ["ETag"] = tag });
        }
        check("transport real loopback 206 range and final disk SHA", () =>
        {
            using var server = new Loopback(request => { Assert(request.Headers.GetValueOrDefault("Authorization") == "Bearer fixture-memory-token", "Private Range auth missing."); return Range(request); }, 4);
            using var http = AssetTransport.CreateClient(false, seconds: 120); using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var report = AssetTransport.DownloadAsync(new(server.Uri + "asset", content.Length, Hash(content), RangeBytes: 4), Fresh("http.bin"), Budget(), cancel.Token, bearerToken: "fixture-memory-token", client: http).GetAwaiter().GetResult();
            Assert(report.Requests == 4 && report.IndependentlyReadBack && File.ReadAllBytes(report.Path).SequenceEqual(content), "Range fixture full bytes differ.");
        });
        check("transport resume requires exact ETag and range offset", () =>
        {
            using var server = new Loopback(request =>
            {
                Assert(request.Headers["If-Range"] == "\"object-v1\"", "If-Range identity omitted."); return Range(request);
            }, 3);
            var target = Fresh("resume.bin"); var spec = new DownloadSpec(server.Uri + "asset", content.Length, Hash(content), "\"object-v1\"", 4);
            File.WriteAllBytes(target + ".sezika-part", content[..4]);
            AssetTransport.WriteJson(target + ".sezika-download.json", new DownloadState(1, spec.Url, spec.Bytes, spec.Sha256, spec.ETag), TransportJsonContext.Default.DownloadState, Budget());
            using var http = AssetTransport.CreateClient(false);
            var result = AssetTransport.DownloadAsync(spec, target, Budget(), CancellationToken.None, client: http).GetAwaiter().GetResult();
            Assert(result.ResumedBytes == 4 && result.Requests == 3, "Resume byte denominator differs.");
        });
        check("transport mismatching ETag leaves partial intact", () =>
        {
            using var server = new Loopback(request => Range(request, "\"changed\""), 1);
            var target = Fresh("changed.bin"); var spec = new DownloadSpec(server.Uri + "asset", content.Length, Hash(content), "\"object-v1\"", 4);
            File.WriteAllBytes(target + ".sezika-part", content[..4]);
            AssetTransport.WriteJson(target + ".sezika-download.json", new DownloadState(1, spec.Url, spec.Bytes, spec.Sha256, spec.ETag), TransportJsonContext.Default.DownloadState, Budget());
            using var http = AssetTransport.CreateClient(false);
            Reject(() => AssetTransport.DownloadAsync(spec, target, Budget(), CancellationToken.None, client: http).GetAwaiter().GetResult());
            Assert(new FileInfo(target + ".sezika-part").Length == 4 && !File.Exists(target), "Changed object appended bytes.");
        });
        check("transport ignored Range and wrong Content-Range rejected", () =>
        {
            using var ignored = new Loopback(_ => new(200, content, new() { ["ETag"] = "\"v1\"" }), 1);
            using var http = AssetTransport.CreateClient(false);
            Reject(() => AssetTransport.DownloadAsync(new(ignored.Uri + "asset", content.Length, Hash(content), RangeBytes: 4), Fresh("ignored.bin"), Budget(), CancellationToken.None, client: http).GetAwaiter().GetResult());
            using var wrong = new Loopback(_ => new(206, content[..4], new() { ["ETag"] = "\"v1\"", ["Content-Range"] = "bytes 1-4/16" }), 1);
            Reject(() => AssetTransport.DownloadAsync(new(wrong.Uri + "asset", content.Length, Hash(content), RangeBytes: 4), Fresh("wrong-range.bin"), Budget(), CancellationToken.None, client: http).GetAwaiter().GetResult());
        });
        check("transport final SHA mismatch never promotes file", () =>
        {
            using var server = new Loopback(request => Range(request), 1); using var http = AssetTransport.CreateClient(false);
            var target = Fresh("wrong-sha.bin");
            Reject(() => AssetTransport.DownloadAsync(new(server.Uri + "asset", content.Length, new('0', 64), RangeBytes: 16), target, Budget(), CancellationToken.None, client: http).GetAwaiter().GetResult());
            Assert(!File.Exists(target) && File.Exists(target + ".sezika-part"), "Bad SHA file promoted or failure evidence lost.");
        });
        check("transport truncated range can resume with pinned identity", () =>
        {
            var first = true;
            using var server = new Loopback(request =>
            {
                var response = Range(request);
                if (!first) return response;
                first = false; return response with { Body = response.Body[..2], DeclaredLength = response.Body.Length };
            }, 4);
            using var http = AssetTransport.CreateClient(false); var target = Fresh("truncated.bin");
            var spec = new DownloadSpec(server.Uri + "asset", content.Length, Hash(content), RangeBytes: 8);
            Reject(() => AssetTransport.DownloadAsync(spec, target, Budget(), CancellationToken.None, client: http).GetAwaiter().GetResult());
            var result = AssetTransport.DownloadAsync(spec, target, Budget(), CancellationToken.None, client: http).GetAwaiter().GetResult();
            Assert(result.ResumedBytes == 2 && result.IndependentlyReadBack, "Truncated request did not resume exact bytes.");
        });
        check("transport public preview rejects detailed private fields", () =>
        {
            var folder = Fresh("public"); Directory.CreateDirectory(folder); var bytes = Encoding.UTF8.GetBytes("{\"nested\":{\"RecordId\":\"private-id\"}}");
            File.WriteAllBytes(Path.Combine(folder, "report.json"), bytes);
            var plan = new PublicationPlan(1, "huggingface", "IoTSharp/fixture", "main", "public", "model", "fixture", new('a', 40), new('b', 40), 1024,
                [new("report.json", bytes.Length, Hash(bytes))]);
            Reject(() => AssetTransport.Preview(folder, plan, Budget()));
            Reject(() => AssetTransport.Preview(folder, plan with { Kind = "dataset" }, Budget()));
        });
        check("transport public tokenizer accepts large vocab with ordinary token names", () =>
        {
            var folder = Fresh("public-tokenizer"); Directory.CreateDirectory(folder); var path = Path.Combine(folder, "tokenizer.json");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var json = new Utf8JsonWriter(output))
            {
                json.WriteStartObject(); json.WriteString("version", "1.0"); json.WriteStartObject("model"); json.WriteString("type", "WordLevel"); json.WriteStartObject("vocab");
                json.WriteNumber("target", 0); json.WriteNumber("token", 1); var padding = new string('x', 1024);
                for (var item = 2; item < 17000; item++)
                {
                    if (watch.Elapsed.TotalSeconds >= 10) throw new TimeoutException("Tokenizer fixture generation deadline.");
                    json.WriteNumber("word-" + item + padding, item);
                }
                json.WriteEndObject(); json.WriteEndObject(); json.WriteEndObject();
            }
            var file = new AssetFile("tokenizer.json", new FileInfo(path).Length, AssetTransport.FileHash(path, Budget()));
            Assert(file.Bytes > 16 * 1024 * 1024, "Large tokenizer fixture does not cover the old public cap.");
            var plan = new PublicationPlan(1, "huggingface", "IoTSharp/fixture", "main", "public", "model", "fixture", new('a', 40), new('b', 40), 64 * 1024 * 1024, [file]);
            Assert(AssetTransport.Preview(folder, plan, Budget()).PublicContentReviewed, "Normal large tokenizer rejected as detailed private data.");
            var malicious = Encoding.UTF8.GetBytes("{\"model\":{\"type\":\"WordLevel\",\"vocab\":{\"word\":0}},\"Rows\":[{\"RecordId\":\"private\"}]}");
            File.WriteAllBytes(path, malicious);
            Reject(() => AssetTransport.Preview(folder, plan with { Files = [new("tokenizer.json", malicious.Length, Hash(malicious))] }, Budget()));
        });
        check("transport frozen preview authorization cannot drift", () =>
        {
            var f = Fixture("approval"); var plan = new PublicationPlan(1, "moark", "maikebing/fixture", "master", "private", "model", "fixture", new('a', 40), new('b', 40), 1024, f.Files);
            var preview = AssetTransport.Preview(f.Root, plan, Budget());
            var authorization = new PublicationAuthorization(preview.PlanSha256, plan.Repository, plan.ExpectedRef, plan.Visibility, "offline-fixture", DateTimeOffset.UtcNow);
            AssetTransport.ValidateAuthorization(preview, authorization, Budget());
            Reject(() => AssetTransport.ValidateAuthorization(preview, authorization with { Visibility = "public" }, Budget()));
            Reject(() => AssetTransport.ValidateAuthorization(preview, authorization with { PlanSha256 = new('0', 64) }, Budget()));
            Assert(preview.Upload == "NOT_RUN" && preview.RefPublication == "NOT_RUN", "Preview falsely inferred remote writes.");
        });
        void UploadFixture(string provider, bool multipart, bool resume = false, bool rejectPrior = false)
        {
            var suffix = provider + multipart + resume + rejectPrior;
            var folder = Fresh("upload-" + suffix); Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, "model.bin"), content);
            var file = new AssetFile("model.bin", content.Length, Hash(content)); var plan = new PublicationPlan(1, provider, "owner/fixture", "main", "private", "model", "fixture", new('a', 40), new('b', 40), 1024, [file]);
            var preview = AssetTransport.Preview(folder, plan, Budget());
            var approval = new PublicationAuthorization(preview.PlanSha256, plan.Repository, plan.ExpectedRef, "private", "offline-fixture", DateTimeOffset.UtcNow);
            var uploaded = false; var received = new List<byte>(); var sessionParts = 0; var sessionBodies = new Dictionary<int, byte[]>(); var completionAttempts = 0;
            string? resumePath = null;
            if (resume)
            {
                resumePath = Fresh("resume-candidate-" + suffix + ".json");
                AssetTransport.WriteJson(resumePath, new MultipartResumeState(1, plan.Repository, file.Sha256, file.Bytes, 4, new string('d', 64),
                    [new(1, "\"prior-session\"", 4, Hash(content[..4]))], "PART_ACKNOWLEDGED"), TransportJsonContext.Default.MultipartResumeState, Budget());
                if (!rejectPrior) sessionBodies.Add(1, content[..4]);
            }
            Loopback? server = null;
            Reply Json(string json) => new(200, Encoding.UTF8.GetBytes(json), new() { ["Content-Type"] = "application/json" });
            server = new Loopback(request =>
            {
                if (request.Target.EndsWith("/batch", StringComparison.Ordinal))
                {
                    var expectedAuth = provider == "moark" ? "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("fixture-user:offline-dummy")) : "Bearer offline-dummy";
                    Assert(request.Headers.GetValueOrDefault("Authorization") == expectedAuth && request.Headers.GetValueOrDefault("Content-Type") == "application/vnd.git-lfs+json",
                        "LFS account authentication scope or MIME differs.");
                    using var doc = JsonDocument.Parse(request.Body);
                    Assert(doc.RootElement.GetProperty("objects")[0].GetProperty("oid").GetString() == file.Sha256, "LFS OID instruction differs.");
                    if (uploaded) return Json($"{{\"objects\":[{{\"oid\":\"{file.Sha256}\",\"size\":16}}]}}");
                    var header = multipart ? $",\"header\":{{\"chunk_size\":\"4\",\"1\":\"{server!.Uri}part/1\",\"2\":\"{server.Uri}part/2\",\"3\":\"{server.Uri}part/3\",\"4\":\"{server.Uri}part/4\"}}" : "";
                    return Json($"{{\"transfer\":\"{(multipart ? "multipart" : "basic")}\",\"objects\":[{{\"oid\":\"{file.Sha256}\",\"size\":16,\"actions\":{{\"upload\":{{\"href\":\"{server!.Uri}upload\"{header}}}}}}}]}}");
                }
                if (request.Target.StartsWith("/part/", StringComparison.Ordinal))
                { Assert(!request.Headers.ContainsKey("Authorization"), "Account credential leaked to signed part URL."); var number = int.Parse(request.Target[6..], System.Globalization.CultureInfo.InvariantCulture); sessionBodies[number] = request.Body; sessionParts++; return new(200, [], new() { ["ETag"] = "\"part-" + number + "\"" }); }
                if (request.Target == "/upload")
                {
                    Assert(!request.Headers.ContainsKey("Authorization"), "Account credential leaked to signed upload/completion URL.");
                    if (multipart)
                    {
                        using var complete = JsonDocument.Parse(request.Body);
                        Assert(complete.RootElement.GetProperty("parts")[0].GetProperty("partNumber").GetInt32() == 1, "Multipart completion ordering differs.");
                        completionAttempts++;
                        if (rejectPrior && completionAttempts == 1) return new(400, [], new());
                        Assert(sessionBodies.Count == 4, "Multipart current session misses part bytes.");
                        foreach (var part in sessionBodies.OrderBy(p => p.Key)) received.AddRange(part.Value);
                    }
                    else received.AddRange(request.Body);
                    Assert(received.SequenceEqual(content), "Uploaded actual loopback bytes differ."); uploaded = true; return Json("{}");
                }
                if (request.Target.Contains("/revision/", StringComparison.Ordinal)) return Json("{\"sha\":\"" + plan.ExpectedRef + "\",\"private\":true}");
                if (request.Target.Contains("/commit/", StringComparison.Ordinal))
                {
                    var body = Encoding.UTF8.GetString(request.Body); Assert(body.Contains("parentCommit", StringComparison.Ordinal) && body.Contains("lfsFile", StringComparison.Ordinal), "HF commit protocol differs.");
                    var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    Assert(lines.Length == 2, "HF body is not exact single-line NDJSON records.");
                    foreach (var line in lines) { using var item = JsonDocument.Parse(line); Assert(item.RootElement.ValueKind == JsonValueKind.Object, "NDJSON line invalid."); }
                    return Json("{\"commitOid\":\"" + new string('c', 40) + "\"}");
                }
                throw new InvalidDataException("Unexpected loopback protocol request.");
            }, 16);
            using (server)
            using (var http = AssetTransport.CreateClient(false))
            {
                var auth = provider == "moark" ? new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("fixture-user:offline-dummy"))) : null;
                var result = AssetTransport.UploadLfsObjectAsync(preview, approval, folder, file, new Uri(server.Uri), http, provider == "moark" ? null : "offline-dummy", Budget(), CancellationToken.None,
                    resumePath, resume ? Fresh("resume-checkpoints-" + suffix) : null, auth).GetAwaiter().GetResult();
                Assert(result.Parts == (multipart ? resume && !rejectPrior ? 3 : 4 : 1) && result.ServerObjectReadBack && result.RefPublication == "NOT_RUN", "Object upload/ref gates mixed.");
                if (resume)
                {
                    Assert(result.PriorCandidateParts == 1 && result.ReplacedPriorParts == (rejectPrior ? 1 : 0) && File.Exists(result.ResumeCheckpoint), "Multipart recovery evidence differs.");
                    var snapshot = File.ReadAllText(result.ResumeCheckpoint!); Assert(!snapshot.Contains(server.Uri, StringComparison.Ordinal) && !snapshot.Contains("offline-dummy", StringComparison.Ordinal), "Signed URL or token persisted in recovery snapshot.");
                }
                if (provider == "huggingface")
                {
                    var commit = AssetTransport.CommitHuggingFaceAsync(preview, approval, folder, new Uri(server.Uri), http, "offline-dummy", Budget(), CancellationToken.None).GetAwaiter().GetResult();
                    Assert(commit.Commit == new string('c', 40) && commit.IndependentReadback == "NOT_RUN", "HF commit evidence differs.");
                }
            }
        }
        check("transport MoArk real loopback LFS basic protocol", () => UploadFixture("moark", false));
        check("transport HF real loopback multipart and parent-bound commit", () => UploadFixture("huggingface", true));
        check("transport multipart old ETag candidate accepted by current session", () => UploadFixture("huggingface", true, true));
        check("transport multipart old session rejection reuploads only prior parts", () => UploadFixture("huggingface", true, true, true));
    }

    private static string FrozenCorpusFixture(string root)
    {
        // Frozen synthetic bytes from the independently authored legacy fixture; never GPU output.
        Directory.CreateDirectory(root);
        var files = new (string Name, string Base64)[]
        {
            ("contract.json", "eyJzY2hlbWFfdmVyc2lvbiI6MSwicmVhZHkiOnRydWUsInRlc3Rfb25seSI6dHJ1ZSwicnVuX2lkIjoiZml4dHVyZS1mdWxsLWNvcnB1cyIsInNvdXJjZV9jb21taXQiOiIxMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExIiwib3JpZ2luYWxfZGF0YXNldF9jb21taXQiOiIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyIiwicHVibGlzaGVkX2RhdGFzZXRfY29tbWl0IjoiNDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NCIsIm1vZGVsX2NvbW1pdCI6IjMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMiLCJ0cmFpbl9yZWNvcmRzIjo0LCJkZXZlbG9wbWVudF9yZWNvcmRzIjoyLCJzb3VyY2VfY291bnQiOjIsInBoeXNpY2FsX3RyYWluX3NvdXJjZV9jb3VudCI6MiwiZXBvY2hzIjoxLCJjb3JwdXNfbWFuaWZlc3Rfc2hhMjU2IjoiYjMyZjAyNjQ1M2UwMmJhNDE1NTQ4NzM3OGQ1YTNiNmViZTBmMTk4ZWFjZjkzNWE2NjViYjFjN2ZjZDFjNTkyZSIsImNvcnB1c19iaW5kaW5nc19zaGEyNTYiOiI5ODMzZjExN2IzODgyNjdkMzcwOGE5MjBmYWYyOGFiYjY3MTE5YWRiYTg4ZmFmNTlhNjA5MzJlNmIzNzE5NjRjIiwicGxhbl9zaGEyNTYiOiIzZDhjZGRjODEwYjFkYTVhYjcyN2E2NThiZWFmNTMxODM1NTM2ZTI1ZjNkMGMyM2QzM2M2ZmQ4MzM0NDUyZDU2IiwiYmFzZV9lbmNvZGVyX3NoYTI1NiI6ImJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmIiLCJ0b2tlbml6ZXJfc2hhMjU2IjoiZGY0YjE0NDYzYWIwMzcxODMxZTA2ZGNmNjVmNTgwNjllOWVmODdmOGJkNzA4NmUxYjUzZjNjMDczMGYwN2Q5MCIsImJhY2t3YXJkX21hbmlmZXN0X3NoYTI1NiI6IkNDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0MiLCJsZW5ndGhfcmVwb3J0X3NoYTI1NiI6IjUxZGFiZGRjNjRjOTE0ZjhjZDE2YjQxMmU5M2NmZmEyNGZmZTI0ZjUzOTczZGJmZjM1MDI2NTdjNjRkYzNjZmEiLCJ0cmFpbmluZ19ncHVfdXVpZCI6ImFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhIiwidmFsaWRhdGlvbl9ncHVfdXVpZCI6ImFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhIiwiYmFzZV9maWxlcyI6W3sicGF0aCI6IkxJQ0VOU0UtQ09NUE9ORU5UUy5tZCIsImJ5dGVzIjozMSwic2hhMjU2IjoiZGU3NGRmYTYxYmRlOWVlMDcwMTJhZjRiMzk5M2MwMmI0NDI5NWY4N2Q3OTEzMTM0YWVkY2E1YjdjYjY3NjhmZiJ9LHsicGF0aCI6ImxpY2Vuc2UtcHJvdmVuYW5jZS5qc29uIiwiYnl0ZXMiOjMxLCJzaGEyNTYiOiJkZTc0ZGZhNjFiZGU5ZWUwNzAxMmFmNGIzOTkzYzAyYjQ0Mjk1Zjg3ZDc5MTMxMzRhZWRjYTViN2NiNjc2OGZmIn0seyJwYXRoIjoibm90aWNlcy9iYXNlLU5PVElDRS50eHQiLCJieXRlcyI6MzEsInNoYTI1NiI6ImRlNzRkZmE2MWJkZTllZTA3MDEyYWY0YjM5OTNjMDJiNDQyOTVmODdkNzkxMzEzNGFlZGNhNWI3Y2I2NzY4ZmYifV0sImNvZGVfbm90aWNlcyI6eyJjb2RlLUxJQ0VOU0UudHh0IjoiZWZlMjYyMTc2M2RjMWIyNzUwM2UxZTJlYmJkYjdkNWZkMTM1NTA1ZTQ4ZTdmN2M2ZTQzYjYzOGM4ZDBiNzEzZCIsImNvZGUtTk9USUNFLnR4dCI6IjllMWQ5NTZhMDhjYWU5MzEwZmEyYWYzYTRjZGYzYzRkZjQ0OGUxYWEzZWEyODE0YmFlZTFiNjA3Y2IwOGMyMTcifSwicGxhbl9zZXR0aW5ncyI6eyJTZWVkIjoxNywiTWF4aW11bVRva2VucyI6OCwiQ2hlY2twb2ludEV2ZXJ5IjoyLCJWYWxpZGF0aW9uUGVyU291cmNlIjoxLCJFbmNvZGVyUmF0ZSI6MWUtMDUsIkhlYWRSYXRlIjowLjAwMSwiV2VpZ2h0RGVjYXkiOjAuMDEsIk1heGltdW1Pd25lZEJ5dGVzIjoxMDQ4NTc2fSwibW9kZWxfY29uZmlnIjp7IlZvY2FidWxhcnlTaXplIjo4LCJIaWRkZW5TaXplIjo0LCJJbnRlcm1lZGlhdGVTaXplIjo2LCJMYXllckNvdW50IjoyLCJIZWFkQ291bnQiOjIsIk1heFRva2VucyI6OCwiR2xvYmFsQXR0ZW50aW9uRXZlcnkiOjIsIkxvY2FsQXR0ZW50aW9uIjoyLCJHbG9iYWxSb3BlVGhldGEiOjE2MDAwMCwiTG9jYWxSb3BlVGhldGEiOjE2MDAwMCwiTm9ybUVwc2lsb24iOjFlLTA1fX0="),
            ("validation.json", "eyJTdGF0dXMiOiJQQVNTX25ld19wcm9jZXNzX3NhdmVkX2NvcnB1c19HUFVfZm9yd2FyZF92YWxpZGF0aW9uX29ubHkiLCJSdW5JZCI6ImZpeHR1cmUtZnVsbC1jb3JwdXMiLCJQYWNrYWdlU2hhMjU2IjoiZDMwZDNkMDk2ODMzNGU0NjliYTcyMDdkY2IxMjk1NTYxOTQ4Y2VmZjYwODE5YjFiMzFmMGI0MGRjZTc2ZmY3ZiIsIklucHV0cyI6eyJQbGFuU2hhMjU2IjoiM2Q4Y2RkYzgxMGIxZGE1YWI3MjdhNjU4YmVhZjUzMTgzNTUzNmUyNWYzZDBjMjNkMzNjNmZkODMzNDQ1MmQ1NiIsIkRhdGFNYW5pZmVzdFNoYTI1NiI6ImIzMmYwMjY0NTNlMDJiYTQxNTU0ODczNzhkNWEzYjZlYmUwZjE5OGVhY2Y5MzVhNjY1YmIxYzdmY2QxYzU5MmUiLCJSZWNvcmRzU2hhMjU2IjoiZjZiYmRjNjA1NjcwNzllMTJlMDVmMjk2ZTFlZjE1MWQxZTc1MGNmYWU5YTMzYTk1ZjdkNDE1MjQzMzUwM2YxNCIsIkJhc2VFbmNvZGVyU2hhMjU2IjoiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYiIsIlRva2VuaXplclNoYTI1NiI6ImRmNGIxNDQ2M2FiMDM3MTgzMWUwNmRjZjY1ZjU4MDY5ZTllZjg3ZjhiZDcwODZlMWI1M2YzYzA3MzBmMDdkOTAiLCJPcmRlclNoYTI1NiI6IjllMDI0YmZjYzdlMmQwMjJlNWYzZGYwN2I2M2RlMzVkMzhhN2ZiYmQyY2QzYzVhNTg2YzIyMmVlOThiMDM5OWYiLCJCYWNrd2FyZE1hbmlmZXN0U2hhMjU2IjoiQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQyJ9LCJHcHVVdWlkIjoiYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWEiLCJQcm9jZXNzSWQiOjEwMSwiUHJvY2Vzc1N0YXJ0VXRjIjoiMjAyNi0xMC0xM1QwMTowMDowMFoiLCJPcmlnaW5hbFJvd3MiOjQsIlJvdGF0ZWRSb3dzIjoyLCJPcmlnaW5hbFJvd3NCeVNvdXJjZUFuZFNwbGl0Ijp7Im9uZXx0cmFpbiI6MSwib25lfGRldmVsb3BtZW50IjoxLCJ0d298dHJhaW4iOjEsInR3b3xkZXZlbG9wbWVudCI6MX0sIlJvd3MiOlt7IlJlY29yZElkIjoib25lLXRyYWluIiwiU3BsaXQiOiJ0cmFpbiIsIlR5cGUiOiJDaG9pY2UiLCJSb3RhdGVkIjpmYWxzZSwiQ2FuZGlkYXRlSWRzIjpbImEiLCJiIl0sIlRhcmdldCI6WzAuMCwxLjBdLCJQcm9iYWJpbGl0eSI6WzAuMjUsMC43NV0sIkxvc3MiOjAuMjg3NjgyMDcyNDUxNzgwOSwiUHJlZGljdGVkQ2FuZGlkYXRlSWQiOiJiIiwiVGVhY2hlckFyZ21heENhbmRpZGF0ZUlkIjoiYiJ9LHsiUmVjb3JkSWQiOiJvbmUtdHJhaW4iLCJTcGxpdCI6InRyYWluIiwiVHlwZSI6IkNob2ljZSIsIlJvdGF0ZWQiOnRydWUsIkNhbmRpZGF0ZUlkcyI6WyJiIiwiYSJdLCJUYXJnZXQiOlsxLjAsMC4wXSwiUHJvYmFiaWxpdHkiOlswLjc1LDAuMjVdLCJMb3NzIjowLjI4NzY4MjA3MjQ1MTc4MDksIlByZWRpY3RlZENhbmRpZGF0ZUlkIjoiYiIsIlRlYWNoZXJBcmdtYXhDYW5kaWRhdGVJZCI6ImIifSx7IlJlY29yZElkIjoib25lLWRldmVsb3BtZW50IiwiU3BsaXQiOiJkZXZlbG9wbWVudCIsIlR5cGUiOiJCb29sZWFuIiwiUm90YXRlZCI6ZmFsc2UsIkNhbmRpZGF0ZUlkcyI6WyJhIiwiYiJdLCJUYXJnZXQiOlswLjAsMS4wXSwiUHJvYmFiaWxpdHkiOlswLjI1LDAuNzVdLCJMb3NzIjowLjI4NzY4MjA3MjQ1MTc4MDksIlByZWRpY3RlZENhbmRpZGF0ZUlkIjoiYiIsIlRlYWNoZXJBcmdtYXhDYW5kaWRhdGVJZCI6ImIifSx7IlJlY29yZElkIjoidHdvLXRyYWluIiwiU3BsaXQiOiJ0cmFpbiIsIlR5cGUiOiJDaG9pY2UiLCJSb3RhdGVkIjpmYWxzZSwiQ2FuZGlkYXRlSWRzIjpbImEiLCJiIl0sIlRhcmdldCI6WzAuMCwxLjBdLCJQcm9iYWJpbGl0eSI6WzAuMjUsMC43NV0sIkxvc3MiOjAuMjg3NjgyMDcyNDUxNzgwOSwiUHJlZGljdGVkQ2FuZGlkYXRlSWQiOiJiIiwiVGVhY2hlckFyZ21heENhbmRpZGF0ZUlkIjoiYiJ9LHsiUmVjb3JkSWQiOiJ0d28tdHJhaW4iLCJTcGxpdCI6InRyYWluIiwiVHlwZSI6IkNob2ljZSIsIlJvdGF0ZWQiOnRydWUsIkNhbmRpZGF0ZUlkcyI6WyJiIiwiYSJdLCJUYXJnZXQiOlsxLjAsMC4wXSwiUHJvYmFiaWxpdHkiOlswLjc1LDAuMjVdLCJMb3NzIjowLjI4NzY4MjA3MjQ1MTc4MDksIlByZWRpY3RlZENhbmRpZGF0ZUlkIjoiYiIsIlRlYWNoZXJBcmdtYXhDYW5kaWRhdGVJZCI6ImIifSx7IlJlY29yZElkIjoidHdvLWRldmVsb3BtZW50IiwiU3BsaXQiOiJkZXZlbG9wbWVudCIsIlR5cGUiOiJCb29sZWFuIiwiUm90YXRlZCI6ZmFsc2UsIkNhbmRpZGF0ZUlkcyI6WyJhIiwiYiJdLCJUYXJnZXQiOlswLjAsMS4wXSwiUHJvYmFiaWxpdHkiOlswLjI1LDAuNzVdLCJMb3NzIjowLjI4NzY4MjA3MjQ1MTc4MDksIlByZWRpY3RlZENhbmRpZGF0ZUlkIjoiYiIsIlRlYWNoZXJBcmdtYXhDYW5kaWRhdGVJZCI6ImIifV0sIlBlYWtPd25lZEJ5dGVzIjoxLCJRdWFsaXR5QWNjZXB0ZWQiOmZhbHNlLCJDYWxpYnJhdGVkIjpmYWxzZX0="),
            ("train.tsv", "c3RhdHVzCWNvbXBsZXRlZA0KcGF5bG9hZF9leGl0CTANCmZpbmFsX2NoZWNrX2NvbXBsZXRlCTENCmNsZWFudXBfaW5jb21wbGV0ZQkwDQp2ZXJpZmllZF9saXZlX2NvdW50CTANCnVua25vd25fY291bnQJMA0KdW5jaGVja2VkX2NvdW50CTANCmNsZWFudXBfYnVkZ2V0X2V4aGF1c3RlZAkwDQo="),
            ("validate.tsv", "c3RhdHVzCWNvbXBsZXRlZA0KcGF5bG9hZF9leGl0CTANCmZpbmFsX2NoZWNrX2NvbXBsZXRlCTENCmNsZWFudXBfaW5jb21wbGV0ZQkwDQp2ZXJpZmllZF9saXZlX2NvdW50CTANCnVua25vd25fY291bnQJMA0KdW5jaGVja2VkX2NvdW50CTANCmNsZWFudXBfYnVkZ2V0X2V4aGF1c3RlZAkwDQo="),
            ("tool/code-NOTICE.txt", "U3ludGhldGljIGNvZGUgbm90aWNlIGNvZGUtTk9USUNFLnR4dA0K"),
            ("tool/code-LICENSE.txt", "U3ludGhldGljIGNvZGUgbm90aWNlIGNvZGUtTElDRU5TRS50eHQNCg=="),
            ("package/corpus-package.json", "eyJTY2hlbWFWZXJzaW9uIjoxLCJSdW5JZCI6ImZpeHR1cmUtZnVsbC1jb3JwdXMiLCJNZXRob2QiOiJmdWxsX2VuY29kZXJfYWRhbXdfZnAzMl9zdHJlYW1lZF9jb3JwdXMiLCJTb3VyY2VDb21taXQiOiIxMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExIiwiRGF0YXNldENvbW1pdCI6IjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIiLCJNb2RlbENvbW1pdCI6IjMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMiLCJJbnB1dHMiOnsiUGxhblNoYTI1NiI6IjNkOGNkZGM4MTBiMWRhNWFiNzI3YTY1OGJlYWY1MzE4MzU1MzZlMjVmM2QwYzIzZDMzYzZmZDgzMzQ0NTJkNTYiLCJEYXRhTWFuaWZlc3RTaGEyNTYiOiJiMzJmMDI2NDUzZTAyYmE0MTU1NDg3Mzc4ZDVhM2I2ZWJlMGYxOThlYWNmOTM1YTY2NWJiMWM3ZmNkMWM1OTJlIiwiUmVjb3Jkc1NoYTI1NiI6ImY2YmJkYzYwNTY3MDc5ZTEyZTA1ZjI5NmUxZWYxNTFkMWU3NTBjZmFlOWEzM2E5NWY3ZDQxNTI0MzM1MDNmMTQiLCJCYXNlRW5jb2RlclNoYTI1NiI6ImJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmIiLCJUb2tlbml6ZXJTaGEyNTYiOiJkZjRiMTQ0NjNhYjAzNzE4MzFlMDZkY2Y2NWY1ODA2OWU5ZWY4N2Y4YmQ3MDg2ZTFiNTNmM2MwNzMwZjA3ZDkwIiwiT3JkZXJTaGEyNTYiOiI5ZTAyNGJmY2M3ZTJkMDIyZTVmM2RmMDdiNjNkZTM1ZDM4YTdmYmJkMmNkM2M1YTU4NmMyMjJlZTk4YjAzOTlmIiwiQmFja3dhcmRNYW5pZmVzdFNoYTI1NiI6IkNDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0MifSwiQ29uZmlnIjp7IlZvY2FidWxhcnlTaXplIjo4LCJIaWRkZW5TaXplIjo0LCJJbnRlcm1lZGlhdGVTaXplIjo2LCJMYXllckNvdW50IjoyLCJIZWFkQ291bnQiOjIsIk1heFRva2VucyI6OCwiR2xvYmFsQXR0ZW50aW9uRXZlcnkiOjIsIkxvY2FsQXR0ZW50aW9uIjoyLCJHbG9iYWxSb3BlVGhldGEiOjE2MDAwMCwiTG9jYWxSb3BlVGhldGEiOjE2MDAwMCwiTm9ybUVwc2lsb24iOjFlLTA1fSwiQ29tcGxldGVkVXBkYXRlcyI6NCwiV2VpZ2h0c1NoYTI1NiI6IjhmNzE1MTU3YTFkMGU4ZDI1Yjg0MDYxMDFlY2FiYTBhNzRkNTk4MzcwZDllYWExY2U5YTdkYWE0MWU2NWMwMjMiLCJUb2tlbml6ZXJTaGEyNTYiOiJkZjRiMTQ0NjNhYjAzNzE4MzFlMDZkY2Y2NWY1ODA2OWU5ZWY4N2Y4YmQ3MDg2ZTFiNTNmM2MwNzMwZjA3ZDkwIiwiQ2hlY2twb2ludFJlY2VpcHRTaGEyNTYiOiI4MTZhZTk1OWQ4ZmQ4NTFmODM1NzEzOTNjN2MyODE5ZmUyNGQ3OTBhZjcwZjM5YmY1YmI2ZjA3OTYxY2FlYThlIiwiVHJhaW5pbmdSZXBvcnRTaGEyNTYiOiI3OGIwYTI2NmE4YzhhNTAzZmMwMmI4NWZkNmE4ZGUxMTI2M2RjNjY5M2EyNDIyNGIyOTQ2YmNmNDE5ZTFlZWRjIiwiQ29ycHVzTWFuaWZlc3RTaGEyNTYiOiJiMzJmMDI2NDUzZTAyYmE0MTU1NDg3Mzc4ZDVhM2I2ZWJlMGYxOThlYWNmOTM1YTY2NWJiMWM3ZmNkMWM1OTJlIiwiQ29ycHVzQmluZGluZ3NTaGEyNTYiOiI5ODMzZjExN2IzODgyNjdkMzcwOGE5MjBmYWYyOGFiYjY3MTE5YWRiYTg4ZmFmNTlhNjA5MzJlNmIzNzE5NjRjIiwiTGljZW5zZSI6InVua25vd25fb3ZlcmFsbF9jb21wb25lbnRfc3BlY2lmaWMiLCJDYWxpYnJhdGlvblN0YXR1cyI6InVuY2FsaWJyYXRlZCIsIlF1YWxpdHlBY2NlcHRlZCI6ZmFsc2UsIkFvdEFjY2VwdGVkIjpmYWxzZSwiU291cmNlUHJvdmVuYW5jZVNoYTI1NiI6IjY1ZDYyYWQ4MzVkNDM1ZDhmY2FhNmFhZDNhZmQyNWE3NDdlNmI1MWQwYTRmYjQxYTQ4ZjI5YzMyYjI2MTRmYWIifQ=="),
            ("package/weights.safetensors", "GAUAAAAAAAB7Im1vZGVsLmVtYmVkZGluZ3MudG9rX2VtYmVkZGluZ3Mud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls4LDRdLCJkYXRhX29mZnNldHMiOlswLDEyOF19LCJtb2RlbC5lbWJlZGRpbmdzLm5vcm0ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0XSwiZGF0YV9vZmZzZXRzIjpbMTI4LDE0NF19LCJtb2RlbC5maW5hbF9ub3JtLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNF0sImRhdGFfb2Zmc2V0cyI6WzE0NCwxNjBdfSwibW9kZWwubGF5ZXJzLjAuYXR0bi5XcWt2LndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbMTIsNF0sImRhdGFfb2Zmc2V0cyI6WzE2MCwzNTJdfSwibW9kZWwubGF5ZXJzLjAuYXR0bi5Xby53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzQsNF0sImRhdGFfb2Zmc2V0cyI6WzM1Miw0MTZdfSwibW9kZWwubGF5ZXJzLjAubWxwX25vcm0ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0XSwiZGF0YV9vZmZzZXRzIjpbNDE2LDQzMl19LCJtb2RlbC5sYXllcnMuMC5tbHAuV2kud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOlsxMiw0XSwiZGF0YV9vZmZzZXRzIjpbNDMyLDYyNF19LCJtb2RlbC5sYXllcnMuMC5tbHAuV28ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0LDZdLCJkYXRhX29mZnNldHMiOls2MjQsNzIwXX0sIm1vZGVsLmxheWVycy4xLmF0dG5fbm9ybS53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzRdLCJkYXRhX29mZnNldHMiOls3MjAsNzM2XX0sIm1vZGVsLmxheWVycy4xLmF0dG4uV3Frdi53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzEyLDRdLCJkYXRhX29mZnNldHMiOls3MzYsOTI4XX0sIm1vZGVsLmxheWVycy4xLmF0dG4uV28ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0LDRdLCJkYXRhX29mZnNldHMiOls5MjgsOTkyXX0sIm1vZGVsLmxheWVycy4xLm1scF9ub3JtLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNF0sImRhdGFfb2Zmc2V0cyI6Wzk5MiwxMDA4XX0sIm1vZGVsLmxheWVycy4xLm1scC5XaS53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzEyLDRdLCJkYXRhX29mZnNldHMiOlsxMDA4LDEyMDBdfSwibW9kZWwubGF5ZXJzLjEubWxwLldvLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNCw2XSwiZGF0YV9vZmZzZXRzIjpbMTIwMCwxMjk2XX0sImhlYWQubGluZWFyLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbMyw0XSwiZGF0YV9vZmZzZXRzIjpbMTI5NiwxMzQ0XX19ICAgIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=="),
            ("package/training-report.json", "eyJTdGF0dXMiOiJQQVNTX2FsbF9yZXF1ZXN0ZWRfY29ycHVzX2Vwb2Noc19HUFVfc2F2ZWRfb25seSIsIlJ1bklkIjoiZml4dHVyZS1mdWxsLWNvcnB1cyIsIklucHV0cyI6eyJQbGFuU2hhMjU2IjoiM2Q4Y2RkYzgxMGIxZGE1YWI3MjdhNjU4YmVhZjUzMTgzNTUzNmUyNWYzZDBjMjNkMzNjNmZkODMzNDQ1MmQ1NiIsIkRhdGFNYW5pZmVzdFNoYTI1NiI6ImIzMmYwMjY0NTNlMDJiYTQxNTU0ODczNzhkNWEzYjZlYmUwZjE5OGVhY2Y5MzVhNjY1YmIxYzdmY2QxYzU5MmUiLCJSZWNvcmRzU2hhMjU2IjoiZjZiYmRjNjA1NjcwNzllMTJlMDVmMjk2ZTFlZjE1MWQxZTc1MGNmYWU5YTMzYTk1ZjdkNDE1MjQzMzUwM2YxNCIsIkJhc2VFbmNvZGVyU2hhMjU2IjoiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYiIsIlRva2VuaXplclNoYTI1NiI6ImRmNGIxNDQ2M2FiMDM3MTgzMWUwNmRjZjY1ZjU4MDY5ZTllZjg3ZjhiZDcwODZlMWI1M2YzYzA3MzBmMDdkOTAiLCJPcmRlclNoYTI1NiI6IjllMDI0YmZjYzdlMmQwMjJlNWYzZGYwN2I2M2RlMzVkMzhhN2ZiYmQyY2QzYzVhNTg2YzIyMmVlOThiMDM5OWYiLCJCYWNrd2FyZE1hbmlmZXN0U2hhMjU2IjoiQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQyJ9LCJHcHVVdWlkIjoiYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWEiLCJQcm9jZXNzSWQiOjEwMCwiUHJvY2Vzc1N0YXJ0VXRjIjoiMjAyNi0xMC0xM1QwMDowMDowMFoiLCJJbml0aWFsVXBkYXRlcyI6MCwiQ29tcGxldGVkVXBkYXRlcyI6NCwiU3RhZ2VSZXF1ZXN0ZWRVcGRhdGVzIjo0LCJHbG9iYWxVcGRhdGVzIjo0LCJDb21wbGV0ZWRFcG9jaHMiOjEsIldpdGhpbkVwb2NoIjowLCJNZWFuT2JzZXJ2ZWRMb3NzIjowLjcsIkVsYXBzZWRTZWNvbmRzIjoxLCJQZWFrT3duZWRCeXRlcyI6MSwiQ2hlY2twb2ludFJlY2VpcHRTaGEyNTYiOiI4MTZhZTk1OWQ4ZmQ4NTFmODM1NzEzOTNjN2MyODE5ZmUyNGQ3OTBhZjcwZjM5YmY1YmI2ZjA3OTYxY2FlYThlIiwiQ3Vyc29yUmVjZWlwdFNoYTI1NiI6IjBlY2E5MzZlMzA0NTc0MTJiNzhlOWM2ZDViMjQ2ZDJlODcwMWQ5YzIxYjFkOWNhZDlhYWY2ZTRjNTYzYWI0NTUiLCJGdWxsQ29ycHVzRXBvY2hDb3ZlcmFnZSI6dHJ1ZSwiUXVhbGl0eUFjY2VwdGVkIjpmYWxzZSwiQ2FsaWJyYXRlZCI6ZmFsc2V9"),
            ("package/input-source-assets.json", "eyJzeW50aGV0aWMiOnRydWV9"),
            ("package/input-manifest.json", "eyJzY2hlbWFfdmVyc2lvbiI6MSwicGlsb3QiOmZhbHNlLCJmb3JtYWxfdHJhaW5pbmdfYWRtaXR0ZWQiOmZhbHNlLCJyZWNvcmRzX3dyaXR0ZW4iOjYsInNoYXJkcyI6W3sic291cmNlIjoib25lIiwic3BsaXQiOiJ0cmFpbiJ9LHsic291cmNlIjoib25lIiwic3BsaXQiOiJkZXZlbG9wbWVudCJ9LHsic291cmNlIjoidHdvIiwic3BsaXQiOiJ0cmFpbiJ9LHsic291cmNlIjoidHdvIiwic3BsaXQiOiJkZXZlbG9wbWVudCJ9XSwic291cmNlX25vdGljZV9iaW5kaW5ncyI6eyJjb21wb25lbnRzXFxyb290LTAwXFxzb3VyY2Utbm90aWNlc1xcVVBTVFJFQU0tUkVBRE1FLm1kIjoiMjBhNjhkMjc2OTk0NTAyZGYwYmU0YzQ3ZGRhNTI5YzNhZjNiYTIxOTA3ZTdlMTU3NzQyZTY5ODI3ODI2N2Q4NCJ9fQ=="),
            ("package/input-lengths.json", "eyJTdGF0dXMiOiJQQVNTX2Z1bGxfY29ycHVzX3N0cmljdF9sZW5ndGhzX29ubHkiLCJDb3JwdXNNYW5pZmVzdFNoYTI1NiI6ImIzMmYwMjY0NTNlMDJiYTQxNTU0ODczNzhkNWEzYjZlYmUwZjE5OGVhY2Y5MzVhNjY1YmIxYzdmY2QxYzU5MmUiLCJDb3JwdXNCaW5kaW5nc1NoYTI1NiI6Ijk4MzNmMTE3YjM4ODI2N2QzNzA4YTkyMGZhZjI4YWJiNjcxMTlhZGJhODhmYWY1OWE2MDkzMmU2YjM3MTk2NGMiLCJUb2tlbml6ZXJTaGEyNTYiOiJkZjRiMTQ0NjNhYjAzNzE4MzFlMDZkY2Y2NWY1ODA2OWU5ZWY4N2Y4YmQ3MDg2ZTFiNTNmM2MwNzMwZjA3ZDkwIiwiTWF4aW11bVRva2VucyI6OCwiUGh5c2ljYWxSb3dzIjo2LCJFeGNsdWRlZFJvd3MiOjAsIkFjY2VwdGVkIjo2LCJSZWplY3RlZCI6MCwiVHJhaW4iOjQsIkRldmVsb3BtZW50IjoyLCJMb25nZXN0VG9rZW5zIjo4LCJMb25nZXN0VHJhaW5pbmdUb2tlbnMiOjgsIlRyYWluaW5nQnlTb3VyY2UiOnsib25lIjoyLCJ0d28iOjJ9LCJHcHVGZWF0dXJlc0NvbXB1dGVkIjpmYWxzZSwiRm9ybWFsVHJhaW5pbmdBZG1pdHRlZCI6ZmFsc2UsIkNvbXBsZXRlU2NhbiI6dHJ1ZX0="),
            ("package/input-corpus-bindings.json", "eyJNYW5pZmVzdFNoYTI1NiI6ImIzMmYwMjY0NTNlMDJiYTQxNTU0ODczNzhkNWEzYjZlYmUwZjE5OGVhY2Y5MzVhNjY1YmIxYzdmY2QxYzU5MmUiLCJFeGNsdXNpb25zU2hhMjU2IjoiZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZSIsIlNvdXJjZVJpZ2h0c1NoYTI1NiI6ImE4MTk4NTI0ZjU4ZTcyYjU2MjgzYWI3MWViZGRhZGEyMjg0MGYxMDhiNDZlZjcxNjViYjNmY2E5OTE5YzU1NTgiLCJTb3VyY2VBc3NldHNTaGEyNTYiOiJhODE5ODUyNGY1OGU3MmI1NjI4M2FiNzFlYmRkYWRhMjI4NDBmMTA4YjQ2ZWY3MTY1YmIzZmNhOTkxOWM1NTU4IiwiRm9ybWFsVHJhaW5pbmdBZG1pdHRlZCI6ZmFsc2UsIlNvdXJjZVJvb3QiOm51bGwsIlNvdXJjZUFzc2V0c1BhdGgiOiJzb3VyY2UtYXNzZXRzLmpzb24ifQ=="),
            ("package/corpus-provenance.json", "eyJTY2hlbWFWZXJzaW9uIjoxLCJTdGF0dXMiOiJQQVNTX3ByaXZhdGVfYm91bmRfc291cmNlX21ldGFkYXRhX2J5dGVzX29ubHkiLCJDb3JwdXNNYW5pZmVzdFNoYTI1NiI6ImIzMmYwMjY0NTNlMDJiYTQxNTU0ODczNzhkNWEzYjZlYmUwZjE5OGVhY2Y5MzVhNjY1YmIxYzdmY2QxYzU5MmUiLCJPcmlnaW5hbEJpbmRpbmdzU2hhMjU2IjoiOTgzM2YxMTdiMzg4MjY3ZDM3MDhhOTIwZmFmMjhhYmI2NzExOWFkYmE4OGZhZjU5YTYwOTMyZTZiMzcxOTY0YyIsIk9yaWdpbmFsU291cmNlQXNzZXRzUGF0aCI6InNvdXJjZS1hc3NldHMuanNvbiIsIk9yaWdpbmFsU291cmNlUm9vdCI6bnVsbCwiU291cmNlQXNzZXRzUGF0aCI6ImlucHV0LXNvdXJjZS1hc3NldHMuanNvbiIsIlNvdXJjZVJvb3QiOm51bGwsIkZpbGVzIjpbeyJQYWNrYWdlUGF0aCI6ImlucHV0LW1hbmlmZXN0Lmpzb24iLCJPcmlnaW5hbFJvb3QiOiIvc3ludGhldGljL2NvcnB1cyIsIk9yaWdpbmFsUmVsYXRpdmVQYXRoIjoiaW5wdXQtbWFuaWZlc3QuanNvbiIsIlNoYTI1NiI6ImIzMmYwMjY0NTNlMDJiYTQxNTU0ODczNzhkNWEzYjZlYmUwZjE5OGVhY2Y5MzVhNjY1YmIxYzdmY2QxYzU5MmUiLCJCeXRlcyI6Mzk0LCJSb2xlIjoic291cmNlX21ldGFkYXRhX2Rlc2NyaXB0b3IifSx7IlBhY2thZ2VQYXRoIjoiaW5wdXQtY29ycHVzLWJpbmRpbmdzLmpzb24iLCJPcmlnaW5hbFJvb3QiOiIvc3ludGhldGljL2NvcnB1cyIsIk9yaWdpbmFsUmVsYXRpdmVQYXRoIjoiaW5wdXQtY29ycHVzLWJpbmRpbmdzLmpzb24iLCJTaGEyNTYiOiI5ODMzZjExN2IzODgyNjdkMzcwOGE5MjBmYWYyOGFiYjY3MTE5YWRiYTg4ZmFmNTlhNjA5MzJlNmIzNzE5NjRjIiwiQnl0ZXMiOjQzNiwiUm9sZSI6Im9yaWdpbmFsX2JpbmRpbmdfaGlzdG9yaWNhbF9wYXRocyJ9LHsiUGFja2FnZVBhdGgiOiJpbnB1dC1zb3VyY2UtcmlnaHRzLmpzb24iLCJPcmlnaW5hbFJvb3QiOiIvc3ludGhldGljL2NvcnB1cyIsIk9yaWdpbmFsUmVsYXRpdmVQYXRoIjoiaW5wdXQtc291cmNlLXJpZ2h0cy5qc29uIiwiU2hhMjU2IjoiYTgxOTg1MjRmNThlNzJiNTYyODNhYjcxZWJkZGFkYTIyODQwZjEwOGI0NmVmNzE2NWJiM2ZjYTk5MTljNTU1OCIsIkJ5dGVzIjoxOCwiUm9sZSI6InNvdXJjZV9yaWdodHNfZGVjbGFyZWRfcHJpdmF0ZSJ9LHsiUGFja2FnZVBhdGgiOiJpbnB1dC1zb3VyY2UtYXNzZXRzLmpzb24iLCJPcmlnaW5hbFJvb3QiOiIvc3ludGhldGljL2NvcnB1cyIsIk9yaWdpbmFsUmVsYXRpdmVQYXRoIjoiaW5wdXQtc291cmNlLWFzc2V0cy5qc29uIiwiU2hhMjU2IjoiYTgxOTg1MjRmNThlNzJiNTYyODNhYjcxZWJkZGFkYTIyODQwZjEwOGI0NmVmNzE2NWJiM2ZjYTk5MTljNTU1OCIsIkJ5dGVzIjoxOCwiUm9sZSI6InNvdXJjZV9hc3NldF9kZXNjcmlwdG9yc19ub19yYXdfcGF5bG9hZHMifSx7IlBhY2thZ2VQYXRoIjoiY29ycHVzLXByb3ZlbmFuY2UvY29tcG9uZW50cy9yb290LTAwL3NvdXJjZS1ub3RpY2VzL1VQU1RSRUFNLVJFQURNRS5tZCIsIk9yaWdpbmFsUm9vdCI6Ii9zeW50aGV0aWMvY29ycHVzIiwiT3JpZ2luYWxSZWxhdGl2ZVBhdGgiOiJjb21wb25lbnRzXFxyb290LTAwXFxzb3VyY2Utbm90aWNlc1xcVVBTVFJFQU0tUkVBRE1FLm1kIiwiU2hhMjU2IjoiMjBhNjhkMjc2OTk0NTAyZGYwYmU0YzQ3ZGRhNTI5YzNhZjNiYTIxOTA3ZTdlMTU3NzQyZTY5ODI3ODI2N2Q4NCIsIkJ5dGVzIjozMywiUm9sZSI6InNvdXJjZV9ub3RpY2VfZGVjbGFyZWRfcHJpdmF0ZSJ9XSwiU291cmNlTm90aWNlQmluZGluZ3MiOnsiY29ycHVzLXByb3ZlbmFuY2UvY29tcG9uZW50cy9yb290LTAwL3NvdXJjZS1ub3RpY2VzL1VQU1RSRUFNLVJFQURNRS5tZCI6IjIwYTY4ZDI3Njk5NDUwMmRmMGJlNGM0N2RkYTUyOWMzYWYzYmEyMTkwN2U3ZTE1Nzc0MmU2OTgyNzgyNjdkODQifSwiUHJpdmF0ZVNvdXJjZU1ldGFkYXRhIjp0cnVlLCJQdWJsaWNQdWJsaWNhdGlvbkFjY2VwdGVkIjpmYWxzZSwiQ29udGFpbnNSYXdFeGFtcGxlc09yR29sZExhYmVscyI6ZmFsc2UsIkNvbnRhaW5zUGVyUmVjb3JkRXhjbHVzaW9uSWRlbnRpdGllcyI6ZmFsc2V9"),
            ("package/tokenizer/tokenizer.json", "eyJzeW50aGV0aWNfZml4dHVyZV9ub3RfdG9rZW5pemVyIjp0cnVlfQ=="),
            ("package/run-plan.json", "eyJTY2hlbWFWZXJzaW9uIjoxLCJSdW5JZCI6ImZpeHR1cmUtZnVsbC1jb3JwdXMiLCJDb3JwdXNSb290IjoiL2ZpeHR1cmUvY29ycHVzIiwiQ29ycHVzTWFuaWZlc3RTaGEyNTYiOiJiMzJmMDI2NDUzZTAyYmE0MTU1NDg3Mzc4ZDVhM2I2ZWJlMGYxOThlYWNmOTM1YTY2NWJiMWM3ZmNkMWM1OTJlIiwiQ29ycHVzQmluZGluZ3NTaGEyNTYiOiI5ODMzZjExN2IzODgyNjdkMzcwOGE5MjBmYWYyOGFiYjY3MTE5YWRiYTg4ZmFmNTlhNjA5MzJlNmIzNzE5NjRjIiwiTGVuZ3RoUmVwb3J0UGF0aCI6Ii9maXh0dXJlL2xlbmd0aHMuanNvbiIsIkxlbmd0aFJlcG9ydFNoYTI1NiI6IjUxZGFiZGRjNjRjOTE0ZjhjZDE2YjQxMmU5M2NmZmEyNGZmZTI0ZjUzOTczZGJmZjM1MDI2NTdjNjRkYzNjZmEiLCJCYXNlTW9kZWxSb290IjoiL2ZpeHR1cmUvYmFzZSIsIlNvdXJjZUNvbW1pdCI6IjExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTEiLCJEYXRhc2V0Q29tbWl0IjoiMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMiIsIk1vZGVsQ29tbWl0IjoiMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMyIsIkVwb2NocyI6MSwiVHJhaW5SZWNvcmRzIjo0LCJFeHBlY3RlZEdwdVV1aWQiOiJhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYSIsIkV4cGVyaW1lbnRhbE9ubHkiOnRydWUsIlNlZWQiOjE3LCJNYXhpbXVtVG9rZW5zIjo4LCJDaGVja3BvaW50RXZlcnkiOjIsIlZhbGlkYXRpb25QZXJTb3VyY2UiOjEsIkVuY29kZXJSYXRlIjoxZS0wNSwiSGVhZFJhdGUiOjAuMDAxLCJXZWlnaHREZWNheSI6MC4wMSwiTWF4aW11bU93bmVkQnl0ZXMiOjEwNDg1NzZ9"),
            ("package/resources-reclaimed.txt", "UEFTU19vd25lZF9idWZmZXJzX21vZHVsZXNfcmVzdG9yZWRfYW5kX2NvbnRleHRfZGlzcG9zZWQNCg=="),
            ("package/base-LICENSE-COMPONENTS.md", "U3ludGhldGljIGJhc2Ugc291cmNlIG5vdGljZS4NCg=="),
            ("package/base-license-provenance.json", "U3ludGhldGljIGJhc2Ugc291cmNlIG5vdGljZS4NCg=="),
            ("package/input-source-rights.json", "eyJzeW50aGV0aWMiOnRydWV9"),
            ("package/LICENSE-COMPONENTS.md", "U3ludGhldGljIGxpY2Vuc2UgYm91bmRhcnksIHVua25vd24gb3ZlcmFsbC4NCg=="),
            ("package/checkpoint-0000000004/weights.safetensors", "GAUAAAAAAAB7Im1vZGVsLmVtYmVkZGluZ3MudG9rX2VtYmVkZGluZ3Mud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls4LDRdLCJkYXRhX29mZnNldHMiOlswLDEyOF19LCJtb2RlbC5lbWJlZGRpbmdzLm5vcm0ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0XSwiZGF0YV9vZmZzZXRzIjpbMTI4LDE0NF19LCJtb2RlbC5maW5hbF9ub3JtLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNF0sImRhdGFfb2Zmc2V0cyI6WzE0NCwxNjBdfSwibW9kZWwubGF5ZXJzLjAuYXR0bi5XcWt2LndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbMTIsNF0sImRhdGFfb2Zmc2V0cyI6WzE2MCwzNTJdfSwibW9kZWwubGF5ZXJzLjAuYXR0bi5Xby53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzQsNF0sImRhdGFfb2Zmc2V0cyI6WzM1Miw0MTZdfSwibW9kZWwubGF5ZXJzLjAubWxwX25vcm0ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0XSwiZGF0YV9vZmZzZXRzIjpbNDE2LDQzMl19LCJtb2RlbC5sYXllcnMuMC5tbHAuV2kud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOlsxMiw0XSwiZGF0YV9vZmZzZXRzIjpbNDMyLDYyNF19LCJtb2RlbC5sYXllcnMuMC5tbHAuV28ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0LDZdLCJkYXRhX29mZnNldHMiOls2MjQsNzIwXX0sIm1vZGVsLmxheWVycy4xLmF0dG5fbm9ybS53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzRdLCJkYXRhX29mZnNldHMiOls3MjAsNzM2XX0sIm1vZGVsLmxheWVycy4xLmF0dG4uV3Frdi53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzEyLDRdLCJkYXRhX29mZnNldHMiOls3MzYsOTI4XX0sIm1vZGVsLmxheWVycy4xLmF0dG4uV28ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0LDRdLCJkYXRhX29mZnNldHMiOls5MjgsOTkyXX0sIm1vZGVsLmxheWVycy4xLm1scF9ub3JtLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNF0sImRhdGFfb2Zmc2V0cyI6Wzk5MiwxMDA4XX0sIm1vZGVsLmxheWVycy4xLm1scC5XaS53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzEyLDRdLCJkYXRhX29mZnNldHMiOlsxMDA4LDEyMDBdfSwibW9kZWwubGF5ZXJzLjEubWxwLldvLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNCw2XSwiZGF0YV9vZmZzZXRzIjpbMTIwMCwxMjk2XX0sImhlYWQubGluZWFyLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbMyw0XSwiZGF0YV9vZmZzZXRzIjpbMTI5NiwxMzQ0XX19ICAgIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=="),
            ("package/checkpoint-0000000004/moments.safetensors", "CAsAAAAAAAB7ImZpcnN0Lm1vZGVsLmVtYmVkZGluZ3MudG9rX2VtYmVkZGluZ3Mud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls4LDRdLCJkYXRhX29mZnNldHMiOlswLDEyOF19LCJzZWNvbmQubW9kZWwuZW1iZWRkaW5ncy50b2tfZW1iZWRkaW5ncy53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzgsNF0sImRhdGFfb2Zmc2V0cyI6WzEyOCwyNTZdfSwiZmlyc3QubW9kZWwuZW1iZWRkaW5ncy5ub3JtLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNF0sImRhdGFfb2Zmc2V0cyI6WzI1NiwyNzJdfSwic2Vjb25kLm1vZGVsLmVtYmVkZGluZ3Mubm9ybS53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzRdLCJkYXRhX29mZnNldHMiOlsyNzIsMjg4XX0sImZpcnN0Lm1vZGVsLmZpbmFsX25vcm0ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0XSwiZGF0YV9vZmZzZXRzIjpbMjg4LDMwNF19LCJzZWNvbmQubW9kZWwuZmluYWxfbm9ybS53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzRdLCJkYXRhX29mZnNldHMiOlszMDQsMzIwXX0sImZpcnN0Lm1vZGVsLmxheWVycy4wLmF0dG4uV3Frdi53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzEyLDRdLCJkYXRhX29mZnNldHMiOlszMjAsNTEyXX0sInNlY29uZC5tb2RlbC5sYXllcnMuMC5hdHRuLldxa3Yud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOlsxMiw0XSwiZGF0YV9vZmZzZXRzIjpbNTEyLDcwNF19LCJmaXJzdC5tb2RlbC5sYXllcnMuMC5hdHRuLldvLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNCw0XSwiZGF0YV9vZmZzZXRzIjpbNzA0LDc2OF19LCJzZWNvbmQubW9kZWwubGF5ZXJzLjAuYXR0bi5Xby53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzQsNF0sImRhdGFfb2Zmc2V0cyI6Wzc2OCw4MzJdfSwiZmlyc3QubW9kZWwubGF5ZXJzLjAubWxwX25vcm0ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0XSwiZGF0YV9vZmZzZXRzIjpbODMyLDg0OF19LCJzZWNvbmQubW9kZWwubGF5ZXJzLjAubWxwX25vcm0ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0XSwiZGF0YV9vZmZzZXRzIjpbODQ4LDg2NF19LCJmaXJzdC5tb2RlbC5sYXllcnMuMC5tbHAuV2kud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOlsxMiw0XSwiZGF0YV9vZmZzZXRzIjpbODY0LDEwNTZdfSwic2Vjb25kLm1vZGVsLmxheWVycy4wLm1scC5XaS53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzEyLDRdLCJkYXRhX29mZnNldHMiOlsxMDU2LDEyNDhdfSwiZmlyc3QubW9kZWwubGF5ZXJzLjAubWxwLldvLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNCw2XSwiZGF0YV9vZmZzZXRzIjpbMTI0OCwxMzQ0XX0sInNlY29uZC5tb2RlbC5sYXllcnMuMC5tbHAuV28ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0LDZdLCJkYXRhX29mZnNldHMiOlsxMzQ0LDE0NDBdfSwiZmlyc3QubW9kZWwubGF5ZXJzLjEuYXR0bl9ub3JtLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNF0sImRhdGFfb2Zmc2V0cyI6WzE0NDAsMTQ1Nl19LCJzZWNvbmQubW9kZWwubGF5ZXJzLjEuYXR0bl9ub3JtLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbNF0sImRhdGFfb2Zmc2V0cyI6WzE0NTYsMTQ3Ml19LCJmaXJzdC5tb2RlbC5sYXllcnMuMS5hdHRuLldxa3Yud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOlsxMiw0XSwiZGF0YV9vZmZzZXRzIjpbMTQ3MiwxNjY0XX0sInNlY29uZC5tb2RlbC5sYXllcnMuMS5hdHRuLldxa3Yud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOlsxMiw0XSwiZGF0YV9vZmZzZXRzIjpbMTY2NCwxODU2XX0sImZpcnN0Lm1vZGVsLmxheWVycy4xLmF0dG4uV28ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0LDRdLCJkYXRhX29mZnNldHMiOlsxODU2LDE5MjBdfSwic2Vjb25kLm1vZGVsLmxheWVycy4xLmF0dG4uV28ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0LDRdLCJkYXRhX29mZnNldHMiOlsxOTIwLDE5ODRdfSwiZmlyc3QubW9kZWwubGF5ZXJzLjEubWxwX25vcm0ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0XSwiZGF0YV9vZmZzZXRzIjpbMTk4NCwyMDAwXX0sInNlY29uZC5tb2RlbC5sYXllcnMuMS5tbHBfbm9ybS53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzRdLCJkYXRhX29mZnNldHMiOlsyMDAwLDIwMTZdfSwiZmlyc3QubW9kZWwubGF5ZXJzLjEubWxwLldpLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbMTIsNF0sImRhdGFfb2Zmc2V0cyI6WzIwMTYsMjIwOF19LCJzZWNvbmQubW9kZWwubGF5ZXJzLjEubWxwLldpLndlaWdodCI6eyJkdHlwZSI6IkYzMiIsInNoYXBlIjpbMTIsNF0sImRhdGFfb2Zmc2V0cyI6WzIyMDgsMjQwMF19LCJmaXJzdC5tb2RlbC5sYXllcnMuMS5tbHAuV28ud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOls0LDZdLCJkYXRhX29mZnNldHMiOlsyNDAwLDI0OTZdfSwic2Vjb25kLm1vZGVsLmxheWVycy4xLm1scC5Xby53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzQsNl0sImRhdGFfb2Zmc2V0cyI6WzI0OTYsMjU5Ml19LCJmaXJzdC5oZWFkLmxpbmVhci53ZWlnaHQiOnsiZHR5cGUiOiJGMzIiLCJzaGFwZSI6WzMsNF0sImRhdGFfb2Zmc2V0cyI6WzI1OTIsMjY0MF19LCJzZWNvbmQuaGVhZC5saW5lYXIud2VpZ2h0Ijp7ImR0eXBlIjoiRjMyIiwic2hhcGUiOlszLDRdLCJkYXRhX29mZnNldHMiOlsyNjQwLDI2ODhdfX0gICAgICAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
            ("package/checkpoint-0000000004/corpus-cursor.json", "eyJTY2hlbWFWZXJzaW9uIjoxLCJSdW5JZCI6ImZpeHR1cmUtZnVsbC1jb3JwdXMiLCJJbnB1dHMiOnsiUGxhblNoYTI1NiI6IjNkOGNkZGM4MTBiMWRhNWFiNzI3YTY1OGJlYWY1MzE4MzU1MzZlMjVmM2QwYzIzZDMzYzZmZDgzMzQ0NTJkNTYiLCJEYXRhTWFuaWZlc3RTaGEyNTYiOiJiMzJmMDI2NDUzZTAyYmE0MTU1NDg3Mzc4ZDVhM2I2ZWJlMGYxOThlYWNmOTM1YTY2NWJiMWM3ZmNkMWM1OTJlIiwiUmVjb3Jkc1NoYTI1NiI6ImY2YmJkYzYwNTY3MDc5ZTEyZTA1ZjI5NmUxZWYxNTFkMWU3NTBjZmFlOWEzM2E5NWY3ZDQxNTI0MzM1MDNmMTQiLCJCYXNlRW5jb2RlclNoYTI1NiI6ImJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmIiLCJUb2tlbml6ZXJTaGEyNTYiOiJkZjRiMTQ0NjNhYjAzNzE4MzFlMDZkY2Y2NWY1ODA2OWU5ZWY4N2Y4YmQ3MDg2ZTFiNTNmM2MwNzMwZjA3ZDkwIiwiT3JkZXJTaGEyNTYiOiI5ZTAyNGJmY2M3ZTJkMDIyZTVmM2RmMDdiNjNkZTM1ZDM4YTdmYmJkMmNkM2M1YTU4NmMyMjJlZTk4YjAzOTlmIiwiQmFja3dhcmRNYW5pZmVzdFNoYTI1NiI6IkNDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0MifSwiQ29tcGxldGVkVXBkYXRlcyI6NCwiRXBvY2giOjEsIldpdGhpbkVwb2NoIjowLCJUcmFpbmluZ1JlY29yZHNQZXJFcG9jaCI6NCwiUGxhbm5lZEVwb2NocyI6MSwiQ2hlY2twb2ludFJlY2VpcHRTaGEyNTYiOiI4MTZhZTk1OWQ4ZmQ4NTFmODM1NzEzOTNjN2MyODE5ZmUyNGQ3OTBhZjcwZjM5YmY1YmI2ZjA3OTYxY2FlYThlIiwiU2NoZWR1bGVBbGdvcml0aG0iOiJzb3VyY2Vfcm91bmRfcm9iaW5fdjE7c291cmNlX29yZGVyPXNoYTI1NihzZWVkLGVwb2NoLHNvdXJjZSk7d2l0aGluX3NvdXJjZT1tYW5pZmVzdF9zaGFyZF9yb3dfb3JkZXI7dHJhaW5fb25seTtub19vdmVyc2FtcGxpbmcifQ=="),
            ("package/checkpoint-0000000004/checkpoint.json", "eyJTY2hlbWFWZXJzaW9uIjoxLCJSdW5JZCI6ImZpeHR1cmUtZnVsbC1jb3JwdXMiLCJDb21wbGV0ZWRVcGRhdGVzIjo0LCJJbnB1dHMiOnsiUGxhblNoYTI1NiI6IjNkOGNkZGM4MTBiMWRhNWFiNzI3YTY1OGJlYWY1MzE4MzU1MzZlMjVmM2QwYzIzZDMzYzZmZDgzMzQ0NTJkNTYiLCJEYXRhTWFuaWZlc3RTaGEyNTYiOiJiMzJmMDI2NDUzZTAyYmE0MTU1NDg3Mzc4ZDVhM2I2ZWJlMGYxOThlYWNmOTM1YTY2NWJiMWM3ZmNkMWM1OTJlIiwiUmVjb3Jkc1NoYTI1NiI6ImY2YmJkYzYwNTY3MDc5ZTEyZTA1ZjI5NmUxZWYxNTFkMWU3NTBjZmFlOWEzM2E5NWY3ZDQxNTI0MzM1MDNmMTQiLCJCYXNlRW5jb2RlclNoYTI1NiI6ImJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmIiLCJUb2tlbml6ZXJTaGEyNTYiOiJkZjRiMTQ0NjNhYjAzNzE4MzFlMDZkY2Y2NWY1ODA2OWU5ZWY4N2Y4YmQ3MDg2ZTFiNTNmM2MwNzMwZjA3ZDkwIiwiT3JkZXJTaGEyNTYiOiI5ZTAyNGJmY2M3ZTJkMDIyZTVmM2RmMDdiNjNkZTM1ZDM4YTdmYmJkMmNkM2M1YTU4NmMyMjJlZTk4YjAzOTlmIiwiQmFja3dhcmRNYW5pZmVzdFNoYTI1NiI6IkNDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0MifSwiQ29uZmlnIjp7IlZvY2FidWxhcnlTaXplIjo4LCJIaWRkZW5TaXplIjo0LCJJbnRlcm1lZGlhdGVTaXplIjo2LCJMYXllckNvdW50IjoyLCJIZWFkQ291bnQiOjIsIk1heFRva2VucyI6OCwiR2xvYmFsQXR0ZW50aW9uRXZlcnkiOjIsIkxvY2FsQXR0ZW50aW9uIjoyLCJHbG9iYWxSb3BlVGhldGEiOjE2MDAwMCwiTG9jYWxSb3BlVGhldGEiOjE2MDAwMCwiTm9ybUVwc2lsb24iOjFlLTA1fSwiV2VpZ2h0c1NoYTI1NiI6IjhmNzE1MTU3YTFkMGU4ZDI1Yjg0MDYxMDFlY2FiYTBhNzRkNTk4MzcwZDllYWExY2U5YTdkYWE0MWU2NWMwMjMiLCJNb21lbnRzU2hhMjU2IjoiOGM2NjhiMmFlNTc3NzYwYzMyYWYyY2E2ZjhkYmY0NTk0ODI5NTRhMjdlMGZmMWE1N2ZhYzQ0ZGU2N2YxNzhmNyJ9"),
            ("package/notices/base-NOTICE.txt", "U3ludGhldGljIGJhc2Ugc291cmNlIG5vdGljZS4NCg=="),
            ("package/corpus-provenance/components/root-00/source-notices/UPSTREAM-README.md", "U3ludGhldGljIOengeacieiuuOWPr+WOn+Wtl+iKgg0K"),
        };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (files.Length > 64) throw new InvalidDataException("Frozen fixture file cap.");
        foreach (var file in files)
        {
            if (watch.Elapsed.TotalSeconds >= 5) throw new TimeoutException("Frozen fixture wall cap.");
            var path = Path.Combine(root, file.Name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.Write(Convert.FromBase64String(file.Base64));
        }
        return root;
    }

    private sealed record Request(string Method, string Target, Dictionary<string, string> Headers, byte[] Body);
    private sealed record Reply(int Status, byte[] Body, Dictionary<string, string> Headers, int? DeclaredLength = null);
    private sealed class Loopback : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(20));
        private readonly Task worker;
        internal string Uri { get; }
        internal Loopback(Func<Request, Reply> handle, int maximumRequests)
        {
            if (maximumRequests is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(maximumRequests));
            listener.Start(1); Uri = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
            worker = Task.Run(async () =>
            {
                try
                {
                    for (var index = 0; index < maximumRequests; index++)
                    {
                        stop.Token.ThrowIfCancellationRequested(); using var client = await listener.AcceptTcpClientAsync(stop.Token); using var stream = client.GetStream();
                        using var header = new MemoryStream(); var matched = 0;
                        for (var bytes = 0; bytes < 8192; bytes++)
                        {
                            var single = new byte[1]; if (await stream.ReadAsync(single, stop.Token) != 1) throw new EndOfStreamException("Fixture request header truncated.");
                            header.WriteByte(single[0]); matched = single[0] == "\r\n\r\n"[matched] ? matched + 1 : single[0] == '\r' ? 1 : 0;
                            if (matched == 4) break;
                        }
                        if (matched != 4) throw new InvalidDataException("Fixture header cap exceeded.");
                        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                        var first = lines[0].Split(' '); var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var line in lines.Skip(1)) { var split = line.IndexOf(':'); if (split <= 0 || !headers.TryAdd(line[..split], line[(split + 1)..].Trim())) throw new InvalidDataException("Fixture header malformed."); }
                        var length = headers.TryGetValue("Content-Length", out var raw) ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture) : 0;
                        if (length is < 0 or > 1024 * 1024 || headers.ContainsKey("Transfer-Encoding")) throw new InvalidDataException("Fixture body cap/framing exceeded.");
                        var body = new byte[length]; await stream.ReadExactlyAsync(body, stop.Token);
                        var reply = handle(new(first[0], first[1], headers, body));
                        var text = new StringBuilder("HTTP/1.1 " + reply.Status + " Fixture\r\nContent-Length: " + (reply.DeclaredLength ?? reply.Body.Length) + "\r\nConnection: close\r\n");
                        foreach (var value in reply.Headers) text.Append(value.Key).Append(": ").Append(value.Value).Append("\r\n");
                        text.Append("\r\n"); await stream.WriteAsync(Encoding.ASCII.GetBytes(text.ToString()), stop.Token); await stream.WriteAsync(reply.Body, stop.Token); await stream.FlushAsync(stop.Token);
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (SocketException) when (stop.IsCancellationRequested) { }
            });
        }
        public void Dispose()
        {
            stop.Cancel(); listener.Stop();
            try { worker.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
            finally { stop.Dispose(); }
        }
    }
}
