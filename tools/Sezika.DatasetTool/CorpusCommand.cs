using Sezika.Tooling;

namespace Sezika.DatasetTool;

public static class CorpusCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 3 || args[0] != "corpus" || args[1] is not "prepare" and not "inspect" and not "xz-checks") throw new ArgumentException("corpus prepare|inspect <request-json>; corpus xz-checks <new-owned-directory>");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1800));
        ConsoleCancelEventHandler handler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); }; Console.CancelKeyPress += handler;
        try
        {
            if (args[1] == "xz-checks") { Console.WriteLine("PASS XZ checks=" + XZRowReader.CheckFixture(args[2])); return 0; }
            var budget = new Budget(1800, null, cancellation.Token);
            IEnumerable<System.Text.Json.JsonElement> ReadRows(string path, int count) => path.EndsWith(".xz", StringComparison.Ordinal) ? XZRowReader.Read(path, count, 1800, cancellation.Token) : CorpusRowReader.Read(path, count, 1800, cancellation.Token);
            if (args[1] == "inspect") { var inspect = CorpusJson.Read(args[2], CorpusJsonContext.Default.CorpusInspectRequest, budget); var report = CorpusOperations.Inspect(inspect, budget, ReadRows); Console.WriteLine(report.Status); return 0; }
            var request = CorpusJson.Read(args[2], CorpusJsonContext.Default.CorpusPrepareRequest, budget);
            var result = CorpusOperations.Prepare(request, budget, ReadRows);
            Console.WriteLine($"{result.Status}; records={result.RecordsWritten}; formal_training_admitted=false"); return 0;
        }
        catch (OperationCanceledException) { return 124; }
        catch (TimeoutException) { return 124; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Console.CancelKeyPress -= handler; }
    }
}
