using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

namespace Sezika.RepositoryTrainer;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record EvaluationCase(string Id, string Language, MarkerQuestionType Type, string State,
    string Instruction, TrainingCandidate[] Candidates, string LabelKind, string? CorrectCandidateId,
    double? ExpectedScore, string SourceId);
internal sealed record TensorShape(string Name, int[] Shape, int Elements);
internal sealed record LoadReceipt(string Status, string RunId, string PackageSha256, string WeightsSha256,
    string TokenizerSha256, string HeadPayloadSha256, int TensorCount, long FloatCount, TensorShape[] Tensors,
    bool AllFinite, bool HeadNonzero, long CompletedUpdates, string License, string CalibrationStatus);
internal sealed record EvaluationRow(string Id, string Language, string Type, string LabelKind,
    string SourceId, string Status, string? Error, int? TokenCount, string? SequenceSha256, string[] CandidateIds,
    double[] Probabilities, string? PredictedCandidateId, string? CorrectCandidateId, bool? Correct,
    double? ScoreAbsoluteError, double ElapsedSeconds);
internal sealed record EvaluationReceipt(string Status, string ModelName, string PackageSha256, string WeightsSha256,
    string TokenizerSha256, string CasesSha256, int MaximumTokens, int Total, int Success, int Failed, int Refused,
    int GoldTotal, int GoldCorrect, int GoldAnswered, int TeacherTotal, int TeacherMatched, EvaluationRow[] Rows,
    CudaDeviceInfo Device, CudaMemorySnapshot Before, CudaMemorySnapshot After, bool ResourcesReclaimed,
    int ProcessId, DateTime ProcessStartUtc, double ElapsedSeconds, bool QualityAccepted, bool Calibrated, bool AotAccepted);

internal static class Acceptance
{
    internal static void Baseline(string root, string headPath, string reportPath, string expectedHeadSha, string expectedReportSha,
        string casesPath, string casesSha, string expectedUuid, int maximumCases, int maximumTokens,
        ulong maximumOwnedBytes, string output, RunBudget budget)
    {
        if (File.Exists(output) || File.Exists(output + ".load.json")) throw new IOException("Preserve existing evidence.");
        if (Data.FileHash(reportPath, budget) != expectedReportSha || Data.FileHash(headPath, budget) != expectedHeadSha ||
            Data.FileHash(casesPath, budget) != casesSha) throw new InvalidDataException("Explicit baseline/cases identity mismatch.");
        using var stream = File.OpenRead(reportPath);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Baseline metadata byte bound.");
        using var doc = System.Text.Json.JsonDocument.Parse(stream, new() { MaxDepth = 32 });
        var report = doc.RootElement; var id = report.GetProperty("identity");
        var identity = new MarkerFeatureIdentity(id.GetProperty("model_id").GetString()!, id.GetProperty("encoder_sha256").GetString()!,
            id.GetProperty("tokenizer_sha256").GetString()!, id.GetProperty("data_manifest_sha256").GetString()!,
            id.GetProperty("hidden_size").GetInt32(), id.GetProperty("protocol").GetString()!, id.GetProperty("length_policy").GetString()!);
        if (!report.GetProperty("encoder_frozen").GetBoolean() || report.GetProperty("completed_steps").GetInt32() != 24576 ||
            !expectedHeadSha.Equals(report.GetProperty("head_sha256").GetString(), StringComparison.OrdinalIgnoreCase) || identity.EncoderSha256 != IndependentModelLoader.ConvertedEncoderSha256 ||
            identity.TokenizerSha256 != IndependentModelLoader.TokenizerSha256 || identity.ModelId != IndependentModelLoader.ModelId)
            throw new InvalidDataException("Explicit frozen-encoder baseline contract differs.");
        using var model = IndependentModelLoader.Load(root, new() { Kernel = EncoderKernelMode.Scalar }, budget.Token);
        var asset = IndependentMarkerHeadAssetStore.Load(headPath, identity, report.GetProperty("feature_sha256").GetString()!, expectedHeadSha, budget.Token);
        var cases = Assets.Read(casesPath, TrainerJson.Default.EvaluationCaseArray, 16 * 1024 * 1024);
        if (cases.Length < 1 || cases.Length > maximumCases || cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != cases.Length)
            throw new InvalidDataException("Baseline case bound/ID contract.");
        var values = asset.Head.CopyWeights();
        try
        {
            var descriptors = Assets.Descriptors(model.Encoder.Config).Where(p => !p.IsHead).ToArray();
            Data.WriteNew(output + ".load.json", new LoadReceipt("PASS_frozen_encoder_and_explicit_24576_step_head_loaded", "frozen-encoder-24576-20261009",
                expectedReportSha, model.EncoderSha256, model.TokenizerSha256, Data.Hash(MemoryMarshal.AsBytes(values.AsSpan())),
                descriptors.Length, descriptors.Sum(p => (long)p.Elements), descriptors.Select(p => new TensorShape(p.Name, p.Shape, p.Elements)).ToArray(),
                true, values.Any(v => v != 0), asset.CompletedSteps, "component_specific_base_license_not_output_rights", "uncalibrated"), TrainerJson.Default.LoadReceipt);
            Evaluate("frozen-encoder-24576-20261009", expectedReportSha, model.EncoderSha256, model.TokenizerSha256, casesSha,
                model.Encoder.Config, model.Encoder.Weights, asset.Head, model.Tokenizer, cases, expectedUuid,
                maximumTokens, maximumOwnedBytes, output, budget);
        }
        finally { Array.Clear(values); }
    }

