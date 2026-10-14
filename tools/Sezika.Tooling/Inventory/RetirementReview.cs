using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record ReviewFilePin(string Path, long Bytes, string Sha256);
internal sealed record RetirementCaller(ReviewFilePin File, string Kind);
internal sealed record ReplacementGate(string Id, string WorkPackage, string[] OriginalPaths, string CSharpEntry, ReviewFilePin[] Sources,
    ReviewFilePin Receipt, string ExpectedReceiptStatus, bool IndependentlyVerified, bool ProjectExternalDependencyReleased = false);
internal sealed record RetirementReviewedLine(string CallerPath, string CallerSha256, int Line, string LineSha256,
    string Category, string Reason, bool IndependentlyVerified);
internal sealed record RetirementArchiveDecision(ReviewFilePin Original, string Purpose, string Reason, bool IndependentlyVerified);
internal sealed record CurrentEvidenceRetention(ReviewFilePin Current, string Purpose, string Reason,
    bool IndependentlyVerified, bool NoCurrentOperationalResponsibility);
internal sealed record RetirementReviewRequest(string ClassificationPath, string ClassificationSha256, string SourceRoot, ReplacementGate[] Gates,
    RetirementCaller[] CurrentCallers, bool CallerScopeDeclaredComplete, int ExpectedEntries = 466,
    RetirementReviewedLine[]? ReviewedNonExecutionLines = null, RetirementArchiveDecision[]? HistoricalArchiveDecisions = null,
    CurrentEvidenceRetention[]? CurrentEvidenceRetentions = null);
internal sealed record RetirementSnapshot(string Path, string Status, long? Bytes, string? Sha256, string? Error);
internal sealed record RetirementCallerResult(string Path, string Kind, string Status, long? Bytes, string? Sha256);
internal sealed record RetirementGateResult(string Id, string Status, string CSharpEntry, string[] Reasons, RetirementSnapshot[] Sources, RetirementSnapshot Receipt);
internal sealed record RetirementCallerFinding(string OriginalPath, string CallerPath, int Line, string State, string Token, string Reason);
internal sealed record RetirementItem(string Path, string Tracking, string WorkPackage, string Disposition, string Replacement, string OriginalStatus,
    RetirementSnapshot Current, string ReviewState, string? GateId, int ExecutionReferences, int AmbiguousReferences, string[] Reasons, bool ProjectExternalDependencyReleased)
{
    public string ReviewBasis { get; init; } = "CAPABILITY_REPLACEMENT";
}
internal sealed record RetirementGateGap(string Path, string WorkPackage, string Replacement, string Reason);
internal sealed record RetirementReport(string Status, string ClassificationSha256, string SourceInventorySha256, string SourceRoot, int Entries,
    int Ready, int Pending, int ReviewRequired, int Unknown, long HashedBytes, bool CallerScopeDeclaredComplete, RetirementItem[] Items,
    RetirementGateResult[] Gates, RetirementCallerResult[] Callers, RetirementCallerFinding[] Findings, RetirementGateGap[] MissingGates,
    bool RetirementPerformed = false, bool OriginalScriptsExecuted = false, bool SharedSkillsChanged = false, string Scope = "supplied_current_callers_and_explicitly_verified_gates_only")
{
    public RetirementReviewedLine[] ReviewedNonExecutionLines { get; init; } = [];
    public RetirementArchiveDecision[] HistoricalArchiveDecisions { get; init; } = [];
    public CurrentEvidenceRetention[] CurrentEvidenceRetentions { get; init; } = [];
    public int RetainedUnknownHistory { get; init; }
    public int HistoricalBaselineUnknown { get; init; }
}

internal static class RetirementReview
{
    internal const int MaximumEntries = 512;
    internal const int MaximumCallers = 128;
    internal const long MaximumCallerBytes = 16L * 1024 * 1024;
    internal const long MaximumHashedBytes = 2L * 1024 * 1024 * 1024;
    private sealed record CallerLine(string Path, int Number, string Kind, string Text, string? Exclusion, string Sha256, bool MixedExecution);

