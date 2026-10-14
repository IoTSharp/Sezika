using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika.Tooling;

internal sealed record GpuObservationRequest(string NvidiaSmiExecutable, string NvidiaSmiSha256, string GpuUuid,
    string OutputDirectory, int Samples = 1, int IntervalMilliseconds = 1000, int TimeoutSeconds = 60,
    int QueryTimeoutSeconds = 5, int CleanupSeconds = 5, string? CancelFile = null, string? BrokerExecutable = null);

internal sealed record GpuObservationMetrics(string Uuid, double UtilizationGpuPercent, double UtilizationMemoryPercent,
    double MemoryUsedMiB, double MemoryTotalMiB, double PowerDrawWatts, double PowerLimitWatts,
    double SmClockMHz, double TemperatureCelsius);
internal sealed record GpuMetricRange(double Minimum, double Maximum, double Mean);
internal sealed record GpuObservationStatistics(GpuMetricRange UtilizationGpuPercent, GpuMetricRange UtilizationMemoryPercent,
    GpuMetricRange MemoryUsedMiB, GpuMetricRange MemoryTotalMiB, GpuMetricRange PowerDrawWatts,
    GpuMetricRange PowerLimitWatts, GpuMetricRange SmClockMHz, GpuMetricRange TemperatureCelsius);
internal sealed record GpuObservationEvidence(string RequestPath, string NativeReceiptPath, string? NativeReceiptSha256 = null,
    string? StdoutSha256 = null, string? StderrSha256 = null, string? ExecutedExecutable = null,
    string[]? ExecutedArguments = null, string ReadStatus = "NOT_READ", string? NativeStatus = null,
    string? NativeCleanupStatus = null, int? NativeExitCode = null);
internal sealed record GpuObservationSample(int Index, DateTimeOffset StartedUtc, DateTimeOffset CompletedUtc,
    double ElapsedSeconds, string Status, GpuObservationMetrics? Metrics, GpuObservationEvidence Evidence,
    string? Error, bool Synthetic);
internal sealed record GpuObservationReport(string Status, string StopReason, string GpuUuid, int RequestedSamples,
    int AttemptedSamples, int CompletedSamples, double ElapsedSeconds, string SamplesPath,
    GpuObservationStatistics? Statistics, string? Error, bool Synthetic,
    string Acceptance = "OBSERVATION_ONLY_NOT_SM_OCCUPANCY_OR_TENSOR_UTILIZATION",
    bool TrainingAccepted = false, bool PerformanceAccepted = false, bool ModelQualityAccepted = false);

// Tests may provide an explicitly synthetic adapter, including a genuine native
// input-echo fixture. Its executed request and original receipt remain separate
// from the production query request. RunAsync never accepts an adapter.
internal sealed record GpuObservationQueryResult(ExecutionRequest ExecutedRequest, ExecutionResult Result);
internal delegate Task<GpuObservationQueryResult> GpuObservationQuery(ExecutionRequest productionRequest,
    Budget budget, CancellationToken cancellation);

internal static class GpuObservation
{
    internal const int MaximumSamples = 120;
    internal const int MaximumCsvBytes = 4096;
    internal const int MaximumSampleBytes = 8192;
    internal const int MaximumReceiptBytes = 1_048_576;
    internal const string QueryArgument = "--query-gpu=uuid,utilization.gpu,utilization.memory,memory.used,memory.total,power.draw,power.limit,clocks.sm,temperature.gpu";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static Task<GpuObservationReport> RunAsync(GpuObservationRequest request, Budget budget, CancellationToken cancellation) =>
        RunCoreAsync(request, budget, cancellation, false, NativeQueryAsync);

    internal static Task<GpuObservationReport> RunSyntheticAsync(GpuObservationRequest request, Budget budget,
        CancellationToken cancellation, GpuObservationQuery query) => RunCoreAsync(request, budget, cancellation, true, query);

    private static async Task<GpuObservationQueryResult> NativeQueryAsync(ExecutionRequest request, Budget budget, CancellationToken cancellation) =>
        new(request, await ProcessExecutor.ExecuteAsync(request, budget, cancellation).ConfigureAwait(false));

