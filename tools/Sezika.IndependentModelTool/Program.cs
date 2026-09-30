using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Sezika;

const string SourceSha256 = "8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e";
const long SourceBytes = 1_231_188_142;
var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
try
{
    if (command == "convert-pytorch")
    {
        var input = RequiredOption("--input"); var output = RequiredOption("--output");
        var timeout = BoundedOption("--timeout-seconds", 1, 1800, 900);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        var result = ConvertPyTorch(Path.GetFullPath(input), Path.GetFullPath(output), cancellation.Token);
        Console.WriteLine($"converted {result.TensorCount} tensors, bytes={result.Bytes:N0}, sha256={result.Sha256}");
        return 0;
    }
    if (command == "smoke")
    {
        var package = Path.GetFullPath(RequiredOption("--package"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var model = IndependentModelLoader.Load(package, new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromMinutes(10) }, cancellation.Token);
        using var state = JsonDocument.Parse("{\"text\":\"independent encoder smoke\"}");
        var question = new IndependentChoiceQuestion("intent", "choose the intent", [
            new IndependentChoiceCandidate("a", "first"), new IndependentChoiceCandidate("b", "second")]);
        var request = new IndependentDecisionRequest(1, model.ModelId, state.RootElement.Clone(), [question]);
        var sequence = IndependentDecisionV1SequenceBuilder.Build(model.Tokenizer, request, question, cancellationToken: cancellation.Token);
        var hidden = model.Encoder.Encode(sequence.TokenIds, cancellation.Token);
        if (hidden.Length != checked(sequence.TokenIds.Length * model.Encoder.Config.HiddenSize) || hidden.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Independent encoder smoke output is not finite or complete.");
        var hiddenHash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(hidden.AsSpan())));
        var tokenHash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(sequence.TokenIds.AsSpan())));
        Console.WriteLine($"model={model.ModelId} source={model.SourceModelId}@{model.Revision} tokenizer_sha256={model.TokenizerSha256} encoder_sha256={model.EncoderSha256} tokens={sequence.TokenIds.Length} token_ids_sha256={tokenHash} markers={string.Join(',', sequence.MarkerPositions)} hidden={hidden.Length} hidden_sha256={hiddenHash} hidden_min={hidden.Min():R} hidden_max={hidden.Max():R} resident_bytes={model.EstimatedResidentBytes}");
        return 0;
    }
    if (command == "train-original")
    {
        var package = Path.GetFullPath(RequiredOption("--package"));
        var recordsPath = Path.GetFullPath(RequiredOption("--records"));
        var headPath = Path.GetFullPath(RequiredOption("--head"));
        var reportPath = Path.GetFullPath(RequiredOption("--report"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        TrainOriginal(package, recordsPath, headPath, reportPath, cancellation.Token);
        return 0;
    }
    Console.WriteLine("Sezika.IndependentModelTool convert-pytorch --input <pytorch_model.bin> --output <model.safetensors> [--timeout-seconds 1..1800]");
    Console.WriteLine("Sezika.IndependentModelTool smoke --package <independent-model-package>");
    Console.WriteLine("Sezika.IndependentModelTool train-original --package <package> --records <records.jsonl> --head <head.asset> --report <report.json>");
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("independent-model-tool: conversion cancelled or exceeded its bounded timeout");
    return 124;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"independent-model-tool: {exception.Message}");
    return 1;
}

static string RequiredOption(string name)
{
    for (var index = 1; index + 1 < Environment.GetCommandLineArgs().Length; index++)
        if (Environment.GetCommandLineArgs()[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return Environment.GetCommandLineArgs()[index + 1];
    throw new ArgumentException($"Missing {name}.");
}

static int BoundedOption(string name, int min, int max, int fallback)
{
    var args = Environment.GetCommandLineArgs();
    for (var index = 1; index + 1 < args.Length; index++)
        if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[index + 1], out var value))
        {
            if (value < min || value > max) throw new ArgumentOutOfRangeException(name);
            return value;
        }
    return fallback;
}

