using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.IndependentModelTool;

public enum TrainingFeatureSource { OriginalEncoder, SyntheticFixture }

public sealed record TrainingOutputPaths(string Checkpoint, string Head, string Report);
public sealed record TrainingResumePaths(string Checkpoint, string CheckpointSha256, string Report, string ReportSha256);
public sealed record TrainingWorkflowResult(MarkerTrainingState State, WorkflowTrainingReport Report, string ReportSha256);

/// <summary>File workflow around the existing bounded SGD trainer; it does not admit data or establish quality.</summary>
public static class IndependentTrainingWorkflow
{
    public static TrainingWorkflowResult Run(MarkerFeatureSet train, IReadOnlyList<MarkerFeatureExample> development,
        string recordsSha256, MarkerTrainingOptions options, TrainingOutputPaths output,
        TrainingFeatureSource source, TrainingResumePaths? resume = null,
        Action<MarkerTrainingProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var token = deadline.Token;
        token.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(train);
        ArgumentNullException.ThrowIfNull(development);
        if (!IsHash(recordsSha256) || train.Examples is null || development.Count is < 1 or > 32 ||
            train.Examples.Count + development.Count > 32 || !Enum.IsDefined(source))
            throw new DecisionException("training_workflow_input_invalid", "Records identity, feature source or split counts are invalid.");
        ValidateOutputPaths(output, resume is null ? [] : [resume.Checkpoint, resume.Report]);
        MarkerTrainingState? restored = null;
        if (resume is not null)
        {
            var report = WorkflowTrainingReport.ReadBounded(resume.Report, resume.ReportSha256, token);
            restored = MarkerTrainingCheckpoint.Load(resume.Checkpoint, train, resume.CheckpointSha256, token);
            if (!HashEqual(report.CheckpointSha256, resume.CheckpointSha256) ||
                !HashEqual(report.RecordsSha256, recordsSha256) || !HashEqual(report.FeatureSha256, restored.FeatureSha256) ||
                !report.Identity.Matches(restored.Identity) || report.Seed != options.Seed ||
                report.LearningRate != options.LearningRate || report.CompletedSteps != restored.CompletedSteps ||
                report.LastLoss != restored.LastLoss || report.TrainCount != train.Examples.Count ||
                report.DevelopmentCount != development.Count ||
                report.UsesRealIndependentEncoder != (source == TrainingFeatureSource.OriginalEncoder))
                throw new DecisionException("training_resume_mismatch", "Resume report, checkpoint, full records or training configuration differ.");
        }
        // MaxSteps remains a cumulative endpoint. The trainer replays its deterministic
        // order from CompletedSteps, rather than starting a new epoch on restoration.
        var state = IndependentMarkerHeadTrainer.Train(train, options, restored, progress, token);
        var groups = new List<WorkflowDevelopmentGroup>();
        var seen = new HashSet<string>(train.Examples.Select(example => example.RecordId), StringComparer.Ordinal);
        foreach (var example in development)
        {
            token.ThrowIfCancellationRequested();
            if (example is null || !seen.Add(example.RecordId) || example.Language is not ("zh" or "en"))
                throw new DecisionException("training_workflow_input_invalid", "Development records must be unique, separate from train and language-tagged.");
            var gradient = state.Head.Gradient(example, token.ThrowIfCancellationRequested);
            var predicted = 0;
            for (var index = 1; index < gradient.Logits.Length; index++)
                if (gradient.Logits[index] > gradient.Logits[predicted]) predicted = index;
            var type = example.Type switch
            {
                MarkerQuestionType.Choice => "choice", MarkerQuestionType.Score => "score",
                MarkerQuestionType.Boolean => "boolean", _ => throw new DecisionException("training_shape_invalid", "Unknown marker question type.")
            };
            var groupIndex = groups.FindIndex(group => group.Language == example.Language && group.QuestionType == type);
            if (groupIndex < 0)
                groups.Add(new WorkflowDevelopmentGroup { Language = example.Language, QuestionType = type,
                    Count = 1, Correct = predicted == example.TargetIndex ? 1 : 0, Accuracy = predicted == example.TargetIndex ? 1d : 0d });
            else
            {
                var group = groups[groupIndex];
                var correct = group.Correct + (predicted == example.TargetIndex ? 1 : 0);
                groups[groupIndex] = group with { Count = group.Count + 1, Correct = correct, Accuracy = (double)correct / (group.Count + 1) };
            }
        }
        output = FullPaths(output);
        string? checkpointHash = null, headHash = null, reportHash = null;
        var temporaryReport = Path.Combine(Path.GetDirectoryName(output.Report)!, $".{Path.GetFileName(output.Report)}.{Guid.NewGuid():N}.tmp");
        try
        {
            foreach (var path in new[] { output.Checkpoint, output.Head, output.Report })
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            }
            checkpointHash = MarkerTrainingCheckpoint.SaveNew(output.Checkpoint, state, token);
            headHash = IndependentMarkerHeadAssetStore.SaveNew(output.Head, state, token);
            // Read both files through their production loaders before the report can
            // advertise success; reports are the final published commit marker.
            _ = MarkerTrainingCheckpoint.Load(output.Checkpoint, train, checkpointHash, token);
            _ = IndependentMarkerHeadAssetStore.Load(output.Head, state.Identity, state.FeatureSha256, headHash, token);
            var report = new WorkflowTrainingReport
            {
                SchemaVersion = 2, Status = source == TrainingFeatureSource.OriginalEncoder ? "development_smoke_only" : "fixture_contract_only",
                Identity = WorkflowTrainingIdentity.From(state.Identity), RecordsSha256 = recordsSha256,
                FeatureSha256 = state.FeatureSha256, HeadSha256 = headHash, CheckpointSha256 = checkpointHash,
                Seed = state.Seed, LearningRate = state.LearningRate, StepSemantics = "total_completed_steps",
                InitialSteps = restored?.CompletedSteps ?? 0, MaxSteps = options.MaxSteps, CompletedSteps = state.CompletedSteps,
                LastLoss = state.LastLoss, TrainCount = train.Examples.Count, DevelopmentCount = development.Count,
                UsesRealIndependentEncoder = source == TrainingFeatureSource.OriginalEncoder, RepresentsQualityAcceptance = false,
                Optimizer = "sgd_v1_no_momentum", FeatureSource = source == TrainingFeatureSource.OriginalEncoder ? "original_encoder" : "synthetic_fixture",
                TrainingTimeoutSeconds = options.MaxDuration.TotalSeconds, ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds,
                Resume = resume is null ? null : new WorkflowTrainingResume { CheckpointSha256 = resume.CheckpointSha256,
                    ReportSha256 = resume.ReportSha256, InitialSteps = restored!.CompletedSteps },
                DevelopmentGroups = groups.OrderBy(group => group.Language, StringComparer.Ordinal).ThenBy(group => group.QuestionType, StringComparer.Ordinal).ToArray()
            };
            token.ThrowIfCancellationRequested();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(report, WorkflowJsonContext.Default.WorkflowTrainingReport);
            _ = WorkflowTrainingReport.ParseBounded(bytes, token);
            using (var stream = new FileStream(temporaryReport, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporaryReport, output.Report);
            reportHash = Convert.ToHexString(SHA256.HashData(bytes));
            return new TrainingWorkflowResult(state, report, reportHash);
        }
        catch
        {
            // Only files this invocation successfully created, with the exact bytes
            // it published, are eligible for rollback. Existing outputs are preserved.
            RemoveOwnedOutput(output.Head, headHash);
            RemoveOwnedOutput(output.Checkpoint, checkpointHash);
            RemoveOwnedOutput(output.Report, reportHash);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryReport)) File.Delete(temporaryReport);
        }
    }

    public static void ValidateOutputPaths(TrainingOutputPaths paths, IReadOnlyList<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (inputs.Count > 8) throw new DecisionException("training_output_invalid", "Too many workflow input paths.");
        paths = FullPaths(paths);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var outputs = new[] { paths.Checkpoint, paths.Head, paths.Report };
        if (outputs.Distinct(comparer).Count() != 3 || outputs.Any(path => inputs.Any(input => comparer.Equals(path, Path.GetFullPath(input)))))
            throw new DecisionException("training_output_invalid", "Output paths must be distinct from one another and all inputs.");
        foreach (var path in outputs)
            if (File.Exists(path) || Directory.Exists(path))
                throw new DecisionException("training_output_exists", "A workflow output already exists; choose a new path.");
    }

    private static TrainingOutputPaths FullPaths(TrainingOutputPaths paths) =>
        new(Path.GetFullPath(paths.Checkpoint), Path.GetFullPath(paths.Head), Path.GetFullPath(paths.Report));

    private static void RemoveOwnedOutput(string path, string? hash)
    {
        if (hash is null || !File.Exists(path)) return;
        var bytes = ReadSmallFile(path);
        if (HashEqual(Convert.ToHexString(SHA256.HashData(bytes)), hash)) File.Delete(Path.GetFullPath(path));
    }

    internal static byte[] ReadSmallFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 1_048_576) throw new DecisionException("training_report_invalid", "File exceeds the 1 MiB workflow metadata bound.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }

    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(character => char.IsAsciiHexDigit(character));
    internal static bool HashEqual(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
}

public sealed record WorkflowTrainingIdentity
{
    public required string ModelId { get; init; }
    public required string EncoderSha256 { get; init; }
    public required string TokenizerSha256 { get; init; }
    public required string DataManifestSha256 { get; init; }
    public required int HiddenSize { get; init; }
    public required string Protocol { get; init; }
    public required string LengthPolicy { get; init; }

    public static WorkflowTrainingIdentity From(MarkerFeatureIdentity identity) => new() { ModelId = identity.ModelId,
        EncoderSha256 = identity.EncoderSha256, TokenizerSha256 = identity.TokenizerSha256, DataManifestSha256 = identity.DataManifestSha256,
        HiddenSize = identity.HiddenSize, Protocol = identity.Protocol, LengthPolicy = identity.LengthPolicy };
    public MarkerFeatureIdentity ToIdentity() => new(ModelId, EncoderSha256, TokenizerSha256, DataManifestSha256, HiddenSize, Protocol, LengthPolicy);
    public bool Matches(MarkerFeatureIdentity identity) => ModelId == identity.ModelId && HiddenSize == identity.HiddenSize &&
        Protocol == identity.Protocol && LengthPolicy == identity.LengthPolicy &&
        IndependentTrainingWorkflow.HashEqual(EncoderSha256, identity.EncoderSha256) &&
        IndependentTrainingWorkflow.HashEqual(TokenizerSha256, identity.TokenizerSha256) &&
        IndependentTrainingWorkflow.HashEqual(DataManifestSha256, identity.DataManifestSha256);
}

public sealed record WorkflowTrainingResume
{
    public required string CheckpointSha256 { get; init; }
    public required string ReportSha256 { get; init; }
    public required int InitialSteps { get; init; }
}

public sealed record WorkflowDevelopmentGroup
{
    public required string Language { get; init; }
    public required string QuestionType { get; init; }
    public required int Count { get; init; }
    public required int Correct { get; init; }
    public required double Accuracy { get; init; }
}

public sealed record WorkflowTrainingReport
{
    public required int SchemaVersion { get; init; }
    public required string Status { get; init; }
    public required WorkflowTrainingIdentity Identity { get; init; }
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
    public required WorkflowTrainingResume? Resume { get; init; }
    public required WorkflowDevelopmentGroup[] DevelopmentGroups { get; init; }

    /// <summary>Bind all report identity fields to the actual loaded encoder/data contract before loading its head.</summary>
    public MarkerFeatureIdentity BindIdentity(MarkerFeatureIdentity actualIdentity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actualIdentity);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(cancellationToken);
        if (!Identity.Matches(actualIdentity))
            throw new DecisionException("training_report_identity_mismatch", "The complete training report identity differs from the loaded encoder or data contract.");
        // Preserve the asserted identity instead of reconstructing and silently
        // replacing dimensions or protocol fields from the current model.
        return Identity.ToIdentity();
    }

    public static WorkflowTrainingReport ReadBounded(string path, string expectedSha256, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IndependentTrainingWorkflow.IsHash(expectedSha256))
            throw new DecisionException("training_report_hash_invalid", "Expected training report SHA-256 is required.");
        var bytes = IndependentTrainingWorkflow.ReadSmallFile(path);
        if (!IndependentTrainingWorkflow.HashEqual(Convert.ToHexString(SHA256.HashData(bytes)), expectedSha256))
            throw new DecisionException("training_report_hash_invalid", "Training report SHA-256 does not match.");
        return ParseBounded(bytes, cancellationToken);
    }

    public static WorkflowTrainingReport ParseBounded(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length is <= 0 or > 1_048_576)
            throw new DecisionException("training_report_invalid", "Training report exceeds the 1 MiB bound.");
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 16 });
            var scopes = new Stack<HashSet<string>>();
            var count = 0;
            var clock = Stopwatch.StartNew();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > 4096 || clock.Elapsed > TimeSpan.FromSeconds(10))
                    throw new DecisionException("training_report_invalid", "Training report JSON exceeds token or wall-clock limits.");
                if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new(StringComparer.Ordinal));
                else if (reader.TokenType == JsonTokenType.EndObject) scopes.Pop();
                else if (reader.TokenType == JsonTokenType.PropertyName && !scopes.Peek().Add(reader.GetString()!))
                    throw new DecisionException("training_report_invalid", "Training report contains duplicate properties.");
            }
            var report = JsonSerializer.Deserialize(bytes, WorkflowJsonContext.Default.WorkflowTrainingReport) ??
                throw new DecisionException("training_report_invalid", "Training report is null.");
            report.Validate(cancellationToken);
            return report;
        }
        catch (JsonException)
        {
            throw new DecisionException("training_report_invalid", "Training report JSON fields, types or version are invalid.");
        }
    }

    internal void Validate(CancellationToken cancellationToken)
    {
        var identity = Identity;
        if (SchemaVersion != 2 || Status is not ("development_smoke_only" or "fixture_contract_only") ||
            identity is null || string.IsNullOrWhiteSpace(identity.ModelId) || identity.ModelId.Length > 128 ||
            !identity.ModelId.StartsWith("sezika/", StringComparison.Ordinal) || identity.HiddenSize is < 1 or > 2048 ||
            identity.Protocol != "decision-v1" || identity.LengthPolicy != "strict" ||
            !IndependentTrainingWorkflow.IsHash(identity.EncoderSha256) || !IndependentTrainingWorkflow.IsHash(identity.TokenizerSha256) ||
            !IndependentTrainingWorkflow.IsHash(identity.DataManifestSha256) || !IndependentTrainingWorkflow.IsHash(RecordsSha256) ||
            !IndependentTrainingWorkflow.IsHash(FeatureSha256) || !IndependentTrainingWorkflow.IsHash(HeadSha256) ||
            !IndependentTrainingWorkflow.IsHash(CheckpointSha256) || !float.IsFinite(LearningRate) || LearningRate <= 0 || LearningRate > 1 ||
            StepSemantics != "total_completed_steps" || MaxSteps is < 1 or > 10_000 || CompletedSteps != MaxSteps ||
            InitialSteps < 0 || InitialSteps > CompletedSteps || !double.IsFinite(LastLoss) || LastLoss < 0 ||
            TrainCount is < 1 or > 31 || DevelopmentCount is < 1 or > 31 || TrainCount + DevelopmentCount > 32 ||
            RepresentsQualityAcceptance || Optimizer != "sgd_v1_no_momentum" ||
            FeatureSource != (UsesRealIndependentEncoder ? "original_encoder" : "synthetic_fixture") ||
            Status != (UsesRealIndependentEncoder ? "development_smoke_only" : "fixture_contract_only") ||
            !double.IsFinite(TrainingTimeoutSeconds) || TrainingTimeoutSeconds <= 0 || TrainingTimeoutSeconds > 600 ||
            !double.IsFinite(ElapsedMilliseconds) || ElapsedMilliseconds < 0 || DevelopmentGroups is null || DevelopmentGroups.Length is < 1 or > 6 ||
            (Resume is null && InitialSteps != 0) ||
            (Resume is not null && (!IndependentTrainingWorkflow.IsHash(Resume.CheckpointSha256) ||
                !IndependentTrainingWorkflow.IsHash(Resume.ReportSha256) || Resume.InitialSteps != InitialSteps)))
            throw new DecisionException("training_report_invalid", "Training report contract or identity is invalid.");
        var keys = new HashSet<(string, string)>();
        var total = 0;
        foreach (var group in DevelopmentGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group is null || group.Language is not ("zh" or "en") || group.QuestionType is not ("choice" or "score" or "boolean") ||
                !keys.Add((group.Language, group.QuestionType)) || group.Count is < 1 or > 32 || group.Correct < 0 || group.Correct > group.Count ||
                !double.IsFinite(group.Accuracy) || group.Accuracy != (double)group.Correct / group.Count)
                throw new DecisionException("training_report_invalid", "Training report development denominators are invalid.");
            total += group.Count;
        }
        if (total != DevelopmentCount) throw new DecisionException("training_report_invalid", "Training report development count differs from its groups.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true)]
[JsonSerializable(typeof(WorkflowTrainingReport))]
internal partial class WorkflowJsonContext : JsonSerializerContext;
