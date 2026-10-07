using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Sezika.IndependentNumericCompare;

internal static class SelfTests
{
    private static readonly int[] Tokens = [2,19946,1425,9330,21062,8332,539,9330,18176,8332,35722,9330,36648,573,13503,12990,1,19946,1082,9330,38131,60147,15627,12990,1,4,19946,539,9330,235250,8332,1082,9330,4878,12990,4,19946,539,9330,235268,8332,1082,9330,9200,12990,1];
    private const string RequestText = "{\"schema_version\":1,\"model_id\":\"sezika/mmbert-base-independent-s4\",\"state\":{\"text\":\"independent encoder smoke\"},\"questions\":[{\"id\":\"intent\",\"type\":\"choice\",\"instruction\":\"choose the intent\",\"candidates\":[{\"id\":\"a\",\"text\":\"first\"},{\"id\":\"b\",\"text\":\"second\"}]}]}";
    // Byte-exact public source config, not model weights or a measured output.
    private const string ConfigBase64 = "ewogICJfbmFtZV9vcl9wYXRoIjogIm1tQkVSVC1iYXNlIiwKICAiYXJjaGl0ZWN0dXJlcyI6IFsKICAgICJNb2Rlcm5CZXJ0Rm9yTWFza2VkTE0iCiAgXSwKICAiYXR0ZW50aW9uX2JpYXMiOiBmYWxzZSwKICAiYXR0ZW50aW9uX2Ryb3BvdXQiOiAwLjAsCiAgImJvc190b2tlbl9pZCI6IDIsCiAgImNsYXNzaWZpZXJfYWN0aXZhdGlvbiI6ICJnZWx1IiwKICAiY2xhc3NpZmllcl9iaWFzIjogZmFsc2UsCiAgImNsYXNzaWZpZXJfZHJvcG91dCI6IDAuMCwKICAiY2xhc3NpZmllcl9wb29saW5nIjogIm1lYW4iLAogICJjbHNfdG9rZW5faWQiOiAxLAogICJkZWNvZGVyX2JpYXMiOiB0cnVlLAogICJkZXRlcm1pbmlzdGljX2ZsYXNoX2F0dG4iOiBmYWxzZSwKICAiZW1iZWRkaW5nX2Ryb3BvdXQiOiAwLjAsCiAgImVvc190b2tlbl9pZCI6IDEsCiAgImdsb2JhbF9hdHRuX2V2ZXJ5X25fbGF5ZXJzIjogMywKICAiZ2xvYmFsX3JvcGVfdGhldGEiOiAxNjAwMDAsCiAgImdyYWRpZW50X2NoZWNrcG9pbnRpbmciOiBmYWxzZSwKICAiaGlkZGVuX2FjdGl2YXRpb24iOiAiZ2VsdSIsCiAgImhpZGRlbl9zaXplIjogNzY4LAogICJpbml0aWFsaXplcl9jdXRvZmZfZmFjdG9yIjogMi4wLAogICJpbml0aWFsaXplcl9yYW5nZSI6IDAuMDIsCiAgImludGVybWVkaWF0ZV9zaXplIjogMTE1MiwKICAibGF5ZXJfbm9ybV9lcHMiOiAxZS0wNSwKICAibG9jYWxfYXR0ZW50aW9uIjogMTI4LAogICJsb2NhbF9yb3BlX3RoZXRhIjogMTYwMDAwLAogICJtYXhfcG9zaXRpb25fZW1iZWRkaW5ncyI6IDgxOTIsCiAgIm1scF9iaWFzIjogZmFsc2UsCiAgIm1scF9kcm9wb3V0IjogMC4wLAogICJtb2RlbF90eXBlIjogIm1vZGVybmJlcnQiLAogICJub3JtX2JpYXMiOiBmYWxzZSwKICAibm9ybV9lcHMiOiAxZS0wNSwKICAibnVtX2F0dGVudGlvbl9oZWFkcyI6IDEyLAogICJudW1faGlkZGVuX2xheWVycyI6IDIyLAogICJwYWRfdG9rZW5faWQiOiAwLAogICJwb3NpdGlvbl9lbWJlZGRpbmdfdHlwZSI6ICJzYW5zX3BvcyIsCiAgInNlcF90b2tlbl9pZCI6IDEsCiAgInRpZV93b3JkX2VtYmVkZGluZ3MiOiB0cnVlLAogICJ0b3JjaF9kdHlwZSI6ICJmbG9hdDMyIiwKICAidHJhbnNmb3JtZXJzX3ZlcnNpb24iOiAiNC40Ny4wLmRldjAiLAogICJ2b2NhYl9zaXplIjogMjU2MDAwLAogICJtYXNrX3Rva2VuX2lkIjogNAp9";
    private static readonly string[] Names = ["fixture_pass_not_independent", "all_26_numerical_failures_retained", "source_revision_and_null_dto_rejected", "swapped_markers_rejected", "token_array_bytes_disagreement", "missing_checkpoint_rejected", "duplicate_actual_name_rejected", "shape_rejected", "nan_and_infinity_rejected", "duplicate_json_property_rejected", "tensor_hash_rejected", "path_traversal_rejected", "external_contract_hash_rejected", "internally_matching_code_outside_frozen_contract_rejected", "existing_report_preserved", "production_fixture_and_shared_capture_root_rejected"];
    internal static int Run(Options options, Budget budget)
    {
        Io.NewOutput(options.Output, budget); Directory.CreateDirectory(options.Output);
        var contractHash = Io.Hash(Io.Read(options.Contract, 1_048_576, budget));
        var contract = Comparison.Contract(options.Contract, contractHash, budget, out _);
        var results = new List<SelfTestCase>(16);
        // Maximum 16 cases, each <8MiB synthetic data and <80 files; common budget <=60s.
        for (var index = 0; index < options.MaximumCases; index++)
        {
            budget.Check(); var root = Path.Combine(options.Output, $"case-{index + 1:D2}"); Directory.CreateDirectory(root);
            var fixture = Fixture(root, options.Contract, contractHash, contract, budget);
            var actual = fixture.Actual; var reference = fixture.Reference; var runOptions = fixture.Options;
            var expectReject = index >= 2;
            switch (index)
            {
                case 1:
                    for (var i = 0; i < 25; i++) { budget.Check(); reference.Tensors[i] = ChangeTensor(reference.Tensors[i], fixture.ReferenceRoot, 0.5f, budget); }
                    reference = reference with { MarkerVectors = ChangeTensor(reference.MarkerVectors, fixture.ReferenceRoot, 0.5f, budget) }; break;
                case 2: reference = reference with { SourceRevision = "wrong" }; break;
                case 3: reference = reference with { MarkerPositions = [35, 25] }; break;
                case 4: reference.TokenIds[0] = 7; break;
                case 5: reference = reference with { Tensors = reference.Tensors[..24] }; break;
                case 6: actual.Tensors[2] = actual.Tensors[2] with { Name = actual.Tensors[0].Name }; break;
                case 7: reference.Tensors[0] = reference.Tensors[0] with { Shape = [768, 46] }; break;
                case 8: actual.Tensors[0] = ChangeTensor(actual.Tensors[0], fixture.ActualRoot, float.NaN, budget); break;
                case 10: actual.Tensors[0] = actual.Tensors[0] with { Sha256 = new string('0', 64) }; break;
                case 11: reference.Tensors[0] = reference.Tensors[0] with { Path = "../escaped.f32" }; break;
                case 12: runOptions = runOptions with { ContractSha256 = new string('0', 64) }; break;
                case 13: actual = actual with { FixtureOnly = false }; reference = reference with { FixtureOnly = false }; break;
                case 14: WriteOwned(runOptions.Output, Encoding.UTF8.GetBytes("owned-sentinel"), budget); break;
            }
            WriteOwned(runOptions.Actual, JsonSerializer.SerializeToUtf8Bytes(actual, CompareJsonContext.Default.ActualCapture), budget);
            WriteOwned(runOptions.Reference, JsonSerializer.SerializeToUtf8Bytes(reference, CompareJsonContext.Default.ReferenceCapture), budget);
            if (index == 9)
            {
                var json = Encoding.UTF8.GetString(Io.Read(runOptions.Reference, 1_048_576, budget));
                WriteOwned(runOptions.Reference, Encoding.UTF8.GetBytes(json.Replace("\"schema_version\": \"sezika.independent-reference.v1\",", "\"schema_version\": \"sezika.independent-reference.v1\", \"schema_version\": \"sezika.independent-reference.v1\",", StringComparison.Ordinal)), budget);
            }
            ComparisonReport? report = null; var rejected = false;
            try { report = Comparison.Run(runOptions, budget, allowFixtures: index != 15); }
            catch (Exception e) when (e is InvalidDataException or JsonException) { rejected = true; }
            Require.That(rejected == expectReject, $"Unexpected rejection state in self-test {index + 1}.");
            if (report is not null)
            {
                Require.That(report.ComparedCount == 26 && report.ComparisonDenominator == 26 && report.ElementDenominator == 884_736 && report.FixtureOnly && !report.AcceptedIndependentReference,
                    "Fixture coverage/acceptance flag invalid.");
                Require.That(index == 0 ? report.Passed && report.FailedComparisons == 0 : !report.Passed && report.FailedComparisons == 26 && report.FailedElements == report.ElementDenominator,
                    "Numerical failure denominator lost.");
                Io.WriteNew(runOptions.Output, report, CompareJsonContext.Default.ComparisonReport, budget);
            }
            if (index == 14) Require.That(Encoding.UTF8.GetString(Io.Read(runOptions.Output, 64, budget)) == "owned-sentinel", "Existing report was overwritten.");
            var variants = 1;
            if (index == 2)
            {
                reference = reference with { SourceRevision = contract.SourceRevision };
                var json = JsonSerializer.Serialize(reference, CompareJsonContext.Default.ReferenceCapture);
                WriteOwned(runOptions.Reference, Encoding.UTF8.GetBytes(json.Replace("\"token_ids\": [", "\"token_ids\": null, \"discarded_tokens\": [", StringComparison.Ordinal)), budget);
                ExpectRejected(runOptions, budget); variants++;
            }
            if (index == 8)
            {
                actual.Tensors[0] = ChangeTensor(actual.Tensors[0], fixture.ActualRoot, 0.125f, budget);
                reference.Tensors[0] = ChangeTensor(reference.Tensors[0], fixture.ReferenceRoot, float.PositiveInfinity, budget);
                WriteOwned(runOptions.Actual, JsonSerializer.SerializeToUtf8Bytes(actual, CompareJsonContext.Default.ActualCapture), budget);
                WriteOwned(runOptions.Reference, JsonSerializer.SerializeToUtf8Bytes(reference, CompareJsonContext.Default.ReferenceCapture), budget);
                ExpectRejected(runOptions, budget); variants++;
            }
            if (index == 15)
            {
                var shared = Path.Combine(fixture.ActualRoot, "shared-reference.json");
                WriteOwned(shared, JsonSerializer.SerializeToUtf8Bytes(reference, CompareJsonContext.Default.ReferenceCapture), budget);
                ExpectRejected(runOptions with { Reference = shared }, budget); variants++;
            }
            results.Add(new(index + 1, Names[index], true, variants)); Console.Error.WriteLine($"Self-test {index + 1}/{options.MaximumCases}: {Names[index]}; variants={variants}");
        }
        Io.WriteNew(Path.Combine(options.Output, "self-test.json"), new SelfTestReport("sezika.independent-compare-self-test.v1", true, false, results.Count, results.Sum(r => r.AssertionVariants), results.ToArray()), CompareJsonContext.Default.SelfTestReport, budget);
        Console.WriteLine($"Synthetic self-tests passed: {results.Count}/{options.MaximumCases}; output retained for audit; no model execution."); return 0;
    }
    private sealed record FixtureData(ActualCapture Actual, ReferenceCapture Reference, Options Options, string ActualRoot, string ReferenceRoot);
    private static FixtureData Fixture(string root, string contractPath, string contractHash, FrozenContract c, Budget budget)
    {
        var ar = Path.Combine(root, "actual"); var rr = Path.Combine(root, "reference"); Directory.CreateDirectory(ar); Directory.CreateDirectory(rr);
        var tokenBytes = new byte[184]; var markerBytes = new byte[8];
        for (var i = 0; i < 46; i++) { budget.Check(); BinaryPrimitives.WriteInt32LittleEndian(tokenBytes.AsSpan(i * 4, 4), Tokens[i]); }
        BinaryPrimitives.WriteInt32LittleEndian(markerBytes.AsSpan(0, 4), 25); BinaryPrimitives.WriteInt32LittleEndian(markerBytes.AsSpan(4, 4), 35);
        var token = Save(ar, "token-ids.i32", tokenBytes, budget); Save(rr, token.Path, tokenBytes, budget);
        var marker = Save(ar, "marker-positions.i32", markerBytes, budget); Save(rr, marker.Path, markerBytes, budget);
        var request = Save(ar, "request.json", Encoding.UTF8.GetBytes(RequestText), budget); Save(rr, request.Path, Encoding.UTF8.GetBytes(RequestText), budget);
        var config = Save(ar, "source-config.json", Convert.FromBase64String(ConfigBase64), budget);
        var package = new PackageManifest { SchemaVersion = 1, ModelId = "sezika/mmbert-base-independent-s4", SourceModelId = c.SourceModelId, SourceRevision = c.SourceRevision,
            SourceWeightsSha256 = c.SourceWeightsSha256, WeightsSha256 = c.ConvertedEncoderSha256, TokenizerSha256 = c.TokenizerSha256 };
        var packageArtifact = Save(ar, "package-manifest.json", JsonSerializer.SerializeToUtf8Bytes(package, CompareJsonContext.Default.PackageManifest), budget);
        var all = new TensorArtifact[267]; var layerNames = new[] { "norm1", "qkv", "rope_q", "rope_k", "attention", "projected", "residual", "norm2", "up", "gelu", "mlp", "hidden" };
        for (var i = 0; i < 267; i++)
        {
            budget.Check(); string name; var width = 768;
            if (i < 2) name = Comparison.Checkpoints[i]; else if (i == 266) name = "encoder/final";
            else { var slot = (i - 2) % 12; name = $"layer/{(i - 2) / 12}/{layerNames[slot]}"; width = slot is 1 or 8 ? 2304 : slot == 9 ? 1152 : 768; }
            all[i] = new(name, $"tensor-{i:D3}.f32", "F32", [46, width], "little_endian_row_major", 46L * width * 4, new string('0', 64));
            if (Comparison.Checkpoints.Contains(name)) all[i] = ChangeTensor(all[i], ar, 0.125f, budget);
        }
        var selected = new TensorArtifact[25];
        for (var i = 0; i < 25; i++)
        {
            budget.Check(); var source = all.Single(t => t.Name == Comparison.Checkpoints[i]);
            selected[i] = ChangeTensor(source with { Path = $"tensor-{i:D3}.f32" }, rr, 0.125f, budget);
        }
        var mv = new TensorArtifact("marker-vectors.f32", "marker-vectors.f32", "F32", [2, 768], "little_endian_row_major", 6144, new string('0', 64));
        var actualMarker = ChangeTensor(mv, ar, 0.125f, budget); var referenceMarker = ChangeTensor(mv, rr, 0.125f, budget);
        var script = Save(root, "fixture-oracle.py", Encoding.UTF8.GetBytes("# synthetic fixture; not executable oracle\n"), budget);
        var sourceCode = Save(root, "fixture-modeling.py", Encoding.UTF8.GetBytes("# synthetic source identity; no model\n"), budget);
        var scriptPath = Path.Combine(root, script.Path); var sourcePath = Path.Combine(root, sourceCode.Path);
        var actual = new ActualCapture { SchemaVersion = 1, Status = "cpu_capture_complete", Kind = "csharp_cpu_capture", ModelId = package.ModelId, SourceModelId = c.SourceModelId,
            SourceRevision = c.SourceRevision, Protocol = c.Protocol, LengthPolicy = c.LengthPolicy, Backend = "csharp_scalar_fp32", CaseId = c.CaseId, FixtureOnly = true, RepresentsUpstreamParity = false,
            ExpectedCheckpoints = 267, IntegerDtype = "I32", IntegerLayout = "little_endian_contiguous", TokenIds = (int[])Tokens.Clone(), MarkerPositions = [25, 35], TokenIdsArtifact = token,
            MarkerPositionsArtifact = marker, Request = request, Assets = [config, packageArtifact, new("pytorch_model.bin", c.SourceWeightsSha256, c.SourceWeightsBytes),
                new("encoder.safetensors", c.ConvertedEncoderSha256, 1_227_772_549), new("tokenizer/tokenizer.json", c.TokenizerSha256, 17_525_329)], Tensors = all,
            MarkerVectors = actualMarker, FinalHiddenSha256 = all[^1].Sha256 };
        var reference = new ReferenceCapture { SchemaVersion = "sezika.independent-reference.v1", Status = "complete", Kind = "upstream_original_weights_capture", FixtureOnly = true,
            CaseId = c.CaseId, SourceModelId = c.SourceModelId, SourceRevision = c.SourceRevision, SourceWeightsSha256 = c.SourceWeightsSha256, SourceWeightsBytes = c.SourceWeightsBytes, OriginalWeightsVerified = false,
            ConfigSha256 = c.ConfigSha256, TokenizerSha256 = c.TokenizerSha256, ContractSha256 = contractHash, Protocol = c.Protocol, LengthPolicy = c.LengthPolicy,
            IntegerDtype = "I32", IntegerLayout = c.IntegerLayout, TokenIds = (int[])Tokens.Clone(), MarkerPositions = [25, 35], TokenIdsArtifact = token, MarkerPositionsArtifact = marker,
            Request = request, OracleProgram = new(scriptPath, script.Sha256, script.Bytes), UpstreamSource = new(sourcePath, sourceCode.Sha256, sourceCode.Bytes, "transformers", c.Execution.Transformers),
            Execution = c.Execution, Tensors = selected, MarkerVectors = referenceMarker };
        return new(actual, reference, new("compare", Path.Combine(ar, "capture.json"), Path.Combine(rr, "reference.json"), contractPath, contractHash, scriptPath, script.Sha256,
            sourcePath, sourceCode.Sha256, Path.Combine(root, "comparison.json"), 60, null, 16), ar, rr);
    }
    private static TensorArtifact ChangeTensor(TensorArtifact t, string root, float value, Budget budget)
    {
        var bytes = new byte[(int)t.Bytes];
        for (var i = 0; i < bytes.Length / 4; i++) { if ((i & 255) == 0) budget.Check(); BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), value); }
        WriteOwned(Path.Combine(root, t.Path), bytes, budget); return t with { Sha256 = Io.Hash(bytes) };
    }
    private static Artifact Save(string root, string name, byte[] bytes, Budget budget)
    { WriteOwned(Path.Combine(root, name), bytes, budget); return new(name, Io.Hash(bytes), bytes.Length, "synthetic_fixture_only"); }
    private static void WriteOwned(string path, byte[] bytes, Budget budget)
    { budget.Check(); Io.CheckPath(path, budget); File.WriteAllBytes(path, bytes); }
    private static void ExpectRejected(Options options, Budget budget)
    {
        var rejected = false;
        try { _ = Comparison.Run(options, budget, allowFixtures: true); }
        catch (Exception e) when (e is InvalidDataException or JsonException) { rejected = true; }
        Require.That(rejected, "Grouped adversarial input was accepted.");
    }
}
