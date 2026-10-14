using System.Text.Json;
using static Sezika.Tooling.CorpusJson;
using V = Sezika.Tooling.ValidationJson;

namespace Sezika.Tooling;

internal static partial class ValidationOperations
{
    internal static S5SummaryReport Summarize(S5SummaryRequest request, Budget budget)
    {
        Require(request.ReportPaths.Length is >= 1 and <= 32 && request.ReportPaths.Select(Path.GetFullPath).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() == request.ReportPaths.Length, "S5 unique report count1..32.");
        var rows = new List<S5ValidatedRow>(); string? modelIdentity = null; var inputHashes = new Dictionary<int, string>();
        foreach (var path in request.ReportPaths)
        {
            budget.Check(); var report = Read(path, budget, 4 * 1024 * 1024); Require(V.Integer(report.GetProperty("schema_version"), 1, 1) == 1 && V.Text(report, "status") == "passed" && V.Text(report, "phase") == "complete", "Only completed schema1 S5 report accepted.");
            var backend = V.Text(report, "backend"); Require(backend is "scalar" or "simd" or "int8" or "cuda", "S5 unknown backend."); var aot = V.Boolean(report.GetProperty("native_aot")); var samples = (int)V.Integer(report.GetProperty("samples"), 1, 30);
            foreach (var name in new[] { "rid", "precision", "runtime", "operating_system", "cpu", "environment_label", "model_revision", "timing_scope", "resource_scope", "quality_scope" }) V.Text(report, name);
            var weights = V.Hash(report.GetProperty("weights_sha256")); var tokenizer = V.Hash(report.GetProperty("tokenizer_sha256")); var manifest = V.Hash(report.GetProperty("manifest_sha256")); V.Hash(report.GetProperty("executable_sha256"));
            var identity = V.Text(report, "model_revision") + "|" + weights + "|" + tokenizer + "|" + manifest; modelIdentity ??= identity; Require(modelIdentity == identity, "S5 reports have different model/weights/tokenizer/manifest."); Require(V.Boolean(report.GetProperty("model_objects_collected")), "Model/session collection sentinel did not pass.");
            foreach (var name in new[] { "peak_working_set_bytes", "managed_bytes_before", "managed_bytes_after_collection", "quantized_encoder_bytes", "quantized_head_bytes" }) V.Number(report.GetProperty(name), name);
            var loads = V.Array(report.GetProperty("loads"), 2, 1);
            for (var cycle = 0; cycle < loads.Length; cycle++) { budget.Check(); Require(V.Integer(loads[cycle].GetProperty("cycle"), 0, 1) == cycle, "S5 ordered load cycle differs."); foreach (var name in new[] { "model_load_milliseconds", "backend_load_milliseconds", "first_request_milliseconds", "total_to_first_response_milliseconds" }) V.Number(loads[cycle].GetProperty(name), name); }
            var requests = V.Array(report.GetProperty("requests"), 3, 1); var seenQuestions = new HashSet<int>();
            foreach (var input in requests)
            {
                budget.Check(); var questions = (int)V.Integer(input.GetProperty("questions"), 1, 32); Require(seenQuestions.Add(questions), "Duplicate S5 question count row."); var hash = V.Hash(input.GetProperty("input_sha256")); Require(Sha(V.Text(input, "input_json")) == hash, "S5 input SHA differs.");
                if (inputHashes.TryGetValue(questions, out var previous)) Require(previous == hash, "Question count uses different inputs across S5 reports."); else inputHashes.Add(questions, hash);
                var durations = V.Array(input.GetProperty("milliseconds"), 30, 1).Select(v => V.Number(v, "milliseconds")).ToArray(); Require(durations.Length == samples, "S5 sample/header denominator differs."); var latency = input.GetProperty("latency"); Require(V.Integer(latency.GetProperty("count"), 1, 30) == samples, "S5 latency sample count differs.");
                foreach (var rank in new[] { ("p50", .5), ("p95", .95), ("p99", .99) }) V.Near(latency.GetProperty(rank.Item1), V.Percentile(durations, rank.Item2), "S5 latency." + rank.Item1);
            }
            var alignment = report.GetProperty("alignment"); foreach (var pair in new[] { ("encoder_max_absolute_error", "encoder_tolerance"), ("logits_max_absolute_error", "logits_tolerance"), ("probability_max_absolute_error", "probability_tolerance") }) Require(V.Number(alignment.GetProperty(pair.Item1), pair.Item1) <= V.Number(alignment.GetProperty(pair.Item2), pair.Item2), "S5 numerical tolerance exceeded.");
            if (!V.Null(alignment, "head_only_max_absolute_error")) Require(V.Number(alignment.GetProperty("head_only_max_absolute_error")) <= V.Number(alignment.GetProperty("logits_tolerance")), "S5 head-only tolerance exceeded.");
            if (backend == "cuda") { V.Number(report.GetProperty("cuda_before_unload").GetProperty("peak_owned_bytes")); var after = report.GetProperty("cuda_after_unload"); foreach (var name in new[] { "owned_bytes", "owned_allocation_count", "loaded_module_count", "release_failure_count" }) Require(V.Number(after.GetProperty(name), name) == 0, "CUDA unload did not reach zero."); }
            rows.Add(new(Path.GetFullPath(path), FileHash(path, budget), V.Text(report, "rid"), backend, aot, samples, V.Text(report, "model_revision"), weights, tokenizer, manifest, report));
        }
        if (request.VerifyMatrix)
        {
            Require(rows.All(row => row.NativeAot), "Historical acceptance matrix requires native_aot on every source row.");
            foreach (var backend in new[] { "scalar", "simd", "int8", "cuda" })
            { budget.Check(); Require(rows.Where(r => r.Rid == "win-x64" && r.Backend == backend && r.Samples >= 5).Any(r => new[] { 1, 8, 32 }.All(q => V.Array(r.OriginalReport.GetProperty("requests"), 3, 1).Any(item => V.Integer(item.GetProperty("questions"), 1, 32) == q))), "Missing historical Windows backend q1/8/32 row."); }
            foreach (var backend in new[] { "simd", "cuda" }) Require(rows.Any(row => row.Rid == "linux-x64" && row.Backend == backend), "Missing historical Linux backend smoke row.");
        }
        var result = new S5SummaryReport("PASS_HISTORICAL_S5_REPORT_CONSISTENCY_ONLY", request.VerifyMatrix, rows.ToArray()); Write(request.Output, result, ValidationJsonContext.Default.S5SummaryReport, budget); return result;
    }
}
