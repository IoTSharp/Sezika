using System.Text.Json;
using static Sezika.Tooling.CorpusJson;
using V = Sezika.Tooling.ValidationJson;

namespace Sezika.Tooling;

internal static partial class ValidationOperations
{
    internal static ProfileValidationReport Profile(ProfileValidationRequest request, Budget budget)
    {
        Require(request.ReportPaths.Length is >= 1 and <= 32 && request.ReportPaths.Select(Path.GetFullPath).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() == request.ReportPaths.Length, "Unique profile report count1..32 required.");
        var reports = new List<ValidatedProfile>();
        foreach (var path in request.ReportPaths)
        {
            budget.Check(); var report = Read(path, budget, 4 * 1024 * 1024); var identity = Identity(report, budget); V.Integer(report.GetProperty("request_token_budget"), 1, 1_048_576); Require(V.Number(report.GetProperty("request_deadline_seconds")) is > 0 and <= 1800, "Request deadline bounds."); var profile = report.GetProperty("profile");
            Require(V.Integer(profile.GetProperty("schema_version"), 3, 3) == 3, "Profile schema3 required.");
            var planned = (int)V.Integer(profile.GetProperty("planned_cases"), 0, 9); var successful = (int)V.Integer(profile.GetProperty("successful_cases"), 0, 9); var rejected = (int)V.Integer(profile.GetProperty("rejected_cases"), 0, 9); var stageCount = (int)V.Integer(profile.GetProperty("cases_with_stage_timings"), 0, 9);
            var plannedQuestions = V.Integer(profile.GetProperty("planned_questions"), 1, 123); V.Text(profile, "input_set_version"); V.Text(profile, "rendering_version"); var detail = V.Text(profile, "detail"); Require(detail is "full" or "end_to_end", "Invalid profile detail.");
            var lengths = V.Array(profile.GetProperty("selected_lengths"), 3, 1).Select(V.Text).ToArray(); var questionCounts = V.Array(profile.GetProperty("selected_questions"), 3, 1).Select(v => (int)V.Integer(v, 1, 32)).ToArray();
            Require(lengths.Distinct(StringComparer.Ordinal).Count() == lengths.Length && lengths.All(v => v is "short" or "medium" or "long") && questionCounts.Distinct().Count() == questionCounts.Length && questionCounts.All(v => v is 1 or 8 or 32), "Invalid or duplicate selected dimensions.");
            var cases = V.Array(profile.GetProperty("cases"), 9); Require(cases.Length == planned && cases.Length == lengths.Length * questionCounts.Length, "Profile case matrix denominator differs.");
            var ids = new HashSet<string>(StringComparer.Ordinal); var rows = new List<ValidatedProfileCase>(); var measuredQuestions = 0; var measuredStages = 0;
            foreach (var item in cases)
            {
                budget.Check(); var id = V.Text(item, "id"); var status = V.Text(item, "status"); var questions = (int)V.Integer(item.GetProperty("questions"), 1, 32); var length = V.Text(item, "length_preset");
                Require(status is "measured" or "rejected" && questionCounts.Contains(questions) && lengths.Contains(length, StringComparer.Ordinal) && id == length + "-" + questions && ids.Add(id), "Case does not uniquely cover declared matrix.");
                var repetitions = V.Integer(item.GetProperty("state_repetitions"), 1, 100); Require(repetitions == (length == "short" ? 4 : length == "medium" ? 20 : 100) && V.Integer(item.GetProperty("candidates_per_question"), 2, 2) == 2, "Input preset mismatch.");
                var discovery = V.Integer(item.GetProperty("discovery_completed_forwards"), 0, 32); var entered = V.Integer(item.GetProperty("instrumented_entered_forwards"), 0, 32); var completed = V.Integer(item.GetProperty("instrumented_completed_forwards"), 0, 32);
                var inputHash = V.Hash(item.GetProperty("input_sha256")); Require(Sha(V.Text(item, "input_json")) == inputHash, "Input JSON hash mismatch."); V.Number(item.GetProperty("tokenizer_and_rendering_milliseconds"), "tokenizer_and_rendering_milliseconds");
                if (status == "rejected")
                {
                    var error = V.Text(item, "error_code"); Require(error is "decision_token_budget_exceeded" or "decision_token_limit_exceeded" && V.Array(item.GetProperty("end_to_end_milliseconds"), 30).Length == 0 && V.Array(item.GetProperty("allocated_bytes"), 30).Length == 0 && new[] { "latency", "requests_per_second", "questions_per_second", "encoder_and_head_milliseconds", "instrumented_end_to_end_milliseconds" }.All(field => V.Null(item, field)), "Rejected case has unsupported error or numerical claims.");
                    rows.Add(new(id, status, questions, inputHash, 0, null, null, null, null, null, null, null, error)); continue;
                }
                var samples = V.Array(item.GetProperty("end_to_end_milliseconds"), 30, 1).Select(v => V.Number(v, "end_to_end_milliseconds")).ToArray(); Require(samples.Length == identity.Samples && samples.Sum() > 0 && double.IsFinite(samples.Sum()), "Measured E2E denominator/total differs.");
                var allocations = V.Array(item.GetProperty("allocated_bytes"), 30); Require(allocations.Length == samples.Length, "Allocation denominator differs."); foreach (var value in allocations) V.Integer(value, 0, long.MaxValue, "allocated_bytes");
                var latency = item.GetProperty("latency"); Require(V.Integer(latency.GetProperty("count"), 1, 30) == samples.Length, "Latency count differs."); V.Near(latency.GetProperty("minimum"), samples.Min(), "latency.minimum"); V.Near(latency.GetProperty("maximum"), samples.Max(), "latency.maximum");
                foreach (var rank in new[] { ("p50", .50), ("p95", .95), ("p99", .99) }) V.Near(latency.GetProperty(rank.Item1), V.Percentile(samples, rank.Item2), "latency." + rank.Item1);
                var rate = samples.Length * 1000 / samples.Sum(); V.Near(item.GetProperty("requests_per_second"), rate, "requests_per_second"); V.Near(item.GetProperty("questions_per_second"), rate * questions, "questions_per_second");
                Require(V.Text(item, "rendering_verification") == "exact_token_marker_type_match" && discovery == questions, "Measured discovery was not fully verified.");
                var rendered = V.Array(item.GetProperty("rendered_sequences"), 32); var actual = V.Array(item.GetProperty("actual_sequences"), 32); Require(rendered.Length == questions && actual.Length == questions, "Rendering sequence denominator differs."); long tokenTotal = 0;
                for (var sequence = 0; sequence < rendered.Length; sequence++)
                {
                    budget.Check(); var proposal = rendered[sequence]; var observation = actual[sequence]; var tokens = V.Integer(proposal.GetProperty("token_count"), 1, 8192); tokenTotal += tokens;
                    Require(V.Integer(observation.GetProperty("token_count"), 1, 8192) == tokens && V.Integer(proposal.GetProperty("type_id"), 0, 2) == V.Integer(observation.GetProperty("type_id"), 0, 2) && V.Text(proposal, "question_id") == V.Text(observation, "question_id") && V.Hash(proposal.GetProperty("tokens_sha256")) == V.Hash(observation.GetProperty("tokens_sha256")), "Observed sequence differs.");
                    var markers = V.Array(proposal.GetProperty("markers"), 2, 2); var seenMarkers = V.Array(observation.GetProperty("markers"), 2, 2); for (var marker = 0; marker < 2; marker++) Require(V.Integer(markers[marker], 0, tokens - 1) == V.Integer(seenMarkers[marker], 0, tokens - 1), "Observed marker position differs.");
                }
                Require(V.Integer(item.GetProperty("rendered_total_tokens"), 1, 262_144) == tokenTotal && V.Integer(item.GetProperty("response").GetProperty("usage").GetProperty("token_count"), 1, 262_144) == tokenTotal, "Rendered/response token total mismatch.");
                Require(V.Array(item.GetProperty("resource_observation_errors"), 0).Length == 0 && V.Text(item, "phase") == "complete" && V.Null(item, "error_code"), "Measured case has unresolved failures.");
                string? stage = null; double? stageMilliseconds = null;
                if (detail == "full")
                { stageMilliseconds = V.Number(item.GetProperty("encoder_and_head_milliseconds"), "encoder_and_head_milliseconds"); V.Number(item.GetProperty("instrumented_end_to_end_milliseconds"), "instrumented_end_to_end_milliseconds"); Require(V.Text(item, "encoder_and_head_status") == "measured_independent_instrumented_request" && entered == questions && completed == questions, "Incomplete independent stage measurement."); stage = "encoder_and_head"; measuredStages++; }
                else Require(V.Null(item, "encoder_and_head_milliseconds") && V.Null(item, "instrumented_end_to_end_milliseconds") && V.Text(item, "encoder_and_head_status") == "not_requested_end_to_end_detail" && entered == 0 && completed == 0, "E2E-only case includes undeclared stage measurements.");
                measuredQuestions += questions; rows.Add(new(id, status, questions, inputHash, samples.Length, V.Number(latency.GetProperty("p50")), V.Number(latency.GetProperty("p95")), V.Number(latency.GetProperty("p99")), rate, rate * questions, stage, stageMilliseconds, null));
            }
            Require(successful == rows.Count(c => c.Status == "measured") && rejected == rows.Count(c => c.Status == "rejected") && V.Text(profile, "status") == (rejected == 0 ? "complete" : "complete_with_rejections"), "Profile successful/rejected/status denominator differs.");
            Require(plannedQuestions == lengths.Length * questionCounts.Sum() && stageCount == measuredStages, "Profile question/stage denominator mismatch."); V.Near(profile.GetProperty("request_coverage"), (double)successful / cases.Length, "request_coverage"); V.Near(profile.GetProperty("question_coverage"), (double)measuredQuestions / plannedQuestions, "question_coverage");
            reports.Add(new(Path.GetFullPath(path), FileHash(path, budget), identity.Backend, identity.Rid, identity.Aot, V.Text(report, "model_revision"), V.Hash(report.GetProperty("weights_sha256")), V.Hash(report.GetProperty("tokenizer_sha256")), V.Hash(report.GetProperty("manifest_sha256")), planned, successful, rejected, (double)successful / cases.Length, (double)measuredQuestions / plannedQuestions, rows.ToArray()));
        }
        var output = new ProfileValidationReport("PASS_HISTORICAL_PROFILE_CONSISTENCY_ONLY", reports.ToArray()); Write(request.Output, output, ValidationJsonContext.Default.ProfileValidationReport, budget); return output;
    }
    private static (string Backend, string Rid, bool Aot, int Samples) Identity(JsonElement report, Budget budget)
    {
        Require(V.Integer(report.GetProperty("schema_version"), 1, 1) == 1 && V.Text(report, "status") == "passed" && V.Text(report, "phase") == "complete", "Only completed schema1 passed reports accepted.");
        var backend = V.Text(report, "backend"); Require(backend is "scalar" or "simd" or "int8" or "cuda", "Unsupported backend.");
        foreach (var field in new[] { "rid", "precision", "runtime", "cpu", "model_revision", "environment_label" }) V.Text(report, field);
        foreach (var field in new[] { "weights_sha256", "tokenizer_sha256", "manifest_sha256", "executable_sha256" }) V.Hash(report.GetProperty(field));
        var aot = V.Boolean(report.GetProperty("native_aot")); var samples = (int)V.Integer(report.GetProperty("samples"), 1, 30); V.Integer(report.GetProperty("warmup"), 0, 5); V.Integer(report.GetProperty("inference_threads"), 1, 1024);
        if (report.TryGetProperty("request_token_budget", out var tokens)) V.Integer(tokens, 1, 1_048_576);
        if (report.TryGetProperty("request_deadline_seconds", out var seconds)) Require(V.Number(seconds) is > 0 and <= 1800, "Request deadline bounds.");
        var artifacts = V.Array(report.GetProperty("code_artifacts"), 4, 1); Require(artifacts.Length == (aot ? 1 : 4), "Incomplete code artifact identities."); var paths = new HashSet<string>(StringComparer.Ordinal); var executable = 0; var assemblies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts)
        { budget.Check(); var path = V.Text(artifact, "path"); Require(paths.Add(path), "Duplicate code artifact path."); V.Hash(artifact.GetProperty("sha256")); var role = V.Text(artifact, "role"); Require(role is "process_executable" or "managed_assembly", "Invalid code artifact role."); if (role == "process_executable") { executable++; Require(V.Hash(artifact.GetProperty("sha256")) == V.Hash(report.GetProperty("executable_sha256")), "Executable identity mismatch."); } else assemblies.Add(path.Replace('\\', '/').Split('/')[^1]); }
        Require(executable == 1 && (aot || assemblies.SetEquals(new[] { "Sezika.Benchmarks.dll", "Sezika.dll", "Sezika.Cuda.dll" })), "Managed application identity mismatch."); return (backend, V.Text(report, "rid"), aot, samples);
    }
}
