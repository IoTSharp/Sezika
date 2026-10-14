using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika.ReferenceTool.Tokenization;

namespace Sezika.ReferenceTool.Laya;

internal sealed record LayaInputChecksRequest(FilePin Reference, FilePin Cases, FilePin Contract,
    FilePin Tokenizer, FilePin TokenizerConfig, FilePin EncoderConfig, FilePin AgentConfig,
    FilePin SourceLock, FilePin CommonSource, FilePin AgentSource, FilePin[] ProgramSources,
    FilePin RuntimeExecutable, FilePin? RuntimeAssembly, FilePin BuildReceipt, string ProcessEvidenceDirectory,
    int MaximumInputCases = 46, int MaximumDecoderCases = 42, int TimeoutSeconds = 60,
    long MaximumResidentBytes = 1073741824, string? CancelFile = null);

internal sealed record HistoricalLayaProvenance(string ModelId, string ModelRevision, string WeightsSha256,
    string TokenizerSha256, string UpstreamSourceRevision, string UpstreamVersion, string CasesSha256,
    string ContractSha256, string SourceLockSha256, string UpstreamAgentSha256, string UpstreamCommonSha256,
    int MaxLen, int HeadMaxLen, string TemperaturePolicy, string Implementation, string Backend, int BatchSize);
internal sealed record HistoricalLayaFailure(string Stage, string Type, string Message, string UpstreamType,
    string UpstreamMessage);
internal sealed record HistoricalLayaCase(string Id, string Primitive, string Status, LayaCaseInput Input,
    string[] Tags, int[]? TokenIds = null, int[]? MarkerPositions = null, string[]? CandidateLabels = null, float[]? RawLogits = null,
    float[]? Probabilities = null, LayaPrediction? Prediction = null, HistoricalLayaFailure? Failure = null,
    SequenceDetails? SequenceDiagnostics = null);
internal sealed record HistoricalLayaCapture(string SchemaVersion, HistoricalLayaProvenance Provenance,
    string MeasurementStatus, int TotalManifestCases, int SelectedCaseCount, HistoricalLayaCase[] Cases);
internal sealed record InputCheckTolerance(double Absolute, double Relative);
internal sealed record InputCheckTolerances(InputCheckTolerance Probabilities, InputCheckTolerance Score);
internal sealed record InputCheckComparison(double ProbabilitySumAbsoluteTolerance, string[] BooleanSemanticOrder,
    double BooleanThreshold, bool BooleanThresholdEquality);
internal sealed record InputCheckContract(string SchemaVersion, string Status, InputCheckTolerances Tolerances,
    InputCheckComparison Comparison);

internal sealed record LayaInputCheckRow(string Id, string Primitive, string Status, string ExpectedOutcome,
    string ObservedOutcome, int[]? ComputedTokenIds, int[]? ComputedMarkerPositions,
    string[]? ComputedCandidateLabels, SequenceDetails? ComputedSequenceDiagnostics, string[] Differences,
    string? Error, bool ContainsNewModelLogits = false);
internal sealed record LayaDecoderCheckRow(string Id, string Primitive, string Status, float[] HistoricalInputLogits,
    float[]? ComputedProbabilities, LayaPrediction? ComputedDecodedPrediction, double MaximumProbabilityAbsoluteError,
    string[] Differences, string? Error, string SourceScope = "Postprocessing of explicitly supplied historical raw_logits only; no neural head/scorer/act_head or new model prediction");
internal sealed record LayaInputCheckBinding(FilePin Request, FilePin Reference, FilePin Cases, FilePin Contract,
    FilePin Tokenizer, FilePin TokenizerConfig, FilePin EncoderConfig, FilePin AgentConfig,
    FilePin SourceLock, FilePin CommonSource, FilePin AgentSource, FilePin[] ProgramSources,
    FilePin RuntimeExecutable, FilePin? RuntimeAssembly, FilePin BuildReceipt, FilePin BuildExecution,
    FilePin[] RuntimeFiles, FilePin ProcessEvidence, PortProcess Process, string[] ActualArguments);
