using System.Globalization;
using System.Text.Json;

namespace Sezika.IndependentPredict;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help")
        {
            Console.WriteLine("Sezika.IndependentPredict export --manifest <json> --manifest-sha256 <hash> --package <directory> --head <asset> --output <new-directory> [--max-records 2..32] [--request-timeout-seconds 1..300] [--timeout-seconds 1..1800]");
            Console.WriteLine("Sezika.IndependentPredict self-test --output <new-directory>");
            return 0;
        }
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        ConsoleCancelEventHandler handler = (_, value) => { value.Cancel = true; lifetime.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            if (args[0] == "self-test") return SelfTest.Run(args, lifetime.Token);
            var options = Options.Parse(args, lifetime.Token);
            lifetime.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            var result = Exporter.Run(options, lifetime.Token);
            Console.WriteLine($"{result.Report.Status}: {result.OutputDirectory}; answered={result.Report.Answered}/{result.Report.Total}");
            return result.Report.Unprocessed > 0 || result.Report.TerminalError == "export_cancelled_or_deadline_exceeded" ? 124 : result.Report.Status == "not_eligible" ? 3 : 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("independent-predict: cancelled; no complete export is claimed."); return 124; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or Sezika.DecisionException)
        { Console.Error.WriteLine($"independent-predict: {exception.Message}"); return 1; }
        finally { Console.CancelKeyPress -= handler; }
    }
}

internal sealed record Options(string Manifest, string ManifestSha256, string Package, string Head, string Output,
    int MaximumRecords = 32, int RequestTimeoutSeconds = 300, int TimeoutSeconds = 1800)
{
    internal static Options Parse(IReadOnlyList<string> args, CancellationToken token = default)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        token.ThrowIfCancellationRequested();
        BoundedFiles.Require(args.Count is >= 11 and <= 17 && args[0] == "export", "Expected bounded export command.");
        var known = new HashSet<string>(StringComparer.Ordinal) { "--manifest", "--manifest-sha256", "--package", "--head", "--output", "--max-records", "--request-timeout-seconds", "--timeout-seconds" };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index += 2)
        {
            token.ThrowIfCancellationRequested();
            BoundedFiles.Require(clock.Elapsed < TimeSpan.FromSeconds(5), "Option parsing exceeded its 5 second wall-clock limit.");
            BoundedFiles.Require(index + 1 < args.Count && known.Contains(args[index]) && !string.IsNullOrWhiteSpace(args[index + 1]) &&
                !args[index + 1].StartsWith("--", StringComparison.Ordinal) && values.TryAdd(args[index], args[index + 1]), "Unknown, duplicate or missing option.");
        }
        string Required(string name) => values.TryGetValue(name, out var value) ? value : throw new InvalidDataException($"Missing {name}.");
        int Number(string name, int minimum, int maximum, int fallback)
        {
            if (!values.TryGetValue(name, out var text)) return fallback;
            BoundedFiles.Require(int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= minimum && value <= maximum, "Invalid bounded numeric option.");
            return value;
        }
        var hash = Required("--manifest-sha256");
        BoundedFiles.Require(BoundedFiles.IsHash(hash), "Manifest SHA-256 must contain 64 hexadecimal digits.");
        return new(Path.GetFullPath(Required("--manifest")), hash, Path.GetFullPath(Required("--package")), Path.GetFullPath(Required("--head")),
            Path.GetFullPath(Required("--output")), Number("--max-records", 2, 32, 32), Number("--request-timeout-seconds", 1, 300, 300), Number("--timeout-seconds", 1, 1800, 1800));
    }
}
