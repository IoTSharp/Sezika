using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Sezika;

namespace Sezika.ExperimentTrainer;

internal sealed partial class ExperimentData
{
    internal const string RepairedContract = "sezika/teacher-labelled-experiment-data/v3";
    internal const string RepairedParentSha256 = "17946E4ED202F45832C4E0E90B62F2CD7AAEDED096896E97268CCDB82DF3C937";
    internal const int RepairedTotal = 215;
    internal const int RepairedTrain = 162;
    internal const int RepairedDevelopment = 53;

    internal static void ValidateRepairedManifest(ExperimentManifest value)
    {
        if (value.SchemaVersion != 3 || value.Contract != RepairedContract || value.DatasetId != "general-zh-en-repaired-20261009-v1" ||
            value.Purpose != "teacher_labelled_experiment" || value.Protocol != "decision-v1" || value.StudentModelId != IndependentModelLoader.ModelId ||
            value.Formal || value.HumanReview != "pending" || value.TeacherOutputTrainingPermitted || !value.UserAuthorizedExperiment ||
            !value.TeacherAnswerExposed || value.EligibleAsSealedTest || value.Total != RepairedTotal || value.Train != RepairedTrain || value.Development != RepairedDevelopment ||
            value.SplitRule != "inherit-parent-family-split-no-reshuffle-with-explicit-deletions" || value.SplitRuleFrozenUtc != "2026-10-08T11:38:40.6891325Z" ||
            value.Teacher is not null || value.FixtureOnly || value.Sources is null || value.Sources.Length != 3 ||
            value.TeacherApi is not { Provider: "sonnet_vip", Model: "gpt-6.1-sol", Effort: "max", Identity: "gateway-response-declaration-only",
                TrainingPermission: "user-authorized-experiment-only", License: "unknown" })
            throw Invalid("Only the explicit pending, exposed 215/162/53 repaired v3 experiment contract is accepted.");
        ValidateExpandedAsset(value.Records); ValidateExpandedAsset(value.Provenance);
        if (value.Derivation is null) throw Invalid("Repaired v3 requires its complete256 derivation ledger.");
        ValidateExpandedAsset(value.Derivation);
        if (!DeclaredName(value.Records.Path, "experiment-records.jsonl") || !DeclaredName(value.Provenance.Path, "input-provenance.json") ||
            !DeclaredName(value.Derivation.Path, "derivation-ledger.json")) throw Invalid("Repaired package file names differ.");
        var kinds = new HashSet<string>(["source_catalog", "teacher_requests", "teacher_responses"], StringComparer.Ordinal);
        for (var index = 0; index < 3; index++)
        {
            var source = value.Sources[index]; ValidateExpandedAsset(source);
            var name = source.Kind switch { "source_catalog" => "source-catalog.json", "teacher_requests" => "teacher-requests.json", "teacher_responses" => "teacher-responses.json", _ => "" };
            if (!kinds.Remove(source.Kind) || !DeclaredName(source.Path, name)) throw Invalid("Repaired source kinds/names differ.");
        }
    }