static ConversionResult ConvertPyTorch(string inputPath, string outputPath, CancellationToken cancellationToken)
{
    if (!File.Exists(inputPath)) throw new FileNotFoundException("Pinned PyTorch source is missing.", inputPath);
    var sourceInfo = new FileInfo(inputPath);
    if (sourceInfo.Length != SourceBytes || !HashFile(inputPath, cancellationToken).Equals(SourceSha256, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Pinned PyTorch source size or SHA-256 does not match the audited revision.");
    if (File.Exists(outputPath)) throw new IOException($"Output already exists: {outputPath}");
    var partial = outputPath + ".partial-" + Guid.NewGuid().ToString("N");
    try
    {
        using var archive = ZipFile.OpenRead(inputPath);
        var pickleEntry = archive.GetEntry("pytorch_model/data.pkl") ?? throw new InvalidDataException("PyTorch data.pkl is missing.");
        if (pickleEntry.Length is <= 0 or > 4 * 1024 * 1024) throw new InvalidDataException("data.pkl exceeds the conversion bound.");
        byte[] pickle;
        using (var stream = pickleEntry.Open()) using (var memory = new MemoryStream()) { stream.CopyTo(memory); pickle = memory.ToArray(); }
        cancellationToken.ThrowIfCancellationRequested();
        var tensors = new PickleReader(pickle, cancellationToken).ReadTensorMap();
        var descriptors = tensors.Values.Where(tensor => tensor.Name.StartsWith("model.", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        if (descriptors.Length is < 100 or > 140 || tensors.Count - descriptors.Length is < 1 or > 8)
            throw new InvalidDataException($"Unexpected encoder/head tensor counts: encoder={descriptors.Length}, total={tensors.Count}.");
        long payloadBytes = 0;
        foreach (var tensor in descriptors)
        {
            ValidateTensor(tensor);
            payloadBytes = checked(payloadBytes + tensor.Numel * 4);
        }
        var header = BuildHeader(descriptors, payloadBytes);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        {
            Span<byte> size = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(size, checked((ulong)header.Length)); output.Write(size); output.Write(header);
            foreach (var tensor in descriptors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.GetEntry($"pytorch_model/data/{tensor.StorageKey}") ?? throw new InvalidDataException($"Storage {tensor.StorageKey} is missing.");
                if (entry.Length < checked(tensor.StorageOffset * 4 + tensor.Numel * 4)) throw new InvalidDataException($"Storage {tensor.StorageKey} is too short.");
                using var source = entry.Open(); Skip(source, checked(tensor.StorageOffset * 4), cancellationToken); CopyExactly(source, output, checked(tensor.Numel * 4), cancellationToken);
            }
            output.Flush(flushToDisk: true);
        }
        File.Move(partial, outputPath);
        return new ConversionResult(descriptors.Length, new FileInfo(outputPath).Length, HashFile(outputPath, cancellationToken));
    }
    finally
    {
        if (File.Exists(partial)) File.Delete(partial);
    }
}

static byte[] BuildHeader(IReadOnlyList<TensorRef> tensors, long payloadBytes)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
        writer.WriteStartObject(); var offset = 0L;
        foreach (var tensor in tensors)
        {
            writer.WritePropertyName(tensor.Name); writer.WriteStartObject(); writer.WriteString("dtype", "F32");
            writer.WritePropertyName("shape"); writer.WriteStartArray(); foreach (var dimension in tensor.Shape) writer.WriteNumberValue(dimension); writer.WriteEndArray();
            writer.WritePropertyName("data_offsets"); writer.WriteStartArray(); writer.WriteNumberValue(offset); offset = checked(offset + tensor.Numel * 4); writer.WriteNumberValue(offset); writer.WriteEndArray(); writer.WriteEndObject();
        }
        writer.WriteEndObject(); writer.Flush();
    }
    if (stream.Length > 64 * 1024 * 1024 || stream.Length > int.MaxValue || stream.Length == 0 || stream.Length > payloadBytes * 2)
        throw new InvalidDataException("SafeTensors header is outside the conversion bound.");
    return stream.ToArray();
}

static void ValidateTensor(TensorRef tensor)
{
    if (string.IsNullOrWhiteSpace(tensor.Name) || !tensor.Name.StartsWith("model.", StringComparison.Ordinal) &&
        !tensor.Name.StartsWith("decoder.", StringComparison.Ordinal) && !tensor.Name.StartsWith("head.", StringComparison.Ordinal))
        throw new InvalidDataException($"Unexpected tensor name: {tensor.Name}");
    if (tensor.Shape.Length is < 1 or > 3 || tensor.Shape.Any(value => value <= 0)) throw new InvalidDataException($"Invalid shape: {tensor.Name}");
    var numel = tensor.Shape.Aggregate(1L, (current, next) => checked(current * next));
    if (numel != tensor.Numel || tensor.StorageOffset < 0) throw new InvalidDataException($"Tensor size mismatch: {tensor.Name}");
    for (var index = 0; index < tensor.Shape.Length; index++)
    {
        var expected = 1L; for (var tail = index + 1; tail < tensor.Shape.Length; tail++) expected = checked(expected * tensor.Shape[tail]);
        if (tensor.Stride[index] != expected) throw new InvalidDataException($"Non-contiguous tensor is not supported: {tensor.Name}");
    }
}

static void Skip(Stream stream, long bytes, CancellationToken cancellationToken)
{
    var buffer = new byte[1024 * 1024]; while (bytes > 0) { cancellationToken.ThrowIfCancellationRequested(); var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes)); if (read == 0) throw new EndOfStreamException(); bytes -= read; }
}

