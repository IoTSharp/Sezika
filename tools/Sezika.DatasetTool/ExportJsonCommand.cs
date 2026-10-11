using System.Diagnostics;
using System.Text;
using DuckDB.NET.Data;

// Offline source decoding only. This command does not participate in inference
// or training kernels and never executes a downloaded dataset loader.
internal static class ExportJsonCommand
{
    internal static int Run(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length != 5 || !int.TryParse(args[3], out var maximum) || maximum is < 1 or > 5_000_000 ||
            !int.TryParse(args[4], out var seconds) || seconds is < 1 or > 1800)
        {
            Console.Error.WriteLine("export-json <parquet> <output-jsonl-or-dash> <max-rows> <timeout-seconds>");
            return 2;
        }
        var watch = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        StreamWriter? file = null;
        try
        {
            var input = Path.GetFullPath(args[1]);
            if (!File.Exists(input)) throw new FileNotFoundException("Parquet source missing.", input);
            TextWriter output = Console.Out;
            if (args[2] != "-")
            {
                file = new StreamWriter(new FileStream(Path.GetFullPath(args[2]), FileMode.CreateNew), new UTF8Encoding(false));
                output = file;
            }
            using var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();
            using (var setup = connection.CreateCommand())
            {
                setup.CommandText = "SET threads=1; SET memory_limit='512MB'";
                setup.CommandTimeout = 10;
                setup.ExecuteNonQuery();
            }
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_json(t) FROM read_parquet(?) AS t";
            command.CommandTimeout = seconds;
            command.Parameters.Add(new DuckDBParameter { Value = input });
            using var reader = command.ExecuteReader();
            var count = 0;
            for (; count < maximum && reader.Read(); count++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (watch.Elapsed.TotalSeconds >= seconds) throw new TimeoutException("Source decode deadline.");
                var row = reader.GetString(0);
                if (row.Length > 4 * 1024 * 1024) throw new InvalidDataException("Source JSON row exceeds 4MiB.");
                output.WriteLine(row);
                if (count % 10000 == 0) Console.Error.WriteLine($"decoded={count + 1};elapsed={watch.Elapsed.TotalSeconds:F1}s");
            }
            // This is an explicit bounded export. The caller decides whether a
            // prefix is a pilot and must compare its count with the source ledger.
            output.Flush();
            Console.Error.WriteLine($"exported={count};maximum={maximum};elapsed={watch.Elapsed.TotalSeconds:F1}s");
            return 0;
        }
        catch (OperationCanceledException) { return 124; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { file?.Dispose(); Console.CancelKeyPress -= handler; }
    }
}
