using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sezika.Tooling;

return ToolingCommand.Run(args);

internal static class ToolingCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length == 3 && args[0] == "process" && args[1] is "broker" or "watchdog")
        {
            var configuration = args[2];
            try { return args[1] == "broker" ? ProcessExecutor.RunBroker(configuration, CancellationToken.None) : ProcessExecutor.RunWatchdog(configuration, CancellationToken.None); }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or System.ComponentModel.Win32Exception or TimeoutException or OperationCanceledException)
            { Console.Error.WriteLine($"broker: {error.GetType().Name}: {error.Message}"); return 127; }
        }
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("Sezika.Tooling process audit --log-directory <path> [--log-directory <path>] --output <new-json> [--timeout-seconds 1..90] [--cancel-file <path>]");
            Console.WriteLine("Sezika.Tooling archive extract --archive <tar> --sha256 <hex> --destination <new-directory> --output <new-json> [--timeout-seconds 1..1800] [--cancel-file <path>]");
            Console.WriteLine("Sezika.Tooling inventory freeze --inventory <baseline-csv> --output <new-json> [--timeout-seconds 1..90]");
            Console.WriteLine("Sezika.Tooling process run --request <execution-json> --output <new-json> [--timeout-seconds 1..1800] [--cancel-file <path>]");
            Console.WriteLine("C#/.NET 10. Audit is read-only. Extraction preserves partial output on failure; files.sha256 is mandatory. Process execution requires explicit bounded native targets.");
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
                "inventory freeze" => ["--inventory", "--output", "--timeout-seconds", "--cancel-file"],
                "process run" => ["--request", "--output", "--timeout-seconds", "--cancel-file"],
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
            var maximum = command is "process audit" or "inventory freeze" ? 90 : command is "process run" or "task resident" ? 86400 : 1800;
            var seconds = Optional("--timeout-seconds") is { } raw ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture) : Math.Min(maximum, 1800);
            if (seconds < 1 || seconds > maximum) throw new ArgumentOutOfRangeException(nameof(seconds));
            cancellation.CancelAfter(TimeSpan.FromSeconds(seconds));
            var budget = new Budget(seconds, Optional("--cancel-file"), cancellation.Token);
            output = Path.GetFullPath(Required("--output"));
            SafePaths.NoLinks(output, budget);
            if (Path.Exists(output)) throw new IOException("Output already exists; preserve it.");
            if (!Directory.Exists(Path.GetDirectoryName(output))) throw new DirectoryNotFoundException("Output parent must already exist.");
            outputAvailable = true;
            if (command == "process run")
            {
                var requestPath = Path.GetFullPath(Required("--request"));
                SafePaths.NoLinks(requestPath, budget);
                using var input = File.OpenRead(requestPath);
                if (input.Length > 2 * 1024 * 1024) throw new InvalidDataException("Execution configuration byte bound2MiB.");
                var request = JsonSerializer.Deserialize(input, ExecutionJsonContext.Default.ExecutionRequest) ?? throw new InvalidDataException("Execution configuration missing.");
                if (OperatingSystem.IsLinux() && request.BrokerExecutable is null && Environment.ProcessPath is { } executable && !Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) request.BrokerExecutable = executable;
                var execution = ProcessExecutor.ExecuteAsync(request, budget, cancellation.Token).GetAwaiter().GetResult();
                Write(output, execution, ExecutionJsonContext.Default.ExecutionResult);
                outputAvailable = false;
                Console.WriteLine($"{execution.Status}; exit={execution.ExitCode}; remaining={execution.RemainingProcesses}; receipt={output}");
                return execution.Status == "PASS" ? 0 : 1;
            }
            if (command == "inventory freeze")
            {
                var inventory = InventoryOperations.Freeze(Required("--inventory"), budget);
                Write(output, inventory, InventoryJsonContext.Default.MigrationInventory);
                outputAvailable = false;
                Console.WriteLine($"{inventory.Status}; entries={inventory.Entries}; receipt={output}");
                return 0;
            }
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
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or OperationCanceledException or TimeoutException or FormatException or OverflowException or KeyNotFoundException or System.ComponentModel.Win32Exception or HttpRequestException)
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
