using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DuckDB.NET.Data;

const string ParquetSha256 = "ae342ff12bb84b84b95f468abf5db6cb7c7bd578271299fe9c99be75b8132f4d";
const string SelectionSha256 = "4e7359ab58484e4bf716e916f688360f5dd3da41e3760a5d3cdeef03ee553069";
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: Sezika.DatasetTool <pinned-paws-test.parquet> <pinned-nimble-selection.json> <output-dir>");
    return 2;
}

try
{
    var watch = Stopwatch.StartNew();
    var parquetPath = Path.GetFullPath(args[0]);
    var selectionPath = Path.GetFullPath(args[1]);
    var outputPath = Path.GetFullPath(args[2]);
    VerifyHash(parquetPath, ParquetSha256);
    VerifyHash(selectionPath, SelectionSha256);
    var selection = JsonSerializer.Deserialize(File.ReadAllText(selectionPath), DatasetJsonContext.Default.Selection)
        ?? throw new InvalidDataException("Selection manifest is empty.");
    if (selection.Count != 250 || selection.Ids.Length != 250)
        throw new InvalidDataException("Expected 250 frozen PAWS IDs.");
    var wanted = new HashSet<string>(selection.Ids, StringComparer.Ordinal);
    if (wanted.Count != 250) throw new InvalidDataException("Duplicate selection ID.");

    var found = new Dictionary<string, PawsRow>(StringComparer.Ordinal);
    using (var connection = new DuckDBConnection("Data Source=:memory:"))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, sentence1, sentence2, label FROM read_parquet(?)";
        command.CommandTimeout = 30;
        command.Parameters.Add(new DuckDBParameter { Value = parquetPath });
        using var reader = command.ExecuteReader();
        var read = 0;
        while (reader.Read())
        {
            if (++read > 8000 || watch.Elapsed > TimeSpan.FromSeconds(60))
                throw new InvalidDataException("PAWS row or wall-clock limit exceeded.");
            var id = "paws-" + reader.GetInt32(0);
            if (!wanted.Contains(id)) continue;
            var row = new PawsRow(reader.GetString(1), reader.GetString(2), reader.GetInt32(3));
            if (!found.TryAdd(id, row)) throw new InvalidDataException($"Duplicate source ID: {id}");
        }
        if (read != 8000) throw new InvalidDataException($"Expected 8000 source rows; found {read}.");
    }
    if (found.Count != 250) throw new InvalidDataException($"Found only {found.Count}/250 selected IDs.");

    var lines = new List<string>(250);
    var trueCount = 0;
    foreach (var id in selection.Ids)
    {
        if (watch.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("PAWS conversion exceeded 60 seconds.");
        var row = found[id];
        if (row.Label is not (0 or 1) || string.IsNullOrWhiteSpace(row.Sentence1) ||
            string.IsNullOrWhiteSpace(row.Sentence2)) throw new InvalidDataException($"Invalid PAWS row: {id}");
        var target = row.Label == 1;
        if (target) trueCount++;
        var record = new EvaluationRecord(id, id, "paws", "paws",
            new EvaluationInput(new Dictionary<string, string>(StringComparer.Ordinal)
                { ["sentence_1"] = row.Sentence1, ["sentence_2"] = row.Sentence2 },
                new Dictionary<string, EvaluationQuestion>(StringComparer.Ordinal)
                {
                    ["decision"] = new EvaluationQuestion("noul",
                        "Do these two sentences express the same situation with the same participants and roles?",
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["true"] = "Both sentences have the same meaning.",
                            ["false"] = "The meaning or participant roles differ.",
                        }),
                }),
            new EvaluationReference(target, "PAWS labeled_final test human annotation"));
        lines.Add(JsonSerializer.Serialize(record, DatasetJsonContext.Default.EvaluationRecord));
    }
    if (trueCount != 121) throw new InvalidDataException($"Expected 121 true labels; found {trueCount}.");

    Directory.CreateDirectory(outputPath);
    var dataPath = Path.Combine(outputPath, "eval.jsonl");
    var manifestPath = Path.Combine(outputPath, "source.json");
    if (File.Exists(dataPath) || File.Exists(manifestPath))
        throw new IOException("The selected output files already exist.");
    File.WriteAllLines(dataPath, lines, new UTF8Encoding(false));
    var dataSha256 = HashFile(dataPath);
    var manifest = new SourceManifest("google-research-datasets/paws",
        "161ece9501cf0a11f3e48bd356eaa82de46d6a09", "labeled_final/test",
        ParquetSha256, SelectionSha256, 250, trueCount, 250 - trueCount,
        "sezika-paws-paraphrase-v1", "Google PAWS: may be freely used for any purpose; acknowledge Google LLC",
        dataSha256, DateTimeOffset.UtcNow);
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, DatasetJsonContext.Default.SourceManifest),
        new UTF8Encoding(false));
    Console.WriteLine($"PAWS frozen subset: 250/250, true {trueCount}, false {250 - trueCount}, SHA-256 {dataSha256}; elapsed {watch.Elapsed.TotalSeconds:F1}s");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"dataset-tool: {exception.GetType().Name}: {exception.Message}");
    return 1;
}

static void VerifyHash(string path, string expected)
{
    var actual = HashFile(path);
    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException($"SHA-256 mismatch: {path}: {actual}");
}

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

internal sealed record Selection(int Count, string[] Ids);
internal sealed record PawsRow(string Sentence1, string Sentence2, int Label);
internal sealed record EvaluationRecord(string Id, string Family, string SourceFamily, string Domain,
    EvaluationInput Input, EvaluationReference Reference);
internal sealed record EvaluationInput(Dictionary<string, string> State,
    Dictionary<string, EvaluationQuestion> Questions);
internal sealed record EvaluationQuestion(string Type, string Instructions, Dictionary<string, string> Criteria);
internal sealed record EvaluationReference(bool Target, string Source);
internal sealed record SourceManifest(string Source, string SourceRevision, string SourceSplit,
    string SourceParquetSha256, string SelectionSha256, int SelectedIds, int TrueLabels, int FalseLabels,
    string PromptVersion, string License, string EvalSha256, DateTimeOffset GeneratedUtc);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(Selection))]
[JsonSerializable(typeof(EvaluationRecord))]
[JsonSerializable(typeof(SourceManifest))]
internal partial class DatasetJsonContext : JsonSerializerContext { }
