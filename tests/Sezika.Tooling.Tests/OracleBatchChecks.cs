using System.Security.Cryptography;
using System.Text.Json;
using Sezika.Tooling;

internal static class OracleBatchChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var owned = Path.Combine(root, "oracle-batch-simulation"); Directory.CreateDirectory(owned);
        Budget Budget() => new(25, null, cancellation.Token);
        static void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        static OracleFilePin Pin(string path) { var bytes = File.ReadAllBytes(path); return new(Path.GetFullPath(path), bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))); }
        var packageIndex = 0;
        OraclePackagePin Package(string name, string filename)
        {
            var directory = Path.Combine(owned, "input-" + (++packageIndex) + "-" + name); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, filename); File.WriteAllText(path, "Explicit inert Oracle orchestration simulation bytes\n");
            var pin = Pin(path); return new(directory, [new(filename, pin.Bytes, pin.Sha256)]);
        }
        OracleProgramPin Program(string name)
        {
            var runtime = Package("runtime", name + (OperatingSystem.IsWindows() ? ".exe" : "")); var source = Package("source", "Program.cs");
            return new(Path.Combine(runtime.Root, runtime.Files[0].RelativePath), runtime, source);
        }
        var referenceProgram = Program("Sezika.ReferenceTool"); var actualProgram = Program("Sezika.OracleCapture"); var compareProgram = Program("Sezika.OracleCompare");
        var model = Package("model", "inert-model.bin"); var generic = Pin(Path.Combine(model.Root, "inert-model.bin"));
        var sourceFile = referenceProgram.Source.Files[0]; var sourcePin = new OracleFilePin(Path.Combine(referenceProgram.Source.Root, sourceFile.RelativePath), sourceFile.Bytes, sourceFile.Sha256);
        var executableFile = referenceProgram.RuntimeAndCode.Files[0]; var executablePin = new OracleFilePin(referenceProgram.Executable, executableFile.Bytes, executableFile.Sha256);
        var inputs = new OracleReferenceInputs(generic, generic, generic, generic, generic, generic, generic, generic, [sourcePin], executablePin, generic);
        var contractPath = Path.Combine(owned, "contract.json");
        File.WriteAllText(contractPath, "{\"schema_version\":\"sezika.laya-oracle-contract.port-derived.v2\"}"); var contract = Pin(contractPath);
        var cases = new List<OraclePlanBatch>();
        for (var index = 0; index < 2; index++)
        {
            cancellation.Token.ThrowIfCancellationRequested(); var path = Path.Combine(owned, "cases-" + index + ".json");
            File.WriteAllText(path, "{\"schema_version\":\"sezika.laya-oracle-inputs.v1\",\"cases\":[{\"id\":\"case-" + index + "\"}]}");
            cases.Add(new(path, Pin(path).Sha256, index, ["case-" + index]));
        }
        var planPath = Path.Combine(owned, "plan.json");
        TaskOperations.WriteNew(planPath, new OraclePlan("sezika.evaluation-oracle-batches.v1", generic.Sha256, 2, cases.ToArray()), OracleJsonContext.Default.OraclePlan);
        var request = new OracleBatchRequest("simulation-oracle-batch", Pin(planPath), contract, model, referenceProgram, actualProgram, compareProgram, inputs,
            Path.Combine(owned, "run"), MaximumBatches: 2, TimeoutSeconds: 60, CancelFile: Path.Combine(owned, "forwarded-cancel"));
        var calls = new List<string>(); var overflow = false;
        Task<ExecutionResult> Fixture(OracleStageInvocation invocation, Budget budget, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); budget.Check(); Assert(calls.Count < 12, "Simulation stage bound12.");
            calls.Add(invocation.PlanIndex + ":" + invocation.Stage);
            Directory.CreateDirectory(Path.GetDirectoryName(invocation.OutputPath)!);
            if (invocation.Stage is "reference" or "actual")
            {
                string? producerReceiptPath = null;
                if (invocation.Stage == "reference")
                {
                    var arguments = invocation.Request.ArgumentList;
                    Assert(arguments.Length is 7 or 9 && arguments[0] == "laya", "Reference CLI literal command/options count differs.");
                    var options = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (var index = 1; index < arguments.Length && index <= 7; index += 2)
                    {
                        budget.Check(); Assert(options.TryAdd(arguments[index], arguments[index + 1]), "Duplicate reference CLI option.");
                    }
                    Assert(options.ContainsKey("--request") && options.ContainsKey("--output") && options.ContainsKey("--timeout-seconds") &&
                        options.Keys.All(name => name is "--request" or "--output" or "--timeout-seconds" or "--cancel-file"), "Reference CLI fixed option identities differ.");
                    var wire = OracleIntegrity.Read(options["--request"], OracleJsonContext.Default.OracleLayaRequest, budget);
                    Assert(wire.TokenizerConfig == inputs.TokenizerConfig, "Simulation reference lost the mandatory fifth asset byte/hash pin.");
                    producerReceiptPath = options["--output"];
                    Assert(options["--timeout-seconds"] == wire.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                        options.GetValueOrDefault("--cancel-file") == wire.CancelFile && Path.GetDirectoryName(producerReceiptPath) == invocation.Request.WorkingDirectory &&
                        Path.GetDirectoryName(options["--request"]) == invocation.Request.WorkingDirectory,
                        "Reference CLI timeout/cancel/request/output does not match its exact typed wire.");
                }
                using var stream = new FileStream(invocation.OutputPath, FileMode.CreateNew, FileAccess.Write); using var writer = new Utf8JsonWriter(stream);
                writer.WriteStartObject(); writer.WriteString("schema_version", "sezika.oracle-batch.simulation.v1"); writer.WriteBoolean("simulation_only", true);
                writer.WriteBoolean("fixture_only", true); writer.WriteStartObject("provenance"); writer.WriteString("cases_sha256", invocation.Manifest.Sha256);
                writer.WriteString("contract_sha256", contract.Sha256); writer.WriteEndObject();
                if (invocation.Stage == "actual") writer.WriteString("reference_sha256", Pin(invocation.ReferencePath).Sha256);
                writer.WriteStartArray("cases"); writer.WriteStartObject(); writer.WriteString("id", "case-" + invocation.PlanIndex); writer.WriteEndObject(); writer.WriteEndArray(); writer.WriteEndObject();
                writer.Flush();
                if (invocation.Stage == "reference") File.WriteAllText(producerReceiptPath ?? throw new InvalidOperationException("Explicit simulation --output binding required."), "{\"status\":\"SIMULATION_ONLY\"}");
            }
            else
            {
                using var stream = new FileStream(invocation.OutputPath, FileMode.CreateNew, FileAccess.Write); using var writer = new Utf8JsonWriter(stream);
                var passed = invocation.PlanIndex != 0;
                writer.WriteStartObject(); writer.WriteString("schema_version", "sezika.oracle-comparison.simulation.v1"); writer.WriteBoolean("simulation_only", true);
                writer.WriteString("reference_sha256", Pin(invocation.ReferencePath).Sha256); writer.WriteString("actual_sha256", Pin(invocation.ActualPath).Sha256);
                writer.WriteString("contract_sha256", contract.Sha256); writer.WriteString("cases_sha256", invocation.Manifest.Sha256);
                writer.WriteNumber("manifest_count", 1); writer.WriteNumber("compared_count", 1); writer.WriteStartArray("selected_case_ids"); writer.WriteStringValue("case-" + invocation.PlanIndex); writer.WriteEndArray();
                writer.WriteStartArray("missing_case_ids"); writer.WriteEndArray(); writer.WriteBoolean("passed", passed); writer.WriteBoolean("full_manifest_passed", passed); writer.WriteEndObject(); writer.Flush();
            }
            var exit = invocation.Stage == "compare" && invocation.PlanIndex == 0 ? 1 : 0;
            var result = new ExecutionResult
            {
                Status = "SIMULATION", ExecutionStatus = "SIMULATION", CleanupStatus = "PASS", ExitCode = exit,
                Executable = invocation.Request.Executable, ArgumentList = invocation.Request.ArgumentList, WorkingDirectory = invocation.Request.WorkingDirectory,
                OutputDirectory = invocation.Request.OutputDirectory, InputComplete = true, OutputComplete = !(overflow && invocation.Stage == "actual"), RemainingProcesses = 0,
                Errors = exit == 1 ? ["Process exit code 1"] : []
            };
            Directory.CreateDirectory(invocation.Request.OutputDirectory);
            TaskOperations.WriteNew(Path.Combine(invocation.Request.OutputDirectory, "result.json"), result, ExecutionJsonContext.Default.ExecutionResult);
            return Task.FromResult(result);
        }
        OracleBatchReport? first = null;
        check("Oracle simulation preserves fixed reference actual compare order and continues numerical differences", () =>
        {
            first = OracleBatchOperations.SimulateAsync(request, Budget(), cancellation.Token, Fixture).GetAwaiter().GetResult();
            Assert(first.SimulationOnly && first.Status == "SIMULATION_SELECTED_BATCHES_COMPLETE_WITH_NUMERICAL_DIFFERENCES" && first.CompletedBatches == 2 &&
                first.NumericalDifferences == 1 && first.RemainingBatches == 0 && first.Scoring == "NOT_RUN_SEPARATE_STAGE" && first.ModelInferenceAcceptance == "NOT_ACCEPTED", "Simulation was mislabeled as model acceptance or changed difference semantics.");
            Assert(calls.SequenceEqual(new[] { "0:reference", "0:actual", "0:compare", "1:reference", "1:actual", "1:compare" }), "Fixed Oracle stage/plan order changed.");
            Assert(OracleIntegrity.Fingerprint(request) != OracleIntegrity.Fingerprint(request with { ReferenceInputs = inputs with { TokenizerConfig = contract } }),
                "Recovery task fingerprint omitted the mandatory fifth tokenizer asset.");
            var index = OracleIntegrity.Read(Path.Combine(request.OutputDirectory, "actuals.json"), OracleJsonContext.Default.OracleCaptureIndex, Budget());
            Assert(index.SimulationOnly && index.Batches.Length == 2, "Completed numeric difference was excluded from the separate scoring index.");
        });
        check("Oracle simulation resumes frozen same-task batches without repeating any stage", () =>
        {
            Assert(first is not null, "Positive control required."); var before = calls.Count;
            var resumed = request with { OutputDirectory = Path.Combine(owned, "resume"), RecoveryRunIndex = Pin(Path.Combine(request.OutputDirectory, "run.json")) };
            var report = OracleBatchOperations.SimulateAsync(resumed, Budget(), cancellation.Token, Fixture).GetAwaiter().GetResult();
            Assert(report.RecoveredBatches == 2 && report.CompletedBatches == 2 && report.NumericalDifferences == 1 && calls.Count == before && report.SimulationOnly,
                "Frozen same-task Oracle batch was re-executed or changed verdict.");
        });
        check("Oracle simulation rejects changed recovered output bytes before executing a stage", () =>
        {
            var frozen = first ?? throw new InvalidOperationException("Positive control required."); var before = calls.Count;
            var changed = Path.Combine(owned, "changed-reference.json"); File.Copy(frozen.Batches[0].Reference!.Path, changed, false); File.AppendAllText(changed, "\nchanged simulation fixture bytes");
            var rows = frozen.Batches.ToArray(); rows[0] = rows[0] with { Reference = rows[0].Reference! with { Path = changed } };
            var forged = Path.Combine(owned, "changed-recovery.json"); TaskOperations.WriteNew(forged, frozen with { Batches = rows }, OracleJsonContext.Default.OracleBatchReport);
            var report = OracleBatchOperations.SimulateAsync(request with { OutputDirectory = Path.Combine(owned, "reject-changed"), RecoveryRunIndex = Pin(forged) },
                Budget(), cancellation.Token, Fixture).GetAwaiter().GetResult();
            Assert(report.Status == "SIMULATION_INCOMPLETE" && report.CompletedBatches == 0 && report.RecoveredBatches == 0 && report.RemainingBatches == 2 &&
                report.Error is not null && report.Error.StartsWith("InvalidDataException: File bytes/SHA differ.", StringComparison.Ordinal) && calls.Count == before,
                "Changed recovered output was accepted or re-executed.");
            var rejectedIndex = OracleIntegrity.Read(Path.Combine(owned, "reject-changed", "actuals.json"), OracleJsonContext.Default.OracleCaptureIndex, Budget());
            Assert(rejectedIndex.SimulationOnly && rejectedIndex.Batches.Length == 0, "Rejected recovery was admitted into a completed scoring index.");
        });
        check("Oracle simulation aborts actual output EOF failure before comparison and later batches", () =>
        {
            var before = calls.Count; overflow = true;
            try
            {
                var report = OracleBatchOperations.SimulateAsync(request with { OutputDirectory = Path.Combine(owned, "reject-eof") }, Budget(), cancellation.Token, Fixture).GetAwaiter().GetResult();
                Assert(report.Status == "SIMULATION_INCOMPLETE" && report.CompletedBatches == 0 && report.RemainingBatches == 2 && calls.Count == before + 2 &&
                    report.Batches[0].Status == "ABORTED_EXECUTION_OR_EVIDENCE_FAILURE" && report.Batches[1].Status == "UNPROCESSED", "EOF failure was misclassified as a numerical difference or did not abort.");
                var index = OracleIntegrity.Read(Path.Combine(owned, "reject-eof", "actuals.json"), OracleJsonContext.Default.OracleCaptureIndex, Budget());
                Assert(index.Batches.Length == 0, "Failed/partial capture entered a completed scoring index.");
            }
            finally { overflow = false; }
        });
        check("Oracle simulation cancellation preserves a closed cancellation receipt without starting a stage", () =>
        {
            var before = calls.Count; var cancelFile = Path.Combine(owned, "CANCEL"); File.WriteAllText(cancelFile, "simulation cancellation");
            var report = OracleBatchOperations.SimulateAsync(request with { OutputDirectory = Path.Combine(owned, "cancelled"), CancelFile = cancelFile },
                Budget(), cancellation.Token, Fixture).GetAwaiter().GetResult();
            Assert(report.Status == "SIMULATION_CANCELLED" && report.CompletedBatches == 0 && report.Error is not null && calls.Count == before &&
                report.Scoring == "NOT_RUN_SEPARATE_STAGE", "Cancellation was lost, mislabeled as parity, or started a stage.");
        });
    }
}
