using System.Text.Json;
using static Sezika.Tooling.CorpusJson;

namespace Sezika.Tooling;

internal static partial class CorpusOperations
{
    internal static CorpusInspectionReport Inspect(CorpusInspectRequest request, Budget budget, Func<string, int, IEnumerable<JsonElement>>? parquetReader = null)
    {
        Require(request.MaximumSources is >= 1 and <= 96 && request.RowsPerSource is >= 1 and <= 32, "Source inspection bounds.");
        var assets = Read(request.AssetManifest, CorpusJsonContext.Default.CorpusAssetArray, budget); Require(assets.Length <= 50_000, "Inspection asset count.");
        var selected = assets.Where(asset => asset.Split is "source-train" or "train" || Included(asset)).GroupBy(asset => asset.Dataset, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Take(request.MaximumSources).Select(g => g.OrderBy(a => a.Path, StringComparer.Ordinal).First()).ToArray();
        Require(selected.Length > 0, "Inspection has no original-training carriers."); var output = NewOutput(request.Output, budget); Directory.CreateDirectory(Path.Combine(output, "private-samples")); var items = new List<CorpusInspectionItem>();
        foreach (var asset in selected)
        {
            budget.Check(); var path = SafePaths.Within(request.DatasetRoot, asset.Path, budget); Require(new FileInfo(path).Length == asset.Bytes && IsSha(asset.Sha256) && FileHash(path, budget) == asset.Sha256, "Source inspection immutable file identity differs.");
            var sampleName = "private-samples/" + Sha(asset.Dataset) + ".jsonl"; var samplePath = SafePaths.Within(output, sampleName, budget); var count = 0; var adapter = true; string? failure = null;
            using (var samples = Writer(samplePath))
            {
                try
                {
                    foreach (var row in Rows(path, asset.Dataset, request.RowsPerSource, budget, parquetReader, true))
                    {
                        budget.Check(); Require(count < request.RowsPerSource, "Inspection per-source sample bound."); samples.WriteLine(Canonical(row)); count++;
                        try { CorpusAdapters.Adapt(asset.Dataset, row); }
                        catch (Exception error) when (error is InvalidDataException or KeyNotFoundException or FormatException or ArgumentException or InvalidOperationException) { adapter = false; failure ??= error.GetType().Name + ": " + error.Message; }
                    }
                }
                catch (Exception error) when (error is InvalidDataException or JsonException or IOException or InvalidOperationException) { failure = error.GetType().Name + ": " + error.Message; adapter = false; }
            }
            items.Add(new(asset.Dataset, asset.Path, asset.Split ?? "original-train-inferred-by-reviewed-carrier-name", asset.Sha256, asset.Bytes, count > 0 ? "SOURCE_SCHEMA_SAMPLE_ONLY" : "SOURCE_SCHEMA_SAMPLE_FAILED", count, sampleName, FileHash(samplePath, budget), adapter, failure));
        }
        var report = new CorpusInspectionReport(items.All(item => item.SampleRows > 0) ? "SOURCE_SCHEMA_INSPECTION_ONLY" : "PARTIAL_SOURCE_SCHEMA_INSPECTION_PRESERVE_FAILURES", items.ToArray(), true); Write(Path.Combine(output, "report.json"), report, CorpusJsonContext.Default.CorpusInspectionReport, budget); return report;
    }
}
