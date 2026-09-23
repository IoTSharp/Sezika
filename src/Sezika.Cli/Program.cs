using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sezika;

namespace Sezika.Cli;

internal static class Program
{
    private const int MaxRequestBytes = 1 * 1024 * 1024;
    private const int InputChunkBytes = 64 * 1024;
    private const int MaxInputReads = MaxRequestBytes + 1;

    public static async Task<int> Main(string[] args)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            lifetime.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintUsage();
                return 0;
            }

            if (args.Length > 32)
            {
                return Fail("cli_option_limit_exceeded", "At most 32 command arguments are accepted.");
            }
            if (args.AsSpan().Contains("--help") || args.AsSpan().Contains("-h"))
            {
                PrintUsage();
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            var options = CliOptions.Parse(args.AsSpan(1));
            return command switch
            {
                "inspect" => await InspectAsync(options, lifetime.Token).ConfigureAwait(false),
                "predict" => await PredictAsync(options, lifetime.Token).ConfigureAwait(false),
                _ => Fail("cli_command_unknown", $"Unknown command '{args[0]}'. Use 'inspect' or 'predict'."),
            };
        }
        catch (OperationCanceledException)
        {
            WriteError("decision_cancelled", "The operation was cancelled or exceeded the command timeout.");
            return 2;
        }
        catch (DecisionException exception)
        {
            WriteError(exception.Code, exception.Message);
            return 2;
        }
        catch (JsonException exception)
        {
            WriteError("cli_invalid_json", exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            WriteError("decision_manifest_invalid", exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            WriteError("cli_io_error", exception.Message);
            return 2;
        }
        finally
        {
            if (cancelHandler is not null)
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
    }

    private static async Task<int> PredictAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var modelDirectory = options.RequireModelDirectory();
        var input = await ReadInputAsync(options.InputPath, cancellationToken).ConfigureAwait(false);
        var hasBom = input.Length >= 3 && input[0] == 0xEF && input[1] == 0xBB && input[2] == 0xBF;
        var request = DecisionRequestParser.Parse(hasBom ? input.AsSpan(3) : input);
        using var runtime = DecisionModelRuntime.Load(
            modelDirectory,
            budget: new DecisionResourceBudget
            {
                MaxTokens = options.MaxTokens,
                Deadline = TimeSpan.FromSeconds(options.DeadlineSeconds),
            },
            minimumConcentration: options.MinimumConcentration,
            cancellationToken: cancellationToken);
        var response = runtime.Evaluate(request, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(response, DecisionJsonContext.Default.DecisionResponse));

        return 0;
    }

    private static async Task<int> InspectAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var modelDirectory = options.RequireModelDirectory();
        var report = await ModelInspector.ReadAsync(modelDirectory, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(report, CliJsonContext.Default.ModelInspectReport));
        return report.AssetsVerified ? 0 : 2;
    }

    private static async Task<byte[]> ReadInputAsync(string? inputPath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(inputPath) && inputPath != "-")
        {
            var fullPath = Path.GetFullPath(inputPath);
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                throw new DecisionException("cli_input_missing", $"Input file '{fullPath}' does not exist.");
            }
            if (info.Length > MaxRequestBytes)
            {
                throw new DecisionException("decision_input_limit_exceeded", $"Input exceeds the {MaxRequestBytes} byte limit.");
            }

            await using var fileInput = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                InputChunkBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await ReadBoundedAsync(fileInput, cancellationToken).ConfigureAwait(false);
        }

        await using var standardInput = Console.OpenStandardInput();
        return await ReadBoundedAsync(standardInput, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream input, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(capacity: InputChunkBytes);
        var chunk = new byte[InputChunkBytes];
        for (var readCount = 0; readCount < MaxInputReads; readCount++)
        {
            var read = await input.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length > MaxRequestBytes - read)
            {
                throw new DecisionException("decision_input_limit_exceeded", $"Input exceeds the {MaxRequestBytes} byte limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        throw new DecisionException("decision_input_limit_exceeded", $"Input exceeds the {MaxRequestBytes} byte limit.");
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

    private static int Fail(string code, string message)
    {
        WriteError(code, message);
        return 2;
    }

    private static void WriteError(string code, string message)
    {
        var error = new CliError { Code = code, Message = message };
        Console.Error.WriteLine(JsonSerializer.Serialize(error, CliJsonContext.Default.CliError));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Sezika typed decision CLI");
        Console.WriteLine("  sezika inspect --model <directory>");
        Console.WriteLine("  sezika predict --model <directory> [--input <file|->] [--minimum-concentration <0..1>] [--deadline-seconds <1..300>] [--max-tokens <n>]");
        Console.WriteLine();
        Console.WriteLine("predict reads a DecisionRequest JSON document from --input or stdin and writes DecisionResponse JSON to stdout.");
    }
}

internal sealed record CliOptions
{
    public string? ModelDirectory { get; init; }
    public string? InputPath { get; init; }
    public double MinimumConcentration { get; init; }
    public int DeadlineSeconds { get; init; } = 30;
    public int MaxTokens { get; init; } = 4096;

    public string RequireModelDirectory()
    {
        if (string.IsNullOrWhiteSpace(ModelDirectory))
        {
            throw new DecisionException("cli_model_required", "--model <directory> is required.");
        }

        return Path.GetFullPath(ModelDirectory);
    }

    public static CliOptions Parse(ReadOnlySpan<string> args)
    {
        string? model = null;
        string? input = null;
        var concentration = 0d;
        var deadlineSeconds = 30;
        var maxTokens = 4096;
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            switch (option)
            {
                case "--model":
                    model = Next(args, ref index, option);
                    break;
                case "--input":
                    input = Next(args, ref index, option);
                    break;
                case "--minimum-concentration":
                    concentration = ParseDouble(Next(args, ref index, option), option);
                    if (!double.IsFinite(concentration) || concentration is < 0d or > 1d)
                    {
                        throw new DecisionException("cli_option_invalid", "--minimum-concentration must be between 0 and 1.");
                    }
                    break;
                case "--deadline-seconds":
                    deadlineSeconds = ParseInt(Next(args, ref index, option), option);
                    if (deadlineSeconds is < 1 or > 300)
                    {
                        throw new DecisionException("cli_option_invalid", "--deadline-seconds must be between 1 and 300.");
                    }
                    break;
                case "--max-tokens":
                    maxTokens = ParseInt(Next(args, ref index, option), option);
                    if (maxTokens is < 1 or > 4096)
                    {
                        throw new DecisionException("cli_option_invalid", "--max-tokens must be between 1 and 4096.");
                    }
                    break;
                case "-h":
                case "--help":
                    throw new DecisionException("cli_help_requested", "Use 'sezika --help' for usage.");
                default:
                    throw new DecisionException("cli_option_unknown", $"Unknown option '{option}'.");
            }
        }

        return new CliOptions
        {
            ModelDirectory = model,
            InputPath = input,
            MinimumConcentration = concentration,
            DeadlineSeconds = deadlineSeconds,
            MaxTokens = maxTokens,
        };
    }

    private static string Next(ReadOnlySpan<string> args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new DecisionException("cli_option_value_missing", $"Option '{option}' requires a value.");
        }

        return args[index];
    }

    private static int ParseInt(string value, string option) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new DecisionException("cli_option_invalid", $"Option '{option}' must be an integer.");

    private static double ParseDouble(string value, string option) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new DecisionException("cli_option_invalid", $"Option '{option}' must be a number.");
}

