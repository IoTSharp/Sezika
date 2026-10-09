using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sezika;

namespace Sezika.ExperimentTrainer;

internal sealed partial class ExperimentData
{
    internal const string ExpandedContract = "sezika/teacher-labelled-experiment-data/v2";
    internal const int ExpandedRecordsLimit = 256 * 65_538 + 3;

    internal static void ValidateExpandedManifest(ExperimentManifest value, bool fixtureOnly = false)
    {
        if (value.SchemaVersion == 3) { if (fixtureOnly) throw Invalid("Repaired v3 cannot be a fixture-only teacher substitute."); ValidateRepairedManifest(value); return; }
        if (value.SchemaVersion != 2 || value.Contract != ExpandedContract || value.DatasetId != "general-zh-en-teacher-expansion-256-v1" ||
            value.Purpose != "teacher_labelled_experiment" || value.Protocol != "decision-v1" || value.StudentModelId != IndependentModelLoader.ModelId ||
            value.Formal || value.HumanReview != "pending" || value.TeacherOutputTrainingPermitted || !value.UserAuthorizedExperiment ||
            !value.TeacherAnswerExposed || value.EligibleAsSealedTest || value.Total != 256 || value.Train != 192 || value.Development != 64 ||
            value.SplitRule != "source-family-frozen-192-64-v1" || string.IsNullOrWhiteSpace(value.SplitRuleFrozenUtc) ||
            value.Teacher is not null || value.FixtureOnly != fixtureOnly || value.Sources is null || value.Sources.Length != 3 || value.Derivation is not null)
            throw Invalid("Only the explicitly exposed, pending 256/192/64 v2 expansion contract is accepted.");
        if (fixtureOnly)
        {
            if (value.TeacherApi is not null) throw Invalid("Fixture-only inputs must not declare a real API teacher.");
        }
        else if (value.TeacherApi is not { Provider: "sonnet_vip", Model: "gpt-6.1-sol", Effort: "max",
            Identity: "gateway-response-declaration-only", TrainingPermission: "user-authorized-experiment-only", License: "unknown" })
            throw Invalid("Expansion API teacher identity/permission/license declaration differs.");
        ValidateExpandedAsset(value.Records); ValidateExpandedAsset(value.Provenance);
        if (!DeclaredName(value.Records.Path, "experiment-records.jsonl") || !DeclaredName(value.Provenance.Path, "input-provenance.json"))
            throw Invalid("Expanded derived file names differ.");
        var kinds = new HashSet<string>(["source_catalog", "teacher_requests", "teacher_responses"], StringComparer.Ordinal);
        for (var index = 0; index < 3; index++)
        {
            var source = value.Sources[index]; ValidateExpandedAsset(source);
            var expected = source.Kind switch { "source_catalog" => "source-catalog.json", "teacher_requests" => "teacher-requests.json", "teacher_responses" => "teacher-responses.json", _ => "" };
            if (!kinds.Remove(source.Kind) || !DeclaredName(source.Path, expected)) throw Invalid("Expanded source kinds/names differ.");
        }
    }
    private static void ValidateExpandedAsset(ExperimentAsset value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Path) || value.Path.Length > 2048 || value.Bytes is < 1 or > ExpandedRecordsLimit)
            throw Invalid("Expanded asset path/byte identity is invalid.");
        RequireHash(value.Sha256);
    }
    internal static ExperimentData OpenExpanded(string directory, string expectedManifestSha256, Action check, CancellationToken token, bool fixtureOnly = false)
    {
        RequireHash(expectedManifestSha256);
        var manifestPath = Path.Combine(Path.GetFullPath(directory), "experiment-manifest.json"); CheckPath(manifestPath, check);
        bool repaired;
        using (var probe = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var pinned = Snapshot(probe, 1_048_576, check);
            if (!SameHash(Hash(pinned), expectedManifestSha256)) throw Invalid("Expanded pinned manifest probe hash differs.");
            CheckJson(pinned, 1_048_576, token); using var declaration = JsonDocument.Parse(pinned);
            repaired = declaration.RootElement.GetProperty("schema_version").GetInt32() == 3;
        }
        if (repaired)
        {
            if (fixtureOnly) throw Invalid("Repaired v3 cannot bypass real teacher/derivation verification.");
            return OpenRepaired(directory, expectedManifestSha256, check, token);
        }
        var result = new ExperimentData { Root = Path.GetFullPath(directory) };
        try
        {
            string[] names = ["experiment-manifest.json", "experiment-records.jsonl", "input-provenance.json", "source-catalog.json", "teacher-requests.json", "teacher-responses.json"];
            for (var index = 0; index < 6; index++)
            {
                check(); var path = Path.Combine(result.Root, names[index]); CheckPath(path, check);
                result._leases[index] = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            var bytes = Snapshot(result._leases[0]!, 1_048_576, check); result.ManifestSha256 = Hash(bytes);
            if (!SameHash(result.ManifestSha256, expectedManifestSha256)) throw Invalid("Expanded pinned manifest hash differs.");
            CheckJson(bytes, 1_048_576, token);
            result.Manifest = JsonSerializer.Deserialize(bytes, ExperimentJsonContext.Default.ExperimentManifest) ?? throw Invalid("Expanded manifest is null.");
            ValidateExpandedManifest(result.Manifest, fixtureOnly);
            bytes = Snapshot(result._leases[1]!, ExpandedRecordsLimit, check); result.RecordsSha256 = Hash(bytes);
            if (result.Manifest.Records.Bytes != bytes.LongLength || !SameHash(result.Manifest.Records.Sha256, result.RecordsSha256)) throw Invalid("Expanded records hash/bytes differ.");
            result.Records = ParseExpandedRecords(bytes, check, token, fixtureOnly);
            bytes = Snapshot(result._leases[2]!, 1_048_576, check); result.ProvenanceSha256 = Hash(bytes);
            if (result.Manifest.Provenance.Bytes != bytes.LongLength || !SameHash(result.Manifest.Provenance.Sha256, result.ProvenanceSha256)) throw Invalid("Expanded provenance hash/bytes differ.");
            CheckJson(bytes, 1_048_576, token);
            using (var provenance = JsonDocument.Parse(bytes))
            {
                var rows = provenance.RootElement.GetProperty("records");
                if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 256) throw Invalid("Expanded provenance requires all256 rows.");
                for (var index = 0; index < 256; index++)
                {
                    check(); var row = rows[index]; var record = result.Records[index];
                    if (row.GetProperty("record_id").GetString() != record.RecordId || row.GetProperty("family_id").GetString() != record.FamilyId ||
                        row.GetProperty("split").GetString() != record.Split || row.GetProperty("target_index").GetInt32() != record.TargetIndex ||
                        row.GetProperty("selected_candidate_id").GetString() != record.CandidateIds[record.TargetIndex]) throw Invalid("Expanded provenance row mapping differs.");
                    var ids = row.GetProperty("candidate_ids").EnumerateArray().Select(item => item.GetString()).ToArray();
                    if (!ids.SequenceEqual(record.CandidateIds, StringComparer.Ordinal) || ReadBinding(row.GetProperty("teacher_binding")) != record.TeacherBinding)
                        throw Invalid("Expanded provenance teacher binding differs.");
                }
            }
            var snapshots = new byte[3][];
            for (var index = 0; index < 3; index++)
            {
                check(); var declared = result.Manifest.Sources.Single(source => source.Kind == (index == 0 ? "source_catalog" : index == 1 ? "teacher_requests" : "teacher_responses"));
                snapshots[index] = Snapshot(result._leases[index + 3]!, ExpandedRecordsLimit, check);
                if (declared.Bytes != snapshots[index].LongLength || !SameHash(declared.Sha256, Hash(snapshots[index]))) throw Invalid("Expanded source snapshot hash/bytes differ.");
                CheckJson(snapshots[index], ExpandedRecordsLimit, token);
            }
            VerifyExpandedCatalog(snapshots[0], result.Records, check);
            if (!fixtureOnly) VerifyExpandedTeacher(snapshots[1], snapshots[2], result.Records, check, token);
            check(); return result;
        }
        catch (Exception primary)
        {
            try { result.Dispose(); } catch (Exception cleanup) { throw new AggregateException("Expanded acquisition/cleanup failed.", primary, cleanup); }
            throw;
        }
    }
    internal static ExperimentRecord[] ParseExpandedRecords(byte[] bytes, Action check, CancellationToken token, bool fixtureOnly = false)
    {
        if (bytes.Length is < 1 or > ExpandedRecordsLimit) throw Invalid("Expanded records byte bound differs.");
        using var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes));
        var records = new List<ExperimentRecord>(256); var ids = new HashSet<string>(StringComparer.Ordinal);
        var families = new Dictionary<string, string>(StringComparer.Ordinal); var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        var train = 0; var development = 0;
        for (var line = 0; line < 257; line++)
        {
            check(); var text = reader.ReadLine(); if (text is null) break;
            if (line == 256 || string.IsNullOrWhiteSpace(text)) throw Invalid("Exactly256 nonempty expanded records are required.");
            var utf8 = Encoding.UTF8.GetBytes(text); CheckJson(utf8, 65_536, token);
            var row = JsonSerializer.Deserialize(utf8, ExperimentJsonContext.Default.ExperimentRecordJson) ?? throw Invalid("Expanded row is null.");
            if (string.IsNullOrWhiteSpace(row.RecordId) || row.RecordId.Length > 128 || !ids.Add(row.RecordId) || string.IsNullOrWhiteSpace(row.FamilyId) || row.FamilyId.Length > 128 ||
                row.Language is not ("zh" or "en") || row.Split is not ("train" or "development")) throw Invalid("Expanded row identity/language/split differs.");
            if (families.TryGetValue(row.FamilyId, out var priorSplit) && priorSplit != row.Split) throw Invalid("Source family leaks across frozen splits.");
            families[row.FamilyId] = row.Split;
            var binding = row.TeacherBinding ?? throw Invalid("Expanded row requires an explicit teacher binding."); ValidateBinding(binding, fixtureOnly);
            if (canonical.TryGetValue(binding.SourceCanonicalId, out var priorFamily) && priorFamily != row.FamilyId) throw Invalid("Canonical source is assigned to multiple families.");
            canonical[binding.SourceCanonicalId] = row.FamilyId;
            var request = IndependentDecisionV1Parser.Parse(Encoding.UTF8.GetBytes(row.Request.GetRawText()));
            if (request.SchemaVersion != 1 || request.ModelId != IndependentModelLoader.ModelId || request.Questions.Count != 1) throw Invalid("Expanded row requires one fixed-model decision-v1 question.");
            var question = request.Questions[0];
            string[] candidateIds = question switch
            {
                IndependentChoiceQuestion choice => choice.Candidates.Select(candidate => candidate.Id).ToArray(),
                IndependentScoreQuestion score => Enumerable.Range(0, score.Levels.Count).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray(),
                IndependentBooleanQuestion => ["false", "true"], _ => throw Invalid("Unsupported expanded question.")
            };
            var type = question switch { IndependentChoiceQuestion => MarkerQuestionType.Choice, IndependentScoreQuestion => MarkerQuestionType.Score, _ => MarkerQuestionType.Boolean };
            if ((type == MarkerQuestionType.Choice ? candidateIds.Length is < 2 or > 8 : candidateIds.Length != (type == MarkerQuestionType.Score ? 5 : 2)) ||
                (uint)row.TargetIndex >= (uint)candidateIds.Length || binding.TargetIndex != row.TargetIndex || binding.SelectedCandidateId != candidateIds[row.TargetIndex])
                throw Invalid("Expanded teacher target/candidate mapping differs.");
            if (row.Split == "train") train++; else development++;
            records.Add(new(row.RecordId, row.FamilyId, row.Language, row.Split, row.TargetIndex, request, question, type, candidateIds, binding, row.Request.Clone()));
        }
        if (records.Count != 256 || train != 192 || development != 64 || records.Count(row => row.Split == "train" && row.Type == MarkerQuestionType.Choice) < 16)
            throw Invalid("Expanded split/rotation denominator differs.");
        return records.ToArray();
    }
    private static ExperimentTeacherBinding ReadBinding(JsonElement value) => new(value.GetProperty("source_canonical_id").GetString()!, value.GetProperty("response_id").GetString()!,
        value.GetProperty("request_sha256").GetString()!, value.GetProperty("response_sha256").GetString()!, value.GetProperty("selected_candidate_id").GetString()!, value.GetProperty("target_index").GetInt32());
    private static void ValidateBinding(ExperimentTeacherBinding value, bool fixtureOnly)
    {
        if (string.IsNullOrWhiteSpace(value.SourceCanonicalId) || value.SourceCanonicalId.Length > 256 || string.IsNullOrWhiteSpace(value.ResponseId) || value.ResponseId.Length > 256 ||
            string.IsNullOrWhiteSpace(value.SelectedCandidateId) || value.TargetIndex is < 0 or > 7 || (fixtureOnly ? value.ResponseId != "fixture-only" : value.ResponseId == "fixture-only"))
            throw Invalid("Expanded teacher binding identity is invalid.");
        RequireHash(value.RequestSha256); RequireHash(value.ResponseSha256);
    }
    private static void VerifyExpandedCatalog(byte[] bytes, ExperimentRecord[] records, Action check)
    {
        using var document = JsonDocument.Parse(bytes); var rows = document.RootElement.GetProperty("records");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 256) throw Invalid("Expanded source catalog denominator differs.");
        var seen = new HashSet<string>(StringComparer.Ordinal); var byId = records.ToDictionary(row => row.RecordId, StringComparer.Ordinal);
        for (var index = 0; index < 256; index++)
        {
            check(); var row = rows[index]; var id = row.GetProperty("record_id").GetString()!;
            if (!seen.Add(id) || !byId.TryGetValue(id, out var source) || row.GetProperty("source_canonical_id").GetString() != source.TeacherBinding!.SourceCanonicalId ||
                row.GetProperty("family_id").GetString() != source.FamilyId || row.GetProperty("language").GetString() != source.Language || row.GetProperty("split").GetString() != source.Split)
                throw Invalid("Expanded catalog/source family identity differs.");
        }
    }
    private static void VerifyExpandedTeacher(byte[] requests, byte[] responses, ExperimentRecord[] records, Action check, CancellationToken token)
    {
        var requestHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestRows = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var byId = records.ToDictionary(row => row.RecordId, StringComparer.Ordinal);
        using var requestDocument = JsonDocument.Parse(requests); var batches = requestDocument.RootElement.GetProperty("batches");
        if (batches.ValueKind != JsonValueKind.Array || batches.GetArrayLength() is < 1 or > 64) throw Invalid("API request archive requires 1..64 batches.");
        for (var index = 0; index < batches.GetArrayLength() && index < 64; index++)
        {
            check(); var batch = batches[index]; var bytes = Convert.FromBase64String(batch.GetProperty("bytes_base64").GetString()!);
            var hash = batch.GetProperty("sha256").GetString();
            if (bytes.Length is < 1 or > 2_097_152 || !SameHash(Hash(bytes), hash) || !requestHashes.Add(hash!)) throw Invalid("API request wire bytes/hash differ or duplicate.");
            CheckJson(bytes, 2_097_152, token); using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.GetProperty("model").GetString() != "gpt-6.1-sol" || document.RootElement.GetProperty("reasoning").GetProperty("effort").GetString() != "max")
                throw Invalid("Archived request teacher selection differs.");
            var input = document.RootElement.GetProperty("input"); if (input.GetArrayLength() is < 1 or > 4) throw Invalid("Archived input message bound differs.");
            string? userText = null;
            for (var messageIndex = 0; messageIndex < input.GetArrayLength() && messageIndex < 4; messageIndex++)
            {
                check(); var message = input[messageIndex]; if (message.GetProperty("role").GetString() != "user") continue;
                var content = message.GetProperty("content"); if (content.GetArrayLength() != 1 || content[0].GetProperty("type").GetString() != "input_text" || userText is not null) throw Invalid("Archived user payload shape differs.");
                userText = content[0].GetProperty("text").GetString();
            }
            if (userText is null) throw Invalid("Archived request user payload missing.");
            var userBytes = Encoding.UTF8.GetBytes(userText); CheckJson(userBytes, 1_048_576, token); using var userDocument = JsonDocument.Parse(userBytes);
            var sourceRows = userDocument.RootElement.GetProperty("rows"); if (sourceRows.GetArrayLength() is < 1 or > 8) throw Invalid("Archived request requires1..8 source rows.");
            var selected = new HashSet<string>(StringComparer.Ordinal);
            for (var rowIndex = 0; rowIndex < sourceRows.GetArrayLength() && rowIndex < 8; rowIndex++)
            {
                check(); var source = sourceRows[rowIndex]; var id = source.GetProperty("record_id").GetString()!;
                if (!selected.Add(id) || !byId.TryGetValue(id, out var actual) || actual.OriginalRequestJson is null || source.GetProperty("language").GetString() != actual.Language ||
                    !JsonElement.DeepEquals(source.GetProperty("request"), actual.OriginalRequestJson.Value)) throw Invalid("Archived teacher input differs from the expanded original decision request.");
                if (source.TryGetProperty("target_index", out _) || source.TryGetProperty("teacher_binding", out _)) throw Invalid("Archived teacher input exposes the local target/binding.");
            }
            requestRows.Add(hash!, selected);
        }
        using var responseDocument = JsonDocument.Parse(responses); batches = responseDocument.RootElement.GetProperty("batches");
        if (batches.ValueKind != JsonValueKind.Array || batches.GetArrayLength() is < 1 or > 64) throw Invalid("API response archive requires 1..64 batches.");
        var seenResponses = new HashSet<string>(StringComparer.Ordinal); var verified = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < batches.GetArrayLength() && index < 64; index++)
        {
            check(); var batch = batches[index]; var responseId = batch.GetProperty("response_id").GetString()!; var requestHash = batch.GetProperty("request_sha256").GetString()!;
            var responseHash = batch.GetProperty("sha256").GetString()!; var bytes = Convert.FromBase64String(batch.GetProperty("bytes_base64").GetString()!);
            if (!seenResponses.Add(responseId) || !requestHashes.Contains(requestHash) || bytes.Length is < 1 or > 2_097_152 || !SameHash(Hash(bytes), responseHash)) throw Invalid("API response wire identity/hash differs.");
            CheckJson(bytes, 2_097_152, token); using var document = JsonDocument.Parse(bytes); var value = document.RootElement;
            if (value.GetProperty("id").GetString() != responseId || value.GetProperty("model").GetString() != "gpt-6.1-sol" || value.GetProperty("status").GetString() != "completed") throw Invalid("Archived response gateway identity/status differs.");
            if (value.TryGetProperty("reasoning", out var reasoning) && reasoning.TryGetProperty("effort", out var effort) && effort.ValueKind != JsonValueKind.Null && effort.GetString() != "max") throw Invalid("Archived response effort differs.");
            var outputs = value.GetProperty("output"); if (outputs.GetArrayLength() > 50) throw Invalid("Archived output exceeds50 items.");
            string? text = null;
            for (var outputIndex = 0; outputIndex < outputs.GetArrayLength() && outputIndex < 50; outputIndex++)
            {
                check(); var output = outputs[outputIndex]; if (output.GetProperty("type").GetString() != "message") continue;
                var contents = output.GetProperty("content"); if (contents.GetArrayLength() > 20) throw Invalid("Archived content exceeds20 parts.");
                for (var part = 0; part < contents.GetArrayLength() && part < 20; part++)
                {
                    check(); if (contents[part].GetProperty("type").GetString() != "output_text") continue;
                    if (text is not null) throw Invalid("Archived response requires one output_text."); text = contents[part].GetProperty("text").GetString();
                }
            }
            if (text is null) throw Invalid("Archived response output_text missing.");
            var payload = Encoding.UTF8.GetBytes(text); CheckJson(payload, 1_048_576, token); using var labels = JsonDocument.Parse(payload);
            var reviews = labels.RootElement.GetProperty("reviews"); if (reviews.GetArrayLength() is < 1 or > 8) throw Invalid("Archived reviews require1..8 rows.");
            for (var reviewIndex = 0; reviewIndex < reviews.GetArrayLength() && reviewIndex < 8; reviewIndex++)
            {
                check(); var review = reviews[reviewIndex]; var id = review.GetProperty("record_id").GetString()!;
                if (!byId.TryGetValue(id, out var row) || !verified.Add(id) || !requestRows[requestHash].Contains(id)) throw Invalid("Archived teacher row is unknown/repeated or absent from its request.");
                var binding = row.TeacherBinding!;
                if (binding.ResponseId != responseId || !SameHash(binding.RequestSha256, requestHash) || !SameHash(binding.ResponseSha256, responseHash) ||
                    review.GetProperty("question_id").GetString() != row.Question.Id || review.GetProperty("answer_status").GetString() != "determinate" ||
                    !review.GetProperty("suggested_correct_index").TryGetInt32(out var target) || target != row.TargetIndex || review.GetProperty("suggested_correct_id").GetString() != binding.SelectedCandidateId)
                    throw Invalid("Expanded training target is not the actual determinate archived API answer.");
            }
        }
        if (verified.Count != 256) throw Invalid("All256 expanded rows require a real archived determinate API answer.");
    }
}