    private static async Task<GpuObservationReport> RunCoreAsync(GpuObservationRequest request, Budget parentBudget,
        CancellationToken cancellation, bool synthetic, GpuObservationQuery query)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(query);
        var clock = Stopwatch.StartNew();
        parentBudget.Check();
        if (request.TimeoutSeconds is < 1 or > 1800 || request.TimeoutSeconds > Math.Ceiling(parentBudget.RemainingSeconds))
            throw new ArgumentException("GPU observation total wall bound must fit the parent budget.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, parentBudget.Cancellation);
        linked.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        var budget = new Budget(request.TimeoutSeconds, request.CancelFile, linked.Token);
        Validate(request, budget, linked.Token, synthetic);
        var token = linked.Token;
        void CheckWindow() { parentBudget.Check(); Check(request, budget, token, clock); }
        CheckWindow();
        var root = Path.GetFullPath(request.OutputDirectory);
        if (Path.Exists(root)) throw new IOException("GPU observation output exists; preserve it.");
        Directory.CreateDirectory(root);
        TaskOperations.WriteNew(Path.Combine(root, "request.json"), request, GpuObservationJsonContext.Default.GpuObservationRequest);
        var samplesPath = Path.Combine(root, "samples.jsonl");
        using var output = new FileStream(samplesPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var values = new List<GpuObservationMetrics>(request.Samples);
        var attempted = 0; var status = "PASS"; var reason = "REQUESTED_SAMPLES_COMPLETE"; string? errorText = null;
        try
        {
            // <=120 serial queries AND the fixed total wall deadline. A failed
            // query is never retried, and all original native evidence remains.
            for (var index = 0; index < request.Samples && index < MaximumSamples; index++)
            {
                CheckWindow();
                if (index != 0) await DelayAsync(request.IntervalMilliseconds, token, CheckWindow).ConfigureAwait(false);
                CheckWindow();
                var reserved = request.QueryTimeoutSeconds + request.CleanupSeconds + 1;
                if (request.TimeoutSeconds - clock.Elapsed.TotalSeconds < reserved || budget.RemainingSeconds < reserved || parentBudget.RemainingSeconds < reserved)
                    throw new TimeoutException("Insufficient observation wall budget for query and complete cleanup.");
                VerifyImage(request, budget, synthetic);
                var directory = Path.Combine(root, "sample-" + (index + 1).ToString("D3", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);
                var execution = CreateQuery(request, root, Path.Combine(directory, "native"));
                var requestPath = Path.Combine(directory, "query-request.json");
                TaskOperations.WriteNew(requestPath, execution, ExecutionJsonContext.Default.ExecutionRequest);
                var evidence = new GpuObservationEvidence(requestPath, Path.Combine(execution.OutputDirectory, "result.json"));
                var started = DateTimeOffset.UtcNow; var sampleClock = Stopwatch.StartNew(); attempted++;
                GpuObservationMetrics? metrics = null; Exception? failure = null;
                try
                {
                    var result = await query(execution, budget, token).ConfigureAwait(false);
                    // Preserve the raw receipt even if cancellation prevents the
                    // final evidence read; do not synthesize a native success.
                    CheckWindow();
                    var read = ReadQuery(execution, result, budget, synthetic);
                    evidence = read.Evidence with { RequestPath = requestPath };
                    RequireSuccess(read.Receipt, synthetic);
                    VerifyImage(request, budget, synthetic);
                    CheckWindow();
                    metrics = Parse(read.Stdout, request.GpuUuid, token);
                }
                catch (Exception exception) when (Expected(exception)) { failure = exception; }
                var sample = new GpuObservationSample(index + 1, started, DateTimeOffset.UtcNow, sampleClock.Elapsed.TotalSeconds,
                    failure is null ? "PASS" : "FAIL", metrics, evidence, failure is null ? null : Error(failure), synthetic);
                WriteSample(output, sample);
                if (metrics is not null) values.Add(metrics);
                Console.Error.WriteLine($"gpu observation sample={index + 1}/{request.Samples} status={sample.Status} synthetic={synthetic} elapsed={clock.Elapsed.TotalSeconds:F1}s");
                if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        catch (Exception exception) when (Expected(exception))
        {
            status = exception is OperationCanceledException ? (clock.Elapsed.TotalSeconds >= request.TimeoutSeconds ? "TIMED_OUT" : "CANCELLED") :
                exception is TimeoutException ? "TIMED_OUT" : "FAIL";
            reason = "STOPPED_WITH_PARTIAL_EVIDENCE"; errorText = Error(exception);
        }
        output.Flush(true);
        var report = new GpuObservationReport(status, reason, request.GpuUuid, request.Samples, attempted, values.Count,
            clock.Elapsed.TotalSeconds, samplesPath, Summarize(values), errorText, synthetic);
        TaskOperations.WriteNew(Path.Combine(root, "summary.json"), report, GpuObservationJsonContext.Default.GpuObservationReport);
        return report;
    }

    internal static ExecutionRequest CreateQuery(GpuObservationRequest request, string workingDirectory, string outputDirectory) => new()
    {
        Executable = request.NvidiaSmiExecutable,
        ArgumentList = ["--id=" + request.GpuUuid, QueryArgument, "--format=csv,noheader,nounits"],
        WorkingDirectory = workingDirectory, OutputDirectory = outputDirectory,
        TimeoutSeconds = request.QueryTimeoutSeconds, CleanupSeconds = request.CleanupSeconds,
        MaximumProcesses = 8, MaximumInputBytes = 0, MaximumOutputBytes = MaximumCsvBytes,
        CancelFile = request.CancelFile, BrokerExecutable = request.BrokerExecutable
    };

    private static void Validate(GpuObservationRequest request, Budget budget, CancellationToken cancellation, bool synthetic)
    {
        cancellation.ThrowIfCancellationRequested(); budget.Check();
        if (request.Samples is < 1 or > MaximumSamples || request.IntervalMilliseconds is < 100 or > 60000 ||
            request.TimeoutSeconds is < 1 or > 1800 || request.QueryTimeoutSeconds is < 1 or > 5 || request.CleanupSeconds is < 1 or > 10)
            throw new ArgumentException("GPU observation count/interval/wall/query/cleanup limits differ.");
        var reservedMilliseconds = (long)request.Samples * (request.QueryTimeoutSeconds + request.CleanupSeconds) * 1000 +
            (long)(request.Samples - 1) * request.IntervalMilliseconds + 1000;
        if (reservedMilliseconds > (long)request.TimeoutSeconds * 1000 || request.TimeoutSeconds > Math.Ceiling(budget.RemainingSeconds))
            throw new ArgumentException("GPU observation wall limit must reserve every query, cleanup, interval and final receipt.");
        RequireUuid(request.GpuUuid);
        foreach (var path in new[] { request.NvidiaSmiExecutable, request.OutputDirectory, request.CancelFile, request.BrokerExecutable })
        {
            if (path is null) continue;
            if (path.Length is < 1 or > 1024 || !Path.IsPathFullyQualified(path)) throw new ArgumentException("GPU observation paths must be explicit absolute paths within1024 characters.");
            SafePaths.NoLinks(path, budget);
        }
        if (request.CancelFile is not null && File.Exists(request.CancelFile)) throw new OperationCanceledException("GPU observation cancellation file already exists.");
        if (!synthetic && OperatingSystem.IsLinux() && request.BrokerExecutable is null)
            throw new ArgumentException("Linux GPU observation requires the explicit C# process broker.");
        VerifyImage(request, budget, synthetic);
    }

    private static void VerifyImage(GpuObservationRequest request, Budget budget, bool synthetic)
    {
        if (!synthetic)
        {
            var expected = OperatingSystem.IsWindows() ? "nvidia-smi.exe" : OperatingSystem.IsLinux() ? "nvidia-smi" : throw new PlatformNotSupportedException();
            if (!Path.GetFileName(request.NvidiaSmiExecutable).Equals(expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Production observation requires the pinned native nvidia-smi image.");
            ProcessExecutor.ValidateExecutable(request.NvidiaSmiExecutable, budget);
        }
        TaskOperations.VerifyFile(request.NvidiaSmiExecutable, new FileInfo(request.NvidiaSmiExecutable).Length, request.NvidiaSmiSha256, budget);
    }

    private static void RequireUuid(string uuid)
    {
        if (uuid is null || uuid.Length != 40 || !uuid.StartsWith("GPU-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(uuid.AsSpan(4), "D", out var identity) || identity == Guid.Empty)
            throw new ArgumentException("One complete nonzero GPU UUID is required; indices and prefixes are rejected.");
    }

    internal static GpuObservationMetrics Parse(byte[] bytes, string expectedUuid, CancellationToken cancellation = default)
    {
        var clock = Stopwatch.StartNew(); cancellation.ThrowIfCancellationRequested();
        RequireUuid(expectedUuid);
        if (bytes.Length is < 1 or > MaximumCsvBytes) throw new InvalidDataException("GPU CSV byte bound1..4096.");
        var text = Utf8.GetString(bytes).Trim(' ', '\t', '\r', '\n');
        if (text.Length == 0 || text.IndexOfAny(['\r', '\n', '\0', '"']) >= 0) throw new InvalidDataException("Exactly one unquoted GPU CSV row is required.");
        var fields = text.Split(',', StringSplitOptions.TrimEntries);
        if (fields.Length != 9 || !fields[0].Equals(expectedUuid, StringComparison.Ordinal)) throw new InvalidDataException("GPU CSV must have exactly9 columns and the exact selected UUID.");
        Span<double> values = stackalloc double[8];
        for (var index = 0; index < 8; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 1) throw new TimeoutException("GPU CSV parser exceeded8 fields/1s.");
            if (!double.TryParse(fields[index + 1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out values[index]) || !double.IsFinite(values[index]))
                throw new InvalidDataException("GPU CSV contains missing/nonfinite/nonnumeric or unit-bearing data.");
        }
        if (values[0] is < 0 or > 100 || values[1] is < 0 or > 100 || values[2] < 0 || values[2] > values[3] ||
            values[3] is <= 0 or > 16777216 || values[4] is < 0 or > 10000 || values[5] is <= 0 or > 10000 ||
            values[6] is < 0 or > 10000 || values[7] is < 0 or > 150)
            throw new InvalidDataException("GPU CSV physical range or used/total memory relation differs.");
        return new(fields[0], values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]);
    }

    private static (GpuObservationEvidence Evidence, byte[] Stdout, ExecutionResult Receipt) ReadQuery(ExecutionRequest requested,
        GpuObservationQueryResult query, Budget budget, bool synthetic)
    {
        var executed = query.ExecutedRequest; var returned = query.Result;
        if (executed.OutputDirectory != requested.OutputDirectory || executed.WorkingDirectory != requested.WorkingDirectory ||
            executed.TimeoutSeconds != requested.TimeoutSeconds || executed.CleanupSeconds != requested.CleanupSeconds ||
            executed.MaximumOutputBytes != requested.MaximumOutputBytes || executed.MaximumProcesses != requested.MaximumProcesses ||
            (!synthetic && (executed.Executable != requested.Executable || !executed.ArgumentList.SequenceEqual(requested.ArgumentList) || executed.StandardInput.Length != 0)))
            throw new InvalidDataException("GPU adapter changed the fixed native query or resource/output binding.");
        var receiptPath = Path.Combine(requested.OutputDirectory, "result.json");
        var receiptBytes = ReadBounded(receiptPath, MaximumReceiptBytes, budget);
        using var document = JsonDocument.Parse(receiptBytes, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 32 });
        var receipt = document.RootElement.Deserialize(ExecutionJsonContext.Default.ExecutionResult) ?? throw new InvalidDataException("Native GPU receipt is null.");
        if (receipt.ArgumentList is null || receipt.Errors is null || returned.Status != receipt.Status || returned.OwnershipId != receipt.OwnershipId || returned.RootPid != receipt.RootPid ||
            receipt.Executable != executed.Executable || !receipt.ArgumentList.SequenceEqual(executed.ArgumentList) ||
            receipt.WorkingDirectory != executed.WorkingDirectory || receipt.OutputDirectory != executed.OutputDirectory)
            throw new InvalidDataException("GPU native receipt does not bind the actual executed query.");
        var stdoutPath = Path.Combine(requested.OutputDirectory, "stdout.bin");
        var stdout = ReadBounded(stdoutPath, MaximumCsvBytes, budget);
        var stderrPath = Path.Combine(requested.OutputDirectory, "stderr.bin");
        var stderr = ReadBounded(stderrPath, MaximumCsvBytes, budget);
        if (receipt.StandardOutputBytes != stdout.LongLength || receipt.StandardErrorBytes != stderr.LongLength || receipt.StandardInputBytes != executed.StandardInput.LongLength)
            throw new InvalidDataException("GPU native receipt input/stdout/stderr byte counts differ from original evidence.");
        // Hash the exact bytes decoded above, rather than a later independent
        // file read that could accidentally describe different raw bytes.
        return (new("", receiptPath, Digest(receiptBytes), Digest(stdout), Digest(stderr),
            executed.Executable, executed.ArgumentList.ToArray(), "READ_AND_BOUND", receipt.Status, receipt.CleanupStatus, receipt.ExitCode), stdout, receipt);
    }

    private static void RequireSuccess(ExecutionResult receipt, bool synthetic)
    {
        var success = receipt.Status == "PASS" && receipt.ExecutionStatus == "SUCCEEDED" ||
            synthetic && receipt.Status == "SYNTHETIC" && receipt.ExecutionStatus == "SYNTHETIC";
        if (!success || receipt.ExitCode != 0 || receipt.CleanupStatus != "PASS" || receipt.RemainingProcesses != 0 ||
            !receipt.OutputComplete || !receipt.InputComplete || receipt.OutputLimitExceeded || receipt.Errors.Length != 0)
            throw new IOException("GPU query failed or native EOF/input/cleanup evidence is incomplete; original receipt is preserved.");
    }

    private static byte[] ReadBounded(string path, int maximum, Budget budget)
    {
        RegularFiles.Require(path, budget);
        using var input = File.OpenRead(path);
        if (input.Length < 0 || input.Length > maximum) throw new InvalidDataException("GPU evidence byte bound exceeded.");
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new InvalidDataException("GPU evidence grew during read.");
        budget.Check(); return bytes;
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void WriteSample(FileStream output, GpuObservationSample sample)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(sample, GpuObservationJsonContext.Default.GpuObservationSample);
        if (bytes.Length > MaximumSampleBytes || output.Length + bytes.Length + 1 > MaximumSamples * (MaximumSampleBytes + 1L))
            throw new IOException("GPU observation JSONL line/total byte limit exceeded.");
        output.Write(bytes); output.WriteByte((byte)'\n'); output.Flush(true);
    }

    private static async Task DelayAsync(int intervalMilliseconds, CancellationToken cancellation, Action check)
    {
        var wait = Stopwatch.StartNew();
        // <=602 chunks AND <=60 seconds; cancellation/total wall check every100ms.
        for (var chunk = 0; chunk < 602 && wait.Elapsed.TotalMilliseconds < intervalMilliseconds; chunk++)
        {
            check();
            var delay = Math.Min(100, intervalMilliseconds - wait.Elapsed.TotalMilliseconds);
            if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(delay))), cancellation).ConfigureAwait(false);
        }
        if (wait.Elapsed.TotalMilliseconds < intervalMilliseconds) throw new TimeoutException("GPU observation cadence wait bound exhausted.");
    }

    private static void Check(GpuObservationRequest request, Budget budget, CancellationToken cancellation, Stopwatch clock)
    {
        cancellation.ThrowIfCancellationRequested(); budget.Check();
        if (request.CancelFile is not null && File.Exists(request.CancelFile)) throw new OperationCanceledException("GPU observation cancellation file observed.");
        if (clock.Elapsed.TotalSeconds >= request.TimeoutSeconds) throw new TimeoutException("GPU observation total wall deadline exceeded.");
    }

    private static GpuObservationStatistics? Summarize(List<GpuObservationMetrics> values)
    {
        if (values.Count == 0) return null;
        // Each reduction has a fixed input upper bound of120 successful rows.
        GpuMetricRange Range(Func<GpuObservationMetrics, double> select) => new(values.Min(select), values.Max(select), values.Average(select));
        return new(Range(x => x.UtilizationGpuPercent), Range(x => x.UtilizationMemoryPercent), Range(x => x.MemoryUsedMiB),
            Range(x => x.MemoryTotalMiB), Range(x => x.PowerDrawWatts), Range(x => x.PowerLimitWatts),
            Range(x => x.SmClockMHz), Range(x => x.TemperatureCelsius));
    }

    private static string Error(Exception exception)
    {
        var text = exception.GetType().Name + ": " + exception.Message;
        return text.Length <= 1024 ? text : text[..1024];
    }

    private static bool Expected(Exception exception) => exception is IOException or InvalidDataException or ArgumentException or OperationCanceledException or TimeoutException or JsonException;
}

[JsonSerializable(typeof(GpuObservationRequest))]
[JsonSerializable(typeof(GpuObservationReport))]
[JsonSerializable(typeof(GpuObservationSample))]
[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
internal partial class GpuObservationJsonContext : JsonSerializerContext;
