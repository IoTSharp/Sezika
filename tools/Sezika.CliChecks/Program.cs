using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Sezika;
using Sezika.Cli;
using Sezika.IndependentModelTool;
using CliProgram = Sezika.Cli.Program;

if (args.Length > 1 || (args.Length == 1 && args[0] != "--require-aot"))
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new CliError
    {
        Code = "cli_checks_arguments_invalid",
        Message = "CliChecks accepts no arguments or the single --require-aot flag.",
    }, CliJsonContext.Default.CliError));
    return 2;
}
var requireAot = args.Length == 1;
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var watch = Stopwatch.StartNew();
var checks = 0;
void Check(bool value, string name)
{
    deadline.Token.ThrowIfCancellationRequested();
    if (++checks > 80 || watch.Elapsed > TimeSpan.FromSeconds(30)) throw new InvalidOperationException("CLI check budget exceeded.");
    if (!value) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS {checks}: {name}");
}

if (requireAot)
{
    Check(!RuntimeFeature.IsDynamicCodeSupported && !RuntimeFeature.IsDynamicCodeCompiled &&
        AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is null, "check tool executes in Native AOT runtime");
    Console.WriteLine($"AOT execution: dynamic_code_supported={RuntimeFeature.IsDynamicCodeSupported}; dynamic_code_compiled={RuntimeFeature.IsDynamicCodeCompiled}; trusted_platform_assemblies=null");
}

var evidenceRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".artifacts", "supervision-20261007", "cli"));
var ownedRoot = Path.GetFullPath(Path.Combine(evidenceRoot, "cli-check-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(ownedRoot);
var ownedFiles = new List<string>();
string WriteOwned(string name, string text)
{
    deadline.Token.ThrowIfCancellationRequested();
    if (ownedFiles.Count >= 20) throw new InvalidOperationException("Fixture file count exceeded.");
    var path = Path.GetFullPath(Path.Combine(ownedRoot, name));
    if (Path.GetDirectoryName(path) != ownedRoot) throw new InvalidOperationException("Fixture path escaped.");
    File.WriteAllText(path, text, new UTF8Encoding(false));
    ownedFiles.Add(path);
    return path;
}

async Task<(int Code, string Out, string Error)> Run(string[] arguments, Stream? input = null,
    IndependentRuntimeLoader? loader = null, CancellationToken? token = null)
{
    using var stdout = new StringWriter();
    using var stderr = new StringWriter();
    var code = await CliProgram.RunAsync(arguments, input, stdout, stderr, token ?? deadline.Token, loader);
    return (code, stdout.ToString(), stderr.ToString());
}
void Error((int Code, string Out, string Error) value, string expected, string name)
{
    using var json = JsonDocument.Parse(value.Error);
    Check(value.Code == 2 && value.Out.Length == 0 && json.RootElement.GetProperty("code").GetString() == expected, name);
}

try
{
    // Real product Main is exercised with its fixed production loader.
    var consoleOut = Console.Out;
    var consoleError = Console.Error;
    using var publicOut = new StringWriter();
    using var publicError = new StringWriter();
    try
    {
        Console.SetOut(publicOut); Console.SetError(publicError);
        var publicCode = await CliProgram.Main(["inspect", "--model-format", "unknown"]);
        Console.SetOut(consoleOut); Console.SetError(consoleError);
        Error((publicCode, publicOut.ToString(), publicError.ToString()), "cli_model_format_invalid", "product Main rejects unknown format");
    }
    finally { Console.SetOut(consoleOut); Console.SetError(consoleError); }

    var help = await Run(["--help"]);
    Check(help.Code == 0 && help.Out.Contains("independent-v1", StringComparison.Ordinal) && help.Error.Length == 0, "help documents independent route and budgets");
    Error(await Run(["inspect"]), "cli_model_required", "model is required");
    Error(await Run(["other"]), "cli_command_unknown", "unknown command is structured");
    Error(await Run(["inspect", "--model", "--max-tokens", "2"]), "cli_option_value_missing", "option cannot consume another option");
    Error(await Run(["inspect", "--model", ownedRoot, "--model", ownedRoot]), "cli_option_duplicate", "duplicate option is rejected");
    Error(await Run(["inspect", "--command-timeout-seconds", "0"]), "cli_option_invalid", "zero command budget is rejected");
    Error(await Run(["predict", "--input-timeout-seconds", "301"]), "cli_option_invalid", "unbounded input budget is rejected");
    Error(await Run(["inspect", "--head", "a"]), "cli_option_invalid", "legacy route rejects independent head");
    Error(await Run(["inspect", "--model-format", "independent-v1", "--model", ownedRoot]), "cli_head_required", "independent head binding is required");
    Check(CliOptions.Parse([]).ModelFormat == CliModelFormat.LegacyLaya && CliOptions.Parse([]).MaxTokens == 4096, "legacy remains the default");
    Error(await Run(["inspect", "--model-format", "independent-v1", "--max-tokens", "1025"]), "cli_option_invalid", "independent token budget matches fixed encoder");

    var tokenizerText = new StringBuilder("{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true,\"fuse_unk\":true,\"unk_token\":\"<unk>\",\"vocab\":{\"<pad>\":0,\"<eos>\":1,\"<bos>\":2,\"<unk>\":3,\"<mask>\":4");
    // Fixed 256-item loop, guarded by the global wall-clock/cancellation budget.
    for (var value = 0; value < 256; value++)
    {
        deadline.Token.ThrowIfCancellationRequested();
        tokenizerText.Append($",\"<0x{value:X2}>\":{1000 + value}");
    }
    tokenizerText.Append("},\"merges\":[]}}");
    var tokenizerPath = WriteOwned("tokenizer.json", tokenizerText.ToString());
    var identity = new MarkerFeatureIdentity("sezika/cli-fixture", new string('A', 64), new string('B', 64), new string('D', 64), 2);
    var features = new MarkerFeatureSet(identity, [new MarkerFeatureExample("fixture", MarkerQuestionType.Choice, "en", [2, 4, 4, 1], [1, 2], [[1f, 0f], [-1f, 0f]], 0)]);
    var state = IndependentMarkerHeadTrainer.Train(features, new MarkerTrainingOptions(7, 1, 0.1f, TimeSpan.FromSeconds(2)), cancellationToken: deadline.Token);
    var headPath = Path.Combine(ownedRoot, "head.asset");
    ownedFiles.Add(headPath);
    var headHash = IndependentMarkerHeadAssetStore.SaveNew(headPath, state, deadline.Token);
    var binding = new IndependentHeadBinding { SchemaVersion = 1, Identity = identity, FeatureSha256 = state.FeatureSha256, HeadSha256 = headHash };
    var bindingPath = WriteOwned("binding.json", JsonSerializer.Serialize(binding, CliJsonContext.Default.IndependentHeadBinding));
    var independent = new[] { "--model-format", "independent-v1", "--model", ownedRoot, "--head", headPath, "--head-binding", bindingPath };
    string[] Args(string command, params string[] extra) => [command, .. independent, .. extra];
    IndependentDecisionV1Runtime LoadFixture(string package, string head, MarkerFeatureIdentity expected, string featureHash,
        string expectedHeadHash, IndependentDecisionRuntimeOptions options, CancellationToken token)
    {
        Check(package == ownedRoot, "internal fixture seam receives requested package");
        var asset = IndependentMarkerHeadAssetStore.Load(head, expected, featureHash, expectedHeadHash, token);
        var config = new ModernBertConfig { VocabularySize = 2000, HiddenSize = 2, IntermediateSize = 2, LayerCount = 1,
            HeadCount = 1, MaxTokens = 1024, GlobalAttentionEvery = 1, LocalAttention = 2, GlobalRopeTheta = 16000,
            LocalRopeTheta = 16000, NormEpsilon = 1e-5f };
        var model = new IndependentEncoderModelPackage { ModelId = identity.ModelId, SourceModelId = "synthetic/cli-fixture", Revision = "fixture-v1",
            License = "MIT", EncoderSha256 = identity.EncoderSha256, TokenizerSha256 = identity.TokenizerSha256,
            Tokenizer = new TokenizerJson(tokenizerPath, token), EstimatedResidentBytes = 0,
            Encoder = new ModernBertEncoder(config, new ModernBertWeights { TokenEmbeddings = new float[4000], EmbeddingNorm = [1f, 1f], FinalNorm = [1f, 1f],
                Layers = [new ModernBertLayerWeights { AttentionNorm = null, Qkv = new float[12], AttentionOutput = new float[4],
                    MlpNorm = [1f, 1f], MlpUp = new float[8], MlpDown = new float[4] }] },
                new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromSeconds(3) }, token) };
        return new IndependentDecisionV1Runtime(model, asset, options);
    }
    var inspect = await Run(Args("inspect"), loader: LoadFixture);
    using (var json = JsonDocument.Parse(inspect.Out))
        Check(inspect.Code == 0 && inspect.Error.Length == 0 && json.RootElement.GetProperty("runtime_load_verified").GetBoolean() &&
            !json.RootElement.GetProperty("quality_verified").GetBoolean(), "independent inspect verifies runtime binding without quality claim");
    Error(await Run(Args("inspect")), "independent_model_not_installed", "production loader does not accept synthetic package");
    Error(await Run(Args("inspect", "--minimum-concentration", "0")), "cli_option_invalid", "independent route rejects unsupported abstention option");

    // Exercise the actual report producer, including fractional training seconds;
    // this project reference exists only in the check tool, never in the CLI.
    var workflowPaths = new TrainingOutputPaths(Path.Combine(ownedRoot, "workflow.checkpoint"),
        Path.Combine(ownedRoot, "workflow.head"), Path.Combine(ownedRoot, "workflow.report.json"));
    ownedFiles.AddRange([workflowPaths.Checkpoint, workflowPaths.Head, workflowPaths.Report]);
    var workflow = IndependentTrainingWorkflow.Run(features,
        [new MarkerFeatureExample("development-fixture", MarkerQuestionType.Choice, "en", [2, 4, 4, 1], [1, 2], [[1f, 0f], [-1f, 0f]], 0)],
        new string('E', 64), new MarkerTrainingOptions(7, 1, 0.1f, TimeSpan.FromMilliseconds(1500)),
        workflowPaths, TrainingFeatureSource.SyntheticFixture, cancellationToken: deadline.Token);
    string[] ReportArgs(string command, params string[] extra) => [command, "--model-format", "independent-v1", "--model", ownedRoot,
        "--head", workflowPaths.Head, "--head-binding", workflowPaths.Report, .. extra];
    Check(workflow.Report.TrainingTimeoutSeconds == 1.5 && (await Run(ReportArgs("inspect"), loader: LoadFixture)).Code == 0,
        "actual schema 2 training report is directly consumed by CLI");
    var unknownReport = WriteOwned("unknown-report.json", File.ReadAllText(workflowPaths.Report).Replace("\"schema_version\": 2", "\"schema_version\": 2,\"extra\":1", StringComparison.Ordinal));
    Error(await Run(["inspect", .. independent[..^1], unknownReport]), "cli_head_binding_invalid", "schema 2 unknown field is rejected");

    const string request = """
        {"schema_version":1,"model_id":"sezika/cli-fixture","state":"x","questions":[
          {"id":"choice","type":"choice","instruction":"choose","candidates":[{"id":"a","text":"first"},{"id":"b","text":"second"}]},
          {"id":"score","type":"score","instruction":"rate","levels":["low","high"]},
          {"id":"bool","type":"boolean","instruction":"judge","statement":"x","when_false":"no","when_true":"yes"}]}
        """;
    var requestPath = WriteOwned("request.json", request);
    var predict = await Run(Args("predict", "--input", requestPath), loader: LoadFixture);
    Check((await Run(ReportArgs("predict", "--input", requestPath), loader: LoadFixture)).Code == 0,
        "producer exported head and report feed the same CLI prediction path");
    using (var json = JsonDocument.Parse(predict.Out))
    {
        var answers = json.RootElement.GetProperty("answers");
        Check(predict.Code == 0 && predict.Error.Length == 0 && answers.GetArrayLength() == 3 && json.RootElement.GetProperty("status").GetString() == "uncalibrated", "CLI evaluates all three decision-v1 primitives with tiny C# runtime");
        foreach (var answer in answers.EnumerateArray())
        {
            var probabilities = answer.GetProperty("probabilities").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            Check(probabilities.All(double.IsFinite) && Math.Abs(probabilities.Sum() - 1) < 1e-12, "CLI emits finite normalized distributions");
        }
    }
    using (var input = new MemoryStream(Encoding.UTF8.GetBytes(request)))
        Check((await Run(Args("predict"), input, LoadFixture)).Code == 0, "CLI accepts bounded stdin path");
    using (var input = new MemoryStream(Encoding.UTF8.GetBytes(request.Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1", StringComparison.Ordinal))))
        Error(await Run(Args("predict"), input, LoadFixture), "decision_duplicate_property", "duplicate request key is rejected before loading");
    using (var input = new MemoryStream(Encoding.UTF8.GetBytes(request.Replace("sezika/cli-fixture", "sezika/other", StringComparison.Ordinal))))
        Error(await Run(Args("predict"), input, LoadFixture), "independent_runtime_model_mismatch", "request model identity mismatch is structured");
    Error(await Run(Args("predict", "--input", requestPath, "--max-tokens", "2"), loader: LoadFixture), "decision_v1_token_budget_exceeded", "strict token budget rejects truncation");

    var badHead = WriteOwned("wrong-head-binding.json", JsonSerializer.Serialize(binding with { HeadSha256 = new string('0', 64) }, CliJsonContext.Default.IndependentHeadBinding));
    Error(await Run(["inspect", .. independent[..^1], badHead], loader: LoadFixture), "head_asset_hash_invalid", "wrong head file SHA-256 is rejected");
    var badFeature = WriteOwned("wrong-feature-binding.json", JsonSerializer.Serialize(binding with { FeatureSha256 = new string('0', 64) }, CliJsonContext.Default.IndependentHeadBinding));
    Error(await Run(["inspect", .. independent[..^1], badFeature], loader: LoadFixture), "head_asset_identity_mismatch", "wrong feature SHA-256 is rejected");
    var duplicateBinding = WriteOwned("duplicate-binding.json", File.ReadAllText(bindingPath).Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1", StringComparison.Ordinal));
    Error(await Run(["inspect", .. independent[..^1], duplicateBinding]), "cli_head_binding_invalid", "duplicate head binding field is rejected");
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        Error(await Run(Args("inspect"), token: cancelled.Token), "decision_cancelled", "pre-cancellation is structured");
    }
    using (var blocked = new BlockedInput())
        Error(await Run(Args("predict", "--input-timeout-seconds", "1"), blocked), "cli_input_timeout", "blocked input is cancelled by its input budget");
    using (var blocked = new BlockedInput())
        Error(await Run(Args("predict", "--command-timeout-seconds", "1", "--input-timeout-seconds", "5"), blocked), "decision_cancelled", "total command budget also bounds input");
    Console.WriteLine($"CLI checks passed: {checks}; evidence: synthetic fixture only; quality not accepted.");
}
finally
{
    var cleanupWatch = Stopwatch.StartNew();
    if (ownedFiles.Count > 20 || Path.GetDirectoryName(ownedRoot) != evidenceRoot || !Path.GetFileName(ownedRoot).StartsWith("cli-check-", StringComparison.Ordinal))
        throw new InvalidOperationException("Owned fixture cleanup boundary is invalid.");
    foreach (var path in ownedFiles)
    {
        if (cleanupWatch.Elapsed > TimeSpan.FromSeconds(5) || Path.GetDirectoryName(Path.GetFullPath(path)) != ownedRoot)
            throw new InvalidOperationException("Fixture cleanup budget/path exceeded.");
        if (File.Exists(path)) File.Delete(path);
    }
    Directory.Delete(ownedRoot, recursive: false);
}
return 0;

sealed class BlockedInput : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    { await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken); return 0; }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
