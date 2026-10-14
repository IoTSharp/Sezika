using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using static Sezika.Tooling.OracleIntegrity;

namespace Sezika.Tooling;

internal static class OracleBatchOperations
{
    private const string FrozenTokenizerConfigSha256 = "424b69444bf7b5809dc2cd2e36d0bd71b8055124dd24274d6db3c655d38205e7";
    internal static OracleBatchRequest ReadRequest(string path, Budget budget) => Read(path, OracleJsonContext.Default.OracleBatchRequest, budget, 16777216);
    internal static Task<OracleBatchReport> RunAsync(OracleBatchRequest request, Budget budget, CancellationToken cancellation) =>
        RunCoreAsync(request, budget, cancellation, null);
    // Internal test seam only. No request or public CLI flag can choose this.
    internal static Task<OracleBatchReport> SimulateAsync(OracleBatchRequest request, Budget budget, CancellationToken cancellation,
        Func<OracleStageInvocation, Budget, CancellationToken, Task<ExecutionResult>> fixtureExecutor) =>
        RunCoreAsync(request, budget, cancellation, fixtureExecutor ?? throw new ArgumentNullException(nameof(fixtureExecutor)));

    private static async Task<OracleBatchReport> RunCoreAsync(OracleBatchRequest request, Budget outerBudget, CancellationToken cancellation,
        Func<OracleStageInvocation, Budget, CancellationToken, Task<ExecutionResult>>? fixtureExecutor)
    {
        Require(request.MaximumBatches is >= 1 and <= 256 && request.BatchOffset is >= 0 and <= 255 && request.TimeoutSeconds is >= 60 and <= 1740,
            "Oracle batch count/offset/wall bounds differ.");
        SafePaths.Relative(request.TaskId); Require(!request.TaskId.Contains('/') && request.TaskId.Length <= 128, "Explicit portable Oracle task ID required.");
        Require(Path.IsPathFullyQualified(request.OutputDirectory), "Absolute new Oracle output root required.");
        var simulation = fixtureExecutor is not null; var clock = Stopwatch.StartNew();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, outerBudget.Cancellation);
        linked.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        var budget = new Budget(request.TimeoutSeconds, request.CancelFile, linked.Token);
        var root = Path.GetFullPath(request.OutputDirectory); SafePaths.NoLinks(root, outerBudget);
        Require(!Path.Exists(root), "Oracle output exists; previous evidence must be preserved.");
        var fingerprint = Fingerprint(request); Directory.CreateDirectory(root);
        var rows = new List<OracleBatchResult>(); OraclePlan? plan = null; string? failure = null;
        var status = "INCOMPLETE"; var recovered = 0; var completed = 0; var differences = 0; var selected = 0; var ported = false;
        void Gate(bool nextStage = false)
        {
            outerBudget.Check(); budget.Check();
            if (nextStage && clock.Elapsed.TotalSeconds >= request.TimeoutSeconds - 35) throw new TimeoutException("Oracle remaining budget cannot admit another stage.");
        }
        OracleProgramPin RoleProgram(string stage) => stage switch
        { "reference" => request.ReferenceTool!, "actual" => request.ActualTool, "compare" => request.CompareTool, _ => throw new InvalidDataException("Unknown Oracle stage.") };
        async Task<(OracleStageResult Stage, ExecutionResult Execution)> Execute(string stage, int planIndex, string batchDirectory,
            OracleFilePin manifest, string reference, string actual, string output, List<OracleStageResult> stages)
        {
            Gate(nextStage: true); var program = RoleProgram(stage); ProgramPin(program, stage, budget, simulation);
            Package(request.Model, budget); Verify(request.Contract, budget, 1048576); Verify(manifest, budget, 33554432);
            if (request.BrokerTool is not null) ProgramPin(request.BrokerTool, "broker", budget, simulation);
            var remaining = (int)Math.Floor(request.TimeoutSeconds - clock.Elapsed.TotalSeconds - 25);
            var limit = Math.Min(stage == "compare" ? 45 : 620, remaining);
            if (limit < 10) throw new TimeoutException("Insufficient Oracle stage budget.");
            var internalSeconds = Math.Min(stage == "compare" ? 30 : 600, limit - 5);
            var processDirectory = SafePaths.Within(batchDirectory, "process-" + stage, budget);
            OracleFilePin? inputRequest = null; OracleFilePin? producerReceipt = null; string? referenceProducerPath = null; string[] arguments;
            if (stage == "reference")
            {
                var inputs = request.ReferenceInputs!; VerifyReferenceInputs(inputs, request.ReferenceTool!, budget);
                var wire = new OracleLayaRequest(inputs.Weights, inputs.EncoderConfig, inputs.AgentConfig, inputs.Tokenizer, inputs.TokenizerConfig,
                    inputs.SourceLock, inputs.CommonSource, inputs.AgentSource, manifest, request.Contract, inputs.ProgramSources,
                    inputs.RuntimeExecutable, inputs.RuntimeAssembly, Path.GetDirectoryName(output)!, processDirectory,
                    inputs.BuildReceipt, rows[planIndex - request.BatchOffset].CaseIds.Length, internalSeconds, inputs.MaximumResidentBytes, request.CancelFile);
                var path = SafePaths.Within(batchDirectory, "reference-request.json", budget);
                TaskOperations.WriteNew(path, wire, OracleJsonContext.Default.OracleLayaRequest); inputRequest = Pin(path, budget);
                referenceProducerPath = SafePaths.Within(batchDirectory, "reference-receipt.json", budget);
                arguments = ReferenceArguments(inputRequest.Path, referenceProducerPath, wire);
            }
            else if (stage == "actual")
            {
                arguments = ["--reference", reference, "--model", request.Model.Root, "--cases", manifest.Path, "--contract", request.Contract.Path,
                    "--output", Path.GetDirectoryName(output)!, "--backend", "cuda", "--length-policy", "laya_compatible", "--max-cases",
                    rows[planIndex - request.BatchOffset].CaseIds.Length.ToString(CultureInfo.InvariantCulture), "--timeout-seconds", internalSeconds.ToString(CultureInfo.InvariantCulture)];
                if (request.RequireActualAot) arguments = arguments.Append("--require-aot").ToArray();
            }
            else arguments = [reference, actual, request.Contract.Path, manifest.Path, output, internalSeconds.ToString(CultureInfo.InvariantCulture)];
            var executionRequest = new ExecutionRequest
            {
                Executable = program.Executable, ArgumentList = arguments, WorkingDirectory = batchDirectory, OutputDirectory = processDirectory,
                TimeoutSeconds = limit, CleanupSeconds = 5, MaximumProcesses = 64, MaximumOutputBytes = 1048576,
                BrokerExecutable = request.BrokerTool?.Executable, CancelFile = request.CancelFile,
                Environment = new(StringComparer.Ordinal) { ["HF_HUB_OFFLINE"] = "1", ["TRANSFORMERS_OFFLINE"] = "1", ["HF_HUB_DISABLE_TELEMETRY"] = "1", ["TOKENIZERS_PARALLELISM"] = "false" }
            };
            var invocation = new OracleStageInvocation(stage, planIndex, output, reference, actual, manifest, executionRequest, simulation);
            var result = fixtureExecutor is null ? await ProcessExecutor.ExecuteAsync(executionRequest, budget, linked.Token).ConfigureAwait(false)
                : await fixtureExecutor(invocation, budget, linked.Token).ConfigureAwait(false);
            var receiptPath = Path.Combine(processDirectory, "result.json");
            var receipt = File.Exists(receiptPath) ? Pin(receiptPath, new Budget(10, null, CancellationToken.None), 16777216) : null;
            var stageResult = new OracleStageResult(stage, "EXECUTION_RETURNED_NOT_YET_VALIDATED", result.ExitCode, receiptPath, receipt?.Sha256, null, simulation, inputRequest);
            stages.Add(stageResult);
            VerifyExecution(result, stage == "compare", simulation, budget); Gate();
            Require(receipt is not null || simulation, "Actual Oracle executor immutable receipt is absent.");
            Require(result.Executable == program.Executable && result.ArgumentList.SequenceEqual(arguments) && result.OutputDirectory == processDirectory,
                "Oracle native execution receipt belongs to another declared invocation.");
            ProgramPin(program, stage, budget, simulation); Verify(manifest, budget, 33554432);
            var outputPin = Pin(output, budget);
            if (stage == "reference")
            {
                producerReceipt = Pin(referenceProducerPath ?? throw new InvalidDataException("Reference producer --output binding is absent."), budget, 1048576);
                if (!simulation) ValidateProducerReceipt(producerReceipt, outputPin, rows[planIndex - request.BatchOffset].CaseIds.Length, budget);
            }
            stageResult = stageResult with { Status = simulation ? "SIMULATION_EXECUTION_COMPLETE" : result.ExitCode == 1 ? "NUMERICAL_DIFFERENCE_EXECUTION_COMPLETE" : "NATIVE_EXECUTION_COMPLETE",
                Output = outputPin, ProducerReceipt = producerReceipt };
            stages[^1] = stageResult; return (stageResult, result);
        }
        try
        {
            Gate(); Verify(request.Plan, budget, 1048576); ported = OracleCaptureEvidence.PortedContract(request.Contract, budget);
            plan = Read(request.Plan.Path, OracleJsonContext.Default.OraclePlan, budget, 1048576); ValidatePlan(plan, request, budget);
            selected = Math.Min(request.MaximumBatches, plan.Batches.Length - request.BatchOffset);
            ValidateConfiguration(request, ported, budget, simulation, root);
            for (var index = request.BatchOffset; index < request.BatchOffset + selected; index++)
            {
                Gate(); var item = plan.Batches[index]; var path = Path.GetFullPath(item.Manifest, Path.GetDirectoryName(request.Plan.Path)!);
                var manifest = Pin(path, budget); Require(manifest.Sha256 == item.ManifestSha256, "Frozen Oracle batch manifest SHA differs.");
                OracleCaptureEvidence.Manifest(manifest, item.CaseIds, budget);
                rows.Add(new(index, item.Offset, item.CaseIds, "UNPROCESSED", manifest, null, null, null, null, [], null));
            }
            var recovery = Recovery(request, fingerprint, ported, simulation, budget);
            var legacy = LegacyReferences(request, ported, budget);
            for (var index = 0; index < rows.Count; index++)
            {
                Gate(nextStage: true); var row = rows[index]; var stages = new List<OracleStageResult>();
                if (recovery.TryGetValue(row.PlanIndex, out var frozen))
                {
                    Require(frozen.Manifest == row.Manifest && frozen.Offset == row.Offset && frozen.CaseIds.SequenceEqual(row.CaseIds), "Recovered Oracle batch inputs/order differ.");
                    rows[index] = frozen with { RecoveredFrom = request.RecoveryRunIndex!.Path }; recovered++; completed++;
                    if (frozen.FullManifestPassed != true) differences++;
                    Console.Error.WriteLine($"oracle recovered plan-index={row.PlanIndex} complete={completed}/{selected}; no stage re-execution"); continue;
                }
                var directory = SafePaths.Within(root, "batch-" + (row.PlanIndex + 1).ToString("D3", CultureInfo.InvariantCulture), budget); Directory.CreateDirectory(directory);
                var referencePath = Path.Combine(directory, "reference", "capture.json"); var actualPath = Path.Combine(directory, "actual", "capture.json");
                var comparisonPath = Path.Combine(directory, "comparison.json");
                try
                {
                    OracleFilePin reference;
                    if (!ported)
                    {
                        Require(legacy.TryGetValue(row.Manifest.Sha256, out var frozenReference), "Selected batch is absent from frozen legacy upstream reference index.");
                        reference = frozenReference!; stages.Add(new("reference", "REUSED_FROZEN_LEGACY_INPUT_DECLARATION", null, "", null, reference, simulation));
                    }
                    else reference = (await Execute("reference", row.PlanIndex, directory, row.Manifest, referencePath, actualPath, referencePath, stages).ConfigureAwait(false)).Stage.Output!;
                    OracleCaptureEvidence.Capture(reference, row.Manifest, request.Contract, row.CaseIds, true, ported, simulation, null, budget);
                    if (ported && !simulation) ValidateReferenceProvenance(reference, request.ReferenceInputs!, budget);
                    var actual = (await Execute("actual", row.PlanIndex, directory, row.Manifest, reference.Path, actualPath, actualPath, stages).ConfigureAwait(false)).Stage.Output!;
                    OracleCaptureEvidence.Capture(actual, row.Manifest, request.Contract, row.CaseIds, false, ported, simulation, reference.Sha256, budget);
                    var comparison = await Execute("compare", row.PlanIndex, directory, row.Manifest, reference.Path, actual.Path, comparisonPath, stages).ConfigureAwait(false);
                    var full = OracleCaptureEvidence.Comparison(comparison.Stage.Output!, reference, actual, row.Manifest, request.Contract, row.CaseIds,
                        comparison.Execution.ExitCode!.Value, simulation, budget);
                    Verify(reference, budget, 33554432); Verify(actual, budget, 33554432); Verify(row.Manifest, budget, 33554432);
                    row = row with { Status = full ? "COMPLETE" : "COMPLETE_WITH_NUMERICAL_DIFFERENCES", Reference = reference, Actual = actual,
                        Comparison = comparison.Stage.Output, FullManifestPassed = full, Stages = stages.ToArray() };
                    TaskOperations.WriteNew(Path.Combine(directory, "batch-result.json"), row, OracleJsonContext.Default.OracleBatchResult);
                    rows[index] = row; completed++; if (!full) differences++;
                    Console.Error.WriteLine($"oracle batches={completed}/{selected}; differences={differences}; remaining={selected - completed}");
                }
                catch (Exception error)
                {
                    rows[index] = row with { Status = "ABORTED_EXECUTION_OR_EVIDENCE_FAILURE", Stages = stages.ToArray(), Error = error.GetType().Name + ": " + error.Message };
                    if (!File.Exists(Path.Combine(directory, "batch-result.json")))
                        TaskOperations.WriteNew(Path.Combine(directory, "batch-result.json"), rows[index], OracleJsonContext.Default.OracleBatchResult);
                    throw;
                }
            }
            Gate(); Verify(request.Plan, budget, 1048576); Verify(request.Contract, budget, 1048576);
            Package(request.Model, budget);
            foreach (var role in new[] { "actual", "compare" }) { budget.Check(); ProgramPin(RoleProgram(role), role, budget, simulation); }
            if (ported) { ProgramPin(request.ReferenceTool!, "reference", budget, simulation); VerifyReferenceInputs(request.ReferenceInputs!, request.ReferenceTool!, budget); }
            if (request.BrokerTool is not null) ProgramPin(request.BrokerTool, "broker", budget, simulation);
            if (request.RecoveryRunIndex is not null) Verify(request.RecoveryRunIndex, budget, 16777216);
            if (request.LegacyFrozenReferenceIndex is not null) Verify(request.LegacyFrozenReferenceIndex, budget, 1048576);
            status = differences == 0 ? "SELECTED_BATCHES_COMPLETE" : "SELECTED_BATCHES_COMPLETE_WITH_NUMERICAL_DIFFERENCES";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or TimeoutException or OperationCanceledException or OverflowException or KeyNotFoundException)
        { status = error is OperationCanceledException ? "CANCELLED" : error is TimeoutException ? "BUDGET_EXHAUSTED" : "INCOMPLETE"; failure = error.GetType().Name + ": " + error.Message; }
        finally
        {
            var finalBudget = new Budget(30, null, CancellationToken.None); var closed = rows.Where(row => row.Status is "COMPLETE" or "COMPLETE_WITH_NUMERICAL_DIFFERENCES").ToArray();
            OracleCaptureIndex Index(bool reference) => new(ported ? "sezika.evaluation-captures.port-derived.v2" : "sezika.evaluation-captures.v1",
                closed.Select(row => new OracleCaptureIndexEntry((reference ? row.Reference : row.Actual)!.Path, (reference ? row.Reference : row.Actual)!.Sha256,
                    row.Manifest.Path, row.Manifest.Sha256)).ToArray(), simulation ? "SIMULATION_ONLY" : ported ? "independently_ported_original_laya_weights_capture" : "legacy_upstream_v1_frozen_input_declaration", simulation);
            finalBudget.Check(); TaskOperations.WriteNew(Path.Combine(root, "references.json"), Index(true), OracleJsonContext.Default.OracleCaptureIndex);
            finalBudget.Check(); TaskOperations.WriteNew(Path.Combine(root, "actuals.json"), Index(false), OracleJsonContext.Default.OracleCaptureIndex);
            var report = new OracleBatchReport("sezika.oracle-batch-run.v2", request.TaskId, fingerprint, simulation ? "SIMULATION_" + status : status,
                request.Plan, request.Contract, plan?.DatasetSha256 ?? "UNKNOWN", plan?.DatasetTotal ?? 0, request.BatchOffset, selected, completed, recovered, differences,
                Math.Max(0, selected - completed), rows.ToArray(), clock.Elapsed.TotalSeconds, request.TimeoutSeconds,
                Math.Max(0, request.TimeoutSeconds - clock.Elapsed.TotalSeconds), simulation, failure);
            finalBudget.Check(); TaskOperations.WriteNew(Path.Combine(root, "run.json"), report, OracleJsonContext.Default.OracleBatchReport);
        }
        return Read(Path.Combine(root, "run.json"), OracleJsonContext.Default.OracleBatchReport, new Budget(10, null, CancellationToken.None), 16777216);
    }

    private static void ProgramPin(OracleProgramPin program, string stage, Budget budget, bool simulation) => VerifyProgram(program,
        stage switch { "reference" => "Sezika.ReferenceTool", "actual" => "Sezika.OracleCapture", "compare" => "Sezika.OracleCompare", "broker" => "Sezika.Tooling", _ => "INVALID" }, budget, simulation);
    private static void ValidatePlan(OraclePlan plan, OracleBatchRequest request, Budget budget)
    {
        Require(plan.SchemaVersion == "sezika.evaluation-oracle-batches.v1" && IsSha(plan.DatasetSha256) && plan.DatasetTotal is > 0 and <= 10000 &&
            plan.Batches is { Length: > 0 and <= 256 } && request.BatchOffset < plan.Batches.Length, "Invalid frozen Oracle batch plan.");
        var ids = new HashSet<string>(StringComparer.Ordinal); var offset = 0;
        foreach (var item in plan.Batches)
        {
            budget.Check();
            Require(item.CaseIds is { Length: > 0 and <= 46 } && item.Offset == offset && IsSha(item.ManifestSha256), "Oracle batch fixed-order/count/hash differs.");
            foreach (var id in item.CaseIds) { budget.Check(); Require(id.Length is > 0 and <= 128 && ids.Add(id), "Duplicate/invalid frozen Oracle case ID."); }
            offset += item.CaseIds.Length;
        }
        Require(offset == plan.DatasetTotal, "Oracle plan dataset denominator differs.");
    }
    private static void ValidateConfiguration(OracleBatchRequest request, bool ported, Budget budget, bool simulation, string output)
    {
        Require(ported ? request.ReferenceTool is not null && request.ReferenceInputs is not null && request.LegacyFrozenReferenceIndex is null :
            request.ReferenceTool is null && request.ReferenceInputs is null && request.LegacyFrozenReferenceIndex is not null,
            "New port generation and frozen historical upstream inputs must be distinct explicit modes.");
        Package(request.Model, budget); ProgramPin(request.ActualTool, "actual", budget, simulation); ProgramPin(request.CompareTool, "compare", budget, simulation);
        var programs = new[] { request.ReferenceTool, request.ActualTool, request.CompareTool, request.BrokerTool }.Where(program => program is not null).Cast<OracleProgramPin>().ToArray();
        foreach (var package in programs.SelectMany(program => new[] { program.Source, program.RuntimeAndCode }).Append(request.Model))
        {
            budget.Check(); var input = Path.TrimEndingDirectorySeparator(package.Root); var prefix = input + Path.DirectorySeparatorChar;
            Require(output != input && !output.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Oracle output must be outside pinned packages.");
        }
        if (ported)
        {
            ProgramPin(request.ReferenceTool!, "reference", budget, simulation); VerifyReferenceInputs(request.ReferenceInputs!, request.ReferenceTool!, budget);
            if (!simulation) Require(request.ReferenceInputs!.TokenizerConfig.Sha256 == FrozenTokenizerConfigSha256, "Reference tokenizer configuration is not the frozen fifth Laya asset.");
        }
        if (request.BrokerTool is not null) ProgramPin(request.BrokerTool, "broker", budget, simulation);
        if (!simulation && OperatingSystem.IsLinux()) Require(request.BrokerTool is not null, "Linux Oracle execution requires a complete pinned C# broker package.");
    }
    private static void VerifyReferenceInputs(OracleReferenceInputs inputs, OracleProgramPin program, Budget budget)
    {
        Require(inputs.ProgramSources is { Length: > 0 and <= 256 } && inputs.MaximumResidentBytes is >= 1048576 and <= 2147483648,
            "Independent reference source/resident bound.");
        foreach (var file in new[] { inputs.Weights, inputs.EncoderConfig, inputs.AgentConfig, inputs.Tokenizer, inputs.TokenizerConfig, inputs.SourceLock,
            inputs.CommonSource, inputs.AgentSource, inputs.RuntimeExecutable, inputs.BuildReceipt }.Concat(inputs.ProgramSources)) Verify(file, budget);
        if (inputs.RuntimeAssembly is not null) Verify(inputs.RuntimeAssembly, budget);
        Require(inputs.RuntimeExecutable.Path == program.Executable, "Reference wire runtime executable differs from the complete native package.");
        foreach (var file in inputs.ProgramSources)
        {
            budget.Check(); var relative = Path.GetRelativePath(program.Source.Root, file.Path).Replace('\\', '/');
            Require(program.Source.Files.Any(source => source.RelativePath == relative && source.Bytes == file.Bytes && source.Sha256 == file.Sha256), "Reference program source is outside its complete source pin.");
        }
        var assembly = inputs.RuntimeAssembly;
        if (assembly is not null)
        {
            var relative = Path.GetRelativePath(program.RuntimeAndCode.Root, assembly.Path).Replace('\\', '/');
            Require(program.RuntimeAndCode.Files.Any(file => file.RelativePath == relative && file.Bytes == assembly.Bytes && file.Sha256 == assembly.Sha256), "Reference managed assembly is outside its complete runtime/code package.");
        }
    }
    private static void ValidateProducerReceipt(OracleFilePin receipt, OracleFilePin capture, int count, Budget budget)
    {
        using var document = Document(receipt.Path, budget, 1048576); var root = document.RootElement;
        Require(OracleCaptureEvidence.Text(root, "status") == "PASS_independent_laya_port_capture" && root.GetProperty("planned").GetInt32() == count &&
            root.GetProperty("completed").GetInt32() == count && !root.GetProperty("upstream_executed").GetBoolean() &&
            !root.GetProperty("represents_parity").GetBoolean() && root.GetProperty("error").ValueKind == JsonValueKind.Null, "Independent reference producer did not complete this exact batch.");
        Require(OracleCaptureEvidence.Text(root, "output_directory") == Path.GetDirectoryName(capture.Path) &&
            root.GetProperty("answered").GetInt32() + root.GetProperty("failed").GetInt32() == count, "Independent producer output ownership/row denominator differs.");
        Require(root.GetProperty("capture").Deserialize(OracleJsonContext.Default.OracleFilePin) == capture, "Independent reference receipt capture pin differs.");
    }
    private static void ValidateReferenceProvenance(OracleFilePin capture, OracleReferenceInputs inputs, Budget budget)
    {
        using var document = Document(capture.Path, budget); var provenance = document.RootElement.GetProperty("provenance");
        foreach (var binding in new[] { ("weights_sha256", inputs.Weights), ("tokenizer_sha256", inputs.Tokenizer), ("tokenizer_config_sha256", inputs.TokenizerConfig), ("encoder_config_sha256", inputs.EncoderConfig),
            ("agent_config_sha256", inputs.AgentConfig), ("source_lock_sha256", inputs.SourceLock), ("upstream_agent_sha256", inputs.AgentSource), ("upstream_common_sha256", inputs.CommonSource) })
        { budget.Check(); Require(OracleCaptureEvidence.Text(provenance, binding.Item1) == binding.Item2.Sha256, "Independent reference source/asset capture binding differs."); }
        Require(inputs.TokenizerConfig.Sha256 == FrozenTokenizerConfigSha256, "Captured tokenizer configuration is not the frozen fifth Laya asset.");
        Verify(inputs.TokenizerConfig, budget, 1048576);
        using var tokenizerConfig = Document(inputs.TokenizerConfig.Path, budget, 1048576);
        var configuredRoles = tokenizerConfig.RootElement;
        var roles = provenance.GetProperty("tokenizer_roles");
        Require(OracleCaptureEvidence.Text(roles, "cls_token") == OracleCaptureEvidence.Text(configuredRoles, "cls_token") && roles.GetProperty("cls_id").GetInt32() == 2 &&
            OracleCaptureEvidence.Text(roles, "sep_token") == OracleCaptureEvidence.Text(configuredRoles, "sep_token") && roles.GetProperty("sep_id").GetInt32() == 1 &&
            OracleCaptureEvidence.Text(roles, "mask_token") == OracleCaptureEvidence.Text(configuredRoles, "mask_token") && roles.GetProperty("mask_id").GetInt32() == 4 &&
            OracleCaptureEvidence.Text(roles, "pad_token") == OracleCaptureEvidence.Text(configuredRoles, "pad_token") && roles.GetProperty("pad_id").GetInt32() == 0,
            "Independent reference tokenizer cls/sep/mask/pad roles differ from the frozen configuration.");
        Require(provenance.GetProperty("runtime_executable").Deserialize(OracleJsonContext.Default.OracleFilePin) == inputs.RuntimeExecutable &&
            provenance.GetProperty("runtime_assembly").Deserialize(OracleJsonContext.Default.OracleFilePin) == inputs.RuntimeAssembly &&
            provenance.GetProperty("build_receipt").Deserialize(OracleJsonContext.Default.OracleFilePin) == inputs.BuildReceipt, "Independent reference compiled-runtime/build binding differs.");
        var sources = provenance.GetProperty("program_sources").Deserialize(OracleJsonContext.Default.OracleFilePinArray) ?? [];
        Require(sources.SequenceEqual(inputs.ProgramSources), "Independent reference complete declared program source binding differs.");
        var processEvidence = provenance.GetProperty("process_evidence").Deserialize(OracleJsonContext.Default.OracleFilePin) ?? throw new InvalidDataException("Reference process evidence pin is absent.");
        Verify(processEvidence, budget, 16777216);
    }
    private static Dictionary<string, OracleFilePin> LegacyReferences(OracleBatchRequest request, bool ported, Budget budget)
    {
        var result = new Dictionary<string, OracleFilePin>(StringComparer.Ordinal); if (ported) return result;
        var index = request.LegacyFrozenReferenceIndex!; Verify(index, budget, 1048576);
        var records = Read(index.Path, OracleJsonContext.Default.OracleLegacyCaptureIndex, budget, 1048576);
        Require(records.SchemaVersion == "sezika.evaluation-captures.v1" && records.Batches.Length is > 0 and <= 256, "Historical reference index generation/count differs.");
        foreach (var entry in records.Batches)
        {
            budget.Check(); var path = Path.GetFullPath(entry.Capture, Path.GetDirectoryName(index.Path)!); var pin = Pin(path, budget);
            Require(pin.Sha256 == entry.CaptureSha256 && IsSha(entry.ManifestSha256) && result.TryAdd(entry.ManifestSha256, pin), "Historical reference duplicate/hash binding differs.");
        }
        return result;
    }
    private static Dictionary<int, OracleBatchResult> Recovery(OracleBatchRequest request, string fingerprint, bool ported, bool simulation, Budget budget)
    {
        var results = new Dictionary<int, OracleBatchResult>(); if (request.RecoveryRunIndex is null) return results;
        Verify(request.RecoveryRunIndex, budget, 16777216); var prior = Read(request.RecoveryRunIndex.Path, OracleJsonContext.Default.OracleBatchReport, budget, 16777216);
        Require(prior.SchemaVersion == "sezika.oracle-batch-run.v2" && prior.TaskId == request.TaskId && prior.TaskFingerprint == fingerprint &&
            prior.SimulationOnly == simulation && prior.Batches.Length <= 256 && prior.BatchOffset == request.BatchOffset, "Oracle recovery task/contract/inputs/runtime/source identity differs.");
        foreach (var row in prior.Batches)
        {
            budget.Check(); if (row.Status is not ("COMPLETE" or "COMPLETE_WITH_NUMERICAL_DIFFERENCES")) continue;
            Require(row.Reference is not null && row.Actual is not null && row.Comparison is not null && row.Stages.Length == 3 &&
                row.Stages.Select(stage => stage.Stage).SequenceEqual(new[] { "reference", "actual", "compare" }), "Frozen complete Oracle batch lacks its three ordered stages.");
            Verify(row.Manifest, budget); Verify(row.Reference!, budget); Verify(row.Actual!, budget); Verify(row.Comparison!, budget);
            foreach (var stage in row.Stages)
            {
                budget.Check(); Require(stage.SimulationOnly == simulation, "Recovery crossed simulation/native provenance.");
                if (stage.Stage == "reference" && !ported) continue;
                Require(stage.ProcessReceiptSha256 is not null, "Completed Oracle stage lacks a frozen execution receipt.");
                var receipt = Pin(stage.ProcessReceipt, budget, 16777216); Require(receipt.Sha256 == stage.ProcessReceiptSha256, "Recovered process receipt changed.");
                var execution = Read(receipt.Path, ExecutionJsonContext.Default.ExecutionResult, budget, 16777216); VerifyExecution(execution, stage.Stage == "compare", simulation, budget);
                Require(execution.Executable == (stage.Stage == "reference" ? request.ReferenceTool!.Executable : stage.Stage == "actual" ? request.ActualTool.Executable : request.CompareTool.Executable) &&
                    execution.ExitCode == stage.ExitCode && stage.Output is not null, "Recovered Oracle stage role/exit/output differs.");
                Verify(stage.Output!, budget);
                ValidateRecoveredExecution(request, row, stage, execution, budget);
                if (stage.InputRequest is not null) Verify(stage.InputRequest, budget);
                if (stage.ProducerReceipt is not null) Verify(stage.ProducerReceipt, budget);
            }
            OracleCaptureEvidence.Capture(row.Reference!, row.Manifest, request.Contract, row.CaseIds, true, ported, simulation, null, budget);
            OracleCaptureEvidence.Capture(row.Actual!, row.Manifest, request.Contract, row.CaseIds, false, ported, simulation, row.Reference!.Sha256, budget);
            if (ported && !simulation) { ValidateReferenceProvenance(row.Reference!, request.ReferenceInputs!, budget); ValidateProducerReceipt(row.Stages[0].ProducerReceipt!, row.Reference!, row.CaseIds.Length, budget); }
            var full = OracleCaptureEvidence.Comparison(row.Comparison!, row.Reference!, row.Actual!, row.Manifest, request.Contract, row.CaseIds,
                row.Stages[2].ExitCode!.Value, simulation, budget);
            Require(full == row.FullManifestPassed && results.TryAdd(row.PlanIndex, row), "Recovered numeric verdict/duplicate plan index differs.");
        }
        Require(results.Count == prior.CompletedBatches, "Recovered completed-batch denominator differs."); return results;
    }
    private static void ValidateRecoveredExecution(OracleBatchRequest request, OracleBatchResult row, OracleStageResult stage, ExecutionResult execution, Budget budget)
    {
        Require(execution.OutputDirectory == Path.GetDirectoryName(stage.ProcessReceipt) && execution.WorkingDirectory == Path.GetDirectoryName(execution.OutputDirectory),
            "Recovered Oracle execution/working directory ownership differs.");
        string[] expected;
        if (stage.Stage == "reference")
        {
            Require(stage.InputRequest is not null && stage.ProducerReceipt is not null, "Reference recovery requires its exact frozen request and producer receipt.");
            Verify(stage.InputRequest!, budget); Verify(stage.ProducerReceipt!, budget);
            var wire = Read(stage.InputRequest!.Path, OracleJsonContext.Default.OracleLayaRequest, budget);
            var inputs = request.ReferenceInputs!;
            var wanted = new OracleLayaRequest(inputs.Weights, inputs.EncoderConfig, inputs.AgentConfig, inputs.Tokenizer, inputs.TokenizerConfig, inputs.SourceLock,
                inputs.CommonSource, inputs.AgentSource, row.Manifest, request.Contract, inputs.ProgramSources, inputs.RuntimeExecutable,
                inputs.RuntimeAssembly, Path.GetDirectoryName(row.Reference!.Path)!, execution.OutputDirectory, inputs.BuildReceipt, row.CaseIds.Length,
                wire.TimeoutSeconds, inputs.MaximumResidentBytes, wire.CancelFile);
            Require(wire.TimeoutSeconds is >= 1 and <= 600 && JsonSerializer.Serialize(wire, OracleJsonContext.Default.OracleLayaRequest) ==
                JsonSerializer.Serialize(wanted, OracleJsonContext.Default.OracleLayaRequest), "Recovered independent reference typed request inputs/source/runtime binding differs.");
            expected = ReferenceArguments(stage.InputRequest.Path, stage.ProducerReceipt.Path, wire);
        }
        else if (stage.Stage == "actual")
        {
            var arguments = execution.ArgumentList; var secondsIndex = request.RequireActualAot ? arguments.Length - 2 : arguments.Length - 1;
            Require(secondsIndex >= 0 && int.TryParse(arguments[secondsIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 1 and <= 600,
                "Recovered actual stage timeout is invalid.");
            expected = ["--reference", row.Reference!.Path, "--model", request.Model.Root, "--cases", row.Manifest.Path, "--contract", request.Contract.Path,
                "--output", Path.GetDirectoryName(row.Actual!.Path)!, "--backend", "cuda", "--length-policy", "laya_compatible", "--max-cases",
                row.CaseIds.Length.ToString(CultureInfo.InvariantCulture), "--timeout-seconds", arguments[secondsIndex]];
            if (request.RequireActualAot) expected = expected.Append("--require-aot").ToArray();
        }
        else
        {
            Require(execution.ArgumentList.Length == 6 && int.TryParse(execution.ArgumentList[5], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 1 and <= 30,
                "Recovered comparison timeout is invalid.");
            expected = [row.Reference!.Path, row.Actual!.Path, request.Contract.Path, row.Manifest.Path, row.Comparison!.Path, execution.ArgumentList[5]];
        }
        Require(execution.ArgumentList.SequenceEqual(expected), "Recovered Oracle native argv belongs to another task/input/backend invocation.");
    }
    private static string[] ReferenceArguments(string requestPath, string producerReceiptPath, OracleLayaRequest wire)
    {
        Require(wire.TimeoutSeconds is >= 1 and <= 600 && Path.IsPathFullyQualified(requestPath) && Path.IsPathFullyQualified(producerReceiptPath),
            "Exact independent reference CLI request/output/time binding required.");
        string[] arguments = ["laya", "--request", requestPath, "--output", producerReceiptPath, "--timeout-seconds", wire.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)];
        if (wire.CancelFile is not null)
        {
            Require(Path.IsPathFullyQualified(wire.CancelFile), "Independent reference CLI cancellation path must be absolute.");
            arguments = [.. arguments, "--cancel-file", wire.CancelFile];
        }
        return arguments;
    }
}
