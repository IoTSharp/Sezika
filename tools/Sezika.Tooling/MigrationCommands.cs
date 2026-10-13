using System.Text.Json.Serialization.Metadata;
namespace Sezika.Tooling;
internal static class MigrationCommands
{
    internal static readonly string[] Commands = ["task prepare", "bundle freeze", "archive pack", "checkpoint verify", "host inspect", "corpus prepare", "corpus compose", "corpus queue", "corpus collect", "corpus admission", "corpus dataset", "corpus research", "corpus research-check", "corpus seed-check", "corpus inspect", "corpus estimate", "corpus bind"];
    internal static int Run(string command, string requestPath, string output, Budget budget, CancellationToken cancellation)
    {
        T Read<T>(JsonTypeInfo<T> type)
        {
            var path = Path.GetFullPath(requestPath); RegularFiles.Require(path, budget);
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Command request exceeds4MiB.");
            return CorpusJson.Read(path, type, budget);
        }
        int Save<T>(T result, JsonTypeInfo<T> type)
        {
            budget.Check(); CorpusJson.Write(output, result, type, budget);
            var status = result switch { CorpusOperationReport report => report.Status, CorpusManifest manifest => manifest.Status, CorpusResearchCheckReport report => report.Status, CorpusSeedCheckReport report => report.Status, _ => "" };
            return status.StartsWith("PARTIAL", StringComparison.Ordinal) || status.StartsWith("FAIL", StringComparison.Ordinal) || status.StartsWith("UNKNOWN", StringComparison.Ordinal) ? 1 : 0;
        }
        switch (command)
        {
            case "task prepare": return Save(TaskOperations.Prepare(Read(TaskJsonContext.Default.TaskPreparationRequest), budget), TaskJsonContext.Default.PreparedTask);
            case "bundle freeze": return Save(TaskOperations.Freeze(Read(TaskJsonContext.Default.BundleRequest), budget), TaskJsonContext.Default.BundleReceipt);
            case "archive pack": return Save(TaskOperations.Pack(Read(TaskJsonContext.Default.PackRequest), budget), TaskJsonContext.Default.PackReceipt);
            case "checkpoint verify": return Save(TaskOperations.VerifyCheckpoint(Read(TaskJsonContext.Default.CheckpointVerificationRequest), budget), TaskJsonContext.Default.CheckpointVerificationReceipt);
            case "host inspect": return Save(TaskOperations.InspectHost(Read(TaskJsonContext.Default.HostInspectionRequest), budget), TaskJsonContext.Default.HostInspectionReceipt);
            case "corpus prepare": return Save(CorpusOperations.Prepare(Read(CorpusJsonContext.Default.CorpusPrepareRequest), budget), CorpusJsonContext.Default.CorpusManifest);
            case "corpus compose": return Save(CorpusOperations.Compose(Read(CorpusJsonContext.Default.CorpusComposeRequest), budget), CorpusJsonContext.Default.CorpusManifest);
            case "corpus queue": return Save(CorpusOperations.BuildQueue(Read(CorpusJsonContext.Default.CorpusQueueRequest), budget), CorpusJsonContext.Default.CorpusOperationReport);
            case "corpus collect": return Save(CorpusOperations.Collect(Read(CorpusJsonContext.Default.CorpusTeacherRequest), budget), CorpusJsonContext.Default.CorpusOperationReport);
            case "corpus admission": return Save(CorpusOperations.CheckAdmission(Read(CorpusJsonContext.Default.CorpusAdmissionRequest), budget), CorpusJsonContext.Default.CorpusOperationReport);
            case "corpus dataset": return Save(CorpusOperations.BuildDataset(Read(CorpusJsonContext.Default.CorpusDatasetBuildRequest), budget), CorpusJsonContext.Default.CorpusOperationReport);
            case "corpus research": return Save(CorpusOperations.BuildResearchPlan(Read(CorpusJsonContext.Default.CorpusResearchPlanRequest), budget), CorpusJsonContext.Default.CorpusOperationReport);
            case "corpus research-check": return Save(CorpusOperations.CheckResearch(Read(CorpusJsonContext.Default.CorpusResearchCheckRequest), budget), CorpusJsonContext.Default.CorpusResearchCheckReport);
            case "corpus seed-check": return Save(CorpusOperations.CheckSeeds(Read(CorpusJsonContext.Default.CorpusSeedCheckRequest), budget), CorpusJsonContext.Default.CorpusSeedCheckReport);
            case "corpus inspect": return Save(CorpusOperations.Inspect(Read(CorpusJsonContext.Default.CorpusInspectRequest), budget), CorpusJsonContext.Default.CorpusInspectionReport);
            case "corpus estimate": return Save(CorpusOperations.Estimate(Read(CorpusJsonContext.Default.CorpusEstimateRequest), budget), CorpusJsonContext.Default.CorpusEstimateReport);
            case "corpus bind": return Save(CorpusOperations.Bind(Read(CorpusJsonContext.Default.CorpusBindRequest), budget), CorpusJsonContext.Default.CorpusBinding);
            default: throw new ArgumentException("Unsupported corpus migration command.");
        }
    }
}
