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
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(30));
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) => { eventArgs.Cancel = true; lifetime.Cancel(); };
Console.CancelKeyPress += cancelHandler;
try
{
    if (command == "check-development-metrics")
    {
        CheckDevelopmentMetrics(lifetime.Token);
        return 0;
    }
    if (command == "convert-pytorch")
    {
        var input = RequiredOption("--input"); var output = RequiredOption("--output");
        var timeout = BoundedOption("--timeout-seconds", 1, 1800, 900);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(timeout));
        var result = ConvertPyTorch(Path.GetFullPath(input), Path.GetFullPath(output), cancellation.Token);
        Console.WriteLine($"converted {result.TensorCount} tensors, bytes={result.Bytes:N0}, sha256={result.Sha256}");
        return 0;
    }
    if (command == "smoke")
    {
        var package = Path.GetFullPath(RequiredOption("--package"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromMinutes(10));
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
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromMinutes(10));
        TrainOriginal(package, recordsPath, headPath, reportPath, cancellation.Token);
        return 0;
    }
    if (command == "evaluate-original")
    {
        var package = Path.GetFullPath(RequiredOption("--package"));
        var recordsPath = Path.GetFullPath(RequiredOption("--records"));
        var headPath = Path.GetFullPath(RequiredOption("--head"));
        var trainingReportPath = Path.GetFullPath(RequiredOption("--training-report"));
        var reportPath = Path.GetFullPath(RequiredOption("--report"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromMinutes(10));
        EvaluateOriginal(package, recordsPath, headPath, trainingReportPath, reportPath, cancellation.Token);
        return 0;
    }
    Console.WriteLine("Sezika.IndependentModelTool convert-pytorch --input <pytorch_model.bin> --output <model.safetensors> [--timeout-seconds 1..1800]");
    Console.WriteLine("Sezika.IndependentModelTool smoke --package <independent-model-package>");
    Console.WriteLine("Sezika.IndependentModelTool train-original --package <package> --records <records.jsonl> --head <head.asset> --report <report.json>");
    Console.WriteLine("Sezika.IndependentModelTool evaluate-original --package <package> --records <records.jsonl> --head <head.asset> --training-report <training-report.json> --report <evaluation-report.json>");
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("independent-model-tool: operation cancelled or exceeded its bounded timeout");
    return 124;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"independent-model-tool: {exception.Message}");
    return 1;
}
finally { Console.CancelKeyPress -= cancelHandler; }

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

