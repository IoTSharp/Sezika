namespace Sezika.RepositoryTrainer;

internal static class CorpusStopPolicy
{
    internal static double SaveReserve(double checkpointSeconds)
    {
        if(!double.IsFinite(checkpointSeconds)||checkpointSeconds<=0)throw new InvalidDataException("Resident mode needs a measured complete checkpoint from this tool/GPU profile.");
        // Includes a new save, retained-checkpoint verification/pruning and final receipts.
        return 2*Math.Max(120,4*checkpointSeconds)+60;
    }
    internal static string? Reason(bool resident,string? stopFile,double remaining,double maximumStepSeconds,double saveReserve)
    {
        if(stopFile is not null&&File.Exists(stopFile))return "requested_safe_stop";
        if(resident&&remaining<=4*maximumStepSeconds+saveReserve)return "checkpoint_deadline_reserve";
        return null;
    }
}