    internal static RetirementReport Review(RetirementReviewRequest request, Budget budget)
    {
        var clock = Stopwatch.StartNew(); long hashed = 0; var findingsCount = 0;
        void Check() { budget.Check(); if (clock.Elapsed.TotalSeconds >= 180) throw new TimeoutException("Retirement review wall bound180s."); }
        if (request.ExpectedEntries is < 1 or > MaximumEntries || request.Gates.Length > 128 || request.CurrentCallers.Length > MaximumCallers ||
            request.ReviewedNonExecutionLines?.Length > 1024 || request.HistoricalArchiveDecisions?.Length > MaximumEntries ||
            request.CurrentEvidenceRetentions?.Length > MaximumEntries ||
            !Path.IsPathFullyQualified(request.SourceRoot)) throw new InvalidDataException("Retirement input item/gate/caller/root bounds differ.");
        var root = Path.GetFullPath(request.SourceRoot); SafePaths.NoLinks(root, budget);
        if (!Directory.Exists(root) || root.StartsWith("\\\\", StringComparison.Ordinal)) throw new InvalidDataException("Explicit local SourceRoot required.");
        string Resolve(string path, bool external = false)
        {
            if (external)
            {
                if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)) throw new InvalidDataException("Explicit local external dependency path required.");
                var full = Path.GetFullPath(path); SafePaths.NoLinks(full, budget); return full;
            }
            if (Path.IsPathFullyQualified(path))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/'); return SafePaths.Within(root, relative, budget);
            }
            return SafePaths.Within(root, path, budget);
        }
        string Hash(Stream stream, long bytes)
        {
            Check();
            if (bytes < 0 || bytes > MaximumHashedBytes - hashed) throw new InvalidDataException("Retirement total hash byte bound2GiB.");
            hashed += bytes; return budget.Hash(stream, bytes);
        }
        RetirementSnapshot Snapshot(string path, bool external = false)
        {
            Check();
            try
            {
                var full = Resolve(path, external);
                if (!Path.Exists(full)) return new(path, "NOT_FOUND", null, null, null);
                RegularFiles.Require(full, budget); using var stream = File.OpenRead(full);
                var bytes = stream.Length; return new(path, "PRESENT", bytes, Hash(stream, bytes), null);
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return new(path, "NOT_FOUND", null, null, error.GetType().Name); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { return new(path, "UNREADABLE_OR_UNSAFE", null, null, error.GetType().Name); }
        }
        bool Matches(RetirementSnapshot actual, ReviewFilePin pin) => actual.Status == "PRESENT" && actual.Bytes == pin.Bytes && actual.Sha256 == pin.Sha256;
        void Pin(ReviewFilePin pin)
        {
            Check(); TaskOperations.Sha(pin.Sha256);
            if (pin.Bytes < 0 || pin.Bytes > MaximumHashedBytes || pin.Path.Length is < 1 or > 2048) throw new InvalidDataException("Retirement file pin bounds differ.");
        }
        TaskOperations.Sha(request.ClassificationSha256);
        var classificationPath = Resolve(request.ClassificationPath);
        RegularFiles.Require(classificationPath, budget);
        using var classificationStream = File.OpenRead(classificationPath);
        if (classificationStream.Length is < 1 or > 4 * 1024 * 1024 || Hash(classificationStream, classificationStream.Length) != request.ClassificationSha256) throw new InvalidDataException("Frozen classification bytes/SHA differ.");
        classificationStream.Position = 0;
        var inventory = JsonSerializer.Deserialize(classificationStream, RetirementJson.Default.MigrationInventory) ?? throw new InvalidDataException("Frozen classification missing.");
        if (inventory.Status != "PASS_classification_only" || inventory.Entries != request.ExpectedEntries || inventory.Assignments.Length != inventory.Entries || inventory.Entries > MaximumEntries || inventory.ImplementationsAccepted) throw new InvalidDataException("Classification denominator/status differs; mapping is not implementation acceptance.");
        var classified = new Dictionary<string, MigrationEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in inventory.Assignments)
        {
            Check();
            if (entry.Path.Length is < 1 or > 2048 || entry.WorkPackage.Length is < 1 or > 64 || entry.Disposition.Length is < 1 or > 256 || entry.Replacement.Length is < 1 or > 256 || entry.Tracking is not ("tracked" or "ignored-targeted-sample" or "external-skill" or "untracked-existing") || !classified.TryAdd(entry.Path, entry)) throw new InvalidDataException("Frozen classification item or unique path differs.");
        }
        if (inventory.Tracked != inventory.Assignments.Count(entry => entry.Tracking == "tracked") || inventory.IgnoredSamples != inventory.Assignments.Count(entry => entry.Tracking == "ignored-targeted-sample") || inventory.ExternalDependencies != inventory.Assignments.Count(entry => entry.Tracking == "external-skill") || inventory.UserEvidence != inventory.Assignments.Count(entry => entry.Tracking == "untracked-existing")) throw new InvalidDataException("Frozen tracking denominators differ.");

        // A named historical recipe can be retired by an explicit owner
        // disposition without claiming a replacement reproduces its semantics.
        var archives = new Dictionary<string, RetirementArchiveDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (var decision in request.HistoricalArchiveDecisions ?? [])
        {
            Check(); Pin(decision.Original);
            if (!decision.IndependentlyVerified || !DecisionText(decision.Purpose) || !DecisionText(decision.Reason) ||
                !classified.TryGetValue(decision.Original.Path, out var entry) || entry.Tracking is not ("tracked" or "ignored-targeted-sample") ||
                !archives.TryAdd(decision.Original.Path, decision) || !Matches(Snapshot(decision.Original.Path), decision.Original))
                throw new InvalidDataException("Historical archive decision requires one independently reviewed, present, exactly pinned local original and its specific purpose/reason.");
        }

        // Existing user evidence without a trusted historical baseline stays
        // distinct from both archive retirement and capability replacement.
        var retentions = new Dictionary<string, CurrentEvidenceRetention>(StringComparer.OrdinalIgnoreCase);
        foreach (var retention in request.CurrentEvidenceRetentions ?? [])
        {
            Check();
            if (retention is null || retention.Current is null) throw new InvalidDataException("Current evidence retention is incomplete.");
            Pin(retention.Current);
            if (!retention.IndependentlyVerified || !retention.NoCurrentOperationalResponsibility ||
                !DecisionText(retention.Purpose) || !DecisionText(retention.Reason) ||
                !classified.TryGetValue(retention.Current.Path, out var entry) || entry.Tracking != "untracked-existing" ||
                archives.ContainsKey(retention.Current.Path) || !retentions.TryAdd(retention.Current.Path, retention) ||
                !Matches(Snapshot(retention.Current.Path), retention.Current))
                throw new InvalidDataException("Current evidence retention requires exact present user evidence, independent purpose/reason and no remaining operational responsibility; history remains unknown.");
        }

        var callerResults = new List<RetirementCallerResult>(); var lines = new List<CallerLine>(); var callerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long callerBytes = 0;
        foreach (var caller in request.CurrentCallers)
        {
            Check(); Pin(caller.File);
            if (caller.Kind is not ("code" or "documentation" or "configuration") || !callerNames.Add(caller.File.Path)) throw new InvalidDataException("Explicit unique current caller kind/path required.");
            callerBytes = checked(callerBytes + caller.File.Bytes); if (callerBytes > MaximumCallerBytes) throw new InvalidDataException("Current caller total byte bound16MiB.");
            var current = Snapshot(caller.File.Path); var status = Matches(current, caller.File) ? "VERIFIED" : current.Status == "PRESENT" ? "PIN_MISMATCH" : current.Status;
            callerResults.Add(new(caller.File.Path, caller.Kind, status, current.Bytes, current.Sha256));
            if (status != "VERIFIED") continue;
            using var input = new FileStream(Resolve(caller.File.Path), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length != caller.File.Bytes) throw new InvalidDataException("Current caller length changed during scan.");
            using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var historicalSection = false; var historicalDepth = 0; var fence = false; string? precedingScope = null; string? fenceScope = null;
            var eof = false;
            for (var index = 1; index <= 200001; index++)
            {
                Check(); var line = reader.ReadLine(); if (line is null) { eof = true; break; }
                if (index > 200000 || line.Length > 65536) throw new InvalidDataException("Caller line/count bound65536/200000.");
                var trimmed = line.Trim();
                if (caller.Kind == "documentation" && trimmed.StartsWith('#'))
                {
                    var depth = 0; for (; depth < trimmed.Length && trimmed[depth] == '#'; depth++) { }
                    if (Historical(trimmed) && !Execution(trimmed, caller.Kind)) { historicalSection = true; historicalDepth = depth; }
                    else if (historicalSection && depth <= historicalDepth) { historicalSection = false; historicalDepth = 0; }
                }
                if (caller.Kind == "documentation" && trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    fence = !fence; fenceScope = fence ? precedingScope ?? (historicalSection ? "HISTORICAL_REFERENCE" : null) : null; continue;
                }
                var mixed = MixedProhibitionExecution(line, caller.Kind);
                var exclusion = mixed ? null : Prohibited(line) ? "PROHIBITED_NOTICE" : Historical(line) && !Execution(line, caller.Kind) || historicalSection || fenceScope == "HISTORICAL_REFERENCE" ? "HISTORICAL_REFERENCE" : fenceScope == "PROHIBITED_NOTICE" ? "PROHIBITED_NOTICE" : null;
                if (!fence && trimmed.Length != 0) precedingScope = exclusion;
                if (HasScriptExtension(line) || SuspectedInterpreterInvocation(line, caller.Kind))
                {
                    if (lines.Count >= 8192) throw new InvalidDataException("Script-referencing caller line bound8192.");
                    lines.Add(new(caller.File.Path, index, caller.Kind, line.Replace('\\', '/'), exclusion,
                        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line))), mixed));
                }
            }
            if (!eof) throw new InvalidDataException("Caller scan iteration bound exceeded.");
            input.Position = 0; if (Hash(input, input.Length) != caller.File.Sha256) throw new InvalidDataException("Current caller changed while scanning.");
        }
        // Explicit independent reviews bind both the entire caller and the exact
        // UTF-8 line body (without its newline). They are recorded decisions, not
        // automatic C# control-flow analysis or an exemption inferred from a word.
        var reviewed = new Dictionary<string, RetirementReviewedLine>(StringComparer.OrdinalIgnoreCase);
        var callerByPath = callerResults.ToDictionary(caller => caller.Path, StringComparer.OrdinalIgnoreCase);
        var lineByKey = lines.ToDictionary(line => line.Path + "\0" + line.Number, StringComparer.OrdinalIgnoreCase);
        foreach (var lineReview in request.ReviewedNonExecutionLines ?? [])
        {
            Check(); TaskOperations.Sha(lineReview.CallerSha256); TaskOperations.Sha(lineReview.LineSha256);
            var key = lineReview.CallerPath + "\0" + lineReview.Line;
            if (!lineReview.IndependentlyVerified || lineReview.Line is < 1 or > 200000 || !DecisionText(lineReview.Reason) ||
                !callerByPath.TryGetValue(lineReview.CallerPath, out var caller) || caller.Status != "VERIFIED" || caller.Sha256 != lineReview.CallerSha256 ||
                !lineByKey.TryGetValue(key, out var line) || line.Sha256 != lineReview.LineSha256 || !reviewed.TryAdd(key, lineReview))
                throw new InvalidDataException("Independent non-execution review must bind one unique verified caller and exact line SHA.");
            var admitted = line.Kind == "documentation"
                ? lineReview.Category is "historical-record" or "retired-entry-mapping" or "prohibited-notice"
                : line.Kind == "code"
                    ? lineReview.Category is "rejection-fixture" or "classification-rule" or "schema-metadata"
                    : lineReview.Category == "schema-metadata";
            if (!admitted) throw new InvalidDataException("Non-execution review category differs from the caller kind.");
        }
        bool IndependentlyReviewed(CallerLine line) => reviewed.ContainsKey(line.Path + "\0" + line.Number);
        var scopeComplete = request.CallerScopeDeclaredComplete && request.CurrentCallers.Length > 0 && callerResults.All(caller => caller.Status == "VERIFIED");
        var gateResults = new List<RetirementGateResult>(); var gateById = new Dictionary<string, ReplacementGate>(StringComparer.Ordinal); var coverage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var gate in request.Gates)
        {
            Check(); var reasons = new List<string>();
            if (gate.Id.Length is < 1 or > 64 || !gateById.TryAdd(gate.Id, gate) || gate.OriginalPaths.Length is < 1 or > MaximumEntries || gate.Sources.Length is < 1 or > 64 || gate.CSharpEntry.Length is < 1 or > 256) throw new InvalidDataException("Replacement gate bounds/identity differ.");
            if (!gate.IndependentlyVerified) reasons.Add("GATE_NOT_INDEPENDENTLY_VERIFIED");
            if (HasScriptExtension(gate.CSharpEntry)) reasons.Add("REPLACEMENT_ENTRY_IS_LEGACY_SCRIPT");
            var sourcePins = new List<RetirementSnapshot>(); var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in gate.Sources)
            {
                Pin(source); if (!sourceNames.Add(source.Path) || Path.GetExtension(source.Path) is not (".cs" or ".csproj")) throw new InvalidDataException("Replacement source must be unique explicit C# or project files.");
                var current = Snapshot(source.Path); sourcePins.Add(current); if (!Matches(current, source)) reasons.Add("REPLACEMENT_SOURCE_PIN_MISMATCH:" + source.Path);
            }
            if (!gate.Sources.Any(source => Path.GetExtension(source.Path) == ".cs")) reasons.Add("C_SHARP_SOURCE_MISSING");
            Pin(gate.Receipt); var receipt = Snapshot(gate.Receipt.Path);
            if (!Matches(receipt, gate.Receipt)) reasons.Add("VALIDATION_RECEIPT_PIN_MISMATCH");
            else
            {
                if (gate.Receipt.Bytes is < 1 or > 4 * 1024 * 1024) throw new InvalidDataException("Validation receipt byte bound4MiB.");
                try
                {
                    using var document = TaskOperations.ReadDocument(Resolve(gate.Receipt.Path), budget);
                    if (!gate.ExpectedReceiptStatus.StartsWith("PASS", StringComparison.Ordinal) || document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("Status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != gate.ExpectedReceiptStatus) reasons.Add("EXPLICIT_PASSED_VALIDATION_STATUS_DIFFERS");
                    if (!Matches(Snapshot(gate.Receipt.Path), gate.Receipt)) reasons.Add("VALIDATION_RECEIPT_CHANGED_WHILE_READING");
                }
                catch (JsonException) { reasons.Add("VALIDATION_RECEIPT_METADATA_INVALID"); }
            }
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in gate.OriginalPaths)
            {
                Check();
                if (!paths.Add(path) || !classified.TryGetValue(path, out var entry) || entry.WorkPackage != gate.WorkPackage || archives.ContainsKey(path) || retentions.ContainsKey(path)) throw new InvalidDataException("Gate must name exact distinct classification paths within its work package, separate from archive or current-evidence decisions.");
                if (!coverage.TryGetValue(path, out var ids)) coverage.Add(path, ids = []); ids.Add(gate.Id);
            }
            gateResults.Add(new(gate.Id, reasons.Count == 0 ? "VERIFIED_EXPLICIT_GATE" : "PENDING", gate.CSharpEntry, reasons.ToArray(), sourcePins.ToArray(), receipt));
        }
        var gateState = gateResults.ToDictionary(gate => gate.Id, StringComparer.Ordinal);
        var names = inventory.Assignments.GroupBy(entry => Path.GetFileName(entry.Path.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var items = new List<RetirementItem>(); var findings = new List<RetirementCallerFinding>(); var gaps = new List<RetirementGateGap>();
        var unresolved = new List<CallerLine>();
        foreach (var line in lines)
        {
            Check();
            if (IndependentlyReviewed(line))
            {
                var lineReview = reviewed[line.Path + "\0" + line.Number];
                if (++findingsCount > 8192) throw new InvalidDataException("Retirement caller finding bound8192.");
                findings.Add(new("*", line.Path, line.Number, "INDEPENDENT_NON_EXECUTION_REVIEW", lineReview.Category, lineReview.Reason));
                continue;
            }
            if (line.Kind == "code" && Comment(line.Text) || !Execution(line.Text, line.Kind) || line.Exclusion is not null && line.Kind != "code") continue;
            var matched = false;
            foreach (var entry in inventory.Assignments)
            {
                Check(); var path = entry.Path.Replace('\\', '/');
                if (line.Text.Contains(path, StringComparison.OrdinalIgnoreCase) || line.Text.Contains(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) { matched = true; break; }
            }
            if (!matched)
            {
                unresolved.Add(line);
                if (++findingsCount > 8192) throw new InvalidDataException("Retirement caller finding bound8192.");
                findings.Add(new("*", line.Path, line.Number, "REVIEW_REQUIRED", "unresolved_script_expression", "execution syntax references a script which cannot be bound to a frozen original path"));
            }
        }
        foreach (var entry in inventory.Assignments)
        {
            Check(); var current = Snapshot(entry.Path, entry.Tracking == "external-skill"); var reasons = new List<string>(); var executions = 0; var ambiguous = unresolved.Count;
            var original = entry.Path.Replace('\\', '/'); var basename = Path.GetFileName(original);
            foreach (var line in lines)
            {
                Check();
                if (IndependentlyReviewed(line)) continue;
                var exact = line.Text.Contains(original, StringComparison.OrdinalIgnoreCase); var baseMatch = !exact && line.Text.Contains(basename, StringComparison.OrdinalIgnoreCase);
                if (!exact && !baseMatch) continue;
                if (++findingsCount > 8192) throw new InvalidDataException("Retirement caller finding bound8192.");
                var state = line.Exclusion;
                var reason = state is null ? "" : "explicit historical/prohibition context";
                if (state is not null && line.Kind == "code" && !Comment(line.Text) && Execution(line.Text, line.Kind))
                {
                    state = null; reason = "code string context cannot authorize excluding an execution statement";
                }
                if (state is null && line.Kind == "documentation" && MarkdownLink(line.Text) && !Execution(line.Text, line.Kind)) { state = "HISTORICAL_LINK_ONLY"; reason = "link without an execution instruction"; }
                if (state is null && line.Kind == "code" && Comment(line.Text)) { state = "COMMENT_REFERENCE_ONLY"; reason = "source comment is not an execution statement"; }
                if (line.MixedExecution)
                {
                    state = "REVIEW_REQUIRED"; reason = "mixed prohibition and positive execution clauses require independent review"; ambiguous++;
                }
                else if (state is null)
                {
                    var unresolvedBasename = baseMatch && names[basename] > 1;
                    state = !unresolvedBasename && Execution(line.Text, line.Kind) ? "CURRENT_EXECUTION_REFERENCE" : "REVIEW_REQUIRED";
                    reason = unresolvedBasename ? "duplicate basename requires exact path review" : state == "REVIEW_REQUIRED" ? "reference/dynamic invocation cannot be resolved from the supplied line" : "legacy execution syntax in current caller";
                    if (state == "CURRENT_EXECUTION_REFERENCE") executions++; else ambiguous++;
                }
                findings.Add(new(entry.Path, line.Path, line.Number, state, exact ? original : basename, reason));
            }
            string? gateId = null; var released = false;
            if (archives.TryGetValue(entry.Path, out var archive))
            {
                if (!Matches(current, archive.Original)) reasons.Add("ARCHIVED_ORIGINAL_PIN_CHANGED");
            }
            else if (retentions.TryGetValue(entry.Path, out var retention))
            {
                if (!Matches(current, retention.Current)) reasons.Add("RETAINED_CURRENT_EVIDENCE_PIN_CHANGED");
            }
            else if (!coverage.TryGetValue(entry.Path, out var ids)) { reasons.Add("EXPLICIT_REPLACEMENT_GATE_MISSING"); gaps.Add(new(entry.Path, entry.WorkPackage, entry.Replacement, "EXPLICIT_REPLACEMENT_GATE_MISSING")); }
            else if (ids.Count != 1) reasons.Add("MULTIPLE_REPLACEMENT_GATES_REQUIRE_REVIEW");
            else
            {
                gateId = ids[0]; var gate = gateById[gateId];
                released = entry.Tracking == "external-skill" && gate.ProjectExternalDependencyReleased && gateState[gateId].Status == "VERIFIED_EXPLICIT_GATE" && scopeComplete && executions == 0 && ambiguous == 0;
                if (gateState[gateId].Status != "VERIFIED_EXPLICIT_GATE") { reasons.Add("REPLACEMENT_GATE_PENDING"); gaps.Add(new(entry.Path, entry.WorkPackage, entry.Replacement, "REPLACEMENT_GATE_PENDING:" + gateId)); }
                if (entry.Tracking == "external-skill" && !released) reasons.Add("PROJECT_EXTERNAL_DEPENDENCY_NOT_RELEASED");
            }
            if (!scopeComplete) reasons.Add("CURRENT_CALLER_SCOPE_NOT_COMPLETE_OR_PINNED");
            if (executions != 0) reasons.Add("CURRENT_LEGACY_EXECUTION_REFERENCE");
            if (ambiguous != 0) reasons.Add("AMBIGUOUS_CURRENT_REFERENCE");
            if (unresolved.Count != 0) reasons.Add("UNRESOLVED_CURRENT_SCRIPT_EXPRESSION");
            var stateName = current.Status != "PRESENT" ? "UNKNOWN" : ambiguous != 0 || reasons.Contains("MULTIPLE_REPLACEMENT_GATES_REQUIRE_REVIEW", StringComparer.Ordinal) ? "REVIEW_REQUIRED" : reasons.Count == 0 ? retentions.ContainsKey(entry.Path) ? "RETAINED_UNKNOWN_HISTORY" : "READY" : "PENDING";
            if (current.Status != "PRESENT") reasons.Add("ORIGINAL_" + current.Status);
            items.Add(new(entry.Path, entry.Tracking, entry.WorkPackage, entry.Disposition, entry.Replacement, entry.OriginalStatus, current, stateName, gateId, executions, ambiguous, reasons.ToArray(), released)
            { ReviewBasis = archives.ContainsKey(entry.Path) ? "HISTORICAL_RECIPE_ARCHIVE_DECISION" : retentions.ContainsKey(entry.Path) ? "CURRENT_EVIDENCE_RETENTION_UNKNOWN_HISTORY" : "CAPABILITY_REPLACEMENT" });
        }
        Check();
        var ready = items.Count(item => item.ReviewState == "READY"); var pending = items.Count(item => item.ReviewState == "PENDING"); var review = items.Count(item => item.ReviewState == "REVIEW_REQUIRED"); var unknown = items.Count(item => item.ReviewState == "UNKNOWN");
        var retained = items.Count(item => item.ReviewState == "RETAINED_UNKNOWN_HISTORY");
        if (ready + pending + review + unknown + retained != inventory.Entries) throw new InvalidDataException("Retirement review lost its frozen denominator.");
        var reportStatus = ready == inventory.Entries ? "READY_review_only_no_mutation" : ready + retained == inventory.Entries
            ? "READY_current_review_unknown_history_retained_no_mutation" : "PENDING_review_incomplete_no_mutation";
        return new(reportStatus, request.ClassificationSha256, inventory.SourceSha256, root, inventory.Entries, ready, pending, review, unknown, hashed, scopeComplete, items.ToArray(), gateResults.ToArray(), callerResults.ToArray(), findings.ToArray(), gaps.ToArray())
        {
            ReviewedNonExecutionLines = reviewed.Values.ToArray(), HistoricalArchiveDecisions = archives.Values.ToArray(),
            CurrentEvidenceRetentions = retentions.Values.ToArray(), RetainedUnknownHistory = retained, HistoricalBaselineUnknown = inventory.UserEvidence
        };
    }
    private static bool DecisionText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 20 && text.Length <= 1024 &&
        text.Any(char.IsLetterOrDigit) && !text.Any(value => char.IsControl(value) || char.GetUnicodeCategory(value) == System.Globalization.UnicodeCategory.Format);
    private static bool MixedProhibitionExecution(string text, string kind)
    {
        if (kind == "code" && Comment(text)) return false;
        if (!Prohibited(text)) return false;
        // This conservative clause check is not a natural-language/control-flow
        // parser. Ambiguous mixed statements need an exact independent review.
        var clauses = text.Replace(" but ", ";", StringComparison.OrdinalIgnoreCase).Replace("但", ";", StringComparison.Ordinal)
            .Replace("然后", ";", StringComparison.Ordinal)
            .Split([';', '；', '。', ',', '，'], 65, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (clauses.Length == 65) return true;
        return clauses.Any(Prohibited) && clauses.Any(clause => !Prohibited(clause) && Execution(clause, kind));
    }
    private static bool HasScriptExtension(string text)
    {
        string[] extensions = [".ps1", ".py", ".sh", ".cmd", ".bat"];
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '.') continue;
            foreach (var extension in extensions)
            {
                if (!text.AsSpan(index).StartsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                var next = index + extension.Length;
                if (next == text.Length || !char.IsAsciiLetterOrDigit(text[next]) && text[next] != '_') return true;
            }
        }
        return false;
    }
    private static bool Prohibited(string text) => new[] { "禁止", "不得", "不要执行", "不能执行", "不再调用", "不得调用", "do not run", "do not execute", "never run", "never execute", "must not" }.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    private static bool Historical(string text) => new[] { "历史证据", "历史回执", "原始回执", "仅供追溯", "historical evidence", "historical receipt", "reference only" }.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    private static bool MarkdownLink(string text) => text.Contains("](", StringComparison.Ordinal) && text.Contains(')');
    private static bool Comment(string text)
    {
        var line = text.TrimStart();
        if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('#')) return true;
        if (!line.StartsWith("/*", StringComparison.Ordinal)) return false;
        var end = line.IndexOf("*/", StringComparison.Ordinal);
        return end < 0 || string.IsNullOrWhiteSpace(line[(end + 2)..]);
    }
    private static bool SuspectedInterpreterInvocation(string text, string kind) => kind == "code" && !Comment(text) && Execution(text, kind) &&
        new[] { "pwsh", "powershell", "python", "bash", "cmd.exe", "\"cmd\"", "'cmd'", "\"sh\"", "'sh'", "/bin/sh" }.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    private static bool Execution(string text, string kind)
    {
        var line = text.TrimStart();
        return line.StartsWith('&') || line.StartsWith(". ", StringComparison.Ordinal) || new[] { "pwsh ", "pwsh.exe", "powershell ", "powershell.exe", "python ", "python3 ", "bash ", "sh ", "cmd /c", "cmd.exe", "Process.Start", "ProcessStartInfo", "Executable =", "ArgumentList.Add", "运行", "执行", "invoke ", "run " }.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

[JsonSerializable(typeof(MigrationInventory))]
[JsonSerializable(typeof(RetirementReviewRequest))]
[JsonSerializable(typeof(RetirementReport))]
[JsonSourceGenerationOptions(WriteIndented = true, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class RetirementJson : JsonSerializerContext;
