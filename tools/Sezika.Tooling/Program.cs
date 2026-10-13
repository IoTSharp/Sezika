using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sezika.Tooling;

return ToolingCommand.Run(args);

internal static class ToolingCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("Sezika.Tooling process audit --log-directory <path> [--log-directory <path>] --output <new-json> [--timeout-seconds 1..90] [--cancel-file <path>]");
            Console.WriteLine("Sezika.Tooling archive extract --archive <tar> --sha256 <hex> --destination <new-directory> --output <new-json> [--timeout-seconds 1..1800] [--cancel-file <path>]");
            Console.WriteLine("C#/.NET 10; no subprocesses. Audit is read-only. Extraction preserves partial output on failure; files.sha256 is mandatory.");
            return 0;
        }
        string? output = null;
        var outputAvailable = false;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            StartupGate.Wait(cancellation.Token);
            if (args.Length is < 4 or > 64 || args.Length % 2 != 0 || args.Any(a => a.Length > 32768))
                throw new ArgumentException("Invalid command argument count or size.");
            var command = args[0] + " " + args[1];
            string[] allowed = command switch
            {
                "process audit" => ["--log-directory", "--output", "--timeout-seconds", "--cancel-file"],
                "archive extract" => ["--archive", "--sha256", "--destination", "--output", "--timeout-seconds", "--cancel-file"],
                _ => throw new ArgumentException("Unknown command. Use --help.")
            };
            var options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (var index = 2; index < args.Length; index += 2)
            {
                if (!allowed.Contains(args[index], StringComparer.Ordinal) || string.IsNullOrWhiteSpace(args[index + 1])) throw new ArgumentException("Unknown or empty option.");
                if (!options.TryGetValue(args[index], out var values)) options[args[index]] = values = [];
                if (values.Count != 0 && args[index] != "--log-directory") throw new ArgumentException("Duplicate option.");
                values.Add(args[index + 1]);
            }
            string Required(string name) => options.TryGetValue(name, out var list) ? list[0] : throw new ArgumentException($"Missing {name}.");
            string? Optional(string name) => options.TryGetValue(name, out var list) ? list[0] : null;
            var maximum = command == "process audit" ? 90 : 1800;
            var seconds = Optional("--timeout-seconds") is { } raw ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture) : maximum;
            if (seconds < 1 || seconds > maximum) throw new ArgumentOutOfRangeException(nameof(seconds));
            cancellation.CancelAfter(TimeSpan.FromSeconds(seconds));
            var budget = new Budget(seconds, Optional("--cancel-file"), cancellation.Token);
            output = Path.GetFullPath(Required("--output"));
            SafePaths.NoLinks(output, budget);
            if (Path.Exists(output)) throw new IOException("Output already exists; preserve it.");
            if (!Directory.Exists(Path.GetDirectoryName(output))) throw new DirectoryNotFoundException("Output parent must already exist.");
            outputAvailable = true;
            if (command == "process audit")
            {
                _ = Required("--log-directory");
                var result = ProcessAudit.Run(options["--log-directory"].ToArray(), budget);
                Write(output, result, ToolingJsonContext.Default.AuditReport);
                outputAvailable = false;
                Console.WriteLine($"{result.Status}; live={result.MatchingLive}; unknown={result.Unknown}; receipt={output}");
                return result.MatchingLive == 0 && result.Unknown == 0 ? 0 : 1;
            }
            var destination = Path.GetFullPath(Required("--destination"));
            if (output.StartsWith(Path.TrimEndingDirectorySeparator(destination) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Receipt must be outside the extracted model directory.");
            var archive = ArchiveOperations.Extract(Required("--archive"), Required("--sha256"), destination, budget);
            Write(output, archive, ToolingJsonContext.Default.ArchiveReport);
            outputAvailable = false;
            Console.WriteLine($"{archive.Status}; files={archive.Files}; bytes={archive.Bytes}; receipt={output}");
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or OperationCanceledException or TimeoutException or FormatException or OverflowException)
        {
            var timedOut = error is OperationCanceledException or TimeoutException;
            if (outputAvailable && output is not null && !Path.Exists(output))
                Write(output, new CommandFailure(timedOut ? "CANCELLED_OR_TIMED_OUT" : "FAIL", error.GetType().Name, error.Message), ToolingJsonContext.Default.CommandFailure);
            Console.Error.WriteLine($"tooling: {error.GetType().Name}: {error.Message}");
            return timedOut ? 124 : 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static void Write<T>(string path, T value, JsonTypeInfo<T> type)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, type);
        stream.Flush(flushToDisk: true);
    }
}
