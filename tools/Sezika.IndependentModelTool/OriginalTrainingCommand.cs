using System.Globalization;
using Sezika;

namespace Sezika.IndependentModelTool;

/// <summary>Explicit, finite train-original arguments. Invalid optional values never fall back to defaults.</summary>
public sealed record OriginalTrainingCommand(string Package, string Records, TrainingOutputPaths Outputs,
    MarkerTrainingOptions Options, int TimeoutSeconds, TrainingResumePaths? Resume)
{
    public static OriginalTrainingCommand Parse(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (arguments.Count is < 1 or > 31 || !arguments[0].Equals("train-original", StringComparison.OrdinalIgnoreCase))
            throw Invalid("Expected a bounded train-original command.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "--package", "--records", "--checkpoint", "--head", "--report", "--max-steps", "--seed",
            "--learning-rate", "--training-timeout-seconds", "--timeout-seconds", "--resume-checkpoint",
            "--resume-sha256", "--resume-report", "--resume-report-sha256"
        };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw Invalid("Training argument parsing wall-clock limit expired.");
            if (index + 1 >= arguments.Count || !known.Contains(arguments[index]) ||
                string.IsNullOrWhiteSpace(arguments[index + 1]) || !values.TryAdd(arguments[index], arguments[index + 1]))
                throw Invalid("Unknown, duplicate or missing training option.");
        }
        string Required(string name) => values.TryGetValue(name, out var value) ? value : throw Invalid($"Missing {name}.");
        int Integer(string name, int min, int max, int fallback)
        {
            if (!values.TryGetValue(name, out var text)) return fallback;
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
                throw Invalid($"Invalid {name}; expected integer in [{min},{max}].");
            return value;
        }
        var rate = 0.05f;
        if (values.TryGetValue("--learning-rate", out var rateText) &&
            (!float.TryParse(rateText, NumberStyles.Float, CultureInfo.InvariantCulture, out rate) || !float.IsFinite(rate) || rate <= 0 || rate > 1))
            throw Invalid("Invalid --learning-rate; expected finite number in (0,1].");
        var resumeNames = new[] { "--resume-checkpoint", "--resume-sha256", "--resume-report", "--resume-report-sha256" };
        var resumeCount = resumeNames.Count(values.ContainsKey);
        if (resumeCount is not (0 or 4)) throw Invalid("Resume checkpoint, checkpoint hash, report and report hash must be supplied together.");
        TrainingResumePaths? resume = null;
        if (resumeCount == 4)
        {
            var checkpointHash = Required("--resume-sha256");
            var reportHash = Required("--resume-report-sha256");
            if (!IndependentTrainingWorkflow.IsHash(checkpointHash) || !IndependentTrainingWorkflow.IsHash(reportHash))
                throw Invalid("Resume hashes must be 64 hexadecimal SHA-256 characters.");
            resume = new(Path.GetFullPath(Required("--resume-checkpoint")), checkpointHash,
                Path.GetFullPath(Required("--resume-report")), reportHash);
        }
        return new(Path.GetFullPath(Required("--package")), Path.GetFullPath(Required("--records")),
            new(Path.GetFullPath(Required("--checkpoint")), Path.GetFullPath(Required("--head")), Path.GetFullPath(Required("--report"))),
            new(Integer("--seed", int.MinValue, int.MaxValue, 20260930), Integer("--max-steps", 1, 10_000, 24), rate,
                TimeSpan.FromSeconds(Integer("--training-timeout-seconds", 1, 600, 120))),
            Integer("--timeout-seconds", 1, 1800, 600), resume);
    }

    private static DecisionException Invalid(string message) => new("training_arguments_invalid", message);
}
