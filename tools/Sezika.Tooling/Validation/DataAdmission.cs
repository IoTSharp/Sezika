using System.Globalization;
using System.Text;
using System.Text.Json;
using static Sezika.Tooling.CorpusJson;
using V = Sezika.Tooling.ValidationJson;

namespace Sezika.Tooling;

internal static partial class ValidationOperations
{
    internal static DataAdmissionReport DataAdmission(DataAdmissionRequest request, Budget budget)
    {
        Require(request.MaximumRecords is >= 1 and <= 1000, "Data admission record bound1..1000."); var path = Path.GetFullPath(request.Manifest); var root = Path.GetDirectoryName(path)!; var manifestHash = FileHash(path, budget); var manifest = Read(path, budget, 4 * 1024 * 1024);
        V.Properties(manifest, ["schema_version", "dataset_id", "stage", "intended_use", "records", "sources", "test_seal"]);
        var id = V.Text(manifest, "dataset_id"); var stage = V.Text(manifest, "stage"); var use = V.Text(manifest, "intended_use"); Require(V.Integer(manifest.GetProperty("schema_version"), 1, 1) == 1 && stage is "FixtureOnly" or "Candidate" && use is "Research" or "Commercial" or "OpenSourceRedistribution", "Invalid S4 admission manifest schema/stage/use.");
        var recordsPath = VerifyArtifact(root, manifest.GetProperty("records"), budget); var recordsHash = FileHash(recordsPath, budget); var records = Lines(recordsPath, request.MaximumRecords, budget).ToArray(); Require(records.Length > 0, "Admission records are empty.");
        var sources = V.Array(manifest.GetProperty("sources"), 32, 1); var sourceMap = new Dictionary<string, JsonElement>(StringComparer.Ordinal); var issues = new List<ValidationDiagnostic>();
        void Issue(string code, string subject, string detail, string? related = null) { budget.Check(); Require(issues.Count < 10_000, "Admission diagnostic count10000."); issues.Add(new(code, subject, detail, related)); }
        if (stage == "FixtureOnly") Issue("fixture_only", id, "Fixture data cannot be admitted as a real dataset.");
        foreach (var source in sources)
        {
            budget.Check(); V.Properties(source, ["id", "upstream_id", "upstream_split", "revision", "origin", "raw", "license_expression", "license_review", "license_reviewer", "license_reviewed_utc", "license_evidence", "allowed_uses", "allowed_splits", "exposure"]);
            var sourceId = V.Text(source, "id"); Require(sourceMap.TryAdd(sourceId, source), "Duplicate source ID."); var origin = V.Text(source, "origin"); var exposure = V.Text(source, "exposure"); var review = V.Text(source, "license_review");
            Require(origin is "Original" or "ThirdParty" or "PawsViewedTest250" or "NimbleViewedEval324" && exposure is "Unseen" or "DevelopmentViewed" or "EvaluationViewed" && review is "Pending" or "Approved" or "Rejected", "Invalid source policy.");
            var uses = V.Array(source.GetProperty("allowed_uses"), 3).Select(V.Text).ToArray(); var splits = V.Array(source.GetProperty("allowed_splits"), 5).Select(V.Text).ToArray(); VerifyArtifact(root, source.GetProperty("raw"), budget);
            if (!V.Null(source, "license_evidence")) VerifyArtifact(root, source.GetProperty("license_evidence"), budget);
            if (review != "Approved") Issue("license_not_approved", sourceId, "License review is pending or rejected.");
            else if (!Nonempty(source, "license_reviewer") || !Past(source, "license_reviewed_utc") || V.Null(source, "license_evidence")) Issue("license_evidence_missing", sourceId, "Approval requires reviewer, past UTC time and bound evidence.");
            if (!uses.Contains(use, StringComparer.Ordinal)) Issue("use_not_allowed", sourceId, "Intended use is absent from source allowlist.");
            var hash = V.Hash(source.GetProperty("raw").GetProperty("sha256")); var viewed = exposure == "EvaluationViewed" || origin is "PawsViewedTest250" or "NimbleViewedEval324" || hash is "c295258fdd73452f196b2673bdbee58cf01fa3f24400c2f1c0f26fd1126bc6f3" or "8e9e48b8de5206593912ae01ddc95bd77e40ad2ecf4c9292c1711290eca0d896";
            if (viewed && !splits.SequenceEqual(new[] { "Audit" })) Issue("viewed_source_policy", sourceId, "Viewed evaluation sources may allow only Audit.");
        }
        var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal); var counts = new Dictionary<string, int>(StringComparer.Ordinal); var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal); var shingles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var row in records)
        {
            budget.Check(); V.Properties(row, ["id", "source_id", "family", "entities", "language", "domain", "split", "text", "derivation", "parent_id", "human_review", "reviewer", "reviewed_utc", "review_note", "exposure"]);
            foreach (var field in new[] { "id", "source_id", "family", "language", "domain", "split", "text", "derivation", "human_review", "exposure" }) V.Text(row, field);
            var recordId = V.Text(row, "id"); Require(byId.TryAdd(recordId, row), "Duplicate record ID."); var split = V.Text(row, "split"); var derivation = V.Text(row, "derivation"); var human = V.Text(row, "human_review"); var exposure = V.Text(row, "exposure");
            Require(split is "Training" or "Development" or "Calibration" or "SealedTest" or "Audit" && derivation is "Original" or "Translation" or "Counterfactual" or "Paraphrase" && human is "Pending" or "Approved" or "Rejected" && exposure is "Unseen" or "DevelopmentViewed" or "EvaluationViewed", "Invalid record split/derivation/review/exposure.");
            var entities = V.Array(row.GetProperty("entities"), 32, 1).Select(V.Text).ToArray(); Require(entities.All(e => e.Length <= 128 && e.Trim() == e), "Invalid record entity.");
            Require(derivation == "Original" ? V.Null(row, "parent_id") : Nonempty(row, "parent_id"), "Derivation/parent mismatch."); Require(sourceMap.TryGetValue(V.Text(row, "source_id"), out var source), "Unknown record source.");
            if (!V.Array(source.GetProperty("allowed_splits"), 5).Select(V.Text).Contains(split, StringComparer.Ordinal)) Issue("split_not_allowed", recordId, "Record split is absent from source allowlist.");
            if (human != "Approved" || !Nonempty(row, "reviewer") || !Past(row, "reviewed_utc") || !Nonempty(row, "review_note")) Issue("human_review_required", recordId, "Human review requires reviewer, past time and note.");
            var viewed = V.Text(source, "exposure") == "EvaluationViewed" || V.Text(source, "origin") is "PawsViewedTest250" or "NimbleViewedEval324" || exposure == "EvaluationViewed";
            if (viewed && split != "Audit") Issue("viewed_evaluation_reused", recordId, "Viewed evaluation content and derivatives are audit-only.");
            if ((V.Text(source, "exposure") != "Unseen" || exposure != "Unseen") && split == "SealedTest") Issue("sealed_test_exposed", recordId, "Viewed content cannot become a fresh sealed test.");
            counts[split] = counts.GetValueOrDefault(split) + 1; var normalized = NormalizeAdmissionText(V.Text(row, "text"), budget); fingerprints.Add(recordId, Sha(normalized)); shingles.Add(recordId, Shingles(normalized, budget));
        }
        foreach (var row in records)
        {
            if (V.Text(row, "derivation") == "Original") continue; var current = row; var ancestry = new HashSet<string>(StringComparer.Ordinal);
            for (var depth = 0; depth < records.Length && !V.Null(current, "parent_id"); depth++)
            {
                budget.Check(); var currentId = V.Text(current, "id"); if (!ancestry.Add(currentId)) { Issue("parent_cycle", V.Text(row, "id"), "Derivation ancestry contains a cycle.", currentId); break; }
                var parentId = V.Text(current, "parent_id"); if (!byId.TryGetValue(parentId, out var parent)) { Issue("parent_missing", V.Text(row, "id"), "Every derivation ancestor must be present.", parentId); break; }
                if (V.Text(row, "family") != V.Text(parent, "family") || V.Text(row, "split") != V.Text(parent, "split")) Issue("derivation_isolation", V.Text(row, "id"), "Derivatives inherit family and split.", parentId); current = parent;
                if (depth == records.Length - 1 && !V.Null(current, "parent_id")) Issue("parent_cycle", V.Text(row, "id"), "Derivation depth exceeded bounded record count.");
            }
        }
        long compared = 0; var maximumPairs = (long)request.MaximumRecords * (request.MaximumRecords - 1) / 2;
        for (var left = 0; left < records.Length; left++)
            for (var right = left + 1; right < records.Length; right++)
            {
                budget.Check(); Require(++compared <= maximumPairs, "Admission pair bound."); var a = records[left]; var b = records[right]; if (V.Text(a, "split") == V.Text(b, "split")) continue; var aid = V.Text(a, "id"); var bid = V.Text(b, "id");
                if (V.Text(a, "family") == V.Text(b, "family")) Issue("family_crosses_split", aid, "Family members must remain in one split.", bid);
                if (V.Array(a.GetProperty("entities"), 32).Select(V.Text).Intersect(V.Array(b.GetProperty("entities"), 32).Select(V.Text), StringComparer.Ordinal).Any()) Issue("entity_crosses_split", aid, "Entity appears in different splits.", bid);
                if (fingerprints[aid] == fingerprints[bid]) Issue("fingerprint_crosses_split", aid, "Normalized text appears in different splits.", bid);
                else if (Jaccard(shingles[aid], shingles[bid], budget) >= .82) Issue("near_duplicate_crosses_split", aid, "Character-trigram Jaccard exceeds frozen0.82 threshold.", bid);
            }
        foreach (var split in new[] { "Development", "Calibration", "SealedTest" }) if (counts.GetValueOrDefault(split) == 0) Issue("required_split_missing", id, "Required split is empty: " + split);
        var seal = manifest.GetProperty("test_seal"); V.Properties(seal, ["status", "custodian", "sealed_utc", "evidence"]);
        if (V.Text(seal, "status") != "Sealed" || !Nonempty(seal, "custodian") || !Past(seal, "sealed_utc") || V.Null(seal, "evidence")) Issue("test_not_sealed", id, "Seal requires custodian, past time and bound evidence.");
        if (!V.Null(seal, "evidence"))
        {
            var evidence = Read(VerifyArtifact(root, seal.GetProperty("evidence"), budget), budget, 4 * 1024 * 1024); V.Properties(evidence, ["dataset_id", "records_sha256", "custodian", "sealed_utc"]);
            if (V.Text(evidence, "dataset_id") != id || V.Hash(evidence.GetProperty("records_sha256")) != recordsHash || V.Text(evidence, "custodian") != V.Text(seal, "custodian") || !SameTime(evidence, "sealed_utc", seal, "sealed_utc")) Issue("seal_binding_mismatch", id, "Seal evidence must bind dataset/records/custodian/freeze time.");
        }
        Require(FileHash(recordsPath, budget) == recordsHash && FileHash(path, budget) == manifestHash, "Admission source changed during inspection.");
        var result = new DataAdmissionReport(issues.Count == 0 ? "checks_passed_pending_human_acceptance" : "blocked", id, manifestHash, recordsHash, records.Length, compared, counts, issues.ToArray(), issues.Count == 0); Write(request.Output, result, ValidationJsonContext.Default.DataAdmissionReport, budget);
        if (request.RequireAdmission && issues.Count > 0) throw new InvalidDataException("Admission diagnostics are blocked; report preserved."); return result;
    }
    private static string VerifyArtifact(string root, JsonElement reference, Budget budget)
    { var path = SafePaths.Within(root, V.Text(reference, "path"), budget); var info = new FileInfo(path); Require(info.Length is >= 1 and <= 4 * 1024 * 1024 && FileHash(path, budget) == V.Hash(reference.GetProperty("sha256")), "Admission artifact SHA/bytes differs."); return path; }
    private static bool Nonempty(JsonElement value, string name) => value.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(member.GetString());
    private static bool Past(JsonElement value, string name) => value.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(member.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date <= DateTimeOffset.UtcNow;
    private static bool SameTime(JsonElement a, string an, JsonElement b, string bn) => DateTimeOffset.TryParse(V.Text(a, an), CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) && DateTimeOffset.TryParse(V.Text(b, bn), CultureInfo.InvariantCulture, DateTimeStyles.None, out var second) && first == second;
    internal static string NormalizeAdmissionText(string text, Budget budget)
    {
        Require(text.Length <= 8192, "Admission text UTF16 unit bound8192."); var normalized = text.Normalize(NormalizationForm.FormKC).ToUpperInvariant(); var builder = new StringBuilder(); var count = 0;
        foreach (var rune in normalized.EnumerateRunes()) { budget.Check(); Require(++count <= 32_768, "Normalized rune count bound."); var category = Rune.GetUnicodeCategory(rune); if (Rune.IsLetterOrDigit(rune) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) builder.Append(rune.ToString()); }
        Require(builder.Length > 0, "Admission text must contain letter/digit."); return builder.ToString();
    }
    private static HashSet<string> Shingles(string text, Budget budget)
    {
        var runes = text.EnumerateRunes().Take(32_769).Select(r => r.ToString()).ToArray(); Require(runes.Length <= 32_768, "Shingle rune bound."); var result = new HashSet<string>(StringComparer.Ordinal); if (runes.Length < 3) { result.Add(text); return result; }
        for (var index = 0; index + 2 < runes.Length; index++) { budget.Check(); result.Add(runes[index] + runes[index + 1] + runes[index + 2]); } return result;
    }
    private static double Jaccard(HashSet<string> left, HashSet<string> right, Budget budget)
    { var intersection = 0; Require(left.Count <= 32_768 && right.Count <= 32_768, "Jaccard item bound."); foreach (var item in left) { budget.Check(); if (right.Contains(item)) intersection++; } var union = left.Count + right.Count - intersection; return union == 0 ? 1 : (double)intersection / union; }
}
