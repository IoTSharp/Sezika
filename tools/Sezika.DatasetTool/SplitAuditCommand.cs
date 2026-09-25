using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

internal static class SplitAuditCommand
{
    private const int MaxRecords = 1000;
    private const int MaxSources = 32;
    private const int MaxFileBytes = 4 * 1024 * 1024;
    private const int MaxTextChars = 8192;
    private const int MaxNormalizedChars = 2 * 1024 * 1024;
    private const int MaxIssues = 10000;
    private const long MaxPairs = (long)MaxRecords * (MaxRecords - 1) / 2;
    private const double NearDuplicateThreshold = 0.82;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: Sezika.DatasetTool audit-splits <manifest.json> <new-report.json>");
            return 2;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        var guard = new AuditGuard(cancellation.Token);
        try
        {
            var manifestPath = Path.GetFullPath(args[1]);
            var root = Path.GetDirectoryName(manifestPath) ?? throw new InvalidDataException("Manifest has no directory.");
            var outputPath = Path.GetFullPath(args[2]);
            if (File.Exists(outputPath)) throw new IOException("Report already exists; choose a new output path.");
            var bytes = await ReadBoundedAsync(manifestPath, guard);
            var manifest = ReadJson(bytes, SplitAuditJsonContext.Default.AuditManifest);
            ValidateManifest(manifest);
            var issues = new List<AuditIssue>();
            if (manifest.Stage == DatasetStage.FixtureOnly)
                AddIssue(issues, "fixture_only", manifest.DatasetId, null, "Fixtures cannot be admitted as a real dataset.");

            var recordsBytes = await VerifyArtifactAsync(root, manifest.Records, guard);
            var sources = new Dictionary<string, AuditSource>(StringComparer.Ordinal);
            var admissions = new List<SourceAdmission>(manifest.Sources.Length);
            for (var index = 0; index < manifest.Sources.Length; index++)
            {
                guard.Check($"source {index + 1}/{manifest.Sources.Length}");
                var source = manifest.Sources[index] ?? throw new InvalidDataException("Null source.");
                ValidateSource(source);
                if (!sources.TryAdd(source.Id, source)) throw new InvalidDataException($"Duplicate source: {source.Id}");
                await VerifyArtifactAsync(root, source.Raw, guard);
                var before = issues.Count;
                if (source.LicenseReview != ReviewStatus.Approved)
                    AddIssue(issues, "license_not_approved", source.Id, null, "License review is pending or rejected.");
                else if (string.IsNullOrWhiteSpace(source.LicenseReviewer) || source.LicenseReviewedUtc is null ||
                    source.LicenseReviewedUtc > DateTimeOffset.UtcNow || source.LicenseEvidence is null)
                    AddIssue(issues, "license_evidence_missing", source.Id, null, "Approval requires reviewer, past UTC date and hash-bound evidence.");
                if (source.LicenseEvidence is not null) await VerifyArtifactAsync(root, source.LicenseEvidence, guard);
                if (!source.AllowedUses.Contains(manifest.IntendedUse))
                    AddIssue(issues, "use_not_allowed", source.Id, null, "Requested use is absent from the declared license allowlist.");
                if (IsAuditOnly(source) && source.AllowedSplits.Any(split => split != DataSplit.Audit))
                    AddIssue(issues, "viewed_source_policy", source.Id, null, "Previously viewed evaluation sources must allow only Audit.");
                admissions.Add(new SourceAdmission(source.Id, source.LicenseReview, source.AllowedUses,
                    source.AllowedSplits, source.Exposure, before == issues.Count));
            }

            var rows = ReadRecords(recordsBytes, guard);
            var byId = new Dictionary<string, AuditRecord>(StringComparer.Ordinal);
            var fingerprints = new string[rows.Count];
            var shingles = new HashSet<string>[rows.Count];
            var counts = new int[Enum.GetValues<DataSplit>().Length];
            var normalizedChars = 0;
            for (var index = 0; index < rows.Count; index++)
            {
                guard.Check($"record {index + 1}/{rows.Count}");
                var row = rows[index];
                ValidateRecord(row);
                if (!byId.TryAdd(row.Id, row)) throw new InvalidDataException($"Duplicate record: {row.Id}");
                if (!sources.TryGetValue(row.SourceId, out var source)) throw new InvalidDataException($"Unknown source: {row.SourceId}");
                counts[(int)row.Split]++;
                if (!source.AllowedSplits.Contains(row.Split))
                    AddIssue(issues, "split_not_allowed", row.Id, null, "Record split is absent from the source allowlist.");
                if ((IsAuditOnly(source) || row.Exposure == Exposure.EvaluationViewed) && row.Split != DataSplit.Audit)
                    AddIssue(issues, "viewed_evaluation_reused", row.Id, null, "Previously viewed evaluation content is audit-only, including derivatives.");
                if ((source.Exposure != Exposure.Unseen || row.Exposure != Exposure.Unseen) && row.Split == DataSplit.SealedTest)
                    AddIssue(issues, "sealed_test_exposed", row.Id, null, "Previously viewed content cannot become a fresh sealed test.");
                if (row.HumanReview != ReviewStatus.Approved || string.IsNullOrWhiteSpace(row.Reviewer) ||
                    row.ReviewedUtc is null || row.ReviewedUtc > DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(row.ReviewNote))
                    AddIssue(issues, "human_review_required", row.Id, null, "Human label/provenance/derivation review requires reviewer, past UTC date and note.");
                var normalized = Normalize(row.Text, guard);
                if (normalized.Length == 0) throw new InvalidDataException($"No letters or digits in text: {row.Id}");
                normalizedChars = checked(normalizedChars + normalized.Length);
                if (normalizedChars > MaxNormalizedChars) throw new InvalidDataException("Total normalized text limit exceeded.");
                fingerprints[index] = Hash(StrictUtf8.GetBytes(normalized));
                shingles[index] = MakeShingles(normalized, guard);
            }

            CheckDerivations(rows, byId, sources, issues, guard);
            long compared = 0;
            for (var left = 0; left < rows.Count; left++)
            {
                for (var right = left + 1; right < rows.Count; right++)
                {
                    guard.Check($"pair {++compared}/{(long)rows.Count * (rows.Count - 1) / 2}");
                    if (compared > MaxPairs) throw new InvalidDataException("Pair limit exceeded.");
                    var a = rows[left];
                    var b = rows[right];
                    if (a.Split == b.Split) continue;
                    if (a.Family == b.Family) AddIssue(issues, "family_crosses_split", a.Id, b.Id, "Family members must share a split across all languages and sources.");
                    if (a.Entities.Intersect(b.Entities, StringComparer.Ordinal).Any())
                        AddIssue(issues, "entity_crosses_split", a.Id, b.Id, "A declared entity occurs in different splits.");
                    if (fingerprints[left] == fingerprints[right])
                        AddIssue(issues, "fingerprint_crosses_split", a.Id, b.Id, "NFKC case/spacing/punctuation normalized text occurs in different splits.");
                    else if (Similarity(shingles[left], shingles[right], guard) >= NearDuplicateThreshold)
                        AddIssue(issues, "near_duplicate_crosses_split", a.Id, b.Id, "Character trigram Jaccard similarity exceeds the frozen 0.82 threshold.");
                }
            }

            if (counts[(int)DataSplit.Development] == 0 || counts[(int)DataSplit.Calibration] == 0 || counts[(int)DataSplit.SealedTest] == 0)
                AddIssue(issues, "required_split_missing", manifest.DatasetId, null, "Development, Calibration and SealedTest must each contain records.");
            if (manifest.TestSeal.Status != SealStatus.Sealed || string.IsNullOrWhiteSpace(manifest.TestSeal.Custodian) ||
                manifest.TestSeal.SealedUtc is null || manifest.TestSeal.SealedUtc > DateTimeOffset.UtcNow || manifest.TestSeal.Evidence is null)
                AddIssue(issues, "test_not_sealed", manifest.DatasetId, null, "Seal requires custodian, past UTC date and hash-bound access/freeze evidence.");
            if (manifest.TestSeal.Evidence is not null)
            {
                var sealBytes = await VerifyArtifactAsync(root, manifest.TestSeal.Evidence, guard);
                var seal = ReadJson(sealBytes, SplitAuditJsonContext.Default.SealEvidence);
                if (seal.DatasetId != manifest.DatasetId ||
                    !string.Equals(seal.RecordsSha256, manifest.Records.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    seal.Custodian != manifest.TestSeal.Custodian || seal.SealedUtc != manifest.TestSeal.SealedUtc)
                    AddIssue(issues, "seal_binding_mismatch", manifest.DatasetId, null, "Seal evidence must bind this dataset, records hash, custodian and freeze time.");
            }

            var passed = issues.Count == 0;
            var report = new AuditReport(1, manifest.DatasetId, passed ? "checks_passed_pending_human_acceptance" : "blocked",
                passed, true, false, "s4-05-admission-v1", Hash(bytes), Hash(recordsBytes),
                "nfkc-invariant-upper-letter-digit-v1+sha256; unicode-scalar-trigram-jaccard-v1", NearDuplicateThreshold,
                rows.Count, compared, Enum.GetValues<DataSplit>().Select(split => new SplitCount(split, counts[(int)split])).ToArray(),
                admissions.ToArray(), issues.ToArray(), guard.StartedUtc, guard.Elapsed.TotalSeconds);
            guard.Check("write report");
            await using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(output, report, SplitAuditJsonContext.Default.AuditReport, cancellation.Token);
            Console.WriteLine($"split audit: {report.Status}; records {rows.Count}; pairs {compared}; issues {issues.Count}; elapsed {guard.Elapsed.TotalSeconds:F1}s");
            return passed ? 0 : 3;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("split audit: cancelled or 60-second deadline exceeded; no complete audit result is claimed.");
            return 4;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"split audit: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void ValidateManifest(AuditManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || !Enum.IsDefined(manifest.Stage) || !Enum.IsDefined(manifest.IntendedUse) ||
            manifest.Sources is null || manifest.Sources.Length is < 1 or > MaxSources || manifest.Records is null ||
            manifest.TestSeal is null || !Enum.IsDefined(manifest.TestSeal.Status))
            throw new InvalidDataException("Invalid manifest schema, enum, source count or seal.");
        RequireKey(manifest.DatasetId, "dataset_id");
    }

