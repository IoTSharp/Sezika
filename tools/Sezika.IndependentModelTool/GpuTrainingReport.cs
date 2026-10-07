using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.IndependentModelTool;

/// <summary>Versioned GPU evidence. The complete existing v2 training contract remains nested and unchanged.</summary>
public sealed record GpuTrainingReport
{
    public const string ContractName = "cuda-marker-head-workflow-v1";
    public required int SchemaVersion { get; init; }
    public required string Contract { get; init; }
    public required WorkflowTrainingReport Training { get; init; }
    public required GpuTrainingExecution Execution { get; init; }

    public static GpuTrainingReport ReadBounded(string path, string expectedSha256, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IndependentTrainingWorkflow.IsHash(expectedSha256))
            throw new DecisionException("training_report_hash_invalid", "Expected GPU report SHA-256 is required.");
        var bytes = IndependentTrainingWorkflow.ReadSmallFile(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IndependentTrainingWorkflow.HashEqual(Convert.ToHexString(SHA256.HashData(bytes)), expectedSha256))
            throw new DecisionException("training_report_hash_invalid", "GPU report SHA-256 does not match.");
        return ParseBounded(bytes, cancellationToken);
    }

    public static GpuTrainingReport ParseBounded(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            BoundedTrainingJson.Check(bytes, 1_048_576, 16, 4096, cancellationToken, Invalid);
            var report = JsonSerializer.Deserialize(bytes, GpuTrainingJsonContext.Default.GpuTrainingReport) ?? throw Invalid("GPU report is null.");
            report.Validate(cancellationToken);
            return report;
        }
        catch (JsonException) { throw Invalid("GPU report fields, types or version are invalid."); }
    }

    public byte[] SerializeBounded(CancellationToken cancellationToken = default)
    {
        Validate(cancellationToken);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, GpuTrainingJsonContext.Default.GpuTrainingReport);
        _ = ParseBounded(bytes, cancellationToken);
        return bytes;
    }

    /// <summary>Formal original-data resume/evaluation rejects engineering fixtures before binding model identity.</summary>
    public MarkerFeatureIdentity BindApprovedIdentity(MarkerFeatureIdentity actual, CancellationToken cancellationToken = default)
    {
        Validate(cancellationToken);
        if (Execution.DataMode != GpuTrainingExecution.ApprovedDataMode)
            throw new DecisionException("training_resume_backend_mismatch", "Engineering fixtures cannot resume or evaluate approved original data.");
        return Training.BindIdentity(actual, cancellationToken);
    }

    public void RequireExecutionCompatible(GpuTrainingExecution current, CancellationToken cancellationToken = default)
    {
        Validate(cancellationToken);
        Execution.RequireCompatible(current, cancellationToken);
    }

    private void Validate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SchemaVersion != 3 || Contract != ContractName || Training is null || Execution is null)
            throw Invalid("GPU report requires schema 3, complete training and execution identities.");
        // This is the same full validation used by the v2 production reader, including denominators and resume fields.
        Training.Validate(cancellationToken);
        Execution.Validate(cancellationToken);
        if (Training.UsesRealIndependentEncoder != (Execution.DataMode == GpuTrainingExecution.ApprovedDataMode))
            throw Invalid("Feature provenance and GPU data mode differ.");
    }

    internal static DecisionException Invalid(string message) => new("training_gpu_report_invalid", message);
}

public sealed record GpuTrainingExecution
{
    public const string ApprovedDataMode = "approved_original";
    public const string FixtureDataMode = "synthetic_fixture";
    public required string DataMode { get; init; }
    public required string FeatureBackend { get; init; }
    public required string TrainingBackend { get; init; }
    public required string NumericContract { get; init; }
    public required string OrderContract { get; init; }
    public required string DevelopmentBackend { get; init; }
    public required string KernelSourceSha256 { get; init; }
    public required string KernelManifestSha256 { get; init; }
    public required string Compiler { get; init; }
    public required string CompilerVersion { get; init; }
    public required string Target { get; init; }
    public required string PtxVersion { get; init; }
    public required GpuTrainingKernelIdentity[] Kernels { get; init; }
    public required GpuTrainingDeviceIdentity Device { get; init; }