internal static class ExpansionChecks
{
    internal static void Run(string[] arguments, CancellationToken token)
    {
        if (arguments.Length != 9 || arguments[1] != "--data" || arguments[3] != "--manifest-sha256" || arguments[5] != "--fixture-only" || arguments[7] != "--max-checks" ||
            !bool.TryParse(arguments[6], out var fixtureOnly) || !int.TryParse(arguments[8], out var checks) || checks is < 1 or > 24) throw ExperimentData.Invalid("check-expanded-input requires fixed named pairs and1..24checks (legacy v2 max16).");
        using var budget = new ExperimentBudget(30, token);
        using var data = ExperimentData.OpenExpanded(arguments[2], arguments[4], budget.Check, budget.Token, fixtureOnly);
        if (data.Manifest.SchemaVersion != 3 && checks > 16) throw ExperimentData.Invalid("Legacy v2 check count remains1..16.");
        var negatives = new (string Field, JsonNode? Value)[]
        {
            ("schema_version", JsonValue.Create(1)), ("contract", JsonValue.Create(ExperimentData.Contract)), ("dataset_id", JsonValue.Create("wrong")),
            ("formal", JsonValue.Create(true)), ("human_review", JsonValue.Create("approved")), ("teacher_output_training_permitted", JsonValue.Create(true)),
            ("user_authorized_experiment", JsonValue.Create(false)), ("teacher_answer_exposed", JsonValue.Create(false)), ("eligible_as_sealed_test", JsonValue.Create(true)),
            ("total", JsonValue.Create(255)), ("train", JsonValue.Create(191)), ("development", JsonValue.Create(63)), ("split_rule", JsonValue.Create("random")),
            ("sources", new JsonArray()), ("fixture_only", JsonValue.Create(!fixtureOnly)),
            ("derivation", null), ("contract", JsonValue.Create(ExperimentData.ExpandedContract)), ("total", JsonValue.Create(256)),
            ("train", JsonValue.Create(192)), ("development", JsonValue.Create(64))
        };
        var manifest = JsonSerializer.SerializeToUtf8Bytes(data.Manifest, ExperimentJsonContext.Default.ExperimentManifest);
        for (var index = 1; index < checks && index <= 20; index++)
        {
            budget.Check(); var mutated = JsonNode.Parse(manifest)!.AsObject(); mutated[negatives[index - 1].Field] = negatives[index - 1].Value?.DeepClone();
            var rejected = false;
            try { ExperimentData.ValidateExpandedManifest(JsonSerializer.Deserialize(mutated.ToJsonString(), ExperimentJsonContext.Default.ExperimentManifest)!, fixtureOnly); }
            catch (DecisionException) { rejected = true; }
            if (!rejected) throw ExperimentData.Invalid("Expanded negative contract was accepted: " + negatives[index - 1].Field);
            Console.Error.WriteLine($"expansion fixture/input check {index + 1}/{checks}");
        }
        if (checks >= 22)
        {
            var tiny = new ExpansionVisitObserver(["tiny-a", "tiny-b"], 17, budget.Check);
            for (var step = 0; step <= 3; step++) { budget.Check(); tiny.Committed(step); }
            var tinyVisits = tiny.Complete(3); tinyVisits.Validate(["tiny-a", "tiny-b"], 17, 3, budget.Check);
            if (tinyVisits.CompleteOrderCycles != 1 || tinyVisits.FinalCyclePrefixCount != 1 || tinyVisits.Visits.Sum(row => row.CommittedUpdates) != 3)
                throw ExperimentData.Invalid("Tiny visit probe failed.");
        }
        if (checks >= 23)
        {
            var ids = data.Records.Where(row => row.Split == "train").Select(row => row.RecordId).ToArray();
            var observer = new ExpansionVisitObserver(ids, 17, budget.Check);
            for (var step = 0; step <= 24_576; step++) { budget.Check(); observer.Committed(step); }
            var visits = observer.Complete(24_576); visits.Validate(ids, 17, 24_576, budget.Check);
            if (visits.CompleteOrderCycles != 151 || visits.FinalCyclePrefixCount != 114 || visits.Visits.Sum(row => row.CommittedUpdates) != 24_576 ||
                visits.Visits.Count(row => row.CommittedUpdates == 152) != 114 || visits.Visits.Count(row => row.CommittedUpdates == 151) != 48)
                throw ExperimentData.Invalid("Repaired162 visit cycle reference differs.");
        }
        if (checks >= 24)
        {
            var observer = new ExpansionVisitObserver(["tiny"], 17, budget.Check); observer.Committed(0); var rejected = false;
            try { observer.Committed(2); } catch (DecisionException) { rejected = true; }
            if (!rejected) throw ExperimentData.Invalid("Skipped commit was accepted.");
        }
        Console.WriteLine($"{{\"status\":\"passed\",\"checks\":{checks},\"fixture_only\":{fixtureOnly.ToString().ToLowerInvariant()},\"training_executed\":false,\"gpu_opened\":false,\"api_called\":false}}");
    }

