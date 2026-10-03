using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

const int MaxFileBytes = 128 * 1024 * 1024;
const int MaxRows = 1_000_000;
var command = args.Length == 0 ? "help" : args[0];
if (command.Equals("self-test", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        _ = ParseManifest(Encoding.UTF8.GetBytes("{\"schema_version\":1,\"schema_version\":1}"));
        Console.Error.WriteLine("self-test expected duplicate manifest rejection");
        return 1;
    }
    catch (InvalidDataException)
    {
        try
        {
            _ = ParseRows(Encoding.UTF8.GetBytes("{\"id\":\"x\",\"id\":\"y\",\"split\":\"calibration\",\"primitive\":\"boolean\",\"label\":0,\"probabilities\":[1,0]}\n"), "boolean", CancellationToken.None, out _, out _, out _);
            Console.Error.WriteLine("self-test expected duplicate prediction rejection");
            return 1;
        }
        catch (InvalidDataException)
        {
            Console.WriteLine("PASS: duplicate JSON properties are rejected before deserialization");
            return 0;
        }
    }
}
if (!command.Equals("evaluate", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: Sezika.CalibrationTool evaluate --manifest <manifest.json> --predictions <predictions.jsonl> --output <report.json> --primitive <choice|score|boolean> --model-id <id> --model-revision <revision> --model-weights-sha256 <sha256> --tokenizer-revision <revision> --tokenizer-sha256 <sha256> --prompt-schema-id <id> --prompt-schema-sha256 <sha256> [--timeout-seconds 1..600] [--temperature-steps 2..10000]");
    return 2;
}

try
{
    var manifestPath = Full(Required("--manifest"));
    var predictionsPath = Full(Required("--predictions"));
    var outputPath = Full(Required("--output"));
    var primitive = Required("--primitive");
    var timeoutSeconds = Bounded("--timeout-seconds", 1, 600, 600);
    var steps = Bounded("--temperature-steps", 2, 10_000, 200);
    using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; lifetime.Cancel(); };
    Console.CancelKeyPress += cancel;
    try
    {
        var manifestBytes = ReadBytes(manifestPath, lifetime.Token);
        var manifestSha256 = Hash(manifestBytes);
        var parsedManifest = ParseManifest(manifestBytes);
        if (parsedManifest.ManifestSha256 is not null &&
            !manifestSha256.Equals(parsedManifest.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new DecisionException("quality_hash_mismatch", "The manifest SHA-256 does not match the manifest binding.");
        var manifest = parsedManifest with { ManifestSha256 = manifestSha256 };
        var predictionBytes = ReadBytes(predictionsPath, lifetime.Token);
        var rows = ParseRows(predictionBytes, primitive, lifetime.Token, out var recordsSha256, out var calibrationSha256, out var sealedSha256);
        if (File.Exists(outputPath)) throw new IOException("Output already exists; choose a new report path.");
        var identity = new QualityCalibrationIdentity
        {
            ModelId = Required("--model-id"), ModelRevision = Required("--model-revision"),
            ModelWeightsSha256 = Required("--model-weights-sha256"), TokenizerRevision = Required("--tokenizer-revision"),
            TokenizerSha256 = Required("--tokenizer-sha256"), PromptSchemaId = Required("--prompt-schema-id"),
            PromptSchemaSha256 = Required("--prompt-schema-sha256"),
        };
        var gates = new CalibrationGates
        {
            MinimumTestExamples = Bounded("--minimum-test-examples", 1, MaxRows, 100),
            MinimumAccuracyDeltaOverRandom = Decimal("--minimum-accuracy-delta-over-random", 0, 1, 0.05),
            MaximumNllIncreaseOverUncalibrated = Decimal("--maximum-nll-increase", 0, 100, 0.02),
            MaximumBrierIncreaseOverUncalibrated = Decimal("--maximum-brier-increase", 0, 100, 0.02),
            MaximumExpectedCalibrationError = Decimal("--maximum-ece", 0, 1, 0.1),
            MinimumCoverage = Decimal("--minimum-coverage", 0, 1, 0.95),
            MaximumSelectiveRisk = Decimal("--maximum-selective-risk", 0, 1, 0.2),
            MaximumScoreMae = Decimal("--maximum-score-mae", 0, 32, 0.75), ConfidenceLevel = 0.95,
        };
        var report = QualityCalibrationPipeline.Run(manifest with { ManifestSha256 = manifestSha256 }, rows, identity,
            manifestSha256, recordsSha256, calibrationSha256, sealedSha256, primitive, gates,
            temperatureSteps: steps, cancellationToken: lifetime.Token, maxDuration: TimeSpan.FromSeconds(timeoutSeconds),
            progress: (done, total) => { if (done == total || done % 1000 == 0) Console.Error.WriteLine($"validated {done}/{total} rows"); });
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporaryOutputPath = outputPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var output = new FileStream(temporaryOutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(output, report, ToolJsonContext.Default.QualityCalibrationReport, lifetime.Token);
            File.Move(temporaryOutputPath, outputPath);
        }
        finally
        {
            if (File.Exists(temporaryOutputPath)) File.Delete(temporaryOutputPath);
        }
        Console.WriteLine($"quality calibration: gate={report.Gate.Status}; calibration={report.CalibrationCount}; sealed_test={report.SealedTestCount}; temperature={report.Temperature:R}; records_sha256={recordsSha256}");
        return report.Gate.Status == "passed" ? 0 : 3;
    }
    finally { Console.CancelKeyPress -= cancel; }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("quality calibration: cancelled or bounded timeout exceeded; no complete report is claimed.");
    return 124;
}
catch (TimeoutException exception)
{
    Console.Error.WriteLine($"quality calibration: {exception.GetType().Name}: {exception.Message}; no complete report is claimed.");
    return 124;
}
catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or DecisionException or JsonException)
{
    Console.Error.WriteLine($"quality calibration: {exception.GetType().Name}: {exception.Message}");
    return 1;
}

static string Required(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var index = 1; index + 1 < args.Length; index++)
        if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
    throw new ArgumentException($"Missing {name}.");
}

