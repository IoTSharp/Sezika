using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sezika.Tooling;

internal static class CorpusInputAdmission
{
    internal const string PassStatus = "PASS_full_corpus_inputs_only";
    private const long MaximumFileBytes = 8L * 1024 * 1024 * 1024;
    private const long MaximumTotalBytes = 128L * 1024 * 1024 * 1024;
    private const int MaximumMetadataBytes = 16 * 1024 * 1024;
    private const string ScanAlgorithm = "strict_decision_v1_choice_rotation_chunk4096_explicit_source_and_token_exclusions_v1";

    internal static CorpusInputAdmissionReport Validate(CorpusInputAdmissionRequest request, Budget budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var validation = new Validation(request, budget);
        try { return validation.Run(); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or
            KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            // Cancellation/deadline exceptions deliberately keep their original semantics.
            budget.Check();
            return validation.Report("FAIL", error.GetType().Name + ": " + error.Message);
        }
    }

    private sealed class InputFile(string path, long bytes, string sha256)
    {
        internal string Path { get; } = path;
        internal long Bytes { get; } = bytes;
        internal string Sha256 { get; } = sha256;
        internal bool Verified { get; set; }
        internal bool Shard { get; set; }
    }

    private sealed class Validation(CorpusInputAdmissionRequest request, Budget budget)
    {
        private readonly Dictionary<string, InputFile> files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<InputFile>> sources = new(StringComparer.Ordinal);
        private readonly HashSet<string> trainingSources = new(StringComparer.Ordinal);
        private long plannedBytes, verifiedBytes, metadataBytes;
        private int verifiedFiles, plannedShards;
        private bool inventoryComplete;
        private string corpusRoot = "", modelRoot = "", modelCommit = "";
        private string? verifiedEncoderSha, verifiedTokenizerSha, verifiedManifestSha, verifiedBindingsSha, verifiedExclusionsSha, verifiedLengthsSha, verifiedRecordsSha;
        private string planPath = request.PlanPath, policyPath = request.SourcePolicyPath, readbackPath = request.ModelReadbackReceiptPath;

        internal CorpusInputAdmissionReport Report(string status, string? failure) => new()
        {
            Status = status, PlanPath = planPath, PlanSha256 = request.PlanSha256,
            SourcePolicyPath = policyPath, SourcePolicySha256 = request.SourcePolicySha256,
            ModelReadbackReceiptPath = readbackPath, ModelReadbackReceiptSha256 = request.ModelReadbackReceiptSha256,
            CorpusRoot = corpusRoot, BaseModelRoot = modelRoot, ModelCommit = modelCommit,
            BaseEncoderSha256 = verifiedEncoderSha, TokenizerSha256 = verifiedTokenizerSha, CorpusManifestSha256 = verifiedManifestSha,
            CorpusBindingsSha256 = verifiedBindingsSha, ExclusionsSha256 = verifiedExclusionsSha, LengthReportSha256 = verifiedLengthsSha, RecordsSha256 = verifiedRecordsSha,
            InventoryComplete = inventoryComplete, PlannedFiles = files.Count, VerifiedFiles = verifiedFiles,
            PlannedBytes = plannedBytes, VerifiedBytes = verifiedBytes, PlannedShards = plannedShards,
            VerifiedShards = files.Values.Count(f => f.Shard && f.Verified), PlannedSources = sources.Count,
            VerifiedSources = sources.Values.Count(group => group.All(f => f.Verified)), Failure = failure
        };

