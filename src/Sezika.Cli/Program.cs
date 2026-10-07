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
        using var lifetime = new CancellationTokenSource();
        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            lifetime.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return await RunAsync(args, cancellationToken: lifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    internal static async Task<int> RunAsync(string[] args, Stream? standardInput = null,
        TextWriter? output = null, TextWriter? error = null, CancellationToken cancellationToken = default,
        IndependentRuntimeLoader? independentLoader = null)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintUsage(output);
                return 0;
            }

            if (args.Length > 32)
            {
                return Fail("cli_option_limit_exceeded", "At most 32 command arguments are accepted.", error);
            }
            if (args.AsSpan().Contains("--help") || args.AsSpan().Contains("-h"))
            {
                PrintUsage(output);
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            var options = CliOptions.Parse(args.AsSpan(1));
            options.ValidateCommand(command);
            lifetime.CancelAfter(TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
            return command switch
            {
                "inspect" => await InspectAsync(options, output, independentLoader, lifetime.Token).ConfigureAwait(false),
                "predict" => await PredictAsync(options, standardInput, output, independentLoader, lifetime.Token).ConfigureAwait(false),
                _ => Fail("cli_command_unknown", $"Unknown command '{args[0]}'. Use 'inspect' or 'predict'.", error),
            };
        }
        catch (OperationCanceledException)
        {
            WriteError("decision_cancelled", "The operation was cancelled or exceeded the command timeout.", error);
            return 2;
        }
        catch (DecisionException exception)
        {
            WriteError(exception.Code, exception.Message, error);
            return 2;
        }
        catch (JsonException exception)
        {
            WriteError("cli_invalid_json", exception.Message, error);
            return 2;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            WriteError("decision_manifest_invalid", exception.Message, error);
            return 2;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            WriteError("cli_io_error", exception.Message, error);
            return 2;
        }
    }

    private static async Task<int> PredictAsync(CliOptions options, Stream? standardInput, TextWriter output,
        IndependentRuntimeLoader? independentLoader, CancellationToken cancellationToken)
    {
        var modelDirectory = options.RequireModelDirectory();
        var binding = options.ModelFormat == CliModelFormat.IndependentV1
            ? await IndependentHeadBinding.ReadAsync(options.HeadBindingPath!, cancellationToken).ConfigureAwait(false) : null;
        using var inputLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        inputLifetime.CancelAfter(TimeSpan.FromSeconds(options.InputTimeoutSeconds));
        byte[] input;
        try
        {
            input = await ReadInputAsync(options.InputPath, standardInput, inputLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DecisionException("cli_input_timeout", "Reading the request exceeded --input-timeout-seconds.", exception);
        }
        var hasBom = input.Length >= 3 && input[0] == 0xEF && input[1] == 0xBB && input[2] == 0xBF;
        if (binding is not null)
        {
            var independentRequest = IndependentDecisionV1Parser.Parse(hasBom ? input.AsSpan(3) : input);
            using var independentRuntime = IndependentCli.Load(options, binding, independentLoader, cancellationToken);
            var independentResponse = independentRuntime.Evaluate(independentRequest, cancellationToken);
            output.WriteLine(JsonSerializer.Serialize(independentResponse, CliJsonContext.Default.IndependentDecisionResponse));
            return 0;
        }
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
        output.WriteLine(JsonSerializer.Serialize(response, DecisionJsonContext.Default.DecisionResponse));

        return 0;
    }

    private static async Task<int> InspectAsync(CliOptions options, TextWriter output,
        IndependentRuntimeLoader? independentLoader, CancellationToken cancellationToken)
    {
        var modelDirectory = options.RequireModelDirectory();
        if (options.ModelFormat == CliModelFormat.IndependentV1)
        {
            var binding = await IndependentHeadBinding.ReadAsync(options.HeadBindingPath!, cancellationToken).ConfigureAwait(false);
            using var runtime = IndependentCli.Load(options, binding, independentLoader, cancellationToken);
            var independentReport = new IndependentInspectReport(modelDirectory, Path.GetFullPath(options.HeadPath!),
                runtime.ModelId, runtime.EncoderSha256, runtime.TokenizerSha256, binding.HeadSha256,
                binding.FeatureSha256, binding.Identity.DataManifestSha256);
            output.WriteLine(JsonSerializer.Serialize(independentReport, CliJsonContext.Default.IndependentInspectReport));
            return 0;
        }
        var report = await ModelInspector.ReadAsync(modelDirectory, cancellationToken).ConfigureAwait(false);
        output.WriteLine(JsonSerializer.Serialize(report, CliJsonContext.Default.ModelInspectReport));
        return report.AssetsVerified ? 0 : 2;
    }

    private static async Task<byte[]> ReadInputAsync(string? inputPath, Stream? standardInput, CancellationToken cancellationToken)
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

        if (standardInput is not null) return await ReadBoundedAsync(standardInput, cancellationToken).ConfigureAwait(false);
        await using var consoleInput = Console.OpenStandardInput();
        return await ReadBoundedAsync(consoleInput, cancellationToken).ConfigureAwait(false);
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

    private static int Fail(string code, string message, TextWriter error)
    {
        WriteError(code, message, error);
        return 2;
    }

    private static void WriteError(string code, string message, TextWriter writer)
    {
        var error = new CliError { Code = code, Message = message };
        writer.WriteLine(JsonSerializer.Serialize(error, CliJsonContext.Default.CliError));
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Sezika typed decision CLI");
        output.WriteLine("  sezika inspect --model <directory> [--model-format legacy-laya]");
        output.WriteLine("  sezika predict --model <directory> [--model-format legacy-laya] [--input <file|->] [--minimum-concentration <0..1>]");
        output.WriteLine("  sezika <inspect|predict> --model-format independent-v1 --model <directory> --head <asset> --head-binding <json> [--input <file|->]");
        output.WriteLine("  Common budgets: --deadline-seconds <1..300> --max-tokens <n> --command-timeout-seconds <1..1800> --input-timeout-seconds <1..300>");
        output.WriteLine("  Model format defaults to legacy-laya. Independent predict consumes decision-v1 JSON and returns uncalibrated distributions.");
        output.WriteLine("  Independent inspect loads and verifies the encoder and separately hash-bound head; it does not evaluate model quality.");
    }
}

