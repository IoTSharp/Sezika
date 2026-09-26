using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Sezika;
using Sezika.Cuda;

namespace Sezika.Benchmarks;

internal sealed class PerformanceProfile
{
    public int SchemaVersion { get; init; } = 2;
    public string InputSetVersion { get; init; } = "sezika.performance-inputs.v1";
    public string RenderingVersion { get; init; } = "sezika.prompt.laya-4066d5d5.v2";
    public string DiagnosticLengthPolicy { get; init; } = "laya_compatible_proposal_then_actual_request_policy";
    public string[] SelectedLengths { get; init; } = [];
    public int[] SelectedQuestions { get; init; } = [];
    public string Status { get; set; } = "pending";
    public int PlannedCases => SelectedLengths.Length * SelectedQuestions.Length;
    public int SuccessfulCases => Cases.Count(row => row.Status == "measured");
    public int RejectedCases => Cases.Count(row => row.Status == "rejected");
    public double RequestCoverage => PlannedCases == 0 ? 0 : (double)SuccessfulCases / PlannedCases;
    public int PlannedQuestions => SelectedLengths.Length * SelectedQuestions.Sum();
    public double QuestionCoverage => PlannedQuestions == 0 ? 0 :
        (double)Cases.Where(row => row.Status == "measured").Sum(row => row.Questions) / PlannedQuestions;
    public string CoverageScope { get; init; } = "Complete measured cases / all selected planned cases, and their question counts. Rejected, failed, cancelled and pending rows remain in the denominator. This is execution coverage, not accuracy or abstention coverage.";
    public string LengthScope { get; init; } = "short/medium/long are fixed state repetition presets, not guaranteed token counts. The shared PromptSequenceBuilder reports original lengths and a Laya-compatible retained proposal using independent 256-prefix/1024-total budgets. The real request keeps its explicit length policy (strict by default); actual tokens are reported only after a real pipeline call. Truncation, runtime rejection and inference success remain distinct.";
    public string StageScope { get; init; } = "E2E samples disable instrumentation. Tokenizer/rendering is measured in an independent preparation pass. Encoder/head wall time and CUDA events come from one extra instrumented real request; CPU split measurements use an additional independent Encode + ScoreEncoded pass, including ScoreEncoded's defensive hidden-state copy. These intervals must not be summed or subtracted to reconstruct E2E.";
    public List<ProfileCase> Cases { get; } = [];
}

internal sealed class ProfileCase
{
    public required string Id { get; init; }
    public required string LengthPreset { get; init; }
    public required int StateRepetitions { get; init; }
    public required int Questions { get; init; }
    public int CandidatesPerQuestion { get; init; } = 2;
    public required string InputJson { get; init; }
    public required string InputSha256 { get; init; }
    public string Status { get; set; } = "pending";
    public string Phase { get; set; } = "pending";
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public int RuntimeSequenceLimit { get; set; }
    public int RuntimePrefixTokenBudget { get; set; }
    public PromptLengthPolicy LengthPolicy { get; set; } = PromptLengthPolicy.Strict;
    public int OriginalTotalTokens { get; set; }
    public bool ProposedTruncation { get; set; }
    public int RenderedTotalTokens { get; set; }
    public List<ProfileSequence> RenderedSequences { get; } = [];
    public List<ProfileSequence> ActualSequences { get; } = [];
    public string RenderingVerification { get; set; } = "unavailable_no_pipeline_call";
    public double? DiscoveryWallMilliseconds { get; set; }
    public long? DiscoveryAllocatedBytes { get; set; }
    public double? TokenizerAndRenderingMilliseconds { get; set; }
    public long? TokenizerAndRenderingAllocatedBytes { get; set; }
    public List<double> EndToEndMilliseconds { get; } = [];
    public List<long> AllocatedBytes { get; } = [];
    public Distribution? Latency { get; set; }
    public double? RequestsPerSecond { get; set; }
    public double? QuestionsPerSecond { get; set; }
    public double? InstrumentedEndToEndMilliseconds { get; set; }
    public double? EncoderAndHeadMilliseconds { get; set; }
    public string EncoderAndHeadStatus { get; set; } = "unavailable_no_successful_forward";
    public double? EncoderMilliseconds { get; set; }
    public double? HeadMilliseconds { get; set; }
    public string EncoderHeadSplitStatus { get; set; } = "unavailable_no_successful_forward";
    public CudaTelemetrySnapshot? CudaInstrumentedForward { get; set; }
    public string CudaTelemetryStatus { get; set; } = "unavailable_cpu_backend";
    public long? WorkingSetBeforeBytes { get; set; }
    public long? WorkingSetAfterBytes { get; set; }
    public long? ProcessLifetimePeakWorkingSetBytes { get; set; }
    public CudaMemorySnapshot? CudaBefore { get; set; }
    public CudaMemorySnapshot? CudaAfter { get; set; }
    public List<ResourceObservationError> ResourceObservationErrors { get; } = [];
    public DecisionResponse? Response { get; set; }
}

