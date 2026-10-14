using System.Text.Json;
using Sezika.ReferenceTool.Laya;

namespace Sezika.ReferenceTool.Tokenization;

internal static class TokenizerExport
{
    internal static int Run(string requestPath, string receiptPath, CancellationToken cancellation)
    {
        var admission = new Budget(20, 536870912, null, cancellation); var pin = admission.Pin(requestPath, 4194304); var request = admission.Read(pin, TokenizerJson.Default.TokenizerExportRequest, 4194304);
        if (request.MaximumCases is < 1 or > 267 || request.TimeoutSeconds is < 1 or > 300 || request.MaximumResidentBytes is < 134217728 or > 1073741824) throw new InvalidDataException("Tokenizer export item/time/resident scope differs.");
        Budget.Safe(receiptPath); if (Path.Exists(receiptPath) || !Directory.Exists(Path.GetDirectoryName(receiptPath))) throw new IOException("Fresh tokenizer receipt required.");
        var budget = new Budget(request.TimeoutSeconds, request.MaximumResidentBytes, request.CancelFile, cancellation); var rows = new List<TokenizerCase>(); var crosschecked = 0; FilePin? capture = null;
        try
        {
            budget.Verify(pin, 4194304);
            var output = Path.GetFullPath(request.OutputDirectory); Budget.Safe(output); if (Path.Exists(output) || !Directory.Exists(Path.GetDirectoryName(output))) throw new IOException("Fresh tokenizer output directory required."); Directory.CreateDirectory(output);
            var runtime = PortProof.Runtime(request.ProgramSources, request.RuntimeExecutable, request.RuntimeAssembly, request.BuildReceipt, request.ProcessEvidenceDirectory, budget);
            var tokenizer = new FrozenTokenizer(request.Tokenizer, budget); var golden = budget.Read(request.Golden, TokenizerJson.Default.TokenizerGolden, 4194304);
            if (golden.SchemaVersion != "sezika.tokenizer-boundaries.v1" || golden.TokenizerSha256 != FrozenTokenizer.Sha256 || golden.Planned != 267 || golden.Processed != 267 || golden.Cases.Length != 267) throw new InvalidDataException("Exactly267 original tokenizer golden rows required.");
            var texts = new List<string> { "Alpha\n\nBefore Beta", "Alpha\nPolicy", "Alpha\tBeta", "Alpha\r\nBeta", "Alpha  Beta", "Alpha\n\n Before Beta", "中文\n\n后续", "A\n\n\n\nB", "A <mask>B", "A\t\n<mask>B", "<bos>Alpha<eos>", "\n\n", "\t", "  ", "A\u00a0<mask>B", "A\u0085<mask>B", "A\u2003<mask>B", "A\u001c<mask>B" };
            texts.AddRange(tokenizer.AddedContents.Select(content => "Alpha" + content + "Beta")); if (texts.Count != 267) throw new InvalidDataException("Independent boundary generator denominator differs.");
            for (var index = 0; index < request.MaximumCases; index++)
            {
                budget.Check(); var id = "boundary-" + (index + 1).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
                var row = new TokenizerCase(id, texts[index], tokenizer.Encode(texts[index], true, budget), tokenizer.Encode(texts[index], false, budget)); rows.Add(row); var expected = golden.Cases[index];
                if (expected.Id != row.Id || expected.Text != row.Text || !expected.Ids.SequenceEqual(row.Ids) || !expected.WithoutSpecialTokens.SequenceEqual(row.WithoutSpecialTokens))
                {
                    Budget.WriteNew(Path.Combine(output, "mismatch-" + id + ".json"), JsonSerializer.SerializeToUtf8Bytes(row, TokenizerJson.Default.TokenizerCase));
                    throw new InvalidDataException("Independent tokenizer golden mismatch at " + id + "; generated IDs retained.");
                }
                crosschecked++; if (index == 0 || (index + 1) % 32 == 0) Console.WriteLine("independent-tokenizer " + (index + 1) + "/" + request.MaximumCases);
            }
            budget.Verify(request.Tokenizer, 67108864); budget.Verify(request.Golden, 4194304); foreach (var source in request.ProgramSources) budget.Verify(source, 4194304);
            budget.Verify(request.RuntimeExecutable, 536870912); if (request.RuntimeAssembly is not null) budget.Verify(request.RuntimeAssembly, 536870912); budget.Verify(request.BuildReceipt, 20971520);
            budget.Verify(pin, 4194304); PortProof.VerifyRuntimeAfter(runtime.RuntimeFiles, budget);
            var result = new TokenizerCapture("sezika.tokenizer-boundaries.port-derived.v2", FrozenTokenizer.Sha256, "independent_csharp_frozen_json_bpe", "Original minimal regression text; Apache-2.0", 267, rows.Count, rows.ToArray(), request.ProgramSources, request.RuntimeExecutable, request.RuntimeAssembly, request.BuildReceipt, runtime.BuildExecution, runtime.Evidence, runtime.Process, request.Golden, crosschecked, pin, runtime.RuntimeFiles);
            var path = Path.Combine(output, "capture.json"); Budget.WriteNew(path, JsonSerializer.SerializeToUtf8Bytes(result, TokenizerJson.Default.TokenizerCapture)); capture = budget.Pin(path, 4194304);
            Budget.WriteNew(receiptPath, JsonSerializer.SerializeToUtf8Bytes(new TokenizerReceipt(rows.Count == 267 ? "PASS_independent_tokenizer_267_crosscheck" : "PASS_independent_tokenizer_subset_crosscheck", capture, 267, rows.Count, crosschecked, budget.Elapsed, budget.PeakBytes, null), TokenizerJson.Default.TokenizerReceipt)); return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException or TimeoutException or PlatformNotSupportedException or BadImageFormatException)
        {
            Budget.WriteNew(receiptPath, JsonSerializer.SerializeToUtf8Bytes(new TokenizerReceipt(error is OperationCanceledException or TimeoutException ? "CANCELLED_OR_TIMED_OUT" : "FAIL", capture, 267, rows.Count, crosschecked, budget.Elapsed, budget.PeakBytes, error.GetType().Name + ":" + error.Message), TokenizerJson.Default.TokenizerReceipt)); return error is OperationCanceledException or TimeoutException ? 124 : 1;
        }
    }
}
