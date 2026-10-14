using System.Text.Json;
using static Sezika.Tooling.OracleIntegrity;

namespace Sezika.Tooling;

internal static class OracleCaptureEvidence
{
    internal static bool PortedContract(OracleFilePin contract, Budget budget)
    {
        Verify(contract, budget, 1048576); using var document = Document(contract.Path, budget, 1048576);
        var schema = Text(document.RootElement, "schema_version");
        Require(schema is "sezika.laya-oracle-contract.port-derived.v2" or "sezika.laya-oracle-contract.v1", "Unsupported Oracle contract generation.");
        return schema == "sezika.laya-oracle-contract.port-derived.v2";
    }
    internal static void Manifest(OracleFilePin manifest, string[] ids, Budget budget)
    {
        Verify(manifest, budget, 33554432); using var document = Document(manifest.Path, budget);
        Require(Text(document.RootElement, "schema_version") == "sezika.laya-oracle-inputs.v1", "Frozen Oracle manifest schema differs.");
        CaseIds(document.RootElement, ids, budget);
    }
    internal static void Capture(OracleFilePin capture, OracleFilePin manifest, OracleFilePin contract, string[] ids,
        bool reference, bool ported, bool simulation, string? referenceSha, Budget budget)
    {
        Verify(capture, budget, 33554432); using var document = Document(capture.Path, budget); var root = document.RootElement;
        if (simulation)
        {
            Require(Text(root, "schema_version") == "sezika.oracle-batch.simulation.v1" && root.GetProperty("simulation_only").GetBoolean() &&
                root.GetProperty("fixture_only").GetBoolean(), "Synthetic Oracle capture must remain explicitly simulation-only.");
        }
        else
        {
            var expected = ported ? reference ? "sezika.laya-oracle.port-derived.v2" : "sezika.laya-oracle.actual.v2" : "sezika.laya-oracle.v1";
            Require(Text(root, "schema_version") == expected, "Oracle capture generation differs; legacy v1 cannot masquerade as a new reference.");
            Require(Text(root, "measurement_status") is "complete" or "completed", "Oracle capture is incomplete.");
            if (reference && ported)
                Require(Text(root, "kind") == "independently_ported_original_laya_weights_capture" && !root.GetProperty("fixture_only").GetBoolean() &&
                    !root.GetProperty("upstream_executed").GetBoolean() && Text(root.GetProperty("provenance"), "implementation") == "laya-4066d5-transformers-4.57.1-csharp-fp32-v2",
                    "Independent Laya port source semantics differ.");
            if (reference && !ported)
                Require(Text(root.GetProperty("provenance"), "implementation") == "pinned_upstream_laya_agent", "Legacy upstream reference declaration is absent.");
            if (!reference) Require(Text(root.GetProperty("implementation"), "backend") == "cuda_fp32", "Actual Oracle capture must report the explicitly selected CUDA backend.");
        }
        var provenance = root.GetProperty("provenance");
        Require(Text(provenance, "cases_sha256") == manifest.Sha256 && Text(provenance, "contract_sha256") == contract.Sha256, "Oracle capture contract/input SHA differs.");
        if (!reference) Require(Text(root, "reference_sha256") == referenceSha, "Actual capture consumed another reference.");
        CaseIds(root, ids, budget);
    }
    internal static bool Comparison(OracleFilePin comparison, OracleFilePin reference, OracleFilePin actual,
        OracleFilePin manifest, OracleFilePin contract, string[] ids, int exitCode, bool simulation, Budget budget)
    {
        Verify(comparison, budget, 33554432); using var document = Document(comparison.Path, budget); var root = document.RootElement;
        Require(Text(root, "schema_version") == (simulation ? "sezika.oracle-comparison.simulation.v1" : "sezika.oracle-comparison.v1"), "Oracle comparison schema differs.");
        if (simulation) Require(root.GetProperty("simulation_only").GetBoolean(), "Comparison fixture must remain explicitly simulation-only.");
        Require(Text(root, "reference_sha256") == reference.Sha256 && Text(root, "actual_sha256") == actual.Sha256 &&
            Text(root, "contract_sha256") == contract.Sha256 && Text(root, "cases_sha256") == manifest.Sha256,
            "Oracle comparison output is not bound to this exact reference/actual/contract/input.");
        Require(root.GetProperty("manifest_count").GetInt32() == ids.Length && root.GetProperty("compared_count").GetInt32() == ids.Length &&
            root.GetProperty("missing_case_ids").GetArrayLength() == 0 && Strings(root.GetProperty("selected_case_ids")).SequenceEqual(ids), "Oracle comparison selection/coverage differs.");
        var passed = root.GetProperty("passed").GetBoolean(); var full = root.GetProperty("full_manifest_passed").GetBoolean();
        Require(exitCode == (passed ? 0 : 1) && (!full || passed), "Comparator numerical verdict disagrees with its actual exit code.");
        return full;
    }
    private static void CaseIds(JsonElement root, string[] ids, Budget budget)
    {
        var cases = root.GetProperty("cases"); Require(cases.ValueKind == JsonValueKind.Array && cases.GetArrayLength() == ids.Length && ids.Length is > 0 and <= 46, "Oracle capture/manifest row count differs.");
        var index = 0; foreach (var row in cases.EnumerateArray()) { budget.Check(); Require(Text(row, "id") == ids[index++], "Oracle fixed manifest row order differs."); }
    }
    internal static string Text(JsonElement root, string field) => root.GetProperty(field).GetString() ?? throw new InvalidDataException("Missing Oracle text field: " + field);
    private static string[] Strings(JsonElement value)
    { Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= 46, "Oracle ID array bound46."); return value.EnumerateArray().Select(row => row.GetString() ?? throw new InvalidDataException("Null Oracle ID.")).ToArray(); }
}
