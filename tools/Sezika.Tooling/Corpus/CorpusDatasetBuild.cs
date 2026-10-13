using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Sezika.Tooling.CorpusJson;

namespace Sezika.Tooling;

internal static partial class CorpusOperations
{
    internal static CorpusBinding Bind(CorpusBindRequest request, Budget budget)
    {
        var input = Frozen(new(request.Root, request.ManifestSha256), budget);
        var binding = new CorpusBinding(input.Hash, FileHash(Path.Combine(input.Root, "exclusions.jsonl"), budget), FileHash(Path.Combine(input.Root, "source-rights.json"), budget),
            FileHash(Path.Combine(input.Root, "source-assets.json"), budget), "source-assets.json", Path.GetFullPath(request.SourceRoot));
        Write(request.Output, binding, CorpusJsonContext.Default.CorpusBinding, budget); return binding;
    }
    internal static CorpusOperationReport BuildDataset(CorpusDatasetBuildRequest request, Budget budget)
    {
        Require(request.MaximumRecords is >= 1 and <= 1024 && request.Mode is "pilot" or "prompt-v2" or "review", "Dataset mode/item bounds.");
        var root = Path.GetFullPath(request.SourceRoot);
        var manifestPath = SafePaths.Within(root, request.ManifestFile ?? (request.Mode == "review" && !File.Exists(Path.Combine(root, "manifest.json")) ? "report.json" : "manifest.json"), budget);
        var candidatesPath = SafePaths.Within(root, request.RecordsFile ?? (request.Mode == "review" ? File.Exists(Path.Combine(root, "records.jsonl")) ? "records.jsonl" : "teacher-records.jsonl" : "candidates.jsonl"), budget);
        Require(IsSha(request.ManifestSha256) && IsSha(request.CandidatesSha256) && FileHash(manifestPath, budget) == request.ManifestSha256 && FileHash(candidatesPath, budget) == request.CandidatesSha256, "Pinned dataset inputs differ.");
        var manifest = Read(manifestPath, budget); var rows = Lines(candidatesPath, request.MaximumRecords, budget).ToArray(); Require(rows.Length > 0, "Dataset has no candidates.");
        var generatedAssets = new List<CorpusDatasetAsset>();
        var output = NewOutput(request.Output, budget); var provenance = Path.Combine(output, "provenance"); Directory.CreateDirectory(provenance); File.Copy(manifestPath, Path.Combine(provenance, "source-manifest.json")); File.Copy(candidatesPath, Path.Combine(provenance, "source-candidates.jsonl"));
        using var candidates = Writer(Path.Combine(output, "candidates.jsonl")); using var review = request.Mode == "review" ? Writer(Path.Combine(output, "review-template.jsonl")) : null;
        var reviewText = new StringBuilder("# 教师候选人工审核\n\n原始请求与响应仅作为证据。未审核的数据不获得训练准入。\n\n");
        var copied = new Dictionary<string, (string Hash, long Bytes)>(StringComparer.Ordinal); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            budget.Check(); var id = Optional(row, "record_id") ?? Optional(row, "RecordId") ?? (row.TryGetProperty("Question", out var originalQuestion) ? Optional(originalQuestion, "RecordId") : null) ?? throw new InvalidDataException("Candidate record ID missing."); Require(ids.Add(id), "Repeated dataset candidate ID.");
            if (request.Mode == "review")
            {
                ArchiveReferences(row, root, output, copied, budget);
                if (row.TryGetProperty("Question", out _))
                {
                    foreach (var evidence in new[] { ("request.json", "RequestSha256"), ("response.json", "ResponseSha256") })
                    {
                        var relative = "items/" + Sha(id) + "/" + evidence.Item1; var path = SafePaths.Within(root, relative, budget); Require(new FileInfo(path).Length <= 2 * 1024 * 1024 && FileHash(path, budget) == Str(row, evidence.Item2), "Frozen corpus teacher raw evidence SHA differs.");
                        var asset = new CorpusDatasetAsset(relative, FileHash(path, budget), new FileInfo(path).Length); var json = JsonSerializer.SerializeToElement(asset, CorpusJsonContext.Default.CorpusDatasetAsset); ArchiveReferences(json, root, output, copied, budget);
                    }
                }
                reviewText.Append("## ").Append(id).Append("\n\n").Append("~~~~json\n").Append(Canonical(row).Replace("~~~~", "~ ~ ~ ~", StringComparison.Ordinal)).Append("\n~~~~\n\n");
                review!.WriteLine("{\"record_id\":" + JsonSerializer.Serialize(id, CorpusJsonContext.Default.String) + ",\"review_status\":\"pending\",\"reviewer\":null,\"decision\":null,\"training_admission\":false}"); candidates.WriteLine(Canonical(row)); continue;
            }
            string requestRelative; byte[] bytes;
            if (row.TryGetProperty("request_path", out var pathValue))
            {
                requestRelative = Text(pathValue); var path = SafePaths.Within(root, requestRelative, budget); bytes = File.ReadAllBytes(path);
                Require(bytes.Length <= 131_072 && Sha(bytes) == Str(row, "request_sha256"), "Original decision request SHA differs.");
            }
            else if (request.Mode == "pilot" && row.TryGetProperty("state", out var state) && row.TryGetProperty("question", out var question))
            {
                requestRelative = "requests/" + SafePaths.Relative(id) + ".json";
                using var memory = new MemoryStream(); using (var writer = new Utf8JsonWriter(memory)) { writer.WriteStartObject(); writer.WriteNumber("schema_version", 1); writer.WriteString("model_id", Optional(manifest, "student_model_id") ?? throw new InvalidDataException("Student model ID missing.")); writer.WritePropertyName("state"); state.WriteTo(writer); writer.WritePropertyName("questions"); writer.WriteStartArray(); question.WriteTo(writer); writer.WriteEndArray(); writer.WriteEndObject(); } bytes = memory.ToArray();
            }
            else throw new InvalidDataException("Dataset requires a frozen decision request or pilot draft.");
            using var decision = JsonDocument.Parse(bytes); var questions = decision.RootElement.GetProperty("questions"); Require(questions.GetArrayLength() == 1, "One decision per pilot record required.");
            var q = questions[0]; var type = Str(q, "type");
            var candidateIds = type switch { "boolean" => new[] { "false", "true" }, "score" => Enumerable.Range(0, q.GetProperty("levels").GetArrayLength()).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(), "choice" => q.GetProperty("candidates").EnumerateArray().Select(c => Str(c, "id")).ToArray(), _ => throw new InvalidDataException("Decision type unsupported.") };
            Require(candidateIds.Length is >= 2 and <= 32 && candidateIds.Distinct(StringComparer.Ordinal).Count() == candidateIds.Length, "Decision candidate IDs/count.");
            var requestDestination = SafePaths.Within(output, requestRelative, budget); Directory.CreateDirectory(Path.GetDirectoryName(requestDestination)!); using (var file = new FileStream(requestDestination, FileMode.CreateNew)) { file.Write(bytes); file.Flush(true); }
            var promptRelative = Optional(row, "prompt_path") ?? "prompts/" + SafePaths.Relative(id) + ".txt"; var promptDestination = SafePaths.Within(output, promptRelative, budget); Directory.CreateDirectory(Path.GetDirectoryName(promptDestination)!);
            var prompt = "You are an offline teacher answering an unreviewed finite decision. Use only supplied state, instruction and candidate definitions. No tools or external facts. The request model_id identifies the student, not the teacher.\n" +
                "Candidate IDs in original order: " + JsonSerializer.Serialize(candidateIds, CorpusJsonContext.Default.StringArray) + ". selected_candidate_id must be a JSON string for every answered Choice, Score and Boolean; never a number or Boolean. Score IDs are zero-based level strings. Boolean IDs are strings false/true.\n" +
                "Return exactly three fields: question_id (same string as request), status (answered or abstained), selected_candidate_id (one allowed string when answered; null when abstained). No fences, explanation, probability, accuracy or confidence claims. The answer needs separate human review before training.\ndecision-v1 request:\n" + Utf8.GetString(bytes) + "\n";
            File.WriteAllText(promptDestination, prompt, Utf8); Require(FileHash(requestDestination, budget) == Sha(bytes), "Generated request independent reread differs.");
            generatedAssets.Add(new(requestRelative, Sha(bytes), bytes.Length)); generatedAssets.Add(new(promptRelative, FileHash(promptDestination, budget), new FileInfo(promptDestination).Length));
            using var expanded = new MemoryStream(); using (var writer = new Utf8JsonWriter(expanded))
            {
                writer.WriteStartObject(); foreach (var p in row.EnumerateObject()) if (p.Name is not "prompt_sha256" and not "prompt_bytes" and not "request_path" and not "request_sha256" and not "request_bytes" and not "prompt_path") p.WriteTo(writer);
                writer.WriteString("request_path", requestRelative); writer.WriteString("request_sha256", Sha(bytes)); writer.WriteNumber("request_bytes", bytes.Length); writer.WriteString("prompt_path", promptRelative); writer.WriteString("prompt_sha256", FileHash(promptDestination, budget)); writer.WriteNumber("prompt_bytes", new FileInfo(promptDestination).Length); writer.WriteString("source_manifest_sha256", request.ManifestSha256); writer.WriteEndObject();
            }
            using var expandedJson = JsonDocument.Parse(expanded.ToArray()); candidates.WriteLine(Canonical(expandedJson.RootElement));
        }
        candidates.Flush(); review?.Flush(); candidates.Dispose(); review?.Dispose(); if (request.Mode == "review") File.WriteAllText(Path.Combine(output, "review.md"), reviewText.ToString(), Utf8);
        Require(FileHash(manifestPath, budget) == request.ManifestSha256 && FileHash(candidatesPath, budget) == request.CandidatesSha256, "Source dataset changed during construction.");
        var report = new CorpusOperationReport("DATASET_BUILT_PENDING_HUMAN_AND_LICENSE_REVIEW", output, rows.Length, rows.Length, 0, 0, 0, false, false);
        budget.Check();
        using (var file = new FileStream(Path.Combine(output, "manifest.json"), FileMode.CreateNew))
        {
            using (var writer = new Utf8JsonWriter(file))
            {
                writer.WriteStartObject();
                foreach (var property in manifest.EnumerateObject())
                    if (property.Name is not "dataset_id" and not "total_records" and not "assets" and not "candidates" and not "generator" and not "training_admission" and not "human_review" and not "teacher_called" and not "quality_evidence" and not "prompt_contract") property.WriteTo(writer);
                writer.WriteString("dataset_id", (Optional(manifest, "dataset_id") ?? "dataset") + "-csharp-v1-" + request.Mode); writer.WriteNumber("total_records", rows.Length);
                writer.WriteString("human_review", "pending"); writer.WriteString("training_admission", "pending"); writer.WriteBoolean("teacher_called", false); writer.WriteBoolean("quality_evidence", false);
                writer.WriteString("generator", "Sezika.Tooling corpus dataset-build v1"); writer.WriteString("source_manifest_sha256", request.ManifestSha256); writer.WriteString("source_candidates_sha256", request.CandidatesSha256);
                writer.WritePropertyName("assets"); JsonSerializer.Serialize(writer, generatedAssets.ToArray(), CorpusJsonContext.Default.CorpusDatasetAssetArray);
                writer.WritePropertyName("source_evidence"); JsonSerializer.Serialize(writer, copied.Select(asset => new CorpusDatasetAsset("source-evidence/" + asset.Key, asset.Value.Hash, asset.Value.Bytes)).ToArray(), CorpusJsonContext.Default.CorpusDatasetAssetArray);
                writer.WritePropertyName("candidates"); writer.WriteStartObject(); writer.WriteString("path", "candidates.jsonl"); writer.WriteString("sha256", FileHash(Path.Combine(output, "candidates.jsonl"), budget)); writer.WriteNumber("bytes", new FileInfo(Path.Combine(output, "candidates.jsonl")).Length); writer.WriteEndObject(); writer.WriteEndObject();
            }
            file.Flush(true);
        }
        Write(Path.Combine(output, "build-receipt.json"), report, CorpusJsonContext.Default.CorpusOperationReport, budget); return report;
    }
    private static void ArchiveReferences(JsonElement value, string root, string output, Dictionary<string, (string Hash, long Bytes)> copied, Budget budget, int depth = 0)
    {
        budget.Check(); Require(depth <= 32 && copied.Count <= 4096, "Reference archive bounds.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("path", out var p) && value.TryGetProperty("sha256", out var h) && value.TryGetProperty("bytes", out var b))
            {
                var relative = Text(p); var hash = Text(h); var bytes = b.GetInt64(); Require(IsSha(hash) && bytes is >= 0 and <= 64 * 1024 * 1024, "Reference hash/bytes bound.");
                var source = SafePaths.Within(root, relative, budget); Require(new FileInfo(source).Length == bytes && FileHash(source, budget) == hash, "Original reference bytes differ.");
                if (copied.TryGetValue(relative, out var prior)) Require(prior.Hash == hash && prior.Bytes == bytes, "Conflicting same-path references.");
                else { var target = SafePaths.Within(output, "source-evidence/" + relative, budget); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target); Require(FileHash(target, budget) == hash, "Archived reference bytes differ."); copied.Add(relative, (hash, bytes)); }
            }
            var properties = value.EnumerateObject().Take(4097).ToArray(); Require(properties.Length <= 4096, "Reference property bounds."); foreach (var property in properties) ArchiveReferences(property.Value, root, output, copied, budget, depth + 1);
        }
        else if (value.ValueKind == JsonValueKind.Array) { Require(value.GetArrayLength() <= 4096, "Reference array bounds."); foreach (var item in value.EnumerateArray()) ArchiveReferences(item, root, output, copied, budget, depth + 1); }
    }
    internal static CorpusOperationReport BuildResearchPlan(CorpusResearchPlanRequest request, Budget budget)
    {
        Require(request.MaximumSources is >= 1 and <= 96 && request.MaximumFiles is >= 1 and <= 50_000, "Research plan bounds.");
        var seeds = Read(request.Seeds, budget); Require(seeds.ValueKind == JsonValueKind.Array && seeds.GetArrayLength() <= request.MaximumSources, "Source seed count.");
        var assets = new List<CorpusResearchAsset>(); var sources = new List<CorpusResearchSource>(); var observed = new HashSet<string>(StringComparer.Ordinal);
        using var nullJson = JsonDocument.Parse("null");
        foreach (var seed in seeds.EnumerateArray())
        {
            budget.Check(); var id = Str(seed, "id"); Require(id.Split('/').Length == 2 && id.Length <= 256, "Source seed identity.");
            var metadataPath = SafePaths.Within(request.MetadataRoot, "metadata/hf/" + id.Replace("/", "--", StringComparison.Ordinal) + ".json", budget);
            if (!File.Exists(metadataPath)) { sources.Add(new(id, "metadata_unavailable_401_or_missing", null, nullJson.RootElement.Clone(), "", "https://huggingface.co/datasets/" + id)); continue; }
            var metadata = Read(metadataPath, budget); var actualId = Str(metadata, "id"); if (!observed.Add(actualId)) continue;
            var revision = Str(metadata, "sha"); Require(revision.Length is >= 7 and <= 64 && revision.All(char.IsAsciiHexDigit), "Pinned source revision required.");
            var card = metadata.TryGetProperty("cardData", out var cardData) ? cardData : default; var license = card.ValueKind == JsonValueKind.Object && card.TryGetProperty("license", out var terms) ? terms.Clone() : nullJson.RootElement.Clone();
            sources.Add(new(actualId, "metadata_frozen", revision, license, FileHash(metadataPath, budget), "https://huggingface.co/datasets/" + actualId));
            var siblings = metadata.GetProperty("siblings"); Require(siblings.GetArrayLength() <= request.MaximumFiles, "Metadata sibling count.");
            foreach (var sibling in siblings.EnumerateArray())
            {
                budget.Check(); var name = Str(sibling, "rfilename"); SafePaths.Relative(name);
                if (!Regex.IsMatch(name, @"(?i)(\.(jsonl?|parquet|arrow|csv|tsv|txt|gz|zip|zst|bz2|xz|7z|tar|jsonlines|ndjson|tfrecord|md|ya?ml|py|cff)$|(^|/)(LICENSE|NOTICE|COPYING)(\.[^/]+)?$)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || Regex.IsMatch(name, @"(?i)(^|/)(predictions|results|evaluation_outputs)/", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) continue;
                var split = Regex.IsMatch(name, @"(?i)(^|[/_.-])train(ing)?([/_.-]|$)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ? "source-train" : Regex.IsMatch(name, "(?i)(test|eval)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ? "source-test-evaluation" : Regex.IsMatch(name, "(?i)(dev|valid)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ? "source-development" : "unclassified-review-required";
                var expected = sibling.TryGetProperty("lfs", out var lfs) ? Optional(lfs, "sha256") : null; long? size = sibling.TryGetProperty("size", out var sizeValue) && sizeValue.ValueKind == JsonValueKind.Number ? sizeValue.GetInt64() : null;
                Require(assets.Count < request.MaximumFiles, "Research total file bound."); assets.Add(new(actualId, revision, split, "https://huggingface.co/datasets/" + actualId + "/resolve/" + revision + "/" + name, "raw/hf/" + actualId + "/" + name, size, expected));
            }
        }
        foreach (var item in new[] { ("sys1cal-tree.json", "little-g-ai/Sys1Cal-v1", "evaluation-calibration-only", new[] { "README.md", "LICENSE", "hf/Sys1Cal-v1/README.md", "datasets/generated/v0.1.0_tiny_cleanvars_binary.jsonl", "datasets/generated/v0.1.0_tiny_cleanvars_parallel_primitives.jsonl" }), ("s1mb-tree.json", "hotchpotch/S1MB", "evaluation-only", new[] { "README.md", "LICENSE", "evaluator/dataset-source.json" }) })
        {
            var path = SafePaths.Within(request.MetadataRoot, "metadata/github/" + item.Item1, budget); if (!File.Exists(path)) continue; var tree = Read(path, budget); var entries = tree.GetProperty("tree"); Require(entries.GetArrayLength() <= 50_000, "GitHub tree count.");
            foreach (var entry in entries.EnumerateArray()) { budget.Check(); var name = Str(entry, "path"); if (!item.Item4.Contains(name, StringComparer.Ordinal)) continue; Require(assets.Count < request.MaximumFiles, "Research total file bound."); var revision = Str(tree, "sha"); assets.Add(new(item.Item2, revision, item.Item3, "https://raw.githubusercontent.com/" + item.Item2 + "/" + revision + "/" + name, "raw/github/" + item.Item2 + "/" + name, entry.GetProperty("size").GetInt64(), null)); }
        }
        var output = NewOutput(request.Output, budget); var ordered = assets.OrderBy(a => Documentation(a.Path) ? 0 : 1).ThenBy(a => a.Dataset, StringComparer.Ordinal).ThenBy(a => a.Path, StringComparer.Ordinal).ToArray();
        Write(Path.Combine(output, "source-catalog.json"), sources.ToArray(), CorpusJsonContext.Default.CorpusResearchSourceArray, budget); Write(Path.Combine(output, "raw-download-plan.json"), ordered, CorpusJsonContext.Default.CorpusResearchAssetArray, budget);
        Write(Path.Combine(output, "documentation-plan.json"), ordered.Where(a => Documentation(a.Path)).ToArray(), CorpusJsonContext.Default.CorpusResearchAssetArray, budget);
        var report = new CorpusOperationReport("RESEARCH_PLAN_FROZEN_ONLY", output, seeds.GetArrayLength(), sources.Count, sources.Count(s => s.Verification != "metadata_frozen"), 0, 0, false, false); Write(Path.Combine(output, "manifest.json"), report, CorpusJsonContext.Default.CorpusOperationReport, budget); return report;
    }
    private static bool Documentation(string name) => Regex.IsMatch(name, @"(?i)(README|SOURCES|manifest|license|\.py$|schema|task-catalog|score-policy|checksums)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