    internal static ExperimentData OpenRepaired(string directory, string expectedManifestSha256, Action check, CancellationToken token)
    {
        RequireHash(expectedManifestSha256);
        var clock = Stopwatch.StartNew();
        void Guard() { token.ThrowIfCancellationRequested(); check(); if (clock.Elapsed > TimeSpan.FromSeconds(30)) throw Invalid("Repaired input validation exceeded30seconds."); }
        void Phase(int phase, string text) => Console.Error.WriteLine($"repaired input {phase}/5: {text} ({clock.Elapsed.TotalSeconds:F3}s)");
        var result = new ExperimentData { Root = Path.GetFullPath(directory) };
        try
        {
            Phase(1, "manifest, final records and provenance");
            string[] names = ["experiment-manifest.json", "experiment-records.jsonl", "input-provenance.json", "source-catalog.json", "teacher-requests.json", "teacher-responses.json", "derivation-ledger.json"];
            for (var index = 0; index < names.Length; index++)
            {
                Guard(); var path = Path.Combine(result.Root, names[index]); CheckPath(path, Guard);
                result._leases[index] = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            var bytes = Snapshot(result._leases[0]!, 1_048_576, Guard); result.ManifestSha256 = Hash(bytes);
            if (!SameHash(result.ManifestSha256, expectedManifestSha256)) throw Invalid("Repaired pinned manifest SHA differs.");
            CheckJson(bytes, 1_048_576, token);
            result.Manifest = JsonSerializer.Deserialize(bytes, ExperimentJsonContext.Default.ExperimentManifest) ?? throw Invalid("Repaired manifest is null.");
            ValidateRepairedManifest(result.Manifest);
            byte[] Asset(int lease, ExperimentAsset declared)
            {
                var snapshot = Snapshot(result._leases[lease]!, ExpandedRecordsLimit, Guard);
                if (declared.Bytes != snapshot.LongLength || !SameHash(declared.Sha256, Hash(snapshot))) throw Invalid("Repaired asset hash/bytes differ: " + names[lease]);
                return snapshot;
            }
            bytes = Asset(1, result.Manifest.Records); result.RecordsSha256 = Hash(bytes);
            result.Records = ParseRepairedRecords(bytes, Guard, token);
            bytes = Asset(2, result.Manifest.Provenance); result.ProvenanceSha256 = Hash(bytes);
            CheckJson(bytes, ExpandedRecordsLimit, token);
            using (var provenance = JsonDocument.Parse(bytes)) VerifyRepairedProvenance(provenance.RootElement, result.Records, Guard);
            bytes = Asset(6, result.Manifest.Derivation!); result.DerivationLedgerSha256 = Hash(bytes);
            CheckJson(bytes, ExpandedRecordsLimit, token);
            using var ledgerDocument = JsonDocument.Parse(bytes);
            Phase(2, "frozen256 parent and complete derivation ledger");
            var parents = ReadRepairedParents(result.Root, Guard, token);
            var ledger = VerifyRepairedLedger(ledgerDocument.RootElement, parents, result.Records, Guard);
            var catalog = Asset(3, result.Manifest.Sources.Single(source => source.Kind == "source_catalog"));
            var requests = Asset(4, result.Manifest.Sources.Single(source => source.Kind == "teacher_requests"));
            var responses = Asset(5, result.Manifest.Sources.Single(source => source.Kind == "teacher_responses"));
            CheckJson(catalog, ExpandedRecordsLimit, token); CheckJson(requests, ExpandedRecordsLimit, token); CheckJson(responses, ExpandedRecordsLimit, token);
            Phase(3, "preserved source catalog and parent aggregate scope");
            VerifyRepairedCatalog(catalog, parents, result.Records, Guard);
            Phase(4, "original33 audits and repaired7 blind reaudits");
            VerifyRepairedTeacher(requests, responses, ledger, result.Records, Guard, token);
            Phase(5, "repair14 raw pairs, proposals and final label agreement");
            VerifyRepairCompanions(result.Root, ledger, result.Records, Guard, token);
            Guard(); Console.Error.WriteLine($"repaired input complete: 215 final / 256 parent ({clock.Elapsed.TotalSeconds:F3}s)"); return result;
        }
        catch (Exception primary)
        {
            try { result.Dispose(); } catch (Exception cleanup) { throw new AggregateException("Repaired input acquisition/cleanup failed.", primary, cleanup); }
            throw;
        }
    }

    internal static ExperimentRecord[] ParseRepairedRecords(byte[] bytes, Action check, CancellationToken token)
    {
        if (bytes.Length is < 1 or > ExpandedRecordsLimit) throw Invalid("Repaired records exceed the byte bound.");
        var clock = Stopwatch.StartNew();
        void Guard() { token.ThrowIfCancellationRequested(); check(); if (clock.Elapsed > TimeSpan.FromSeconds(30)) throw Invalid("Repaired record parsing exceeded30seconds."); }
        using var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes));
        var records = new List<ExperimentRecord>(RepairedTotal); var ids = new HashSet<string>(StringComparer.Ordinal);
        var families = new Dictionary<string, string>(StringComparer.Ordinal); var canonical = new HashSet<string>(StringComparer.Ordinal);
        for (var line = 0; line <= RepairedTotal; line++)
        {
            Guard(); var text = reader.ReadLine(); if (text is null) break;
            if (line == RepairedTotal || string.IsNullOrWhiteSpace(text)) throw Invalid("Exactly215 nonempty repaired records are required.");
            var utf8 = Encoding.UTF8.GetBytes(text); CheckJson(utf8, 65_536, token);
            var row = JsonSerializer.Deserialize(utf8, ExperimentJsonContext.Default.ExperimentRecordJson) ?? throw Invalid("Repaired row is null.");
            if (string.IsNullOrWhiteSpace(row.RecordId) || row.RecordId.Length > 128 || !ids.Add(row.RecordId) || string.IsNullOrWhiteSpace(row.FamilyId) || row.FamilyId.Length > 128 ||
                row.Language is not ("zh" or "en") || row.Split is not ("train" or "development")) throw Invalid("Repaired row identity/language/split differs.");
            if (families.TryGetValue(row.FamilyId, out var priorSplit) && priorSplit != row.Split) throw Invalid("Repaired source family leaks across splits.");
            families[row.FamilyId] = row.Split;
            var binding = row.TeacherBinding ?? throw Invalid("Repaired row requires a real archived API teacher binding."); ValidateBinding(binding, false);
            if (!canonical.Add(binding.SourceCanonicalId)) throw Invalid("Repaired canonical source is repeated.");
            var request = IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes(row.Request.GetRawText()));
            if (request.SchemaVersion != 1 || request.ModelId != IndependentModelLoader.ModelId || request.Questions.Count != 1) throw Invalid("Repaired row requires one fixed-model decision-v1 question.");
            var question = request.Questions[0]; var candidateIds = RepairedCandidateIds(question);
            var type = question is IndependentChoiceQuestion ? MarkerQuestionType.Choice : MarkerQuestionType.Boolean;
            if ((uint)row.TargetIndex >= (uint)candidateIds.Length || binding.TargetIndex != row.TargetIndex || binding.SelectedCandidateId != candidateIds[row.TargetIndex])
                throw Invalid("Repaired teacher target/candidate mapping differs.");
            records.Add(new(row.RecordId, row.FamilyId, row.Language, row.Split, row.TargetIndex, request, question, type, candidateIds, binding, row.Request.Clone()));
        }
        if (records.Count != RepairedTotal || records.Count(row => row.Split == "train") != RepairedTrain || records.Count(row => row.Split == "development") != RepairedDevelopment ||
            records.Count(row => row.Type == MarkerQuestionType.Choice) != 140 || records.Count(row => row.Type == MarkerQuestionType.Boolean) != 75 ||
            records.Count(row => row.Split == "train" && row.Type == MarkerQuestionType.Choice) < 16) throw Invalid("Repaired actual split/type/rotation denominators differ.");
        return records.ToArray();
    }

    private static string[] RepairedCandidateIds(IndependentDecisionQuestion question) => question switch
    {
        IndependentChoiceQuestion choice when choice.Candidates.Count is >= 2 and <= 8 => choice.Candidates.Select(candidate => candidate.Id).ToArray(),
        IndependentBooleanQuestion => ["false", "true"], _ => throw Invalid("Repaired v3 adds Choice/Boolean supervision only.")
    };
    private static JsonElement RepairedRows(JsonElement value, string name, int count)
    {
        var rows = value.GetProperty(name);
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != count) throw Invalid("Repaired " + name + " denominator differs.");
        return rows;
    }
    private static void VerifyRepairedProvenance(JsonElement value, ExperimentRecord[] records, Action check)
    {
        var rows = RepairedRows(value, "records", RepairedTotal);
        for (var index = 0; index < RepairedTotal; index++)
        {
            check(); var row = rows[index]; var record = records[index];
            if (row.GetProperty("record_id").GetString() != record.RecordId || row.GetProperty("family_id").GetString() != record.FamilyId ||
                row.GetProperty("split").GetString() != record.Split || row.GetProperty("target_index").GetInt32() != record.TargetIndex ||
                row.GetProperty("selected_candidate_id").GetString() != record.CandidateIds[record.TargetIndex] || ReadBinding(row.GetProperty("teacher_binding")) != record.TeacherBinding ||
                !row.GetProperty("candidate_ids").EnumerateArray().Select(item => item.GetString()).SequenceEqual(record.CandidateIds, StringComparer.Ordinal))
                throw Invalid("Repaired provenance/final teacher mapping differs.");
        }
    }
    private static byte[] RepairedCompanion(string root, string relative, int maximum, Action check)
    {
        check(); var path = Path.Combine(root, relative); CheckPath(path, check);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Snapshot(file, maximum, check);
    }
    private static Dictionary<string, JsonElement> ReadRepairedParents(string root, Action check, CancellationToken token)
    {
        var bytes = RepairedCompanion(root, "parent-records.jsonl", ExpandedRecordsLimit, check);
        if (!SameHash(Hash(bytes), RepairedParentSha256)) throw Invalid("Repaired complete256 parent companion SHA differs.");
        using var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes));
        var rows = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var index = 0; index <= 256; index++)
        {
            check(); var line = reader.ReadLine(); if (line is null) break;
            if (index == 256 || string.IsNullOrWhiteSpace(line)) throw Invalid("Frozen parent requires exactly256 rows.");
            var utf8 = Encoding.UTF8.GetBytes(line); CheckJson(utf8, 65_536, token); using var document = JsonDocument.Parse(utf8);
            var row = document.RootElement.Clone();
            if (!rows.TryAdd(row.GetProperty("record_id").GetString()!, row)) throw Invalid("Frozen parent row ID is repeated.");
        }
        if (rows.Count != 256) throw Invalid("Frozen parent requires its complete256 denominator.");
        return rows;
    }
    private static Dictionary<string, JsonElement> VerifyRepairedLedger(JsonElement value, Dictionary<string, JsonElement> parents, ExperimentRecord[] records, Action check)
    {
        if (value.GetProperty("contract").GetString() != "sezika/repair-deletion-ledger/v1" || value.GetProperty("original_total").GetInt32() != 256 ||
            value.GetProperty("retained").GetInt32() != RepairedTotal || value.GetProperty("retained_unchanged").GetInt32() != 178 || value.GetProperty("repaired").GetInt32() != 37 ||
            value.GetProperty("deleted").GetInt32() != 41 || value.GetProperty("formal").GetBoolean() || value.GetProperty("human_review").GetString() != "pending" ||
            !SameHash(value.GetProperty("source_sha256").GetString(), RepairedParentSha256)) throw Invalid("Repaired ledger contract/count/source identity differs.");
        var rows = RepairedRows(value, "records", 256); var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var final = records.ToDictionary(row => row.RecordId, StringComparer.Ordinal); var unchanged = 0; var repaired = 0; var deleted = 0;
        for (var index = 0; index < 256; index++)
        {
            check(); var row = rows[index]; var id = row.GetProperty("record_id").GetString()!;
            if (!result.TryAdd(id, row) || !parents.TryGetValue(id, out var parent) || row.GetProperty("source_canonical_id").GetString() != parent.GetProperty("source_canonical_id").GetString() ||
                row.GetProperty("family_id").GetString() != parent.GetProperty("family_id").GetString() || row.GetProperty("language").GetString() != parent.GetProperty("language").GetString() ||
                row.GetProperty("split").GetString() != parent.GetProperty("split").GetString() || row.GetProperty("source_gold_index").GetInt32() != parent.GetProperty("target_index").GetInt32() ||
                !JsonElement.DeepEquals(row.GetProperty("original_request"), parent.GetProperty("request")) || row.GetProperty("formal").GetBoolean() ||
                row.GetProperty("human_review").GetString() != "pending" || string.IsNullOrWhiteSpace(row.GetProperty("reason").GetString()))
                throw Invalid("Repaired ledger changes original source/gold/family/split/request identity.");
            var action = row.GetProperty("action").GetString(); var revised = row.GetProperty("revised_request");
            if (action == "deleted")
            {
                deleted++; if (final.ContainsKey(id) || revised.ValueKind != JsonValueKind.Null) throw Invalid("Deleted ledger rows entered the final experiment.");
                if (!IsRootExcludedRepair(row))
                {
                    RequireHash(row.GetProperty("repair_request_sha256").GetString()); RequireHash(row.GetProperty("repair_response_sha256").GetString());
                }
                if (row.GetProperty("reaudit_request_sha256").ValueKind != JsonValueKind.Null || row.GetProperty("reaudit_response_sha256").ValueKind != JsonValueKind.Null) throw Invalid("Deleted rows cannot declare accepted reaudits.");
                continue;
            }
            if (!final.TryGetValue(id, out var record) || record.FamilyId != row.GetProperty("family_id").GetString() || record.Language != row.GetProperty("language").GetString() ||
                record.Split != row.GetProperty("split").GetString() || record.TeacherBinding!.SourceCanonicalId != row.GetProperty("source_canonical_id").GetString()) throw Invalid("Retained ledger/final identity differs.");
            var original = row.GetProperty("original_request");
            if (action == "retained_unchanged")
            {
                unchanged++;
                if (revised.ValueKind != JsonValueKind.Null || !JsonElement.DeepEquals(original, record.OriginalRequestJson!.Value) ||
                    row.GetProperty("repair_request_sha256").ValueKind != JsonValueKind.Null || row.GetProperty("repair_response_sha256").ValueKind != JsonValueKind.Null ||
                    row.GetProperty("reaudit_request_sha256").ValueKind != JsonValueKind.Null || row.GetProperty("reaudit_response_sha256").ValueKind != JsonValueKind.Null)
                    throw Invalid("Unchanged ledger rows declare modifications or changed requests.");
            }
            else if (action == "repaired_and_reaudited")
            {
                repaired++; RequireHash(row.GetProperty("repair_request_sha256").GetString()); RequireHash(row.GetProperty("repair_response_sha256").GetString());
                if (!JsonElement.DeepEquals(revised, record.OriginalRequestJson!.Value) || !JsonElement.DeepEquals(original.GetProperty("state"), revised.GetProperty("state")) ||
                    original.GetProperty("schema_version").GetInt32() != revised.GetProperty("schema_version").GetInt32() || original.GetProperty("model_id").GetString() != revised.GetProperty("model_id").GetString() ||
                    original.GetProperty("questions")[0].GetProperty("id").GetString() != record.Question.Id || original.GetProperty("questions")[0].GetProperty("type").GetString() != revised.GetProperty("questions")[0].GetProperty("type").GetString() ||
                    !SameHash(row.GetProperty("reaudit_request_sha256").GetString(), record.TeacherBinding.RequestSha256) || !SameHash(row.GetProperty("reaudit_response_sha256").GetString(), record.TeacherBinding.ResponseSha256))
                    throw Invalid("Repaired request changes source state/type or lacks its independent blind reaudit binding.");
            }
            else throw Invalid("Unknown repaired ledger action.");
        }
        if (unchanged != 178 || repaired != 37 || deleted != 41) throw Invalid("Repaired ledger action counts differ.");
        return result;
    }
    private static void VerifyRepairedCatalog(byte[] bytes, Dictionary<string, JsonElement> parents, ExperimentRecord[] records, Action check)
    {
        using var document = JsonDocument.Parse(bytes); var value = document.RootElement;
        // These inherited catalog aggregates describe the immutable parent, not the final215 rows.
        if (value.GetProperty("contract").GetString() != "sezika/public-source-teacher-expansion/v1" || value.GetProperty("record_count").GetInt32() != 256 ||
            value.GetProperty("split_counts").GetProperty("train").GetInt32() != 192 || value.GetProperty("split_counts").GetProperty("development").GetInt32() != 64 ||
            !SameHash(value.GetProperty("prepared_sha256").GetString(), RepairedParentSha256) || value.GetProperty("formal").GetBoolean() ||
            value.GetProperty("human_review").GetString() != "pending" || value.GetProperty("eligible_as_sealed_test").GetBoolean() || value.GetProperty("score_new_supervision").GetInt32() != 0 ||
            value.GetProperty("old32_included").GetBoolean() || !value.GetProperty("old32_regression_is_separate").GetBoolean()) throw Invalid("Repaired catalog parent/source scope differs.");
        var rows = RepairedRows(value, "records", RepairedTotal); var byId = records.ToDictionary(row => row.RecordId, StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < RepairedTotal; index++)
        {
            check(); var row = rows[index]; var id = row.GetProperty("record_id").GetString()!;
            if (!seen.Add(id) || !byId.TryGetValue(id, out var record) || !parents.TryGetValue(id, out var parent) || row.GetProperty("source_canonical_id").GetString() != record.TeacherBinding!.SourceCanonicalId ||
                row.GetProperty("family_id").GetString() != record.FamilyId || row.GetProperty("language").GetString() != record.Language || row.GetProperty("split").GetString() != record.Split ||
                !JsonElement.DeepEquals(row.GetProperty("source"), parent.GetProperty("source"))) throw Invalid("Repaired catalog changes preserved original source metadata.");
        }
    }

    private static JsonElement ArchivedUser(JsonElement value, Action check, CancellationToken token)
    {
        if (value.GetProperty("model").GetString() != "gpt-6.1-sol" || value.GetProperty("reasoning").GetProperty("effort").GetString() != "max" ||
            value.GetProperty("store").GetBoolean() || value.GetProperty("stream").GetBoolean()) throw Invalid("Repaired archived request model/effort/store/stream differs.");
        var input = value.GetProperty("input"); if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() is < 1 or > 4) throw Invalid("Archived request input bound differs.");
        string? text = null;
        for (var index = 0; index < input.GetArrayLength() && index < 4; index++)
        {
            check(); var message = input[index]; if (message.GetProperty("role").GetString() != "user") continue;
            var parts = message.GetProperty("content");
            if (parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() != 1 || parts[0].GetProperty("type").GetString() != "input_text" || text is not null) throw Invalid("Archived request user payload differs.");
            text = parts[0].GetProperty("text").GetString();
        }
        if (text is null) throw Invalid("Archived request user payload is absent.");
        var bytes = Encoding.UTF8.GetBytes(text); CheckJson(bytes, 1_048_576, token); using var document = JsonDocument.Parse(bytes); return document.RootElement.Clone();
    }
    private static JsonElement ArchivedAnswer(JsonElement value, string? expectedId, Action check, CancellationToken token)
    {
        if (value.GetProperty("model").GetString() != "gpt-6.1-sol" || value.GetProperty("status").GetString() != "completed" ||
            string.IsNullOrWhiteSpace(value.GetProperty("id").GetString()) || (expectedId is not null && value.GetProperty("id").GetString() != expectedId)) throw Invalid("Archived response gateway identity/status differs.");
        if (value.TryGetProperty("reasoning", out var reasoning) && reasoning.TryGetProperty("effort", out var effort) && effort.ValueKind != JsonValueKind.Null && effort.GetString() != "max") throw Invalid("Archived response effort differs.");
        var outputs = value.GetProperty("output"); if (outputs.ValueKind != JsonValueKind.Array || outputs.GetArrayLength() > 50) throw Invalid("Archived response output bound differs.");
        string? text = null;
        for (var index = 0; index < outputs.GetArrayLength() && index < 50; index++)
        {
            check(); var output = outputs[index]; if (output.GetProperty("type").GetString() != "message") continue;
            var parts = output.GetProperty("content"); if (parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() > 20) throw Invalid("Archived response content bound differs.");
            for (var part = 0; part < parts.GetArrayLength() && part < 20; part++)
            {
                check(); if (parts[part].GetProperty("type").GetString() != "output_text") continue;
                if (text is not null) throw Invalid("Archived response contains repeated output_text."); text = parts[part].GetProperty("text").GetString();
            }
        }
        if (text is null) throw Invalid("Archived response output_text is absent.");
        var bytes = Encoding.UTF8.GetBytes(text); CheckJson(bytes, 1_048_576, token); using var document = JsonDocument.Parse(bytes); return document.RootElement.Clone();
    }
    private static void VerifyRepairedTeacher(byte[] requests, byte[] responses, Dictionary<string, JsonElement> ledger, ExperimentRecord[] records, Action check, CancellationToken token)
    {
        using var requestDocument = JsonDocument.Parse(requests); var batches = RepairedRows(requestDocument.RootElement, "batches", 40);
        var requestRows = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.OrdinalIgnoreCase); var originalIds = new HashSet<string>(StringComparer.Ordinal); var reauditIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 40; index++)
        {
            check(); var batch = batches[index]; var bytes = Convert.FromBase64String(batch.GetProperty("bytes_base64").GetString()!); var hash = batch.GetProperty("sha256").GetString()!;
            if (bytes.Length is < 1 or > 2_097_152 || !SameHash(Hash(bytes), hash)) throw Invalid("Repaired audit request wire hash/bytes differ.");
            CheckJson(bytes, 2_097_152, token); using var document = JsonDocument.Parse(bytes); var user = ArchivedUser(document.RootElement, check, token);
            var isOriginal = index < 33;
            if (isOriginal ? user.GetProperty("audit_contract").GetString() != "sezika/independent-ai-label-audit/v1" : user.GetProperty("contract").GetString() != "sezika/repair-or-delete/v1") throw Invalid("Archived original/reaudit request contract differs.");
            var sourceRows = user.GetProperty("rows"); if (sourceRows.ValueKind != JsonValueKind.Array || sourceRows.GetArrayLength() is < 1 or > 8) throw Invalid("Repaired audit request row bound differs.");
            var selected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            for (var rowIndex = 0; rowIndex < sourceRows.GetArrayLength() && rowIndex < 8; rowIndex++)
            {
                check(); var source = sourceRows[rowIndex]; var id = source.GetProperty("record_id").GetString()!;
                if (!selected.TryAdd(id, source) || !ledger.TryGetValue(id, out var original) || source.GetProperty("language").GetString() != original.GetProperty("language").GetString() ||
                    source.TryGetProperty("target_index", out _) || source.TryGetProperty("source_gold_index", out _) || source.TryGetProperty("teacher_binding", out _) ||
                    !JsonElement.DeepEquals(source.GetProperty("request"), original.GetProperty(isOriginal ? "original_request" : "revised_request"))) throw Invalid("Archived blind request/source mapping differs.");
                if (isOriginal ? !originalIds.Add(id) : !reauditIds.Add(id) || original.GetProperty("action").GetString() != "repaired_and_reaudited" || !SameHash(original.GetProperty("reaudit_request_sha256").GetString(), hash))
                    throw Invalid("Archived original/reaudit membership differs.");
                var parsed = IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes(source.GetProperty("request").GetRawText())); var candidates = RepairedCandidateIds(parsed.Questions[0]);
                if (!source.GetProperty("candidate_index_to_id").EnumerateArray().Select(item => item.GetString()).SequenceEqual(candidates, StringComparer.Ordinal)) throw Invalid("Archived candidate order differs.");
            }
            if (!requestRows.TryAdd(hash, selected)) throw Invalid("Archived audit request hash is repeated.");
        }
        if (originalIds.Count != 256 || reauditIds.Count != 37) throw Invalid("Archived original256/repaired37 request coverage differs.");
        using var responseDocument = JsonDocument.Parse(responses); batches = RepairedRows(responseDocument.RootElement, "batches", 40);
        var byId = records.ToDictionary(row => row.RecordId, StringComparer.Ordinal); var finalVerified = new HashSet<string>(StringComparer.Ordinal);
        var seenResponseIds = new HashSet<string>(StringComparer.Ordinal); var seenRequestHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < 40; index++)
        {
            check(); var batch = batches[index]; var requestHash = batch.GetProperty("request_sha256").GetString()!; var responseHash = batch.GetProperty("sha256").GetString()!; var responseId = batch.GetProperty("response_id").GetString()!;
            var bytes = Convert.FromBase64String(batch.GetProperty("bytes_base64").GetString()!);
            if (bytes.Length is < 1 or > 2_097_152 || !SameHash(Hash(bytes), responseHash) || !seenResponseIds.Add(responseId) || !seenRequestHashes.Add(requestHash) || !requestRows.TryGetValue(requestHash, out var selected)) throw Invalid("Archived audit response request/hash/ID differs.");
            CheckJson(bytes, 2_097_152, token); using var document = JsonDocument.Parse(bytes); var answer = ArchivedAnswer(document.RootElement, responseId, check, token); var reviews = RepairedRows(answer, "reviews", selected.Count); var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var reviewIndex = 0; reviewIndex < selected.Count && reviewIndex < 8; reviewIndex++)
            {
                check(); var review = reviews[reviewIndex]; var id = review.GetProperty("record_id").GetString()!;
                if (!seen.Add(id) || !selected.TryGetValue(id, out var source) || review.GetProperty("question_id").GetString() != source.GetProperty("request").GetProperty("questions")[0].GetProperty("id").GetString()) throw Invalid("Archived audit review/request row differs.");
                if (!byId.TryGetValue(id, out var record) || !SameHash(record.TeacherBinding!.ResponseSha256, responseHash)) continue;
                var binding = record.TeacherBinding!;
                if (!finalVerified.Add(id) || binding.ResponseId != responseId || !SameHash(binding.RequestSha256, requestHash) || review.GetProperty("clarity_status").GetString() != "clear" ||
                    review.GetProperty("answer_status").GetString() != "determinate" || review.GetProperty("question_revision_needed").GetBoolean() ||
                    !review.GetProperty("suggested_correct_index").TryGetInt32(out var target) || target != record.TargetIndex || review.GetProperty("suggested_correct_id").GetString() != binding.SelectedCandidateId ||
                    !JsonElement.DeepEquals(source.GetProperty("request"), record.OriginalRequestJson!.Value)) throw Invalid("Final repaired target is not its actual clear determinate archived answer.");
            }
        }
        if (finalVerified.Count != RepairedTotal || seenRequestHashes.Count != 40) throw Invalid("All215 final records require their exact archived teacher answer.");
    }

    private static void VerifyRepairCompanions(string root, Dictionary<string, JsonElement> ledger, ExperimentRecord[] records, Action check, CancellationToken token)
    {
        var final = records.ToDictionary(row => row.RecordId, StringComparer.Ordinal);
        var verified = new HashSet<string>(StringComparer.Ordinal); var requestHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var responseHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var responseIds = new HashSet<string>(StringComparer.Ordinal);
        var repairProposals = 0; var deleteProposals = 0; var excludedRepairs = 0;
        for (var index = 0; index < 14; index++)
        {
            check(); var prefix = Path.Combine("repair-evidence", "repair" + index.ToString("00", CultureInfo.InvariantCulture));
            var requestBytes = RepairedCompanion(root, prefix + ".request.json", 2_097_152, check); var responseBytes = RepairedCompanion(root, prefix + ".response.json", 2_097_152, check);
            var requestHash = Hash(requestBytes); var responseHash = Hash(responseBytes);
            if (!requestHashes.Add(requestHash) || !responseHashes.Add(responseHash)) throw Invalid("Repair companion wire identity is repeated.");
            CheckJson(requestBytes, 2_097_152, token); CheckJson(responseBytes, 2_097_152, token);
            using var requestDocument = JsonDocument.Parse(requestBytes); using var responseDocument = JsonDocument.Parse(responseBytes);
            var user = ArchivedUser(requestDocument.RootElement, check, token); var answer = ArchivedAnswer(responseDocument.RootElement, null, check, token);
            if (!responseIds.Add(responseDocument.RootElement.GetProperty("id").GetString()!)) throw Invalid("Repair companion response ID is repeated.");
            if (user.GetProperty("contract").GetString() != "sezika/repair-or-delete/v1") throw Invalid("Repair companion request contract differs.");
            var sourceRows = user.GetProperty("rows"); if (sourceRows.ValueKind != JsonValueKind.Array || sourceRows.GetArrayLength() is < 1 or > 8) throw Invalid("Repair companion row bound differs.");
            var selected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            for (var rowIndex = 0; rowIndex < sourceRows.GetArrayLength() && rowIndex < 8; rowIndex++)
            {
                check(); var source = sourceRows[rowIndex]; var id = source.GetProperty("record_id").GetString()!;
                if (!selected.TryAdd(id, source) || !ledger.TryGetValue(id, out var row) || row.GetProperty("action").GetString() == "retained_unchanged" ||
                    !RepairWireMatches(row, requestHash, responseHash) ||
                    source.TryGetProperty("target_index", out _) || source.TryGetProperty("source_gold_index", out _) || source.TryGetProperty("teacher_binding", out _) ||
                    source.GetProperty("language").GetString() != row.GetProperty("language").GetString() || !JsonElement.DeepEquals(source.GetProperty("request"), row.GetProperty("original_request"))) throw Invalid("Repair companion does not match the ledger/original request.");
            }
            var decisions = RepairedRows(answer, "decisions", selected.Count);
            for (var decisionIndex = 0; decisionIndex < selected.Count && decisionIndex < 8; decisionIndex++)
            {
                check(); var proposal = decisions[decisionIndex]; var id = proposal.GetProperty("record_id").GetString()!;
                if (!verified.Add(id) || !selected.ContainsKey(id)) throw Invalid("Repair companion proposal ID differs or repeats.");
                var row = ledger[id]; var decision = proposal.GetProperty("decision").GetString();
                if (decision == "delete") { deleteProposals++; if (row.GetProperty("action").GetString() != "deleted") throw Invalid("Teacher deletion proposal was retained."); }
                else if (decision == "repair")
                {
                    repairProposals++;
                    if (row.GetProperty("action").GetString() == "deleted")
                    {
                        if (!IsRootExcludedRepair(row)) throw Invalid("Only the two frozen semantic exclusions may override a repair proposal.");
                        excludedRepairs++; continue;
                    }
                    var question = row.GetProperty("revised_request").GetProperty("questions")[0];
                    if (question.GetProperty("instruction").GetString() != proposal.GetProperty("instruction").GetString()) throw Invalid("Repaired instruction differs from its proposal.");
                    if (question.GetProperty("type").GetString() == "boolean")
                    {
                        if (question.GetProperty("statement").GetString() != proposal.GetProperty("statement").GetString()) throw Invalid("Repaired Boolean statement differs from its proposal.");
                    }
                    else
                    {
                        var candidates = question.GetProperty("candidates"); var proposed = proposal.GetProperty("candidates");
                        if (candidates.GetArrayLength() is < 2 or > 8 || candidates.GetArrayLength() != proposed.GetArrayLength()) throw Invalid("Repaired Choice proposal candidate denominator differs.");
                        var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                        for (var candidateIndex = 0; candidateIndex < proposed.GetArrayLength() && candidateIndex < 8; candidateIndex++)
                        {
                            check(); var candidate = proposed[candidateIndex]; if (!byId.TryAdd(candidate.GetProperty("id").GetString()!, candidate)) throw Invalid("Repair proposal candidate is repeated.");
                        }
                        for (var candidateIndex = 0; candidateIndex < candidates.GetArrayLength() && candidateIndex < 8; candidateIndex++)
                        {
                            check(); var candidate = candidates[candidateIndex]; if (!byId.TryGetValue(candidate.GetProperty("id").GetString()!, out var proposedCandidate) || !JsonElement.DeepEquals(candidate, proposedCandidate)) throw Invalid("Repaired Choice candidate differs from the proposal permutation.");
                        }
                    }
                    if (!proposal.GetProperty("suggested_correct_index").TryGetInt32(out var proposalIndex) || !final.TryGetValue(id, out var record)) throw Invalid("Retained repair proposal target is absent.");
                    var proposalId = proposal.GetProperty("suggested_correct_id").GetString();
                    var proposedIds = question.GetProperty("type").GetString() == "boolean" ? new[] { "false", "true" } :
                        proposal.GetProperty("candidates").EnumerateArray().Select(candidate => candidate.GetProperty("id").GetString()).ToArray();
                    if ((uint)proposalIndex >= (uint)proposedIds.Length || proposedIds[proposalIndex] != proposalId || record.CandidateIds[record.TargetIndex] != proposalId)
                        throw Invalid("Retained repair proposal target differs from its independent blind reaudit after candidate permutation.");
                }
                else throw Invalid("Unknown repair companion decision.");
            }
        }
        if (verified.Count != 78 || repairProposals != 39 || deleteProposals != 39 || excludedRepairs != 2) throw Invalid("Repair companion78/39/39/2 derivation denominator differs.");
    }

    private static bool IsRootExcludedRepair(JsonElement row)
    {
        // The immutable ledger deliberately cleared repair hashes for these two rejected semantic changes.
        // Their raw proposals are still required, separately pinned below, and cannot become final supervision.
        var reason = row.GetProperty("record_id").GetString() switch
        {
            "boolq-train-row-094" => "将角色是否有姓名替换成段落有没有记载其他姓名，改变原命题；原文未提及不能证明原问题的否定答案，按用户授权从新版本排除。",
            "boolq-validation-row-058" => "将组合是否存在改成电影内是否有虚构组合，预设了待辨别的现实/虚构范围并改变原问题命题，不能用此获得确定标签，按用户授权从新版本排除。",
            _ => null
        };
        return reason is not null && row.GetProperty("action").GetString() == "deleted" && row.GetProperty("reason").GetString() == reason &&
            row.GetProperty("repair_request_sha256").ValueKind == JsonValueKind.Null && row.GetProperty("repair_response_sha256").ValueKind == JsonValueKind.Null &&
            row.GetProperty("reaudit_request_sha256").ValueKind == JsonValueKind.Null && row.GetProperty("reaudit_response_sha256").ValueKind == JsonValueKind.Null;
    }
    private static bool RepairWireMatches(JsonElement row, string requestHash, string responseHash)
    {
        if (!IsRootExcludedRepair(row)) return SameHash(row.GetProperty("repair_request_sha256").GetString(), requestHash) && SameHash(row.GetProperty("repair_response_sha256").GetString(), responseHash);
        return row.GetProperty("record_id").GetString() switch
        {
            "boolq-train-row-094" => SameHash(requestHash, "B0EDD49555B03B11A1EF908D0A649DEBF79F12728FF0F6355EB6FEB4AE30DDC1") && SameHash(responseHash, "159DAB2492049C8F2587F838F49B911BB855EF1194E5255143C6B9D42C9BE5F4"),
            "boolq-validation-row-058" => SameHash(requestHash, "3AC8D0DF1A88229BCDE11631435E4CB82F017D62046CBF57FD903F9FF711AFD2") && SameHash(responseHash, "6053F102002BB2AD2A535BDF3DF527DB2860BB299AA849587276CA7170EEC549"),
            _ => false
        };
    }
}