internal sealed record LayaInputChecksReport([property: JsonPropertyName("Status")] string Status,
    int HistoricalInputDenominator, int HistoricalDecoderDenominator, int SelectedInputCases, int SelectedDecoderCases,
    int CompletedInputCases, int CompletedDecoderCases, int PassedInputCases, int FailedInputCases,
    int PassedDecoderCases, int FailedDecoderCases, LayaInputCheckRow[] InputRows, LayaDecoderCheckRow[] DecoderRows,
    string[] UnprocessedInputIds, string[] UnprocessedDecoderIds, LayaInputCheckBinding? SourceBinding,
    string FinalInputAndRuntimeRecheck, double ElapsedSeconds, long ObservedPeakBytes, string? Error,
    bool FixtureOnly = true, bool OriginalWeightsVerified = false, bool UpstreamExecuted = false,
    bool RepresentsEncoderForward = false, bool RepresentsNeuralHeadForward = false,
    bool RepresentsActionHeadForward = false, bool RepresentsFullLayaForward = false,
    bool RepresentsQuality = false, bool RepresentsCalibration = false, bool RepresentsAot = false,
    string SourceScope = "Real fixed historical46 inputs and42 frozen raw-logit rows; new C# Question/Sequence and postprocessing fixture conformance only");

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(LayaInputChecksRequest))]
[JsonSerializable(typeof(HistoricalLayaCapture))]
[JsonSerializable(typeof(InputCheckContract))]
[JsonSerializable(typeof(LayaInputChecksReport))]
internal partial class LayaInputChecksJson : JsonSerializerContext;

// Historical inputs/logits remain immutable data. None of this file calls the
// encoder, neural head, scorer, action head, production Sezika or a script.
internal static class LayaInputChecks
{
    private const string ReferenceSha = "bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa";
    private const string CasesSha = "d0fb41807322f27e60fe5eabd2a146d4dc8ce31b326eb4f292bcc20ab85b4a7a";
    private const string ContractSha = "771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee";
    private const string WeightsSha = "9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204";
    private const string TokenizerConfigSha = "424b69444bf7b5809dc2cd2e36d0bd71b8055124dd24274d6db3c655d38205e7";
    private const string EncoderConfigSha = "83f6916d13ef0f556ac461f28308dc2bffa7ebeadee8ec9e2db5812020ea5bb4";
    private const string AgentConfigSha = "25061739243b617ad88d1219ba6f8a9c86c5881ca28df024fa2d9b3b2fcc30c6";

