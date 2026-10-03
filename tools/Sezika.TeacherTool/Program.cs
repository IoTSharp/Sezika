using System.Security.Cryptography;
using System.Text.Json;

const int MaxCatalogBytes = 1 * 1024 * 1024;
const int MaxRecords = 32;
const int MaxRecordBytes = 65_536;

var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
try
{
    if (command == "validate-catalog")
    {
        var catalogPath = Path.GetFullPath(Required("--catalog"));
        var reportPath = Path.GetFullPath(Required("--report"));
        var catalogHash = ValidateCatalog(catalogPath, out var candidate);
        WriteCatalogReport(reportPath, catalogPath, catalogHash, candidate);
        Console.WriteLine($"catalog_valid candidate={candidate.Id} status={candidate.Status} sha256={catalogHash} report={reportPath}");
        return candidate.Status == "verified" ? 0 : 3;
    }

    if (command == "validate-records")
    {
        var recordsPath = Path.GetFullPath(Required("--records"));
        var outputPath = Path.GetFullPath(Required("--report"));
        var result = ValidateRecords(recordsPath);
        WriteRecordsReport(outputPath, recordsPath, result);
        Console.WriteLine($"records_valid total={result.Total} accepted={result.Accepted} rejected={result.Rejected} sha256={result.Hash} report={outputPath}");
        return result.Rejected == 0 ? 0 : 3;
    }

    Console.WriteLine("Sezika.TeacherTool validate-catalog --catalog <catalog.json> --report <report.json>");
    Console.WriteLine("Sezika.TeacherTool validate-records --records <teacher-records.jsonl> --report <report.json>");
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("teacher-tool: cancelled or exceeded its bounded operation budget");
    return 124;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"teacher-tool: {exception.Message}");
    return 1;
}

static string Required(string name)
{
    var commandLine = Environment.GetCommandLineArgs();
    for (var index = 1; index + 1 < commandLine.Length; index++)
        if (commandLine[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return commandLine[index + 1];
    throw new ArgumentException($"Missing {name}.");
}

static string ValidateCatalog(string path, out CatalogCandidate candidate)
{
    if (!File.Exists(path)) throw new FileNotFoundException("Teacher catalog is missing.", path);
    var info = new FileInfo(path);
    if (info.Length is <= 0 or > MaxCatalogBytes) throw new InvalidDataException("Teacher catalog exceeds the 1 MiB bound.");
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 16 });
    var root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("candidate", out var item))
        throw new InvalidDataException("Catalog must contain a candidate object.");
    candidate = new CatalogCandidate(
        RequiredText(item, "id"), RequiredText(item, "service"), RequiredText(item, "model"),
        RequiredText(item, "revision"), RequiredText(item, "license"), RequiredText(item, "status"),
        item.TryGetProperty("endpoint", out var endpoint) && endpoint.ValueKind == JsonValueKind.String ? endpoint.GetString() : null,
        item.TryGetProperty("terms_evidence", out var terms) && terms.ValueKind == JsonValueKind.String ? terms.GetString() : null);
    if (candidate.Id.Length > 128 || candidate.Service.Length > 128 || candidate.Model.Length > 256 || candidate.Revision.Length > 256)
        throw new InvalidDataException("Catalog identity field is too long.");
    if (candidate.Status is not ("unverified" or "verified" or "blocked")) throw new InvalidDataException("Catalog status is invalid.");
    if (candidate.Status == "verified" && (string.IsNullOrWhiteSpace(candidate.Endpoint) || string.IsNullOrWhiteSpace(candidate.TermsEvidence)))
        throw new InvalidDataException("A verified candidate requires endpoint and terms evidence.");
    if (!candidate.Id.Equals("qwen35-9b-q4km", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Only the audited qwen35-9b-q4km candidate is permitted by this contract.");
    if (!candidate.Service.Equals("IoTSharp/Tomur", StringComparison.OrdinalIgnoreCase) ||
        !candidate.Model.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("The candidate service/model does not match the S4-10 contract.");
    return hash;
}

static string RequiredText(JsonElement item, string name)
{
    if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        throw new InvalidDataException($"Catalog field '{name}' is required.");
    return value.GetString()!;
}

static RecordValidation ValidateRecords(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException("Teacher records are missing.", path);
    using var input = File.OpenRead(path);
    var hash = Convert.ToHexString(SHA256.HashData(input));
    var total = 0; var accepted = 0; var rejected = 0; var ids = new HashSet<string>(StringComparer.Ordinal);
    using var reader = new StreamReader(path);
    while (reader.ReadLine() is { } line)
    {
        if (++total > MaxRecords) throw new InvalidDataException("Teacher records exceed the 32-record exploration bound.");
        if (line.Length is 0 or > MaxRecordBytes) throw new InvalidDataException($"Teacher record {total} is empty or oversized.");
        using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        var id = root.TryGetProperty("record_id", out var idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
        var status = root.TryGetProperty("review_status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String ? statusElement.GetString() : null;
        var responseHash = root.TryGetProperty("response_sha256", out var hashElement) && hashElement.ValueKind == JsonValueKind.String ? hashElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || status is not ("pending" or "accepted" or "rejected" or "failed" or "abstained") ||
            (status is "accepted" or "rejected") && !IsSha256(responseHash)) rejected++; else accepted++;
    }
    return new RecordValidation(total, accepted, rejected, hash);
}

static bool IsSha256(string? value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

static void WriteCatalogReport(string path, string sourcePath, string hash, CatalogCandidate candidate)
{
    EnsureNew(path);
    var report = new CatalogReport(1, candidate.Status == "verified" ? "verified" : "blocked_unverified", sourcePath, hash, candidate);
    File.WriteAllText(path, JsonSerializer.Serialize(report, TeacherJsonContext.Default.CatalogReport));
}

static void WriteRecordsReport(string path, string sourcePath, RecordValidation result)
{
    EnsureNew(path);
    var report = new RecordsReport(1, result.Rejected == 0 ? "valid_pending_human_review" : "rejected_records_present", sourcePath, result.Hash, result.Total, result.Accepted, result.Rejected, false);
    File.WriteAllText(path, JsonSerializer.Serialize(report, TeacherJsonContext.Default.RecordsReport));
}

static void EnsureNew(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    if (File.Exists(path)) throw new IOException($"Output already exists: {path}");
}

record CatalogCandidate(string Id, string Service, string Model, string Revision, string License, string Status, string? Endpoint, string? TermsEvidence);
record RecordValidation(int Total, int Accepted, int Rejected, string Hash);
record CatalogReport(int SchemaVersion, string Status, string SourcePath, string SourceSha256, CatalogCandidate Candidate);
record RecordsReport(int SchemaVersion, string Status, string SourcePath, string SourceSha256, int Total, int Accepted, int Rejected, bool TeacherLabelsPermittedForTraining);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower)]
[System.Text.Json.Serialization.JsonSerializable(typeof(CatalogReport))]
[System.Text.Json.Serialization.JsonSerializable(typeof(RecordsReport))]
partial class TeacherJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
