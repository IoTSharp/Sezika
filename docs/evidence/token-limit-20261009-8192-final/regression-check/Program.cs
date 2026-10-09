using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(300));
ConsoleCancelEventHandler cancel = (_, item) => { item.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancel;
try { return Regression.Run(args, cancellation.Token); }
catch (OperationCanceledException) { Console.Error.WriteLine("regression: cancelled/deadline"); return 124; }
catch (Exception error) { Console.Error.WriteLine("regression: " + error); return 1; }
finally { Console.CancelKeyPress -= cancel; }

internal static class Regression
{
    internal const string OriginalSha = "A015A8FA182EE62223CAFC307C618C515A19E90F843290DB9DFB53A56B9A041C";
    internal const string AuditSha = "B78A4F42338574AA346A2A6CDE3041749BB8C142D0D91C5EFC8F116F1ECCF463";
    internal const ulong GpuLimit = 3UL * 1024 * 1024 * 1024;
    internal static int Run(string[] arguments, CancellationToken token)
    {
        if (arguments.Length < 1 || arguments[0] is not ("regress" or "check-input" or "lengths")) throw new ArgumentException("Commands: regress,check-input,lengths; explicit named pairs required.");
        var options = Parse(arguments, token);
        using var budget = new Budget(arguments[0] == "lengths" ? 60 : arguments[0] == "check-input" ? 30 : Number(options, "--timeout-seconds", 1, 300, 300), token);
        if (arguments[0] == "lengths") return Lengths(options, budget);
        using var sourceLease = Open(Need(options, "--records"), budget);
        using var auditLease = Open(Need(options, "--audit"), budget);
        var sourceBytes = Snapshot(sourceLease, 32 * 65_538 + 3, budget); RequireHash(Hash(sourceBytes), OriginalSha);
        var auditBytes = Snapshot(auditLease, 1_048_576, budget); RequireHash(Hash(auditBytes), AuditSha);
        StrictJson(auditBytes, budget);
        var audit = JsonSerializer.Deserialize(auditBytes, Json.Default.AuditInput) ?? throw new InvalidDataException("Audit sidecar null.");
        var records = ReadRecords(sourceBytes, 32, budget); var inputs = BindAudit(records, audit, budget);
        if (arguments[0] == "check-input")
        {
            var maximum = Number(options, "--max-records", 1, 32, 1); if (maximum is not (1 or 32)) throw new ArgumentException("check-input max-records must be1or32.");
            WriteNew(Need(options, "--output"), JsonSerializer.SerializeToUtf8Bytes(new InputCheckReport("sezika/exposed-regression-input-check/v1", "passed_input_contract_only", OriginalSha, AuditSha, 32, inputs.Length, maximum, 3, false, false, false), Json.Default.InputCheckReport), budget);
            return 0;
        }
        using var reportLease = Open(Need(options, "--training-report"), budget);
        var reportBytes = Snapshot(reportLease, 4_194_304, budget); var reportSha = Need(options, "--report-sha256"); RequireHash(Hash(reportBytes), reportSha);
        StrictJson(reportBytes, budget, 500_000);
        var report = JsonSerializer.Deserialize(reportBytes, Json.Default.TrainingInput) ?? throw new InvalidDataException("Expanded report null.");
        VerifyTraining(report, budget);
        using var manifestLease = Open(Path.Combine(Need(options, "--package"), "model.json"), budget);
        RequireHash(Hash(Snapshot(manifestLease, 16_384, budget)), report.EncoderManifestSha256);
        using var headLease = Open(Need(options, "--head"), budget);
        var headBytes = Snapshot(headLease, 131_072, budget); RequireHash(Hash(headBytes), report.HeadSha256);
        var head = IndependentMarkerHeadAssetStore.Load(headLease.Name, report.Identity, report.FeatureSha256, report.HeadSha256, budget.Token);
        var weights = head.Head.CopyWeights(); try { RequireHash(Hash(MemoryMarshal.AsBytes(weights.AsSpan())), report.ParameterSha256); } finally { Array.Clear(weights); }
        if (head.CompletedSteps != 24_576 || head.Seed != 17 || head.LearningRate != 0.001f) throw new InvalidDataException("Loaded expanded head optimizer identity differs.");
        var rows = new RegressionRow[32]; var started = 0; var completed = 0; CudaMemorySnapshot snapshot; CudaDeviceInfo actualDevice; int actualDeviceIndex; long peak, working;
        using (var model = IndependentModelLoader.Load(Need(options, "--package"), new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(300) }, budget.Token))
        using (var device = CudaDevice.Open(Number(options, "--device", 0, 15, 0)))
        {
            if (model.Encoder.Config.MaxTokens != report.MaxTokens) throw new InvalidDataException("Regression encoder token profile differs.");
            if (device.Info.Uuid != report.Device.Uuid || device.Info.DriverVersion != report.Device.DriverVersion) throw new InvalidDataException("Regression device differs from expanded training report.");
            actualDevice = device.Info; actualDeviceIndex = device.DeviceIndex;
            RequireHash(model.EncoderSha256, report.Identity.EncoderSha256); RequireHash(model.TokenizerSha256, report.Identity.TokenizerSha256);
            using (var pipeline = new IndependentCudaDecisionPipeline(device, model, head, budget.Token))
            {
                for (var index = 0; index < 32; index++)
                {
                    CheckGpu(device, budget); var input = inputs[index];
                    var sequence = IndependentDecisionV1SequenceBuilder.Build(model.Tokenizer, input.Request, input.Question, new IndependentDecisionSequenceOptions { MaxTokens = report.MaxTokens }, budget.Token);
                    if (!sequence.CandidateIds.SequenceEqual(input.CandidateIds) || sequence.TokenIds.Length < 2 || sequence.TokenIds.Length > report.MaxTokens) throw new InvalidDataException("Regression renderer identity/strict8192 differs.");
                    started++; var logits = pipeline.Score(sequence.TokenIds, sequence.Type, sequence.MarkerPositions, budget.Token); completed++; CheckGpu(device, budget);
                    if (logits.Length != input.CandidateIds.Length || logits.Any(value => !double.IsFinite(value))) throw new InvalidDataException("Regression GPU logits incomplete/nonfinite.");
                    var max = logits.Max(); var probabilities = logits.Select(value => Math.Exp(value - max)).ToArray(); var sum = probabilities.Sum();
                    for (var candidate = 0; candidate < probabilities.Length && candidate < 5; candidate++) { budget.Check(); probabilities[candidate] /= sum; }
                    var prediction = Array.IndexOf(logits, max); var nll = max + Math.Log(sum) - logits[input.CorrectedTarget];
                    if (!double.IsFinite(nll) || nll < 0) throw new InvalidDataException("Regression stable-softmax teacher NLL invalid.");
                    rows[index] = new(input.Source.RecordId, input.Source.Language, input.Source.Split ?? "", sequence.Type.ToString().ToLowerInvariant(), input.CandidateIds, input.Source.TargetIndex,
                        input.CorrectedTarget, input.CandidateIds[input.CorrectedTarget], input.CorrectedTarget != input.Source.TargetIndex, prediction, input.CandidateIds[prediction], prediction == input.CorrectedTarget,
                        logits, probabilities, nll, sequence.TokenIds.Length, sequence.MarkerPositions, Hash(MemoryMarshal.AsBytes(sequence.TokenIds.AsSpan())), Hash(MemoryMarshal.AsBytes(sequence.MarkerPositions.AsSpan())));
                    Console.Error.WriteLine($"regression GPU {index + 1}/32 {input.Source.RecordId}");
                }
            }
            snapshot = device.GetMemorySnapshot(); CheckGpu(device, budget);
            if (snapshot.OwnedBytes != 0 || snapshot.OwnedAllocationCount != 0 || snapshot.LoadedModuleCount != 0 || snapshot.ReleaseFailureCount != 0) throw new InvalidDataException("Regression pipeline did not release task-owned GPU resources.");
            using var process = Process.GetCurrentProcess(); peak = process.PeakWorkingSet64; working = process.WorkingSet64;
        }
        budget.Check(); if (started != 32 || completed != 32 || peak > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Regression full denominator/memory differs.");
        var scoreSupervision = report.LoadedObservations.Rows.Count(row => row.Type == "score" && row.Split == "train" && row.Variant == "original");
        var groups = rows.GroupBy(row => row.Type).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new RegressionGroup(group.Key, group.Count(), group.Count(row => row.AgreesWithCorrectedAudit),
            group.Count(row => row.AgreesWithCorrectedAudit) / (double)group.Count(), group.Average(row => row.CorrectedAuditNll), group.Key == "score" ? scoreSupervision : report.LoadedObservations.Rows.Count(row => row.Type == group.Key && row.Split == "train" && row.Variant == "original"))).ToArray();
        var result = new RegressionReport("sezika/exposed-original32-corrected-label-gpu-regression/v2", "completed_exposed_regression_not_quality_acceptance", "previously_exposed32_with_ai_audit_sidecar_not_gold_or_sealed",
            OriginalSha, AuditSha, reportSha, report.HeadSha256, report.FeatureSha256, report.ParameterSha256, report.Identity, actualDevice, actualDeviceIndex, 32, 32, started, completed, 3, report.MaxTokens,
            "cuda-independent-linear-marker-head", "host_stable_softmax_of_gpu_logits_not_calibrated_accuracy", scoreSupervision,
            scoreSupervision == 0 ? "Score has zero new supervised training examples; report every Score regression result explicitly; no quality-improvement claim." : "Score supervision count is reported from the pinned expanded training observations.",
            rows, groups, snapshot, peak, working, true, true, budget.ElapsedSeconds, new Acceptance()) { EncoderManifestSha256 = report.EncoderManifestSha256 };
        WriteNew(Need(options, "--output"), JsonSerializer.SerializeToUtf8Bytes(result, Json.Default.RegressionReport), budget); return 0;
    }
    private static Input[] BindAudit(SourceRow[] records, AuditInput audit, Budget budget)
    {
        if (audit.Contract != "sezika/independent-ai-label-audit/v1" || audit.Status != "completed_and_strictly_validated" || audit.HumanReview != "pending" || audit.Formal || audit.EligibleAsSealedTest || audit.OriginalDataModified || audit.Reviews.Length != 32) throw new InvalidDataException("Pinned exposed AI audit contract differs.");
        RequireHash(audit.SourceSha256, OriginalSha); var seen = new HashSet<string>(StringComparer.Ordinal); var result = new Input[32]; var changes = 0;
        for (var index = 0; index < 32; index++)
        {
            budget.Check(); var row = records[index]; var request = IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes(row.Request.GetRawText()));
            if (request.Questions.Count != 1 || request.ModelId != IndependentModelLoader.ModelId) throw new InvalidDataException("Original regression requires one fixed-model actual request.");
            var question = request.Questions[0]; var ids = CandidateIds(question); var review = audit.Reviews.Single(item => item.RecordId == row.RecordId);
            if (!seen.Add(row.RecordId) || review.QuestionId != question.Id || review.AnswerStatus != "determinate" || review.OriginalTargetIndex != row.TargetIndex || (uint)row.TargetIndex >= (uint)ids.Length ||
                review.OriginalTargetId != ids[row.TargetIndex] || (uint)review.SuggestedCorrectIndex >= (uint)ids.Length || review.SuggestedCorrectId != ids[review.SuggestedCorrectIndex] || !JsonElement.DeepEquals(review.OriginalRequest, row.Request)) throw new InvalidDataException("Audit/source canonical question and target binding differs.");
            var changed = row.TargetIndex != review.SuggestedCorrectIndex; if (review.ChangeOriginalLabel != changed) throw new InvalidDataException("Audit change flag differs.");
            if (changed)
            {
                changes++; var expected = row.RecordId switch { "general-en-score-03" => (2, 3), "general-zh-boolean-02" => (1, 0), "general-en-boolean-01" => (1, 0), _ => throw new InvalidDataException("Unexpected correction record.") };
                if ((row.TargetIndex, review.SuggestedCorrectIndex) != expected) throw new InvalidDataException("Frozen three corrections differ.");
            }
            result[index] = new(row, request, question, ids, review.SuggestedCorrectIndex);
        }
        if (changes != 3) throw new InvalidDataException("Exactly three frozen audit corrections are required."); return result;
    }
    private static void VerifyTraining(TrainingInput report, Budget budget)
    {
        budget.Check();
        if (report.TrainingContract != "sezika/teacher-labelled-gpu-expanded-experiment-training/v4" || report.Status != "completed_experiment_not_formal_acceptance" || report.CompletedSteps != 24_576 || report.Seed != 17 || report.LearningRate != 0.001f || !report.EncoderFrozen || report.MaxTokens != 8192 || report.TrainCount != 162 || report.DevelopmentCount != 53 || report.EncoderForwardsCompleted != 231 || report.LoadedObservations.Rows.Length != 231 ||
            report.Identity.ModelId != IndependentModelLoader.ModelId || report.Identity.HiddenSize != 768 || report.Identity.Protocol != "decision-v1" || report.Identity.LengthPolicy != "strict" || report.Acceptance.AnyTrue) throw new InvalidDataException("Expanded head report contract differs.");
        RequireHash(report.ManifestSha256, "E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6");
        RequireHash(report.DerivationLedgerSha256, "55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0");
        if (report.LoadedObservations.Rows.Count(row=>row.Split=="train" && row.Variant=="original")!=162 || report.LoadedObservations.Rows.Count(row=>row.Split=="development" && row.Variant=="original")!=53 || report.LoadedObservations.Rows.Count(row=>row.Split=="train" && row.Variant=="fixed-left1" && row.Type=="choice")!=16 || report.LoadedObservations.Rows.Any(row=>row.Type=="score")) throw new InvalidDataException("Repaired actual observation denominator differs.");
        RequireHash(report.Identity.EncoderSha256, IndependentModelLoader.ConvertedEncoderSha256); RequireHash(report.Identity.TokenizerSha256, IndependentModelLoader.TokenizerSha256); RequireHash(report.Identity.DataManifestSha256, report.ManifestSha256);
    }
    private static int Lengths(Dictionary<string, string> options, Budget budget)
    {
        var maximum = Number(options, "--max-records", 1, 256, 1); if (maximum is not (1 or 256)) throw new ArgumentException("lengths max-records must be1or256.");
        using var source = Open(Need(options, "--records"), budget); var bytes = Snapshot(source, 256 * 65_538 + 3, budget); var sourceHash = Hash(bytes); RequireHash(sourceHash, Need(options, "--records-sha256"));
        var records = ReadRecords(bytes, 256, budget);
        using var tokenizerLease = Open(Need(options, "--tokenizer"), budget); bytes = Snapshot(tokenizerLease, 17_525_329, budget);
        if (bytes.Length != 17_525_329) throw new InvalidDataException("Fixed tokenizer byte size differs."); RequireHash(Hash(bytes), IndependentModelLoader.TokenizerSha256); Array.Clear(bytes);
        var tokenizer = new TokenizerJson(tokenizerLease.Name, budget.Token); budget.Check();
        if (tokenizer.BosId != 2 || tokenizer.EosId != 1 || tokenizer.MaskId != 4) throw new InvalidDataException("Fixed tokenizer special IDs differ.");
        var rows = new LengthRow[maximum]; var completed = 0; var failed = 0; var maximumMeasured = 0;
        for (var index = 0; index < maximum && index < 256; index++)
        {
            budget.Check(); var row = records[index];
            try
            {
                var request = IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes(row.Request.GetRawText()));
                if (request.ModelId != IndependentModelLoader.ModelId || request.Questions.Count != 1) throw new InvalidDataException("Lengths requires one fixed-model actual request per row.");
                var question = request.Questions[0]; var ids = CandidateIds(question);
                var sequence = IndependentDecisionV1SequenceBuilder.Build(tokenizer, request, question, new IndependentDecisionSequenceOptions { MaxTokens = 1024 }, budget.Token);
                if (sequence.TokenIds.Length is < 2 or > 1024 || !sequence.CandidateIds.SequenceEqual(ids)) throw new InvalidDataException("Strict renderer length/candidate order differs.");
                maximumMeasured = Math.Max(maximumMeasured, sequence.TokenIds.Length); completed++;
                rows[index] = new(row.RecordId, row.Language, row.Split ?? "", "rendered", sequence.TokenIds.Length, sequence.MarkerPositions, ids, null);
            }
            catch (DecisionException error) { failed++; rows[index] = new(row.RecordId, row.Language, row.Split ?? "", "rejected_by_actual_parser_or_strict_renderer", null, [], [], error.Message); }
            catch (InvalidDataException error) { failed++; rows[index] = new(row.RecordId, row.Language, row.Split ?? "", "invalid_actual_request", null, [], [], error.Message); }
            if ((index + 1) % 16 == 0 || index + 1 == maximum) Console.Error.WriteLine($"rendered lengths {index + 1}/{maximum}: completed={completed} failed={failed}");
        }
        budget.Check(); using var process = Process.GetCurrentProcess(); if (process.PeakWorkingSet64 > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Lengths host peak exceeds4GiB.");
        var result = new LengthReport("sezika/expanded-prepared256-strict-rendered-lengths/v1", failed == 0 ? "completed_rendered_lengths_no_model_execution" : "completed_with_rejected_records_no_model_execution",
            sourceHash, IndependentModelLoader.TokenizerSha256, Path.GetFullPath(source.Name), Path.GetFullPath(tokenizerLease.Name), 256, maximum, maximum, completed, failed, 0, 1024, maximumMeasured,
            "maximum_measured_tokens_covers_successful_rows_only; rejected_rows_have_null_length", 60, budget.ElapsedSeconds, false, false, false, false, process.PeakWorkingSet64, rows);
        WriteNew(Need(options, "--output"), JsonSerializer.SerializeToUtf8Bytes(result, Json.Default.LengthReport), budget); return failed == 0 ? 0 : 1;
    }
    private static SourceRow[] ReadRecords(byte[] bytes, int count, Budget budget)
    {
        using var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes)); var result = new SourceRow[count]; var seen = new HashSet<string>(StringComparer.Ordinal);
        if (count is < 1 or > 256) throw new InvalidDataException("Record denominator out of bounds.");
        for (var index = 0; index < count && index < 256; index++)
        {
            budget.Check(); var text = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Missing/empty expected row."); var utf8 = Encoding.UTF8.GetBytes(text); if (utf8.Length > 65_536) throw new InvalidDataException("Row exceeds64KiB."); StrictJson(utf8, budget);
            var row = JsonSerializer.Deserialize(utf8, Json.Default.SourceRow) ?? throw new InvalidDataException("Source row null."); if (row.Language is not ("zh" or "en") || string.IsNullOrWhiteSpace(row.RecordId) || row.RecordId.Length > 128 || !seen.Add(row.RecordId)) throw new InvalidDataException("Source row identity/language differs."); result[index] = row;
        }
        budget.Check(); if (reader.ReadLine() is not null) throw new InvalidDataException("Unexpected extra row.");
        return result;
    }
    private static string[] CandidateIds(IndependentDecisionQuestion question) => question switch { IndependentChoiceQuestion choice => choice.Candidates.Select(item => item.Id).ToArray(), IndependentScoreQuestion score => Enumerable.Range(0, score.Levels.Count).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray(), IndependentBooleanQuestion => ["false", "true"], _ => throw new InvalidDataException("Unsupported request question.") };
    private static void CheckGpu(CudaDevice device, Budget budget) { budget.Check(); var memory = device.GetMemorySnapshot(); if (memory.OwnedBytes > GpuLimit || memory.PeakOwnedBytes > GpuLimit || memory.ReleaseFailureCount != 0) throw new InvalidDataException("Regression GPU owned limit/native release differs."); }
    private static FileStream Open(string path, Budget budget) { path = Path.GetFullPath(path); CheckPath(path, budget); return new(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
    private static byte[] Snapshot(FileStream stream, int maximum, Budget budget) { budget.Check(); if (stream.Length is < 1 || stream.Length > maximum) throw new InvalidDataException("Snapshot byte bound exceeded."); var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); budget.Check(); return bytes; }
    private static void CheckPath(string path, Budget budget)
    {
        string? item = Path.GetFullPath(path); for (var depth = 0; depth < 64 && item is not null; depth++) { budget.Check(); if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Input/output links/junctions rejected."); item = Path.GetDirectoryName(item); } if (item is not null) throw new InvalidDataException("Path depth exceeded.");
    }
    private static void WriteNew(string path, byte[] bytes, Budget budget)
    {
        budget.Check(); path = Path.GetFullPath(path); CheckPath(path, budget); if (bytes.Length > 1_048_576 || !Directory.Exists(Path.GetDirectoryName(path)) || File.Exists(path) || Directory.Exists(path)) throw new InvalidDataException("Report requires fresh exclusive file under existing parent,<=1MiB.");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); stream.Write(bytes); stream.Flush(true); budget.Check(); Console.WriteLine($"{{\"report_sha256\":\"{Hash(bytes)}\",\"report_path\":{JsonSerializer.Serialize(path, Json.Default.String)}}}");
    }
    private static void StrictJson(ReadOnlySpan<byte> bytes, Budget budget, int maximumTokens = 65_536)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 }); var objects = new Stack<HashSet<string>>(); var count = 0;
        while (reader.Read()) { budget.Check(); if (++count > maximumTokens) throw new InvalidDataException("JSON token bound exceeded."); if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal)); else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop(); else if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek().Add(reader.GetString()!)) throw new InvalidDataException("Duplicate JSON key."); }
    }
    private static Dictionary<string, string> Parse(string[] arguments, CancellationToken token)
    {
        if (arguments.Length is < 7 or > 25 || (arguments.Length & 1) == 0) throw new ArgumentException("Expected bounded named option pairs.");
        string[] allowed = arguments[0] switch { "lengths" => ["--records", "--records-sha256", "--tokenizer", "--output", "--max-records"], "check-input" => ["--records", "--audit", "--output", "--max-records"], _ => ["--records", "--audit", "--training-report", "--report-sha256", "--head", "--package", "--output", "--device", "--timeout-seconds"] };
        var values = new Dictionary<string, string>(StringComparer.Ordinal); var clock = Stopwatch.StartNew();
        for (var index = 1; index < arguments.Length && index < 25; index += 2) { token.ThrowIfCancellationRequested(); if (clock.Elapsed.TotalSeconds >= 5 || !allowed.Contains(arguments[index], StringComparer.Ordinal) || string.IsNullOrWhiteSpace(arguments[index + 1]) || arguments[index + 1].Length > 2048 || !values.TryAdd(arguments[index], arguments[index + 1])) throw new ArgumentException("Unknown/duplicate/oversized option."); }
        return values;
    }
    private static string Need(Dictionary<string, string> options, string name) => options.TryGetValue(name, out var value) ? value : throw new ArgumentException("Missing " + name);
    private static int Number(Dictionary<string, string> options, string name, int minimum, int maximum, int fallback) { if (!options.TryGetValue(name, out var text)) return fallback; if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < minimum || number > maximum) throw new ArgumentException("Invalid " + name); return number; }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void RequireHash(string actual, string expected) { if (expected.Length != 64 || !expected.All(char.IsAsciiHexDigit) || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pinned64hex SHA differs."); }
}

internal sealed class Budget : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew(); private readonly CancellationTokenSource _deadline; private readonly int _seconds;
    private readonly Process _process = Process.GetCurrentProcess(); private double _nextMemoryCheck;
    internal double ElapsedSeconds => _clock.Elapsed.TotalSeconds; internal CancellationToken Token => _deadline.Token;
    internal Budget(int seconds, CancellationToken token) { _seconds = seconds; _deadline = CancellationTokenSource.CreateLinkedTokenSource(token); _deadline.CancelAfter(TimeSpan.FromSeconds(seconds)); }
    internal void Check()
    {
        Token.ThrowIfCancellationRequested(); var elapsed = _clock.Elapsed.TotalSeconds;
        if (elapsed >= _seconds) throw new OperationCanceledException("Explicit stage deadline.");
        // JSON/token loops can call Check hundreds of thousands of times.
        // Sample resident memory at most twice per second using one owned handle.
        if (elapsed >= _nextMemoryCheck)
        {
            _nextMemoryCheck = elapsed + 0.5; _process.Refresh();
            if (_process.WorkingSet64 > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Process resident exceeds4GiB.");
        }
    }
    public void Dispose() { _deadline.Dispose(); _process.Dispose(); }
}
internal sealed record Input(SourceRow Source, IndependentDecisionRequest Request, IndependentDecisionQuestion Question, string[] CandidateIds, int CorrectedTarget);
internal sealed class SourceRow { public required string RecordId { get; init; } public required string Language { get; init; } public string? Split { get; init; } public int TargetIndex { get; init; } public required JsonElement Request { get; init; } }
internal sealed class AuditInput { public required string Contract { get; init; } public required string Status { get; init; } public required string SourceSha256 { get; init; } public required string HumanReview { get; init; } public bool Formal { get; init; } public bool EligibleAsSealedTest { get; init; } public bool OriginalDataModified { get; init; } public required AuditRow[] Reviews { get; init; } }
internal sealed class AuditRow { public required string RecordId { get; init; } public required string QuestionId { get; init; } public int OriginalTargetIndex { get; init; } public required string OriginalTargetId { get; init; } public required string AnswerStatus { get; init; } public int SuggestedCorrectIndex { get; init; } public required string SuggestedCorrectId { get; init; } public bool ChangeOriginalLabel { get; init; } public required JsonElement OriginalRequest { get; init; } }
internal sealed class TrainingInput { public required string EncoderManifestSha256 { get; init; } public required string DerivationLedgerSha256 { get; init; } public required string TrainingContract { get; init; } public required string Status { get; init; } public required string ManifestSha256 { get; init; } public required string HeadSha256 { get; init; } public required string FeatureSha256 { get; init; } public required string ParameterSha256 { get; init; } public required MarkerFeatureIdentity Identity { get; init; } public required DeviceInput Device { get; init; } public int CompletedSteps { get; init; } public int Seed { get; init; } public float LearningRate { get; init; } public bool EncoderFrozen { get; init; } public int MaxTokens { get; init; } public int TrainCount { get; init; } public int DevelopmentCount { get; init; } public int EncoderForwardsCompleted { get; init; } public required ObservationInput LoadedObservations { get; init; } public required Acceptance Acceptance { get; init; } }
internal sealed record DeviceInput(string Uuid, string Name, int DriverVersion, int ComputeCapabilityMajor, int ComputeCapabilityMinor, ulong TotalMemoryBytes, int DeviceIndex);
internal sealed class ObservationInput { public required ObservationRow[] Rows { get; init; } }
internal sealed record ObservationRow(string Type, string Split, string Variant);
internal sealed class Acceptance { public bool FormalTrainingAdmission { get; init; } public bool HumanQuality { get; init; } public bool LicenseRelease { get; init; } public bool Calibration { get; init; } public bool SealedTest { get; init; } public bool NativeAot { get; init; } public bool Performance { get; init; } public bool Publication { get; init; } [JsonIgnore] public bool AnyTrue => FormalTrainingAdmission || HumanQuality || LicenseRelease || Calibration || SealedTest || NativeAot || Performance || Publication; }
internal sealed record RegressionRow(string RecordId, string Language, string Split, string Type, string[] CandidateIds, int OriginalTargetIndex, int CorrectedAuditTargetIndex, string CorrectedAuditTargetId, bool LabelCorrected, int PredictedIndex, string SelectedCandidateId, bool AgreesWithCorrectedAudit, double[] GpuLogits, double[] StableSoftmaxProbabilities, double CorrectedAuditNll, int TokenCount, int[] MarkerPositions, string TokenSha256, string MarkerSha256);
internal sealed record RegressionGroup(string Type, int Total, int AgreementCount, double AgreementRate, double MeanCorrectedAuditNll, int ExpandedTrainingSupervisionCount);
internal sealed record RegressionReport(string Contract, string Status, string Use, string OriginalRecordsSha256, string CorrectedAuditSha256, string ExpandedTrainingReportSha256, string HeadSha256, string ExpandedFeatureSha256, string ParameterSha256, MarkerFeatureIdentity Identity, CudaDeviceInfo Device, int DeviceIndex, int Total, int Completed, int EncoderForwardsStarted, int EncoderForwardsCompleted, int Corrections, int MaxTokens, string Backend, string ProbabilitySemantics, int ScoreExpandedTrainingSupervisionCount, string ScoreCaveat, RegressionRow[] Rows, RegressionGroup[] Groups, CudaMemorySnapshot SnapshotAfterPipelineDispose, long ProcessPeakWorkingSetBytes, long ProcessWorkingSetBytes, bool DeviceDisposeReturned, bool ModelDisposeReturned, double ElapsedSeconds, Acceptance Acceptance) { public required string EncoderManifestSha256 { get; init; } }
internal sealed record InputCheckReport(string Contract, string Status, string OriginalRecordsSha256, string AuditSha256, int Records, int AuditBoundRecords, int RequestedMaximumRecords, int Corrections, bool GpuOpened, bool EncoderLoaded, bool TrainingExecuted);
internal sealed record LengthRow(string RecordId, string Language, string Split, string Status, int? TokenCount, int[] MarkerPositions, string[] CandidateIds, string? Failure);
internal sealed record LengthReport(string Contract, string Status, string RecordsSha256, string TokenizerSha256, string ActualRecordsPath, string ActualTokenizerPath, int SourceRecords, int RequestedRecords, int AttemptedRecords, int CompletedRecords, int FailedRecords, int NotAttemptedRecords, int StrictMaxTokens, int MaximumMeasuredTokens, string MaximumSemantics, int TimeoutSeconds, double ElapsedSeconds, bool EncoderLoaded, bool FeatureComputed, bool TrainingExecuted, bool GpuOpened, long ProcessPeakWorkingSetBytes, LengthRow[] Rows);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip, WriteIndented = true)]
[JsonSerializable(typeof(SourceRow))]
[JsonSerializable(typeof(AuditInput))]
[JsonSerializable(typeof(TrainingInput))]
[JsonSerializable(typeof(RegressionReport))]
[JsonSerializable(typeof(InputCheckReport))]
[JsonSerializable(typeof(LengthReport))]
[JsonSerializable(typeof(string))]
internal partial class Json : JsonSerializerContext;
