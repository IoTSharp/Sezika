using System.Diagnostics;
using System.Text.Json;

namespace Sezika.IndependentNumericCompare;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "help")
        {
            Console.WriteLine("IndependentNumericCompare compare --actual <capture.json> --reference <reference.json> --contract <json> --contract-sha256 <hash> --oracle-program <file> --oracle-program-sha256 <hash> --upstream-source <file> --upstream-source-sha256 <hash> --output <new.json> [--timeout-seconds 1..60] [--cancel-file <file>]");
            Console.WriteLine("IndependentNumericCompare self-test --contract <json> --output <new-directory> [--maximum-cases 1..16] [--timeout-seconds 1..60]");
            Console.WriteLine("IndependentNumericCompare compare-port --actual <capture.json> --reference <port-v2.json> --contract <run-contract.json> --contract-sha256 <hash> --port-proof <pinned-proof.json> --output <new-json> [--timeout-seconds 1..60] [--cancel-file <file>]");
            Console.WriteLine("IndependentNumericCompare self-test-port --contract <port-v2-template.json> --output <new-directory> [--maximum-cases 1..16] [--timeout-seconds 1..60]");
            return 0;
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            var options = Options.Parse(args);
            using var budget = new Budget(options.TimeoutSeconds, options.CancelFile, cancellation.Token);
            if (options.Command == "self-test") return SelfTests.Run(options, budget);
            if (options.Command == "self-test-port") return SelfTests.RunPort(options, budget);
            var report = options.Command == "compare-port" ? Comparison.RunPort(options, budget) : Comparison.Run(options, budget);
            Io.WriteNew(options.Output, report, CompareJsonContext.Default.ComparisonReport, budget);
            Console.WriteLine($"Compared {report.ComparedCount}/26; failures={report.FailedComparisons}; fixture_only={report.FixtureOnly}; accepted={report.AcceptedIndependentReference}");
            return report.Passed ? 0 : 1;
        }
        catch (OperationCanceledException e) { Console.Error.WriteLine(e.Message); return 124; }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or OverflowException or FormatException)
        { Console.Error.WriteLine($"Rejected: {e.GetType().Name}: {e.Message}"); return 2; }
        finally { Console.CancelKeyPress -= handler; }
    }
}

internal sealed record Options(string Command, string Actual, string Reference, string Contract, string ContractSha256,
    string OracleProgram, string OracleProgramSha256, string UpstreamSource, string UpstreamSourceSha256,
    string Output, int TimeoutSeconds, string? CancelFile, int MaximumCases, string? PortProof = null)
{
    internal static Options Parse(string[] args)
    {
        Require.That(args.Length is >= 3 and <= 29 && args.Length % 2 == 1 && args[0] is "compare" or "self-test" or "compare-port" or "self-test-port", "Invalid command or argument count.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(["--actual", "--reference", "--contract", "--contract-sha256", "--oracle-program", "--oracle-program-sha256", "--upstream-source", "--upstream-source-sha256", "--output", "--timeout-seconds", "--cancel-file", "--maximum-cases", "--port-proof"], StringComparer.Ordinal);
        var parseWatch = Stopwatch.StartNew();
        for (var i = 1; i < args.Length; i += 2)
        {
            Require.That(parseWatch.Elapsed.TotalSeconds < 5, "Argument parsing deadline exceeded.");
            Require.That(allowed.Contains(args[i]) && values.TryAdd(args[i], args[i + 1]) && !string.IsNullOrWhiteSpace(args[i + 1]), "Unknown, duplicate or empty argument.");
        }
        string Get(string key, bool required = true) => values.TryGetValue(key, out var value) ? value : required ? throw new InvalidDataException($"Missing {key}.") : "";
        var timeout = values.TryGetValue("--timeout-seconds", out var raw) ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture) : 60;
        var maximum = values.TryGetValue("--maximum-cases", out raw) ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture) : 16;
        Require.That(timeout is >= 1 and <= 60 && maximum is >= 1 and <= 16, "Timeout/self-test count exceeded.");
        var compare = args[0] is "compare" or "compare-port"; var legacy = args[0] == "compare"; var port = args[0] == "compare-port";
        if (compare) Require.That(!values.ContainsKey("--maximum-cases"), "maximum-cases is only for self-test.");
        if (!port) Require.That(!values.ContainsKey("--port-proof"), "port-proof belongs only to compare-port.");
        if (port) Require.That(new[] { "--oracle-program", "--oracle-program-sha256", "--upstream-source", "--upstream-source-sha256" }.All(key => !values.ContainsKey(key)), "Port proof does not use legacy upstream flags.");
        return new(args[0], Get("--actual", compare), Get("--reference", compare), Get("--contract"), Get("--contract-sha256", compare),
            Get("--oracle-program", legacy), Get("--oracle-program-sha256", legacy), Get("--upstream-source", legacy), Get("--upstream-source-sha256", legacy),
            Get("--output"), timeout, values.GetValueOrDefault("--cancel-file"), maximum, port ? Get("--port-proof") : null);
    }
}

internal sealed class Budget : IDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly string? _cancelFile;
    internal DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    internal double ElapsedSeconds => _watch.Elapsed.TotalSeconds;
    internal Budget(int seconds, string? cancelFile, CancellationToken token)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _cancellation.CancelAfter(TimeSpan.FromSeconds(seconds)); _cancelFile = cancelFile;
    }
    internal void Check()
    {
        _cancellation.Token.ThrowIfCancellationRequested();
        if (_cancelFile is not null && File.Exists(_cancelFile)) throw new OperationCanceledException("Cancel file exists.");
    }
    internal ProcessIdentity Identity()
    {
        using var process = Process.GetCurrentProcess();
        return new(Environment.ProcessId, process.StartTime.ToUniversalTime(), Environment.CommandLine, "Use outer bounded runner PID/start/command/parent-chain evidence.");
    }
    public void Dispose() => _cancellation.Dispose();
}

internal static class Require
{
    internal static void That(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
