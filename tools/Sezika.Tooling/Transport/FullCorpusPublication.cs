using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sezika.Tooling;

internal static partial class AssetTransport
{
    private const string CorpusSchedule = "source_round_robin_v1;source_order=sha256(seed,epoch,source);within_source=manifest_shard_row_order;train_only;no_oversampling";

    private static JsonElement Document(string path, Budget budget)
    {
        RegularFiles.Require(path, budget); using var file = File.OpenRead(path);
        if (file.Length > MaximumJsonBytes) throw new InvalidDataException("Publication metadata byte cap exceeded.");
        using var document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = 32 }); ValidateJson(document.RootElement, budget);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Publication JSON object required.");
        return document.RootElement.Clone();
    }
    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() ?? throw new InvalidDataException("Missing text field: " + key);
    private static long Number(JsonElement value, string key) => value.GetProperty(key).GetInt64();
    private static bool Flag(JsonElement value, string key) => value.GetProperty(key).GetBoolean();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static bool Same(JsonElement left, JsonElement right) => JsonElement.DeepEquals(left, right);
    private static bool EqualSha(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static string TextHash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void BoundArray(JsonElement value, int minimum, int maximum, string label) =>
        Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() >= minimum && value.GetArrayLength() <= maximum, label + " count differs.");

    internal static LogicalManifest PreparePublication(string packageRoot, string validationPath, string contractPath, string outputRoot,
        string toolRoot, string trainingProof, string validationProof, Budget budget)
    {
        budget.Check(); var root = Path.GetFullPath(packageRoot); SafePaths.NoLinks(root, budget);
        var contract = Document(contractPath, budget);
        Require(Number(contract, "schema_version") == 1 && Flag(contract, "ready"), "Frozen READY publication contract required.");
        var testOnly = Flag(contract, "test_only"); var runId = Text(contract, "run_id");
        Require(runId.Length is >= 3 and <= 64 && char.IsAsciiLetterOrDigit(runId[0]) && runId.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'), "Invalid run identity.");
        string PathFor(string name) => AssetPath(root, name, budget);
        var package = Document(PathFor("corpus-package.json"), budget); var packageSha = FileHash(PathFor("corpus-package.json"), budget);
        var plan = Document(PathFor("run-plan.json"), budget); var report = Document(PathFor("training-report.json"), budget); var validation = Document(validationPath, budget);
        var identity = package.GetProperty("Inputs"); var config = package.GetProperty("Config");
        var updates = checked(Number(contract, "train_records") * Number(contract, "epochs"));
        Require(updates is >= 2 and <= 800000000, "Global update denominator outside bound.");
        foreach (var value in new[] { plan, package, report, validation }) { budget.Check(); Require(Text(value, "RunId") == runId, "Run evidence identity differs."); }
        Require(Number(plan, "SchemaVersion") == 1 && Number(package, "SchemaVersion") == 1 && Text(package, "Method") == "full_encoder_adamw_fp32_streamed_corpus" &&
            Flag(plan, "ExperimentalOnly") && Number(plan, "TrainRecords") == Number(contract, "train_records") && Number(plan, "Epochs") == Number(contract, "epochs"), "Frozen global plan/method differs.");
        var settings = contract.GetProperty("plan_settings");
        var expectedSettings = new HashSet<string>(["Seed", "MaximumTokens", "CheckpointEvery", "ValidationPerSource", "EncoderRate", "HeadRate", "WeightDecay", "MaximumOwnedBytes"], StringComparer.Ordinal);
        Require(settings.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expectedSettings), "Exact frozen optimizer/schedule settings required.");
        foreach (var setting in settings.EnumerateObject()) { budget.Check(); Require(Same(plan.GetProperty(setting.Name), setting.Value), "Optimizer or schedule changed."); }
        Require(Same(config, contract.GetProperty("model_config")), "Frozen model config changed.");
        foreach (var binding in new[] { ("SourceCommit", "source_commit"), ("DatasetCommit", "original_dataset_commit"), ("ModelCommit", "model_commit") })
        {
            budget.Check(); var commit = Text(contract, binding.Item2); ValidateCommit(commit);
            Require(Text(plan, binding.Item1) == commit && Text(package, binding.Item1) == commit, "Input commit binding differs.");
        }
        ValidateCommit(Text(contract, "published_dataset_commit"));
        foreach (var binding in new[] { ("CorpusManifestSha256", "corpus_manifest_sha256"), ("CorpusBindingsSha256", "corpus_bindings_sha256") })
        {
            budget.Check(); var sha = Text(contract, binding.Item2); ValidateSha(sha.ToLowerInvariant());
            Require(EqualSha(Text(plan, binding.Item1), sha) && EqualSha(Text(package, binding.Item1), sha), "Corpus frozen SHA binding differs.");
        }
        Require(EqualSha(FileHash(PathFor("run-plan.json"), budget), Text(identity, "PlanSha256")) && EqualSha(Text(identity, "PlanSha256"), Text(contract, "plan_sha256")), "Immutable run plan bytes changed.");
        Require(identity.EnumerateObject().Count() == 7, "Seven-field input identity required.");
        foreach (var property in identity.EnumerateObject()) { budget.Check(); ValidateSha((property.Value.GetString() ?? "").ToLowerInvariant()); }
        Require(EqualSha(Text(identity, "DataManifestSha256"), Text(package, "CorpusManifestSha256")) && EqualSha(Text(identity, "BaseEncoderSha256"), Text(contract, "base_encoder_sha256")) &&
            EqualSha(Text(identity, "TokenizerSha256"), Text(package, "TokenizerSha256")) && EqualSha(Text(identity, "TokenizerSha256"), Text(contract, "tokenizer_sha256")) &&
            EqualSha(Text(identity, "BackwardManifestSha256"), Text(contract, "backward_manifest_sha256")) && Same(report.GetProperty("Inputs"), identity) && Same(validation.GetProperty("Inputs"), identity), "Train/validation/base/tokenizer/PTX input identity differs.");
        Require(Number(package, "CompletedUpdates") == updates && Number(report, "CompletedUpdates") == updates && Number(report, "GlobalUpdates") == updates &&
            Text(report, "Status") == "PASS_all_requested_corpus_epochs_GPU_saved_only" && Flag(report, "FullCorpusEpochCoverage") && Number(report, "CompletedEpochs") == Number(plan, "Epochs") &&
            Number(report, "WithinEpoch") == 0 && checked(Number(report, "InitialUpdates") + Number(report, "StageRequestedUpdates")) == updates && Number(report, "StageRequestedUpdates") > 0,
            "PARTIAL or incomplete global/source EOF evidence.");
        Require(EqualSha(FileHash(PathFor("training-report.json"), budget), Text(package, "TrainingReportSha256")) && EqualSha(Text(report, "CheckpointReceiptSha256"), Text(package, "CheckpointReceiptSha256")), "Training-report binding differs.");
        Require(!Flag(package, "QualityAccepted") && !Flag(package, "AotAccepted") && Text(package, "CalibrationStatus") == "uncalibrated", "Package experimental acceptance boundary differs.");
        foreach (var value in new[] { report, validation })
        {
            budget.Check(); Require(!Flag(value, "QualityAccepted") && !Flag(value, "Calibrated") && Number(value, "ProcessId") > 0 &&
                DateTimeOffset.TryParse(Text(value, "ProcessStartUtc"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _) &&
                Text(value, "GpuUuid").Length == 32 && Text(value, "GpuUuid").All(char.IsAsciiHexDigit), "Real process/GPU identity or acceptance boundary differs.");
        }
        Require(Text(validation, "Status") == "PASS_new_process_saved_corpus_GPU_forward_validation_only" && EqualSha(Text(validation, "PackageSha256"), packageSha) &&
            Number(report, "ProcessId") != Number(validation, "ProcessId") && Text(report, "ProcessStartUtc") != Text(validation, "ProcessStartUtc") &&
            Text(report, "GpuUuid") == Text(contract, "training_gpu_uuid") && Text(validation, "GpuUuid") == Text(contract, "validation_gpu_uuid"), "Independent new-process validation binding differs.");
        Require(new FileInfo(PathFor("resources-reclaimed.txt")).Length <= 256 && File.ReadAllText(PathFor("resources-reclaimed.txt")).Trim() == "PASS_owned_buffers_modules_restored_and_context_disposed", "CUDA reclamation receipt differs.");
        var cpName = "checkpoint-" + updates.ToString("D10", System.Globalization.CultureInfo.InvariantCulture); var cpRoot = PathFor(cpName);
        Require(Inventory(cpRoot, budget).Keys.ToHashSet(StringComparer.Ordinal).SetEquals(["weights.safetensors", "moments.safetensors", "checkpoint.json", "corpus-cursor.json"]), "Exact four-file final checkpoint required.");
        var checkpoint = Document(PathFor(cpName + "/checkpoint.json"), budget); var cursor = Document(PathFor(cpName + "/corpus-cursor.json"), budget);
        Require(EqualSha(FileHash(PathFor(cpName + "/checkpoint.json"), budget), Text(package, "CheckpointReceiptSha256")) && EqualSha(FileHash(PathFor(cpName + "/corpus-cursor.json"), budget), Text(report, "CursorReceiptSha256")), "Final checkpoint/cursor bytes changed.");
        foreach (var value in new[] { checkpoint, cursor })
        { budget.Check(); Require(Number(value, "SchemaVersion") == 1 && Text(value, "RunId") == runId && Same(value.GetProperty("Inputs"), identity) && Number(value, "CompletedUpdates") == updates, "Persisted checkpoint/global cursor identity differs."); }
        Require(Same(checkpoint.GetProperty("Config"), config) && EqualSha(Text(checkpoint, "WeightsSha256"), Text(package, "WeightsSha256")) &&
            Number(cursor, "Epoch") == Number(plan, "Epochs") && Number(cursor, "WithinEpoch") == 0 && Number(cursor, "TrainingRecordsPerEpoch") == Number(plan, "TrainRecords") &&
            Number(cursor, "PlannedEpochs") == Number(plan, "Epochs") && EqualSha(Text(cursor, "CheckpointReceiptSha256"), Text(package, "CheckpointReceiptSha256")) && Text(cursor, "ScheduleAlgorithm") == CorpusSchedule,
            "Complete persisted AdamW/global cursor contract differs.");
        var tensors = CorpusTensors(config, budget); var moments = new Dictionary<string, long[]>(StringComparer.Ordinal);
        foreach (var tensor in tensors) { budget.Check(); moments.Add("first." + tensor.Key, tensor.Value); moments.Add("second." + tensor.Key, tensor.Value); }
        VerifyTensors(PathFor("weights.safetensors"), Text(package, "WeightsSha256"), tensors, budget);
        VerifyTensors(PathFor(cpName + "/weights.safetensors"), Text(checkpoint, "WeightsSha256"), tensors, budget);
        VerifyTensors(PathFor(cpName + "/moments.safetensors"), Text(checkpoint, "MomentsSha256"), moments, budget);
        Require(EqualSha(FileHash(PathFor("tokenizer/tokenizer.json"), budget), Text(package, "TokenizerSha256")), "Tokenizer bytes differ.");
        var selected = new HashSet<string>(["corpus-package.json", "run-plan.json", "training-report.json", "weights.safetensors", "tokenizer/tokenizer.json", "input-lengths.json", "resources-reclaimed.txt",
            "LICENSE-COMPONENTS.md", "base-LICENSE-COMPONENTS.md", "base-license-provenance.json", "corpus-provenance.json"], StringComparer.Ordinal);
        var (inputManifest, corpusBinding) = VerifyCorpusProvenance(root, package, selected, budget);
        if (!testOnly)
        {
            var sourceAssets = Document(PathFor("input-source-assets.json"), budget);
            Require(!sourceAssets.TryGetProperty("synthetic", out _) && !sourceAssets.TryGetProperty("fixture_only", out _) &&
                !plan.GetRawText().Contains("/fixture/", StringComparison.Ordinal), "Synthetic fixture inputs cannot claim production publication.");
        }
        var lengths = Document(PathFor("input-lengths.json"), budget);
        Require(EqualSha(FileHash(PathFor("input-lengths.json"), budget), Text(plan, "LengthReportSha256")) && EqualSha(Text(plan, "LengthReportSha256"), Text(contract, "length_report_sha256")) &&
            Text(lengths, "Status") == "PASS_full_corpus_strict_lengths_only" && Number(lengths, "Train") == Number(plan, "TrainRecords") && Number(lengths, "Development") == Number(contract, "development_records") &&
            Number(lengths, "Rejected") == 0 && Number(lengths, "Accepted") == checked(Number(lengths, "Train") + Number(lengths, "Development")) && Flag(lengths, "CompleteScan") &&
            Text(lengths, "CorpusManifestSha256") == Text(package, "CorpusManifestSha256") && Text(lengths, "CorpusBindingsSha256") == Text(package, "CorpusBindingsSha256") &&
            EqualSha(Text(lengths, "TokenizerSha256"), Text(package, "TokenizerSha256")) && !Flag(lengths, "GpuFeaturesComputed") && !Flag(lengths, "FormalTrainingAdmitted"), "Full admitted length coverage differs.");
        var sources = lengths.GetProperty("TrainingBySource"); long trainingTotal = 0;
        var sourceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources.EnumerateObject()) { budget.Check(); Require(sourceNames.Count < 256 && source.Value.GetInt64() > 0, "Training source count/denominator invalid."); sourceNames.Add(source.Name); trainingTotal = checked(trainingTotal + source.Value.GetInt64()); }
        Require(sourceNames.Count == Number(contract, "source_count") && trainingTotal == Number(plan, "TrainRecords") && !Flag(inputManifest, "pilot") && !Flag(inputManifest, "formal_training_admitted") &&
            Number(lengths, "PhysicalRows") == Number(inputManifest, "records_written") && Number(lengths, "PhysicalRows") == checked(Number(lengths, "Accepted") + Number(lengths, "ExcludedRows")) &&
            Number(lengths, "MaximumTokens") == Number(plan, "MaximumTokens") && Number(lengths, "LongestTrainingTokens") >= 2 && Number(lengths, "LongestTrainingTokens") <= Number(lengths, "LongestTokens") &&
            Number(lengths, "LongestTokens") <= Number(plan, "MaximumTokens"), "Full source/physical/token denominator differs.");
        var shards = inputManifest.GetProperty("shards"); BoundArray(shards, 1, 4096, "Corpus shard"); var physicalSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shard in shards.EnumerateArray()) { budget.Check(); var split = Text(shard, "split"); Require(split is "train" or "development", "Shard crosses split boundary."); if (split == "train") physicalSources.Add(Text(shard, "source")); }
        Require(physicalSources.Count == Number(contract, "physical_train_source_count") && sourceNames.IsSubsetOf(physicalSources), "Source EOF scope differs.");
        Require(EqualSha(Text(identity, "RecordsSha256"), TextHash(Text(package, "CorpusManifestSha256") + "\n" + Text(package, "CorpusBindingsSha256") + "\n" + Text(corpusBinding, "ExclusionsSha256") + "\n" + Text(plan, "LengthReportSha256"))) &&
            EqualSha(Text(identity, "OrderSha256"), TextHash(CorpusSchedule + "\n" + Number(plan, "Seed") + "\n" + Number(plan, "Epochs") + "\n" + Number(plan, "TrainRecords") + "\n" + Text(package, "CorpusManifestSha256"))), "Frozen records/exclusions/scheduler identity differs.");
        VerifyValidationRows(validation, sourceNames, Number(plan, "ValidationPerSource"), budget);
        foreach (var name in new[] { "weights.safetensors", "moments.safetensors", "checkpoint.json", "corpus-cursor.json" }) selected.Add(cpName + "/" + name);
        var baseFiles = contract.GetProperty("base_files"); BoundArray(baseFiles, 3, 130, "Trusted base license"); var baseNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in baseFiles.EnumerateArray())
        {
            budget.Check(); var name = SafePaths.Relative(Text(item, "path"));
            Require(baseNames.Add(name) && (name is "LICENSE-COMPONENTS.md" or "license-provenance.json" || name.StartsWith("notices/", StringComparison.Ordinal)), "Trusted base inventory path differs.");
            var mapped = name.Contains('/') ? name : "base-" + name; var path = PathFor(mapped);
            Require(new FileInfo(path).Length == Number(item, "bytes") && EqualSha(FileHash(path, budget), Text(item, "sha256")), "Base notice original bytes differ."); selected.Add(mapped);
        }
        Require(baseNames.Contains("LICENSE-COMPONENTS.md") && baseNames.Contains("license-provenance.json") && baseNames.Any(n => n.StartsWith("notices/", StringComparison.Ordinal)) &&
            Inventory(PathFor("notices"), budget).Keys.ToHashSet(StringComparer.Ordinal).SetEquals(baseNames.Where(n => n.StartsWith("notices/", StringComparison.Ordinal)).Select(n => n[8..])), "Base notices inventory incomplete or foreign.");
        Require(selected.Count <= 1024, "Publication owned file cap exceeded.");
        var trainProofResult = VerifyExecutionProof(trainingProof, budget); var validateProofResult = VerifyExecutionProof(validationProof, budget);
        Require((trainProofResult.RootPid is null || trainProofResult.RootPid == Number(report, "ProcessId")) &&
            (validateProofResult.RootPid is null || validateProofResult.RootPid == Number(validation, "ProcessId")), "C# execution proof payload PID differs from the training/validation report.");
        Require(Path.GetFullPath(trainingProof) != Path.GetFullPath(validationProof), "Separate execution receipts required.");
        var output = FreshDirectory(outputRoot, packageRoot, budget); var runRoot = Path.Combine(output, "run"); Directory.CreateDirectory(runRoot);
        foreach (var name in selected.Order(StringComparer.Ordinal)) { budget.Check(); _ = Copy(PathFor(name), AssetPath(runRoot, name, budget), name, budget); }
        foreach (var pair in new[] { (validationPath, "validation.json"), (contractPath, "publication-contract.json"), (trainingProof, "execution/train-result.receipt"), (validationProof, "execution/validation-result.receipt") })
        { budget.Check(); _ = Copy(pair.Item1, AssetPath(runRoot, pair.Item2, budget), pair.Item2, budget); }
        var codeNotices = contract.GetProperty("code_notices");
        Require(codeNotices.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(["code-LICENSE.txt", "code-NOTICE.txt"]), "Code license/notice inventory omitted.");
        foreach (var notice in codeNotices.EnumerateObject())
        {
            budget.Check(); var source = AssetPath(toolRoot, notice.Name, budget); Require(EqualSha(FileHash(source, budget), notice.Value.GetString() ?? ""), "Code notice frozen bytes differ.");
            _ = Copy(source, AssetPath(runRoot, "notices/" + notice.Name, budget), "notices/" + notice.Name, budget);
        }
        var publicationBinding = new CorpusPublicationBinding(1, runId, Text(contract, "original_dataset_commit"), Text(contract, "published_dataset_commit"), packageSha,
            FileHash(validationPath, budget), FileHash(contractPath, budget), cpName, updates, trainProofResult.Sha256, validateProofResult.Sha256);
        WriteJson(Path.Combine(runRoot, "publication-binding.json"), publicationBinding, TransportJsonContext.Default.CorpusPublicationBinding, budget);
        File.WriteAllText(Path.Combine(runRoot, "PUBLICATION-README.md"), "# 完整语料实验模型包\n\n入口 corpus-package.json；保留 encoder/head、tokenizer 与四文件 AdamW 断点。完整覆盖和独立 GPU 前向分别核验；正式质量、校准、新权重 AOT、性能未接受。\n\n包含私有来源元数据与详细验证记录，只允许 private 发布。物理分块必须完整重组后核原 SHA。\n", new UTF8Encoding(false));
        var inventory = Inventory(runRoot, budget); var files = inventory.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        var manifest = new LogicalManifest(1, "full-corpus-model", runId, "PASS_offline_full_corpus_publication_contract", "runs/" + runId + "/", files,
            files.Sum(f => f.Bytes), packageSha, updates, Text(contract, "published_dataset_commit"), Text(contract, "original_dataset_commit"), new(true, true, true, true, true, true), true, false, testOnly);
        var manifestPath = Path.Combine(output, "publication-manifest.json"); WriteJson(manifestPath, manifest, TransportJsonContext.Default.LogicalManifest, budget);
        _ = VerifyLogical(runRoot, manifestPath, budget); return manifest;
    }

    private static Dictionary<string, long[]> CorpusTensors(JsonElement config, Budget budget)
    {
        var h = Number(config, "HiddenSize"); var m = Number(config, "IntermediateSize"); var v = Number(config, "VocabularySize"); var layers = Number(config, "LayerCount"); var heads = Number(config, "HeadCount");
        Require(h is >= 2 and <= 2048 && m is >= 2 and <= 8192 && v is >= 1 and <= 300000 && layers is >= 1 and <= 64 && heads >= 1 && h % heads == 0 && h / heads % 2 == 0 &&
            Number(config, "MaxTokens") is >= 2 and <= 8192 && Number(config, "LocalAttention") is >= 2 and <= 1024 && Number(config, "GlobalAttentionEvery") >= 1, "Tensor config dimensions differ.");
        foreach (var key in new[] { "GlobalRopeTheta", "LocalRopeTheta", "NormEpsilon" }) Require(double.IsFinite(config.GetProperty(key).GetDouble()) && config.GetProperty(key).GetDouble() > 0, "Nonfinite config setting.");
        var result = new Dictionary<string, long[]>(StringComparer.Ordinal) { ["model.embeddings.tok_embeddings.weight"] = [v, h], ["model.embeddings.norm.weight"] = [h], ["model.final_norm.weight"] = [h] };
        for (var layer = 0; layer < layers; layer++)
        {
            budget.Check(); var prefix = "model.layers." + layer + "."; if (layer > 0) result.Add(prefix + "attn_norm.weight", [h]);
            result.Add(prefix + "attn.Wqkv.weight", [3 * h, h]); result.Add(prefix + "attn.Wo.weight", [h, h]); result.Add(prefix + "mlp_norm.weight", [h]);
            result.Add(prefix + "mlp.Wi.weight", [2 * m, h]); result.Add(prefix + "mlp.Wo.weight", [h, m]);
        }
        result.Add("head.linear.weight", [3, h]); Require(result.Count * 2 <= 405, "Tensor count cap exceeded."); return result;
    }

    private static void VerifyTensors(string path, string sha, Dictionary<string, long[]> expected, Budget budget)
    {
        Require(EqualSha(FileHash(path, budget), sha), "SafeTensors asset SHA differs.");
        using var file = File.OpenRead(path); Span<byte> prefix = stackalloc byte[8]; file.ReadExactly(prefix); var headerSize = BinaryPrimitives.ReadUInt64LittleEndian(prefix);
        Require(headerSize is >= 1 and <= 1024 * 1024 && headerSize + 8 < (ulong)file.Length, "SafeTensors header bounds differ.");
        var bytes = new byte[(int)headerSize]; file.ReadExactly(bytes); using var document = JsonDocument.Parse(bytes); ValidateJson(document.RootElement, budget);
        Require(document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected.Keys), "Exact encoder/head or AdamW tensor set differs.");
        long offset = 0;
        foreach (var tensor in document.RootElement.EnumerateObject())
        {
            budget.Check(); var info = tensor.Value; var shape = info.GetProperty("shape"); BoundArray(shape, 1, 4, "Tensor shape");
            Require(Text(info, "dtype") == "F32" && shape.EnumerateArray().Select(n => n.GetInt64()).SequenceEqual(expected[tensor.Name]), "Tensor dtype/shape differs.");
            long elements = 1; foreach (var dimension in expected[tensor.Name]) elements = checked(elements * dimension);
            Require(elements is > 0 and <= 200000000, "Tensor element cap exceeded."); var end = checked(offset + elements * 4); var offsets = info.GetProperty("data_offsets");
            BoundArray(offsets, 2, 2, "Tensor offsets"); Require(offsets[0].GetInt64() == offset && offsets[1].GetInt64() == end, "Tensor hole/overlap/offset differs."); offset = end;
        }
        Require(offset + (long)headerSize + 8 == file.Length, "Tensor payload truncated or trailing.");
    }

    private static (JsonElement Manifest, JsonElement Bindings) VerifyCorpusProvenance(string root, JsonElement package, HashSet<string> selected, Budget budget)
    {
        string PathFor(string name) => AssetPath(root, name, budget);
        Require(EqualSha(FileHash(PathFor("corpus-provenance.json"), budget), Text(package, "SourceProvenanceSha256")), "Corpus provenance SHA differs.");
        var provenance = Document(PathFor("corpus-provenance.json"), budget);
        Require(Number(provenance, "SchemaVersion") == 1 && Text(provenance, "Status") == "PASS_private_bound_source_metadata_bytes_only" && Flag(provenance, "PrivateSourceMetadata") &&
            !Flag(provenance, "PublicPublicationAccepted") && !Flag(provenance, "ContainsRawExamplesOrGoldLabels") && provenance.GetProperty("SourceRoot").ValueKind == JsonValueKind.Null &&
            Text(provenance, "SourceAssetsPath") == "input-source-assets.json" && Text(provenance, "CorpusManifestSha256") == Text(package, "CorpusManifestSha256") &&
            Text(provenance, "OriginalBindingsSha256") == Text(package, "CorpusBindingsSha256"), "Private source provenance semantics differ.");
        var files = provenance.GetProperty("Files"); BoundArray(files, 4, 512, "Provenance file"); var mapped = new Dictionary<string, JsonElement>(StringComparer.Ordinal); long total = 0;
        foreach (var item in files.EnumerateArray())
        {
            budget.Check(); var name = SafePaths.Relative(Text(item, "PackagePath")); var size = Number(item, "Bytes"); var path = PathFor(name); total = checked(total + size);
            Require(!name.Split('/').Contains("shards", StringComparer.Ordinal) && mapped.TryAdd(name, item) && size is >= 0 and <= MaximumJsonBytes && total <= 128 * 1024 * 1024 &&
                new FileInfo(path).Length == size && EqualSha(FileHash(path, budget), Text(item, "Sha256")), "Provenance path/bytes/SHA differs."); selected.Add(name);
        }
        var required = new[] { "input-manifest.json", "input-corpus-bindings.json", "input-source-rights.json", "input-source-assets.json" };
        Require(required.All(mapped.ContainsKey) && Text(mapped["input-manifest.json"], "Sha256") == Text(package, "CorpusManifestSha256") && Text(mapped["input-corpus-bindings.json"], "Sha256") == Text(package, "CorpusBindingsSha256"), "Source manifest/bindings/rights/assets omitted.");
        var manifest = Document(PathFor("input-manifest.json"), budget); var binding = Document(PathFor("input-corpus-bindings.json"), budget);
        Require(Text(binding, "ManifestSha256") == Text(package, "CorpusManifestSha256") && !Flag(binding, "FormalTrainingAdmitted") &&
            Text(binding, "SourceRightsSha256") == Text(mapped["input-source-rights.json"], "Sha256") && Text(binding, "SourceAssetsSha256") == Text(mapped["input-source-assets.json"], "Sha256") &&
            Same(binding.GetProperty("SourceRoot"), provenance.GetProperty("OriginalSourceRoot")) && Same(binding.GetProperty("SourceAssetsPath"), provenance.GetProperty("OriginalSourceAssetsPath")), "Historical source bindings differ.");
        var declared = new HashSet<string>(StringComparer.Ordinal); var noticeBindings = provenance.GetProperty("SourceNoticeBindings");
        foreach (var notice in manifest.GetProperty("source_notice_bindings").EnumerateObject())
        {
            budget.Check(); Require(declared.Count < 508, "Provenance notice cap exceeded."); var target = "corpus-provenance/" + notice.Name.Replace('\\', '/');
            _ = SafePaths.Relative(target); Require(declared.Add(target) && mapped.TryGetValue(target, out var item) && Text(item, "OriginalRelativePath") == notice.Name &&
                Text(item, "Sha256") == notice.Value.GetString() && noticeBindings.GetProperty(target).GetString() == notice.Value.GetString(), "Declared notice mapping differs.");
        }
        Require(mapped.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(required.Concat(declared)) && noticeBindings.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(declared), "Complete notice inventory differs.");
        foreach (var pair in new[] { ("SourcePolicySha256", "source-scope-policy.json"), ("LengthScanSha256", "length-scan-complete.json") })
            if (binding.TryGetProperty(pair.Item1, out var expected) && expected.ValueKind != JsonValueKind.Null)
                Require(noticeBindings.TryGetProperty("corpus-provenance/" + pair.Item2, out var notice) && notice.GetString() == expected.GetString(), "Policy or complete-scan notice omitted.");
        return (manifest, binding);
    }

    private static void VerifyValidationRows(JsonElement validation, HashSet<string> sources, long maximumPerSource, Budget budget)
    {
        var tally = validation.GetProperty("OriginalRowsBySourceAndSplit"); var groups = new HashSet<string>(StringComparer.Ordinal); long total = 0; var hasDevelopment = false;
        foreach (var item in tally.EnumerateObject())
        {
            budget.Check(); Require(groups.Count < 256 && item.Value.GetInt64() >= 1 && item.Value.GetInt64() <= maximumPerSource, "Validation source count differs.");
            var separator = item.Name.LastIndexOf('|'); Require(separator > 0 && sources.Contains(item.Name[..separator]) && item.Name[(separator + 1)..] is "train" or "development", "Validation source/split scope differs.");
            groups.Add(item.Name); hasDevelopment |= item.Name.EndsWith("|development", StringComparison.Ordinal); total = checked(total + item.Value.GetInt64());
        }
        Require(groups.Count > 0 && hasDevelopment && sources.All(s => groups.Contains(s + "|train")) && total == Number(validation, "OriginalRows"), "Independent validation per-source coverage differs.");
        var rows = validation.GetProperty("Rows"); BoundArray(rows, 1, 16384, "Validation row");
        Require(rows.GetArrayLength() == Number(validation, "OriginalRows") + Number(validation, "RotatedRows"), "Validation row denominator differs.");
        var original = new Dictionary<(string, string), JsonElement>(); var rotated = new Dictionary<(string, string), JsonElement>();
        foreach (var row in rows.EnumerateArray())
        {
            budget.Check(); var split = Text(row, "Split"); var type = Text(row, "Type"); Require(split is "train" or "development" && type is "Choice" or "Boolean" or "Score", "Validation split/type differs.");
            var candidates = row.GetProperty("CandidateIds"); var probabilities = row.GetProperty("Probability"); var targets = row.GetProperty("Target");
            BoundArray(candidates, 2, 32, "Candidate vector"); BoundArray(probabilities, candidates.GetArrayLength(), candidates.GetArrayLength(), "Probability vector"); BoundArray(targets, candidates.GetArrayLength(), candidates.GetArrayLength(), "Target vector");
            Require(candidates.EnumerateArray().Select(v => v.GetString()).Distinct(StringComparer.Ordinal).Count() == candidates.GetArrayLength() && double.IsFinite(row.GetProperty("Loss").GetDouble()), "Candidate uniqueness or loss differs.");
            int Argmax(JsonElement vector)
            {
                double sum = 0, best = -1; var bestIndex = 0;
                for (var index = 0; index < vector.GetArrayLength(); index++) { budget.Check(); var value = vector[index].GetDouble(); Require(double.IsFinite(value) && value is >= 0 and <= 1, "Nonfinite/out-of-range vector."); sum += value; if (value > best) { best = value; bestIndex = index; } }
                Require(Math.Abs(sum - 1) <= 1e-4, "Unnormalized vector."); return bestIndex;
            }
            Require(Text(row, "PredictedCandidateId") == candidates[Argmax(probabilities)].GetString() && Text(row, "TeacherArgmaxCandidateId") == candidates[Argmax(targets)].GetString(), "Validation argmax binding differs.");
            var collection = Flag(row, "Rotated") ? rotated : original; Require(collection.TryAdd((Text(row, "RecordId"), split), row), "Duplicate validation row identity.");
        }
        Require(original.Count == Number(validation, "OriginalRows") && rotated.Count == Number(validation, "RotatedRows") &&
            rotated.Keys.ToHashSet().SetEquals(original.Where(p => Text(p.Value, "Type") == "Choice").Select(p => p.Key)), "Choice rotation denominator differs.");
        foreach (var pair in rotated)
        {
            budget.Check(); var source = original[pair.Key];
            foreach (var field in new[] { "CandidateIds", "Target" })
            {
                var left = pair.Value.GetProperty(field); var right = source.GetProperty(field);
                Require(left.GetArrayLength() == right.GetArrayLength(), "Rotated vector length differs.");
                for (var index = 0; index < left.GetArrayLength(); index++) { budget.Check(); Require(Same(left[index], right[(index + 1) % right.GetArrayLength()]), "Choice rotation lost candidate/target mapping."); }
            }
        }
    }

    private sealed record ExecutionProof(string Sha256, int? RootPid);
    private static ExecutionProof VerifyExecutionProof(string path, Budget budget)
    {
        RegularFiles.Require(path, budget); Require(new FileInfo(path).Length <= 65536, "Execution receipt exceeds byte cap.");
        int? rootPid = null;
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var proof = ReadJson(path, ExecutionJsonContext.Default.ExecutionResult, budget);
            Require(proof.SchemaVersion == 1 && proof.Status == "PASS" && proof.ExecutionStatus == "SUCCEEDED" && proof.CleanupStatus == "PASS" &&
                proof.ExitCode == 0 && proof.RemainingProcesses == 0 && proof.InputComplete && proof.OutputComplete && !proof.OutputLimitExceeded &&
                proof.Processes is { Length: >= 1 and <= 256 } && proof.Errors is { Length: 0 } && proof.RootPid is > 0 &&
                proof.Processes.Any(p => p.Pid == proof.RootPid && p.ParentPid > 0 && p.ParentChain.Length > 0 && !string.IsNullOrEmpty(p.CommandLine)), "C# execution proof failed or cleanup incomplete.");
            rootPid = proof.RootPid;
        }
        else
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal); var lines = File.ReadAllLines(path);
            Require(lines.Length is > 0 and <= 64, "Legacy execution receipt line cap exceeded.");
            foreach (var line in lines) { budget.Check(); var parts = line.Split('\t'); Require(parts.Length == 2 && fields.TryAdd(parts[0], parts[1]), "Legacy execution receipt malformed/duplicate."); }
            foreach (var pair in new[] { ("status", "completed"), ("payload_exit", "0"), ("final_check_complete", "1"), ("cleanup_incomplete", "0"), ("verified_live_count", "0"), ("unknown_count", "0"), ("unchecked_count", "0"), ("cleanup_budget_exhausted", "0") })
                Require(fields.TryGetValue(pair.Item1, out var value) && value == pair.Item2, "Legacy execution proof failed or incomplete.");
        }
        return new(FileHash(path, budget), rootPid);
    }
}
