using static Sezika.Tooling.CorpusJson;
using V = Sezika.Tooling.ValidationJson;

namespace Sezika.Tooling;

internal static partial class CorpusOperations
{
    internal static CorpusEstimateReport Estimate(CorpusEstimateRequest request, Budget budget)
    {
        Require(request.Updates.Length is >= 1 and <= 32 && request.Updates.All(value => value is >= 1 and <= 800_000_000), "Historical arithmetic scenario bounds.");
        Require(IsSha(request.TrainingReportSha256) && FileHash(request.TrainingReport, budget) == request.TrainingReportSha256 && IsSha(request.ProfileSha256) && FileHash(request.Profile, budget) == request.ProfileSha256 && IsSha(request.PlanSha256) && FileHash(request.Plan, budget) == request.PlanSha256, "Pinned historical arithmetic inputs differ.");
        var training = Read(request.TrainingReport, budget, 1024 * 1024); var profile = Read(request.Profile, budget, 1024 * 1024); var plan = Read(request.Plan, budget, 1024 * 1024); var runId = V.Text(training, "RunId");
        Require(V.Text(training, "Status") == "PASS_experimental_GPU_updates_saved_only" && V.Text(profile, "Status") == "PASS_real_base_profile_only" && V.Text(profile, "RunId") == runId && V.Text(plan, "RunId") == runId && Canonical(training.GetProperty("Inputs")) == Canonical(profile.GetProperty("Inputs")) && V.Hash(training.GetProperty("Inputs").GetProperty("PlanSha256")) == request.PlanSha256 && V.Text(training, "GpuUuid") == V.Text(profile, "GpuUuid") && V.Text(plan, "ExpectedGpuUuid") == V.Text(training, "GpuUuid"), "Historical arithmetic run/model/GPU identity differs.");
        var completed = V.Integer(training.GetProperty("CompletedUpdates"), 1, 800_000_000); Require(completed == V.Integer(plan.GetProperty("MaximumUpdates"), 1, 800_000_000), "Historical update denominator differs."); var initial = V.Integer(training.GetProperty("InitialUpdates"), 0, 800_000_000); var count = completed - initial; var elapsed = V.Number(training.GetProperty("ElapsedSeconds")); var maximumStep = V.Number(profile.GetProperty("MaximumStepSeconds")); Require(count > 0 && elapsed > 0 && maximumStep > 0, "Finite positive historical timing required.");
        var checkpoint = V.Integer(plan.GetProperty("CheckpointEvery"), 1, 800_000_000); var mean = elapsed / count; var scenarios = request.Updates.Select(updates =>
        { budget.Check(); var saves = 1 + updates / checkpoint; var reserve = 600 + 120 * saves; return new CorpusEstimateScenario(updates, mean * updates, maximumStep * updates, 4 * maximumStep * updates + reserve, checkpoint); }).ToArray();
        Require(scenarios.All(s => double.IsFinite(s.HistoricalAmortizedSeconds) && double.IsFinite(s.HistoricalMaximumStepSeconds) && double.IsFinite(s.HistoricalConservativeFormulaSeconds)), "Historical arithmetic overflow.");
        var result = new CorpusEstimateReport("HISTORICAL_ARITHMETIC_ONLY_NOT_CURRENT_TRAINING_BUDGET", runId, count, mean, maximumStep, scenarios, request.TrainingReportSha256, request.ProfileSha256, request.PlanSha256); Write(request.Output, result, CorpusJsonContext.Default.CorpusEstimateReport, budget); return result;
    }
}