    internal void Validate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DataMode is not (ApprovedDataMode or FixtureDataMode) ||
            FeatureBackend != (DataMode == ApprovedDataMode ? "cuda-modernbert-fp32-v1" : "synthetic-marker-features-v1") ||
            TrainingBackend != "cuda-independent-linear-marker-head-sgd-fp32" ||
            NumericContract != "cuda-linear-ce-gradient-sgd-fp32-v1" ||
            OrderContract != "input-order-xorshift32-fixed-cycle-v1" || DevelopmentBackend != "scalar-linear-marker-head-fp64" ||
            !IndependentTrainingWorkflow.IsHash(KernelSourceSha256) || !IndependentTrainingWorkflow.IsHash(KernelManifestSha256) ||
            Compiler != "ILGPU" || CompilerVersion != "1.5.3" || Target != "sm_70" || PtxVersion != "7.0" ||
            Kernels is null || Kernels.Length != 17 || Device is null)
            throw GpuTrainingReport.Invalid("GPU execution backend, numeric, ordering or artifact contract is invalid.");
        Device.Validate();
        var clock = Stopwatch.StartNew();
        for (var index = 0; index < 17; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw GpuTrainingReport.Invalid("GPU identity validation deadline expired.");
            var kernel = Kernels[index];
            if (kernel is null || kernel.Name != KernelNames[index] || kernel.Entry != "Kernel_" + KernelNames[index] ||
                kernel.ParameterCount != ParameterCounts[index] || kernel.BlockSize != 256 || !IndependentTrainingWorkflow.IsHash(kernel.Sha256))
                throw GpuTrainingReport.Invalid("GPU kernel identity is missing, duplicated, reordered or has an incompatible ABI.");
        }
    }

    /// <summary>Exact execution compatibility; device enumeration position is diagnostic, not physical identity.</summary>
    public void RequireCompatible(GpuTrainingExecution current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        Validate(cancellationToken); current.Validate(cancellationToken);
        if (DataMode != current.DataMode || FeatureBackend != current.FeatureBackend || TrainingBackend != current.TrainingBackend ||
            NumericContract != current.NumericContract || OrderContract != current.OrderContract || DevelopmentBackend != current.DevelopmentBackend ||
            !IndependentTrainingWorkflow.HashEqual(KernelSourceSha256, current.KernelSourceSha256) ||
            !IndependentTrainingWorkflow.HashEqual(KernelManifestSha256, current.KernelManifestSha256) ||
            Compiler != current.Compiler || CompilerVersion != current.CompilerVersion || Target != current.Target || PtxVersion != current.PtxVersion ||
            !Device.Matches(current.Device)) throw Mismatch();
        var clock = Stopwatch.StartNew();
        for (var index = 0; index < 17; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw GpuTrainingReport.Invalid("GPU compatibility deadline expired.");
            if (!IndependentTrainingWorkflow.HashEqual(Kernels[index].Sha256, current.Kernels[index].Sha256)) throw Mismatch();
        }
    }

    private static DecisionException Mismatch() => new("training_resume_backend_mismatch", "GPU feature, training, numeric, kernel or physical device/driver identity differs.");
    private static readonly string[] KernelNames = ["VectorAdd", "Linear", "Embedding", "Norm", "SplitQkv", "Rope", "AttentionScores", "Softmax",
        "AttentionContext", "Add", "Activate", "GatedGelu", "RowBias", "TypeEmbeddingAdd", "MarkerCrossEntropy", "MarkerGradientSgd", "MarkerUpdateStatus"];
    private static readonly int[] ParameterCounts = [4, 7, 5, 8, 6, 6, 7, 3, 6, 3, 4, 4, 4, 5, 6, 9, 5];
}

public sealed record GpuTrainingKernelIdentity
{
    public required string Name { get; init; }
    public required string Entry { get; init; }
    public required int BlockSize { get; init; }
    public required int ParameterCount { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record GpuTrainingDeviceIdentity
{
    public required string Uuid { get; init; }
    public required string Name { get; init; }
    public required int DriverVersion { get; init; }
    public required int ComputeCapabilityMajor { get; init; }
    public required int ComputeCapabilityMinor { get; init; }
    public required ulong TotalMemoryBytes { get; init; }
    public required int DeviceIndex { get; init; }

    internal void Validate()
    {
        if (Uuid is not { Length: 32 } || !Uuid.All(char.IsAsciiHexDigit) || Uuid.All(value => value == '0') ||
            string.IsNullOrWhiteSpace(Name) || Name.Length > 256 || DriverVersion is < 1 or > 1_000_000 ||
            ComputeCapabilityMajor is < 7 or > 99 || ComputeCapabilityMinor is < 0 or > 9 ||
            TotalMemoryBytes is 0 or > 1_099_511_627_776UL || DeviceIndex is < 0 or > 15)
            throw GpuTrainingReport.Invalid("GPU physical device identity is invalid or missing its UUID.");
    }

    internal bool Matches(GpuTrainingDeviceIdentity other) => Uuid.Equals(other.Uuid, StringComparison.OrdinalIgnoreCase) && Name == other.Name &&
        DriverVersion == other.DriverVersion && ComputeCapabilityMajor == other.ComputeCapabilityMajor &&
        ComputeCapabilityMinor == other.ComputeCapabilityMinor && TotalMemoryBytes == other.TotalMemoryBytes;
}

/// <summary>Finite duplicate-property preflight shared by the new report and admission protocols.</summary>
internal static class BoundedTrainingJson
{
    internal static void Check(ReadOnlySpan<byte> bytes, int maxBytes, int maxDepth, int maxTokens,
        CancellationToken cancellationToken, Func<string, DecisionException> invalid)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length <= 0 || bytes.Length > maxBytes) throw invalid("JSON byte limit exceeded.");
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = maxDepth });
        var scopes = new Stack<HashSet<string>>();
        var count = 0;
        var clock = Stopwatch.StartNew();
        // The byte, token, depth and wall-clock bounds are independent; cancellation is checked for every token.
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > maxTokens || clock.Elapsed > TimeSpan.FromSeconds(10)) throw invalid("JSON token or wall-clock limit exceeded.");
            if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) scopes.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !scopes.Peek().Add(reader.GetString()!))
                throw invalid("JSON contains duplicate properties.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true)]
[JsonSerializable(typeof(GpuTrainingReport))]
internal partial class GpuTrainingJsonContext : JsonSerializerContext;
