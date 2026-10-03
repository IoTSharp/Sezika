using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const int MaxCatalogBytes = 1 * 1024 * 1024;
const int MaxRecords = 32;
const int MaxRecordBytes = 65_536;
const int MaxRecordsFileBytes = (MaxRecords * (MaxRecordBytes + 2)) + 3;
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
        Console.WriteLine($"catalog_valid candidate={candidate.Id} declared_status={candidate.Status} independently_verified=false sha256={catalogHash} report={reportPath}");
        return candidate.Status == "verified" ? 0 : 3;
    }

    if (command == "validate-records")
    {
        var recordsPath = Path.GetFullPath(Required("--records"));
        var outputPath = Path.GetFullPath(Required("--report"));
        var result = ValidateRecords(recordsPath, budget);
        WriteRecordsReport(outputPath, recordsPath, result);
        Console.WriteLine($"records_checked total={result.Total} pending={result.Pending} accepted={result.Accepted} rejected={result.Rejected} failed={result.Failed} abstained={result.Abstained} invalid={result.Invalid} sha256={result.Hash} report={outputPath}");
        return result.Invalid == 0 ? 0 : 3;
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
    if (commandLine.Length > 16) throw new ArgumentException("At most 16 command line arguments are permitted.");
    for (var index = 1; index + 1 < commandLine.Length; index++)
        if (commandLine[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return commandLine[index + 1];
    return null;
}

static string Required(string name) => Optional(name) ?? throw new ArgumentException($"Missing {name}.");

static string ValidateCatalog(string path, out CatalogCandidate candidate, ValidationBudget budget)
{
    budget.Check();
    var bytes = ReadSnapshot(path, MaxCatalogBytes, budget);
    budget.Check();
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    using var document = ParseStrictJson(bytes, budget);
    var root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("candidate", out var item) || item.ValueKind != JsonValueKind.Object)
        throw new InvalidDataException("Catalog must contain a candidate object.");
    candidate = new CatalogCandidate(
        RequiredText(item, "id"), RequiredText(item, "service"), RequiredText(item, "model"),
        RequiredText(item, "revision"), RequiredText(item, "license"), RequiredText(item, "status"),
        OptionalText(item, "endpoint"), OptionalText(item, "terms_evidence"));
    if (candidate.Endpoint?.Length > 2048 || candidate.TermsEvidence?.Length > 4096)
        throw new InvalidDataException("Catalog endpoint or evidence field is too long.");
    if (candidate.Id.Length > 128 || candidate.Service.Length > 128 || candidate.Model.Length > 256 || candidate.Revision.Length > 256 || candidate.License.Length > 256)
        throw new InvalidDataException("Catalog identity field is too long.");
    if (candidate.Status is not ("unverified" or "verified" or "blocked")) throw new InvalidDataException("Catalog status is invalid.");
    if (candidate.Status == "verified")
    {
        if (!Uri.TryCreate(candidate.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            throw new InvalidDataException("A verified candidate requires an http(s) endpoint.");
        if (!endpoint.IsLoopback || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new InvalidDataException("The local teacher endpoint must be loopback and cannot contain credentials or a fragment.");
        if (string.IsNullOrWhiteSpace(candidate.TermsEvidence)) throw new InvalidDataException("A verified candidate requires terms evidence.");
    }
    if (!candidate.Id.Equals("qwen35-9b-q4km", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Only the planned qwen35-9b-q4km candidate is permitted by this contract; its identity remains externally unverified.");
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

static string? OptionalText(JsonElement item, string name)
{
    if (!item.TryGetProperty(name, out var value)) return null;
    if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"Field '{name}' must be a string when present.");
    return value.GetString();
}

static RecordValidation ValidateRecords(string path, ValidationBudget budget)
{
    budget.Check();
    var bytes = ReadSnapshot(path, MaxRecordsFileBytes, budget);
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    budget.Check();
    var counts = new StatusCounts();
    var ids = new HashSet<string>(StringComparer.Ordinal);
    var families = new Dictionary<string, string>(StringComparer.Ordinal);
    var text = new UTF8Encoding(false, true).GetString(bytes);
    if (text.StartsWith('\uFEFF')) text = text[1..];
    using var reader = new StringReader(text);
    for (var lineNumber = 1; lineNumber <= MaxRecords + 1 && reader.ReadLine() is { } line; lineNumber++)
    {
        budget.Check();
        if (lineNumber > MaxRecords) throw new InvalidDataException("Teacher records exceed the 32-record exploration bound.");
        counts.Total++;
        if (line.Length == 0 || Encoding.UTF8.GetByteCount(line) > MaxRecordBytes)
        {
            counts.Invalid++;
            budget.Progress("records_invalid_size", lineNumber, MaxRecords);
            continue;
        }
        try
        {
            using var document = ParseStrictJson(Encoding.UTF8.GetBytes(line), budget);
            ValidateRecord(document.RootElement, ids, families, counts);
        }
        catch (JsonException) { counts.Invalid++; }
        catch (InvalidDataException) { counts.Invalid++; }
        budget.Progress("records", lineNumber, MaxRecords);
    }
    budget.Check();
    return new RecordValidation(counts.Total, counts.Accepted, counts.Rejected, counts.Failed, counts.Abstained, counts.Pending, counts.Invalid, hash);
}

static void ValidateRecord(JsonElement root, HashSet<string> ids, Dictionary<string, string> families, StatusCounts counts)
{
    if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Record must be an object.");
    var id = RequiredRecordText(root, "record_id", 128);
    if (ids.Contains(id)) throw new InvalidDataException("record_id must be unique.");
    var status = RequiredRecordText(root, "review_status", 32);
    if (status is not ("pending" or "accepted" or "rejected" or "failed" or "abstained")) throw new InvalidDataException("review_status is invalid.");
    var split = RequiredRecordText(root, "split", 32);
    if (split is not ("train" or "development" or "calibration" or "audit")) throw new InvalidDataException("teacher records cannot target sealed_test or an unknown split.");
    RequiredRecordText(root, "source", 256);
    var family = RequiredRecordText(root, "family_id", 128);
    if (families.TryGetValue(family, out var familySplit) && familySplit != split)
        throw new InvalidDataException("The same family cannot cross teacher-data splits.");
    RequiredRecordHash(root, "request_sha256");
    RequiredRecordHash(root, "prompt_sha256");
    RequiredRecordText(root, "model_id", 256);
    RequiredRecordText(root, "model_revision", 256);
    var responseHash = OptionalText(root, "response_sha256");
    if (responseHash is not null && !IsSha256(responseHash)) throw new InvalidDataException("response_sha256 must be a SHA-256 hash when present.");
    if (status is "accepted" or "rejected")
    {
        if (!IsSha256(responseHash)) throw new InvalidDataException("accepted/rejected records require response_sha256.");
        RequiredRecordText(root, "human_review_id", 128);
    }
    ids.Add(id);
    families[family] = split;
    if (status == "pending") counts.Pending++;
    else if (status == "failed") counts.Failed++;
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
    var report = new CatalogReport(2, candidate.Status == "verified" ? "valid_declared_verified_not_independently_verified" : "blocked_unverified", sourcePath, hash, candidate, false);
    WriteNew(path, JsonSerializer.Serialize(report, TeacherJsonContext.Default.CatalogReport));
}

static void WriteRecordsReport(string path, string sourcePath, RecordValidation result)
{
    var report = new RecordsReport(3, result.Invalid == 0 ? "valid_pending_human_review" : "invalid_records_present", sourcePath, result.Hash, result.Total, result.Accepted, result.Rejected, result.Failed, result.Abstained, result.Pending, result.Invalid, false);
    WriteNew(path, JsonSerializer.Serialize(report, TeacherJsonContext.Default.RecordsReport));
}

static void WriteNew(string path, string text)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
    writer.Write(text);
}

static byte[] ReadSnapshot(string path, int maximumBytes, ValidationBudget budget)
{
    budget.Check();
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    var length = stream.Length;
    if (length is <= 0 || length > maximumBytes) throw new InvalidDataException($"Input must contain 1..{maximumBytes} bytes.");
    var bytes = new byte[(int)length];
    var offset = 0;
    // Each successful read advances at least one byte; the byte bound and operation deadline bound this loop.
    for (var reads = 0; offset < bytes.Length && reads < maximumBytes; reads++)
    {
        budget.Check();
        var count = stream.Read(bytes, offset, Math.Min(8192, bytes.Length - offset));
        if (count == 0) throw new EndOfStreamException("Input changed during snapshot read.");
        offset += count;
    }
    if (offset != bytes.Length || stream.ReadByte() != -1) throw new InvalidDataException("Input changed during snapshot read.");
    budget.Check();
    return bytes;
}

static JsonDocument ParseStrictJson(byte[] bytes, ValidationBudget budget)
{
    var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 16 });
    var objects = new Stack<HashSet<string>>();
    // No more than one token can start per input byte; explicit byte and wall-clock bounds apply.
    for (var tokens = 0; tokens <= bytes.Length && reader.Read(); tokens++)
    {
        budget.Check();
        if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new HashSet<string>(StringComparer.Ordinal));
        else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
        else if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek().Add(reader.GetString()!))
            throw new InvalidDataException("Duplicate JSON properties are forbidden, including escaped aliases.");
    }
    return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
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
    public int Pending;
    public int Invalid;
}

record CatalogCandidate(string Id, string Service, string Model, string Revision, string License, string Status, string? Endpoint, string? TermsEvidence);
record RecordValidation(int Total, int Accepted, int Rejected, int Failed, int Abstained, int Pending, int Invalid, string Hash);
record CatalogReport(int SchemaVersion, string Status, string SourcePath, string SourceSha256, CatalogCandidate Candidate, bool IndependentlyVerified);
record RecordsReport(int SchemaVersion, string Status, string SourcePath, string SourceSha256, int Total, int Accepted, int Rejected, int Failed, int Abstained, int Pending, int Invalid, bool TeacherLabelsPermittedForTraining);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower)]
[System.Text.Json.Serialization.JsonSerializable(typeof(CatalogReport))]
[System.Text.Json.Serialization.JsonSerializable(typeof(RecordsReport))]
partial class TeacherJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