static void EvaluateOriginal(string packagePath, string recordsPath, string headPath, string trainingReportPath,
    string reportPath, CancellationToken cancellationToken)
{
    var records = ReadOriginalRecords(recordsPath, cancellationToken);
    var development = records.Where(item => item.Split == "development").ToArray();
    if (development.Length is < 1 or > 32) throw new InvalidDataException("Development evaluation requires 1-32 records.");
    var recordsHash = HashFile(recordsPath, cancellationToken);
    var manifestPath = Path.Combine(Path.GetDirectoryName(recordsPath)!, "manifest.json");
    if (!File.Exists(manifestPath)) throw new FileNotFoundException("Original dataset manifest is missing.", manifestPath);
    var dataHash = HashFile(manifestPath, cancellationToken);
    var training = ReadTrainingReport(trainingReportPath, cancellationToken);
    if (!training.RecordsSha256.Equals(recordsHash, StringComparison.OrdinalIgnoreCase) ||
        !training.DataManifestSha256.Equals(dataHash, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Evaluation records or manifest do not match the training report.");

    using var model = IndependentModelLoader.Load(packagePath,
        new EncoderExecutionOptions { Kernel = EncoderKernelMode.Scalar, Deadline = TimeSpan.FromMinutes(10) }, cancellationToken);
    if (model.ModelId != training.ModelId || !model.EncoderSha256.Equals(training.EncoderSha256, StringComparison.OrdinalIgnoreCase) ||
        !model.TokenizerSha256.Equals(training.TokenizerSha256, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Evaluation model identity does not match the training report.");
    var identity = new MarkerFeatureIdentity(model.ModelId, model.EncoderSha256, model.TokenizerSha256,
        dataHash, model.Encoder.Config.HiddenSize);
    var head = IndependentMarkerHeadAssetStore.Load(headPath, identity, training.FeatureSha256, training.HeadSha256, cancellationToken);
    var rows = new List<OriginalEvaluationRow>(development.Length);
    for (var index = 0; index < development.Length; index++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = development[index];
        Console.WriteLine($"evaluate-original: {index + 1}/{development.Length} {record.RecordId}");
        var candidateIds = CandidateIds(record.Question);
        try
        {
            var feature = IndependentMarkerFeatureExporter.Export(model.Tokenizer, model.Encoder, identity, record.Request,
                record.Question, record.RecordId, record.Language, record.TargetIndex, cancellationToken: cancellationToken);
            var logits = head.Head.Score(feature.Type, feature.Features, cancellationToken.ThrowIfCancellationRequested);
            var probabilities = Softmax(logits);
            var predicted = ArgMax(probabilities);
            var top = probabilities[predicted];
            var second = probabilities.Where((_, candidate) => candidate != predicted).DefaultIfEmpty(0).Max();
            rows.Add(new OriginalEvaluationRow(record.RecordId, record.Language, QuestionTypeName(feature.Type), candidateIds, feature.TargetIndex,
                probabilities.Length, "answered", predicted, predicted == feature.TargetIndex, probabilities[feature.TargetIndex], top,
                top - second, -Math.Log(Math.Max(probabilities[feature.TargetIndex], double.Epsilon)),
                probabilities.Select((probability, candidate) => Math.Pow(probability - (candidate == feature.TargetIndex ? 1d : 0d), 2)).Sum(),
                probabilities, null));
        }
        catch (DecisionException exception)
        {
            rows.Add(new OriginalEvaluationRow(record.RecordId, record.Language, IndependentQuestionTypeName(record.Question), candidateIds, record.TargetIndex,
                0, "rejected", null, null, null, null, null, null, null, [], exception.Code));
        }
    }
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    using var stream = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
    writer.WriteStartObject();
    writer.WriteNumber("schema_version", 1);
    writer.WriteString("status", "development_diagnostic_only");
    writer.WriteString("evaluation_use", "independent_development_diagnostics_not_training_calibration_or_sealed_test");
    writer.WriteString("model_id", model.ModelId); writer.WriteString("encoder_sha256", model.EncoderSha256);
    writer.WriteString("tokenizer_sha256", model.TokenizerSha256); writer.WriteString("data_manifest_sha256", dataHash);
    writer.WriteString("records_sha256", recordsHash); writer.WriteString("training_report_sha256", HashFile(trainingReportPath, cancellationToken));
    writer.WriteString("feature_sha256", training.FeatureSha256); writer.WriteString("head_sha256", training.HeadSha256);
    writer.WriteString("split", "development"); writer.WriteNumber("total", rows.Count);
    writer.WriteNumber("answered", rows.Count(row => row.Status == "answered"));
    writer.WriteNumber("rejected", rows.Count(row => row.Status == "rejected"));
    writer.WriteNumber("coverage", Coverage(rows));
    // Only dimensions represented by the input contract are exposed here.  The
    // remaining S4-06 slices require reviewed labels; inferring them from text
    // or candidate wording would turn a diagnostic into an undocumented labeler.
    writer.WritePropertyName("diagnostic_dimensions"); writer.WriteStartObject();
    writer.WritePropertyName("available"); writer.WriteStartArray();
    writer.WriteStringValue("language"); writer.WriteStringValue("question_type"); writer.WriteStringValue("candidate_order");
    writer.WriteEndArray();
    writer.WritePropertyName("grouped"); writer.WriteStartArray();
    writer.WriteStringValue("language"); writer.WriteStringValue("question_type");
    writer.WriteEndArray();
    writer.WritePropertyName("unavailable"); writer.WriteStartArray();
    writer.WriteStringValue("role"); writer.WriteStringValue("negation"); writer.WriteStringValue("lexical_similarity");
    writer.WriteEndArray();
    writer.WriteString("policy", "unavailable_dimensions_are_null_until_versioned_reviewed_labels_are_present");
    writer.WriteEndObject();
    writer.WritePropertyName("metrics"); WriteMetrics(writer, rows);
    writer.WritePropertyName("by_language_and_type"); writer.WriteStartArray();
    foreach (var group in rows.GroupBy(row => (row.Language, row.QuestionType)).OrderBy(group => group.Key.Language).ThenBy(group => group.Key.QuestionType))
    {
        writer.WriteStartObject(); writer.WriteString("language", group.Key.Language); writer.WriteString("question_type", group.Key.QuestionType);
        writer.WritePropertyName("metrics"); WriteMetrics(writer, group.ToArray()); writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WritePropertyName("rows"); writer.WriteStartArray();
    foreach (var row in rows)
    {
        writer.WriteStartObject(); writer.WriteString("record_id", row.RecordId); writer.WriteString("language", row.Language);
        writer.WriteString("question_type", row.QuestionType); writer.WriteNumber("target_index", row.TargetIndex);
        writer.WriteNumber("candidate_count", row.CandidateCount); writer.WriteString("status", row.Status);
        writer.WritePropertyName("candidate_ids"); writer.WriteStartArray();
        foreach (var candidateId in row.CandidateIds) writer.WriteStringValue(candidateId);
        writer.WriteEndArray();
        if (row.PredictedIndex is int predicted) writer.WriteNumber("predicted_index", predicted);
        if (row.Correct is bool correct) writer.WriteBoolean("correct", correct);
        WriteNullable(writer, "target_probability", row.TargetProbability); WriteNullable(writer, "top_probability", row.TopProbability);
        WriteNullable(writer, "margin", row.Margin); WriteNullable(writer, "nll", row.Nll); WriteNullable(writer, "brier", row.Brier);
        if (row.Status == "answered")
        {
            writer.WritePropertyName("probabilities"); writer.WriteStartArray();
            foreach (var probability in row.Probabilities) writer.WriteNumberValue(probability);
            writer.WriteEndArray();
        }
        if (row.ErrorCode is not null) writer.WriteString("error_code", row.ErrorCode);
        writer.WriteEndObject();
    }
    writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
    Console.WriteLine($"evaluated development records={rows.Count} answered={rows.Count(row => row.Status == "answered")} coverage={Coverage(rows):P2} report={reportPath}");
}

static double[] Softmax(double[] logits)
{
    if (logits.Length is < 2 or > 32 || logits.Any(value => !double.IsFinite(value)))
        throw new InvalidDataException("Head logits are non-finite or outside the candidate bound.");
    var max = logits.Max(); var probabilities = new double[logits.Length]; double sum = 0;
    for (var index = 0; index < logits.Length; index++) sum += probabilities[index] = Math.Exp(logits[index] - max);
    if (!double.IsFinite(sum) || sum <= 0) throw new InvalidDataException("Head logits produced an invalid probability normalizer.");
    for (var index = 0; index < probabilities.Length; index++) probabilities[index] /= sum;
    return probabilities;
}

static double Coverage(IReadOnlyList<OriginalEvaluationRow> rows) => rows.Count == 0 ? 0 : rows.Count(row => row.Status == "answered") / (double)rows.Count;

static void WriteMetrics(Utf8JsonWriter writer, IReadOnlyList<OriginalEvaluationRow> rows)
{
    var answered = rows.Where(row => row.Status == "answered").ToArray();
    writer.WriteStartObject(); writer.WriteNumber("total", rows.Count); writer.WriteNumber("answered", answered.Length);
    writer.WriteNumber("rejected", rows.Count - answered.Length); writer.WriteNumber("coverage", Coverage(rows));
    writer.WriteNumber("correct", answered.Count(row => row.Correct == true));
    WriteNullable(writer, "accuracy_on_all", rows.Count == 0 ? null : answered.Count(row => row.Correct == true) / (double)rows.Count);
    WriteNullable(writer, "accuracy_on_answered", answered.Length == 0 ? null : answered.Count(row => row.Correct == true) / (double)answered.Length);
    writer.WritePropertyName("accuracy_ci95"); WriteConfidenceInterval(writer, answered);
    WriteNullable(writer, "mean_nll", answered.Length == 0 ? null : answered.Average(row => row.Nll!.Value));
    WriteNullable(writer, "mean_brier", answered.Length == 0 ? null : answered.Average(row => row.Brier!.Value));
    WriteNullable(writer, "mean_margin", answered.Length == 0 ? null : answered.Average(row => row.Margin!.Value));
    WriteNullable(writer, "ece_10_bins", ExpectedCalibrationError(answered));
    WriteNullable(writer, "auroc_macro_ovr", MacroAuroc(answered));
    writer.WriteString("auroc_policy", "only_homogeneous_boolean_or_score_with_identical_candidate_mapping_choice_unavailable");
    var score = answered.Where(row => row.QuestionType == "score").ToArray();
    writer.WriteNumber("score_answered", score.Length);
    WriteNullable(writer, "score_mae_on_answered", score.Length == 0 ? null : score.Average(ScoreError));
    var negative = rows.Where(row => row.QuestionType == "boolean" && row.TargetIndex == 0).ToArray();
    writer.WriteNumber("boolean_negative_total", negative.Length);
    WriteNullable(writer, "boolean_negative_recall_on_all", negative.Length == 0 ? null :
        negative.Count(row => row.Status == "answered" && row.PredictedIndex == 0) / (double)negative.Length);
    writer.WriteEndObject();
}

static void WriteConfidenceInterval(Utf8JsonWriter writer, IReadOnlyList<OriginalEvaluationRow> rows)
{
    if (rows.Count == 0) { writer.WriteNullValue(); return; }
    const double z = 1.959963984540054;
    var successes = rows.Count(row => row.Correct == true); var n = rows.Count; var proportion = successes / (double)n;
    var denominator = 1 + z * z / n; var centre = (proportion + z * z / (2 * n)) / denominator;
    var margin = z * Math.Sqrt(proportion * (1 - proportion) / n + z * z / (4 * n * n)) / denominator;
    writer.WriteStartObject(); writer.WriteNumber("lower", Math.Max(0, centre - margin)); writer.WriteNumber("upper", Math.Min(1, centre + margin)); writer.WriteEndObject();
}

static double? ExpectedCalibrationError(IReadOnlyList<OriginalEvaluationRow> rows)
{
    if (rows.Count == 0) return null; double total = 0;
    for (var bin = 0; bin < 10; bin++)
    {
        var lower = bin / 10d; var upper = (bin + 1) / 10d;
        var selected = rows.Where(row => row.TopProbability!.Value >= lower && (bin == 9 ? row.TopProbability.Value <= upper : row.TopProbability.Value < upper)).ToArray();
        if (selected.Length == 0) continue;
        total += selected.Length / (double)rows.Count * Math.Abs(selected.Average(row => row.TopProbability!.Value) - selected.Count(row => row.Correct == true) / (double)selected.Length);
    }
    return total;
}

static double? MacroAuroc(IReadOnlyList<OriginalEvaluationRow> rows)
{
    // Per-question Choice IDs are not a reviewed common class vocabulary.
    // Candidate positions across primitives likewise have different meanings.
    if (rows.Count == 0 || rows[0].QuestionType is not ("boolean" or "score") ||
        rows.Any(row => row.QuestionType != rows[0].QuestionType || row.CandidateCount != rows[0].CandidateCount ||
            !row.CandidateIds.SequenceEqual(rows[0].CandidateIds, StringComparer.Ordinal))) return null;
    var values = new List<double>();
    for (var candidate = 0; candidate < rows[0].CandidateCount; candidate++)
    {
        var positives = rows.Where(row => row.TargetIndex == candidate).ToArray(); var negatives = rows.Where(row => row.TargetIndex != candidate).ToArray();
        if (positives.Length == 0 || negatives.Length == 0) return null;
        var wins = 0d;
        foreach (var positive in positives) foreach (var negative in negatives)
            wins += positive.Probabilities[candidate] > negative.Probabilities[candidate] ? 1 : positive.Probabilities[candidate] == negative.Probabilities[candidate] ? 0.5 : 0;
        values.Add(wins / (positives.Length * (double)negatives.Length));
    }
    return values.Count == 0 ? null : values.Average();
}

static double ScoreError(OriginalEvaluationRow row) => Math.Abs(row.Probabilities.Select((p, index) => p * index).Sum() - row.TargetIndex);

static void CheckDevelopmentMetrics(CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    OriginalEvaluationRow Answer(string id, string type, int target, int predicted, double[] p) =>
        new(id, "en", type, type == "boolean" ? ["false", "true"] : ["0", "1"], target, 2,
            "answered", predicted, target == predicted, p[target], p.Max(), Math.Abs(p[0] - p[1]),
            -Math.Log(p[target]), p.Select((value, index) => Math.Pow(value - (target == index ? 1d : 0d), 2)).Sum(), p, null);
    var negative = Answer("negative", "boolean", 0, 0, [0.8, 0.2]);
    var positive = Answer("positive", "boolean", 1, 1, [0.1, 0.9]);
    var score = Answer("score", "score", 1, 0, [0.75, 0.25]);
    var rejection = new OriginalEvaluationRow("rejected", "en", "boolean", ["false", "true"], 0, 2,
        "rejected", null, null, null, null, null, null, null, [], "synthetic_rejection");
    if (MacroAuroc([negative, positive]) != 1 || MacroAuroc([negative, score]) is not null ||
        MacroAuroc([negative with { QuestionType = "choice" }, positive with { QuestionType = "choice" }]) is not null ||
        MacroAuroc([negative, positive with { CandidateIds = ["true", "false"] }]) is not null || ScoreError(score) != 0.75)
        throw new InvalidOperationException("Development metric mapping check failed.");
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream)) WriteMetrics(writer, [negative, positive, score, rejection]);
    using var document = JsonDocument.Parse(stream.ToArray());
    var json = document.RootElement;
    if (json.GetProperty("coverage").GetDouble() != 0.75 || json.GetProperty("accuracy_on_all").GetDouble() != 0.5 ||
        json.GetProperty("boolean_negative_total").GetInt32() != 2 || json.GetProperty("boolean_negative_recall_on_all").GetDouble() != 0.5 ||
        json.GetProperty("score_mae_on_answered").GetDouble() != 0.75 || json.GetProperty("auroc_macro_ovr").ValueKind != JsonValueKind.Null)
        throw new InvalidOperationException("Development metric denominator check failed.");
    Console.WriteLine("PASS: synthetic development metric mapping and denominator checks; no model inference or quality claim");
}

static string IndependentQuestionTypeName(IndependentDecisionQuestion question) => question switch
{
    IndependentChoiceQuestion => "choice",
    IndependentScoreQuestion => "score",
    IndependentBooleanQuestion => "boolean",
    _ => throw new InvalidDataException("Unknown independent question type."),
};

static string[] CandidateIds(IndependentDecisionQuestion question) => question switch
{
    IndependentChoiceQuestion choice => choice.Candidates.Select(candidate => candidate.Id).ToArray(),
    IndependentScoreQuestion score => Enumerable.Range(0, score.Levels.Count).Select(index => index.ToString()).ToArray(),
    IndependentBooleanQuestion => ["false", "true"],
    _ => [],
};

static void WriteNullable(Utf8JsonWriter writer, string name, double? value)
{
    writer.WritePropertyName(name); if (value is double number && double.IsFinite(number)) writer.WriteNumberValue(number); else writer.WriteNullValue();
}

static TrainingReport ReadTrainingReport(string path, CancellationToken cancellationToken)
{
    if (!File.Exists(path)) throw new FileNotFoundException("Training report is missing.", path);
    var info = new FileInfo(path);
    if (info.Length is <= 0 or > 1_048_576) throw new InvalidDataException("Training report exceeds the 1 MiB bound.");
    using var document = JsonDocument.Parse(File.ReadAllBytes(path)); cancellationToken.ThrowIfCancellationRequested();
    var root = document.RootElement;
    var status = root.GetProperty("status").GetString();
    if (status != "development_smoke_only") throw new InvalidDataException("Training report is not a development smoke report.");
    return new TrainingReport(root.GetProperty("model_id").GetString()!, root.GetProperty("encoder_sha256").GetString()!,
        root.GetProperty("tokenizer_sha256").GetString()!, root.GetProperty("data_manifest_sha256").GetString()!,
        root.GetProperty("records_sha256").GetString()!, root.GetProperty("feature_sha256").GetString()!, root.GetProperty("head_sha256").GetString()!);
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
        using var document = ParseRecordJson(line); var json = document.RootElement;
        var recordId = RequiredRecordText(json, "record_id", 128); var split = RequiredRecordText(json, "split", 16); var language = RequiredRecordText(json, "language", 2); var type = RequiredRecordText(json, "type", 16);
        if (!seen.Add(recordId) || split is not ("train" or "development") || language is not ("zh" or "en")) throw new InvalidDataException($"Original record {recordId} identity or split is invalid.");
        var instruction = RequiredRecordText(json, "instruction", 65_536); var state = json.GetProperty("state").Clone();
        if (state.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) throw new InvalidDataException($"Original record {recordId} state is null.");
        var target = json.GetProperty("target_index").GetInt32(); IndependentDecisionQuestion question;
        switch (type)
        {
            case "choice":
                var candidates = json.GetProperty("candidates");
                if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() is < 2 or > 32) throw new InvalidDataException($"Original record {recordId} candidate count is outside 2-32.");
                question = new IndependentChoiceQuestion(recordId, instruction, candidates.EnumerateArray().Select(value => new IndependentChoiceCandidate(RequiredRecordText(value, "id", 65_536), RequiredRecordText(value, "text", 65_536))).ToArray()); break;
            case "score":
                var levels = json.GetProperty("levels");
                if (levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() is < 2 or > 10) throw new InvalidDataException($"Original record {recordId} score level count is outside 2-10.");
                question = new IndependentScoreQuestion(recordId, instruction, levels.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new InvalidDataException($"Original record {recordId} has an invalid score level.")).ToArray()); break;
            case "boolean": question = new IndependentBooleanQuestion(recordId, instruction, RequiredRecordText(json, "statement", 65_536), RequiredRecordText(json, "when_false", 65_536), RequiredRecordText(json, "when_true", 65_536)); break;
            default: throw new InvalidDataException($"Original record {recordId} question type is unsupported.");
        }
        output.Add(new OriginalRecord(recordId, split, language, target, new IndependentDecisionRequest(1, IndependentModelLoader.ModelId, state, [question]), question));
    }
    return output;
}

