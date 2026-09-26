using System.Text.Json.Serialization;
using Sezika;
using Sezika.Cuda;

namespace Sezika.Benchmarks;

internal sealed class BenchmarkReport
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "running";
    public string Phase { get; set; } = "arguments";
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public string Backend { get; set; } = "";
    public string Precision { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Rid { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string Cpu { get; set; } = "";
    public string EnvironmentLabel { get; set; } = "";
    public bool NativeAot { get; set; }
    public bool ManagedHostDetected { get; set; }
    public bool IsDynamicCodeSupported { get; set; }
    public bool IsDynamicCodeCompiled { get; set; }
    public int LogicalProcessors { get; set; }
    public int InferenceThreads { get; init; } = 1;
    public int VectorFloatWidth { get; set; }
    public string ModelRevision { get; init; } = ModernBertModelLoader.PinnedRevision;
    public string WeightsSha256 { get; init; } = ModernBertModelLoader.PinnedWeightsSha256;
    public string TokenizerSha256 { get; init; } = ModernBertModelLoader.PinnedTokenizerSha256;
    public string? ManifestSha256 { get; set; }
    public string? ExecutableSha256 { get; set; }
    public List<CodeArtifactIdentity> CodeArtifacts { get; } = [];
    public string[] Arguments { get; set; } = [];
    public int Samples { get; set; }
    public int Warmup { get; set; }
    public int TimeoutSeconds { get; set; }
    public int RequestTokenBudget { get; set; }
    public double RequestDeadlineSeconds { get; set; }
    public List<LoadMeasurement> Loads { get; } = [];
    public List<RequestMeasurement> Requests { get; } = [];
    public PerformanceProfile? Profile { get; set; }
    public List<string> Diagnostics { get; } = [];
    public CudaDeviceInfo? Gpu { get; set; }
    public CudaTelemetrySnapshot? CudaLoad { get; set; }
    public CudaTelemetrySnapshot? CudaProfiledForward { get; set; }
    public double? ProfiledForwardWallMilliseconds { get; set; }
    public CudaMemorySnapshot? CudaBeforeUnload { get; set; }
    public CudaMemorySnapshot? CudaAfterUnload { get; set; }
    public AlignmentMeasurement? Alignment { get; set; }
    public long QuantizedEncoderBytes { get; set; }
    public long QuantizedHeadBytes { get; set; }
    public long ManagedBytesBefore { get; set; }
    public long ManagedBytesAfterCollection { get; set; }
    public bool ModelObjectsCollected { get; set; }
    public string CollectionStatus { get; set; } = "not_measured";
    public List<CleanupMeasurement> Cleanup { get; } = [];
    public long? PeakWorkingSetBytes { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public string TimingScope { get; init; } = "E2E: parse request JSON + tokenize + sequential per-question encoder/head + typed response JSON; model load excluded. Cold = first request in this process, OS/driver disk caches not flushed. CUDA events measured in a separate instrumented forward.";
    public string ResourceScope { get; init; } = "PeakWorkingSetBytes is OS process lifetime high-water RSS. CUDA owned peak counts successful driver allocations since the most recent ResetTelemetry, which resets the peak to currently owned bytes; it is not a context lifetime peak and excludes driver/context overhead. Free/total memory is device-wide. FP32 source tensors retained in W8A32 mode. Managed bytes after full collection do not imply immediate OS RSS return.";
    public string QualityScope { get; init; } = "Numerical smoke on fixed inputs only; no language accuracy or calibrated quality claim. Percentiles use nearest rank; small sample counts are exploratory and p95/p99 may coincide.";
}

internal sealed record LoadMeasurement(int Cycle, double ModelLoadMilliseconds, double BackendLoadMilliseconds,
    double FirstRequestMilliseconds, double TotalToFirstResponseMilliseconds);
internal sealed record CodeArtifactIdentity(string Role, string Path, string Sha256);
internal sealed record CleanupMeasurement(int Cycle, string Status, int? ActiveWorkspaces, long? OutstandingWorkspaceBytes,
    CudaMemorySnapshot? CudaAfterRelease, List<string> Errors);
internal sealed record RequestMeasurement(int Questions, string InputJson, string InputSha256, int TokenCount,
    int CandidatesPerQuestion, double[] Milliseconds, long[] AllocatedBytes, Distribution Latency,
    double RequestsPerSecond, double QuestionsPerSecond, DecisionResponse Response);
internal sealed record Distribution(int Count, double Minimum, double P50, double P95, double P99, double Maximum)
{
    public static Distribution From(double[] values)
    {
        if (values.Length == 0 || values.Any(value => !double.IsFinite(value) || value < 0))
            throw new ArgumentException("Samples must be non-empty, finite and nonnegative.");
        var sorted = values.Order().ToArray();
        double Rank(double percentile) => sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new(sorted.Length, sorted[0], Rank(0.50), Rank(0.95), Rank(0.99), sorted[^1]);
    }
}
internal sealed record AlignmentMeasurement(int[] Tokens, float[] ReferenceLogits, float[] ActualLogits,
    float EncoderMaxAbsoluteError, float LogitsMaxAbsoluteError, float ProbabilityMaxAbsoluteError,
    float? HeadOnlyMaxAbsoluteError, float EncoderTolerance, float LogitsTolerance, float ProbabilityTolerance);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(BenchmarkReport))]
[JsonSerializable(typeof(string))]
internal partial class ReportJsonContext : JsonSerializerContext;