        internal CorpusInputAdmissionReport Run()
        {
            Require(request.MaximumFiles is >= 1 and <= 8192 && request.MaximumBytes is >= 1 and <= MaximumTotalBytes,
                "Input admission file/byte limits must be within8192/128GiB.");
            Sha(request.PlanSha256); Sha(request.SourcePolicySha256); Sha(request.ModelReadbackReceiptSha256);
            planPath = Absolute(request.PlanPath); policyPath = Absolute(request.SourcePolicyPath); readbackPath = Absolute(request.ModelReadbackReceiptPath);
            var plan = Read(planPath, request.PlanSha256, "plan");
            Require(Integer(plan, "SchemaVersion", 1, 1) == 1, "Plan schema differs.");
            corpusRoot = Root(Text(plan, "CorpusRoot")); modelRoot = Root(Text(plan, "BaseModelRoot"));
            Require(!SamePath(corpusRoot, modelRoot), "Corpus and base-model roots must be distinct.");
            modelCommit = Text(plan, "ModelCommit", 40); TaskOperations.Commit(modelCommit);
            var manifestSha = HashField(plan, "CorpusManifestSha256");
            var bindingsSha = HashField(plan, "CorpusBindingsSha256");
            var lengthsSha = HashField(plan, "LengthReportSha256");
            var maximumTokens = Integer(plan, "MaximumTokens", 2, 8192);
            var trainRecords = Integer(plan, "TrainRecords", 2, 30_000_000);

            var manifest = Read(Within(corpusRoot, "manifest.json"), manifestSha, "corpus manifest");
            var binding = Read(Within(corpusRoot, "corpus-bindings.json"), bindingsSha, "corpus bindings");
            var physical = Integer(manifest, "records_written", 1, 50_000_000);
            Require(Integer(manifest, "schema_version", 1, 1) == 1 && Text(manifest, "status") == "COMPLETE_PREPARATION_ONLY" &&
                !Flag(manifest, "pilot") && !Flag(manifest, "formal_training_admitted"), "Complete nonpilot corpus required.");
            Require(HashField(binding, "ManifestSha256") == manifestSha && !Flag(binding, "FormalTrainingAdmitted"), "Corpus manifest binding differs.");
            var exclusionsSha = HashField(binding, "ExclusionsSha256");
            var rightsSha = HashField(binding, "SourceRightsSha256");
            var assetsSha = HashField(binding, "SourceAssetsSha256");
            Add(Within(corpusRoot, "exclusions.jsonl"), exclusionsSha);
            Add(Within(corpusRoot, "source-rights.json"), rightsSha);
            // The full derived route materializes the metadata inside its own root.
            var assetRelative = OptionalText(binding, "SourceAssetsPath") ?? "source-assets.json";
            Add(Within(corpusRoot, assetRelative), assetsSha);
            var shards = Array(manifest, "shards", 1, 4096);
            plannedShards = shards.GetArrayLength();
            var shardPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long shardRows = 0;
            foreach (var shard in shards.EnumerateArray())
            {
                budget.Check();
                var relative = Text(shard, "path", 1024);
                Require(shardPaths.Add(SafePaths.Relative(relative)), "Duplicate or case-colliding shard path.");
                var split = Text(shard, "split"); var source = Text(shard, "source", 256);
                Require(split is "train" or "development", "Shard split differs.");
                var rows = Integer(shard, "records", 1, 50_000);
                shardRows = checked(shardRows + rows);
                var file = Add(Within(corpusRoot, relative), HashField(shard, "sha256"), Integer(shard, "bytes", 1, 2L * 1024 * 1024 * 1024));
                Require(!file.Shard, "Shard inventory aliases another shard."); file.Shard = true;
                if (!sources.TryGetValue(source, out var group))
                {
                    Require(sources.Count < 128, "Corpus source limit128 exceeded.");
                    sources.Add(source, group = []);
                }
                group.Add(file);
                if (split == "train") trainingSources.Add(source);
            }
            Require(shardRows == physical && trainingSources.Count > 0, "Physical/source shard denominator differs.");
            var notices = NoticeMap(manifest);
            foreach (var notice in notices) Add(Within(corpusRoot, notice.Key), notice.Value);

            Require(HashField(binding, "SourcePolicySha256") == request.SourcePolicySha256 &&
                notices.TryGetValue("source-scope-policy.json", out var noticePolicy) && noticePolicy == request.SourcePolicySha256,
                "Source policy must bind both corpus metadata and its standard notice.");
            var localPolicy = Within(corpusRoot, "source-scope-policy.json");
            var policy = Read(localPolicy, request.SourcePolicySha256, "corpus source policy");
            _ = Read(policyPath, request.SourcePolicySha256, "requested source policy");
            var allowedSources = Policy(policy);

            var modelReceipt = Read(readbackPath, request.ModelReadbackReceiptSha256, "model readback receipt");
            Require(Text(modelReceipt, "status") == "PASS_local_complete_base_and_run_full_bytes_sha256" &&
                Text(modelReceipt, "source_model_commit", 40) == modelCommit && SamePath(Absolute(Text(modelReceipt, "source_checkout")), modelRoot),
                "Complete base readback status, source checkout or model commit differs.");
            var baseCount = (int)Integer(modelReceipt, "base_files", 3, 256);
            var modelFiles = Array(modelReceipt, "files", baseCount, 1000);
            var baseAssets = new Dictionary<string, InputFile>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < baseCount; index++)
            {
                budget.Check(); var asset = modelFiles[index];
                var relative = SafePaths.Relative(Text(asset, "path", 1024));
                Require(!baseAssets.ContainsKey(relative), "Duplicate base-model asset path.");
                baseAssets.Add(relative, Add(Within(modelRoot, relative), HashField(asset, "sha256"), Integer(asset, "bytes", 0, MaximumFileBytes)));
            }
            Require(baseAssets.TryGetValue("model.json", out var modelAsset), "Complete base receipt omits model.json.");
            var model = Read(modelAsset!.Path, modelAsset.Sha256, "base model manifest");
            Require(Integer(model, "schema_version", 1, 1) == 1, "Base model schema differs.");
            _ = Text(model, "license", 4096);
            var encoderSha = BoundModelAsset(model, "weights_file", "weights_sha256", baseAssets);
            var tokenizerSha = BoundModelAsset(model, "tokenizer_file", "tokenizer_sha256", baseAssets);

