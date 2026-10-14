using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sezika.Tooling;

internal static class GpuObservationChecks
{
    internal const int SyntheticCheckCount = 20;
    internal const int NativeCheckCount = 1;
    private const string Uuid = "GPU-12345678-1234-5678-9abc-123456789abc";
    private const string Csv = Uuid + ", 10, 20, 1024, 24576, 80.5, 450, 2400, 55\n";
    private static readonly UTF8Encoding Utf8 = new(false);

    internal static void Run(string root, Action<string, Action> check)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var clock = Stopwatch.StartNew();
        var owned = Path.Combine(root, "gpu-observation-synthetic"); Directory.CreateDirectory(owned);
        var image = Path.Combine(owned, "inert-query-image.bin");
        File.WriteAllText(image, "SYNTHETIC inert bytes; no GPU or native process exists.\n", Utf8);
        var imageSha = Sha(File.ReadAllBytes(image));
        var calls = 0;
        void Gate()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds >= 30) throw new TimeoutException("GPU synthetic checks deadline30s.");
        }
        GpuObservationRequest Request(string name) => new(image, imageSha, Uuid, Path.Combine(owned, name),
            IntervalMilliseconds: 100, TimeoutSeconds: 15, QueryTimeoutSeconds: 1, CleanupSeconds: 1);
        GpuObservationReport Run(GpuObservationRequest request, GpuObservationQuery query, CancellationToken? token = null)
        {
            Gate(); var actual = token ?? cancellation.Token;
            return GpuObservation.RunSyntheticAsync(request, new Budget(20, null, actual), actual, query).GetAwaiter().GetResult();
        }
        GpuObservationQuery Fixture(string csv = Csv, string status = "SYNTHETIC", int exit = 0,
            bool eof = true, string cleanup = "PASS", bool receipt = true, bool overflow = false)
        {
            return (request, budget, token) =>
            {
                Gate(); token.ThrowIfCancellationRequested(); budget.Check();
                Assert(++calls <= 32, "GPU synthetic query count bound32.");
                AssertProductionQuery(request, Uuid);
                Directory.CreateDirectory(request.OutputDirectory);
                File.WriteAllText(Path.Combine(request.OutputDirectory, "stdout.bin"), csv, Utf8);
                File.WriteAllText(Path.Combine(request.OutputDirectory, "stderr.bin"), "SYNTHETIC_NO_GPU\n", Utf8);
                var result = new ExecutionResult
                {
                    Status = status, ExecutionStatus = "SYNTHETIC", CleanupStatus = cleanup, ExitCode = exit,
                    Executable = request.Executable, ArgumentList = request.ArgumentList.ToArray(),
                    WorkingDirectory = request.WorkingDirectory, OutputDirectory = request.OutputDirectory,
                    OwnershipId = "SYNTHETIC_NO_NATIVE_PROCESS", StartedUtc = DateTimeOffset.UtcNow,
                    InputComplete = true, OutputComplete = eof, OutputLimitExceeded = overflow,
                    StandardOutputBytes = Utf8.GetByteCount(csv), StandardErrorBytes = Utf8.GetByteCount("SYNTHETIC_NO_GPU\n"),
                    Errors = exit == 0 ? [] : ["SYNTHETIC exit failure"]
                };
                if (receipt) TaskOperations.WriteNew(Path.Combine(request.OutputDirectory, "result.json"), result, ExecutionJsonContext.Default.ExecutionResult);
                return Task.FromResult(new GpuObservationQueryResult(request, result));
            };
        }
        void FailedCsv(string name, string csv)
        {
            var request = Request(name); var report = Run(request, Fixture(csv));
            Assert(report.Status == "FAIL" && report.AttemptedSamples == 1 && report.CompletedSamples == 0 && report.Synthetic, "Malformed CSV became a successful observation.");
            var raw = Path.Combine(request.OutputDirectory, "sample-001", "native", "stdout.bin");
            Assert(File.ReadAllText(raw, Utf8) == csv, "Original failed GPU CSV was rewritten.");
            var row = ReadSample(report.SamplesPath);
            Assert(row.Status == "FAIL" && row.Metrics is null && row.Synthetic && File.Exists(row.Evidence.NativeReceiptPath), "Rejected sample lost raw receipt or failure semantics.");
        }

        // Tiny synthetic first; the shared test runner may select only this check.
        check("GPU observation synthetic tiny9 keeps production argv raw pins and observation-only labels", () =>
        {
            var request = Request("tiny"); var report = Run(request, Fixture()); var row = ReadSample(report.SamplesPath);
            Assert(report.Status == "PASS" && report.Synthetic && report.RequestedSamples == 1 && report.AttemptedSamples == 1 && report.CompletedSamples == 1 &&
                !report.TrainingAccepted && !report.PerformanceAccepted && !report.ModelQualityAccepted && report.Acceptance.Contains("NOT_SM_OCCUPANCY_OR_TENSOR", StringComparison.Ordinal), "Synthetic observation became GPU/training/performance acceptance.");
            Assert(row.Metrics == new GpuObservationMetrics(Uuid, 10, 20, 1024, 24576, 80.5, 450, 2400, 55), "Nine-column mapping or units differ.");
            Assert(row.Evidence.ReadStatus == "READ_AND_BOUND" && row.Evidence.StdoutSha256 == Sha(Utf8.GetBytes(Csv)) &&
                row.Evidence.NativeReceiptSha256 == Sha(File.ReadAllBytes(row.Evidence.NativeReceiptPath)), "Observation did not bind original stdout/native receipt bytes.");
            Assert(File.Exists(Path.Combine(request.OutputDirectory, "request.json")) && File.Exists(Path.Combine(request.OutputDirectory, "summary.json")), "Request or summary evidence missing.");
            using var source = JsonDocument.Parse(File.ReadAllBytes(row.Evidence.NativeReceiptPath));
            Assert(source.RootElement.GetProperty("Status").GetString() == "SYNTHETIC" && source.RootElement.GetProperty("RootPid").ValueKind == JsonValueKind.Null, "Synthetic metadata fabricated a native PASS/PID.");
        });
        check("GPU observation synthetic two rows keep cadence fixed denominator and statistics", () =>
        {
            var request = Request("two") with { Samples = 2 };
            var first = Fixture(); var second = Fixture(Csv.Replace(", 10,", ", 30,", StringComparison.Ordinal)); var sequence = 0;
            var watch = Stopwatch.StartNew();
            var report = Run(request, (execution, budget, token) => (++sequence == 1 ? first : second)(execution, budget, token));
            Assert(report.Status == "PASS" && report.CompletedSamples == 2 && report.RequestedSamples == 2 && sequence == 2 && watch.Elapsed.TotalMilliseconds >= 100,
                "GPU sampler changed count or skipped minimum cadence.");
            Assert(report.Statistics is { UtilizationGpuPercent.Minimum: 10, UtilizationGpuPercent.Maximum: 30, UtilizationGpuPercent.Mean: 20 }, "GPU observation summary differs.");
            Assert(File.ReadAllLines(report.SamplesPath).Length == 2, "GPU observation JSONL denominator differs.");
        });
        check("GPU observation rejects another exact UUID and preserves CSV", () => FailedCsv("uuid", Csv.Replace(Uuid, "GPU-12345678-1234-5678-9abc-123456789abd", StringComparison.Ordinal)));
        check("GPU observation rejects NaN and preserves CSV", () => FailedCsv("nan", Csv.Replace(", 10,", ", NaN,", StringComparison.Ordinal)));
        check("GPU observation rejects non-nine-column rows", () => FailedCsv("columns", Csv.Replace(", 55", "", StringComparison.Ordinal)));
        check("GPU observation rejects duplicate or multi-card rows", () => FailedCsv("duplicate", Csv + Csv));
        check("GPU observation rejects attached units", () => FailedCsv("units", Csv.Replace("1024", "1024 MiB", StringComparison.Ordinal)));
        check("GPU observation rejects unavailable sensor fields", () => FailedCsv("unavailable", Csv.Replace("80.5", "[N/A]", StringComparison.Ordinal)));
        check("GPU observation finite physical ranges and used-total relation reject", () =>
        {
            foreach (var csv in new[]
            {
                Csv.Replace(", 10,", ", 101,", StringComparison.Ordinal), Csv.Replace(", 20,", ", 101,", StringComparison.Ordinal),
                Csv.Replace("1024, 24576", "24577, 24576", StringComparison.Ordinal), Csv.Replace("24576", "0", StringComparison.Ordinal),
                Csv.Replace("80.5", "Infinity", StringComparison.Ordinal), Csv.Replace("450", "0", StringComparison.Ordinal),
                Csv.Replace("2400", "10001", StringComparison.Ordinal), Csv.Replace(", 55", ", 151", StringComparison.Ordinal)
            }) { Gate(); Reject(() => GpuObservation.Parse(Utf8.GetBytes(csv), Uuid)); }
        });
        check("GPU observation rejects partial index malformed and zero UUID identities", () =>
        {
            foreach (var uuid in new[] { "0", "GPU-1234", "GPU-00000000-0000-0000-0000-000000000000", "GPU-12345678123456789abc123456789abc" })
            {
                Gate(); var before = calls; Reject(() => Run(Request("invalid-uuid-" + uuid.Length) with { GpuUuid = uuid }, Fixture()));
                Assert(calls == before, "Invalid UUID launched a query.");
            }
        });
        check("GPU observation validates count interval query cleanup and total wall reservation before launch", () =>
        {
            var request = Request("limits");
            foreach (var invalid in new[]
            {
                request with { Samples = 0 }, request with { Samples = 121 }, request with { IntervalMilliseconds = 99 },
                request with { IntervalMilliseconds = 60001 }, request with { QueryTimeoutSeconds = 0 }, request with { QueryTimeoutSeconds = 6 },
                request with { CleanupSeconds = 0 }, request with { CleanupSeconds = 11 }, request with { TimeoutSeconds = 1801 },
                request with { TimeoutSeconds = 2 }, request with { Samples = 120, TimeoutSeconds = 15 }
            }) { Gate(); var before = calls; Reject(() => Run(invalid, Fixture())); Assert(calls == before, "Invalid resource bound launched a query."); }
        });
        check("GPU observation rejects existing output without overwriting evidence", () =>
        {
            var request = Request("existing"); Directory.CreateDirectory(request.OutputDirectory);
            var marker = Path.Combine(request.OutputDirectory, "keep.txt"); File.WriteAllText(marker, "preserved", Utf8);
            var before = calls; Reject(() => Run(request, Fixture()));
            Assert(calls == before && File.ReadAllText(marker, Utf8) == "preserved", "Existing output was reused or altered.");
        });
        check("GPU observation rejects SHA mismatch and production inert image alias before native query", () =>
        {
            var before = calls; Reject(() => Run(Request("wrong-sha") with { NvidiaSmiSha256 = new string('0', 64) }, Fixture()));
            Assert(calls == before, "Wrong executable SHA launched a query.");
            var production = Request("production-inert-reject");
            Reject(() => GpuObservation.RunAsync(production, new Budget(20, null, cancellation.Token), cancellation.Token).GetAwaiter().GetResult());
            Assert(!Directory.Exists(production.OutputDirectory), "Production entry admitted a synthetic image.");
        });
        check("GPU observation cancellation before admission creates no query", () =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var before = calls;
            var request = Request("pre-cancel"); Reject(() => Run(request, Fixture(), cancelled.Token));
            Assert(calls == before && !Directory.Exists(request.OutputDirectory), "Pre-cancelled observation launched or created output.");
        });
        check("GPU observation cancellation between samples preserves the first complete row", () =>
        {
            var request = Request("cancel-cadence") with { Samples = 2, IntervalMilliseconds = 1000, CancelFile = Path.Combine(owned, "CANCEL-cadence.signal") };
            using var signalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            var signal = Task.Run(async () =>
            {
                var wait = Stopwatch.StartNew();
                for (var poll = 0; poll < 200 && wait.Elapsed.TotalSeconds < 5; poll++)
                {
                    signalCancellation.Token.ThrowIfCancellationRequested();
                    var path = Path.Combine(request.OutputDirectory, "samples.jsonl");
                    if (File.Exists(path) && new FileInfo(path).Length > 0)
                    {
                        using var file = new FileStream(request.CancelFile!, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                        file.Write(Utf8.GetBytes("SYNTHETIC_CANCEL_ONLY\n")); file.Flush(true); return;
                    }
                    await Task.Delay(25, signalCancellation.Token).ConfigureAwait(false);
                }
                throw new TimeoutException("Synthetic cancellation signal exceeded200polls/5s.");
            }, signalCancellation.Token);
            try
            {
                var report = Run(request, Fixture());
                Assert(report.Status == "CANCELLED" && report.AttemptedSamples == 1 && report.CompletedSamples == 1 && File.Exists(request.CancelFile), "Cadence cancellation did not preserve exactly one completed sample.");
                signal.GetAwaiter().GetResult();
            }
            finally
            {
                signalCancellation.Cancel();
                try { signal.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            }
        });
        check("GPU observation preserves original failed native receipt stderr and partial row", () =>
        {
            var request = Request("native-failure"); var report = Run(request, Fixture(status: "FAIL", exit: 7));
            var native = Path.Combine(request.OutputDirectory, "sample-001", "native");
            var receipt = Path.Combine(native, "result.json"); var original = File.ReadAllBytes(receipt);
            var sample = ReadSample(report.SamplesPath);
            Assert(report.Status == "FAIL" && report.CompletedSamples == 0 && report.AttemptedSamples == 1 && sample.Status == "FAIL" && sample.Evidence is { NativeStatus: "FAIL", NativeExitCode: 7 }, "Failed query became an observation or lost its original failure state.");
            using var json = JsonDocument.Parse(original);
            Assert(json.RootElement.GetProperty("Status").GetString() == "FAIL" && json.RootElement.GetProperty("ExitCode").GetInt32() == 7 &&
                File.ReadAllText(Path.Combine(native, "stderr.bin"), Utf8) == "SYNTHETIC_NO_GPU\n" && File.ReadAllBytes(receipt).SequenceEqual(original), "Native failure or stderr was rewritten.");
        });
        check("GPU observation rejects unproven output EOF", () =>
        {
            var report = Run(Request("eof"), Fixture(eof: false)); Assert(report.Status == "FAIL" && report.CompletedSamples == 0, "Unproven EOF accepted.");
        });
        check("GPU observation rejects unknown native cleanup", () =>
        {
            var report = Run(Request("cleanup"), Fixture(cleanup: "UNKNOWN"));
            Assert(report.Status == "FAIL" && report.CompletedSamples == 0 && ReadSample(report.SamplesPath).Evidence.NativeCleanupStatus == "UNKNOWN", "Unknown cleanup accepted or hidden.");
        });
        check("GPU observation refuses returned metadata without original native receipt", () =>
        {
            var request = Request("missing-receipt"); var report = Run(request, Fixture(receipt: false));
            Assert(report.Status == "FAIL" && report.CompletedSamples == 0 && File.Exists(Path.Combine(request.OutputDirectory, "sample-001", "native", "stdout.bin")), "Missing native receipt accepted or original output lost.");
        });
        check("GPU observation bounds raw output overflow and JSONL bytes", () =>
        {
            var report = Run(Request("overflow"), Fixture(new string('x', GpuObservation.MaximumCsvBytes + 1), overflow: true));
            Assert(report.Status == "FAIL" && report.CompletedSamples == 0 && new FileInfo(report.SamplesPath).Length <= GpuObservation.MaximumSampleBytes + 1L,
                "Output overflow became accepted or escaped JSONL byte bound.");
        });
    }

    // Separate suite: genuine native process lifecycle, explicitly synthetic CSV.
    // This does not query a driver or create a production command-line bypass.
    internal static void RunNative(string root, Action<string, Action> check)
    {
        check("GPU observation genuine native input echo retains production argv and exact original receipt as synthetic", () =>
        {
            var fixture = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_FIXTURE") ?? throw new InvalidOperationException("Explicit already-built SEZIKA_PROCESS_FIXTURE required.");
            var broker = Environment.GetEnvironmentVariable("SEZIKA_PROCESS_BROKER");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var budget = new Budget(25, null, cancellation.Token);
            ProcessExecutor.ValidateExecutable(fixture, budget);
            var request = new GpuObservationRequest(fixture, Sha(File.ReadAllBytes(fixture)), Uuid, Path.Combine(root, "gpu-observation-native-synthetic"),
                IntervalMilliseconds: 100, TimeoutSeconds: 20, QueryTimeoutSeconds: 5, CleanupSeconds: 5, BrokerExecutable: broker);
            ExecutionResult? original = null;
            async Task<GpuObservationQueryResult> Echo(ExecutionRequest production, Budget operation, CancellationToken token)
            {
                AssertProductionQuery(production, Uuid);
                var executed = new ExecutionRequest
                {
                    Executable = fixture, ArgumentList = ["input"], StandardInput = Utf8.GetBytes(Csv), MaximumInputBytes = 1024,
                    WorkingDirectory = production.WorkingDirectory, OutputDirectory = production.OutputDirectory,
                    TimeoutSeconds = production.TimeoutSeconds, CleanupSeconds = production.CleanupSeconds,
                    MaximumProcesses = production.MaximumProcesses, MaximumOutputBytes = production.MaximumOutputBytes,
                    CancelFile = production.CancelFile, BrokerExecutable = production.BrokerExecutable
                };
                original = await ProcessExecutor.ExecuteAsync(executed, operation, token).ConfigureAwait(false);
                return new(executed, original);
            }
            var report = GpuObservation.RunSyntheticAsync(request, budget, cancellation.Token, Echo).GetAwaiter().GetResult();
            Assert(report.Status == "PASS" && report.Synthetic && report.CompletedSamples == 1 && !report.TrainingAccepted && !report.PerformanceAccepted,
                "Native fixture was promoted to real GPU acceptance.");
            Assert(original is { Status: "PASS", CleanupStatus: "PASS", RemainingProcesses: 0, OutputComplete: true, InputComplete: true, ExitCode: 0 } &&
                original.Processes.Length is >= 1 and <= 8 && original.RootPid is > 0, "Native fixture identity/EOF/cleanup incomplete.");
            var row = ReadSample(report.SamplesPath);
            Assert(row.Synthetic && row.Evidence.ExecutedExecutable == fixture && row.Evidence.ExecutedArguments!.SequenceEqual(new[] { "input" }) &&
                row.Evidence.NativeReceiptSha256 == Sha(File.ReadAllBytes(row.Evidence.NativeReceiptPath)), "Native fixture original receipt or executed argv was hidden/changed.");
            foreach (var identity in original!.Processes)
            {
                budget.Check(); var current = ProcessObserver.Observe(identity.Pid);
                var reused = current.State == "Present" && (identity.Platform == "windows"
                    ? current.Started is not null && identity.Started is not null && current.Started != identity.Started
                    : current.StartTicks is not null && identity.StartTicks is not null && (current.StartTicks != identity.StartTicks || current.BootId != identity.BootId));
                Assert(current.State == "Absent" || reused, "Native fixture process still matches its recorded birth or absence is unknown.");
            }
        });
    }

    private static void AssertProductionQuery(ExecutionRequest request, string uuid) => Assert(
        request.ArgumentList.SequenceEqual(new[] { "--id=" + uuid, GpuObservation.QueryArgument, "--format=csv,noheader,nounits" }) &&
        request.StandardInput.Length == 0 && request.MaximumInputBytes == 0 && request.MaximumOutputBytes == GpuObservation.MaximumCsvBytes &&
        request.MaximumProcesses == 8 && request.TimeoutSeconds is >= 1 and <= 5 && request.CleanupSeconds is >= 1 and <= 10,
        "Fixed production query argv/input/process/output/time bounds differ.");

    private static GpuObservationSample ReadSample(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert(bytes.Length > 0 && bytes.Length <= GpuObservation.MaximumSampleBytes + 1 && bytes[^1] == '\n', "Expected one bounded JSONL sample row.");
        return JsonSerializer.Deserialize(bytes.AsSpan(0, bytes.Length - 1), GpuObservationJsonContext.Default.GpuObservationSample) ?? throw new InvalidDataException("Sample is null.");
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        var rejected = false;
        try { action(); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OperationCanceledException or TimeoutException) { rejected = true; }
        Assert(rejected, "Expected GPU observation rejection did not occur.");
    }
}
