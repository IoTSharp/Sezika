using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Sezika.OracleCompare;

return await Comparison.RunAsync(args);

internal static class Comparison
{
    private const int MaxCases = 128;
    private const int MaxFileBytes = 32 * 1024 * 1024;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is not (6 or 7) || !int.TryParse(args[5], CultureInfo.InvariantCulture, out var seconds) || seconds is < 1 or > 300)
        {
            Console.Error.WriteLine("Usage: Sezika.OracleCompare <reference.json> <actual.json> <contract.v1.json> <cases.v1.json> <new-report.json> <timeout-seconds:1..300> [case-id,case-id,...]");
            return 2;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; deadline.Cancel(); };
        Console.CancelKeyPress += cancel;
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        try
        {
            var token = deadline.Token;
            Console.Error.WriteLine("Loading four bounded local JSON files; no model inference.");
            var referenceBytes = await ReadAsync(args[0], token);
            var actualBytes = await ReadAsync(args[1], token);
            var contractBytes = await ReadAsync(args[2], token);
            var casesBytes = await ReadAsync(args[3], token);
            var reference = JsonSerializer.Deserialize(referenceBytes, CompareJsonContext.Default.Capture)
                ?? throw new InvalidDataException("Reference capture is null.");
            var actual = JsonSerializer.Deserialize(actualBytes, CompareJsonContext.Default.Capture)
                ?? throw new InvalidDataException("Actual capture is null.");
            var contract = JsonSerializer.Deserialize(contractBytes, CompareJsonContext.Default.FrozenContract)
                ?? throw new InvalidDataException("Frozen contract is null.");
            if (contract.SchemaVersion != "sezika.laya-oracle-contract.v1" || contract.OutputSchemaVersion != "sezika.laya-oracle.v1")
                throw new InvalidDataException("Unsupported frozen comparison contract.");
            ValidateTolerances(contract.Tolerances);
            var contractHash = Hash(contractBytes);
            var casesHash = Hash(casesBytes);
            Validate(reference, contractHash, casesHash, token);
            Validate(actual, contractHash, casesHash, token);
            var manifest = JsonSerializer.Deserialize(casesBytes, CompareJsonContext.Default.InputManifest)
                ?? throw new InvalidDataException("Input manifest is null.");
            var manifestCases = ValidateManifest(manifest, token);
            ValidateMembership(reference, manifestCases, token);
            ValidateMembership(actual, manifestCases, token);

            // Validate every row in the original files before applying an explicit
            // subset. Selection cannot conceal invalid identities, malformed rows,
            // duplicates or failures carrying spurious numeric evidence.
            var requestedIds = args.Length == 7 ? ValidateSelection(args[6], reference, actual, manifestCases, token) : null;
            var requestedSet = requestedIds?.ToHashSet(StringComparer.Ordinal);
            var expectedCases = requestedSet is null ? reference.Cases : reference.Cases.Where(row => requestedSet.Contains(row.Id)).ToArray();
            var actualCases = requestedSet is null ? actual.Cases : actual.Cases.Where(row => requestedSet.Contains(row.Id)).ToArray();

            var issues = new List<Issue>();
            if (reference.Provenance != actual.Provenance)
                issues.Add(new("<capture>", "provenance", "Model, tokenizer, source, input, contract or length/temperature identity differs."));
            if (expectedCases.Length != actualCases.Length)
                issues.Add(new("<capture>", "cases", "Case count differs."));
            var actualById = actualCases.ToDictionary(row => row.Id, StringComparer.Ordinal);
            var expectedIds = new HashSet<string>(expectedCases.Select(row => row.Id), StringComparer.Ordinal);
            var compared = 0;
            var answered = 0;
            var failures = 0;
            var nearTies = 0;
            var maxLogit = 0d;
            var maxProbability = 0d;
            foreach (var expected in expectedCases) // Validated <= 128; deadline checked on every case.
            {
                token.ThrowIfCancellationRequested();
                Console.Error.WriteLine($"compare {++compared}/{expectedCases.Length}: {expected.Id}");
                if (!actualById.TryGetValue(expected.Id, out var observed))
                {
                    issues.Add(new(expected.Id, "case", "Missing actual case."));
                    continue;
                }
                if (expected.Primitive != observed.Primitive)
                    issues.Add(new(expected.Id, "primitive", "Primitive differs."));
                if (!JsonElement.DeepEquals(expected.Input, observed.Input))
                    issues.Add(new(expected.Id, "input", "Expanded input differs."));
                if (expected.Status != observed.Status)
                    issues.Add(new(expected.Id, "status", $"Expected {expected.Status}, got {observed.Status}."));
                Exact(expected.Id, "token_ids", expected.TokenIds, observed.TokenIds, issues);
                Exact(expected.Id, "marker_positions", expected.MarkerPositions, observed.MarkerPositions, issues);
                Exact(expected.Id, "candidate_labels", expected.CandidateLabels, observed.CandidateLabels, issues);
                if (expected.Status == "failed" && observed.Status == "failed")
                {
                    failures++;
                    if (expected.Failure!.Stage != observed.Failure!.Stage || expected.Failure.Type != observed.Failure.Type)
                        issues.Add(new(expected.Id, "failure", "Normalized failure stage/type differs; messages are diagnostic only."));
                    continue;
                }
                if (expected.Status != "answered" || observed.Status != "answered") continue;
                answered++;
                var sorted = expected.RawLogits!.OrderDescending().ToArray();
                if (sorted.Length >= 2 && sorted[0] - sorted[1] <= contract.Tolerances.NearTieLogitMargin) nearTies++;
                Numbers(expected.Id, "raw_logits", expected.RawLogits!, observed.RawLogits!, contract.Tolerances.RawLogits, issues, ref maxLogit);
                Numbers(expected.Id, "probabilities", expected.Probabilities!, observed.Probabilities!, contract.Tolerances.Probabilities, issues, ref maxProbability);
                ComparePrediction(expected, observed, contract.Tolerances, issues);
            }
            foreach (var row in actualCases)
            {
                token.ThrowIfCancellationRequested();
                if (!expectedIds.Contains(row.Id)) issues.Add(new(row.Id, "case", "Unexpected actual case."));
            }
            if (answered == 0) issues.Add(new("<capture>", "coverage", "No answered pair; failure agreement alone cannot establish numerical parity."));
            var selectedIds = expectedCases.Where(row => actualById.ContainsKey(row.Id)).Select(row => row.Id).ToArray();
            var selectedSet = new HashSet<string>(selectedIds, StringComparer.Ordinal);
            var missingIds = manifest.Cases.Where(row => !selectedSet.Contains(row.Id)).Select(row => row.Id).ToArray();
            var report = new ComparisonReport
            {
                StartedUtc = started, ReferenceSha256 = Hash(referenceBytes), ActualSha256 = Hash(actualBytes),
                ContractSha256 = contractHash, CasesSha256 = casesHash, Identity = reference.Provenance,
                Tolerances = contract.Tolerances, ReferenceCount = reference.Cases.Length, ActualCount = actual.Cases.Length,
                ReferenceMeasurementStatus = reference.MeasurementStatus, ActualMeasurementStatus = actual.MeasurementStatus,
                SelectionMode = requestedIds is null ? "all_captured_cases" : "explicit_case_ids", RequestedCaseIds = requestedIds,
                ExcludedReferenceCaseIds = reference.Cases.Where(row => !expectedIds.Contains(row.Id)).Select(row => row.Id).ToArray(),
                ExcludedActualCaseIds = actual.Cases.Where(row => !actualById.ContainsKey(row.Id)).Select(row => row.Id).ToArray(),
                ManifestCount = manifest.Cases.Length, SelectedCaseIds = selectedIds, MissingCaseIds = missingIds,
                InputCoverage = (double)selectedIds.Length / manifest.Cases.Length, CompleteInputCoverage = missingIds.Length == 0,
                ComparedCount = selectedIds.Length, AnsweredPairs = answered,
                FailurePairs = failures, NearTieCases = nearTies, MaxLogitError = maxLogit, MaxProbabilityError = maxProbability,
                Passed = issues.Count == 0, FullManifestPassed = issues.Count == 0 && missingIds.Length == 0 &&
                    (reference.MeasurementStatus is "complete" or "completed") && (actual.MeasurementStatus is "complete" or "completed"),
                Issues = issues, ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
            };
            // CreateNew protects input captures and previous evidence from accidental overwrite.
            await using var output = new FileStream(Path.GetFullPath(args[4]), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
            await JsonSerializer.SerializeAsync(output, report, CompareJsonContext.Default.ComparisonReport, token);
            Console.Error.WriteLine($"Comparison {(report.Passed ? "passed" : "failed")}: {answered} answered pairs, {failures} failure pairs, {issues.Count} issues; coverage={selectedIds.Length}/{manifest.Cases.Length}; full_manifest_passed={report.FullManifestPassed}.");
            return report.Passed ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or OperationCanceledException)
        {
            Console.Error.WriteLine($"Comparison unavailable: {exception.Message}");
            return 2;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static async Task<byte[]> ReadAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        if (input.Length is < 1 or > MaxFileBytes) throw new InvalidDataException("JSON file must be 1 byte to 32 MiB.");
        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, token);
        // Reject duplicate properties before DTO deserialization can hide them.
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var nodes = 0;
        Inspect(document.RootElement, token, ref nodes);
        return bytes;
    }

    private static void Inspect(JsonElement element, CancellationToken token, ref int nodes)
    {
        token.ThrowIfCancellationRequested();
        if (++nodes > 500000) throw new InvalidDataException("JSON node limit exceeded.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property: {property.Name}");
                Inspect(property.Value, token, ref nodes);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) Inspect(child, token, ref nodes);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Dictionary<string, InputCase> ValidateManifest(InputManifest manifest, CancellationToken token)
    {
        if (manifest.SchemaVersion != "sezika.laya-oracle-inputs.v1" || manifest.Cases is null || manifest.Cases.Length is < 1 or > MaxCases)
            throw new InvalidDataException("Invalid input manifest schema/case count.");
        var cases = new Dictionary<string, InputCase>(StringComparer.Ordinal);
        foreach (var row in manifest.Cases)
        {
            token.ThrowIfCancellationRequested();
            if (row is null || string.IsNullOrWhiteSpace(row.Id) || row.Id.Length > 128 ||
                row.Primitive is not ("choice" or "score" or "boolean") || !cases.TryAdd(row.Id, row))
                throw new InvalidDataException("Invalid or duplicate case in input manifest.");
        }
        return cases;
    }

    private static void ValidateMembership(Capture capture, Dictionary<string, InputCase> manifest, CancellationToken token)
    {
        foreach (var row in capture.Cases)
        {
            token.ThrowIfCancellationRequested();
            if (!manifest.TryGetValue(row.Id, out var definition) || definition.Primitive != row.Primitive)
                throw new InvalidDataException($"{row.Id}: case identity/primitive is not in the frozen input manifest.");
        }
    }

    private static string[] ValidateSelection(string csv, Capture reference, Capture actual,
        Dictionary<string, InputCase> manifest, CancellationToken token)
    {
        if (csv.Length is < 1 or > MaxCases * 129)
            throw new InvalidDataException("Explicit selection must contain 1..128 bounded case IDs.");
        var selected = csv.Split(',');
        if (selected.Length is < 1 or > MaxCases)
            throw new InvalidDataException("Explicit selection must contain 1..128 case IDs.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var referenceIds = reference.Cases.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var actualIds = actual.Cases.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in selected)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !seen.Add(id))
                throw new InvalidDataException("Explicit selection contains an empty, duplicate or oversized ID.");
            if (!manifest.ContainsKey(id) || !referenceIds.Contains(id) || !actualIds.Contains(id))
                throw new InvalidDataException($"Selected case must exist in the manifest and both original captures: {id}");
        }
        return selected;
    }

    private static void ValidateTolerances(Tolerances values)
    {
        if (values is null || values.RawLogits is null || values.Probabilities is null || values.Score is null ||
            !double.IsFinite(values.NearTieLogitMargin) || values.NearTieLogitMargin < 0)
            throw new InvalidDataException("Missing or invalid frozen tolerances.");
        foreach (var value in new[] { values.RawLogits, values.Probabilities, values.Score })
            if (!double.IsFinite(value.Absolute) || !double.IsFinite(value.Relative) || value.Absolute < 0 || value.Relative < 0)
                throw new InvalidDataException("Tolerances must be finite and nonnegative.");
    }

    private static void Validate(Capture capture, string contractHash, string casesHash, CancellationToken token)
    {
        var identity = capture.Provenance;
        if (capture.SchemaVersion != "sezika.laya-oracle.v1" || identity is null || capture.Cases is null || capture.Cases.Length is < 1 or > MaxCases)
            throw new InvalidDataException("Invalid capture envelope or case count (1..128).");
        if (identity.ModelId != "convaiinnovations/laya-multilingual" ||
            identity.ModelRevision != "052592a15d198d9ad47da779604259b10b47b7aa" ||
            identity.UpstreamSourceRevision != "4066d5d5fbf08b66c6757ddeedbd797bd7655bc0" ||
            identity.WeightsSha256 != "9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204" ||
            identity.TokenizerSha256 != "609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f" ||
            identity.ContractSha256 != contractHash || identity.CasesSha256 != casesHash ||
            identity.MaxLen != 1024 || identity.HeadMaxLen != 256 || identity.TemperaturePolicy != "checkpoint_fixed_1_no_overrides")
            throw new InvalidDataException("Capture identity does not match the pinned oracle and supplied input/contract bytes.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in capture.Cases)
        {
            token.ThrowIfCancellationRequested();
            if (row is null || string.IsNullOrWhiteSpace(row.Id) || row.Id.Length > 128 || !ids.Add(row.Id) ||
                row.Primitive is not ("choice" or "score" or "boolean") || row.Status is not ("answered" or "failed") ||
                row.Input.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Case has missing/duplicate identity, invalid primitive/status or missing expanded input.");
            if (row.TokenIds is { } tokens && (tokens.Length is < 1 or > 1024 || tokens.Any(value => value < 0)))
                throw new InvalidDataException($"{row.Id}: invalid token IDs.");
            if (row.CandidateLabels is { } labels && (labels.Length is < 1 or > 64 ||
                labels.Any(string.IsNullOrWhiteSpace) || labels.Distinct(StringComparer.Ordinal).Count() != labels.Length))
                throw new InvalidDataException($"{row.Id}: invalid candidate labels.");
            if (row.MarkerPositions is { } markers && (row.TokenIds is null || row.CandidateLabels is null ||
                markers.Length != row.CandidateLabels.Length || markers.Any(index => index < 0 || index >= row.TokenIds.Length) ||
                !markers.SequenceEqual(markers.Distinct().Order())))
                throw new InvalidDataException($"{row.Id}: invalid marker positions.");
            if (row.Status == "failed")
            {
                if (row.Failure is null || row.Prediction is not null || row.RawLogits is not null || row.Probabilities is not null ||
                    row.Failure.Stage is not ("validate" or "encode" or "infer" or "decode") ||
                    row.Failure.Type is not ("invalid_question" or "sequence_budget_exceeded" or "resource_exhausted" or "inference_failed" or "non_finite_output"))
                    throw new InvalidDataException($"{row.Id}: failed case must contain a normalized failure and null numeric/prediction fields.");
                continue;
            }
            if (row.Failure is not null || row.Prediction is null || row.TokenIds is null || row.MarkerPositions is null ||
                row.CandidateLabels is null || row.RawLogits is null || row.Probabilities is null ||
                row.RawLogits.Length != row.CandidateLabels.Length || row.Probabilities.Length != row.CandidateLabels.Length ||
                row.RawLogits.Any(value => !double.IsFinite(value)) ||
                row.Probabilities.Any(value => !double.IsFinite(value) || value < 0 || value > 1) ||
                Math.Abs(row.Probabilities.Sum() - 1) > 0.000002)
                throw new InvalidDataException($"{row.Id}: answered case lacks finite, normalized numeric evidence.");
            ValidatePrediction(row);
        }
    }

    private static void ValidatePrediction(CapturedCase row)
    {
        var prediction = row.Prediction!;
        var labels = row.CandidateLabels!;
        var probabilities = row.Probabilities!;
        if (row.Primitive == "choice")
        {
            if (prediction.ChoiceLabel is null || prediction.ChoiceLabel != labels[Array.IndexOf(probabilities, probabilities.Max())])
                throw new InvalidDataException($"{row.Id}: Choice prediction does not match first argmax.");
        }
        else if (row.Primitive == "score")
        {
            var expectedLabels = Enumerable.Range(0, labels.Length).Select(index => index.ToString(CultureInfo.InvariantCulture));
            var score = probabilities.Select((probability, index) => probability * index).Sum();
            if (!labels.SequenceEqual(expectedLabels) || prediction.Score is not { } actualScore || !double.IsFinite(actualScore) || Math.Abs(actualScore - score) > 1e-5)
                throw new InvalidDataException($"{row.Id}: Score labels/expectation are inconsistent.");
        }
        else if (!labels.SequenceEqual(new[] { "false", "true" }) || prediction.ProbabilityTrue is not { } probabilityTrue ||
                 !double.IsFinite(probabilityTrue) || Math.Abs(probabilityTrue - probabilities[1]) > 1e-7 ||
                 prediction.Boolean != (probabilityTrue >= 0.5))
            throw new InvalidDataException($"{row.Id}: Boolean must map P(true) to the true marker.");
    }

    private static void Exact<T>(string id, string field, T[]? expected, T[]? actual, List<Issue> issues)
    {
        if (expected is null ? actual is not null : actual is null || !expected.SequenceEqual(actual))
            issues.Add(new(id, field, "Ordered values differ (including missing values)."));
    }

    private static void Numbers(string id, string field, double[] expected, double[] actual, Tolerance tolerance, List<Issue> issues, ref double maximum)
    {
        if (expected.Length != actual.Length) { issues.Add(new(id, field, "Candidate count differs.")); return; }
        for (var index = 0; index < expected.Length; index++) // <= 64 validated reference candidates.
        {
            var error = Math.Abs(expected[index] - actual[index]);
            maximum = Math.Max(maximum, error);
            if (!tolerance.Accepts(expected[index], actual[index]))
                issues.Add(new(id, $"{field}[{index}]", $"Absolute error {error:R} exceeds absolute + relative * |reference| tolerance."));
        }
    }

    private static void ComparePrediction(CapturedCase expected, CapturedCase actual, Tolerances tolerances, List<Issue> issues)
    {
        var left = expected.Prediction!;
        var right = actual.Prediction!;
        if (left.ChoiceLabel != right.ChoiceLabel || left.Boolean != right.Boolean)
            issues.Add(new(expected.Id, "prediction", "Discrete prediction differs; near ties are not exempt."));
        if (left.Score.HasValue != right.Score.HasValue || (left.Score.HasValue && !tolerances.Score.Accepts(left.Score.Value, right.Score!.Value)))
            issues.Add(new(expected.Id, "prediction.score", "Score expectation differs beyond tolerance."));
        if (left.ProbabilityTrue.HasValue != right.ProbabilityTrue.HasValue ||
            (left.ProbabilityTrue.HasValue && !tolerances.Probabilities.Accepts(left.ProbabilityTrue.Value, right.ProbabilityTrue!.Value)))
            issues.Add(new(expected.Id, "prediction.probability_true", "P(true) differs beyond tolerance."));
    }
}
