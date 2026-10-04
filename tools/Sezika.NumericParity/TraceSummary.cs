using System.Security.Cryptography;
using System.Text.Json;

internal sealed record TraceSummaryInput(string Path, string Sha256);
internal sealed record TraceSummaryIndex(string SchemaVersion, TraceSummaryInput[] Reports);
internal sealed record TraceSummaryRun(string Path, string Sha256, string Status, string[] SelectedCaseIds, string[] UnprocessedCaseIds);
internal sealed record TraceSummaryCell(string Id, string Primitive, string Language, string Length, string Backend,
    int ObservedAttempts, int PassedAttempts, string Status, double? MaxLogitError);
internal sealed record TraceSummaryReport(string SchemaVersion, string ReferenceSha256, string ContractSha256,
    string IndexSha256, Dictionary<string, string> BinarySha256, List<TraceSummaryRun> Runs, TraceSummaryCell[] Cells)
{
    public int PlannedBackendCells => 54;
    public int PassedBackendCells => Cells.Count(cell => cell.Status == "passed");
    public bool CoreMatrixPassed => PassedBackendCells == PlannedBackendCells;
    public int IncompleteRuns => Runs.Count(run => run.Status != "complete");
    public bool FullS306GatePassed => false;
    public string Scope => "Offline aggregation of hash-bound observations with identical binary/reference/contract identity. Interrupted runs remain listed. Repeated observations never hide a failed comparison. Core coverage does not establish independent layer thresholds, near-tie acceptance, quality, AOT or performance.";
}

internal static class TraceSummary
{
    private static readonly string[] BackendNames = ["scalar", "simd", "cuda"];
    private static readonly IReadOnlyDictionary<string, TraceCoverageCell> CoreCells =
        TraceCoverage.Build([]).ToDictionary(cell => cell.Id, StringComparer.Ordinal);

