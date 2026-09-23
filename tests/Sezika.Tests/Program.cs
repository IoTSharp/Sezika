using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}

try
{
    try
    {
        DecisionRequestParser.Parse(Encoding.UTF8.GetBytes("{\"model\":\"x\",\"model\":\"y\"}"));
        throw new InvalidOperationException("duplicate property was accepted");
    }
    catch (DecisionException exception) when (exception.Code == "decision_duplicate_property")
    {
        Check(true, "duplicate JSON property rejection");
    }

    using var state = JsonDocument.Parse("{\"message\":\"hello\"}");
    using var instruction = JsonDocument.Parse("\"choose\"");
    using var first = JsonDocument.Parse("\"one\"");
    using var second = JsonDocument.Parse("\"two\"");
    using var engine = new DecisionEngine(DemoModelFactory.CreateTiny());
    var response = engine.Evaluate(new DecisionRequest
    {
        Model = "sezika-demo-tiny",
        State = state.RootElement.Clone(),
        Questions = new Dictionary<string, Question>
        {
            ["choice"] = new ChoiceQuestion { Instructions = instruction.RootElement.Clone(), Criteria = new Dictionary<string, JsonElement> { ["one"] = first.RootElement.Clone(), ["two"] = second.RootElement.Clone() } },
            ["score"] = new ScoreQuestion { Instructions = instruction.RootElement.Clone(), Criteria = [first.RootElement.Clone(), second.RootElement.Clone()] },
            ["boolean"] = new BooleanQuestion { Instructions = instruction.RootElement.Clone(), Criteria = new BooleanCriteria { WhenTrue = first.RootElement.Clone(), WhenFalse = second.RootElement.Clone() } },
        },
    });
    Check(response.Answers.Count == 3 && response.Answers.Values.All(answer => answer.Calibration.Status == "uncalibrated"), "CPU typed decision response");
    Check(response.Answers["score"] is ScoreAnswer score && score.Score is >= 0 and <= 1, "score expected value bounds");

    var metrics = CalibrationEvaluator.Evaluate([
        new CalibrationExample(0, [0.9, 0.1]),
        new CalibrationExample(1, [0.2, 0.8]),
    ]);
    Check(metrics.Accuracy == 1 && metrics.Coverage == 1, "calibration metrics");
    var trained = DecisionHeadTrainer.Train([
        new HeadTrainingExample([1f, 0f], true),
        new HeadTrainingExample([-1f, 0f], false),
    ], epochs: 20);
    Check(trained.Weights[0] > 0 && double.IsFinite(trained.FinalLogLoss), "frozen encoder head training");

    var realTokenizerPath = Path.Combine(".artifacts", "models", "laya-mmbert", "tokenizer", "tokenizer.json");
    var oraclePath = Path.Combine("tests", "fixtures", "tokenizer-mmbert-oracle.json");
    if (File.Exists(realTokenizerPath) && File.Exists(oraclePath))
    {
        var realTokenizer = new TokenizerJson(realTokenizerPath);
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(oraclePath));
        foreach (var item in oracle.RootElement.GetProperty("cases").EnumerateArray())
        {
            var text = item.GetProperty("text").GetString() ?? string.Empty;
            var expected = item.GetProperty("ids").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            Check(realTokenizer.Encode(text, 1024).SequenceEqual(expected), $"mmBERT tokenizer oracle: {text}");
        }
    }
    else
    {
        Console.WriteLine("SKIP: mmBERT tokenizer oracle (download the pinned model asset first)");
    }

    var tempDirectory = Path.Combine(Path.GetTempPath(), $"sezika-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);
    try
    {
        var header = Encoding.UTF8.GetBytes("{\"tensor\":{\"dtype\":\"F32\",\"shape\":[2],\"data_offsets\":[0,8]}}");
        var file = new byte[8 + header.Length + 8];
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(0, 8), (ulong)header.Length);
        header.CopyTo(file.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(8 + header.Length, 4), BitConverter.SingleToInt32Bits(1.5f));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(12 + header.Length, 4), BitConverter.SingleToInt32Bits(-2f));
        var tensorPath = Path.Combine(tempDirectory, "weights.safetensors");
        File.WriteAllBytes(tensorPath, file);
        var tensors = SafeTensorReader.Read(tensorPath);
        Check(tensors["tensor"].Values.SequenceEqual([1.5f, -2f]), "safe-tensors FP32 reader");
        Check(Convert.ToHexString(SHA256.HashData(file)).Length == 64, "asset hash primitive");
    }
    finally
    {
        if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
    }

    Console.WriteLine($"Sezika tests passed: {passed}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
