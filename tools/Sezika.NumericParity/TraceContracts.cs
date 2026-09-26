using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed record TraceReference
{
    public required string SchemaVersion { get; init; }
    public required JsonElement Provenance { get; init; }
    public required List<TraceReferenceCase> Cases { get; init; }
}

internal sealed record TraceReferenceCase
{
    public required string Id { get; init; }
    public required string Primitive { get; init; }
    public required string Status { get; init; }
    public required JsonElement Input { get; init; }
    public int[]? TokenIds { get; init; }
    public int[]? MarkerPositions { get; init; }
    public string[]? CandidateLabels { get; init; }
    public double[]? RawLogits { get; init; }
    public double[]? Probabilities { get; init; }
    public JsonElement Prediction { get; init; }
}

internal sealed record NumericTolerance(double Absolute, double Relative)
{
    public bool Accepts(double expected, double actual) => double.IsFinite(expected) && double.IsFinite(actual) &&
        Math.Abs(expected - actual) <= Absolute + Relative * Math.Abs(expected);
}

internal sealed record TraceTolerances(NumericTolerance RawLogits, NumericTolerance Probabilities,
    NumericTolerance Score, double NearTieLogitMargin);
internal sealed record TensorDifference(string Name, int Elements, double MaxAbsoluteError,
    double RootMeanSquaredError, double MaxRelativeError, int WorstElement, string ScalarSha256, string ActualSha256);
internal sealed record OutputDifference(bool Passed, bool NearTie, double ReferenceMargin,
    double MaxLogitError, double MaxProbabilityError, List<string> Issues);
internal sealed record TraceBackendReport(string Backend, string Status, string? Error,
    double[]? Logits, double[]? Probabilities, OutputDifference? OracleComparison,
    List<TensorDifference> LayerDifferences, List<string> MissingTraces, long RetainedScalarTraceBytes,
    double ElapsedMilliseconds);
internal sealed record TraceCaseReport(string Id, string Primitive, int TokenCount,
    int[] TokenIds, int[] MarkerPositions, string[] CandidateLabels, List<TraceBackendReport> Backends);
internal sealed record TraceReport
{
    public int SchemaVersion { get; init; } = 1;
    public required DateTimeOffset StartedUtc { get; init; }
    public required double ElapsedMilliseconds { get; init; }
    public required string ReferenceSha256 { get; init; }
    public required string ContractSha256 { get; init; }
    public required JsonElement Provenance { get; init; }
    public required TraceTolerances Tolerances { get; init; }
    public required string Framework { get; init; }
    public required string RuntimeIdentifier { get; init; }
    public required Dictionary<string, string> BinarySha256 { get; init; }
    public required string[] SelectedCaseIds { get; init; }
    public required int ReferenceCases { get; init; }
    public required int NearTieCases { get; init; }
    public required bool SelectedDiagnosticsPassed { get; init; }
    public bool FullS306GatePassed { get; init; }
    public string NearTieAcceptance => NearTieCases == 0 ? "not_measured" : "selected_cases_only";
    public string LengthPolicy { get; init; } = "laya_compatible";
    public long MaxRetainedTraceBytes { get; init; } = TraceCollector.MaxBytes;
    public required List<TraceCaseReport> Cases { get; init; }
    public string Scope { get; init; } = "Layer differences compare Sezika scalar with SIMD/CUDA for internal diagnosis; no independent upstream layer tensors or frozen layer acceptance thresholds are present. Output comparisons use the frozen oracle contract. Selected cases do not establish full length/language/primitive coverage, near-tie acceptance when none were measured, Native AOT, quality, calibration or performance.";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TraceReference))]
[JsonSerializable(typeof(TraceTolerances))]
[JsonSerializable(typeof(TraceReport))]
internal partial class TraceJsonContext : JsonSerializerContext;