internal sealed record ProfileSequence(string QuestionId, int TypeId, int TokenCount, string TokensSha256, int[] Markers,
    string[]? CandidateLabels = null, PromptSequenceDiagnostics? Diagnostics = null);
internal sealed record ResourceObservationError(string Phase, string Resource, string ErrorCode, string Error);
internal sealed record PreparedProfileSequence(string QuestionId, int TypeId, int[] Tokens, int[] Markers,
    string[] CandidateLabels, PromptSequenceDiagnostics Diagnostics)
{
    public ProfileSequence Describe(CancellationToken token) => new(QuestionId, TypeId, Tokens.Length,
        PerformanceProfiler.TokenHash(Tokens, token), Markers, CandidateLabels, Diagnostics);
}

/// <summary>Observes the real pipeline; never fabricates logits or a successful request.</summary>
internal sealed class ProfiledPipeline(IMarkerDecisionPipeline inner) : IMarkerDecisionPipeline
{
    public IMarkerDecisionPipeline Inner { get; } = inner;
    public PreparedProfileSequence[]? Expected { get; set; }
    public List<ProfileSequence> Actual { get; } = [];
    public double Milliseconds { get; private set; }

    public void Reset(PreparedProfileSequence[] expected)
    {
        Expected = expected;
        Actual.Clear();
        Milliseconds = 0;
    }

    public float[] Score(ReadOnlySpan<int> tokenIds, int typeId, ReadOnlySpan<int> markerPositions,
        CancellationToken cancellationToken = default)
    {
        if (Expected is null) return Inner.Score(tokenIds, typeId, markerPositions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (Actual.Count >= Expected.Length || Actual.Count >= 32)
            throw new InvalidOperationException("profile_rendering_mismatch: more pipeline calls than planned.");
        var expected = Expected[Actual.Count];
        Actual.Add(new(expected.QuestionId, typeId, tokenIds.Length, PerformanceProfiler.TokenHash(tokenIds, cancellationToken), markerPositions.ToArray()));
        if (typeId != expected.TypeId || !tokenIds.SequenceEqual(expected.Tokens) || !markerPositions.SequenceEqual(expected.Markers))
            throw new InvalidOperationException("profile_rendering_mismatch: runtime token IDs, marker positions or type differ from the shared prompt builder proposal.");
        var watch = Stopwatch.StartNew();
        try { return Inner.Score(tokenIds, typeId, markerPositions, cancellationToken); }
        finally { Milliseconds += watch.Elapsed.TotalMilliseconds; }
    }

    public void Dispose() => Inner.Dispose();
}

internal static class PerformanceProfiler
{
    // Register every selected input before model identity/load, CUDA setup or the cold request.
    internal static void PreparePlan(Options options, PerformanceProfile profile, CancellationToken token)
    {
        if (profile.Cases.Count != 0) throw new InvalidOperationException("Profile plan was already prepared.");
        foreach (var length in options.Lengths)
        {
            token.ThrowIfCancellationRequested();
            var repetitions = length switch { "short" => 4, "medium" => 20, "long" => 100, _ => throw new ArgumentException("Unknown length.") };
            var state = string.Join(' ', Enumerable.Repeat("The device is ready.", repetitions));
            foreach (var count in options.Questions)
            {
                token.ThrowIfCancellationRequested();
                var json = Benchmark.RequestJson(count, state);
                profile.Cases.Add(new ProfileCase { Id = $"{length}-{count}", LengthPreset = length,
                    StateRepetitions = repetitions, Questions = count, InputJson = json, InputSha256 = Hash(Encoding.UTF8.GetBytes(json)) });
                Console.WriteLine($"profile_plan: {profile.Cases.Count}/{profile.PlannedCases} {length}-{count}");
            }
        }
    }