    private static void ValidateSource(AuditSource source)
    {
        RequireKey(source.Id, "source.id");
        RequireText(source.UpstreamId, "upstream_id", 512);
        RequireText(source.UpstreamSplit, "upstream_split", 128);
        RequireText(source.Revision, "revision", 256);
        RequireText(source.LicenseExpression, "license_expression", 256);
        if (!Enum.IsDefined(source.Origin) || !Enum.IsDefined(source.Exposure) || !Enum.IsDefined(source.LicenseReview) ||
            source.Raw is null || source.AllowedUses is null || source.AllowedUses.Length > 3 ||
            source.AllowedUses.Any(value => !Enum.IsDefined(value)) || source.AllowedSplits is null ||
            source.AllowedSplits.Length > 5 || source.AllowedSplits.Any(value => !Enum.IsDefined(value)))
            throw new InvalidDataException($"Invalid source policy: {source.Id}");
    }

    private static void ValidateRecord(AuditRecord row)
    {
        RequireKey(row.Id, "record.id");
        RequireKey(row.SourceId, "source_id");
        RequireKey(row.Family, "family");
        RequireText(row.Language, "language", 64);
        RequireText(row.Domain, "domain", 128);
        RequireText(row.Text, "text", MaxTextChars);
        if (!Enum.IsDefined(row.Split) || !Enum.IsDefined(row.Derivation) || !Enum.IsDefined(row.HumanReview) ||
            !Enum.IsDefined(row.Exposure) || row.Entities is null || row.Entities.Length is < 1 or > 32)
            throw new InvalidDataException($"Invalid row policy: {row.Id}");
        // At most 32 entities; this bounded projection has no I/O or nested unbounded iteration.
        if (row.Entities.Any(entity => string.IsNullOrWhiteSpace(entity) || entity.Length > 128 || entity != entity.Trim()))
            throw new InvalidDataException($"Invalid entity: {row.Id}");
        if (row.Derivation == Derivation.Original && row.ParentId is not null ||
            row.Derivation != Derivation.Original && string.IsNullOrWhiteSpace(row.ParentId))
            throw new InvalidDataException($"Derivation/parent mismatch: {row.Id}");
    }

