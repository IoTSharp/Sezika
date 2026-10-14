using System.Text.Json;
using static Sezika.Tooling.CorpusJson;
using V = Sezika.Tooling.ValidationJson;

namespace Sezika.Tooling;

internal static partial class ValidationOperations
{
    internal static NearTieReport NearTies(NearTieRequest request, Budget budget)
    {
        Require(request.RunDirectories.Length == 2 && request.MaximumObservedRows is >= 1 and <= 574 && IsSha(request.PlanSha256) && FileHash(request.Plan, budget) == request.PlanSha256, "Frozen near-tie plan and bounded two-source order required.");
        var plan = Read(request.Plan, budget, 4 * 1024 * 1024); Require(V.Text(plan, "schema_version") == "sezika.near-tie-observation-plan.v1" && V.Integer(plan.GetProperty("planned_rows"), 574, 574) == 574 && V.Number(plan.GetProperty("maximum_margin")) == .001 && V.Integer(plan.GetProperty("maximum_followup_cases"), 18, 18) == 18, "Frozen near-tie criterion differs.");
        var specifications = V.Array(plan.GetProperty("sources_in_order"), 2, 2); var rows = new List<NearTieRow>(); var sources = new List<NearTieSource>();
        string Bound(string path)
        {
            var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(request.InputRoot, path)); var relative = Path.GetRelativePath(request.InputRoot, full).Replace('\\', '/'); return SafePaths.Within(request.InputRoot, relative, budget);
        }
        for (var sourceIndex = 0; sourceIndex < specifications.Length; sourceIndex++)
        {
            budget.Check(); var spec = specifications[sourceIndex]; var name = V.Text(spec, "name"); var planned = (int)V.Integer(spec.GetProperty("records"), 1, 574); var directory = request.RunDirectories[sourceIndex];
            if (directory is null || rows.Count >= request.MaximumObservedRows) { sources.Add(new(name, planned, 0, "unprocessed", null)); continue; }
            var runRoot = Bound(directory); var run = Read(Path.Combine(runRoot, "run.json"), budget, 4 * 1024 * 1024); var batchPath = Bound(V.Text(run, "plan")); var batchHash = V.Hash(run.GetProperty("plan_sha256")); Require(FileHash(batchPath, budget) == batchHash, "Frozen batch plan SHA differs."); var batchPlan = Read(batchPath, budget, 4 * 1024 * 1024);
            Require(V.Text(batchPlan, "schema_version") == "sezika.evaluation-oracle-batches.v1" && V.Hash(batchPlan.GetProperty("dataset_sha256")) == V.Hash(spec.GetProperty("sha256")) && V.Integer(batchPlan.GetProperty("dataset_total"), 1, 574) == planned, "Source batch identity differs from frozen observation plan.");
            var batches = V.Array(batchPlan.GetProperty("batches"), 8, 1); var ids = batches.SelectMany(batch => V.Array(batch.GetProperty("case_ids"), 46, 1).Select(V.Text)).ToArray(); Require(ids.Length == planned && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length, "Frozen source batch ID denominator differs.");
            var indexPath = Path.Combine(runRoot, "references.json"); var indexHash = FileHash(indexPath, budget); var index = Read(indexPath, budget, 4 * 1024 * 1024); Require(V.Text(index, "schema_version") == "sezika.evaluation-captures.v1", "Independent capture index schema differs."); var indexed = V.Array(index.GetProperty("batches"), 8, 1); var observed = 0;
            foreach (var item in indexed)
            {
                budget.Check(); if (rows.Count >= request.MaximumObservedRows) break; var manifest = Bound(V.Text(item, "manifest")); var capture = Bound(V.Text(item, "capture")); var captureHash = V.Hash(item.GetProperty("capture_sha256")); var manifestHash = V.Hash(item.GetProperty("manifest_sha256")); Require(FileHash(manifest, budget) == manifestHash && FileHash(capture, budget) == captureHash, "Independent reference capture/manifest SHA differs.");
                var reference = Read(capture, budget, 4 * 1024 * 1024); var provenance = reference.GetProperty("provenance");
                Require(V.Text(reference, "measurement_status") == "complete" && V.Text(provenance, "implementation") == "pinned_upstream_laya_agent" && V.Text(provenance, "model_id") == V.Text(plan, "model_id") && V.Text(provenance, "model_revision") == V.Text(plan, "model_revision") && V.Hash(provenance.GetProperty("contract_sha256")) == V.Hash(plan.GetProperty("contract_sha256")) && V.Hash(provenance.GetProperty("cases_sha256")) == manifestHash, "Independent frozen reference identity differs.");
                Require(V.Text(provenance, "upstream_source_revision") == "4066d5d5fbf08b66c6757ddeedbd797bd7655bc0" && V.Hash(provenance.GetProperty("weights_sha256")) == "9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204" && V.Hash(provenance.GetProperty("tokenizer_sha256")) == "609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f", "Independent Laya asset/source provenance differs.");
                foreach (var value in V.Array(reference.GetProperty("cases"), 46, 1))
                {
                    budget.Check(); if (rows.Count >= request.MaximumObservedRows) break; Require(observed < planned && V.Text(value, "id") == ids[observed], "Independent reference source order differs."); var status = V.Text(value, "status"); double? margin = null;
                    if (status == "answered") { var logits = V.Array(value.GetProperty("raw_logits"), 64, 2).Select(logit => { Require(logit.ValueKind == JsonValueKind.Number && double.IsFinite(logit.GetDouble()), "Invalid finite reference logit."); return logit.GetDouble(); }).OrderDescending().ToArray(); margin = logits[0] - logits[1]; Require(double.IsFinite(margin.Value), "Near-tie difference overflow."); }
                    else Require(status == "failed", "Unknown independent reference status.");
                    rows.Add(new(name, observed, V.Text(value, "id"), V.Text(value, "primitive"), status, margin, margin.HasValue && margin.Value <= .001, capture, captureHash)); observed++;
                }
                Require(FileHash(capture, budget) == captureHash, "Independent reference changed during observation.");
            }
            Require(FileHash(indexPath, budget) == indexHash && FileHash(batchPath, budget) == batchHash, "Frozen observation index/plan changed."); sources.Add(new(name, planned, observed, observed == planned ? "complete" : "partial", indexHash));
        }
        var hits = rows.Where(row => row.NearTie).ToArray(); var result = new NearTieReport(rows.Count == 574 ? "COMPLETE_HISTORICAL_MARGIN_OBSERVATION_ONLY" : "PARTIAL_HISTORICAL_MARGIN_OBSERVATION", request.PlanSha256, 574, rows.Count, 574 - rows.Count, hits.Length, .001, sources.ToArray(), rows.ToArray(), hits.Take(18).ToArray()); Write(request.Output, result, ValidationJsonContext.Default.NearTieReport, budget); return result;
    }
}