            var lengthsPath = Absolute(Text(plan, "LengthReportPath"));
            var lengths = Read(lengthsPath, lengthsSha, "strict lengths receipt");
            Require(Text(lengths, "Status") == "PASS_full_corpus_strict_lengths_only" &&
                HashField(lengths, "CorpusManifestSha256") == manifestSha && HashField(lengths, "CorpusBindingsSha256") == bindingsSha &&
                HashField(lengths, "TokenizerSha256") == tokenizerSha && Integer(lengths, "MaximumTokens", 2, 8192) == maximumTokens &&
                Integer(lengths, "PhysicalRows", 1, 50_000_000) == physical && Integer(lengths, "Rejected", 0, 50_000_000) == 0 &&
                !Flag(lengths, "GpuFeaturesComputed") && !Flag(lengths, "FormalTrainingAdmitted"), "Strict length identity/status differs.");
            var excluded = Integer(lengths, "ExcludedRows", 0, 1_000_000);
            var accepted = Integer(lengths, "Accepted", 1, 50_000_000);
            var train = Integer(lengths, "Train", 2, 30_000_000);
            var development = Integer(lengths, "Development", 1, 50_000_000);
            Require(physical == checked(excluded + accepted) && accepted == checked(train + development) && train == trainRecords,
                "Strict length physical/excluded/accepted/train/development denominator differs.");
            var longest = Integer(lengths, "LongestTokens", 2, maximumTokens);
            _ = Integer(lengths, "LongestTrainingTokens", 2, longest);
            _ = Text(lengths, "LongestTrainingRecordId", 8192);
            var longestSource = Text(lengths, "LongestTrainingSource", 256);
            var bySource = Counts(lengths, "TrainingBySource", train, trainingSources);
            Require(bySource.ContainsKey(longestSource) && bySource.Keys.All(allowedSources.Contains), "Strict length maximum/source-policy coverage differs.");
            Require(Flag(lengths, "CompleteScan", false) && HashField(lengths, "SourcePolicySha256") == request.SourcePolicySha256 &&
                HashField(lengths, "FailureLedgerSha256") == exclusionsSha, "Complete scan and exclusion ledger are mandatory.");
            var scanSha = HashField(binding, "LengthScanSha256");
            Require(HashField(lengths, "ScanCompletionSha256") == scanSha && notices.TryGetValue("length-scan-complete.json", out var noticeScan) && noticeScan == scanSha,
                "Complete scan SHA/standard notice differs.");
            var scan = Read(Within(corpusRoot, "length-scan-complete.json"), scanSha, "complete length scan");
            var originalExcluded = CompleteScan(scan, lengths, manifest, binding, shards, physical, maximumTokens, tokenizerSha, scanSha, exclusionsSha, bySource, notices);