    internal static void Run(string root, string packageSha, string casesPath, string casesSha, string expectedUuid,
        int maximumCases, int maximumTokens, ulong maximumOwnedBytes, string output, RunBudget budget)
    {
        if (File.Exists(output) || File.Exists(output + ".load.json")) throw new IOException("Preserve existing acceptance evidence.");
        root = Path.GetFullPath(root);
        var packagePath = Data.Within(root, "package.json");
        if (Data.FileHash(packagePath, budget) != packageSha) throw new InvalidDataException("Explicit new package SHA mismatch.");
        var package = Assets.Read(packagePath, TrainerJson.Default.PackageReceipt);
        CheckPackage(package);
        if (Data.FileHash(Data.Within(root, "training-report.json"), budget) != package.TrainingReportSha256 ||
            Data.FileHash(Data.Within(root, "checkpoint-0000048/checkpoint.json"), budget) != package.CheckpointReceiptSha256 ||
            !File.Exists(Data.Within(root, "resources-reclaimed.txt")))
            throw new InvalidDataException("Saved report/checkpoint/closure identity differs.");
        var report = Assets.Read(Data.Within(root, "training-report.json"), TrainerJson.Default.TrainReceipt);
        if (report.Inputs != package.Inputs || report.CompletedUpdates != package.CompletedUpdates || report.RunId != package.RunId || report.ProcessId == Environment.ProcessId)
            throw new InvalidDataException("Training provenance or independent process differs.");
        var tokenizerPath = Data.Within(root, "tokenizer/tokenizer.json");
        if (Data.FileHash(tokenizerPath, budget) != package.TokenizerSha256 || package.TokenizerSha256 != IndependentModelLoader.TokenizerSha256)
            throw new InvalidDataException("Tokenizer identity differs.");
        if (Data.FileHash(casesPath, budget) != casesSha) throw new InvalidDataException("Evaluation cases SHA differs.");
        var cases = Assets.Read(casesPath, TrainerJson.Default.EvaluationCaseArray, 16 * 1024 * 1024);
        if (cases.Length < 1 || cases.Length > maximumCases || cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != cases.Length)
            throw new InvalidDataException("Case bound or duplicate ID.");
        foreach (var c in cases)
        {
            budget.Check();
            if (c.State.Length > 262144 || c.Instruction.Length > 65536 || c.Candidates.Length is < 2 or > 32 ||
                c.LabelKind is not ("gold" or "teacher" or "none" or "ordinal") || c.Language.Length is < 2 or > 16 ||
                c.Id.Length is < 1 or > 128 || c.SourceId.Length is < 1 or > 512 ||
                (c.Type == MarkerQuestionType.Score && c.LabelKind == "gold") ||
                (c.LabelKind is "gold" or "teacher" && !c.Candidates.Any(v => v.Id == c.CorrectCandidateId)))
                throw new InvalidDataException("Evaluation label/type/input contract.");
        }
        using var file = new TensorFile(Data.Within(root, "weights.safetensors"), package.WeightsSha256, budget);
        var weights = Assets.LoadWeights(file, package.Config, budget, out var head);
        try
        {
            var descriptors = Assets.Descriptors(package.Config);
            var load = new LoadReceipt("PASS_actual_new_package_tensors_tokenizer_loaded", package.RunId, packageSha,
                package.WeightsSha256, package.TokenizerSha256, Data.Hash(MemoryMarshal.AsBytes(head.AsSpan())),
                descriptors.Count, descriptors.Sum(p => (long)p.Elements), descriptors.Select(p => new TensorShape(p.Name, p.Shape, p.Elements)).ToArray(),
                true, head.Any(v => v != 0), package.CompletedUpdates, package.License, package.CalibrationStatus);
            var tokenizer = new TokenizerJson(tokenizerPath, budget.Token);
            if (tokenizer.BosId != 2 || tokenizer.EosId != 1 || tokenizer.MaskId != 4) throw new InvalidDataException("Tokenizer special ID contract.");
            Data.WriteNew(output + ".load.json", load, TrainerJson.Default.LoadReceipt);
            Evaluate(package.RunId, packageSha, package.WeightsSha256, package.TokenizerSha256, casesSha,
                package.Config, weights, new LinearMarkerHead(package.Config.HiddenSize, head), tokenizer, cases,
                expectedUuid, maximumTokens, maximumOwnedBytes, output, budget);
        }
        finally { Assets.ClearWeights(weights); Array.Clear(head); }
    }