static string RequiredRecordText(JsonElement value, string property, int maxLength)
{
    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(element.GetString()) || element.GetString()!.Length > maxLength)
        throw new InvalidDataException($"Original record property '{property}' must be a bounded non-empty string.");
    return element.GetString()!;
}

static JsonDocument ParseRecordJson(string line)
{
    var bytes = Encoding.UTF8.GetBytes(line);
    var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
    var scopes = new Stack<HashSet<string>>();
    try
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) scopes.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (scopes.Count == 0) throw new InvalidDataException("Record JSON object scope is unbalanced.");
                scopes.Pop();
            }
            else if (reader.TokenType == JsonTokenType.PropertyName && (scopes.Count == 0 || !scopes.Peek().Add(reader.GetString()!)))
                throw new InvalidDataException("Record JSON contains a duplicate property.");
            if (reader.CurrentDepth > 32) throw new InvalidDataException("Record JSON nesting exceeds the 32-level bound.");
        }
        if (scopes.Count != 0) throw new InvalidDataException("Record JSON is incomplete.");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
    }
    catch (JsonException exception)
    {
        throw new InvalidDataException("Record JSON is invalid.", exception);
    }
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

sealed record TrainingReport(string ModelId, string EncoderSha256, string TokenizerSha256, string DataManifestSha256,
    string RecordsSha256, string FeatureSha256, string HeadSha256);

sealed record OriginalEvaluationRow(string RecordId, string Language, string QuestionType, string[] CandidateIds, int TargetIndex, int CandidateCount,
    string Status, int? PredictedIndex, bool? Correct, double? TargetProbability, double? TopProbability, double? Margin,
    double? Nll, double? Brier, double[] Probabilities, string? ErrorCode);