    internal static int Run(string requestPath, string reportPath, CancellationToken cancellation)
    {
        var admission = new Budget(60, 1073741824, null, cancellation);
        var requestPin = admission.Pin(requestPath, 4194304);
        var request = admission.Read(requestPin, LayaInputChecksJson.Default.LayaInputChecksRequest, 4194304);
        if (request.MaximumInputCases is < 1 or > 46 || request.MaximumDecoderCases is < 1 or > 42 ||
            request.TimeoutSeconds is < 1 or > 60 || request.MaximumResidentBytes is < 134217728 or > 1073741824)
            throw new InvalidDataException("Laya partial profile requires1..46 inputs/1..42 decoder rows/<=60s/<=1GiB.");
        Budget.Safe(reportPath);
        if (Path.Exists(reportPath) || !Directory.Exists(Path.GetDirectoryName(reportPath)))
            throw new IOException("Laya partial report must be a fresh file with an existing parent.");
        if (request.CancelFile is not null) Budget.Safe(request.CancelFile);
        var budget = new Budget(request.TimeoutSeconds, request.MaximumResidentBytes, request.CancelFile, cancellation);
        var inputs = new List<LayaInputCheckRow>(); var decoders = new List<LayaDecoderCheckRow>();
        HistoricalLayaCase[] selectedInputs = []; HistoricalLayaCase[] selectedDecoders = [];
        LayaInputCheckBinding? binding = null; string? terminal = null; var recheck = "NOT_RUN";
        var cancelled = false; var completedInputs = 0; var completedDecoders = 0;
        using var progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var progressClock = Stopwatch.StartNew();
        var progress = Task.Run(async () =>
        {
            // At most6 ten-second progress ticks; the main profile deadline is60s.
            for (var tick = 0; tick < 6 && progressClock.Elapsed.TotalSeconds < 61; tick++)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), progressCancellation.Token).ConfigureAwait(false);
                Console.Error.WriteLine($"laya-input-checks elapsed={progressClock.Elapsed.TotalSeconds:F1}s inputs={Volatile.Read(ref completedInputs)}/{request.MaximumInputCases} decoder={Volatile.Read(ref completedDecoders)}/{request.MaximumDecoderCases}");
            }
        }, progressCancellation.Token);
        try
        {
            VerifyInputs(request, budget);
            var historical = budget.Read(request.Reference, LayaInputChecksJson.Default.HistoricalLayaCapture);
            var manifest = budget.Read(request.Cases, LayaJson.Default.LayaManifest);
            var contract = budget.Read(request.Contract, LayaInputChecksJson.Default.InputCheckContract);
            ValidateHistorical(historical, manifest, contract, budget);
            selectedInputs = historical.Cases.Take(request.MaximumInputCases).ToArray();
            selectedDecoders = historical.Cases.Where(row => row.Status == "answered").Take(request.MaximumDecoderCases).ToArray();
            if (request.ProgramSources.Length is < 1 or > 64 || request.ProgramSources.Any(pin => pin is null) ||
                !request.ProgramSources.Any(pin => Path.GetFileName(pin.Path) == "LayaInputChecks.cs"))
                throw new InvalidDataException("Current complete compiled source binding must include LayaInputChecks.cs.");
            var runtime = PortProof.Runtime(request.ProgramSources, request.RuntimeExecutable, request.RuntimeAssembly,
                request.BuildReceipt, request.ProcessEvidenceDirectory, budget);
            ValidateActualRequestArgument(requestPin, budget);
            binding = new(requestPin, request.Reference, request.Cases, request.Contract, request.Tokenizer,
                request.TokenizerConfig, request.EncoderConfig, request.AgentConfig, request.SourceLock,
                request.CommonSource, request.AgentSource, request.ProgramSources, request.RuntimeExecutable,
                request.RuntimeAssembly, request.BuildReceipt, runtime.BuildExecution, runtime.RuntimeFiles,
                runtime.Evidence, runtime.Process, Environment.GetCommandLineArgs());
            var tokenizer = new FrozenTokenizer(request.Tokenizer, budget);
            for (var index = 0; index < selectedInputs.Length && index < 46; index++)
            {
                budget.Check(); inputs.Add(InputRow(selectedInputs[index], tokenizer, budget));
                Volatile.Write(ref completedInputs, inputs.Count);
                if (index == 0 || index + 1 == selectedInputs.Length)
                    Console.WriteLine($"laya-question-sequence {index + 1}/{selectedInputs.Length} status={inputs[^1].Status}");
            }
            for (var index = 0; index < selectedDecoders.Length && index < 42; index++)
            {
                budget.Check(); decoders.Add(DecoderRow(selectedDecoders[index], contract, budget));
                Volatile.Write(ref completedDecoders, decoders.Count);
                if (index == 0 || index + 1 == selectedDecoders.Length)
                    Console.WriteLine($"laya-frozen-logit-decoder {index + 1}/{selectedDecoders.Length} status={decoders[^1].Status}");
            }
            VerifyInputs(request, budget); budget.Verify(requestPin, 4194304);
            foreach (var pin in request.ProgramSources) { budget.Check(); budget.Verify(pin, 4194304); }
            budget.Verify(request.BuildReceipt, 4194304); budget.Verify(runtime.BuildExecution, 20971520);
            PortProof.VerifyRuntimeAfter(runtime.RuntimeFiles, budget); recheck = "PASS";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or
            ArgumentException or InvalidOperationException or OverflowException or TimeoutException or OperationCanceledException or PlatformNotSupportedException)
        {
            cancelled = error is TimeoutException or OperationCanceledException;
            terminal = error.GetType().Name + ": " + error.Message;
        }
        finally
        {
            progressCancellation.Cancel();
            try { progress.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        }
        var inputFailed = inputs.Count(row => row.Status != "PASS"); var decoderFailed = decoders.Count(row => row.Status != "PASS");
        var complete = inputs.Count == request.MaximumInputCases && decoders.Count == request.MaximumDecoderCases;
        var status = cancelled ? "CANCELLED_OR_TIMED_OUT_LAYA_PARTIAL_FIXTURE" : terminal is not null ? "FAIL_LAYA_PARTIAL_FIXTURE_ADMISSION_OR_RECHECK" :
            !complete ? "INCOMPLETE_LAYA_PARTIAL_FIXTURE" : inputFailed + decoderFailed > 0 ? "FAIL_LAYA_PARTIAL_FIXTURE_CONFORMANCE" :
            "PASS_LAYA_PARTIAL_FIXTURE_CONFORMANCE";
        var report = new LayaInputChecksReport(status, 46, 42, request.MaximumInputCases, request.MaximumDecoderCases,
            inputs.Count, decoders.Count, inputs.Count - inputFailed, inputFailed, decoders.Count - decoderFailed, decoderFailed,
            inputs.ToArray(), decoders.ToArray(), selectedInputs.Skip(inputs.Count).Select(row => row.Id).ToArray(),
            selectedDecoders.Skip(decoders.Count).Select(row => row.Id).ToArray(), binding, recheck, budget.Elapsed, budget.PeakBytes, terminal);
        // Cancellation cannot erase already observed row failures. Recovery only
        // serializes a truthful partial report and never resumes profile work.
        var recovery = new Budget(5, 1073741824, null, CancellationToken.None); recovery.Check();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, LayaInputChecksJson.Default.LayaInputChecksReport);
        if (bytes.Length > 4194304) throw new InvalidDataException("Laya partial report exceeds4MiB; no success marker written.");
        recovery.Check(); Budget.WriteNew(reportPath, bytes);
        return cancelled ? 124 : terminal is null && complete && inputFailed + decoderFailed == 0 ? 0 : 1;
    }

    private static void VerifyInputs(LayaInputChecksRequest request, Budget budget)
    {
        var pins = new (FilePin Pin, string Sha, long Maximum)[]
        {
            (request.Reference, ReferenceSha, 1048576), (request.Cases, CasesSha, 1048576), (request.Contract, ContractSha, 1048576),
            (request.Tokenizer, FrozenTokenizer.Sha256, 67108864), (request.TokenizerConfig, TokenizerConfigSha, 1048576),
            (request.EncoderConfig, EncoderConfigSha, 1048576), (request.AgentConfig, AgentConfigSha, 1048576),
            (request.SourceLock, PortProof.LockSha, 1048576), (request.CommonSource, PortProof.CommonSha, 1048576),
            (request.AgentSource, PortProof.AgentSha, 1048576)
        };
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var (pin, sha, maximum) in pins)
        {
            budget.Check(); if (!pin.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase) || !paths.Add(Path.GetFullPath(pin.Path)))
                throw new InvalidDataException("Laya fixed historical/config/source pin identity or duplicate path differs.");
            budget.Verify(pin, maximum);
        }
    }

    private static void ValidateHistorical(HistoricalLayaCapture capture, LayaManifest manifest, InputCheckContract contract, Budget budget)
    {
        var p = capture.Provenance;
        if (capture.SchemaVersion != "sezika.laya-oracle.v1" || capture.MeasurementStatus != "complete" ||
            capture.TotalManifestCases != 46 || capture.SelectedCaseCount != 46 || capture.Cases.Length != 46 ||
            manifest.SchemaVersion != "sezika.laya-oracle-inputs.v1" || manifest.Cases.Length != 46 ||
            capture.Cases.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != 46 ||
            p.ModelId != "convaiinnovations/laya-multilingual" || p.ModelRevision != PortProof.ModelRevision ||
            p.UpstreamSourceRevision != PortProof.SourceRevision || p.UpstreamVersion != "0.3.20" || p.WeightsSha256 != WeightsSha ||
            p.TokenizerSha256 != FrozenTokenizer.Sha256 || p.CasesSha256 != CasesSha || p.ContractSha256 != ContractSha ||
            p.SourceLockSha256 != PortProof.LockSha || p.UpstreamCommonSha256 != PortProof.CommonSha || p.UpstreamAgentSha256 != PortProof.AgentSha ||
            p.MaxLen != 1024 || p.HeadMaxLen != 256 || p.BatchSize != 1 || p.TemperaturePolicy != "checkpoint_fixed_1_no_overrides" ||
            p.Implementation != "pinned_upstream_laya_agent" || p.Backend != "pytorch_cpu_fp32")
            throw new InvalidDataException("Immutable historical Laya identity/46-row source coverage differs.");
        if (contract.SchemaVersion != "sezika.laya-oracle-contract.v1" || contract.Status != "frozen_before_measurement" ||
            contract.Tolerances.Probabilities != new InputCheckTolerance(.0001, .0001) || contract.Tolerances.Score != new InputCheckTolerance(.001, .0001) ||
            contract.Comparison.ProbabilitySumAbsoluteTolerance != .000002 || !contract.Comparison.BooleanSemanticOrder.SequenceEqual(new[] { "false", "true" }) ||
            contract.Comparison.BooleanThreshold != .5 || !contract.Comparison.BooleanThresholdEquality)
            throw new InvalidDataException("Historical postprocessing tolerance/Boolean/score contract changed.");
        var answered = 0; var failed = 0;
        for (var index = 0; index < capture.Cases.Length && index < 46; index++)
        {
            budget.Check(); var row = capture.Cases[index]; var spec = manifest.Cases[index];
            if (row.Id != spec.Id || row.Primitive != spec.Primitive || row.Input.Language != spec.Language || !row.Tags.SequenceEqual(spec.Tags) ||
                row.Input.MaxLen != 1024 || row.Input.HeadMaxLen != 256 || row.Primitive is not ("choice" or "score" or "boolean"))
                throw new InvalidDataException("Historical realized input order/identity/length differs from fixed manifest.");
            if (row.Status == "answered")
            {
                answered++;
                if (row.TokenIds is null || row.TokenIds.Length is < 1 or > 1024 || row.MarkerPositions is null || row.MarkerPositions.Length is < 1 or > 64 ||
                    row.CandidateLabels is null || row.CandidateLabels.Length != row.MarkerPositions.Length || row.RawLogits is null ||
                    row.RawLogits.Length != row.MarkerPositions.Length || row.Probabilities is null || row.Probabilities.Length != row.MarkerPositions.Length ||
                    row.Prediction is null || row.SequenceDiagnostics is null || row.Failure is not null ||
                    row.RawLogits.Any(value => !float.IsFinite(value)) || row.Probabilities.Any(value => !float.IsFinite(value)))
                    throw new InvalidDataException("Historical answered row has incomplete/non-finite input/postprocessing evidence.");
            }
            else if (row.Status == "failed")
            {
                failed++;
                if (row.Failure is not { Stage: "validate", Type: "invalid_question" } || row.TokenIds is not null || row.RawLogits is not null || row.Prediction is not null)
                    throw new InvalidDataException("Historical failed row was relabelled or contains fabricated model output.");
            }
            else throw new InvalidDataException("Historical row status differs.");
        }
        if (answered != 42 || failed != 4) throw new InvalidDataException("Historical answered42/validation4 denominator differs.");
    }

    private static LayaInputCheckRow InputRow(HistoricalLayaCase source, FrozenTokenizer tokenizer, Budget budget)
    {
        var differences = new List<string>(); LayaSequence? sequence = null; LayaQuestion? question = null;
        string? error = null; var observed = "constructed"; var stage = "validate";
        try
        {
            question = LayaQuestion.Convert(source.Input.Question, budget);
            if (source.Primitive != (question.Type == "noul" ? "boolean" : question.Type)) differences.Add("primitive/question type differs");
            stage = "encode"; sequence = LayaSequences.Build(tokenizer, source.Input.State, question, budget, source.Input.MaxLen);
            var collation = LayaSequences.Collate([sequence], budget);
            if (source.Status != "answered") differences.Add("expected historical validation failure, but input constructed");
            if (source.TokenIds is null || !sequence.Ids.SequenceEqual(source.TokenIds)) differences.Add("computed token_ids differ");
            if (source.MarkerPositions is null || !sequence.Markers.SequenceEqual(source.MarkerPositions)) differences.Add("computed marker_positions differ");
            if (source.CandidateLabels is null || !question.Labels.SequenceEqual(source.CandidateLabels)) differences.Add("computed candidate_labels differ");
            CompareDetails(sequence.Details, source.SequenceDiagnostics, differences);
            if (!collation.InputIds[0].SequenceEqual(sequence.Ids) || !collation.MarkerPositions[0].SequenceEqual(sequence.Markers) ||
                collation.AttentionMask[0].Any(value => value != 1) || collation.MarkerMask[0].Any(value => !value) || collation.QuestionTypes[0] != question.TypeIndex)
                differences.Add("computed single-row collation differs");
        }
        catch (InvalidQuestionException exception)
        {
            observed = stage + "/invalid_question"; error = exception.Message;
            if (source.Status != "failed" || stage != "validate" || source.Failure is not { Stage: "validate", Type: "invalid_question" })
                differences.Add("unexpected validation classification: " + observed);
        }
        catch (SequenceBudgetException exception)
        {
            observed = stage + "/sequence_budget_exceeded"; error = exception.Message; differences.Add("unexpected sequence budget failure");
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        { observed = stage + "/" + exception.GetType().Name; error = exception.Message; differences.Add("unexpected component failure"); }
        return new(source.Id, source.Primitive, differences.Count == 0 ? "PASS" : "FAIL", source.Status == "answered" ? "constructed" : "validate/invalid_question",
            observed, sequence?.Ids, sequence?.Markers, question?.Labels, sequence?.Details, differences.ToArray(), error);
    }

    private static void CompareDetails(SequenceDetails actual, SequenceDetails? expected, List<string> differences)
    {
        if (expected is null) { differences.Add("historical sequence diagnostics missing"); return; }
        if (actual.SerializedState != expected.SerializedState) differences.Add("serialized_state differs");
        if (actual.SanitizedState != expected.SanitizedState) differences.Add("sanitized_state differs");
        if (actual.RenderedInstruction != expected.RenderedInstruction) differences.Add("rendered_instruction differs");
        if (!actual.RenderedOptions.SequenceEqual(expected.RenderedOptions)) differences.Add("rendered_options differ");
        if (actual.OriginalInstructionTokens != expected.OriginalInstructionTokens || actual.RetainedInstructionTokens != expected.RetainedInstructionTokens ||
            !actual.OriginalOptionTokens.SequenceEqual(expected.OriginalOptionTokens) || !actual.RetainedOptionTokens.SequenceEqual(expected.RetainedOptionTokens) ||
            actual.PrefixTokensIncludingSpecials != expected.PrefixTokensIncludingSpecials || actual.UntruncatedTotalTokens != expected.UntruncatedTotalTokens ||
            actual.OriginalStateTokens != expected.OriginalStateTokens || actual.RetainedStateTokens != expected.RetainedStateTokens ||
            actual.DroppedStateTokens != expected.DroppedStateTokens || actual.StateRetainedStart != expected.StateRetainedStart ||
            actual.StateTruncationDirection != expected.StateTruncationDirection || actual.MaskTextRemoved != expected.MaskTextRemoved)
            differences.Add("sequence truncation/count/marker diagnostics differ");
    }

    private static LayaDecoderCheckRow DecoderRow(HistoricalLayaCase source, InputCheckContract contract, Budget budget)
    {
        var differences = new List<string>(); float[]? probabilities = null; LayaPrediction? prediction = null;
        string? error = null; double maximumError = 0; var logits = source.RawLogits ?? [];
        try
        {
            budget.Check(); var question = LayaQuestion.Convert(source.Input.Question, budget);
            if (logits.Length is < 1 or > 64 || logits.Length != question.Labels.Length || logits.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException("Historical decoder input logits/label denominator differs.");
            // This computes probabilities; frozen probabilities/predictions are
            // expectations only and never flow into these calculations.
            probabilities = ModernBertReference.Softmax(logits);
            var best = 0; for (var index = 1; index < probabilities.Length && index < 64; index++)
            { budget.Check(); if (probabilities[index] > probabilities[best]) best = index; }
            if (question.Type == "choice") prediction = new(question.Labels[best], null, null, null);
            else if (question.Type == "score")
            {
                double score = 0; for (var index = 0; index < probabilities.Length && index < 64; index++) { budget.Check(); score += index * (double)probabilities[index]; }
                prediction = new(null, score, null, null);
            }
            else
            {
                if (probabilities.Length != 2 || !question.Labels.SequenceEqual(new[] { "false", "true" })) throw new InvalidDataException("Boolean semantic order differs.");
                prediction = new(null, null, probabilities[1] >= .5f, probabilities[1]);
            }
            if (source.Probabilities is null || source.Probabilities.Length != probabilities.Length || source.Prediction is null)
                throw new InvalidDataException("Historical decoder expectations missing.");
            double sum = 0;
            for (var index = 0; index < probabilities.Length && index < 64; index++)
            {
                budget.Check(); var actual = probabilities[index]; var expected = source.Probabilities[index]; sum += actual;
                maximumError = Math.Max(maximumError, Math.Abs(actual - (double)expected));
                if (!Close(actual, expected, contract.Tolerances.Probabilities)) differences.Add("probability differs at index " + index);
            }
            if (Math.Abs(sum - 1) > contract.Comparison.ProbabilitySumAbsoluteTolerance) differences.Add("computed probabilities do not sum to1 within frozen tolerance");
            var target = source.Prediction;
            if (prediction.ChoiceLabel != target.ChoiceLabel || prediction.Boolean != target.Boolean) differences.Add("decoded Choice/Boolean differs");
            if (!OptionalClose(prediction.Score, target.Score, contract.Tolerances.Score)) differences.Add("decoded unrounded Score differs");
            if (!OptionalClose(prediction.ProbabilityTrue, target.ProbabilityTrue, contract.Tolerances.Probabilities)) differences.Add("decoded Boolean probability differs");
        }
        catch (Exception exception) when (exception is InvalidQuestionException or InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        { error = exception.GetType().Name + ": " + exception.Message; differences.Add("decoder component failure"); }
        return new(source.Id, source.Primitive, differences.Count == 0 ? "PASS" : "FAIL", logits,
            probabilities, prediction, maximumError, differences.ToArray(), error);
    }

    private static bool Close(double actual, double expected, InputCheckTolerance tolerance) =>
        double.IsFinite(actual) && double.IsFinite(expected) && Math.Abs(actual - expected) <= tolerance.Absolute + tolerance.Relative * Math.Abs(expected);
    private static bool OptionalClose(double? actual, double? expected, InputCheckTolerance tolerance) =>
        actual is null && expected is null || actual is not null && expected is not null && Close(actual.Value, expected.Value, tolerance);
    private static void ValidateActualRequestArgument(FilePin request, Budget budget)
    {
        var arguments = Environment.GetCommandLineArgs(); string? found = null;
        if (arguments.Length is < 3 or > 64 || arguments.Sum(value => (long)value.Length) > 65536)
            throw new InvalidDataException("Partial profile actual argv count/character bound differs.");
        for (var index = 1; index < arguments.Length && index < 64; index++)
        {
            budget.Check(); if (arguments[index] != "--request") continue;
            if (found is not null || ++index >= arguments.Length || !Path.IsPathFullyQualified(arguments[index]))
                throw new InvalidDataException("Partial profile requires one literal --request absolute-path pair.");
            found = arguments[index];
        }
        if (found is null || !string.Equals(Path.GetFullPath(found), Path.GetFullPath(request.Path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Actual partial profile argv differs from pinned request.");
    }
}