            // All graph members are now known. Leaf failure retains the full inventory.
            inventoryComplete = true;
            foreach (var file in files.Values) { budget.Check(); Verify(file); }
            Require(Ledger(Within(corpusRoot, "exclusions.jsonl")) == excluded, "Actual derived exclusion ledger count differs.");
            Require(Ledger(Within(corpusRoot, "components/length-parent/exclusions.jsonl")) == originalExcluded,
                "Actual original exclusion ledger count differs.");
            budget.Check();
            Require(verifiedFiles == files.Count && verifiedBytes == plannedBytes && Report(PassStatus, null).VerifiedShards == plannedShards,
                "Verified input inventory denominator differs.");
            verifiedEncoderSha = encoderSha; verifiedTokenizerSha = tokenizerSha; verifiedManifestSha = manifestSha; verifiedBindingsSha = bindingsSha;
            verifiedExclusionsSha = exclusionsSha; verifiedLengthsSha = lengthsSha;
            verifiedRecordsSha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifestSha + "\n" + bindingsSha + "\n" + exclusionsSha + "\n" + lengthsSha)));
            return Report(PassStatus, null);
        }

        private long CompleteScan(JsonElement scan, JsonElement lengths, JsonElement manifest, JsonElement binding, JsonElement shards,
            long physical, long maximumTokens, string tokenizerSha, string scanSha, string exclusionsSha,
            Dictionary<string, long> bySource, Dictionary<string, string> notices)
        {
            Require(Text(scan, "Status") == "PASS_complete_strict_scan_explicit_exclusions_private_only", "Exact complete C# scan status required.");
            var inputs = Object(scan, "Inputs");
            Require(Integer(inputs, "SchemaVersion", 1, 1) == 1 && Text(inputs, "Algorithm") == ScanAlgorithm &&
                Integer(inputs, "MaximumTokens", 2, 8192) == maximumTokens && Integer(inputs, "PhysicalRows", 1, 50_000_000) == physical &&
                Integer(inputs, "Shards", 1, 4096) == shards.GetArrayLength() && HashField(inputs, "TokenizerSha256") == tokenizerSha &&
                HashField(inputs, "SourcePolicySha256") == request.SourcePolicySha256, "Complete scan input identities differ.");
            var parentManifestSha = HashField(inputs, "ManifestSha256");
            var parentBindingsSha = HashField(inputs, "BindingsSha256");
            var parentExclusionsSha = HashField(inputs, "OriginalExclusionsSha256");
            var chunkRows = Integer(inputs, "ChunkRows", 1, 50_000);
            var maximumExclusions = Integer(inputs, "MaximumExclusions", 1, 1_000_000);
            var chunks = Integer(inputs, "Chunks", 1, 32768);
            var original = OptionalInteger(scan, "OriginalExcluded", 0, 1_000_000);
            var scope = OptionalInteger(scan, "SourceScopeExcluded", 0, 1_000_000);
            var token = OptionalInteger(scan, "TokenLimitExcluded", 0, 1_000_000);
            var totalExcluded = checked(original + scope + token);
            var accepted = Integer(scan, "Accepted", 1, 50_000_000);
            var train = Integer(scan, "Train", 1, 30_000_000);
            var development = Integer(scan, "Development", 1, 50_000_000);
            Require(Integer(scan, "PhysicalRows", 1, 50_000_000) == physical && physical == checked(totalExcluded + accepted) &&
                accepted == checked(train + development) && Integer(scan, "ExclusionIdentities", 0, maximumExclusions) == totalExcluded &&
                Flag(scan, "FamilyIsolationChecked", false) && Integer(scan, "Families", 1, 4_000_000) <= accepted &&
                !Flag(scan, "GpuFeaturesComputed") && !Flag(scan, "FormalTrainingAdmitted") && Text(scan, "ExclusionsPath") == "scan-exclusions.jsonl" &&
                HashField(scan, "ExclusionsSha256") == exclusionsSha, "Complete scan counters/family/experimental state differ.");
            _ = HashField(scan, "ScanPlanSha256");
            var refs = Array(scan, "Chunks", 1, 32768);
            Require(refs.GetArrayLength() == chunks, "Complete scan chunk reference denominator differs.");
            long expectedChunks = 0;
            foreach (var shard in shards.EnumerateArray())
            {
                budget.Check(); var rows = Integer(shard, "records", 1, 50_000);
                expectedChunks = checked(expectedChunks + (rows + chunkRows - 1) / chunkRows);
            }
            Require(expectedChunks == chunks, "Complete scan shard/chunk coverage differs.");
            for (var index = 0; index < refs.GetArrayLength(); index++)
            {
                budget.Check(); var reference = refs[index];
                Require(Integer(reference, "Index", 0, 32767) == index && Text(reference, "Path") == "chunks/chunk-" + index.ToString("D5", CultureInfo.InvariantCulture) + ".json",
                    "Complete scan chunk order/canonical path differs.");
                _ = HashField(reference, "Sha256");
            }
            var longest = Integer(scan, "LongestTokens", 2, maximumTokens);
            var longestTrain = Integer(scan, "LongestTrainTokens", 2, longest);
            var longestId = Text(scan, "LongestTrainId", 8192); var longestSource = Text(scan, "LongestTrainSource", 256);
            var scanSources = Counts(scan, "TrainingBySource", train, trainingSources);
            Require(scanSources.Count == bySource.Count && scanSources.All(pair => bySource.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
                scanSources.ContainsKey(longestSource), "Complete scan per-source denominator differs.");
            Require(HashField(lengths, "ParentCorpusManifestSha256") == parentManifestSha &&
                Integer(lengths, "ExcludedRows", 0, 1_000_000) == totalExcluded && Integer(lengths, "Accepted", 1, 50_000_000) == accepted &&
                Integer(lengths, "Train", 2, 30_000_000) == train && Integer(lengths, "Development", 1, 50_000_000) == development &&
                OptionalInteger(lengths, "OriginalExcludedRows", 0, 1_000_000) == original && OptionalInteger(lengths, "SourceScopeExcludedRows", 0, 1_000_000) == scope &&
                OptionalInteger(lengths, "TokenLimitExcludedRows", 0, 1_000_000) == token && Integer(lengths, "LongestTokens", 2, maximumTokens) == longest &&
                Integer(lengths, "LongestTrainingTokens", 2, longest) == longestTrain && Text(lengths, "LongestTrainingRecordId", 8192) == longestId &&
                Text(lengths, "LongestTrainingSource", 256) == longestSource, "Derived strict lengths and complete scan disagree.");

            var parentRoot = Within(corpusRoot, "components/length-parent");
            var parentManifest = Read(Within(parentRoot, "manifest.json"), parentManifestSha, "archived parent manifest");
            var parentBinding = Read(Within(parentRoot, "corpus-bindings.json"), parentBindingsSha, "archived parent bindings");
            Add(Within(parentRoot, "exclusions.jsonl"), parentExclusionsSha);
            Require(Integer(parentManifest, "schema_version", 1, 1) == 1 && Text(parentManifest, "status") == "COMPLETE_PREPARATION_ONLY" &&
                !Flag(parentManifest, "pilot") && !Flag(parentManifest, "formal_training_admitted") && Integer(parentManifest, "records_written", 1, 50_000_000) == physical &&
                HashField(parentBinding, "ManifestSha256") == parentManifestSha && HashField(parentBinding, "ExclusionsSha256") == parentExclusionsSha &&
                HashField(parentBinding, "SourceAssetsSha256") == HashField(binding, "SourceAssetsSha256") &&
                HashField(parentBinding, "SourceRightsSha256") == HashField(binding, "SourceRightsSha256") && !Flag(parentBinding, "FormalTrainingAdmitted"),
                "Archived parent corpus/source metadata binding differs.");
            var parentShards = Array(parentManifest, "shards", 1, 4096);
            Require(parentShards.GetArrayLength() == shards.GetArrayLength(), "Archived parent shard count differs.");
            for (var index = 0; index < shards.GetArrayLength(); index++)
            {
                budget.Check();
                foreach (var name in new[] { "path", "source", "split", "records", "bytes", "sha256" })
                    Require(JsonElement.DeepEquals(parentShards[index].GetProperty(name), shards[index].GetProperty(name)), "Derived corpus changed original shard " + name + ".");
            }
            foreach (var notice in NoticeMap(parentManifest))
            {
                budget.Check(); Require(notices.TryGetValue(notice.Key, out var actual) && actual == notice.Value, "Original source notice omitted or changed.");
            }
            foreach (var pair in new[] { ("components/length-parent/manifest.json", parentManifestSha),
                ("components/length-parent/corpus-bindings.json", parentBindingsSha), ("components/length-parent/exclusions.jsonl", parentExclusionsSha) })
                Require(notices.TryGetValue(pair.Item1, out var actual) && actual == pair.Item2, "Archived parent notice omitted or changed.");
            var admission = Object(manifest, "length_admission");
            Require(Integer(admission, "SchemaVersion", 1, 1) == 1 && Text(admission, "Status") == "COMPLETE_SCAN_APPLIED_EXPLICIT_EXCLUSIONS" &&
                HashField(admission, "ParentManifestSha256") == parentManifestSha && HashField(admission, "ParentBindingsSha256") == parentBindingsSha &&
                HashField(admission, "ScanCompletionSha256") == scanSha && HashField(admission, "SourcePolicySha256") == request.SourcePolicySha256 &&
                HashField(admission, "ExclusionsSha256") == exclusionsSha && OptionalInteger(admission, "OriginalExcluded", 0, 1_000_000) == original &&
                OptionalInteger(admission, "SourceScopeExcluded", 0, 1_000_000) == scope && OptionalInteger(admission, "TokenLimitExcluded", 0, 1_000_000) == token,
                "Derived manifest length admission differs.");
            return original;
        }

        private HashSet<string> Policy(JsonElement policy)
        {
            Require(Integer(policy, "SchemaVersion", 1, 1) == 1 && Text(policy, "Status") == "PRIVATE_EXPERIMENT_SCOPE_FIXED_METADATA_ONLY" &&
                !Flag(policy, "FormalTrainingAdmitted") && !Flag(policy, "FullTokenLengthAccepted") && Text(policy, "DefaultAction") == "deny",
                "Explicit private source scope schema/status differs.");
            var allowed = Array(policy, "SourceAllowlist", 1, 128);
            var denied = Array(policy, "SourceDenylist", 0, 128);
            var assetMap = Object(policy, "SourceAssetAllowlist");
            var names = new HashSet<string>(StringComparer.Ordinal); var deniedNames = new HashSet<string>(StringComparer.Ordinal);
            var mapNames = new HashSet<string>(StringComparer.Ordinal); var count = 0;
            foreach (var name in allowed.EnumerateArray())
            {
                budget.Check(); Require(name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()) && name.GetString()!.Length <= 256 && names.Add(name.GetString()!),
                    "Duplicate/invalid allowed source.");
            }
            foreach (var entry in assetMap.EnumerateObject())
            {
                budget.Check(); Require(mapNames.Count < 128 && mapNames.Add(entry.Name) && names.Contains(entry.Name), "Source asset allowlist keys differ.");
                Require(entry.Value.ValueKind == JsonValueKind.Array && entry.Value.GetArrayLength() is >= 1 and <= 4096, "Allowed source asset array bound differs.");
                var paths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var asset in entry.Value.EnumerateArray())
                {
                    budget.Check(); Require(++count <= 4096 && paths.Add(Text(asset, "Path", 1024).Replace('\\', '/')) &&
                        Text(asset, "OriginalSplit") is "train" or "unsplit", "Allowed source asset identity/split differs.");
                    _ = Text(asset, "SourceRevision", 1024); _ = HashField(asset, "FileSha256");
                }
            }
            Require(mapNames.SetEquals(names), "Source asset allowlist source denominator differs.");
            foreach (var entry in denied.EnumerateArray())
            {
                budget.Check(); var name = Text(entry, "Dataset", 256); _ = Text(entry, "Reason", 4096);
                Require(!names.Contains(name) && deniedNames.Add(name), "Duplicate or contradictory denied source.");
            }
            return names;
        }

        private Dictionary<string, string> NoticeMap(JsonElement manifest)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!manifest.TryGetProperty("source_notice_bindings", out var map)) return result;
            Require(map.ValueKind == JsonValueKind.Object, "Source notice bindings must be an object.");
            foreach (var property in map.EnumerateObject())
            {
                budget.Check(); Require(result.Count < 2048 && property.Value.ValueKind == JsonValueKind.String, "Source notice limit/type differs.");
                var name = SafePaths.Relative(property.Name); var sha = property.Value.GetString()!; Sha(sha);
                Require(result.TryAdd(name, sha), "Duplicate/case-colliding source notice path.");
            }
            return result;
        }

        private Dictionary<string, long> Counts(JsonElement owner, string name, long expected, HashSet<string> permitted)
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal); long total = 0;
            foreach (var property in Object(owner, name).EnumerateObject())
            {
                budget.Check(); Require(result.Count < 128 && permitted.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var count) && count is >= 1 and <= 50_000_000,
                    "Training-by-source integer/source bound differs.");
                var value = property.Value.GetInt64(); Require(result.TryAdd(property.Name, value), "Duplicate training source."); total = checked(total + value);
            }
            Require(result.Count > 0 && total == expected, "Training-by-source full denominator differs."); return result;
        }

        private string BoundModelAsset(JsonElement model, string pathField, string hashField, Dictionary<string, InputFile> assets)
        {
            var path = SafePaths.Relative(Text(model, pathField, 1024)); var sha = HashField(model, hashField);
            Require(assets.TryGetValue(path, out var asset) && asset.Sha256 == sha && asset.Bytes > 0, "Model encoder/tokenizer missing from complete bound base: " + pathField);
            return sha;
        }

        private InputFile Add(string path, string sha, long? bytes = null)
        {
            budget.Check(); Sha(sha); path = Absolute(path); SafePaths.NoLinks(path, budget);
            if (files.TryGetValue(path, out var existing))
            {
                Require(SamePath(existing.Path, path) && existing.Sha256 == sha && (bytes is null || bytes.Value == existing.Bytes), "Conflicting input path binding: " + path);
                return existing;
            }
            var length = bytes ?? new FileInfo(path).Length;
            Require(length is >= 0 and <= MaximumFileBytes && files.Count < request.MaximumFiles && checked(plannedBytes + length) <= request.MaximumBytes,
                "Input file/count/total byte bound exceeded: " + path);
            var entry = new InputFile(path, length, sha); files.Add(path, entry); plannedBytes += length; return entry;
        }

        private JsonElement Read(string path, string sha, string label)
        {
            var file = Add(path, sha); Require(file.Bytes <= MaximumMetadataBytes && checked(metadataBytes + file.Bytes) <= 128L * 1024 * 1024,
                "Input metadata bound16MiB each/128MiB total exceeded: " + label);
            metadataBytes += file.Bytes;
            RegularFiles.Require(file.Path, budget);
            var before = new FileInfo(file.Path); var modified = before.LastWriteTimeUtc;
            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(stream.Length == file.Bytes, "Metadata byte denominator changed: " + label);
            var bytes = new byte[(int)file.Bytes]; var offset = 0;
            for (var block = 0; block <= MaximumMetadataBytes / 1_048_576; block++)
            {
                budget.Check();
                if (offset == bytes.Length) break;
                var count = stream.Read(bytes, offset, Math.Min(1_048_576, bytes.Length - offset));
                Require(count > 0, "Metadata ended before its byte denominator: " + label); offset += count;
            }
            Require(offset == bytes.Length && stream.ReadByte() == -1 && Convert.ToHexStringLower(SHA256.HashData(bytes)) == sha,
                "Metadata actual bytes/SHA differ: " + label);
            var after = new FileInfo(file.Path);
            Require(after.Length == file.Bytes && after.LastWriteTimeUtc == modified, "Metadata changed while reading: " + label);
            Mark(file);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48, AllowDuplicateProperties = false });
            Require(document.RootElement.ValueKind == JsonValueKind.Object, "Metadata root must be an object: " + label);
            budget.Check(); return document.RootElement.Clone();
        }

        private void Verify(InputFile file)
        {
            RegularFiles.Require(file.Path, budget); SafePaths.NoLinks(file.Path, budget);
            var before = new FileInfo(file.Path); var modified = before.LastWriteTimeUtc;
            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(stream.Length == file.Bytes && budget.Hash(stream, file.Bytes) == file.Sha256, "Input actual bytes/SHA differ: " + file.Path);
            var after = new FileInfo(file.Path);
            Require(after.Length == file.Bytes && after.LastWriteTimeUtc == modified, "Input changed while hashing: " + file.Path); Mark(file);
        }

        private void Mark(InputFile file)
        {
            if (file.Verified) return;
            file.Verified = true; verifiedFiles++; verifiedBytes = checked(verifiedBytes + file.Bytes);
        }

        private long Ledger(string path)
        {
            RegularFiles.Require(path, budget);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index <= 1_000_000; index++)
            {
                budget.Check(); var line = CorpusJson.ReadLine(reader, 8192, budget); if (line is null) return identifiers.Count;
                Require(index < 1_000_000, "Exclusion identity bound1M exceeded.");
                using var row = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false });
                Require(identifiers.Add(Text(row.RootElement, "record_id", 8192)), "Duplicate actual exclusion record identity.");
                _ = Text(row.RootElement, "reason", 8192);
            }
            throw new InvalidDataException("Exclusion ledger iteration bound reached.");
        }

        private string Root(string path)
        {
            var root = Absolute(path); SafePaths.NoLinks(root, budget); Require(Directory.Exists(root), "Input directory absent: " + root); return root;
        }
        private string Within(string root, string relative) => SafePaths.Within(root, relative, budget);
        private static string Absolute(string path)
        {
            Require(!string.IsNullOrWhiteSpace(path) && path.Length <= 4096, "Absolute input path missing/too long.");
            Require(Path.IsPathFullyQualified(path) && !path.StartsWith("\\\\", StringComparison.Ordinal) && !path.Any(char.IsControl),
                "Fully qualified local absolute input path without control characters required.");
            return Path.GetFullPath(path);
        }
        private static bool SamePath(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        private static string Text(JsonElement owner, string name, int maximum = 4096)
        {
            Require(owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String,
                "Required string field differs: " + name);
            var result = owner.GetProperty(name).GetString()!;
            Require(!string.IsNullOrWhiteSpace(result) && result.Length <= maximum, "String field bound differs: " + name); return result;
        }
        private static string? OptionalText(JsonElement owner, string name) => owner.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? Text(owner, name) : null;
        private static string HashField(JsonElement owner, string name) { var sha = Text(owner, name, 64); Sha(sha); return sha; }
        private static long Integer(JsonElement owner, string name, long minimum, long maximum)
        {
            Require(owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= minimum && number <= maximum,
                "Integer field/type/bound differs: " + name); return owner.GetProperty(name).GetInt64();
        }
        private static long OptionalInteger(JsonElement owner, string name, long minimum, long maximum) => owner.TryGetProperty(name, out _) ? Integer(owner, name, minimum, maximum) : 0;
        private static bool Flag(JsonElement owner, string name, bool optional = true)
        {
            if (!owner.TryGetProperty(name, out var value)) { Require(optional, "Required boolean field absent: " + name); return false; }
            Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False, "Boolean field type differs: " + name); return value.GetBoolean();
        }
        private static JsonElement Object(JsonElement owner, string name)
        {
            Require(owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object, "Required object differs: " + name); return value;
        }
        private static JsonElement Array(JsonElement owner, string name, int minimum, int maximum)
        {
            Require(owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() >= minimum && value.GetArrayLength() <= maximum,
                "Required array/count differs: " + name); return value;
        }
    }

    private static void Sha(string sha)
    {
        Require(!string.IsNullOrEmpty(sha), "Pinned lowercase SHA256 is required.");
        TaskOperations.Sha(sha);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
