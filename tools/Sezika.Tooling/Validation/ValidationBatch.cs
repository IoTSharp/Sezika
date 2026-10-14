using System.Text.Json;
using static Sezika.Tooling.CorpusJson;

namespace Sezika.Tooling;

internal static partial class ValidationOperations
{
    internal static ValidationBatchReport Batch(ValidationBatchRequest request, Budget budget,
        Func<ExecutionRequest, Budget, ExecutionResult>? fixtureExecutor = null)
    {
        Require(request.MaximumCases is >= 1 and <= 64 && request.Cases.Length >= 1 && request.Cases.Length <= request.MaximumCases && request.MaximumCopiedBytes is >= 1 and <= 512 * 1024 * 1024, "Validation batch bounds.");
        Require(request.Cases.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() == request.Cases.Length, "Duplicate validation case name.");
        Require(request.AssetPreflightOnly || request.Cases[0].ExpectedExitCode == 0, "A successful positive control must precede negative validation cases.");
        foreach (var item in request.Cases)
        {
            budget.Check(); SafePaths.Relative(item.Name); Require(!item.Name.Contains('/', StringComparison.Ordinal) && item.Arguments.Length <= 256 && item.Inputs.Length <= 1024 && item.Mutations.Length <= 128 && item.Outputs.Length <= 128 && item.TimeoutSeconds is >= 1 and <= 1800 && item.ExpectedExitCode is >= 0 and <= 255, "Validation case bounds.");
            foreach (var output in item.Outputs)
            {
                budget.Check();
                Require(output.JsonEquals is null || output.MustExist && output.JsonEquals.Count is >= 1 and <= 32 &&
                    output.JsonEquals.All(field => !string.IsNullOrWhiteSpace(field.Key) && field.Key.Length <= 256 &&
                        !field.Key.Contains('\0') && field.Value.ValueKind != JsonValueKind.Undefined),
                    "Validation JSON equality requires a present output and1..32 defined bounded fields.");
            }
            var jsonRejection = item.Outputs.Any(output => output.MustExist &&
                (!string.IsNullOrWhiteSpace(output.JsonStatusProperty) && !string.IsNullOrWhiteSpace(output.JsonStatusValue) ||
                    output.JsonEquals is { Count: > 0 }));
            Require(item.ExpectedExitCode == 0 || !string.IsNullOrWhiteSpace(item.ExpectedStandardError) || jsonRejection,
                "Negative validation case requires targeted stderr or explicit required JSON output assertions.");
            Require(IsSha(item.ExecutableSha256) && FileHash(item.Executable, budget) == item.ExecutableSha256, "Pinned validator executable SHA differs.");
            var fileName = Path.GetFileName(item.Executable); var executable = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fileName[..^4] : fileName; string application;
            if (executable.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            { if (item.ManagedAssembly is null || !IsSha(item.ManagedAssemblySha256) || FileHash(item.ManagedAssembly, budget) != item.ManagedAssemblySha256) throw new InvalidDataException("Pinned managed validator assembly required."); application = Path.GetFileNameWithoutExtension(item.ManagedAssembly); }
            else { Require(item.ManagedAssembly is null && item.ManagedAssemblySha256 is null, "Managed assembly supplied for native apphost."); application = executable; }
            Require(application is "Sezika.Evaluation" or "Sezika.OracleCompare" or "Sezika.IndependentNumericCompare" or "Sezika.IndependentModelTool" or "Sezika.TeacherTool" or "Sezika.CliChecks", "Only fixed project native validators are admitted.");
            if (request.AssetPreflightOnly) Require(application == "Sezika.IndependentModelTool" && item.ExpectedExitCode != 0 && !string.IsNullOrWhiteSpace(item.ExpectedStandardError), "AssetPreflightOnly requires targeted negative IndependentModel synthetic-asset cases.");
            if (application == "Sezika.Evaluation") Require(item.Arguments.Length > 0 && item.Arguments[0] is "--score-capture" or "--verify-baseline" or "--aggregate", "Evaluation batch may only score/verify frozen captures.");
            if (application == "Sezika.IndependentModelTool") Require(item.Arguments.Length > 0 && item.Arguments[0] == "smoke" && item.ExpectedExitCode != 0, "Model preflight batch only permits explicit negative synthetic smoke cases.");
            Require(item.Inputs.Select(input => input.RelativePath).Distinct(StringComparer.Ordinal).Count() == item.Inputs.Length, "Duplicate copied input relative path.");
            foreach (var input in item.Inputs) { budget.Check(); SafePaths.Relative(input.RelativePath); Require(IsSha(input.Sha256) && new FileInfo(input.Source).Length <= 64 * 1024 * 1024 && FileHash(input.Source, budget) == input.Sha256, "Frozen validation input SHA/size differs."); }
        }
        var root = NewOutput(request.Output, budget); var results = request.Cases.Select(item => new ValidationCaseResult(item.Name, "UNPROCESSED", null, "NOT_RUN", null, null, [])).ToArray(); var completed = 0; var passed = 0; var failed = 0; var positiveControls = 0; long copied = 0; string? outerFailure = null;
        try
        {
            for (var index = 0; index < request.Cases.Length; index++)
            {
                budget.Check(); var item = request.Cases[index]; var directory = SafePaths.Within(root, item.Name, budget); Directory.CreateDirectory(directory);
                try
                {
                    Require(request.AssetPreflightOnly || item.ExpectedExitCode == 0 || positiveControls > 0, "Negative validation case cannot run before a successful positive control.");
                    foreach (var input in item.Inputs)
                    { budget.Check(); copied = checked(copied + new FileInfo(input.Source).Length); Require(copied <= request.MaximumCopiedBytes, "Validation copied byte bound."); var target = SafePaths.Within(directory, input.RelativePath, budget); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(input.Source, target); Require(FileHash(target, budget) == input.Sha256, "Copied independent input bytes differ."); }
                    for (var mutationIndex = 0; mutationIndex < item.Mutations.Length; mutationIndex++)
                    {
                        budget.Check(); var mutation = item.Mutations[mutationIndex]; Require(item.Inputs.Any(input => input.RelativePath == mutation.RelativePath) && mutation.Kind is "set" or "bind-sha256" or "duplicate-root-property", "Mutation must affect only a copied task-owned JSON input."); var path = SafePaths.Within(directory, mutation.RelativePath, budget);
                        // The source snapshot remains byte-preserved; mutation is
                        // a new version, and the original copied bytes are frozen.
                        var archive = path + $".before-mutation-{mutationIndex:0000}"; File.Copy(path, archive);
                        string changed;
                        if (mutation.Kind == "duplicate-root-property")
                        { Require(mutation.JsonPath.Length == 1 && mutation.Value.ValueKind != JsonValueKind.Undefined, "Duplicate root fixture path/value."); var text = File.ReadAllText(path, Utf8); var at = text.IndexOf('{'); Require(at >= 0, "Duplicate JSON fixture needs object root."); changed = text.Insert(at + 1, JsonSerializer.Serialize(mutation.JsonPath[0], CorpusJsonContext.Default.String) + ":" + Canonical(mutation.Value) + ","); }
                        else
                        {
                            var original = Read(path, budget, 64 * 1024 * 1024); var replacement = mutation.Value;
                            if (mutation.Kind == "bind-sha256") { Require(mutation.ReferencedFile is not null, "Hash binding needs a copied relative file."); var hash = FileHash(SafePaths.Within(directory, mutation.ReferencedFile!, budget), budget); using var value = JsonDocument.Parse(JsonSerializer.Serialize(hash, CorpusJsonContext.Default.String)); replacement = value.RootElement.Clone(); }
                            else if (replacement.ValueKind == JsonValueKind.String) { var text = replacement.GetString()!.Replace("{case_root}", directory, StringComparison.Ordinal); using var value = JsonDocument.Parse(JsonSerializer.Serialize(text, CorpusJsonContext.Default.String)); replacement = value.RootElement.Clone(); }
                            Require(replacement.ValueKind != JsonValueKind.Undefined, "Mutation replacement missing."); changed = ValidationJson.Mutate(original, mutation.JsonPath, replacement, budget);
                        }
                        File.WriteAllText(path, changed, Utf8);
                    }
                    string Argument(string value)
                    { Require(value.Length <= 16_384 && !value.Contains('\0'), "Validation argv text bound."); return value.Replace("{case_root}", directory, StringComparison.Ordinal); }
                    var arguments = item.Arguments.Select(Argument).ToList(); if (item.ManagedAssembly is not null) arguments.Insert(0, Path.GetFullPath(item.ManagedAssembly));
                    var execution = new ExecutionRequest { Executable = Path.GetFullPath(item.Executable), ArgumentList = arguments.ToArray(), WorkingDirectory = directory, OutputDirectory = Path.Combine(directory, "process"), TimeoutSeconds = item.TimeoutSeconds,
                        MaximumProcesses = 32, MaximumOutputBytes = 1024 * 1024, BrokerExecutable = request.BrokerExecutable, BrokerArgumentPrefix = request.BrokerArgumentPrefix ?? ["process", "broker"] };
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(item.TimeoutSeconds + 15));
                    var observed = fixtureExecutor is null ? ProcessExecutor.ExecuteAsync(execution, budget, cancellation.Token).GetAwaiter().GetResult() : fixtureExecutor(execution, budget);
                    var expectedFailure = "Process exit code " + item.ExpectedExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var expectedErrors = observed.Errors.Length == 0 || item.ExpectedExitCode != 0 && observed.Errors.Length == 1 && observed.Errors[0] == expectedFailure;
                    Require(observed.ExitCode == item.ExpectedExitCode && observed.CleanupStatus == "PASS" && observed.RemainingProcesses == 0 && observed.OutputComplete && observed.InputComplete && !observed.OutputLimitExceeded && expectedErrors, "Validator exit/cleanup/drain differs from expected case.");
                    if (fixtureExecutor is null) Require(item.ExpectedExitCode == 0 ? observed.Status == "PASS" && observed.ExecutionStatus == "SUCCEEDED" : observed.Status == "FAIL" && observed.ExecutionStatus == "FAILED", "Native validator expected failure differs from process/cleanup failure.");
                    if (fixtureExecutor is null) Require(observed.RootPid is > 0 && observed.Launcher is not null && observed.Processes.Length > 0, "Native validator process identity receipt is incomplete.");
                    if (item.ExpectedStandardError is not null)
                    { var path = Path.Combine(execution.OutputDirectory, "stderr.bin"); Require(new FileInfo(path).Length <= 1024 * 1024 && File.ReadAllText(path, Utf8).Contains(item.ExpectedStandardError, StringComparison.Ordinal), "Expected validator rejection differs."); }
                    var hashes = new List<string>();
                    foreach (var output in item.Outputs)
                    {
                        budget.Check(); var target = SafePaths.Within(directory, output.RelativePath, budget); Require(File.Exists(target) == output.MustExist, "Validator output existence differs."); if (!output.MustExist) continue; Require(new FileInfo(target).Length <= 64 * 1024 * 1024, "Validator output byte bound."); hashes.Add(FileHash(target, budget));
                        if (output.JsonStatusProperty is not null || output.JsonEquals is not null)
                        {
                            var actual = Read(target, budget);
                            if (output.JsonStatusProperty is not null) Require(ValidationJson.Text(actual, output.JsonStatusProperty) == output.JsonStatusValue, "Independent validator JSON status differs.");
                            if (output.JsonEquals is not null)
                                foreach (var expected in output.JsonEquals)
                                {
                                    budget.Check();
                                    Require(actual.ValueKind == JsonValueKind.Object && actual.TryGetProperty(expected.Key, out var value) &&
                                        value.ValueKind == expected.Value.ValueKind && JsonElement.DeepEquals(value, expected.Value),
                                        "Independent validator JSON field differs: " + expected.Key);
                                }
                        }
                    }
                    results[index] = new(item.Name, "PASS", observed.ExitCode, observed.CleanupStatus, Path.Combine(execution.OutputDirectory, "result.json"), null, hashes.ToArray()); passed++; if (item.ExpectedExitCode == 0) positiveControls++;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or TimeoutException or OperationCanceledException)
                { results[index] = new(item.Name, "FAIL", null, "READ_PROCESS_RECEIPT", null, error.GetType().Name + ": " + error.Message, []); failed++; }
                completed++; Console.Error.WriteLine($"validation cases={completed}/{request.Cases.Length}; passed={passed}; failed={failed}"); if (failed > 0 && request.StopOnFailure) break;
            }
        }
        catch (Exception error) { outerFailure = error.GetType().Name + ": " + error.Message; throw; }
        finally
        {
            var result = new ValidationBatchReport(failed == 0 && completed == request.Cases.Length && outerFailure is null ? "PASS_BOUNDED_NATIVE_VALIDATION" : "PARTIAL_OR_FAILED_VALIDATION_PRESERVE_RECEIPTS", request.Cases.Length, completed, passed, failed, request.Cases.Length - completed, results, fixtureExecutor is not null);
            Write(Path.Combine(root, "report.json"), result, ValidationJsonContext.Default.ValidationBatchReport, new Budget(30, null, CancellationToken.None));
        }
        return Read(Path.Combine(root, "report.json"), ValidationJsonContext.Default.ValidationBatchReport, budget);
    }
}