    // Fixed maximum: 3 lengths x 3 question counts x (1 discovery + 5 warmup + 30 samples
    // + 1 instrumented request + at most 32 CPU split forwards), sharing the run deadline.
    internal static void Run(Options options, BenchmarkReport report, ModernBertModelPackage model,
        ModernBertDecisionEngine engine, ProfiledPipeline pipeline, CudaDevice? device, CancellationToken token)
    {
        var profile = report.Profile ?? throw new InvalidOperationException("Profile report is missing.");
        if (profile.Cases.Count != profile.PlannedCases) throw new InvalidOperationException("Profile plan is incomplete.");
        profile.Status = "running";
        try
        {
            for (var caseIndex = 0; caseIndex < profile.Cases.Count; caseIndex++)
            {
                token.ThrowIfCancellationRequested();
                var row = profile.Cases[caseIndex];
                report.Phase = $"profile_{row.Id}";
                Console.WriteLine($"{report.Phase}: case {caseIndex + 1}/{profile.Cases.Count}");
                MeasureCase(options, row, model, engine, pipeline, device, token);
            }
            profile.Status = profile.RejectedCases == 0 ? "complete" : "complete_with_rejections";
        }
        catch { profile.Status = "incomplete"; throw; }
        finally { pipeline.Expected = null; }
    }

