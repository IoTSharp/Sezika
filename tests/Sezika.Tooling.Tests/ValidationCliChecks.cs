using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sezika.Tooling;

internal static class ValidationCliChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        var executable = Environment.GetEnvironmentVariable("SEZIKA_VALIDATION_CLI_PATH")
            ?? throw new InvalidOperationException("Set SEZIKA_VALIDATION_CLI_PATH to the exact built Sezika.Tooling native apphost.");
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) ||
            Path.GetFileName(executable) is not ("Sezika.Tooling" or "Sezika.Tooling.exe")) throw new InvalidDataException("Exact validation CLI apphost required.");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        Budget Limit() => new(110, null, cancellation.Token);
        static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var binaryHash = CorpusJson.FileHash(executable, Limit()); var ordinal = 0;
        JsonElement Invoke<T>(string operation, T request, JsonTypeInfo<T> type, string output, int expected, string? expectedStatus = null)
        {
            if (++ordinal > 4) throw new InvalidDataException("CLI check count bound4.");
            var requestPath = Path.Combine(root, "cli-request-" + ordinal + ".json"); CorpusJson.Write(requestPath, request, type, Limit());
            var execution = ProcessExecutor.ExecuteAsync(new ExecutionRequest
            {
                Executable = executable, ArgumentList = ["validation", operation, "--request", requestPath, "--output", output, "--timeout-seconds", "20"],
                WorkingDirectory = Directory.GetCurrentDirectory(), OutputDirectory = Path.Combine(root, "cli-process-" + ordinal),
                TimeoutSeconds = 25, CleanupSeconds = 5, MaximumProcesses = 32, MaximumOutputBytes = 1048576,
                BrokerExecutable = OperatingSystem.IsLinux() ? executable : null
            }, Limit(), cancellation.Token).GetAwaiter().GetResult();
            Assert(execution.RootPid is > 0 && execution.Launcher is not null && execution.Processes.Length > 0 &&
                execution.ExitCode == expected && execution.InputComplete && execution.OutputComplete &&
                !execution.OutputLimitExceeded && execution.CleanupStatus == "PASS" && execution.RemainingProcesses == 0,
                "Real CLI process/exit/drain/cleanup differs.");
            Assert(expected == 0 ? execution.Status == "PASS" && execution.ExecutionStatus == "SUCCEEDED" :
                execution.Status == "FAIL" && execution.ExecutionStatus == "FAILED", "Expected rejection became native process success.");
            var actual = CorpusJson.Read(output, Limit());
            if (expectedStatus is not null) Assert(actual.GetProperty("Status").GetString() == expectedStatus, "Exact CLI report status differs.");
            Assert(CorpusJson.FileHash(executable, Limit()) == binaryHash, "CLI image changed during checks.");
            return actual;
        }
        check("validation CLI rejects Output binding before reading business inputs", () =>
        {
            var output = Path.Combine(root, "cli-binding.json"); var forbidden = Path.Combine(root, "must-not-create.json");
            Invoke("profile", new ProfileValidationRequest([Path.Combine(root, "must-not-open-profile.json")], forbidden),
                ValidationJsonContext.Default.ProfileValidationRequest, output, 1, "FAIL");
            Assert(!File.Exists(forbidden), "Mismatched typed Output was written.");
        });
        check("validation CLI blocked admission preserves blocked and exit1", () =>
        {
            var output = Path.Combine(root, "cli-blocked.json");
            Invoke("data-admission", new DataAdmissionRequest(Path.Combine(Directory.GetCurrentDirectory(), "datasets/s4-05/example-manifest.json"), output),
                ValidationJsonContext.Default.DataAdmissionRequest, output, 1, "blocked");
        });
        check("validation CLI synthetic checks passed remain pending human and exit3", () =>
        {
            var fixture = Path.Combine(root, "cli-synthetic-pending-input"); Directory.CreateDirectory(fixture);
            const string stamp = "2026-01-01T00:00:00Z";
            var splits = new[] { "Development", "Calibration", "SealedTest" }; var texts = new[] { "Alpha violet orbital", "Zebra canyon river", "Quartz meadow amber" };
            var rows = new List<string>();
            for (var index = 0; index < 3; index++) rows.Add("{\"id\":\"synthetic-" + index + "\",\"source_id\":\"synthetic-only\",\"family\":\"family-" + index + "\",\"entities\":[\"entity-" + index + "\"],\"language\":\"en\",\"domain\":\"synthetic\",\"split\":\"" + splits[index] + "\",\"text\":\"" + texts[index] + "\",\"derivation\":\"Original\",\"parent_id\":null,\"human_review\":\"Approved\",\"reviewer\":\"synthetic-fixture-reviewer-not-real-approval\",\"reviewed_utc\":\"" + stamp + "\",\"review_note\":\"Synthetic protocol fixture only\",\"exposure\":\"Unseen\"}");
            var records = Path.Combine(fixture, "records.jsonl"); File.WriteAllText(records, string.Join('\n', rows) + "\n", CorpusJson.Utf8);
            var recordsHash = CorpusJson.FileHash(records, Limit()); var license = Path.Combine(fixture, "license.json");
            File.WriteAllText(license, "{\"SyntheticFixtureOnly\":true}", CorpusJson.Utf8); var licenseHash = CorpusJson.FileHash(license, Limit());
            var seal = Path.Combine(fixture, "seal.json"); File.WriteAllText(seal, "{\"dataset_id\":\"synthetic-cli-pending-not-real-acceptance\",\"records_sha256\":\"" + recordsHash + "\",\"custodian\":\"synthetic-only\",\"sealed_utc\":\"" + stamp + "\"}", CorpusJson.Utf8); var sealHash = CorpusJson.FileHash(seal, Limit());
            var manifest = Path.Combine(fixture, "manifest.json");
            File.WriteAllText(manifest, "{\"schema_version\":1,\"dataset_id\":\"synthetic-cli-pending-not-real-acceptance\",\"stage\":\"Candidate\",\"intended_use\":\"Research\",\"records\":{\"path\":\"records.jsonl\",\"sha256\":\"" + recordsHash + "\"},\"sources\":[{\"id\":\"synthetic-only\",\"upstream_id\":\"synthetic-only\",\"upstream_split\":\"synthetic-only\",\"revision\":\"synthetic-v1\",\"origin\":\"Original\",\"raw\":{\"path\":\"records.jsonl\",\"sha256\":\"" + recordsHash + "\"},\"license_expression\":\"Apache-2.0\",\"license_review\":\"Approved\",\"license_reviewer\":\"synthetic-fixture-not-human-acceptance\",\"license_reviewed_utc\":\"" + stamp + "\",\"license_evidence\":{\"path\":\"license.json\",\"sha256\":\"" + licenseHash + "\"},\"allowed_uses\":[\"Research\"],\"allowed_splits\":[\"Development\",\"Calibration\",\"SealedTest\"],\"exposure\":\"Unseen\"}],\"test_seal\":{\"status\":\"Sealed\",\"custodian\":\"synthetic-only\",\"sealed_utc\":\"" + stamp + "\",\"evidence\":{\"path\":\"seal.json\",\"sha256\":\"" + sealHash + "\"}}}", CorpusJson.Utf8);
            var output = Path.Combine(root, "cli-pending.json"); var result = Invoke("data-admission", new DataAdmissionRequest(manifest, output),
                ValidationJsonContext.Default.DataAdmissionRequest, output, 3, "checks_passed_pending_human_acceptance");
            Assert(result.GetProperty("RequiresHumanAcceptance").GetBoolean() && !result.GetProperty("RepresentsModelQuality").GetBoolean(), "Synthetic pending acquired human or quality acceptance.");
        });
        check("validation CLI writes one file report without duplicate CreateNew", () =>
        {
            var output = Path.Combine(root, "cli-profile.json");
            Invoke("profile", new ProfileValidationRequest([Path.Combine(Directory.GetCurrentDirectory(), "docs/evidence/s346-continuation-2026-09-26/profile-simd-short1.json")], output),
                ValidationJsonContext.Default.ProfileValidationRequest, output, 0, "PASS_HISTORICAL_PROFILE_CONSISTENCY_ONLY");
        });
    }
}
