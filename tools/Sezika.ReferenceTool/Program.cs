using System.Text.Json;

namespace Sezika.ReferenceTool;

internal static class Program
{
    internal static int Main(string[] args)
    {
        if (args is ["--help"] or [])
        {
            Console.WriteLine("Sezika.ReferenceTool capture --request <v2-json> --output <new-receipt.json> --timeout-seconds <1..1800> [--cancel-file <absolute-path>]");
            Console.WriteLine("Sezika.ReferenceTool laya|tokenizer --request <typed-port-json> --output <new-receipt.json> --timeout-seconds <1..1800> [--cancel-file <absolute-path>]");
            Console.WriteLine("Sezika.ReferenceTool build-proof --request <typed-build-proof-json> --output <new-summary.json> --timeout-seconds 30");
            Console.WriteLine("Sezika.ReferenceTool proof-checks --request <pinned-fixture-json> --output <new-report.json> --maximum-cases <1..8> --timeout-seconds 30");
            Console.WriteLine("Sezika.ReferenceTool laya-input-checks --request <pinned-partial-conformance-json> --output <new-report.json> --timeout-seconds 60");
            Console.WriteLine("Sezika.ReferenceTool self-test --maximum-cases <1..8> --output <new-json> --timeout-seconds <1..120> [--cancel-file <absolute-path>]");
            Console.WriteLine("Standalone C# port-derived reference; no production core, Python, scripts, network or model quality acceptance."); return 0;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler; string? output = null;
        try
        {
            if (args.Length is < 5 or > 11 || (args.Length & 1) != 1 || args.Any(value => value.Length > 32768) || args[0] is not ("capture" or "self-test" or "laya" or "tokenizer" or "build-proof" or "proof-checks" or "laya-input-checks")) throw new ArgumentException("Reference command/count bounds differ.");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < args.Length; index += 2)
            {
                if (args[index] is not ("--request" or "--output" or "--timeout-seconds" or "--cancel-file" or "--maximum-cases") || !options.TryAdd(args[index], args[index + 1]) || string.IsNullOrWhiteSpace(args[index + 1])) throw new ArgumentException("Duplicate/unknown reference option.");
            }
            string Required(string name) => options.TryGetValue(name, out var value) ? value : throw new ArgumentException("Missing " + name);
            var seconds = int.Parse(Required("--timeout-seconds"), System.Globalization.CultureInfo.InvariantCulture);
            if (seconds < 1 || seconds > (args[0] == "self-test" ? 120 : 1800)) throw new ArgumentException("Reference CLI wall bound differs.");
            var cancelFile = options.GetValueOrDefault("--cancel-file"); if (cancelFile is not null) Budget.Safe(cancelFile);
            cancellation.CancelAfter(TimeSpan.FromSeconds(seconds)); var budget = new Budget(seconds, 6442450944, cancelFile, cancellation.Token);
            output = Path.GetFullPath(Required("--output")); Budget.Safe(output);
            if (Path.Exists(output) || !Directory.Exists(Path.GetDirectoryName(output))) throw new IOException("Reference receipt requires new file/existing parent.");
            if (args[0] == "build-proof")
            {
                if (seconds != 30 || options.ContainsKey("--maximum-cases") || cancelFile is not null) throw new ArgumentException("Build proof requires its fixed30-second profile and explicit build request.");
                return Provenance.BuildProofExporter.Run(Path.GetFullPath(Required("--request")), output, cancellation.Token);
            }
            if (args[0] == "proof-checks")
            {
                if (seconds != 30 || cancelFile is not null) throw new ArgumentException("Proof checks require their fixed30-second fixture profile.");
                var maximum = int.Parse(Required("--maximum-cases"), System.Globalization.CultureInfo.InvariantCulture);
                return Provenance.ProofChecks.Run(Path.GetFullPath(Required("--request")), output, maximum, cancellation.Token);
            }
            if (args[0] == "self-test")
            {
                if (options.ContainsKey("--request")) throw new ArgumentException("Self-test does not accept model requests.");
                var maximum = int.Parse(Required("--maximum-cases"), System.Globalization.CultureInfo.InvariantCulture);
                if (maximum is < 1 or > 8) throw new ArgumentException("Reference checks bound1..8.");
                var report = Checks(maximum, budget); Budget.WriteNew(output, JsonSerializer.SerializeToUtf8Bytes(report, ReferenceJson.Default.CheckReport));
                return report.Cases.All(item => item.Passed) ? 0 : 1;
            }
            if (args[0] == "laya-input-checks")
            {
                if (seconds != 60 || options.ContainsKey("--maximum-cases") || cancelFile is not null)
                    throw new ArgumentException("Laya partial conformance uses its fixed60-second profile and request-bound row/cancellation limits.");
                return Laya.LayaInputChecks.Run(Path.GetFullPath(Required("--request")), output, cancellation.Token);
            }
            if (options.ContainsKey("--maximum-cases")) throw new ArgumentException("Capture admits exactly one fixed46-token case.");
            if (args[0] == "laya") return Laya.LayaReferenceExport.Run(Path.GetFullPath(Required("--request")), output, cancellation.Token);
            if (args[0] == "tokenizer") return Tokenization.TokenizerExport.Run(Path.GetFullPath(Required("--request")), output, cancellation.Token);
            var pin = budget.Pin(Path.GetFullPath(Required("--request")), 1048576);
            var request = budget.Read(pin, ReferenceJson.Default.ExportRequest);
            if (request.TimeoutSeconds > seconds || request.CancelFile != cancelFile) throw new InvalidDataException("Reference request/CLI deadline or cancellation identity differs.");
            var result = Exporter.Run(request, cancellation.Token); Budget.WriteNew(output, JsonSerializer.SerializeToUtf8Bytes(result, ReferenceJson.Default.ExportReceipt));
            return result.Status == "CAPTURE_COMPLETE_COMPARISON_NOT_RUN" ? 0 : 1;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException or TimeoutException or OperationCanceledException or OverflowException or FormatException or KeyNotFoundException or InvalidOperationException or BadImageFormatException)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            if (output is not null && !Path.Exists(output))
            {
                try { Budget.WriteNew(output, JsonSerializer.SerializeToUtf8Bytes(new ExportReceipt("FAIL_REFERENCE_NOT_COMPLETE", "", null, 0, 0, 0, 0, error.GetType().Name + ": " + error.Message), ReferenceJson.Default.ExportReceipt)); }
                catch (Exception receiptError) when (receiptError is IOException or InvalidDataException or UnauthorizedAccessException) { Console.Error.WriteLine("Failure receipt could not be written: " + receiptError.GetType().Name); }
            }
            return error is OperationCanceledException or TimeoutException ? 124 : 2;
        }
        finally { Console.CancelKeyPress -= handler; }
    }
    private static CheckReport Checks(int maximum, Budget budget)
    {
        // Literal analytic answers; no production math or measured model outputs.
        (string Name, Action Test)[] cases =
        [
            ("linear-row-major-and-vector-reduction", () =>
            {
                Equal(ModernBertReference.Linear([3, 4], 1, 2, [1, 2, 3, 4], 2, budget), [11, 25], 0);
                Equal(ModernBertReference.Linear([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32], 1, 32, Enumerable.Repeat(1f, 32).ToArray(), 1, budget), [528], 0);
            }),
            ("population-layernorm", () => Equal(ModernBertReference.Normalize([1, 3], 1, 2, [1, 2], 0, budget), [-1, 2], 0)),
            ("half-split-rope", () => { float[] values = [1, 2, 3, 4]; ModernBertReference.RotatePair(values, 0, 2, 0, 1); ModernBertReference.RotatePair(values, 1, 2, 0, 1); Equal(values, [-3, -4, 1, 2], 0); }),
            ("erf-gelu", () => Equal([ModernBertReference.Gelu(0), ModernBertReference.Gelu(1), ModernBertReference.Gelu(-1)], [0, .841344746f, -.158655254f], .0000002f)),
            ("stable-softmax", () => Equal(ModernBertReference.Softmax([1000, 1000]), [.5f, .5f], 0)),
            ("global-uniform-attention", () => Equal(ModernBertReference.Attention([0, 0, 0, 0, 1, 2, 0, 0, 0, 0, 3, 4], 2, 1, 2, -1, budget), [2, 3, 2, 3], 0)),
            ("local-self-attention", () => Equal(ModernBertReference.Attention([0, 0, 0, 0, 1, 2, 0, 0, 0, 0, 3, 4], 2, 1, 2, 0, budget), [1, 2, 3, 4], 0)),
            ("non-finite-rejected", () => { try { _ = ModernBertReference.Softmax([float.NaN, 0]); } catch (InvalidDataException) { return; } throw new InvalidDataException("Non-finite softmax was accepted."); })
        ];
        var results = new List<CheckResult>();
        for (var index = 0; index < maximum; index++)
        {
            budget.Check(); try { cases[index].Test(); results.Add(new(cases[index].Name, true, null)); }
            catch (InvalidDataException error) { results.Add(new(cases[index].Name, false, error.Message)); }
            Console.WriteLine($"reference-check {index + 1}/{maximum} {cases[index].Name} passed={results[^1].Passed}");
        }
        return new(results.All(item => item.Passed) ? "PASS_INDEPENDENT_ANALYTIC_FIXTURE_ONLY" : "FAIL_INDEPENDENT_ANALYTIC_FIXTURE", maximum, results.Count, results.ToArray());
    }
    private static void Equal(float[] actual, float[] expected, float tolerance)
    {
        if (actual.Length != expected.Length) throw new InvalidDataException("Analytic fixture dimensions differ.");
        for (var index = 0; index < actual.Length; index++) if (!float.IsFinite(actual[index]) || Math.Abs(actual[index] - expected[index]) > tolerance) throw new InvalidDataException("Analytic fixture differs at " + index);
    }
}