    private static void MeasureCase(Options options, ProfileCase row, ModernBertModelPackage model,
        ModernBertDecisionEngine engine, ProfiledPipeline pipeline, CudaDevice? device, CancellationToken token)
    {
        row.Status = "running";
        row.RuntimeSequenceLimit = Math.Min(DecisionLimits.Default.MaxTokensPerQuestion, model.Encoder.Config.MaxTokens);
        row.RuntimePrefixTokenBudget = model.HeadMaxTokens;
        row.CudaTelemetryStatus = device is null ? "unavailable_cpu_backend" : "unavailable_no_successful_forward";
        try
        {
            row.Phase = "resource_before";
            ObserveResources(row, device, before: true);
            if (row.ResourceObservationErrors.Count != 0)
                throw new DecisionException("profile_resource_observation_failed", "Could not capture the resources before inference.");
            row.Phase = "tokenizer_and_rendering";
            token.ThrowIfCancellationRequested();
            var request = DecisionRequestParser.Parse(Encoding.UTF8.GetBytes(row.InputJson));
            row.LengthPolicy = request.LengthPolicy;
            var before = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            var sequences = RenderCurrent(request, model.Tokenizer, row.RuntimePrefixTokenBudget, row.RuntimeSequenceLimit, token);
            row.TokenizerAndRenderingMilliseconds = watch.Elapsed.TotalMilliseconds;
            row.TokenizerAndRenderingAllocatedBytes = GC.GetTotalAllocatedBytes(true) - before;
            row.RenderedSequences.AddRange(sequences.Select(sequence => sequence.Describe(token)));
            row.RenderedTotalTokens = sequences.Sum(sequence => sequence.Tokens.Length);
            row.OriginalTotalTokens = sequences.Sum(sequence => sequence.Diagnostics.OriginalTotalTokens);
            row.ProposedTruncation = sequences.Any(sequence => sequence.Diagnostics.WasTruncated);
            row.Phase = "discovery";
            pipeline.Reset(sequences);
            before = GC.GetTotalAllocatedBytes(true);
            watch.Restart();
            try { row.Response = Benchmark.Evaluate(engine, row.InputJson, token); }
            finally
            {
                row.DiscoveryWallMilliseconds = watch.Elapsed.TotalMilliseconds;
                row.DiscoveryAllocatedBytes = GC.GetTotalAllocatedBytes(true) - before;
                row.ActualSequences.AddRange(pipeline.Actual);
                if (pipeline.Actual.Count > 0)
                    row.RenderingVerification = "observed_pipeline_calls_request_incomplete";
                pipeline.Expected = null;
            }
            RequireComplete(row, sequences, pipeline);
            row.RenderingVerification = "exact_token_marker_type_match";
            row.Phase = "warmup";
            for (var iteration = 0; iteration < options.Warmup; iteration++)
            {
                token.ThrowIfCancellationRequested();
                Console.WriteLine($"profile_{row.Id}: warmup {iteration + 1}/{options.Warmup}");
                _ = Benchmark.Evaluate(engine, row.InputJson, token);
            }
            row.Phase = "end_to_end_samples";
            for (var iteration = 0; iteration < options.Samples; iteration++)
            {
                token.ThrowIfCancellationRequested();
                Console.WriteLine($"profile_{row.Id}: sample {iteration + 1}/{options.Samples}");
                before = GC.GetTotalAllocatedBytes(true);
                watch.Restart();
                row.Response = Benchmark.Evaluate(engine, row.InputJson, token);
                row.EndToEndMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
                row.AllocatedBytes.Add(GC.GetTotalAllocatedBytes(true) - before);
                Benchmark.Validate(row.Response, row.Questions);
            }
            row.Latency = Distribution.From(row.EndToEndMilliseconds.ToArray());
            row.RequestsPerSecond = options.Samples * 1000d / row.EndToEndMilliseconds.Sum();
            row.QuestionsPerSecond = row.RequestsPerSecond * row.Questions;
            token.ThrowIfCancellationRequested();
            row.Phase = "instrumented_forward";
            Console.WriteLine($"profile_{row.Id}: independent instrumented request");
            pipeline.Reset(sequences);
            if (device is not null) { device.ResetTelemetry(); device.ProfilingEnabled = true; }
            try
            {
                watch.Restart();
                row.Response = Benchmark.Evaluate(engine, row.InputJson, token);
                row.InstrumentedEndToEndMilliseconds = watch.Elapsed.TotalMilliseconds;
                RequireComplete(row, sequences, pipeline);
                row.EncoderAndHeadMilliseconds = pipeline.Milliseconds;
                row.EncoderAndHeadStatus = "measured_independent_instrumented_request";
                row.CudaInstrumentedForward = device?.GetTelemetry();
                if (device is not null) row.CudaTelemetryStatus = "measured_independent_instrumented_request";
            }
            finally { pipeline.Expected = null; if (device is not null) device.ProfilingEnabled = false; }
            if (pipeline.Inner is ModernBertDecisionPipeline cpu)
            {
                row.Phase = "cpu_split";
                double encoderMilliseconds = 0, headMilliseconds = 0;
                for (var index = 0; index < sequences.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    Console.WriteLine($"profile_{row.Id}: independent CPU split {index + 1}/{sequences.Length}");
                    var sequence = sequences[index];
                    watch.Restart();
                    var hidden = model.Encoder.Encode(sequence.Tokens, token);
                    encoderMilliseconds += watch.Elapsed.TotalMilliseconds;
                    watch.Restart();
                    _ = cpu.ScoreEncoded(hidden, sequence.TypeId, sequence.Markers, token);
                    headMilliseconds += watch.Elapsed.TotalMilliseconds;
                }
                row.EncoderMilliseconds = encoderMilliseconds;
                row.HeadMilliseconds = headMilliseconds;
                row.EncoderHeadSplitStatus = "measured_independent_pass_head_includes_hidden_copy";
            }
            else row.EncoderHeadSplitStatus = "unavailable_cuda_resident_pipeline_has_no_independent_head_timing_boundary";
            row.Status = "measured";
            row.Phase = "complete";
        }
        catch (DecisionException exception) when (row.Phase == "discovery" &&
            (exception.Code is "decision_token_budget_exceeded" or "decision_token_limit_exceeded"))
        {
            row.Status = "rejected";
            row.ErrorCode = exception.Code;
            row.Error = exception.Message;
            Console.WriteLine($"profile_{row.Id}: rejected {exception.Code}; rendered={row.RenderedTotalTokens}, actual_pipeline_calls={row.ActualSequences.Count}");
        }
        catch (Exception exception)
        {
            row.Status = token.IsCancellationRequested ? "cancelled" : "failed";
            row.ErrorCode = exception switch { DecisionException decision => decision.Code, CudaException cuda => cuda.Code,
                OperationCanceledException => "benchmark_cancelled_or_timeout", _ => exception.GetType().Name };
            row.Error = exception.Message;
            if (exception.Message.StartsWith("profile_rendering_mismatch:", StringComparison.Ordinal)) row.RenderingVerification = "mismatch";
            throw;
        }
        finally
        {
            pipeline.Expected = null;
            // Observation failures are evidence, never replacements for a pending inference exception.
            ObserveResources(row, device, before: false);
            if (row.Status == "measured" && row.ResourceObservationErrors.Count != 0)
            {
                row.Status = "failed";
                row.Phase = "resource_after";
                row.ErrorCode = "profile_resource_observation_failed";
                row.Error = "Inference completed, but resource observations are incomplete.";
            }
        }
        // This point is reached only when no inference exception is being rethrown.
        if (row.ResourceObservationErrors.Count != 0)
            throw new DecisionException("profile_resource_observation_failed", "Resource observations are incomplete; see the row's separate observation errors.");
    }