    private static void CheckPackage(PackageReceipt p)
    {
        var c = p.Config;
        c.Validate();
        if (p.SchemaVersion != 1 || p.Method != "full_encoder_adamw_fp32" || p.RunId != "encoder-full-20261011-121827" ||
            p.CompletedUpdates != 48 || p.CalibrationStatus != "uncalibrated" || p.QualityAccepted || p.AotAccepted ||
            p.Inputs.BaseEncoderSha256 != IndependentModelLoader.ConvertedEncoderSha256 || p.Inputs.TokenizerSha256 != p.TokenizerSha256 ||
            !new[] { p.SourceCommit, p.DatasetCommit, p.ModelCommit }.All(s => s.Length == 40 && s.All(char.IsAsciiHexDigit)) ||
            !new[] { p.WeightsSha256, p.TokenizerSha256, p.TrainingReportSha256, p.CheckpointReceiptSha256 }.All(s => s.Length == 64 && s.All(char.IsAsciiHexDigit)) ||
            c.VocabularySize != 256000 || c.HiddenSize != 768 || c.IntermediateSize != 1152 || c.LayerCount != 22 || c.HeadCount != 12 ||
            c.MaxTokens != 8192 || c.GlobalAttentionEvery != 3 || c.LocalAttention != 128 || c.GlobalRopeTheta != 160000 || c.LocalRopeTheta != 160000 || c.NormEpsilon != 1e-5f)
            throw new InvalidDataException("Explicit fine-tuned package identity/configuration is unsupported.");
    }

