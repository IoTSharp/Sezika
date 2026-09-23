namespace Sezika;

public sealed record DecisionStatus
{
    public required string Provider { get; init; }
    public required string Backend { get; init; }
    public required string Model { get; init; }
    public required string ModelRevision { get; init; }
    public required bool AssetsVerified { get; init; }
    public required bool SchemaValid { get; init; }
    public required bool SessionLoaded { get; init; }
    public required string CalibrationStatus { get; init; }
    public required bool MultilingualQualityVerified { get; init; }
    public required bool AotSmokeVerified { get; init; }
    public required bool LatencyVerified { get; init; }
}

public static class DecisionStatusFactory
{
    public static DecisionStatus FromModel(DecisionModel model, string calibrationStatus = "uncalibrated") => new()
    {
        Provider = "managed-decision",
        Backend = model.Backend,
        Model = model.ModelId,
        ModelRevision = model.Revision,
        AssetsVerified = false,
        SchemaValid = true,
        SessionLoaded = true,
        CalibrationStatus = calibrationStatus,
        MultilingualQualityVerified = false,
        AotSmokeVerified = false,
        LatencyVerified = false,
    };
}
