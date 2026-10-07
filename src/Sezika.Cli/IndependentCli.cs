using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.Cli;

internal enum CliModelFormat { LegacyLaya, IndependentV1 }

// Only the checks assembly can supply a tiny C# runtime. The product entry always
// calls the fixed production loader; no command option selects a fixture loader.
internal delegate IndependentDecisionV1Runtime IndependentRuntimeLoader(string packageDirectory, string headPath,
    MarkerFeatureIdentity identity, string featureSha256, string headSha256,
    IndependentDecisionRuntimeOptions options, CancellationToken cancellationToken);

internal static class IndependentCli
{
    internal static IndependentDecisionV1Runtime Load(CliOptions options, IndependentHeadBinding binding,
        IndependentRuntimeLoader? loader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtimeOptions = new IndependentDecisionRuntimeOptions
        {
            MaxTokens = options.MaxTokens,
            Deadline = TimeSpan.FromSeconds(options.DeadlineSeconds),
        };
        return loader is null
            ? IndependentDecisionV1Runtime.Load(options.RequireModelDirectory(), Path.GetFullPath(options.HeadPath!),
                binding.Identity, binding.FeatureSha256, binding.HeadSha256, runtimeOptions, cancellationToken)
            : loader(options.RequireModelDirectory(), Path.GetFullPath(options.HeadPath!), binding.Identity,
                binding.FeatureSha256, binding.HeadSha256, runtimeOptions, cancellationToken);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record IndependentHeadBinding
{
    public required int SchemaVersion { get; init; }
    public required MarkerFeatureIdentity Identity { get; init; }
    public required string FeatureSha256 { get; init; }
    public required string HeadSha256 { get; init; }

    internal static async Task<IndependentHeadBinding> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read,
            FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is < 1 or > 65_536)
            throw new DecisionException("cli_head_binding_invalid", "Head binding must contain 1 to 65536 bytes.");
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var version = json.RootElement.GetProperty("schema_version").GetInt32();
            if (version == 1)
                StrictFields(json.RootElement, ["schema_version", "identity", "feature_sha256", "head_sha256"]);
            else if (version == 2)
                StrictFields(json.RootElement, ["schema_version", "status", "identity", "records_sha256", "feature_sha256", "head_sha256",
                    "checkpoint_sha256", "seed", "learning_rate", "step_semantics", "initial_steps", "max_steps", "completed_steps",
                    "last_loss", "train_count", "development_count", "uses_real_independent_encoder", "represents_quality_acceptance",
                    "optimizer", "feature_source", "training_timeout_seconds", "elapsed_milliseconds", "resume", "development_groups"]);
            else throw new DecisionException("cli_head_binding_invalid", "Head binding supports schema 1 or a schema 2 training report.");
            StrictFields(json.RootElement.GetProperty("identity"), ["model_id", "encoder_sha256", "tokenizer_sha256",
                "data_manifest_sha256", "hidden_size", "protocol", "length_policy"]);
            IndependentHeadBinding binding;
            if (version == 1)
                binding = JsonSerializer.Deserialize(bytes, CliJsonContext.Default.IndependentHeadBinding)
                    ?? throw new DecisionException("cli_head_binding_invalid", "Head binding is null.");
            else
            {
                var report = JsonSerializer.Deserialize(bytes, CliJsonContext.Default.TrainingHeadBindingReport)
                    ?? throw new DecisionException("cli_head_binding_invalid", "Training report is null.");
                var resume = json.RootElement.GetProperty("resume");
                if (resume.ValueKind != JsonValueKind.Null)
                    StrictFields(resume, ["checkpoint_sha256", "report_sha256", "initial_steps"]);
                var groups = json.RootElement.GetProperty("development_groups");
                if (groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() > 6)
                    throw new DecisionException("cli_head_binding_invalid", "Training report development groups exceed the bound.");
                foreach (var group in groups.EnumerateArray())
                    StrictFields(group, ["language", "question_type", "count", "correct", "accuracy"]);
                report.Validate();
                binding = new IndependentHeadBinding { SchemaVersion = 1, Identity = report.Identity,
                    FeatureSha256 = report.FeatureSha256, HeadSha256 = report.HeadSha256 };
            }
            var identity = binding.Identity;
            if (binding.SchemaVersion != 1 || identity is null ||
                string.IsNullOrWhiteSpace(identity.ModelId) || identity.ModelId.Length > 128 ||
                !identity.ModelId.StartsWith("sezika/", StringComparison.Ordinal) || identity.HiddenSize is < 1 or > 2048 ||
                identity.Protocol != "decision-v1" || identity.LengthPolicy != "strict" ||
                !IsHash(identity.EncoderSha256) || !IsHash(identity.TokenizerSha256) || !IsHash(identity.DataManifestSha256) ||
                !IsHash(binding.FeatureSha256) || !IsHash(binding.HeadSha256))
                throw new DecisionException("cli_head_binding_invalid", "Head binding identity, protocol, schema or hashes are invalid.");
            return binding;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new DecisionException("cli_head_binding_invalid", "Head binding JSON must contain the complete versioned identity and hashes.", exception);
        }
    }

    internal static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static void StrictFields(JsonElement value, string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new DecisionException("cli_head_binding_invalid", "Head binding sections must be objects.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (++count > names.Length || !seen.Add(property.Name) || !names.Contains(property.Name, StringComparer.Ordinal))
                throw new DecisionException("cli_head_binding_invalid", "Head binding contains duplicate or unknown fields.");
        }
        if (count != names.Length)
            throw new DecisionException("cli_head_binding_invalid", "Head binding is missing required fields.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record TrainingHeadBindingReport
{
    public required int SchemaVersion { get; init; }
    public required string Status { get; init; }
    public required MarkerFeatureIdentity Identity { get; init; }
    public required string RecordsSha256 { get; init; }
    public required string FeatureSha256 { get; init; }
    public required string HeadSha256 { get; init; }
    public required string CheckpointSha256 { get; init; }
    public required int Seed { get; init; }
    public required float LearningRate { get; init; }
    public required string StepSemantics { get; init; }
    public required int InitialSteps { get; init; }
    public required int MaxSteps { get; init; }
    public required int CompletedSteps { get; init; }
    public required double LastLoss { get; init; }
    public required int TrainCount { get; init; }
    public required int DevelopmentCount { get; init; }
    public required bool UsesRealIndependentEncoder { get; init; }
    public required bool RepresentsQualityAcceptance { get; init; }
    public required string Optimizer { get; init; }
    public required string FeatureSource { get; init; }
    public required double TrainingTimeoutSeconds { get; init; }
    public required double ElapsedMilliseconds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required TrainingBindingResume? Resume { get; init; }
    public required TrainingBindingGroup[] DevelopmentGroups { get; init; }

    internal void Validate()
    {
        if (SchemaVersion != 2 || RepresentsQualityAcceptance || StepSemantics != "total_completed_steps" || Optimizer != "sgd_v1_no_momentum" ||
            (UsesRealIndependentEncoder ? Status != "development_smoke_only" || FeatureSource != "original_encoder"
                : Status != "fixture_contract_only" || FeatureSource != "synthetic_fixture") ||
            !IndependentHeadBinding.IsHash(RecordsSha256) || !IndependentHeadBinding.IsHash(CheckpointSha256) ||
            !float.IsFinite(LearningRate) || LearningRate is <= 0 or > 1 || InitialSteps is < 0 or > 10_000 ||
            MaxSteps is < 1 or > 10_000 || CompletedSteps < InitialSteps || CompletedSteps != MaxSteps ||
            !double.IsFinite(LastLoss) || LastLoss < 0 || TrainCount is < 1 or > 31 || DevelopmentCount is < 1 or > 31 || TrainCount + DevelopmentCount > 32 ||
            !double.IsFinite(TrainingTimeoutSeconds) || TrainingTimeoutSeconds is <= 0 or > 600 || !double.IsFinite(ElapsedMilliseconds) || ElapsedMilliseconds < 0 ||
            DevelopmentGroups is null || DevelopmentGroups.Length is < 1 or > 6 || (Resume is null && InitialSteps != 0) ||
            (Resume is not null && (!IndependentHeadBinding.IsHash(Resume.CheckpointSha256) || !IndependentHeadBinding.IsHash(Resume.ReportSha256) || Resume.InitialSteps != InitialSteps)))
            throw new DecisionException("cli_head_binding_invalid", "Training report metadata or provenance is invalid.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in DevelopmentGroups)
            if (group is null || group.Language is not ("zh" or "en") || group.QuestionType is not ("choice" or "score" or "boolean") ||
                !seen.Add(group.Language + "/" + group.QuestionType) || group.Count is < 1 or > 32 || group.Correct < 0 || group.Correct > group.Count ||
                !double.IsFinite(group.Accuracy) || group.Accuracy is < 0 or > 1 || Math.Abs(group.Accuracy - (double)group.Correct / group.Count) > 1e-12)
                throw new DecisionException("cli_head_binding_invalid", "Training report development groups are invalid.");
        if (DevelopmentGroups.Sum(group => group.Count) != DevelopmentCount)
            throw new DecisionException("cli_head_binding_invalid", "Training report development denominator differs from its groups.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record TrainingBindingResume(string CheckpointSha256, string ReportSha256, int InitialSteps);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record TrainingBindingGroup(string Language, string QuestionType, int Count, int Correct, double Accuracy);

internal sealed record IndependentInspectReport(string PackageDirectory, string HeadPath, string ModelId,
    string EncoderSha256, string TokenizerSha256, string HeadSha256, string FeatureSha256, string DataManifestSha256)
{
    public string ModelFormat => "independent-v1";
    public string Protocol => "decision-v1";
    public string CalibrationStatus => "uncalibrated";
    public bool RuntimeLoadVerified => true;
    public bool QualityVerified => false;
}