    internal static int Run(string[] args)
    {
        if (args.Length != 6 || !IsSha(args[1]) || !IsSha(args[2]) || !int.TryParse(args[5], out var seconds) || seconds is < 1 or > 60)
        {
            Console.Error.WriteLine("Usage: --trace-summary <reference-sha256> <contract-sha256> <report-index.json> <new-output.json> <1..60 seconds>");
            return 2;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; deadline.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var token = deadline.Token;
            var indexPath = System.IO.Path.GetFullPath(args[3]);
            if (new FileInfo(indexPath).Length > 1024 * 1024) throw new InvalidDataException("Trace index exceeds 1 MiB.");
            var indexBytes = File.ReadAllBytes(indexPath);
            var indexHash = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
            var index = JsonSerializer.Deserialize(indexBytes, TraceJsonContext.Default.TraceSummaryIndex)
                ?? throw new InvalidDataException("Trace index missing.");
            if (index.SchemaVersion != "sezika.trace-report-index.v1" || index.Reports is null || index.Reports.Length is < 1 or > 64)
                throw new InvalidDataException("Trace summary requires 1..64 indexed reports.");
            var runs = new List<TraceSummaryRun>();
            var observations = new List<TraceCaseReport>();
            Dictionary<string, string>? binaries = null;
            var uniqueHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            for (var itemIndex = 0; itemIndex < index.Reports.Length; itemIndex++)
            {
                token.ThrowIfCancellationRequested();
                var item = index.Reports[itemIndex];
                if (!IsSha(item.Sha256) || !uniqueHashes.Add(item.Sha256)) throw new InvalidDataException("Duplicate or invalid trace artifact hash.");
                var path = System.IO.Path.GetFullPath(item.Path, System.IO.Path.GetDirectoryName(indexPath)!);
                var bytes = TraceDiagnostics.ReadFrozen(path, item.Sha256, 4 * 1024 * 1024, token);
                totalBytes += bytes.Length;
                if (totalBytes > 64 * 1024 * 1024) throw new InvalidDataException("Trace summary exceeds 64 MiB.");
                var report = JsonSerializer.Deserialize(bytes, TraceJsonContext.Default.TraceReport)
                    ?? throw new InvalidDataException("Trace report missing.");
                if (report.SchemaVersion != 2 || !string.Equals(report.ReferenceSha256, args[1], StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(report.ContractSha256, args[2], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Trace report identity or inventory differs.");
                ValidateReport(report);
                if (binaries is not null && !binaries.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .SequenceEqual(report.BinarySha256.OrderBy(pair => pair.Key, StringComparer.Ordinal)))
                    throw new InvalidDataException("Trace reports use different binaries.");
                binaries = report.BinarySha256;
                foreach (var row in report.Cases)
                {
                    token.ThrowIfCancellationRequested();
                    if (!report.SelectedCaseIds.Contains(row.Id, StringComparer.Ordinal) || row.Backends.Count > 3 ||
                        row.Backends.Select(backend => backend.Backend).Distinct(StringComparer.Ordinal).Count() != row.Backends.Count ||
                        row.Backends.Any(backend => backend.Backend is not ("scalar" or "simd" or "cuda")))
                        throw new InvalidDataException("Trace case/backend inventory is invalid.");
                    var previous = observations.FirstOrDefault(previous => previous.Id == row.Id);
                    if (previous is not null && (previous.Primitive != row.Primitive || previous.TokenCount != row.TokenCount ||
                        !previous.TokenIds.SequenceEqual(row.TokenIds) || !previous.MarkerPositions.SequenceEqual(row.MarkerPositions) ||
                        !previous.CandidateLabels.SequenceEqual(row.CandidateLabels)))
                        throw new InvalidDataException("Repeated trace case has different input.");
                    observations.Add(row);
                }
                runs.Add(new(path, item.Sha256.ToLowerInvariant(), report.Status, report.SelectedCaseIds, report.UnprocessedCaseIds));
                Console.WriteLine($"Trace summary {itemIndex + 1}/{index.Reports.Length}: {report.Cases.Count} observed cases, status={report.Status}.");
            }
            var cells = Build(observations, token);
            var summary = new TraceSummaryReport("sezika.trace-summary.v1", args[1].ToLowerInvariant(), args[2].ToLowerInvariant(),
                indexHash, binaries!, runs, cells);
            var output = System.IO.Path.GetFullPath(args[4]);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
            token.ThrowIfCancellationRequested();
            using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, summary, TraceJsonContext.Default.TraceSummaryReport);
            Console.WriteLine($"Core backend cells passed: {summary.PassedBackendCells}/54; incomplete source runs: {summary.IncompleteRuns}.");
            return summary.CoreMatrixPassed ? 0 : 1;
        }
        catch (Exception exception) { Console.Error.WriteLine($"trace-summary: {exception.GetType().Name}: {exception.Message}"); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }

    internal static TraceSummaryCell[] Build(List<TraceCaseReport> observations, CancellationToken token)
    {
        if (observations.Count > 64 * 18) throw new InvalidDataException("Too many trace observations.");
        foreach (var row in observations)
        {
            ValidateCoreCase(row);
            ValidateBackendInventory(row, requireComplete: false);
        }
        var result = new List<TraceSummaryCell>();
        foreach (var core in CoreCells.Values)
        foreach (var backend in BackendNames)
        {
            token.ThrowIfCancellationRequested();
            var attempts = observations.Where(row => row.Id == core.Id).SelectMany(row => row.Backends).Where(row => row.Backend == backend).ToArray();
            var passed = attempts.Count(row => row.Status == "compared" && row.OracleComparison is { Passed: true } && row.MissingTraces.Count == 0);
            result.Add(new(core.Id, core.Primitive, core.Language, core.Length, backend, attempts.Length, passed,
                attempts.Length == 0 ? "unmeasured" : passed == attempts.Length ? "passed" : "failed",
                attempts.Any(row => row.OracleComparison is not null) ? attempts.Where(row => row.OracleComparison is not null).Max(row => row.OracleComparison!.MaxLogitError) : null));
        }
        return result.ToArray();
    }

    /// <summary>Validates the persisted report contract before any observations enter the aggregate.</summary>
    internal static void ValidateReport(TraceReport report)
    {
        if (report.Status is not ("complete" or "incomplete") || report.SelectedCaseIds is null ||
            report.SelectedCaseIds.Length is < 1 or > 18 ||
            report.SelectedCaseIds.Distinct(StringComparer.Ordinal).Count() != report.SelectedCaseIds.Length ||
            report.SelectedCaseIds.Any(id => id is null || !CoreCells.ContainsKey(id)) || report.UnprocessedCaseIds is null ||
            report.UnprocessedCaseIds.Distinct(StringComparer.Ordinal).Count() != report.UnprocessedCaseIds.Length ||
            report.UnprocessedCaseIds.Any(id => !report.SelectedCaseIds.Contains(id, StringComparer.Ordinal)) ||
            report.Cases is null || report.Cases.Any(row => row is null) || report.Cases.Count > report.SelectedCaseIds.Length ||
            report.Cases.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != report.Cases.Count ||
            report.BinarySha256 is null || report.BinarySha256.Count is < 1 or > 4 ||
            report.BinarySha256.Values.Any(value => !IsSha(value)))
            throw new InvalidDataException("Trace report identity or inventory differs.");

        foreach (var row in report.Cases)
        {
            if (!report.SelectedCaseIds.Contains(row.Id, StringComparer.Ordinal))
                throw new InvalidDataException("Trace case is outside the selected core IDs.");
            ValidateCoreCase(row);
            ValidateBackendInventory(row, requireComplete: report.Status == "complete");
        }

        var observed = report.Cases.ToDictionary(row => row.Id, StringComparer.Ordinal);
        foreach (var id in report.UnprocessedCaseIds)
            if (observed.TryGetValue(id, out var row) && row.Backends.Count == BackendNames.Length)
                throw new InvalidDataException("A fully observed case cannot be listed as unprocessed.");
        foreach (var row in report.Cases)
            if (row.Backends.Count != BackendNames.Length && !report.UnprocessedCaseIds.Contains(row.Id, StringComparer.Ordinal))
                throw new InvalidDataException("An incomplete trace case must be listed as unprocessed.");
        if (report.SelectedCaseIds.Any(id => !observed.ContainsKey(id) && !report.UnprocessedCaseIds.Contains(id, StringComparer.Ordinal)))
            throw new InvalidDataException("A selected trace case is neither observed nor listed as unprocessed.");

        // A complete source report is a closed result, never a claim based on partial rows.
        if (report.Status == "complete" &&
            (report.Error is not null || report.SelectedDiagnosticsPassed != true || report.UnprocessedCaseIds.Length != 0 ||
             report.Cases.Count != report.SelectedCaseIds.Length ||
             report.Cases.Any(row => row.Backends.Count != BackendNames.Length ||
                 row.Backends.Any(backend => backend.Status != "compared" || backend.OracleComparison is not { Passed: true } ||
                     backend.MissingTraces.Count != 0))))
            throw new InvalidDataException("Complete trace report must contain three passing backends for every selected case.");

        if (report.Status == "incomplete" && report.SelectedDiagnosticsPassed)
            throw new InvalidDataException("Incomplete trace report cannot claim selected diagnostics passed.");
    }

    private static void ValidateCoreCase(TraceCaseReport row)
    {
        if (row is null || row.Id is null || !CoreCells.TryGetValue(row.Id, out var core) ||
            row.Primitive != core.Primitive || row.TokenIds is not { Length: >= 2 and <= 1024 } ||
            row.TokenCount != row.TokenIds.Length || row.MarkerPositions is not { Length: >= 2 and <= 32 } ||
            row.CandidateLabels is not { Length: >= 2 and <= 32 } ||
            row.MarkerPositions.Length != row.CandidateLabels.Length ||
            row.MarkerPositions.Any(index => index < 0 || index >= row.TokenIds.Length) ||
            row.MarkerPositions.Distinct().Count() != row.MarkerPositions.Length ||
            row.CandidateLabels.Any(string.IsNullOrWhiteSpace) ||
            row.CandidateLabels.Distinct(StringComparer.Ordinal).Count() != row.CandidateLabels.Length)
            throw new InvalidDataException("Trace report contains a non-fixed core case or malformed case fields.");
    }

    private static void ValidateBackendInventory(TraceCaseReport row, bool requireComplete)
    {
        if (row.Backends is null || row.Backends.Count > BackendNames.Length ||
            row.Backends.Any(backend => backend is null) ||
            row.Backends.Select(backend => backend!.Backend).Distinct(StringComparer.Ordinal).Count() != row.Backends.Count ||
            row.Backends.Any(backend => !BackendNames.Contains(backend!.Backend, StringComparer.Ordinal)))
            throw new InvalidDataException("Trace case/backend inventory is invalid.");

        foreach (var backend in row.Backends)
        {
            if (backend.Status is not ("compared" or "failed") || backend.LayerDifferences is null || backend.MissingTraces is null)
                throw new InvalidDataException("Trace backend status or fields are invalid.");
            if (backend.Status == "compared" && (backend.Error is not null || backend.Logits is null ||
                backend.Probabilities is null || backend.Logits.Length is < 2 or > 32 ||
                backend.Probabilities.Length != backend.Logits.Length ||
                backend.Logits.Any(value => !double.IsFinite(value)) || backend.Probabilities.Any(value => !double.IsFinite(value)) ||
                backend.OracleComparison is not { } comparison || comparison.Issues is null ||
                !double.IsFinite(comparison.ReferenceMargin) || !double.IsFinite(comparison.MaxLogitError) ||
                !double.IsFinite(comparison.MaxProbabilityError) || backend.MissingTraces.Count != 0))
                throw new InvalidDataException("Compared backend must carry logits, probabilities and oracle comparison.");
            if (backend.Status == "failed" && (string.IsNullOrWhiteSpace(backend.Error) || backend.Logits is not null ||
                backend.Probabilities is not null || backend.OracleComparison is not null))
                throw new InvalidDataException("Failed backend must carry an error and no compared outputs.");
        }
        if (requireComplete && row.Backends.Count != BackendNames.Length)
            throw new InvalidDataException("Complete trace case must contain scalar, simd and cuda backends.");
    }

    private static bool IsSha(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
