using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

const int MaxCatalogBytes = 1 * 1024 * 1024;
const int MaxRecords = 32;
const int MaxRecordBytes = 65_536;
const int MaxRecordsFileBytes = (MaxRecords * (MaxRecordBytes + 1)) + 1;
const int DefaultTimeoutMs = 30_000;
const int MaxTimeoutMs = 120_000;

var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var timeoutMs = ReadBoundedInt("--timeout-ms", DefaultTimeoutMs, 100, MaxTimeoutMs);
    cancellation.CancelAfter(timeoutMs);
    var budget = new ValidationBudget(cancellation.Token, Stopwatch.StartNew(), timeoutMs);

    if (command == "validate-catalog")
    {
        var catalogPath = Path.GetFullPath(Required("--catalog"));
        var reportPath = Path.GetFullPath(Required("--report"));
        var catalogHash = ValidateCatalog(catalogPath, out var candidate, budget);
        WriteCatalogReport(reportPath, catalogPath, catalogHash, candidate);
        Console.WriteLine($"catalog_valid candidate={candidate.Id} status={candidate.Status} sha256={catalogHash} report={reportPath}");
        return candidate.Status == "verified" ? 0 : 3;
    }

    if (command == "validate-records")
    {
        var recordsPath = Path.GetFullPath(Required("--records"));
        var outputPath = Path.GetFullPath(Required("--report"));
        var result = ValidateRecords(recordsPath, budget);
        WriteRecordsReport(outputPath, recordsPath, result);
        Console.WriteLine($"records_valid total={result.Total} accepted={result.Accepted} rejected={result.Rejected} failed={result.Failed} abstained={result.Abstained} sha256={result.Hash} report={outputPath}");
        return result.Rejected == 0 ? 0 : 3;
    }

    Console.WriteLine("Sezika.TeacherTool validate-catalog --catalog <catalog.json> --report <report.json> [--timeout-ms <100..120000>]");
    Console.WriteLine("Sezika.TeacherTool validate-records --records <teacher-records.jsonl> --report <report.json> [--timeout-ms <100..120000>]");
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

static int ReadBoundedInt(string name, int fallback, int minimum, int maximum)
{
    var value = Optional(name);
    if (value is null) return fallback;
    if (!int.TryParse(value, out var parsed) || parsed < minimum || parsed > maximum)
        throw new ArgumentException($"{name} must be an integer in [{minimum}, {maximum}].");
    return parsed;
}

