using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.IndependentModelTool;

/// <summary>Reviewed input declarations. Validation consumes declarations; it never creates human approval.</summary>
public sealed record ReviewedOriginalTrainingManifest
{
    public required int SchemaVersion { get; init; }
    public required string TrainingContract { get; init; }
    public required string DatasetId { get; init; }
    public required string RecordsFile { get; init; }
    public required string RecordsSha256 { get; init; }
    public required long RecordsBytes { get; init; }
    public required string Source { get; init; }
    public required string Origin { get; init; }
    public required string Purpose { get; init; }
    public required string LicenseExpression { get; init; }
    public required string LicenseReview { get; init; }
    public required string[] AllowedUses { get; init; }
    public required string[] AllowedSplits { get; init; }
    public required ReviewedOriginalSplitCounts SplitCounts { get; init; }
    public required string HumanAcceptance { get; init; }
    public required bool TrainingAdmission { get; init; }
    public required string HumanReviewId { get; init; }
    public required bool RepresentsModelQuality { get; init; }
}

public sealed record ReviewedOriginalSplitCounts
{
    public required int Train { get; init; }
    public required int Development { get; init; }
}

public sealed record ReviewedOriginalTrainingRecord(string RecordId, string Split, string Language, string FamilyId,
    string Source, string HumanReviewId, int TargetIndex, IndependentDecisionRequest Request, IndependentDecisionQuestion Question);

/// <summary>Owns read-only input leases from hashing through feature consumption and output publication.</summary>
public sealed class ReviewedOriginalTrainingInput : IDisposable
{
    private readonly FileStream _recordsLease;
    private readonly FileStream _manifestLease;
    private int _disposed;
    internal ReviewedOriginalTrainingInput(FileStream recordsLease, FileStream manifestLease, ReviewedOriginalTrainingManifest manifest,
        ReviewedOriginalTrainingRecord[] records, string recordsSha256, string manifestSha256)
    {
        _recordsLease = recordsLease; _manifestLease = manifestLease;
        Manifest = manifest; Records = Array.AsReadOnly(records); RecordsSha256 = recordsSha256; ManifestSha256 = manifestSha256;
        RecordsPath = recordsLease.Name; ManifestPath = manifestLease.Name;
    }
    public ReviewedOriginalTrainingManifest Manifest { get; }
    public IReadOnlyList<ReviewedOriginalTrainingRecord> Records { get; }
    public string RecordsSha256 { get; }
    public string ManifestSha256 { get; }
    public string RecordsPath { get; }
    public string ManifestPath { get; }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _recordsLease.Dispose(); } finally { _manifestLease.Dispose(); }
    }
}