    internal static int RunLengths(string[] arguments, CancellationToken token)
    {
        if (arguments.Length != 9 || arguments[1] != "--data" || arguments[3] != "--manifest-sha256" || arguments[5] != "--tokenizer" || arguments[7] != "--max-records" ||
            !int.TryParse(arguments[8], NumberStyles.None, CultureInfo.InvariantCulture, out var maximum)) throw ExperimentData.Invalid("lengths-expanded requires fixed --data --manifest-sha256 --tokenizer --max-records pairs.");
        using var budget = new ExperimentBudget(120, token);
        using var data = ExperimentData.OpenExpanded(arguments[2], arguments[4], budget.Check, budget.Token);
        if (maximum != 1 && maximum != data.Manifest.Total) throw ExperimentData.Invalid("Expanded lengths must probe1 or cover all actual records.");
        var tokenizerPath = Path.GetFullPath(arguments[6]); ExperimentData.CheckPath(tokenizerPath, budget.Check);
        using var lease = new FileStream(tokenizerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        const int tokenizerBytes = 17_525_329;
        var bytes = ExperimentData.Snapshot(lease, tokenizerBytes, budget.Check); var tokenizerHash = ExperimentData.Hash(bytes);
        if (bytes.Length != tokenizerBytes || !ExperimentData.SameHash(tokenizerHash, IndependentModelLoader.TokenizerSha256)) throw ExperimentData.Invalid("Expanded lengths require pinned actual tokenizer bytes/hash.");
        Array.Clear(bytes); var tokenizer = new TokenizerJson(tokenizerPath, budget.Token); budget.Check();
        if (tokenizer.BosId != 2 || tokenizer.EosId != 1 || tokenizer.MaskId != 4) throw ExperimentData.Invalid("Expanded tokenizer special IDs differ.");
        var rows = new ExperimentLengthRow[maximum];
        var repaired = data.Manifest.SchemaVersion == 3;
        var repairedRows = repaired ? new RepairedLengthRow[maximum] : null;
        var attempted = 0; var accepted = 0; var failed = 0; int? maximumSuccessfulTokens = null;
        for (var index = 0; index < maximum && index < 256; index++)
        {
            budget.Check(); var row = data.Records[index];
            Console.Error.WriteLine($"expanded lengths {index + 1}/{maximum}: {row.RecordId}");
            IndependentDecisionSequence sequence;
            attempted++;
            try { sequence = IndependentDecisionV1SequenceBuilder.Build(tokenizer, row.Request, row.Question, new IndependentDecisionSequenceOptions { MaxTokens = 1024 }, budget.Token); }
            catch (DecisionException error) when (repaired && !budget.Token.IsCancellationRequested && error.Code is
                "decision_v1_token_budget_exceeded" or "decision_v1_input_limit_exceeded" or "decision_v1_control_token" or
                "decision_v1_identity_invalid" or "decision_v1_state_invalid" or "decision_v1_question_invalid" or "decision_v1_candidate_invalid" or "decision_v1_text_invalid")
            {
                // Strict rendering can stop at a partial segment; do not invent a full token count.
                // Cancellation and both renderer/caller deadlines propagate instead of becoming row failures.
                budget.Check(); failed++;
                repairedRows![index] = new(row.RecordId, row.Language, row.Split, row.Type.ToString().ToLowerInvariant(), false, null, null, row.CandidateIds, error.Code, error.Message);
                Console.Error.WriteLine($"expanded strict1024 rejected {index + 1}/{maximum}: {row.RecordId} {error.Code}");
                continue;
            }
            if (sequence.TokenIds.Length is < 2 or > 1024 || !sequence.CandidateIds.SequenceEqual(row.CandidateIds) || sequence.MarkerPositions.Length != row.CandidateIds.Length)
                throw ExperimentData.Invalid("Expanded strict1024 render/candidate/marker identity differs.");
            rows[index] = new(row.RecordId, row.Language, row.Split, row.Type.ToString().ToLowerInvariant(), sequence.TokenIds.Length, sequence.MarkerPositions, sequence.CandidateIds);
            accepted++; maximumSuccessfulTokens = Math.Max(maximumSuccessfulTokens ?? 0, sequence.TokenIds.Length);
            if (repaired) repairedRows![index] = new(row.RecordId, row.Language, row.Split, row.Type.ToString().ToLowerInvariant(), true, sequence.TokenIds.Length, sequence.MarkerPositions, sequence.CandidateIds, null, null);
        }
        budget.Check(); lease.Position = 0; bytes = ExperimentData.Snapshot(lease, tokenizerBytes, budget.Check);
        if (bytes.Length != tokenizerBytes || !ExperimentData.SameHash(ExperimentData.Hash(bytes), tokenizerHash)) throw ExperimentData.Invalid("Tokenizer changed during expanded lengths.");
        Array.Clear(bytes); using var process = Process.GetCurrentProcess(); budget.Check();
        if (process.PeakWorkingSet64 > 4L * 1024 * 1024 * 1024) throw ExperimentData.Invalid("Expanded lengths exceeded host bound.");
        if (repaired)
        {
            if (attempted != maximum || accepted + failed != attempted || repairedRows!.Length != attempted) throw ExperimentData.Invalid("Repaired length attempt/result denominator differs.");
            ExperimentData.RequireHash(data.DerivationLedgerSha256);
            var report = new RepairedLengthReport("sezika/teacher-labelled-expanded-rendered-token-lengths/v3", data.Manifest.Contract,
                failed > 0 ? "completed_rendered_lengths_with_failures_no_model_execution" : "completed_rendered_lengths_no_model_execution", IndependentModelLoader.ModelId,
                data.ManifestSha256, data.RecordsSha256, data.ProvenanceSha256, data.DerivationLedgerSha256!, tokenizerHash, data.Root, tokenizerPath,
                data.Manifest.Total, maximum, attempted, accepted, failed, data.Manifest.Total - attempted, 1024, maximumSuccessfulTokens,
                120, budget.ElapsedSeconds, false, false, false, false, process.PeakWorkingSet64, repairedRows!);
            Console.WriteLine(JsonSerializer.Serialize(report, ExperimentJsonContext.Default.RepairedLengthReport));
            return failed > 0 ? 1 : 0;
        }
        var measurement = new ExperimentLengthReport("sezika/teacher-labelled-rendered-token-lengths/v1", "completed_rendered_lengths_no_model_execution", IndependentModelLoader.ModelId,
            data.ManifestSha256, data.RecordsSha256, data.ProvenanceSha256, tokenizerHash, data.Root, tokenizerPath, data.Manifest.Total, maximum, rows.Length,
            1024, rows.Max(row => row.TokenCount), 120, budget.ElapsedSeconds, false, false, false, false, process.PeakWorkingSet64, rows);
        Console.WriteLine(JsonSerializer.Serialize(measurement, ExperimentJsonContext.Default.ExperimentLengthReport));
        return 0;
    }
}