static int Bounded(string name, int minimum, int maximum, int fallback)
{
    var args = Environment.GetCommandLineArgs();
    for (var index = 1; index + 1 < args.Length; index++)
        if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(args[index + 1], out var value) || value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name);
            return value;
        }
    return fallback;
}

static double Decimal(string name, double minimum, double maximum, double fallback)
{
    var args = Environment.GetCommandLineArgs();
    for (var index = 1; index + 1 < args.Length; index++)
        if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            if (!double.TryParse(args[index + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name);
            return value;
        }
    return fallback;
}

static string Full(string path) => Path.GetFullPath(path);

static byte[] ReadBytes(string path, CancellationToken cancellationToken)
{
    if (!File.Exists(path)) throw new FileNotFoundException("Input file is missing.", path);
    var info = new FileInfo(path);
    if (info.Length is < 1 or > MaxFileBytes) throw new InvalidDataException("Input exceeds the 128 MiB bound.");
    var bytes = new byte[checked((int)info.Length)];
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
    var offset = 0;
    const int chunkSize = 64 * 1024;
    for (var chunk = 0; chunk <= MaxFileBytes / chunkSize && offset < bytes.Length; chunk++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = stream.Read(bytes, offset, Math.Min(chunkSize, bytes.Length - offset));
        if (read == 0) throw new InvalidDataException("Input changed while it was being read.");
        offset += read;
    }
    if (offset != bytes.Length) throw new InvalidDataException("Input read exceeded the bounded chunk count.");
    return bytes;
}

static QualityCalibrationManifest ParseManifest(byte[] bytes)
{
    try
    {
        using var document = ParseStrictJson(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Manifest must be a JSON object.");
        string? manifestHash = null;
        if (root.TryGetProperty("manifest_sha256", out var manifestHashElement))
        {
            if (manifestHashElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(manifestHashElement.GetString()) ||
                manifestHashElement.GetString()!.Length != 64 ||
                !manifestHashElement.GetString()!.All(Uri.IsHexDigit))
                throw new InvalidDataException("Manifest property 'manifest_sha256' must be a SHA-256 hex digest.");
            manifestHash = manifestHashElement.GetString();
        }
        return new QualityCalibrationManifest
        {
            SchemaVersion = root.GetProperty("schema_version").GetInt32(), DatasetId = Text(root, "dataset_id"), Status = Text(root, "status"),
            RepresentsModelQuality = root.GetProperty("represents_model_quality").GetBoolean(), RecordCount = root.GetProperty("record_count").GetInt32(),
            CalibrationCount = root.GetProperty("calibration_count").GetInt32(), SealedTestCount = root.GetProperty("sealed_test_count").GetInt32(),
            AbstainBelow = root.GetProperty("abstain_below").GetDouble(),
            ManifestSha256 = manifestHash,
            RecordsSha256 = Text(root, "records_sha256"), CalibrationSplitSha256 = Text(root, "calibration_split_sha256"),
            SealedTestSplitSha256 = Text(root, "sealed_test_split_sha256"),
        };
    }
    catch (InvalidDataException) { throw; }
    catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or JsonException)
    {
        throw new InvalidDataException("Manifest fields are missing or have invalid JSON types.", exception);
    }
}

static IReadOnlyList<QualityCalibrationExample> ParseRows(byte[] bytes, string primitive, CancellationToken token,
    out string recordsSha256, out string calibrationSha256, out string sealedSha256)
{
    try
    {
        using var records = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var calibration = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sealedTest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var rows = new List<QualityCalibrationExample>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        for (var lineNumber = 1; !reader.EndOfStream; lineNumber++)
        {
            token.ThrowIfCancellationRequested();
            if (lineNumber > MaxRows) throw new InvalidDataException("Prediction row count exceeds the bounded maximum.");
            var line = reader.ReadLine() ?? string.Empty;
            if (line.Length is 0 or > 65_536) throw new InvalidDataException($"Prediction line {lineNumber} is empty or oversized.");
            var canonical = Encoding.UTF8.GetBytes(line + "\n"); records.AppendData(canonical);
            using var document = ParseStrictJson(Encoding.UTF8.GetBytes(line));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Prediction row {lineNumber} must be a JSON object.");
            var split = Text(root, "split");
            if (split is not ("calibration" or "sealed_test")) throw new DecisionException("quality_split_invalid", $"Row {lineNumber} has an unsupported split.");
            if (split == "calibration") calibration.AppendData(canonical); else sealedTest.AppendData(canonical);
            var id = Text(root, "id");
            if (id.Length > 256) throw new InvalidDataException($"Prediction row {lineNumber} ID is oversized.");
            if (!seen.Add(id)) throw new DecisionException("quality_input_invalid", $"Duplicate row ID: {id}");
            var rowPrimitive = Text(root, "primitive"); if (!rowPrimitive.Equals(primitive, StringComparison.Ordinal)) throw new DecisionException("quality_input_invalid", "Rows must use one primitive per run.");
            var probabilities = root.GetProperty("probabilities").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            rows.Add(new QualityCalibrationExample(id, split, root.GetProperty("label").GetInt32(), probabilities,
                root.TryGetProperty("language", out var language) ? language.GetString() ?? "und" : "und",
                root.TryGetProperty("domain", out var domain) ? domain.GetString() ?? "default" : "default"));
        }
        if (rows.Count == 0) throw new InvalidDataException("Prediction artifact is empty.");
        // The complete artifact hash binds its exact bytes (including newline style);
        // split hashes use the canonical JSONL stream above for cross-platform use.
        recordsSha256 = Hash(bytes);
        calibrationSha256 = Convert.ToHexString(calibration.GetHashAndReset()).ToLowerInvariant();
        sealedSha256 = Convert.ToHexString(sealedTest.GetHashAndReset()).ToLowerInvariant();
        return rows;
    }
    catch (InvalidDataException) { throw; }
    catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or JsonException)
    {
        throw new InvalidDataException("Prediction fields are missing or have invalid JSON types.", exception);
    }
}

static string Text(JsonElement root, string name)
{
    if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new InvalidDataException($"Manifest/row property '{name}' is required.");
    return value.GetString()!;
}

static JsonDocument ParseStrictJson(byte[] bytes)
{
    var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
    var scopes = new Stack<HashSet<string>>();
    try
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (scopes.Count == 0) throw new InvalidDataException("JSON object scope is unbalanced.");
                scopes.Pop();
            }
            else if (reader.TokenType == JsonTokenType.PropertyName && (scopes.Count == 0 || !scopes.Peek().Add(reader.GetString()!)))
                throw new InvalidDataException("JSON contains a duplicate property.");
            if (reader.CurrentDepth > 16) throw new InvalidDataException("JSON nesting exceeds the 16-level bound.");
        }
        if (scopes.Count != 0) throw new InvalidDataException("JSON is incomplete.");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
    }
    catch (JsonException exception)
    {
        throw new InvalidDataException("JSON is invalid.", exception);
    }
}

static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(QualityCalibrationReport))]
internal partial class ToolJsonContext : JsonSerializerContext { }
