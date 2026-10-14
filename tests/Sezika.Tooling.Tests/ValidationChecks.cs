using System.Text.Json;
using Sezika.Tooling;

internal static class ValidationChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        Budget Limit() => new(30, null, CancellationToken.None);
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static JsonElement Json(string value) { using var doc = JsonDocument.Parse(value); return doc.RootElement.Clone(); }
        static void Reject(Action action) { try { action(); } catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or IOException) { return; } throw new InvalidOperationException("Expected validator rejection."); }
        var repo = Directory.GetCurrentDirectory(); string File(string name) => Path.Combine(root, "validation-" + name); var archivedRoot = Path.Combine(repo, "docs/evidence/s346-continuation-2026-09-26"); var profilePath = Path.Combine(archivedRoot, "profile-simd-short1.json");
        check("validation rejects nonnumeric/string/Boolean measurements", () => { Reject(() => ValidationJson.Number(Json("\"1\""))); Reject(() => ValidationJson.Number(Json("true"))); Reject(() => ValidationJson.Number(Json("-1"))); Assert(ValidationJson.Integer(Json("1"), 1, 1) == 1, "Integer positive control failed."); });
        check("validation four frozen real profile reports retain stage observation", () =>
        {
            var files = new[] { "profile-simd-short1.json", "profile-simd-long1.json", "profile-cuda-short1.json", "profile-cuda-long32.json" }.Select(name => Path.Combine(archivedRoot, name)).ToArray(); var hashes = files.Select(path => CorpusJson.FileHash(path, Limit())).ToArray();
            var report = ValidationOperations.Profile(new(files, File("profiles.json")), Limit()); Assert(report.Reports.Length == 4 && report.Reports[0].Cases[0].Stage == "encoder_and_head" && report.Reports[0].Cases[0].StageMilliseconds.HasValue && report.Scope.Contains("no new inference", StringComparison.Ordinal), "Historical profiles/stage/scope lost."); Assert(files.Select(path => CorpusJson.FileHash(path, Limit())).SequenceEqual(hashes), "Archival profile bytes changed.");
        });
        check("validation profile negative mutation matrix", () =>
        {
            var original = CorpusJson.Read(profilePath, Limit()); var mutations = new (string Name, string[] Path, string Value)[]
            {
                ("string-sample", ["profile", "cases", "0", "end_to_end_milliseconds", "0"], "\"1\""), ("boolean-sample", ["profile", "cases", "0", "end_to_end_milliseconds", "0"], "true"), ("negative-sample", ["profile", "cases", "0", "end_to_end_milliseconds", "0"], "-1"),
                ("duplicate-length", ["profile", "selected_lengths"], "[\"short\",\"short\"]"), ("wrong-dimension", ["profile", "selected_lengths"], "[\"medium\"]"), ("wrong-case", ["profile", "cases", "0", "id"], "\"short-8\""),
                ("request-coverage", ["profile", "request_coverage"], "0"), ("question-coverage", ["profile", "question_coverage"], "0"), ("planned-questions", ["profile", "planned_questions"], "8"),
                ("request-throughput", ["profile", "cases", "0", "requests_per_second"], "1"), ("question-throughput", ["profile", "cases", "0", "questions_per_second"], "1"), ("minimum-latency", ["profile", "cases", "0", "latency", "minimum"], "0"),
                ("input-hash", ["profile", "cases", "0", "input_json"], "\"mutated\""), ("executable-identity", ["executable_sha256"], "\"" + new string('a', 64) + "\""), ("nonboolean-aot", ["native_aot"], "\"false\""),
                ("incomplete-discovery", ["profile", "cases", "0", "discovery_completed_forwards"], "0"), ("observed-token-hash", ["profile", "cases", "0", "actual_sequences", "0", "tokens_sha256"], "\"" + new string('a', 64) + "\""),
                ("token-denominator", ["profile", "cases", "0", "rendered_total_tokens"], "1"), ("incomplete-stage", ["profile", "cases", "0", "instrumented_completed_forwards"], "0"),
                ("undeclared-stage", ["profile", "detail"], "\"end_to_end\""), ("missing-stage", ["profile", "cases", "0", "encoder_and_head_milliseconds"], "null"),
                ("resource-failure", ["profile", "cases", "0", "resource_observation_errors"], "[{\"error_code\":\"fixture\"}]"), ("profile-status", ["profile", "status"], "\"complete_with_rejections\""),
                ("stage-denominator", ["profile", "cases_with_stage_timings"], "0"), ("schema-string", ["schema_version"], "\"1\""),
                ("repetitions-string", ["profile", "cases", "0", "state_repetitions"], "\"4\""), ("candidate-string", ["profile", "cases", "0", "candidates_per_question"], "\"2\""), ("forward-string", ["profile", "cases", "0", "discovery_completed_forwards"], "\"1\"")
            };
            Assert(mutations.Length <= 32, "Profile mutation cap.");
            foreach (var mutation in mutations) { var path = File(mutation.Name + ".json"); System.IO.File.WriteAllText(path, ValidationJson.Mutate(original, mutation.Path, Json(mutation.Value), Limit()), CorpusJson.Utf8); Reject(() => ValidationOperations.Profile(new([path], File(mutation.Name + "-result.json")), Limit())); Assert(!System.IO.File.Exists(File(mutation.Name + "-result.json")), "Invalid profile emitted success output."); }
        });
        check("validation duplicate JSON keys rejected with escaped identity", () =>
        { var path = File("duplicate.json"); System.IO.File.WriteAllText(path, "{\"schema_version\":0,\"schema_versi\\u006fn\":1}", CorpusJson.Utf8); Reject(() => CorpusJson.Read(path, Limit())); });
        check("validation S4 fixture remains blocked and preserves seal issues", () =>
        { var manifest = Path.Combine(repo, "datasets/s4-05/example-manifest.json"); var hash = CorpusJson.FileHash(manifest, Limit()); var result = ValidationOperations.DataAdmission(new(manifest, File("s4-admission.json")), Limit()); Assert(result.Status == "blocked" && result.Issues.Any(i => i.Code == "fixture_only") && result.Issues.Any(i => i.Code == "test_not_sealed") && !result.AdmissionChecksPassed && result.RequiresHumanAcceptance && !result.RepresentsModelQuality, "Fixture acquired admission or lost diagnostics."); Assert(CorpusJson.FileHash(manifest, Limit()) == hash, "Admission rewrote input bytes."); Reject(() => ValidationOperations.DataAdmission(new(manifest, File("s4-require.json"), RequireAdmission: true), Limit())); });
        check("validation admission normalization uses NFKC and letters/digits", () => Assert(ValidationOperations.NormalizeAdmissionText("Ａ b，", Limit()) == "AB", "Frozen normalization semantics changed."));
        check("validation language audit preserves12 original test fixtures", () =>
        { var sources = new[] { "data/s4-02/en/general/test.jsonl", "data/s4-02/en/support/test.jsonl", "data/s4-02/zh/general/test.jsonl", "data/s4-02/zh/support/test.jsonl" }; var result = ValidationOperations.LanguageAudit(new(repo, sources, File("language"), 12), Limit()); Assert(result.Records == 12 && result.Sources.Length == 4 && result.Status == "original_fixture_audit_not_independent_quality_acceptance", "Language fixture count/scope differs."); var rows = CorpusJson.Lines(Path.Combine(File("language"), "eval.jsonl"), 12, Limit()).ToArray(); Assert(rows.Length == 12 && rows.All(row => row.GetProperty("source_family").GetString() == "sezika-s4-02-original-test-fixture"), "Language source family lost."); });
        check("validation native batch fixture preserves frozen source and expected output", () =>
        {
            var fixtureDirectory = File("native-fixture"); Directory.CreateDirectory(fixtureDirectory); var executable = Path.Combine(fixtureDirectory, "Sezika.Evaluation" + (OperatingSystem.IsWindows() ? ".exe" : "")); System.IO.File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("Current native host unavailable."), executable); var input = File("batch-input.json"); System.IO.File.WriteAllText(input, "{\"field\":1}", CorpusJson.Utf8); var sourceHash = CorpusJson.FileHash(input, Limit());
            var item = new ValidationCase("positive", executable, CorpusJson.FileHash(executable, Limit()), ["--score-capture", "{case_root}/input.json"], [new(input, "input.json", sourceHash)], [new("input.json", ["field"], "set", Json("2"))], [new("result.json", true, "Status", "fixture_pass")], 0);
            var calls = 0; var result = ValidationOperations.Batch(new([item], File("batch")), Limit(), (execution, _) =>
            {
                calls++; Directory.CreateDirectory(execution.OutputDirectory); Assert(CorpusJson.Read(execution.ArgumentList[1], Limit()).GetProperty("field").GetInt32() == 2, "Copied mutation/argv differed."); System.IO.File.WriteAllText(Path.Combine(execution.WorkingDirectory, "result.json"), "{\"Status\":\"fixture_pass\"}", CorpusJson.Utf8);
                var receipt = new ExecutionResult { ExitCode = 0, CleanupStatus = "PASS", InputComplete = true, OutputComplete = true }; CorpusJson.Write(Path.Combine(execution.OutputDirectory, "result.json"), receipt, ExecutionJsonContext.Default.ExecutionResult, Limit()); return receipt;
            });
            Assert(calls == 1 && result.Passed == 1 && result.SyntheticTransport && !result.NewInference && sourceHash == CorpusJson.FileHash(input, Limit()), "Synthetic validation source/claims changed.");
        });
        check("validation incomplete cleanup remains FAIL and later cases unprocessed", () =>
        {
            var executable = File("failure-Sezika.Evaluation.exe"); var directory = File("failure-validator"); Directory.CreateDirectory(directory); executable = Path.Combine(directory, "Sezika.Evaluation.exe"); System.IO.File.WriteAllText(executable, "fixture", CorpusJson.Utf8); var sha = CorpusJson.FileHash(executable, Limit()); var first = new ValidationCase("first", executable, sha, ["--score-capture"], [], [], [], 0); var second = first with { Name = "second" }; var calls = 0;
            var result = ValidationOperations.Batch(new([first, second], File("batch-failure")), Limit(), (_, _) => { calls++; return new ExecutionResult { ExitCode = 0, CleanupStatus = "UNKNOWN", OutputComplete = true, InputComplete = true }; });
            Assert(calls == 1 && result.Failed == 1 && result.Unprocessed == 1 && result.Cases[1].Status == "UNPROCESSED", "Failure/unprocessed denominator lost.");
        });
        check("validation rejects negative matrix without positive control", () =>
        { var directory = File("negative-only"); Directory.CreateDirectory(directory); var executable = Path.Combine(directory, "Sezika.Evaluation" + (OperatingSystem.IsWindows() ? ".exe" : "")); System.IO.File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("Host missing."), executable); var item = new ValidationCase("negative", executable, CorpusJson.FileHash(executable, Limit()), ["--score-capture"], [], [], [], 1, "targeted error"); var calls = 0; Reject(() => ValidationOperations.Batch(new([item], File("negative-only-output")), Limit(), (_, _) => { calls++; return new(); })); Assert(calls == 0 && !Directory.Exists(File("negative-only-output")), "Negative-only batch ran without valid control."); });
        check("validation batch cumulative copied byte cap spans actual case files", () =>
        {
            var fixture = File("cumulative-input"); Directory.CreateDirectory(fixture);
            var executable = Path.Combine(fixture, "Sezika.Evaluation" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            System.IO.File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("Native host missing."), executable);
            var input = Path.Combine(fixture, "source.json"); System.IO.File.WriteAllText(input, "0", CorpusJson.Utf8);
            var item = new ValidationCase("first", executable, CorpusJson.FileHash(executable, Limit()), ["--score-capture"],
                [new(input, "input.json", CorpusJson.FileHash(input, Limit()))], [], [], 0);
            var output = File("cumulative-output"); var calls = 0;
            var result = ValidationOperations.Batch(new([item, item with { Name = "second" }], output, MaximumCopiedBytes: 1), Limit(), (_, _) =>
            { calls++; return new ExecutionResult { ExitCode = 0, CleanupStatus = "PASS", InputComplete = true, OutputComplete = true }; });
            Assert(calls == 1 && result.Passed == 1 && result.Failed == 1 && result.Completed == 2 &&
                new FileInfo(Path.Combine(output, "first/input.json")).Length == 1 && !System.IO.File.Exists(Path.Combine(output, "second/input.json")) &&
                System.IO.File.ReadAllText(input, CorpusJson.Utf8) == "0", "Batch copied more physical source bytes than its cumulative cap or altered the source.");
        });
        check("validation negative cases require a targeted rejection and refuse unrelated stderr", () =>
        {
            var fixture = File("targeted-input"); Directory.CreateDirectory(fixture);
            var executable = Path.Combine(fixture, "Sezika.Evaluation" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            System.IO.File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("Native host missing."), executable);
            var positive = new ValidationCase("positive", executable, CorpusJson.FileHash(executable, Limit()), ["--score-capture"], [], [], [], 0);
            var negative = positive with { Name = "negative", ExpectedExitCode = 1 }; var calls = 0;
            Reject(() => ValidationOperations.Batch(new([positive, negative], File("missing-target-output")), Limit(), (_, _) => { calls++; return new(); }));
            Assert(calls == 0 && !Directory.Exists(File("missing-target-output")), "Untargeted negative reached any execution or created business output.");
            negative = negative with { ExpectedStandardError = "expected-contract-error" };
            var result = ValidationOperations.Batch(new([positive, negative], File("wrong-target-output")), Limit(), (request, _) =>
            {
                calls++; Directory.CreateDirectory(request.OutputDirectory); var rejected = calls == 2;
                System.IO.File.WriteAllText(Path.Combine(request.OutputDirectory, "stderr.bin"), rejected ? "unrelated-error" : "", CorpusJson.Utf8);
                return new ExecutionResult { ExitCode = rejected ? 1 : 0, Errors = rejected ? ["Process exit code 1"] : [], CleanupStatus = "PASS", InputComplete = true, OutputComplete = true };
            });
            Assert(calls == 2 && result.Passed == 1 && result.Failed == 1 && result.Cases[1].Status == "FAIL", "Unrelated nonzero exit was promoted to a targeted negative PASS.");
        });
        check("validation near-tie schema fixture preserves574 planned/1 observed/573 unprocessed", () =>
        {
            var directory = File("near-tie-input"); Directory.CreateDirectory(Path.Combine(directory, "run")); var planPath = Path.Combine(repo, "tests/fixtures/laya-oracle/quality-near-tie-plan.v1.json"); var plan = CorpusJson.Read(planPath, Limit()); var manifest = Path.Combine(directory, "manifest.json"); System.IO.File.WriteAllText(manifest, "{\"fixture\":true}", CorpusJson.Utf8); var manifestHash = CorpusJson.FileHash(manifest, Limit()); var capture = Path.Combine(directory, "capture.json");
            var ids = Enumerable.Range(0, 250).Select(i => "case-" + i).ToArray(); using (var file = new FileStream(Path.Combine(directory, "batch-plan.json"), FileMode.CreateNew))
            {
                using var writer = new Utf8JsonWriter(file); writer.WriteStartObject(); writer.WriteString("schema_version", "sezika.evaluation-oracle-batches.v1"); writer.WriteString("dataset_sha256", plan.GetProperty("sources_in_order")[0].GetProperty("sha256").GetString()); writer.WriteNumber("dataset_total", 250); writer.WritePropertyName("batches"); writer.WriteStartArray();
                for (var batch = 0; batch < 6; batch++) { writer.WriteStartObject(); writer.WritePropertyName("case_ids"); writer.WriteStartArray(); foreach (var id in ids.Skip(batch * 46).Take(46)) writer.WriteStringValue(id); writer.WriteEndArray(); writer.WriteEndObject(); } writer.WriteEndArray(); writer.WriteEndObject();
            }
            var captured = "{\"measurement_status\":\"complete\",\"provenance\":{\"implementation\":\"pinned_upstream_laya_agent\",\"model_id\":\"convaiinnovations/laya-multilingual\",\"model_revision\":\"052592a15d198d9ad47da779604259b10b47b7aa\",\"contract_sha256\":\"771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee\",\"cases_sha256\":\"" + manifestHash + "\",\"upstream_source_revision\":\"4066d5d5fbf08b66c6757ddeedbd797bd7655bc0\",\"weights_sha256\":\"9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204\",\"tokenizer_sha256\":\"609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f\"},\"cases\":[{\"id\":\"case-0\",\"primitive\":\"boolean\",\"status\":\"answered\",\"raw_logits\":[-2,-2.0005]}]}";
            System.IO.File.WriteAllText(capture, captured, CorpusJson.Utf8); System.IO.File.WriteAllText(Path.Combine(directory, "run/run.json"), "{\"plan\":\"batch-plan.json\",\"plan_sha256\":\"" + CorpusJson.FileHash(Path.Combine(directory, "batch-plan.json"), Limit()) + "\"}", CorpusJson.Utf8);
            System.IO.File.WriteAllText(Path.Combine(directory, "run/references.json"), "{\"schema_version\":\"sezika.evaluation-captures.v1\",\"batches\":[{\"manifest\":\"manifest.json\",\"manifest_sha256\":\"" + manifestHash + "\",\"capture\":\"capture.json\",\"capture_sha256\":\"" + CorpusJson.FileHash(capture, Limit()) + "\"}]}", CorpusJson.Utf8);
            var report = ValidationOperations.NearTies(new(planPath, CorpusJson.FileHash(planPath, Limit()), directory, ["run", null], File("near-tie-result.json"), 1), Limit()); Assert(report.Observed == 1 && report.Unprocessed == 573 && report.NearTies == 1 && report.SelectedFollowup.Length == 1 && report.Scope.Contains("no model run", StringComparison.Ordinal), "Near-tie planned denominator/source scope changed.");
        });
        check("validation structured exit3 reads exact status counts and Boolean without stderr", () =>
        {
            var fixture = File("structured-exit3-validator"); Directory.CreateDirectory(fixture);
            var executable = Path.Combine(fixture, "Sezika.TeacherTool" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            System.IO.File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("Native host missing."), executable);
            var positive = new ValidationCase("positive", executable, CorpusJson.FileHash(executable, Limit()), ["validate-records"], [], [],
                [new("result.json", true, "status", "valid_pending_human_review",
                    new() { ["total"] = Json("2"), ["pending"] = Json("2"), ["invalid"] = Json("0"), ["teacher_labels_permitted_for_training"] = Json("false") })], 0);
            var negative = positive with { Name = "invalid-records", ExpectedExitCode = 3,
                Outputs = [new("result.json", true, "status", "invalid_records_present",
                    new() { ["total"] = Json("2"), ["pending"] = Json("1"), ["invalid"] = Json("1"), ["teacher_labels_permitted_for_training"] = Json("false") })] };
            var calls = 0; var output = File("structured-exit3-output");
            var report = ValidationOperations.Batch(new([positive, negative], output), Limit(), (request, _) =>
            {
                var rejected = ++calls == 2; Directory.CreateDirectory(request.OutputDirectory);
                var text = rejected ? "{\"status\":\"invalid_records_present\",\"total\":2,\"pending\":1,\"invalid\":1,\"teacher_labels_permitted_for_training\":false}"
                    : "{\"status\":\"valid_pending_human_review\",\"total\":2,\"pending\":2,\"invalid\":0,\"teacher_labels_permitted_for_training\":false}";
                System.IO.File.WriteAllText(Path.Combine(request.WorkingDirectory, "result.json"), text, CorpusJson.Utf8);
                return new ExecutionResult { Status = rejected ? "FAIL" : "PASS", ExecutionStatus = rejected ? "FAILED" : "SUCCEEDED",
                    ExitCode = rejected ? 3 : 0, Errors = rejected ? ["Process exit code 3"] : [], CleanupStatus = "PASS", InputComplete = true, OutputComplete = true };
            });
            var actualOutput = Path.Combine(output, "invalid-records/result.json");
            Assert(calls == 2 && report.Completed == 2 && report.Passed == 2 && report.Failed == 0 && report.SyntheticTransport &&
                report.Cases[1].ExitCode == 3 && report.Cases[1].OutputSha256.SequenceEqual([CorpusJson.FileHash(actualOutput, Limit())]) &&
                !System.IO.File.Exists(Path.Combine(output, "invalid-records/process/stderr.bin")), "Structured exit3 lost exact output/denominator/hash or required nonexistent stderr.");
        });
        check("validation structured rejection refuses wrong JSON types values and unrelated declared stderr", () =>
        {
            var fixture = File("structured-mismatch-validator"); Directory.CreateDirectory(fixture);
            var executable = Path.Combine(fixture, "Sezika.TeacherTool" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            System.IO.File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("Native host missing."), executable);
            var positive = new ValidationCase("positive", executable, CorpusJson.FileHash(executable, Limit()), ["validate-records"], [], [], [], 0);
            var expected = new ValidationOutput("result.json", true, "status", "invalid_records_present",
                new() { ["total"] = Json("2"), ["teacher_labels_permitted_for_training"] = Json("false") });
            (string Name, string Total, string Permission, string? Stderr)[] variants =
                [("number-type", "\"2\"", "false", null), ("number-value", "3", "false", null),
                 ("boolean-type", "2", "\"false\"", null), ("boolean-value", "2", "true", null),
                 ("declared-stderr", "2", "false", "expected-contract-error")];
            var budget = Limit();
            for (var index = 0; index < variants.Length && index < 5; index++)
            {
                budget.Check(); var variant = variants[index]; var calls = 0;
                var negative = positive with { Name = "negative", ExpectedExitCode = 3, ExpectedStandardError = variant.Stderr, Outputs = [expected] };
                var report = ValidationOperations.Batch(new([positive, negative], File("structured-mismatch-" + variant.Name)), budget, (request, _) =>
                {
                    var rejected = ++calls == 2; Directory.CreateDirectory(request.OutputDirectory);
                    if (rejected)
                    {
                        System.IO.File.WriteAllText(Path.Combine(request.WorkingDirectory, "result.json"),
                            "{\"status\":\"invalid_records_present\",\"total\":" + variant.Total + ",\"teacher_labels_permitted_for_training\":" + variant.Permission + "}", CorpusJson.Utf8);
                        if (variant.Stderr is not null) System.IO.File.WriteAllText(Path.Combine(request.OutputDirectory, "stderr.bin"), "unrelated-error", CorpusJson.Utf8);
                    }
                    return new ExecutionResult { ExitCode = rejected ? 3 : 0, Errors = rejected ? ["Process exit code 3"] : [],
                        CleanupStatus = "PASS", InputComplete = true, OutputComplete = true };
                });
                var gate = variant.Stderr is null ? "Independent validator JSON field differs:" : "Expected validator rejection differs.";
                Assert(calls == 2 && report.Completed == 2 && report.Passed == 1 && report.Failed == 1 && report.Unprocessed == 0 &&
                    report.Cases[1].Status == "FAIL" && report.Cases[1].Failure?.Contains(gate, StringComparison.Ordinal) == true,
                    "Wrong typed JSON or declared stderr acquired a structured negative PASS: " + variant.Name);
            }
        });
    }
}
