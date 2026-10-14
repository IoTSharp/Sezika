using System.Text.Json;
using static Sezika.Tooling.CorpusJson;
using V = Sezika.Tooling.ValidationJson;

namespace Sezika.Tooling;

internal static partial class ValidationOperations
{
    internal static LanguageAuditReport LanguageAudit(LanguageAuditRequest request, Budget budget)
    {
        Require(request.Sources.Length is >= 1 and <= 4 && request.MaximumRecords is >= 1 and <= 12 && request.MaximumRecords <= request.Sources.Length * 3 && request.Sources.Distinct(StringComparer.Ordinal).Count() == request.Sources.Length, "Language audit source/record bounds.");
        var rows = new List<string>(); var sources = new List<LanguageAuditSource>();
        foreach (var relative in request.Sources)
        {
            budget.Check(); var path = SafePaths.Within(request.Root, relative, budget); var hash = FileHash(path, budget); var originals = Lines(path, 3, budget).ToArray(); Require(originals.Length == 3, "Original language test fixture must have3 rows."); sources.Add(new(relative, hash, 3));
            foreach (var row in originals)
            {
                budget.Check(); Require(V.Text(row, "split") == "test" && V.Text(row, "language") is "en" or "zh", "Only original language-tagged test rows are allowed."); if (rows.Count >= request.MaximumRecords) continue;
                var primitive = V.Text(row, "primitive"); Require(primitive is "choice" or "score" or "boolean", "Language fixture finite primitive required.");
                using var memory = new MemoryStream(); using (var writer = new Utf8JsonWriter(memory))
                {
                    writer.WriteStartObject(); writer.WriteString("id", V.Text(row, "id")); writer.WriteString("language", V.Text(row, "language")); writer.WriteString("domain", V.Text(row, "domain")); writer.WriteString("family", V.Text(row, "id")); writer.WriteString("source_family", "sezika-s4-02-original-test-fixture");
                    writer.WritePropertyName("input"); writer.WriteStartObject(); writer.WritePropertyName("state"); row.GetProperty("state").WriteTo(writer); writer.WritePropertyName("questions"); writer.WriteStartObject(); writer.WritePropertyName("decision"); writer.WriteStartObject(); writer.WriteString("type", primitive); writer.WriteString("instructions", V.Text(row, "instructions")); writer.WritePropertyName("criteria");
                    if (primitive == "boolean") { writer.WriteStartObject(); writer.WritePropertyName("false"); row.GetProperty("criteria").GetProperty("when_false").WriteTo(writer); writer.WritePropertyName("true"); row.GetProperty("criteria").GetProperty("when_true").WriteTo(writer); writer.WriteEndObject(); } else row.GetProperty("criteria").WriteTo(writer);
                    writer.WriteEndObject(); writer.WriteEndObject(); writer.WriteEndObject(); writer.WritePropertyName("reference"); writer.WriteStartObject(); writer.WritePropertyName("target"); row.GetProperty("label").WriteTo(writer); writer.WriteEndObject(); writer.WriteEndObject();
                }
                rows.Add(Utf8.GetString(memory.ToArray()));
            }
            Require(FileHash(path, budget) == hash, "Original language test source changed.");
        }
        Require(rows.Count == request.MaximumRecords, "Language audit selected record denominator differs."); var output = NewOutput(request.Output, budget); var data = Path.Combine(output, "eval.jsonl"); File.WriteAllText(data, string.Join('\n', rows) + "\n", Utf8);
        var result = new LanguageAuditReport("original_fixture_audit_not_independent_quality_acceptance", rows.Count, FileHash(data, budget), sources.ToArray()); Write(Path.Combine(output, "manifest.json"), result, ValidationJsonContext.Default.LanguageAuditReport, budget); return result;
    }
}