internal sealed record CliOptions
{
    public string? ModelDirectory { get; init; }
    public string? InputPath { get; init; }
    public CliModelFormat ModelFormat { get; init; }
    public string? HeadPath { get; init; }
    public string? HeadBindingPath { get; init; }
    public double MinimumConcentration { get; init; }
    public bool MinimumConcentrationSpecified { get; init; }
    public int DeadlineSeconds { get; init; } = 30;
    public int MaxTokens { get; init; } = 4096;
    public int CommandTimeoutSeconds { get; init; } = 1800;
    public int InputTimeoutSeconds { get; init; } = 30;

    public void ValidateCommand(string command)
    {
        if (command == "inspect" && InputPath is not null)
            throw new DecisionException("cli_option_invalid", "--input is only valid for predict.");
        if (ModelFormat == CliModelFormat.IndependentV1)
        {
            if (string.IsNullOrWhiteSpace(HeadPath) || string.IsNullOrWhiteSpace(HeadBindingPath))
                throw new DecisionException("cli_head_required", "independent-v1 requires --head <asset> and --head-binding <json>.");
            if (MinimumConcentrationSpecified)
                throw new DecisionException("cli_option_invalid", "independent-v1 has no calibrated abstention policy; --minimum-concentration is unsupported.");
        }
        else if (HeadPath is not null || HeadBindingPath is not null)
            throw new DecisionException("cli_option_invalid", "--head and --head-binding require --model-format independent-v1.");
    }

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
        string? head = null;
        string? binding = null;
        var modelFormat = CliModelFormat.LegacyLaya;
        var concentration = 0d;
        var concentrationSpecified = false;
        var deadlineSeconds = 30;
        int? maxTokens = null;
        var commandTimeoutSeconds = 1800;
        var inputTimeoutSeconds = 30;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (!seen.Add(option)) throw new DecisionException("cli_option_duplicate", $"Option '{option}' appears more than once.");
            switch (option)
            {
                case "--model":
                    model = Next(args, ref index, option);
                    break;
                case "--input":
                    input = Next(args, ref index, option);
                    break;
                case "--model-format":
                    modelFormat = Next(args, ref index, option) switch
                    {
                        "legacy-laya" => CliModelFormat.LegacyLaya,
                        "independent-v1" => CliModelFormat.IndependentV1,
                        _ => throw new DecisionException("cli_model_format_invalid", "--model-format must be legacy-laya or independent-v1."),
                    };
                    break;
                case "--head":
                    head = Next(args, ref index, option);
                    break;
                case "--head-binding":
                    binding = Next(args, ref index, option);
                    break;
                case "--minimum-concentration":
                    concentrationSpecified = true;
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
                    break;
                case "--command-timeout-seconds":
                    commandTimeoutSeconds = ParseInt(Next(args, ref index, option), option);
                    if (commandTimeoutSeconds is < 1 or > 1800)
                        throw new DecisionException("cli_option_invalid", "--command-timeout-seconds must be between 1 and 1800.");
                    break;
                case "--input-timeout-seconds":
                    inputTimeoutSeconds = ParseInt(Next(args, ref index, option), option);
                    if (inputTimeoutSeconds is < 1 or > 300)
                        throw new DecisionException("cli_option_invalid", "--input-timeout-seconds must be between 1 and 300.");
                    break;
                case "-h":
                case "--help":
                    throw new DecisionException("cli_help_requested", "Use 'sezika --help' for usage.");
                default:
                    throw new DecisionException("cli_option_unknown", $"Unknown option '{option}'.");
            }
        }

        var effectiveMaxTokens = maxTokens ?? (modelFormat == CliModelFormat.IndependentV1 ? 1024 : 4096);
        if (modelFormat == CliModelFormat.IndependentV1 ? effectiveMaxTokens is < 2 or > 1024 : effectiveMaxTokens is < 1 or > 4096)
            throw new DecisionException("cli_option_invalid", modelFormat == CliModelFormat.IndependentV1
                ? "independent-v1 --max-tokens must be between 2 and 1024."
                : "legacy-laya --max-tokens must be between 1 and 4096.");

        return new CliOptions
        {
            ModelDirectory = model,
            InputPath = input,
            ModelFormat = modelFormat,
            HeadPath = head,
            HeadBindingPath = binding,
            MinimumConcentration = concentration,
            MinimumConcentrationSpecified = concentrationSpecified,
            DeadlineSeconds = deadlineSeconds,
            MaxTokens = effectiveMaxTokens,
            CommandTimeoutSeconds = commandTimeoutSeconds,
            InputTimeoutSeconds = inputTimeoutSeconds,
        };
    }

    private static string Next(ReadOnlySpan<string> args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
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
[JsonSerializable(typeof(IndependentInspectReport))]
[JsonSerializable(typeof(IndependentHeadBinding))]
[JsonSerializable(typeof(TrainingHeadBindingReport))]
[JsonSerializable(typeof(MarkerFeatureIdentity))]
[JsonSerializable(typeof(IndependentDecisionResponse))]
internal partial class CliJsonContext : JsonSerializerContext
{
}
