using System.Diagnostics;
using System.Text.Json;
using DuckDB.NET.Data;

namespace Sezika.DatasetTool;

public static class CorpusRowReader
{
    public static IEnumerable<JsonElement> Read(string path, int maximumRows, int timeoutSeconds, CancellationToken token)
    {
        if (maximumRows is < 1 or > 5_000_001 || timeoutSeconds is < 1 or > 1800) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        var clock = Stopwatch.StartNew();
        using var connection = new DuckDBConnection("Data Source=:memory:"); connection.Open();
        using (var setup = connection.CreateCommand()) { setup.CommandText = "SET threads=1; SET memory_limit='512MB'"; setup.CommandTimeout = 10; setup.ExecuteNonQuery(); }
        using var command = connection.CreateCommand(); command.CommandText = "SELECT to_json(t) FROM read_parquet(?) AS t"; command.CommandTimeout = timeoutSeconds; command.Parameters.Add(new DuckDBParameter { Value = Path.GetFullPath(path) });
        using var cancel = token.Register(() => command.Cancel()); using var reader = command.ExecuteReader();
        for (var index = 0; index < maximumRows && reader.Read(); index++)
        {
            token.ThrowIfCancellationRequested(); if (clock.Elapsed.TotalSeconds >= timeoutSeconds) throw new TimeoutException("Parquet decoder deadline.");
            var text = reader.GetString(0); if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException("Parquet row byte bound.");
            using var document = JsonDocument.Parse(text); yield return document.RootElement.Clone();
        }
    }
}