static string? Optional(string name)
{
    var commandLine = Environment.GetCommandLineArgs();
    for (var index = 1; index + 1 < commandLine.Length; index++)
        if (commandLine[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return commandLine[index + 1];
    return null;
}

static string Required(string name) => Optional(name) ?? throw new ArgumentException($"Missing {name}.");

static string ValidateCatalog(string path, out CatalogCandidate candidate, ValidationBudget budget)
{
    budget.Check();
    if (!File.Exists(path)) throw new FileNotFoundException("Teacher catalog is missing.", path);
    var info = new FileInfo(path);
    if (info.Length is <= 0 or > MaxCatalogBytes) throw new InvalidDataException("Teacher catalog exceeds the 1 MiB bound.");
    var bytes = File.ReadAllBytes(path);
    budget.Check();
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
    var root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("candidate", out var item) || item.ValueKind != JsonValueKind.Object)
        throw new InvalidDataException("Catalog must contain a candidate object.");
    candidate = new CatalogCandidate(
        RequiredText(item, "id"), RequiredText(item, "service"), RequiredText(item, "model"),
        RequiredText(item, "revision"), RequiredText(item, "license"), RequiredText(item, "status"),
        OptionalText(item, "endpoint"), OptionalText(item, "terms_evidence"));
    if (candidate.Id.Length > 128 || candidate.Service.Length > 128 || candidate.Model.Length > 256 || candidate.Revision.Length > 256 || candidate.License.Length > 256)
        throw new InvalidDataException("Catalog identity field is too long.");
    if (candidate.Status is not ("unverified" or "verified" or "blocked")) throw new InvalidDataException("Catalog status is invalid.");
    if (candidate.Status == "verified")
    {
        if (!Uri.TryCreate(candidate.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            throw new InvalidDataException("A verified candidate requires an http(s) endpoint.");
        if (string.IsNullOrWhiteSpace(candidate.TermsEvidence)) throw new InvalidDataException("A verified candidate requires terms evidence.");
    }
    if (!candidate.Id.Equals("qwen35-9b-q4km", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Only the audited qwen35-9b-q4km candidate is permitted by this contract.");
    if (!candidate.Service.Equals("IoTSharp/Tomur", StringComparison.OrdinalIgnoreCase) ||
        !candidate.Model.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("The candidate service/model does not match the S4-10 contract.");
    budget.Progress("catalog", 1, 1);
    return hash;
}

static string RequiredText(JsonElement item, string name)
{
    if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        throw new InvalidDataException($"Catalog field '{name}' is required.");
    return value.GetString()!;
}

static string? OptionalText(JsonElement item, string name) =>
    item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

static RecordValidation ValidateRecords(string path, ValidationBudget budget)
{
    budget.Check();
    if (!File.Exists(path)) throw new FileNotFoundException("Teacher records are missing.", path);
    var info = new FileInfo(path);
    if (info.Length > MaxRecordsFileBytes) throw new InvalidDataException($"Teacher records exceed the {MaxRecords}-record bounded file size.");
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    budget.Check();
    var counts = new StatusCounts();
    var ids = new HashSet<string>(StringComparer.Ordinal);
    using var reader = new StreamReader(path);
    for (var lineNumber = 1; reader.ReadLine() is { } line; lineNumber++)
    {
        budget.Check();
        if (lineNumber > MaxRecords) throw new InvalidDataException("Teacher records exceed the 32-record exploration bound.");
        counts.Total++;
        if (line.Length is 0 or > MaxRecordBytes)
        {
            counts.Rejected++;
            Console.Error.WriteLine($"progress records={lineNumber}/{MaxRecords} status=rejected reason=size");
            continue;
        }
        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 16 });
            ValidateRecord(document.RootElement, ids, counts);
        }
        catch (JsonException) { counts.Rejected++; }
        catch (InvalidDataException) { counts.Rejected++; }
        Console.Error.WriteLine($"progress records={lineNumber}/{MaxRecords} accepted={counts.Accepted} rejected={counts.Rejected} failed={counts.Failed} abstained={counts.Abstained}");
    }
    return new RecordValidation(counts.Total, counts.Accepted, counts.Rejected, counts.Failed, counts.Abstained, hash);
}

static void ValidateRecord(JsonElement root, HashSet<string> ids, StatusCounts counts)
{
    if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Record must be an object.");
    var id = RequiredRecordText(root, "record_id", 128);
    if (!ids.Add(id)) throw new InvalidDataException("record_id must be unique.");
    var status = RequiredRecordText(root, "review_status", 32);
    if (status is not ("pending" or "accepted" or "rejected" or "failed" or "abstained")) throw new InvalidDataException("review_status is invalid.");
    var split = RequiredRecordText(root, "split", 32);
    if (split is not ("train" or "development" or "calibration" or "audit")) throw new InvalidDataException("teacher records cannot target sealed_test or an unknown split.");
    RequiredRecordText(root, "source", 256);
    RequiredRecordText(root, "family_id", 128);
    RequiredRecordHash(root, "request_sha256");
    RequiredRecordHash(root, "prompt_sha256");
    RequiredRecordText(root, "model_id", 256);
    RequiredRecordText(root, "model_revision", 256);
    var responseHash = OptionalText(root, "response_sha256");
    if (status is "accepted" or "rejected")
    {
        if (!IsSha256(responseHash)) throw new InvalidDataException("accepted/rejected records require response_sha256.");
        RequiredRecordText(root, "human_review_id", 128);
    }
    if (status == "failed") counts.Failed++;
    else if (status == "abstained") counts.Abstained++;
    else if (status == "accepted") counts.Accepted++;
    else if (status == "rejected") counts.Rejected++;
}

static string RequiredRecordText(JsonElement root, string name, int maxLength)
{
    var value = OptionalText(root, name);
    if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) throw new InvalidDataException($"Record field '{name}' is required and bounded.");
    return value;
}

static void RequiredRecordHash(JsonElement root, string name)
{
    if (!IsSha256(OptionalText(root, name))) throw new InvalidDataException($"Record field '{name}' must be a SHA-256 hash.");
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
    var report = new RecordsReport(2, result.Rejected == 0 ? "valid_pending_human_review" : "rejected_records_present", sourcePath, result.Hash, result.Total, result.Accepted, result.Rejected, result.Failed, result.Abstained, false);
    File.WriteAllText(path, JsonSerializer.Serialize(report, TeacherJsonContext.Default.RecordsReport));
}

static void EnsureNew(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    if (File.Exists(path)) throw new IOException($"Output already exists: {path}");
}

sealed class ValidationBudget(CancellationToken token, Stopwatch clock, int timeoutMs)
{
    private int _lastProgress = -1;
    public void Check()
    {
        token.ThrowIfCancellationRequested();
        if (clock.ElapsedMilliseconds > timeoutMs) throw new OperationCanceledException(token);
    }

    public void Progress(string phase, int current, int maximum)
    {
        Check();
        if (current != _lastProgress)
        {
            _lastProgress = current;
            Console.Error.WriteLine($"progress phase={phase} current={current} maximum={maximum} elapsed_ms={clock.ElapsedMilliseconds}");
        }
    }
}

sealed class StatusCounts
{
    public int Total;
    public int Accepted;
    public int Rejected;
    public int Failed;
    public int Abstained;
}

record CatalogCandidate(string Id, string Service, string Model, string Revision, string License, string Status, string? Endpoint, string? TermsEvidence);
record RecordValidation(int Total, int Accepted, int Rejected, int Failed, int Abstained, string Hash);
record CatalogReport(int SchemaVersion, string Status, string SourcePath, string SourceSha256, CatalogCandidate Candidate);
record RecordsReport(int SchemaVersion, string Status, string SourcePath, string SourceSha256, int Total, int Accepted, int Rejected, int Failed, int Abstained, bool TeacherLabelsPermittedForTraining);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower)]
[System.Text.Json.Serialization.JsonSerializable(typeof(CatalogReport))]
[System.Text.Json.Serialization.JsonSerializable(typeof(RecordsReport))]
partial class TeacherJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
