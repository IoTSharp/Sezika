using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.ExperimentTrainer;

internal sealed partial class ExperimentData : IDisposable
{
    internal const string Contract = "sezika/teacher-labelled-experiment-data/v1";
    internal const int RecordsLimit = 32 * 65_538 + 3;
    private readonly FileStream?[] _leases = new FileStream?[6];
    private readonly Exception?[] _cleanupErrors = new Exception?[7];
    private readonly IOException _cleanupTimeout = new("Experiment input lease cleanup deadline expired.");
    internal ExperimentManifest Manifest { get; private set; } = null!;
    internal ExperimentRecord[] Records { get; private set; } = [];
    internal string ManifestSha256 { get; private set; } = "";
    internal string RecordsSha256 { get; private set; } = "";
    internal string ProvenanceSha256 { get; private set; } = "";
    internal string Root { get; private set; } = "";

    internal static ExperimentData Open(string directory, string expectedManifestSha256, Action check, CancellationToken token)
    {
        RequireHash(expectedManifestSha256);
        var result = new ExperimentData { Root = Path.GetFullPath(directory) };
        try
        {
            var names = new[] { "experiment-manifest.json", "experiment-records.jsonl", "input-provenance.json" };
            for (var index = 0; index < 3; index++)
            {
                check();
                var path = Path.Combine(result.Root, names[index]);
                CheckPath(path, check);
                result._leases[index] = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            var manifestBytes = Snapshot(result._leases[0]!, 1_048_576, check);
            result.ManifestSha256 = Hash(manifestBytes);
            if (!SameHash(result.ManifestSha256, expectedManifestSha256)) throw Invalid("Experiment manifest SHA differs from the explicit expected hash.");
            CheckJson(manifestBytes, 1_048_576, token);
            result.Manifest = JsonSerializer.Deserialize(manifestBytes, ExperimentJsonContext.Default.ExperimentManifest) ?? throw Invalid("Experiment manifest is null.");
            ValidateManifest(result.Manifest);
            var recordBytes = Snapshot(result._leases[1]!, RecordsLimit, check);
            result.RecordsSha256 = Hash(recordBytes);
            if (result.Manifest.Records.Bytes != recordBytes.LongLength || !SameHash(result.Manifest.Records.Sha256, result.RecordsSha256))
                throw Invalid("Experiment record bytes/hash differs from its manifest.");
            result.Records = ParseRecords(recordBytes, result.Manifest, check, token);
            var provenanceBytes = Snapshot(result._leases[2]!, 1_048_576, check);
            result.ProvenanceSha256 = Hash(provenanceBytes);
            if (result.Manifest.Provenance.Bytes != provenanceBytes.LongLength || !SameHash(result.Manifest.Provenance.Sha256, result.ProvenanceSha256))
                throw Invalid("Experiment provenance bytes/hash differs from its manifest.");
            CheckJson(provenanceBytes, 1_048_576, token);
            using var provenance = JsonDocument.Parse(provenanceBytes);
            var rows = provenance.RootElement.GetProperty("records");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 32) throw Invalid("Input provenance requires all32 rows.");
            for (var index = 0; index < 32; index++)
            {
                check(); var row = rows[index]; var record = result.Records[index];
                if (row.GetProperty("record_id").GetString() != record.RecordId || row.GetProperty("family_id").GetString() != record.FamilyId ||
                    row.GetProperty("split").GetString() != record.Split || row.GetProperty("target_index").GetInt32() != record.TargetIndex)
                    throw Invalid("Derived row differs from its teacher provenance mapping.");
                var ids = row.GetProperty("candidate_ids").EnumerateArray().Select(value => value.GetString()).ToArray();
                if (!ids.SequenceEqual(record.CandidateIds, StringComparer.Ordinal) || row.GetProperty("selected_candidate_id").GetString() != record.CandidateIds[record.TargetIndex])
                    throw Invalid("Teacher label differs from the preserved candidate order.");
            }
            check(); return result;
        }
        catch (Exception primary)
        {
            try { result.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Experiment input acquisition and cleanup failed.", primary, cleanup); }
            throw;
        }
    }

    internal static void ValidateManifest(ExperimentManifest value)
    {
        if (value.SchemaVersion != 1 || value.Contract != Contract || value.DatasetId != "general-zh-en-teacher-experiment-32-v1" ||
            value.Purpose != "teacher_labelled_experiment" || value.Protocol != "decision-v1" || value.StudentModelId != IndependentModelLoader.ModelId ||
            value.Formal || value.HumanReview != "pending" || value.TeacherOutputTrainingPermitted || !value.UserAuthorizedExperiment ||
            !value.TeacherAnswerExposed || value.EligibleAsSealedTest || value.Total != 32 || value.Train != 24 || value.Development != 8 ||
            value.SplitRule != "zh_en_first2_choice_first1_score_first1_boolean_development_v1" || value.TeacherApi is not null || value.FixtureOnly ||
            string.IsNullOrWhiteSpace(value.SplitRuleFrozenUtc) || value.Sources is null || value.Sources.Length != 3)
            throw Invalid("Only the explicit pending, exposed, user-authorized 24/8 teacher experiment contract is accepted.");
        ValidateAsset(value.Records); ValidateAsset(value.Provenance);
        if (!DeclaredName(value.Records.Path, "experiment-records.jsonl") || !DeclaredName(value.Provenance.Path, "input-provenance.json"))
            throw Invalid("Experiment declaration must bind the fixed derived package names.");
        var expectedKinds = new HashSet<string>(["teacher_manifest", "teacher_records", "review_template"], StringComparer.Ordinal);
        for (var index = 0; index < 3; index++)
        {
            var source = value.Sources[index]; ValidateAsset(source);
            if (!expectedKinds.Remove(source.Kind)) throw Invalid("Source declarations are duplicated or unsupported.");
        }
        var teacher = value.Teacher;
        if (teacher is null || teacher.ModelId != "qwen3.5-9b-q4-k-m" || teacher.Revision != "3885219b6810b007914f3a7950a8d1b469d598a5" ||
            !SameHash(teacher.ModelSha256, "03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8") ||
            teacher.License != "Apache-2.0" || teacher.HumanLicenseReview != "pending" || teacher.QuantizedBaseRevisionEquivalence != "not established" ||
            teacher.OfficialLicenseRevision != "c202236235762e1c871ad0ccb60c8ee5ba337b9a" || string.IsNullOrWhiteSpace(teacher.SourceUrl))
            throw Invalid("Teacher/source/license identity differs from the preserved unreviewed pilot declaration.");
        ValidateAsset(teacher.OfficialLicenseSnapshot);
    }

    internal static ExperimentRecord[] ParseRecords(byte[] bytes, ExperimentManifest manifest, Action check, CancellationToken token)
    {
        if (bytes.Length is < 1 or > RecordsLimit) throw Invalid("Experiment records exceed the byte bound.");
        using var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes));
        var result = new List<ExperimentRecord>(32); var ids = new HashSet<string>(StringComparer.Ordinal); var families = new HashSet<string>(StringComparer.Ordinal);
        var train = 0; var development = 0;
        for (var line = 0; line < 33; line++)
        {
            check(); var text = reader.ReadLine(); if (text is null) break;
            if (line == 32 || string.IsNullOrWhiteSpace(text)) throw Invalid("Exactly32 nonempty experiment rows are required.");
            var utf8 = Encoding.UTF8.GetBytes(text); CheckJson(utf8, 65_536, token);
            var row = JsonSerializer.Deserialize(utf8, ExperimentJsonContext.Default.ExperimentRecordJson) ?? throw Invalid("Experiment row is null.");
            if (row.TeacherBinding is not null) throw Invalid("API teacher bindings are accepted only by the explicit v2 expansion command.");
            if (string.IsNullOrWhiteSpace(row.RecordId) || row.RecordId.Length > 128 || !ids.Add(row.RecordId) ||
                string.IsNullOrWhiteSpace(row.FamilyId) || row.FamilyId.Length > 128 || !families.Add(row.FamilyId) ||
                row.Language is not ("zh" or "en") || row.Split is not ("train" or "development")) throw Invalid("Experiment record identity/split is invalid or duplicated.");
            var request = IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes(row.Request.GetRawText()));
            if (request.SchemaVersion != 1 || request.Questions.Count != 1) throw Invalid("Experiment requires one actual decision-v1 question per row.");
            var question = request.Questions[0];
            var candidateIds = question switch
            {
                IndependentChoiceQuestion choice => choice.Candidates.Select(candidate => candidate.Id).ToArray(),
                IndependentScoreQuestion score => Enumerable.Range(0, score.Levels.Count).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
                IndependentBooleanQuestion => ["false", "true"],
                _ => throw Invalid("Unknown experiment question type."),
            };
            var type = question switch { IndependentChoiceQuestion => MarkerQuestionType.Choice, IndependentScoreQuestion => MarkerQuestionType.Score, _ => MarkerQuestionType.Boolean };
            if ((uint)row.TargetIndex >= (uint)candidateIds.Length || candidateIds.Length != (type == MarkerQuestionType.Choice ? 4 : type == MarkerQuestionType.Score ? 5 : 2))
                throw Invalid("Teacher target or candidate count differs from the pilot contract.");
            var expectedSplit = row.RecordId.EndsWith("choice-01", StringComparison.Ordinal) || row.RecordId.EndsWith("choice-02", StringComparison.Ordinal) ||
                row.RecordId.EndsWith("score-01", StringComparison.Ordinal) || row.RecordId.EndsWith("boolean-01", StringComparison.Ordinal) ? "development" : "train";
            if (row.Split != expectedSplit) throw Invalid("Record split differs from the predeclared rule.");
            if (row.Split == "train") train++; else development++;
            result.Add(new(row.RecordId, row.FamilyId, row.Language, row.Split, row.TargetIndex, request, question, type, candidateIds));
        }
        if (result.Count != 32 || train != manifest.Train || development != manifest.Development) throw Invalid("Experiment split denominators differ.");
        return result.ToArray();
    }

    internal static void CheckJson(ReadOnlySpan<byte> bytes, int maximum, CancellationToken token, int maximumTokens = 65_536)
    {
        if (bytes.Length is < 1 || bytes.Length > maximum || maximumTokens is < 1 or > 500_000) throw Invalid("JSON byte/token bound exceeded.");
        var watch = Stopwatch.StartNew(); var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 });
        var objects = new Stack<HashSet<string>>(); var count = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (++count > maximumTokens || watch.Elapsed > TimeSpan.FromSeconds(10)) throw Invalid("JSON token/deadline bound exceeded.");
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek().Add(reader.GetString()!)) throw Invalid("Duplicate JSON properties are rejected.");
        }
    }

    internal static void CheckPath(string path, Action check)
    {
        string? item = Path.GetFullPath(path);
        for (var depth = 0; depth < 64 && item is not null; depth++)
        {
            check();
            if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw Invalid("Experiment paths cannot use links/junctions.");
            item = Path.GetDirectoryName(item);
        }
        if (item is not null) throw Invalid("Experiment path exceeds depth bound.");
    }
    internal static byte[] Snapshot(FileStream stream, int maximum, Action check)
    {
        check(); if (stream.Length is < 1 || stream.Length > maximum) throw Invalid("Input snapshot exceeds byte bound.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); check();
        if (stream.ReadByte() != -1) throw Invalid("Input size changed during snapshot."); return bytes;
    }
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static bool SameHash(string? left, string? right) => IsHash(left) && IsHash(right) && left!.Equals(right, StringComparison.OrdinalIgnoreCase);
    internal static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);
    internal static void RequireHash(string? hash) { if (!IsHash(hash)) throw Invalid("A pinned64hex SHA256 is required."); }
    private static void ValidateAsset(ExperimentAsset value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Path) || value.Path.Length > 2048 || value.Bytes is < 1 or > RecordsLimit)
            throw Invalid("Declared asset path/byte identity is invalid."); RequireHash(value.Sha256);
    }
    private static bool DeclaredName(string path, string name) => path.EndsWith("/" + name, StringComparison.Ordinal) || path.EndsWith("\\" + name, StringComparison.Ordinal);
    internal static DecisionException Invalid(string text) => new("experiment_input_invalid", text);
    public void Dispose()
    {
        var started = Stopwatch.GetTimestamp(); Array.Clear(_cleanupErrors);
        for (var index = 0; index < 6; index++)
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5)) _cleanupErrors[6] = _cleanupTimeout;
            try { _leases[index]?.Dispose(); _leases[index] = null; } catch (Exception error) { _cleanupErrors[index] = error; }
        }
        if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5)) _cleanupErrors[6] = _cleanupTimeout;
        var errors = new List<Exception>(7);
        for (var index = 0; index < 7; index++) if (_cleanupErrors[index] is { } error) errors.Add(error);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Experiment input lease cleanup errors.", errors);
    }
}