internal sealed record CliError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}

internal sealed record ModelInspectReport
{
    public required string PackageDirectory { get; init; }
    public bool ManifestPresent { get; init; }
    public bool ManifestPinned { get; init; }
    public bool WeightsPresent { get; init; }
    public bool TokenizerPresent { get; init; }
    public bool WeightsHashVerified { get; init; }
    public bool TokenizerHashVerified { get; init; }
    public long WeightsBytes { get; init; }
    public long TokenizerBytes { get; init; }
    public string? ModelId { get; init; }
    public string? Revision { get; init; }
    public string? Architecture { get; init; }
    public string? License { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool AssetsVerified { get; init; }
}

internal static class ModelInspector
{
    private const int HashChunkBytes = 64 * 1024;
    private const int MaxManifestBytes = 1 * 1024 * 1024;

    public static Task<ModelInspectReport> ReadAsync(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(directory);
        var manifestPath = Path.Combine(root, "model.json");
        var report = new ModelInspectReport { PackageDirectory = root };
        if (!Directory.Exists(root))
        {
            return Task.FromResult(report with { ErrorCode = "decision_model_not_installed", ErrorMessage = "Model package directory does not exist." });
        }

        if (!File.Exists(manifestPath))
        {
            return Task.FromResult(report with { ErrorCode = "decision_manifest_missing", ErrorMessage = "model.json is missing." });
        }

        try
        {
            byte[] manifestBytes;
            using (var manifestStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (manifestStream.Length > MaxManifestBytes)
                {
                    throw new DecisionException("decision_manifest_limit_exceeded", "model.json exceeds the 1 MiB metadata limit.");
                }

                manifestBytes = new byte[(int)manifestStream.Length];
                manifestStream.ReadExactly(manifestBytes);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(manifestBytes, new JsonDocumentOptions { MaxDepth = 32 });
            var json = document.RootElement;
            var modelId = StringProperty(json, "model_id");
            var revision = StringProperty(json, "revision");
            var architecture = StringProperty(json, "architecture");
            var license = StringProperty(json, "license");
            var weightsRelative = "model.safetensors";
            var tokenizerRelative = "tokenizer/tokenizer.json";
            var weightsPath = SafeCombine(root, weightsRelative);
            var tokenizerPath = SafeCombine(root, tokenizerRelative);
            var weightsPresent = File.Exists(weightsPath);
            var tokenizerPresent = File.Exists(tokenizerPath);
            var weightsVerified = weightsPresent && VerifyHash(weightsPath, ModernBertModelLoader.PinnedWeightsSha256, cancellationToken);
            var tokenizerVerified = tokenizerPresent && VerifyHash(tokenizerPath, ModernBertModelLoader.PinnedTokenizerSha256, cancellationToken);
            var pinned = modelId == ModernBertModelLoader.PinnedModelId && revision == ModernBertModelLoader.PinnedRevision &&
                         StringProperty(json, "tokenizer_revision") == ModernBertModelLoader.PinnedRevision && license == "Apache-2.0" &&
                         json.TryGetProperty("schema_version", out var schemaVersion) && schemaVersion.TryGetInt32(out var version) && version == 1;
            var errorCode = !pinned ? "decision_manifest_invalid" :
                !weightsPresent || !tokenizerPresent ? "decision_model_not_installed" :
                !weightsVerified || !tokenizerVerified ? "decision_asset_hash_mismatch" : null;
            return Task.FromResult(report with
            {
                ManifestPresent = true,
                ManifestPinned = pinned,
                WeightsPresent = weightsPresent,
                TokenizerPresent = tokenizerPresent,
                WeightsHashVerified = weightsVerified,
                TokenizerHashVerified = tokenizerVerified,
                WeightsBytes = weightsPresent ? new FileInfo(weightsPath).Length : 0,
                TokenizerBytes = tokenizerPresent ? new FileInfo(tokenizerPath).Length : 0,
                ModelId = modelId,
                Revision = revision,
                Architecture = architecture,
                License = license,
                ErrorCode = errorCode,
                AssetsVerified = pinned && weightsVerified && tokenizerVerified,
            });
        }
        catch (DecisionException exception)
        {
            return Task.FromResult(report with { ManifestPresent = true, ErrorCode = exception.Code, ErrorMessage = exception.Message });
        }
        catch (JsonException exception)
        {
            return Task.FromResult(report with { ManifestPresent = true, ErrorCode = "decision_manifest_invalid", ErrorMessage = exception.Message });
        }
    }

    private static string? StringProperty(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string SafeCombine(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            throw new DecisionException("decision_manifest_path_invalid", "Manifest asset paths must be relative.");
        }

        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        return path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            ? path
            : throw new DecisionException("decision_manifest_path_invalid", "Manifest asset path escapes its package directory.");
    }

    private static bool VerifyHash(string path, string expected, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, HashChunkBytes, FileOptions.SequentialScan);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[HashChunkBytes];
        var maxReads = checked((int)Math.Min(int.MaxValue, stream.Length / HashChunkBytes + 2));
        for (var readCount = 0; readCount < maxReads; readCount++)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return Convert.ToHexString(hasher.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase);
            }

            hasher.AppendData(buffer, 0, read);
            cancellationToken.ThrowIfCancellationRequested();
        }

        throw new DecisionException("decision_asset_limit_exceeded", "Asset hash operation exceeded its bounded read count.");
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CliError))]
[JsonSerializable(typeof(ModelInspectReport))]
internal partial class CliJsonContext : JsonSerializerContext
{
}