/// <summary>Finite formal admission for reviewed-original-records-v1; legacy smoke and pending pilot files do not qualify.</summary>
public static class OriginalTrainingAdmission
{
    public const string ContractName = "reviewed-original-records-v1";
    private const int MaxRecordsBytes = 32 * (65_536 + 2) + 3;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static ReviewedOriginalTrainingInput ReadBounded(string recordsPath, string manifestPath,
        Action<int, int>? progress = null, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var clock = Stopwatch.StartNew();
        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (clock.Elapsed >= TimeSpan.FromSeconds(30)) throw Invalid("Original admission wall-clock budget expired.");
        }
        Check();
        recordsPath = Path.GetFullPath(recordsPath); manifestPath = Path.GetFullPath(manifestPath);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (comparer.Equals(recordsPath, manifestPath) || !comparer.Equals(Path.GetDirectoryName(recordsPath), Path.GetDirectoryName(manifestPath)))
            throw Invalid("Records and manifest must be distinct files in the same directory.");
        FileStream? recordsLease = null, manifestLease = null;
        var transferred = false;
        try
        {
            recordsLease = new FileStream(recordsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            manifestLease = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifestBytes = ReadSnapshot(manifestLease, 1_048_576, Check);
            var manifest = ParseManifestBounded(manifestBytes, token);
            if (manifest.RecordsFile != Path.GetFileName(recordsPath)) throw Invalid("Manifest records_file does not name the consumed file.");
            var recordsBytes = ReadSnapshot(recordsLease, MaxRecordsBytes, Check);
            var recordsSha256 = Convert.ToHexString(SHA256.HashData(recordsBytes));
            if (manifest.RecordsBytes != recordsBytes.LongLength || !IndependentTrainingWorkflow.HashEqual(manifest.RecordsSha256, recordsSha256))
                throw Invalid("Manifest records bytes or SHA-256 does not match the consumed snapshot.");
            var records = ParseRecords(recordsBytes, manifest, Check, progress, token);
            Check();
            var result = new ReviewedOriginalTrainingInput(recordsLease, manifestLease, manifest, records,
                recordsSha256, Convert.ToHexString(SHA256.HashData(manifestBytes)));
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                try { recordsLease?.Dispose(); } finally { manifestLease?.Dispose(); }
            }
        }
    }

    public static ReviewedOriginalTrainingManifest ParseManifestBounded(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            BoundedTrainingJson.Check(bytes, 1_048_576, 16, 4096, cancellationToken, Invalid);
            var manifest = JsonSerializer.Deserialize(bytes, OriginalAdmissionJsonContext.Default.ReviewedOriginalTrainingManifest) ?? throw Invalid("Original manifest is null.");
            var counts = manifest.SplitCounts;
            if (manifest.SchemaVersion != 2 || manifest.TrainingContract != ContractName || !Text(manifest.DatasetId, 128) ||
                !Text(manifest.RecordsFile, 128) || Path.GetFileName(manifest.RecordsFile) != manifest.RecordsFile ||
                manifest.RecordsFile is "." or ".." || manifest.RecordsFile.IndexOfAny(['/', '\\', ':']) >= 0 ||
                !IndependentTrainingWorkflow.IsHash(manifest.RecordsSha256) || manifest.RecordsBytes is < 1 or > MaxRecordsBytes ||
                !Text(manifest.Source, 256) || manifest.Origin != "Original" || manifest.Purpose != "training" ||
                !Text(manifest.LicenseExpression, 256) || manifest.LicenseReview != "accepted" ||
                manifest.AllowedUses is null || manifest.AllowedUses.Length is < 1 or > 2 ||
                manifest.AllowedUses.Distinct(StringComparer.Ordinal).Count() != manifest.AllowedUses.Length ||
                manifest.AllowedUses.Any(value => value is not ("Research" or "Development")) ||
                manifest.AllowedSplits is null || manifest.AllowedSplits.Length != 2 ||
                !manifest.AllowedSplits.Contains("Train", StringComparer.Ordinal) || !manifest.AllowedSplits.Contains("Development", StringComparer.Ordinal) ||
                counts is null || counts.Train is < 1 or > 31 || counts.Development is < 1 or > 31 || counts.Train + counts.Development > 32 ||
                manifest.HumanAcceptance != "accepted" || !manifest.TrainingAdmission || !Text(manifest.HumanReviewId, 128) || manifest.RepresentsModelQuality)
                throw Invalid("Original manifest requires existing accepted human/license/admission declarations and isolated train/development use.");
            cancellationToken.ThrowIfCancellationRequested();
            return manifest;
        }
        catch (JsonException) { throw Invalid("Original manifest has unknown, missing or invalid fields."); }
    }

    private static ReviewedOriginalTrainingRecord[] ParseRecords(byte[] bytes, ReviewedOriginalTrainingManifest manifest,
        Action check, Action<int, int>? progress, CancellationToken cancellationToken)
    {
        string text;
        try { text = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw Invalid("Records must be valid UTF-8."); }
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        using var reader = new StringReader(text);
        var records = new List<ReviewedOriginalTrainingRecord>(32);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var families = new Dictionary<string, string>(StringComparer.Ordinal);
        var trainCount = 0; var developmentCount = 0;
        // At most 32 records plus one overflow probe; Check also enforces the global 30-second deadline and cancellation.
        for (var lineNumber = 1; lineNumber <= 33; lineNumber++)
        {
            check();
            var line = reader.ReadLine();
            if (line is null) break;
            if (lineNumber > 32 || line.Length == 0 || StrictUtf8.GetByteCount(line) > 65_536) throw Invalid("Records exceed count or line size bounds.");
            try
            {
                var utf8 = StrictUtf8.GetBytes(line);
                BoundedTrainingJson.Check(utf8, 65_536, 32, 16_384, cancellationToken, Invalid);
                var row = JsonSerializer.Deserialize(utf8, OriginalAdmissionJsonContext.Default.ReviewedOriginalRecordJson) ?? throw Invalid("Original record is null.");
                if (!Text(row.RecordId, 128) || !ids.Add(row.RecordId) || row.Split is not ("train" or "development") || row.Language is not ("zh" or "en") ||
                    !Text(row.FamilyId, 128) || row.Source != manifest.Source || !Text(row.HumanReviewId, 128) ||
                    row.ReviewStatus != "accepted" || !row.TrainingAdmission || !Text(row.Instruction, 65_536) ||
                    row.State.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    throw Invalid("Original record is unreviewed, unadmitted, duplicated, missing provenance or outside train/development.");
                if (families.TryGetValue(row.FamilyId, out var split) && split != row.Split) throw Invalid("A family cannot cross train/development splits.");
                families[row.FamilyId] = row.Split;
                var question = BuildQuestion(row, check);
                var candidateCount = question switch { IndependentChoiceQuestion value => value.Candidates.Count, IndependentScoreQuestion value => value.Levels.Count,
                    IndependentBooleanQuestion => 2, _ => 0 };
                if ((uint)row.TargetIndex >= (uint)candidateCount) throw Invalid("Original target_index is outside the ordered candidate range.");
                records.Add(new(row.RecordId, row.Split, row.Language, row.FamilyId, row.Source, row.HumanReviewId, row.TargetIndex,
                    new IndependentDecisionRequest(1, IndependentModelLoader.ModelId, row.State.Clone(), [question]), question));
                if (row.Split == "train") trainCount++; else developmentCount++;
                progress?.Invoke(records.Count, manifest.SplitCounts.Train + manifest.SplitCounts.Development);
                check();
            }
            catch (JsonException) { throw Invalid("Original record has unknown, missing or invalid fields."); }
        }
        if (records.Count is < 2 or > 32 || trainCount != manifest.SplitCounts.Train || developmentCount != manifest.SplitCounts.Development)
            throw Invalid("Original records do not match manifest split denominators.");
        return records.ToArray();
    }

    private static IndependentDecisionQuestion BuildQuestion(ReviewedOriginalRecordJson row, Action check)
    {
        switch (row.Type)
        {
            case "choice":
                if (row.Candidates is null || row.Candidates.Length is < 2 or > 32 || row.Levels is not null ||
                    row.Statement is not null || row.WhenFalse is not null || row.WhenTrue is not null) throw Invalid("Choice record shape is invalid.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var candidates = new IndependentChoiceCandidate[row.Candidates.Length];
                for (var index = 0; index < candidates.Length && index < 32; index++)
                {
                    check(); var candidate = row.Candidates[index];
                    if (candidate is null || !Text(candidate.Id, 128) || !ids.Add(candidate.Id) || !Text(candidate.Text, 65_536)) throw Invalid("Choice candidates must have unique bounded IDs and text.");
                    candidates[index] = new(candidate.Id, candidate.Text);
                }
                return new IndependentChoiceQuestion(row.RecordId, row.Instruction, candidates);
            case "score":
                if (row.Levels is null || row.Levels.Length is < 2 or > 10 || row.Candidates is not null ||
                    row.Statement is not null || row.WhenFalse is not null || row.WhenTrue is not null) throw Invalid("Score record shape is invalid.");
                for (var index = 0; index < row.Levels.Length && index < 10; index++) { check(); if (!Text(row.Levels[index], 65_536)) throw Invalid("Score levels must be bounded nonempty text."); }
                return new IndependentScoreQuestion(row.RecordId, row.Instruction, (string[])row.Levels.Clone());
            case "boolean":
                if (row.Candidates is not null || row.Levels is not null || !Text(row.Statement, 65_536) || !Text(row.WhenFalse, 65_536) || !Text(row.WhenTrue, 65_536))
                    throw Invalid("Boolean record shape is invalid.");
                return new IndependentBooleanQuestion(row.RecordId, row.Instruction, row.Statement!, row.WhenFalse!, row.WhenTrue!);
            default: throw Invalid("Original record type is unsupported.");
        }
    }

    private static byte[] ReadSnapshot(FileStream stream, int maximumBytes, Action check)
    {
        check();
        if (stream.Length is <= 0 || stream.Length > maximumBytes) throw Invalid("Admission file byte limit exceeded.");
        var bytes = new byte[checked((int)stream.Length)];
        var blocks = (bytes.Length + 65_535) / 65_536;
        for (var block = 0; block < blocks && block < 33; block++)
        {
            check(); var offset = block * 65_536;
            stream.ReadExactly(bytes.AsSpan(offset, Math.Min(65_536, bytes.Length - offset)));
        }
        check();
        if (stream.ReadByte() != -1) throw Invalid("Admission file changed during snapshot read.");
        return bytes;
    }

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
    internal static DecisionException Invalid(string message) => new("training_admission_invalid", message);
}

internal sealed record ReviewedOriginalRecordJson
{
    public required string RecordId { get; init; }
    public required string Split { get; init; }
    public required string Language { get; init; }
    public required string FamilyId { get; init; }
    public required string Source { get; init; }
    public required string ReviewStatus { get; init; }
    public required bool TrainingAdmission { get; init; }
    public required string HumanReviewId { get; init; }
    public required string Type { get; init; }
    public required JsonElement State { get; init; }
    public required string Instruction { get; init; }
    public required int TargetIndex { get; init; }
    public ReviewedOriginalCandidateJson[]? Candidates { get; init; }
    public string[]? Levels { get; init; }
    public string? Statement { get; init; }
    public string? WhenFalse { get; init; }
    public string? WhenTrue { get; init; }
}

internal sealed record ReviewedOriginalCandidateJson
{
    public required string Id { get; init; }
    public required string Text { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ReviewedOriginalTrainingManifest))]
[JsonSerializable(typeof(ReviewedOriginalRecordJson))]
internal partial class OriginalAdmissionJsonContext : JsonSerializerContext;