    internal static void Evaluate(string modelName, string packageSha, string weightsSha, string tokenizerSha, string casesSha,
        ModernBertConfig config, ModernBertWeights weights, LinearMarkerHead head, TokenizerJson tokenizer,
        EvaluationCase[] cases, string expectedUuid, int maximumTokens, ulong maximumOwnedBytes, string output, RunBudget budget)
    {
        using var progress = new StreamWriter(new FileStream(output + ".rows.jsonl", FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        using var device = CudaDevice.Open(0);
        if (!device.Info.Uuid.Equals(expectedUuid, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Physical GPU UUID differs.");
        var before = device.GetMemorySnapshot();
        if (before.FreeBytes < maximumOwnedBytes + 512UL * 1024 * 1024) throw new InvalidDataException("Insufficient GPU headroom.");
        var watch = Stopwatch.StartNew();
        var rows = new List<EvaluationRow>();
        string status = "PASS_bounded_GPU_inference_only";
        Exception? fatal = null;
        try
        {
            using var scorer = new CudaMarkerInference(device, config, weights, head, maximumOwnedBytes, budget.Token);
            for (var i = 0; i < cases.Length; i++)
            {
                var c = cases[i];
                var timer = Stopwatch.StartNew();
                int? count = null; string? sequenceSha = null; var ids = c.Candidates.Select(v => v.Id).ToArray();
                try
                {
                    budget.Check();
                    var row = new TrainingRow(c.Id, c.Id, c.Id, "audit", c.Language, c.Type, c.State, c.Instruction, c.Candidates,
                        new float[c.Candidates.Length], "", i);
                    var request = Data.Request(row, out var question);
                    var sequence = IndependentDecisionV1SequenceBuilder.Build(tokenizer, request, question,
                        new() { MaxTokens = maximumTokens }, budget.Token);
                    ids = sequence.CandidateIds; count = sequence.TokenIds.Length;
                    if (!ids.SequenceEqual(c.Candidates.Select(v => v.Id))) throw new InvalidDataException("Rendered candidate identity/order differs.");
                    sequenceSha = Data.Hash(MemoryMarshal.AsBytes(sequence.TokenIds.AsSpan()));
                    var logits = scorer.Score(sequence.TokenIds, sequence.Type, sequence.MarkerPositions, budget.Token);
                    var maximum = logits.Max(); var probabilities = logits.Select(v => Math.Exp(v - maximum)).ToArray();
                    var sum = probabilities.Sum();
                    for (var j = 0; j < probabilities.Length; j++) probabilities[j] /= sum;
                    if (probabilities.Any(v => !double.IsFinite(v) || v < 0 || v > 1) || Math.Abs(probabilities.Sum() - 1) > 1e-9)
                        throw new ArithmeticException("Invalid prediction distribution.");
                    var selected = ids[Array.IndexOf(probabilities, probabilities.Max())];
                    double? ordinalError = c.ExpectedScore is null ? null : Math.Abs(Array.IndexOf(ids, selected) - c.ExpectedScore.Value);
                    rows.Add(new(c.Id, c.Language, c.Type.ToString(), c.LabelKind, c.SourceId, "success_uncalibrated", null,
                        count, sequenceSha, ids, probabilities, selected, c.CorrectCandidateId,
                        c.CorrectCandidateId is null ? null : selected == c.CorrectCandidateId, ordinalError, timer.Elapsed.TotalSeconds));
                }
                catch (DecisionException error)
                {
                    var refused = error.Code.Contains("budget", StringComparison.Ordinal) || error.Code.Contains("limit", StringComparison.Ordinal);
                    rows.Add(new(c.Id, c.Language, c.Type.ToString(), c.LabelKind, c.SourceId, refused ? "refused" : "failed", error.Code,
                        count, sequenceSha, ids, [], null, c.CorrectCandidateId, c.CorrectCandidateId is null ? null : false, null, timer.Elapsed.TotalSeconds));
                }
                catch (OperationCanceledException)
                {
                    rows.Add(new(c.Id, c.Language, c.Type.ToString(), c.LabelKind, c.SourceId, "failed", "cancelled_or_deadline",
                        count, sequenceSha, ids, [], null, c.CorrectCandidateId, c.CorrectCandidateId is null ? null : false, null, timer.Elapsed.TotalSeconds));
                }
                catch (Exception error)
                {
                    rows.Add(new(c.Id, c.Language, c.Type.ToString(), c.LabelKind, c.SourceId, "failed", error.GetType().Name + ": " + error.Message,
                        count, sequenceSha, ids, [], null, c.CorrectCandidateId, c.CorrectCandidateId is null ? null : false, null, timer.Elapsed.TotalSeconds));
                }
                Console.WriteLine($"acceptance {i + 1}/{cases.Length}: {c.Id} {rows[^1].Status} {timer.Elapsed.TotalSeconds:F3}s");
                progress.WriteLine(System.Text.Json.JsonSerializer.Serialize(rows[^1], TrainerJson.Default.EvaluationRow));
            }
        }
        catch (Exception error) { status = "FAIL_inference_setup_or_cleanup"; fatal = error; }
        for (var i = rows.Count; i < cases.Length; i++)
        {
            var c = cases[i];
            rows.Add(new(c.Id, c.Language, c.Type.ToString(), c.LabelKind, c.SourceId, "failed", fatal?.Message ?? "not_executed",
                null, null, c.Candidates.Select(v => v.Id).ToArray(), [], null, c.CorrectCandidateId, c.CorrectCandidateId is null ? null : false, null, 0));
        }
        var after = device.GetMemorySnapshot();
        if (status == "PASS_bounded_GPU_inference_only" && rows.Any(r => r.Status == "failed")) status = "COMPLETE_with_case_failures";
        if (status == "PASS_bounded_GPU_inference_only" && rows.All(r => r.Status == "refused")) status = "COMPLETE_all_cases_refused";
        var reclaimed = after.OwnedBytes == before.OwnedBytes && after.OwnedAllocationCount == before.OwnedAllocationCount &&
            after.LoadedModuleCount == before.LoadedModuleCount && after.ReleaseFailureCount == before.ReleaseFailureCount;
        if (!reclaimed) status = "FAIL_CUDA_resources_not_reclaimed";
        using var process = Process.GetCurrentProcess();
        Data.WriteNew(output, new EvaluationReceipt(status, modelName, packageSha, weightsSha, tokenizerSha, casesSha, maximumTokens,
            cases.Length, rows.Count(r => r.Status == "success_uncalibrated"), rows.Count(r => r.Status == "failed"), rows.Count(r => r.Status == "refused"),
            rows.Count(r => r.LabelKind == "gold"), rows.Count(r => r.LabelKind == "gold" && r.Correct == true),
            rows.Count(r => r.LabelKind == "gold" && r.Status == "success_uncalibrated"), rows.Count(r => r.LabelKind == "teacher"),
            rows.Count(r => r.LabelKind == "teacher" && r.Correct == true), rows.ToArray(), device.Info, before, after, reclaimed,
            Environment.ProcessId, process.StartTime.ToUniversalTime(), watch.Elapsed.TotalSeconds, false, false, false), TrainerJson.Default.EvaluationReceipt);
        if (fatal is not null) throw new InvalidOperationException("Inference setup/cleanup failed; complete denominator saved.", fatal);
        if (!reclaimed) throw new IOException("CUDA resources not reclaimed.");
    }
}
