using System.Text.Json;

namespace Sezika.Tooling;

internal static class CheckpointScheduleValidation
{
    // Mirrors RepositoryTrainer.CorpusSchedule.Validate. This is structural
    // metadata validation; source order, shard bytes and tensors require their
    // own independently pinned contracts at the trainer admission boundary.
    internal static void Validate(JsonElement schedule, long completedUpdates, long trainingRecordsPerEpoch, int plannedEpochs)
    {
        if (trainingRecordsPerEpoch is < 1 or > 30000000 || plannedEpochs is < 1 or > 64 || completedUpdates < 0 || completedUpdates > checked(trainingRecordsPerEpoch * plannedEpochs))
            throw new InvalidDataException("Schedule global plan bounds differ.");
        var epoch = Integer(schedule, "Epoch"); var within = Integer(schedule, "WithinEpoch");
        var round = Integer(schedule, "Round"); var next = Integer(schedule, "NextSource");
        if (epoch != completedUpdates / trainingRecordsPerEpoch || within != completedUpdates % trainingRecordsPerEpoch || epoch > plannedEpochs || within < 0 || round < 0 || round > trainingRecordsPerEpoch)
            throw new InvalidDataException("Schedule global position differs.");
        if (!schedule.TryGetProperty("Sources", out var sources) || sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() > 128)
            throw new InvalidDataException("Schedule source bound128 differs.");
        long total = 0;
        for (var index = 0; index < sources.GetArrayLength(); index++)
        {
            var source = sources[index];
            var accepted = Integer(source, "Accepted"); var row = Integer(source, "Row"); var shard = Integer(source, "Shard");
            if (accepted < 0 || row < 0 || shard is < 0 or > int.MaxValue || accepted > within - total)
                throw new InvalidDataException("Schedule source position or accepted denominator differs.");
            total += accepted;
        }
        if (total != within || (epoch == plannedEpochs ? sources.GetArrayLength() != 0 || next != 0 || round != 0 : sources.GetArrayLength() == 0 || next < 0 || next >= sources.GetArrayLength()))
            throw new InvalidDataException("Bounded per-source cursor differs from committed global cursor.");
    }

    private static long Integer(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
            throw new InvalidDataException("Schedule integer field missing or invalid: " + property);
        return result;
    }
}