    private static void ObserveResources(ProfileCase row, CudaDevice? device, bool before)
    {
        var phase = before ? "resource_before" : "resource_after";
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            if (before) row.WorkingSetBeforeBytes = process.WorkingSet64;
            else
            {
                row.WorkingSetAfterBytes = process.WorkingSet64;
                row.ProcessLifetimePeakWorkingSetBytes = process.PeakWorkingSet64;
            }
        }
        catch (Exception exception)
        {
            row.ResourceObservationErrors.Add(new(phase, "process_working_set", exception.GetType().Name, exception.Message));
        }
        if (device is null) return;
        try
        {
            var snapshot = device.GetMemorySnapshot();
            if (before) row.CudaBefore = snapshot; else row.CudaAfter = snapshot;
        }
        catch (Exception exception)
        {
            row.ResourceObservationErrors.Add(new(phase, "cuda_memory", exception is CudaException cuda ? cuda.Code : exception.GetType().Name, exception.Message));
        }
    }

    private static void RequireComplete(ProfileCase row, PreparedProfileSequence[] sequences, ProfiledPipeline pipeline)
    {
        var response = row.Response ?? throw new InvalidOperationException("Successful request has no response.");
        Benchmark.Validate(response, row.Questions);
        if (pipeline.Actual.Count != sequences.Length || response.Usage!.TokenCount != row.RenderedTotalTokens)
            throw new InvalidOperationException("profile_rendering_mismatch: actual pipeline calls or reported token count differ from the shared prompt builder proposal.");
        if (row.LengthPolicy == PromptLengthPolicy.Strict && row.ProposedTruncation)
            throw new InvalidOperationException("profile_rendering_mismatch: strict runtime accepted a prompt requiring token truncation.");
    }

    // The compatible proposal exposes exact token loss without changing the real request's
    // policy. A strict request still reaches the runtime and records its actual rejection.
    private static PreparedProfileSequence[] RenderCurrent(DecisionRequest request, TokenizerJson tokenizer,
        int prefixTokenBudget, int totalTokenBudget, CancellationToken token)
    {
        if (request.Questions.Count is < 1 or > 32) throw new ArgumentException("Profile request must contain 1..32 questions.");
        var result = new List<PreparedProfileSequence>(request.Questions.Count);
        foreach (var (id, question) in request.Questions)
        {
            token.ThrowIfCancellationRequested();
            var sequence = PromptSequenceBuilder.Build(tokenizer, request.State, question, new PromptSequenceOptions
            {
                PrefixTokenBudget = prefixTokenBudget,
                TotalTokenBudget = totalTokenBudget,
                LengthPolicy = PromptLengthPolicy.LayaCompatible,
            }, token);
            if (sequence.CandidateLabels.Length != 2) throw new ArgumentException("Profile inputs require exactly two candidates.");
            result.Add(new(id, sequence.TypeId, sequence.TokenIds, sequence.MarkerPositions,
                sequence.CandidateLabels, sequence.Diagnostics));
        }
        return result.ToArray();
    }

    internal static string TokenHash(ReadOnlySpan<int> tokens, CancellationToken token)
    {
        // Explicit little-endian encoding keeps hashes stable across host byte order.
        if (tokens.Length > 8192) throw new InvalidOperationException("Profile diagnostic token cap exceeded.");
        var bytes = new byte[checked(tokens.Length * sizeof(int))];
        for (var index = 0; index < tokens.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int), sizeof(int)), tokens[index]);
        }
        return Hash(bytes);
    }
    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