internal sealed record ExperimentRecord(string RecordId, string FamilyId, string Language, string Split, int TargetIndex,
    IndependentDecisionRequest Request, IndependentDecisionQuestion Question, MarkerQuestionType Type, string[] CandidateIds,
    ExperimentTeacherBinding? TeacherBinding = null, JsonElement? OriginalRequestJson = null);
internal sealed class ExperimentRecordJson
{
    public required string RecordId { get; init; }
    public required string FamilyId { get; init; }
    public required string Language { get; init; }
    public required string Split { get; init; }
    public required int TargetIndex { get; init; }
    public required JsonElement Request { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExperimentTeacherBinding? TeacherBinding { get; init; }
}
internal class ExperimentAsset
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public required long Bytes { get; init; }
}
internal sealed class ExperimentSource : ExperimentAsset { public required string Kind { get; init; } }
internal sealed class ExperimentTeacher
{
    public required string ModelId { get; init; }
    public required string Revision { get; init; }
    public required string ModelSha256 { get; init; }
    public required string SourceUrl { get; init; }
    public required string License { get; init; }
    public required string OfficialLicenseRevision { get; init; }
    public required ExperimentAsset OfficialLicenseSnapshot { get; init; }
    public required string HumanLicenseReview { get; init; }
    public required string QuantizedBaseRevisionEquivalence { get; init; }
}
internal sealed class ExperimentManifest
{
    public required int SchemaVersion { get; init; }
    public required string Contract { get; init; }
    public required string DatasetId { get; init; }
    public required string Purpose { get; init; }
    public required string Protocol { get; init; }
    public required string StudentModelId { get; init; }
    public required bool Formal { get; init; }
    public required string HumanReview { get; init; }
    public required bool TeacherOutputTrainingPermitted { get; init; }
    public required bool UserAuthorizedExperiment { get; init; }
    public required bool TeacherAnswerExposed { get; init; }
    public required bool EligibleAsSealedTest { get; init; }
    public required int Total { get; init; }
    public required int Train { get; init; }
    public required int Development { get; init; }
    public required ExperimentAsset Records { get; init; }
    public required ExperimentSource[] Sources { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExperimentTeacher? Teacher { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExperimentTeacherApi? TeacherApi { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FixtureOnly { get; init; }
    public required string SplitRule { get; init; }
    public required string SplitRuleFrozenUtc { get; init; }
    public required ExperimentAsset Provenance { get; init; }
}

internal sealed record ExperimentTeacherApi(string Provider, string Model, string Effort, string Identity,
    string TrainingPermission, string License);
internal sealed record ExperimentTeacherBinding(string SourceCanonicalId, string ResponseId, string RequestSha256,
    string ResponseSha256, string SelectedCandidateId, int TargetIndex);