    private static void CheckDerivations(List<AuditRecord> rows, Dictionary<string, AuditRecord> byId,
        Dictionary<string, AuditSource> sources, List<AuditIssue> issues, AuditGuard guard)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            guard.Check($"lineage {index + 1}/{rows.Count}");
            var row = rows[index];
            var current = row;
            var ancestors = new HashSet<string>(StringComparer.Ordinal) { row.Id };
            for (var depth = 0; depth < rows.Count && current.ParentId is not null; depth++)
            {
                guard.Check($"lineage {index + 1}/{rows.Count}, depth {depth + 1}");
                if (!byId.TryGetValue(current.ParentId, out var parent))
                {
                    AddIssue(issues, "parent_missing", row.Id, current.ParentId, "Include every derivation ancestor in the audit inventory.");
                    break;
                }
                if (!ancestors.Add(parent.Id))
                {
                    AddIssue(issues, "parent_cycle", row.Id, parent.Id, "Derivation ancestry contains a cycle.");
                    break;
                }
                if (row.Family != parent.Family || row.Split != parent.Split)
                    AddIssue(issues, "derivation_isolation", row.Id, parent.Id, "Translations, paraphrases and counterfactuals inherit family and split.");
                if ((IsAuditOnly(sources[parent.SourceId]) || parent.Exposure == Exposure.EvaluationViewed) && row.Split != DataSplit.Audit)
                    AddIssue(issues, "viewed_ancestor_reused", row.Id, parent.Id, "Derivatives of viewed evaluations remain audit-only.");
                if ((sources[parent.SourceId].Exposure != Exposure.Unseen || parent.Exposure != Exposure.Unseen) && row.Split == DataSplit.SealedTest)
                    AddIssue(issues, "viewed_ancestor_in_test", row.Id, parent.Id, "Derivatives of viewed content cannot enter fresh sealed tests.");
                current = parent;
            }
        }
    }

    private static bool IsAuditOnly(AuditSource source) => source.Exposure == Exposure.EvaluationViewed ||
        source.Origin is SourceOrigin.PawsViewedTest250 or SourceOrigin.NimbleViewedEval324 ||
        // The reviewed 250/324 inputs remain audit-only if renamed; other source splits require their own review.
        source.Raw.Sha256.Equals("c295258fdd73452f196b2673bdbee58cf01fa3f24400c2f1c0f26fd1126bc6f3", StringComparison.OrdinalIgnoreCase) ||
        source.Raw.Sha256.Equals("8e9e48b8de5206593912ae01ddc95bd77e40ad2ecf4c9292c1711290eca0d896", StringComparison.OrdinalIgnoreCase);

    private static List<AuditRecord> ReadRecords(byte[] bytes, AuditGuard guard)
    {
        var records = new List<AuditRecord>();
        using var reader = new StringReader(StrictUtf8.GetString(bytes));
        // Every physical line counts, including blank lines, so whitespace cannot evade the bound.
        for (var lineNumber = 1; lineNumber <= MaxRecords + 1; lineNumber++)
        {
            guard.Check($"read line {lineNumber}/{MaxRecords}");
            var line = reader.ReadLine();
            if (line is null) break;
            if (lineNumber > MaxRecords) throw new InvalidDataException("Record/line limit exceeded.");
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("Blank JSONL lines are not allowed.");
            records.Add(ReadJson(StrictUtf8.GetBytes(line), SplitAuditJsonContext.Default.AuditRecord));
        }
        if (records.Count == 0) throw new InvalidDataException("Empty records file.");
        return records;
    }

    private static T ReadJson<T>(byte[] bytes, JsonTypeInfo<T> typeInfo)
    {
        // .NET 10 rejects duplicate members before source-generated deserialization; unknown members also fail.
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 16 });
        return document.Deserialize(typeInfo) ?? throw new InvalidDataException("JSON null is not allowed.");
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, AuditGuard guard)
    {
        guard.Check("read artifact");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is < 1 or > MaxFileBytes) throw new InvalidDataException("Artifact must be between 1 byte and 4 MiB.");
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, guard.Token);
        guard.Check("artifact loaded");
        return bytes;
    }

    private static async Task<byte[]> VerifyArtifactAsync(string root, ArtifactReference artifact, AuditGuard guard)
    {
        if (artifact is null || string.IsNullOrWhiteSpace(artifact.Path) || Path.IsPathRooted(artifact.Path) ||
            string.IsNullOrWhiteSpace(artifact.Sha256) || artifact.Sha256.Length != 64 || !artifact.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Artifact requires a relative path and a 64-digit SHA-256.");
        var path = Path.GetFullPath(artifact.Path, root);
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Artifact path escapes the manifest directory.");
        var bytes = await ReadBoundedAsync(path, guard);
        if (!Hash(bytes).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Artifact SHA-256 mismatch: {artifact.Path}");
        return bytes;
    }

    private static string Normalize(string text, AuditGuard guard)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        var builder = new StringBuilder(normalized.Length);
        var visited = 0;
        foreach (var rune in normalized.EnumerateRunes())
        {
            guard.Check("normalize text");
            if (++visited > MaxTextChars * 18) throw new InvalidDataException("Normalized text bound exceeded.");
            if (Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    private static HashSet<string> MakeShingles(string text, AuditGuard guard)
    {
        var runes = text.EnumerateRunes().ToArray();
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (runes.Length < 3) result.Add(text);
        for (var index = 0; index + 2 < runes.Length; index++)
        {
            guard.Check("text trigrams");
            result.Add(string.Concat(runes[index].ToString(), runes[index + 1].ToString(), runes[index + 2].ToString()));
        }
        return result;
    }

    private static double Similarity(HashSet<string> left, HashSet<string> right, AuditGuard guard)
    {
        var intersection = 0;
        var visited = 0;
        foreach (var shingle in left)
        {
            guard.Check("compare trigrams");
            if (++visited > MaxTextChars * 18) throw new InvalidDataException("Shingle limit exceeded.");
            if (right.Contains(shingle)) intersection++;
        }
        return (double)intersection / (left.Count + right.Count - intersection);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void RequireKey(string? value, string name)
    {
        RequireText(value, name, 128);
        if (value != value!.Trim()) throw new InvalidDataException($"Whitespace around {name}.");
    }

    private static void RequireText(string? value, string name, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) throw new InvalidDataException($"Invalid {name}.");
    }

    private static void AddIssue(List<AuditIssue> issues, string code, string subject, string? related, string detail)
    {
        if (issues.Count >= MaxIssues) throw new InvalidDataException("Issue limit exceeded; no complete audit result is claimed.");
        issues.Add(new AuditIssue(code, subject, related, detail));
    }

    private sealed class AuditGuard(CancellationToken token)
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private TimeSpan lastProgress = TimeSpan.FromSeconds(-1);
        internal CancellationToken Token { get; } = token;
        internal DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
        internal TimeSpan Elapsed => watch.Elapsed;

        internal void Check(string progress)
        {
            Token.ThrowIfCancellationRequested();
            if (watch.Elapsed >= TimeSpan.FromSeconds(60)) throw new OperationCanceledException("Audit deadline exceeded.", Token);
            if (watch.Elapsed - lastProgress < TimeSpan.FromSeconds(1)) return;
            lastProgress = watch.Elapsed;
            Console.Error.WriteLine($"split audit [{watch.Elapsed.TotalSeconds:F1}s/60s]: {progress}");
        }
    }
}
