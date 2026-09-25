using System.Globalization;

namespace Sezika.OracleCapture;

internal sealed record Options(string Reference, string Model, string Cases, string Contract, string Output,
    string Backend, PromptLengthPolicy LengthPolicy, int MaxCases, int TimeoutSeconds, string[] CaseIds, bool RequireAot)
{
    public static Options Parse(string[] args)
    {
        if (args.Length is < 10 or > 21)
            throw new ArgumentException("Usage: Sezika.OracleCapture --reference <capture.json> --model <local-package> --cases <cases.v1.json> --contract <contract.v1.json> --output <new-directory> [--backend scalar|simd|cuda] [--length-policy strict|laya_compatible] [--max-cases 1..46] [--case-ids id,id] [--timeout-seconds 1..1800] [--require-aot]");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var requireAot = false;
        for (var i = 0; i < args.Length; i++) // At most 21 arguments; each iteration consumes a flag or a pair.
        {
            if (args[i] == "--require-aot")
            {
                if (requireAot) throw new ArgumentException("Duplicate option: --require-aot");
                requireAot = true;
                continue;
            }
            if (args[i] is not ("--reference" or "--model" or "--cases" or "--contract" or "--output" or "--backend" or
                "--length-policy" or "--max-cases" or "--case-ids" or "--timeout-seconds") || i + 1 >= args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException($"Unknown, duplicate or missing-valued option: {args[i]}");
            i++;
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? Path.GetFullPath(value) : throw new ArgumentException($"Missing option {key}.");
        int Number(string key, int fallback, int max) => int.TryParse(values.GetValueOrDefault(key, fallback.ToString(CultureInfo.InvariantCulture)),
            CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= max ? n : throw new ArgumentException($"{key} must be 1..{max}.");
        var backend = values.GetValueOrDefault("--backend", "simd");
        if (backend is not ("scalar" or "simd" or "cuda")) throw new ArgumentException("Backend must be scalar, simd or cuda; quantization is outside the frozen FP32 contract.");
        var lengthPolicy = values.GetValueOrDefault("--length-policy", "laya_compatible") switch
        {
            "strict" => PromptLengthPolicy.Strict,
            "laya_compatible" => PromptLengthPolicy.LayaCompatible,
            _ => throw new ArgumentException("Length policy must be strict or laya_compatible."),
        };
        var caseIds = values.TryGetValue("--case-ids", out var selected) ? selected.Split(',') : [];
        var maxCases = Number("--max-cases", 1, 46);
        if (caseIds.Length > maxCases || caseIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128) ||
            caseIds.Distinct(StringComparer.Ordinal).Count() != caseIds.Length)
            throw new ArgumentException("Explicit case IDs must be unique, nonempty and within --max-cases.");
        return new(Required("--reference"), Required("--model"), Required("--cases"), Required("--contract"), Required("--output"),
            backend, lengthPolicy, maxCases, Number("--timeout-seconds", 180, 1800), caseIds, requireAot);
    }
}