static void CopyExactly(Stream source, Stream destination, long bytes, CancellationToken cancellationToken)
{
    var buffer = new byte[1024 * 1024]; while (bytes > 0) { cancellationToken.ThrowIfCancellationRequested(); var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes)); if (read == 0) throw new EndOfStreamException(); destination.Write(buffer, 0, read); bytes -= read; }
}

static string HashFile(string path, CancellationToken cancellationToken)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[1024 * 1024]; int read;
    while ((read = stream.Read(buffer, 0, buffer.Length)) != 0) { cancellationToken.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
    return Convert.ToHexString(hash.GetHashAndReset());
}

static void TrainOriginal(string packagePath, string recordsPath, string headPath, string reportPath, CancellationToken cancellationToken)
{
    var records = ReadOriginalRecords(recordsPath, cancellationToken);
    if (records.Count is < 2 or > 32 || records.Count(r => r.Split == "train") is < 1 || records.Count(r => r.Split == "development") is < 1)
        throw new InvalidDataException("Original dataset requires bounded train and development records.");
    var recordsHash = HashFile(recordsPath, cancellationToken);
    var manifestPath = Path.Combine(Path.GetDirectoryName(recordsPath)!, "manifest.json");
    if (!File.Exists(manifestPath)) throw new FileNotFoundException("Original dataset manifest is missing.", manifestPath);
    var dataHash = HashFile(manifestPath, cancellationToken);
    using var model = IndependentModelLoader.Load(packagePath, new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromMinutes(10) }, cancellationToken);
    var identity = new MarkerFeatureIdentity(model.ModelId, model.EncoderSha256, model.TokenizerSha256, dataHash, model.Encoder.Config.HiddenSize);
    var features = new List<(OriginalRecord Record, MarkerFeatureExample Feature)>();
    foreach (var record in records)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var feature = IndependentMarkerFeatureExporter.Export(model.Tokenizer, model.Encoder, identity, record.Request, record.Question,
            record.RecordId, record.Language, record.TargetIndex, cancellationToken: cancellationToken);
        features.Add((record, feature));
    }
    var train = features.Where(item => item.Record.Split == "train").Select(item => item.Feature).ToArray();
    var development = features.Where(item => item.Record.Split == "development").ToArray();
    var training = IndependentMarkerHeadTrainer.Train(new MarkerFeatureSet(identity, train),
        new MarkerTrainingOptions(20260930, 24, 0.05f, TimeSpan.FromMinutes(2)), cancellationToken: cancellationToken);
    Directory.CreateDirectory(Path.GetDirectoryName(headPath)!); Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    var headHash = IndependentMarkerHeadAssetStore.SaveNew(headPath, training, cancellationToken);
    using (var stream = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
    {
        writer.WriteStartObject(); writer.WriteNumber("schema_version", 1); writer.WriteString("status", "development_smoke_only");
        writer.WriteString("model_id", model.ModelId); writer.WriteString("encoder_sha256", model.EncoderSha256); writer.WriteString("tokenizer_sha256", model.TokenizerSha256);
        writer.WriteString("data_manifest_sha256", dataHash); writer.WriteString("records_sha256", recordsHash); writer.WriteString("feature_sha256", training.FeatureSha256); writer.WriteString("head_sha256", headHash);
        writer.WriteNumber("train_count", train.Length); writer.WriteNumber("development_count", development.Length); writer.WriteNumber("completed_steps", training.CompletedSteps); writer.WriteNumber("last_loss", training.LastLoss);
        writer.WriteBoolean("uses_real_independent_encoder", true); writer.WriteBoolean("represents_quality_acceptance", false);
        writer.WritePropertyName("development_groups"); writer.WriteStartArray();
        foreach (var group in development.GroupBy(item => (item.Record.Language, item.Record.Question.GetType().Name)).OrderBy(group => group.Key.Language).ThenBy(group => group.Key.Name))
        {
            var correct = 0;
            foreach (var item in group) if (ArgMax(training.Head.Score(item.Feature.Type, item.Feature.Features, () => cancellationToken.ThrowIfCancellationRequested())) == item.Feature.TargetIndex) correct++;
            writer.WriteStartObject(); writer.WriteString("language", group.Key.Language); writer.WriteString("question_type", QuestionTypeName(group.First().Feature.Type)); writer.WriteNumber("count", group.Count()); writer.WriteNumber("correct", correct); writer.WriteNumber("accuracy", (double)correct / group.Count()); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
    }
    Console.WriteLine($"trained original data records={records.Count} train={train.Length} development={development.Length} steps={training.CompletedSteps} feature_sha256={training.FeatureSha256} head_sha256={headHash} report={reportPath}");
}

static string QuestionTypeName(MarkerQuestionType type) => type switch
{
    MarkerQuestionType.Choice => "choice",
    MarkerQuestionType.Score => "score",
    MarkerQuestionType.Boolean => "boolean",
    _ => throw new InvalidDataException("Unknown marker question type."),
};

static int ArgMax(double[] values)
{
    var index = 0; for (var candidate = 1; candidate < values.Length; candidate++) if (values[candidate] > values[index]) index = candidate; return index;
}

static IReadOnlyList<OriginalRecord> ReadOriginalRecords(string path, CancellationToken cancellationToken)
{
    if (!File.Exists(path)) throw new FileNotFoundException("Original records file is missing.", path);
    var output = new List<OriginalRecord>(); var seen = new HashSet<string>(StringComparer.Ordinal);
    using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    for (var lineNumber = 1; !reader.EndOfStream; lineNumber++)
    {
        cancellationToken.ThrowIfCancellationRequested(); if (lineNumber > 32) throw new InvalidDataException("Original record count exceeds 32.");
        var line = reader.ReadLine() ?? string.Empty; if (line.Length is 0 or > 65_536) throw new InvalidDataException($"Original record line {lineNumber} is empty or oversized.");
        using var document = JsonDocument.Parse(line); var json = document.RootElement;
        var recordId = json.GetProperty("record_id").GetString() ?? string.Empty; var split = json.GetProperty("split").GetString() ?? string.Empty; var language = json.GetProperty("language").GetString() ?? string.Empty; var type = json.GetProperty("type").GetString() ?? string.Empty;
        if (!seen.Add(recordId) || split is not ("train" or "development") || language is not ("zh" or "en")) throw new InvalidDataException($"Original record {recordId} identity or split is invalid.");
        var instruction = json.GetProperty("instruction").GetString() ?? string.Empty; var state = json.GetProperty("state").Clone(); var target = json.GetProperty("target_index").GetInt32(); IndependentDecisionQuestion question;
        switch (type)
        {
            case "choice": question = new IndependentChoiceQuestion(recordId, instruction, json.GetProperty("candidates").EnumerateArray().Select(value => new IndependentChoiceCandidate(value.GetProperty("id").GetString() ?? string.Empty, value.GetProperty("text").GetString() ?? string.Empty)).ToArray()); break;
            case "score": question = new IndependentScoreQuestion(recordId, instruction, json.GetProperty("levels").EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray()); break;
            case "boolean": question = new IndependentBooleanQuestion(recordId, instruction, json.GetProperty("statement").GetString() ?? string.Empty, json.GetProperty("when_false").GetString() ?? string.Empty, json.GetProperty("when_true").GetString() ?? string.Empty); break;
            default: throw new InvalidDataException($"Original record {recordId} question type is unsupported.");
        }
        output.Add(new OriginalRecord(recordId, split, language, target, new IndependentDecisionRequest(1, IndependentModelLoader.ModelId, state, [question]), question));
    }
    return output;
}

readonly record struct OriginalRecord(string RecordId, string Split, string Language, int TargetIndex, IndependentDecisionRequest Request, IndependentDecisionQuestion Question);

readonly record struct ConversionResult(int TensorCount, long Bytes, string Sha256);
sealed record TensorRef(string Name, string StorageKey, long StorageOffset, long Numel, long[] Shape, long[] Stride);

sealed class PickleReader
{
    private readonly byte[] _bytes; private readonly CancellationToken _cancellationToken; private int _position;
    private readonly List<object> _stack = []; private readonly Dictionary<int, object> _memo = [];
    private sealed record Mark; private sealed record PyString(string Value); private sealed record PyInt(long Value); private sealed record PyGlobal(string Module, string Name);
    private sealed record PyTuple(object[] Values); private sealed record PyStorage(string Key, long Numel); private sealed record PyTensor(string StorageKey, long Offset, long[] Shape, long[] Stride, long Numel);

    public PickleReader(byte[] bytes, CancellationToken cancellationToken) { _bytes = bytes; _cancellationToken = cancellationToken; }

    public IReadOnlyDictionary<string, TensorRef> ReadTensorMap()
    {
        object? result = null;
        while (_position < _bytes.Length)
        {
            if ((_position & 1023) == 0) _cancellationToken.ThrowIfCancellationRequested();
            var opcode = ReadByte();
            switch (opcode)
            {
                case 0x80: _ = ReadByte(); break;
                case 0x2E: result = Top(); _position = _bytes.Length; break;
                case 0x28: _stack.Add(new Mark()); break;
                case 0x29: _stack.Add(new PyTuple([])); break;
                case 0x7D: _stack.Add(new Dictionary<object, object>()); break;
                case 0x5D: _stack.Add(new List<object>()); break;
                case 0x58: _stack.Add(new PyString(ReadUtf8(checked((int)ReadUInt32())))); break;
                case 0x55: _stack.Add(new PyString(ReadUtf8(ReadByte()))); break;
                case 0x63: _stack.Add(new PyGlobal(ReadLine(), ReadLine())); break;
                case 0x4B: _stack.Add(new PyInt(ReadByte())); break;
                case 0x4D: _stack.Add(new PyInt(ReadUInt16())); break;
                case 0x4A: _stack.Add(new PyInt(ReadInt32())); break;
                case 0x8A: _stack.Add(new PyInt(ReadLong1())); break;
                case 0x4E: _stack.Add(null!); break;
                case 0x88: _stack.Add(new PyInt(1)); break;
                case 0x89: _stack.Add(new PyInt(0)); break;
                case 0x71: _memo[ReadByte()] = Top(); break;
                case 0x72: _memo[checked((int)ReadUInt32())] = Top(); break;
                case 0x68: _stack.Add(_memo[ReadByte()]); break;
                case 0x6A: _stack.Add(_memo[checked((int)ReadUInt32())]); break;
                case 0x74: PushTuple(PopToMark().ToArray()); break;
                case 0x85: PushTuple([Pop()]); break;
                case 0x86: { var b = Pop(); var a = Pop(); PushTuple([a, b]); break; }
                case 0x87: { var c = Pop(); var b = Pop(); var a = Pop(); PushTuple([a, b, c]); break; }
                case 0x51: { var value = Pop(); if (value is not PyTuple tuple) throw Invalid("persistent ID is not a tuple"); _stack.Add(StorageFrom(tuple)); break; }
                case 0x52: Reduce(); break;
                case 0x62: { var state = Pop(); _ = state; break; }
                case 0x73: { var value = Pop(); var key = Pop(); SetItem(key, value); break; }
                case 0x75: SetItems(PopToMark()); break;
                case 0x61: { var value = Pop(); if (Top() is not List<object> list) throw Invalid("APPEND target is not a list"); list.Add(value); break; }
                case 0x65: { var values = PopToMark(); if (Top() is not List<object> list) throw Invalid("APPENDS target is not a list"); list.AddRange(values); break; }
                case 0x95: _position = checked(_position + 8); break;
                default: throw Invalid($"unsupported pickle opcode 0x{opcode:X2} at {_position - 1}");
            }
            if (result is not null) break;
        }
        if (result is not Dictionary<object, object> dictionary) throw Invalid("PyTorch root is not a state dictionary");
        var output = new Dictionary<string, TensorRef>(StringComparer.Ordinal);
        foreach (var item in dictionary)
        {
            if (item.Key is not PyString name || item.Value is not PyTensor tensor) throw Invalid("state dictionary contains an unsupported value");
            output.Add(name.Value, new TensorRef(name.Value, tensor.StorageKey, tensor.Offset, tensor.Numel, tensor.Shape, tensor.Stride));
        }
        return output;
    }

    private void Reduce()
    {
        var args = Pop(); var callable = Pop();
        if (callable is not PyGlobal global || args is not PyTuple tuple) throw Invalid("unsupported REDUCE value");
        if (global.Module == "torch._utils" && global.Name == "_rebuild_tensor_v2")
        {
            if (tuple.Values.Length < 4 || tuple.Values[0] is not PyStorage storage || tuple.Values[1] is not PyInt offset || tuple.Values[2] is not PyTuple shape || tuple.Values[3] is not PyTuple stride)
                throw Invalid("malformed _rebuild_tensor_v2 arguments");
            var shapeValues = shape.Values.Select(ToInt).ToArray(); var strideValues = stride.Values.Select(ToInt).ToArray();
            if (shapeValues.Length == 0 || shapeValues.Length != strideValues.Length) throw Invalid("tensor shape/stride is invalid");
            _stack.Add(new PyTensor(storage.Key, offset.Value, shapeValues, strideValues, shapeValues.Aggregate(1L, (current, next) => checked(current * next)))); return;
        }
        if (global.Module == "collections" && global.Name == "OrderedDict") { _stack.Add(new Dictionary<object, object>()); return; }
        throw Invalid($"unsupported REDUCE global {global.Module}.{global.Name}");
    }

    private void SetItems(List<object> values)
    {
        if (Top() is not Dictionary<object, object> dictionary || values.Count % 2 != 0) throw Invalid("SETITEMS target is invalid");
        for (var index = 0; index < values.Count; index += 2) dictionary[values[index]] = values[index + 1];
    }
    private void SetItem(object key, object value) { if (Top() is not Dictionary<object, object> dictionary) throw Invalid("SETITEM target is invalid"); dictionary[key] = value; }
    private PyStorage StorageFrom(PyTuple tuple)
    {
        if (tuple.Values.Length != 5 || tuple.Values[0] is not PyString kind || kind.Value != "storage" || tuple.Values[2] is not PyString key || tuple.Values[3] is not PyString device || device.Value != "cpu" || tuple.Values[4] is not PyInt count)
            throw Invalid("unsupported persistent storage descriptor");
        if (tuple.Values[1] is not PyGlobal type || type.Module != "torch" || type.Name != "FloatStorage") throw Invalid("only FloatStorage is supported");
        return new PyStorage(key.Value, count.Value);
    }
    private List<object> PopToMark() { var values = new List<object>(); while (_stack.Count > 0) { var value = Pop(); if (value is Mark) { values.Reverse(); return values; } values.Add(value); } throw Invalid("pickle MARK is unbalanced"); }
    private void PushTuple(object[] values) => _stack.Add(new PyTuple(values));
    private object Pop() { if (_stack.Count == 0) throw Invalid("pickle stack underflow"); var index = _stack.Count - 1; var value = _stack[index]; _stack.RemoveAt(index); return value; }
    private object Top() => _stack.Count == 0 ? throw Invalid("pickle stack is empty") : _stack[^1];
    private int ReadByte() => _position < _bytes.Length ? _bytes[_position++] : throw Invalid("pickle ended unexpectedly");
    private uint ReadUInt32() { if (_position + 4 > _bytes.Length) throw Invalid("pickle ended unexpectedly"); var value = BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(_position, 4)); _position += 4; return value; }
    private ushort ReadUInt16() { if (_position + 2 > _bytes.Length) throw Invalid("pickle ended unexpectedly"); var value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(_position, 2)); _position += 2; return value; }
    private int ReadInt32() => unchecked((int)ReadUInt32());
    private long ReadLong1() { var length = ReadByte(); if (length > 8 || _position + length > _bytes.Length) throw Invalid("pickle LONG1 is outside the bound"); long value = 0; for (var index = 0; index < length; index++) value |= (long)_bytes[_position++] << (8 * index); return value; }
    private string ReadUtf8(int length) { if (length < 0 || length > 1_000_000 || _position + length > _bytes.Length) throw Invalid("pickle string is outside the bound"); var value = Encoding.UTF8.GetString(_bytes, _position, length); _position += length; return value; }
    private string ReadLine() { var start = _position; while (_position < _bytes.Length && _bytes[_position++] != (byte)'\n') { } if (_position > _bytes.Length) throw Invalid("pickle global line ended unexpectedly"); return Encoding.ASCII.GetString(_bytes, start, _position - start - 1); }
    private static long ToInt(object value) => value is PyInt integer && integer.Value > 0 && integer.Value <= int.MaxValue ? integer.Value : throw new InvalidDataException("pickle shape value is invalid");
    private static InvalidDataException Invalid(string message) => new(message);
}
