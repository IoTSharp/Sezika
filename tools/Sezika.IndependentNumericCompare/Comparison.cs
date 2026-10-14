using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sezika.IndependentNumericCompare;

internal static class Io
{
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static bool SameHash(string a, string b) => ValidHash(a) && ValidHash(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    internal static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    internal static void CheckPath(string path, Budget budget)
    {
        var current = Path.GetFullPath(path);
        for (var i = 0; i < 64; i++)
        {
            budget.Check();
            if (File.Exists(current) || Directory.Exists(current)) Require.That((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Links/reparse points are forbidden.");
            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current) return;
            current = parent;
        }
        throw new InvalidDataException("Path ancestry exceeds 64.");
    }
    internal static string Relative(string directory, string name, Budget budget)
    {
        Require.That(!string.IsNullOrWhiteSpace(name) && name.Length <= 160 && !Path.IsPathRooted(name) && !name.Contains(':') && !name.Contains('\\') && !name.Split('/').Any(part => part is "" or "." or ".."), "Invalid relative artifact path.");
        var root = Path.GetFullPath(directory); var full = Path.GetFullPath(Path.Combine(root, name));
        Require.That(full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Artifact escaped input root.");
        CheckPath(full, budget); return full;
    }
    internal static byte[] Read(string path, long maximum, Budget budget)
    {
        budget.Check(); CheckPath(path, budget);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require.That(stream.Length is >= 1 && stream.Length <= maximum && stream.Length <= int.MaxValue, "Input file byte bound exceeded.");
        var bytes = new byte[(int)stream.Length]; var offset = 0;
        // Each iteration reads >=1 byte or fails; at most maximum iterations.
        for (var iteration = 0; offset < bytes.Length && iteration < bytes.Length; iteration++)
        {
            budget.Check(); var count = stream.Read(bytes.AsSpan(offset));
            Require.That(count > 0, "Truncated input file."); offset += count;
        }
        Require.That(offset == bytes.Length && stream.ReadByte() == -1, "Input bytes changed during read."); return bytes;
    }
    internal static byte[] Artifact(string root, Artifact artifact, long maximum, Budget budget)
    {
        Require.That(artifact.Bytes is >= 1 && artifact.Bytes <= maximum && ValidHash(artifact.Sha256), "Invalid artifact metadata.");
        var bytes = Read(Relative(root, artifact.Path, budget), maximum, budget);
        Require.That(bytes.LongLength == artifact.Bytes && SameHash(Hash(bytes), artifact.Sha256), "Artifact bytes/hash mismatch."); return bytes;
    }
    internal static T Parse<T>(byte[] bytes, JsonTypeInfo<T> info, Budget budget)
    {
        Require.That(bytes.Length is >= 1 and <= 1_048_576, "JSON exceeds 1MiB.");
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow });
        var objects = new Stack<HashSet<string>>(); var nodes = 0;
        while (reader.Read())
        {
            // Input byte limit + explicit 20000-node bound + deadline bound every token.
            budget.Check(); Require.That(++nodes <= 20_000, "JSON node bound exceeded.");
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName) Require.That(objects.Peek().Add(reader.GetString()!), "Duplicate JSON property.");
        }
        return JsonSerializer.Deserialize(bytes, info) ?? throw new InvalidDataException("Null JSON root.");
    }
    internal static T Json<T>(string path, JsonTypeInfo<T> info, Budget budget, out string hash)
    { var bytes = Read(path, 1_048_576, budget); hash = Hash(bytes); return Parse(bytes, info, budget); }
    internal static void NewOutput(string path, Budget budget)
    {
        CheckPath(path, budget); var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        Require.That(parent is not null && Directory.Exists(parent) && !File.Exists(path) && !Directory.Exists(path), "Output must be new with an existing parent.");
    }
    internal static void WriteNew<T>(string path, T value, JsonTypeInfo<T> info, Budget budget)
    {
        NewOutput(path, budget); var bytes = JsonSerializer.SerializeToUtf8Bytes(value, info);
        Require.That(bytes.Length <= 1_048_576, "Output exceeds 1MiB."); budget.Check();
        var output = Path.GetFullPath(path); var owned = Path.Combine(Path.GetDirectoryName(output)!, $".independent-compare-{Guid.NewGuid():N}.partial");
        var created = false; var finished = false; DateTime creation = default;
        try
        {
            CheckPath(owned, budget);
            using (var file = new FileStream(owned, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { created = true; creation = File.GetCreationTimeUtc(owned); file.Write(bytes); file.Flush(true); }
            budget.Check(); NewOutput(output, budget);
            File.Move(owned, output, overwrite: false); owned = output;
            budget.Check(); finished = true;
        }
        finally
        {
            if (created && !finished)
            {
                // Only a single journaled file, within five independent recovery seconds.
                using var recovery = new Budget(5, null, CancellationToken.None);
                try
                {
                    CheckPath(owned, recovery);
                    using var file = new FileStream(owned, FileMode.Open, FileAccess.Read, FileShare.None);
                    Require.That(File.GetCreationTimeUtc(owned) == creation && file.Length >= 0 && file.Length <= bytes.Length, "Owned output changed; retain for review.");
                    var actual = new byte[(int)file.Length]; file.ReadExactly(actual); recovery.Check();
                    Require.That(actual.AsSpan().SequenceEqual(bytes.AsSpan(0, actual.Length)), "Owned output bytes changed; retain for review.");
                    file.Dispose(); recovery.Check(); File.Delete(owned);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
                { Console.Error.WriteLine($"Owned incomplete output retained: {owned}; {error.GetType().Name}"); }
            }
        }
    }
}

internal static class Comparison
{
    internal static readonly string[] Checkpoints = ["embedding/raw", "embedding/norm", .. Enumerable.Range(0, 22).Select(i => $"layer/{i}/hidden"), "encoder/final"];
    private static readonly string[] LayerNames = ["norm1", "qkv", "rope_q", "rope_k", "attention", "projected", "residual", "norm2", "up", "gelu", "mlp", "hidden"];
    internal static FrozenContract Contract(string path, string expectedHash, Budget budget, out string hash)
    {
        var c = Io.Json(path, CompareJsonContext.Default.FrozenContract, budget, out hash);
        Require.That(Io.SameHash(hash, expectedHash), "Externally expected contract SHA-256 mismatch.");
        Require.That(c.SchemaVersion == "sezika.independent-oracle-contract.v1" && c.Status == "frozen_before_upstream_measurement", "Contract is not the frozen v1 contract.");
        Require.That(c.SourceModelId == "jhu-clsp/mmBERT-base" && c.SourceRevision == "c5955035435e2bf121cde7f3c8863ef52ff35d82" &&
            c.CaseId == "independent-smoke-46-v1" && c.SourceWeightsBytes == 1_231_188_142 && c.TokenCount == 46 && c.HiddenSize == 768 && c.LayerCount == 22 &&
            c.Protocol == "decision-v1" && c.LengthPolicy == "strict" && c.IntegerDtype == "I32" && c.IntegerLayout is "little_endian" or "little_endian_contiguous" &&
            c.TensorDtype == "F32" && c.TensorLayout == "little_endian_row_major" && c.CheckpointCount == 25 && c.ComparisonCount == 26 &&
            c.Checkpoints.SequenceEqual(Checkpoints) && c.MarkerPositions.SequenceEqual([25, 35]) && c.MarkerVectorsName == "marker-vectors.f32", "Unexpected fixed contract identity/dimensions/selection.");
        Require.That(Io.SameHash(c.SourceWeightsSha256, "8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e") &&
            Io.SameHash(c.ConfigSha256, "47b40fd2e1df8299426dd5f4bb18c28f028cfafcb51b73645f83e596d187eb37") &&
            Io.SameHash(c.TokenizerSha256, "197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356") &&
            Io.SameHash(c.ConvertedEncoderSha256, "6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528") &&
            Io.SameHash(c.TokenIdsSha256, "BAB3A41CDB62FC4D5E3353D3D88F6BBDCBD0D3CEC9C3DB537841F09604D2D80B") &&
            Io.SameHash(c.MarkerPositionsSha256, "6E45294D69ECA0660E53BB653811E6AEC1BD269B164EE6174D1961D9D5B94396") &&
            Io.SameHash(c.RequestSha256, "75B7055B38C78E7563D1D14DE9BC5F4EE9D63A7FF31E637B8C77F3EBF41D9917"), "Fixed source/input hash changed.");
        var keys = new[] { "embedding/raw", "embedding/norm", "layer_hidden", "encoder/final", "marker-vectors.f32" };
        Require.That(c.Tolerances.Count == 5 && keys.All(c.Tolerances.ContainsKey), "Tolerance keys differ.");
        foreach (var key in keys)
        {
            budget.Check(); var t = c.Tolerances[key];
            Require.That(t is not null && double.IsFinite(t.Absolute) && double.IsFinite(t.Relative) && t.Absolute >= 0 && t.Relative >= 0, "Invalid tolerance.");
            var expected = key == "embedding/raw" ? new Tolerance(0, 0) : key == "embedding/norm" ? new Tolerance(1e-5, 1e-5) : new Tolerance(1e-3, 1e-4);
            Require.That(t == expected, "Frozen v1 tolerance changed.");
        }
        Require.That(Io.ValidHash(c.Execution.ModernbertSourceSha256) && c.Execution.Threads is >= 1 and <= 4 && c.Execution.Backend == "pytorch_cpu_fp32" && c.Execution.Attention == "eager" &&
            !c.Execution.Compile && !c.Execution.Autocast && c.Execution.Network == "offline_only", "Unsupported reference execution policy.");
        return c;
    }

    internal static ComparisonReport Run(Options options, Budget budget, bool allowFixtures = false)
    {
        budget.Check(); Io.NewOutput(options.Output, budget);
        var actual = Io.Json(options.Actual, CompareJsonContext.Default.ActualCapture, budget, out var actualHash);
        var reference = Io.Json(options.Reference, CompareJsonContext.Default.ReferenceCapture, budget, out var referenceHash);
        var c = Contract(options.Contract, options.ContractSha256, budget, out var contractHash);
        var actualRoot = Path.GetDirectoryName(Path.GetFullPath(options.Actual))!;
        var referenceRoot = Path.GetDirectoryName(Path.GetFullPath(options.Reference))!;
        Require.That(!string.Equals(actualRoot, referenceRoot, StringComparison.OrdinalIgnoreCase), "Actual/reference capture roots must be independent.");
        Require.That(actual.SchemaVersion == 1 && actual.Status == "cpu_capture_complete" && actual.Kind == "csharp_cpu_capture" && actual.Backend == "csharp_scalar_fp32" &&
            actual.ModelId == "sezika/mmbert-base-independent-s4" && actual.SourceModelId == c.SourceModelId && actual.SourceRevision == c.SourceRevision &&
            actual.Protocol == c.Protocol && actual.LengthPolicy == c.LengthPolicy && actual.CaseId == c.CaseId && !actual.RepresentsUpstreamParity &&
            actual.ExpectedCheckpoints == 267 && actual.IntegerDtype == "I32" && actual.IntegerLayout == "little_endian_contiguous", "Actual capture identity/status mismatch.");
        Require.That(reference.SchemaVersion == "sezika.independent-reference.v1" && reference.Status == "complete" && reference.Kind == "upstream_original_weights_capture" &&
            reference.SourceModelId == c.SourceModelId && reference.SourceRevision == c.SourceRevision && reference.CaseId == c.CaseId &&
            reference.SourceWeightsBytes == c.SourceWeightsBytes && Io.SameHash(reference.SourceWeightsSha256, c.SourceWeightsSha256) && Io.SameHash(reference.ConfigSha256, c.ConfigSha256) &&
            Io.SameHash(reference.TokenizerSha256, c.TokenizerSha256) && Io.SameHash(reference.ContractSha256, contractHash) && reference.Protocol == c.Protocol && reference.LengthPolicy == c.LengthPolicy &&
            reference.IntegerDtype == "I32" && reference.IntegerLayout is "little_endian" or "little_endian_contiguous", "Reference identity/status mismatch.");
        var fixture = actual.FixtureOnly || reference.FixtureOnly;
        Require.That(!fixture || allowFixtures && actual.FixtureOnly && reference.FixtureOnly, "Fixture is not an independent reference.");
        Require.That(fixture && allowFixtures || Io.SameHash(options.UpstreamSourceSha256, c.Execution.ModernbertSourceSha256), "Upstream source SHA-256 differs from frozen contract.");
        Require.That(fixture && allowFixtures && !reference.OriginalWeightsVerified || !fixture && reference.OriginalWeightsVerified, "Original-weight verification declaration is invalid for fixture/production.");
        Require.That(reference.Execution == c.Execution, "Reference execution metadata differs from frozen contract.");
        Code(reference.OracleProgram, options.OracleProgram, options.OracleProgramSha256, referenceRoot, budget);
        Code(reference.UpstreamSource, options.UpstreamSource, options.UpstreamSourceSha256, referenceRoot, budget);
        Require.That(reference.UpstreamSource.Package == "transformers" && reference.UpstreamSource.Version == c.Execution.Transformers, "Upstream source package/version mismatch.");
        Require.That(actual.Assets.Length is >= 3 and <= 16 && actual.Assets.All(x => x is not null) && actual.Assets.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == actual.Assets.Length, "Actual assets invalid/duplicate.");
        var config = Asset(actual, "source-config.json"); Require.That(Io.SameHash(config.Sha256, c.ConfigSha256), "Actual config identity mismatch.");
        Io.Artifact(actualRoot, config, 1_048_576, budget);
        var package = Io.Parse(Io.Artifact(actualRoot, Asset(actual, "package-manifest.json"), 1_048_576, budget), CompareJsonContext.Default.PackageManifest, budget);
        Require.That(package.SchemaVersion == 1 && package.ModelId == actual.ModelId && package.SourceModelId == c.SourceModelId && package.SourceRevision == c.SourceRevision &&
            Io.SameHash(package.SourceWeightsSha256, c.SourceWeightsSha256) && Io.SameHash(package.WeightsSha256, c.ConvertedEncoderSha256) && Io.SameHash(package.TokenizerSha256, c.TokenizerSha256), "Actual package snapshot identity mismatch.");
        Require.That(Io.SameHash(Asset(actual, "pytorch_model.bin").Sha256, c.SourceWeightsSha256) && Asset(actual, "pytorch_model.bin").Bytes == c.SourceWeightsBytes &&
            Io.SameHash(Asset(actual, "encoder.safetensors").Sha256, c.ConvertedEncoderSha256) && Asset(actual, "encoder.safetensors").Bytes == 1_227_772_549 &&
            Io.SameHash(Asset(actual, "tokenizer/tokenizer.json").Sha256, c.TokenizerSha256) && Asset(actual, "tokenizer/tokenizer.json").Bytes == 17_525_329, "Actual loader source identity differs.");
        Integers(actualRoot, actual.TokenIds, actual.MarkerPositions, actual.TokenIdsArtifact, actual.MarkerPositionsArtifact, c, budget);
        Integers(referenceRoot, reference.TokenIds, reference.MarkerPositions, reference.TokenIdsArtifact, reference.MarkerPositionsArtifact, c, budget);
        Require.That(actual.TokenIds.SequenceEqual(reference.TokenIds) && actual.MarkerPositions.SequenceEqual(reference.MarkerPositions), "Token/marker arrays differ.");
        Request(actualRoot, actual.Request, c, budget); Request(referenceRoot, reference.Request, c, budget);
        Metadata(actual.Tensors, actualRoot, true, budget); Metadata(reference.Tensors, referenceRoot, false, budget);
        MarkerMetadata(actual.MarkerVectors, actualRoot, budget); MarkerMetadata(reference.MarkerVectors, referenceRoot, budget);
        Require.That(!actual.Tensors.Any(t => t.Path == actual.MarkerVectors.Path) && !reference.Tensors.Any(t => t.Path == reference.MarkerVectors.Path), "Marker path aliases checkpoint.");
        var actualPaths = actual.Tensors.Where(t => c.Checkpoints.Contains(t.Name)).Select(t => Io.Relative(actualRoot, t.Path, budget)).Append(Io.Relative(actualRoot, actual.MarkerVectors.Path, budget)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require.That(reference.Tensors.Select(t => Io.Relative(referenceRoot, t.Path, budget)).Append(Io.Relative(referenceRoot, reference.MarkerVectors.Path, budget)).All(path => !actualPaths.Contains(path)), "Reference reuses actual tensor paths.");
        Require.That(Io.SameHash(actual.FinalHiddenSha256, actual.Tensors[^1].Sha256), "Actual final return hash differs from final checkpoint.");
        var results = new List<TensorResult>(26); byte[]? actualFinal = null; byte[]? referenceFinal = null;
        long actualBytes = 0, referenceBytes = 0;
        for (var i = 0; i < 25; i++)
        {
            budget.Check(); var name = c.Checkpoints[i]; var a = actual.Tensors.Single(t => t.Name == name); var r = reference.Tensors[i];
            var ab = Tensor(actualRoot, a, budget); var rb = Tensor(referenceRoot, r, budget);
            actualBytes += ab.Length; referenceBytes += rb.Length;
            Require.That(actualBytes <= 4_194_304 && referenceBytes <= 4_194_304, "Selected payload exceeds 4MiB per side.");
            results.Add(Values(name, a.Shape, ab, rb, Tolerance(c, name), budget));
            if (name == "encoder/final") { actualFinal = ab; referenceFinal = rb; }
            Console.Error.WriteLine($"Compared {i + 1}/26: {name}; failed_elements={results[^1].FailedElements}");
        }
        var am = Tensor(actualRoot, actual.MarkerVectors, budget); var rm = Tensor(referenceRoot, reference.MarkerVectors, budget);
        Require.That(actualBytes + am.Length <= 4_194_304 && referenceBytes + rm.Length <= 4_194_304, "Marker payload exceeds budget.");
        MarkerRows(actualFinal!, am, c.MarkerPositions, budget); MarkerRows(referenceFinal!, rm, c.MarkerPositions, budget);
        results.Add(Values(c.MarkerVectorsName, [2, 768], am, rm, Tolerance(c, c.MarkerVectorsName), budget)); budget.Check();
        var failed = results.Count(t => !t.Passed); var passed = failed == 0 && results.Count == 26;
        return new() { Status = fixture ? "fixture_only_comparison_complete" : "comparison_complete", CaseId = c.CaseId, ActualSha256 = actualHash, ReferenceSha256 = referenceHash,
            ContractSha256 = contractHash, OracleProgramSha256 = options.OracleProgramSha256, UpstreamSourceSha256 = options.UpstreamSourceSha256,
            FixtureOnly = fixture, Passed = passed, AcceptedIndependentReference = passed && !fixture, UpstreamExecuted = !fixture, ComparedCount = results.Count, FailedComparisons = failed,
            ElementDenominator = results.Sum(t => t.ElementCount), FailedElements = results.Sum(t => t.FailedElements), Results = results.ToArray(), StartedUtc = budget.StartedUtc,
            ElapsedSeconds = budget.ElapsedSeconds, Process = budget.Identity() };
    }
    internal static ComparisonReport RunPort(Options options, Budget budget)
    {
        budget.Check(); Io.NewOutput(options.Output, budget);
        var actual = Io.Json(options.Actual, CompareJsonContext.Default.ActualCapture, budget, out var actualHash);
        var reference = Io.Json(options.Reference, PortJson.Default.PortCapture, budget, out var referenceHash);
        var contract = PortReferenceProof.Contract(options.Contract, options.ContractSha256, budget);
        PortReferenceProof.Shape(reference, contract);
        PortReferenceProof.Verify(reference, contract, options, referenceHash, actualHash, budget);
        var report = PortPayload(actual, reference, contract.Numeric, options, actualHash, referenceHash, budget, false);
        return report with { AcceptedPortDerivedReference = report.Passed, AcceptedIndependentReference = false };
    }
    internal static ComparisonReport PortPayload(ActualCapture actual, PortCapture reference, NumericView c, Options options,
        string actualHash, string referenceHash, Budget budget, bool fixture)
    {
        var actualRoot = Path.GetDirectoryName(Path.GetFullPath(options.Actual))!; var referenceRoot = Path.GetDirectoryName(Path.GetFullPath(options.Reference))!;
        Require.That(!string.Equals(actualRoot, referenceRoot, StringComparison.OrdinalIgnoreCase), "Actual/reference capture roots must be independent.");
        Require.That(actual.SchemaVersion == 1 && actual.Status == "cpu_capture_complete" && actual.Kind == "csharp_cpu_capture" && actual.Backend == "csharp_scalar_fp32" && actual.ModelId == "sezika/mmbert-base-independent-s4" && actual.SourceModelId == c.SourceModelId && actual.SourceRevision == c.SourceRevision && actual.Protocol == c.Protocol && actual.LengthPolicy == c.LengthPolicy && actual.CaseId == c.CaseId && !actual.RepresentsUpstreamParity && actual.ExpectedCheckpoints == 267 && actual.IntegerDtype == "I32" && actual.IntegerLayout == "little_endian_contiguous" && (fixture ? actual.FixtureOnly && reference.FixtureOnly : !actual.FixtureOnly && !reference.FixtureOnly), "Actual port comparison identity/status/fixture mismatch.");
        Require.That(reference.SourceModelId == c.SourceModelId && reference.SourceRevision == c.SourceRevision && reference.CaseId == c.CaseId && reference.ModelId == actual.ModelId && reference.Protocol == c.Protocol && reference.LengthPolicy == c.LengthPolicy && !reference.UpstreamExecuted, "Port numeric payload identity differs.");
        Require.That(actual.Assets.Length is >= 3 and <= 16 && actual.Assets.All(item => item is not null) && actual.Assets.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == actual.Assets.Length, "Actual port assets invalid/duplicate.");
        var config = Asset(actual, "source-config.json"); Require.That(Io.SameHash(config.Sha256, c.ConfigSha256), "Actual port config differs."); Io.Artifact(actualRoot, config, 1048576, budget);
        var package = Io.Parse(Io.Artifact(actualRoot, Asset(actual, "package-manifest.json"), 1048576, budget), CompareJsonContext.Default.PackageManifest, budget);
        Require.That(package.SchemaVersion == 1 && package.ModelId == actual.ModelId && package.SourceModelId == c.SourceModelId && package.SourceRevision == c.SourceRevision && Io.SameHash(package.SourceWeightsSha256, c.SourceWeightsSha256) && Io.SameHash(package.WeightsSha256, c.ConvertedEncoderSha256) && Io.SameHash(package.TokenizerSha256, c.TokenizerSha256), "Actual port package snapshot differs.");
        Require.That(Io.SameHash(Asset(actual, "pytorch_model.bin").Sha256, c.SourceWeightsSha256) && Asset(actual, "pytorch_model.bin").Bytes == c.SourceWeightsBytes && Io.SameHash(Asset(actual, "encoder.safetensors").Sha256, c.ConvertedEncoderSha256) && Asset(actual, "encoder.safetensors").Bytes == 1227772549 && Io.SameHash(Asset(actual, "tokenizer/tokenizer.json").Sha256, c.TokenizerSha256) && Asset(actual, "tokenizer/tokenizer.json").Bytes == 17525329, "Actual port loader source differs.");
        Integers(actualRoot, actual.TokenIds, actual.MarkerPositions, actual.TokenIdsArtifact, actual.MarkerPositionsArtifact, c, budget);
        Integers(referenceRoot, reference.TokenIds, reference.MarkerPositions, reference.TokenIdsArtifact, reference.MarkerPositionsArtifact, c, budget);
        Require.That(actual.TokenIds.SequenceEqual(reference.TokenIds) && actual.MarkerPositions.SequenceEqual(reference.MarkerPositions), "Port token/marker arrays differ.");
        Request(actualRoot, actual.Request, c, budget); Request(referenceRoot, reference.Request, c, budget);
        Metadata(actual.Tensors, actualRoot, true, budget); Metadata(reference.Tensors, referenceRoot, false, budget);
        MarkerMetadata(actual.MarkerVectors, actualRoot, budget); MarkerMetadata(reference.MarkerVectors, referenceRoot, budget);
        Require.That(!actual.Tensors.Any(item => item.Path == actual.MarkerVectors.Path) && !reference.Tensors.Any(item => item.Path == reference.MarkerVectors.Path), "Port marker path aliases checkpoint.");
        var paths = actual.Tensors.Where(item => c.Checkpoints.Contains(item.Name)).Select(item => Io.Relative(actualRoot, item.Path, budget)).Append(Io.Relative(actualRoot, actual.MarkerVectors.Path, budget)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require.That(reference.Tensors.Select(item => Io.Relative(referenceRoot, item.Path, budget)).Append(Io.Relative(referenceRoot, reference.MarkerVectors.Path, budget)).All(path => !paths.Contains(path)), "Port reference reuses actual tensor paths.");
        Require.That(Io.SameHash(actual.FinalHiddenSha256, actual.Tensors[^1].Sha256), "Actual port final hidden hash differs.");
        var results = new List<TensorResult>(26); byte[]? finalA = null, finalR = null; long bytesA = 0, bytesR = 0;
        for (var index = 0; index < 25; index++)
        {
            budget.Check(); var name = c.Checkpoints[index]; var a = actual.Tensors.Single(item => item.Name == name); var r = reference.Tensors[index];
            var ab = Tensor(actualRoot, a, budget); var rb = Tensor(referenceRoot, r, budget); bytesA += ab.Length; bytesR += rb.Length;
            Require.That(bytesA <= 4194304 && bytesR <= 4194304, "Port selected payload exceeds4MiB.");
            results.Add(Values(name, a.Shape, ab, rb, Tolerance(c, name), budget)); if (name == "encoder/final") { finalA = ab; finalR = rb; }
            Console.Error.WriteLine($"Port compared {index + 1}/26: {name}; failed_elements={results[^1].FailedElements}");
        }
        var am = Tensor(actualRoot, actual.MarkerVectors, budget); var rm = Tensor(referenceRoot, reference.MarkerVectors, budget);
        Require.That(bytesA + am.Length <= 4194304 && bytesR + rm.Length <= 4194304, "Port marker payload exceeds4MiB.");
        MarkerRows(finalA!, am, c.MarkerPositions, budget); MarkerRows(finalR!, rm, c.MarkerPositions, budget);
        results.Add(Values(c.MarkerVectorsName, [2, 768], am, rm, Tolerance(c, c.MarkerVectorsName), budget));
        var failed = results.Count(item => !item.Passed);
        return new() { SchemaVersion = "sezika.independent-numeric-comparison.port-derived.v2", Status = fixture ? "fixture_only_port_comparison_complete" : "port_comparison_complete",
            CaseId = c.CaseId, ActualSha256 = actualHash, ReferenceSha256 = referenceHash, ContractSha256 = options.ContractSha256,
            OracleProgramSha256 = reference.Executable.Sha256, UpstreamSourceSha256 = reference.UpstreamSources[0].Sha256,
            FixtureOnly = fixture, Passed = failed == 0 && results.Count == 26, AcceptedIndependentReference = false,
            AcceptedPortDerivedReference = false, ReferenceKind = "independently_ported_original_weights_capture", UpstreamExecuted = false,
            ComparedCount = results.Count, FailedComparisons = failed, ElementDenominator = results.Sum(item => item.ElementCount), FailedElements = results.Sum(item => item.FailedElements),
            Results = results.ToArray(), StartedUtc = budget.StartedUtc, ElapsedSeconds = budget.ElapsedSeconds, Process = budget.Identity() };
    }
    private static Artifact Asset(ActualCapture actual, string path) => actual.Assets.SingleOrDefault(a => a.Path == path) ?? throw new InvalidDataException($"Missing {path} source asset.");
    private static void Code(ProgramArtifact artifact, string path, string expectedHash, string root, Budget budget)
    {
        var expectedPath = Path.GetFullPath(path); var declaredPath = Path.IsPathRooted(artifact.Path) ? Path.GetFullPath(artifact.Path) : Path.GetFullPath(Path.Combine(root, artifact.Path));
        Require.That(string.Equals(expectedPath, declaredPath, StringComparison.OrdinalIgnoreCase) && Io.SameHash(artifact.Sha256, expectedHash), "Externally pinned code path/hash mismatch.");
        var bytes = Io.Read(expectedPath, 16_777_216, budget);
        Require.That(artifact.Bytes == bytes.Length && Io.SameHash(Io.Hash(bytes), expectedHash), "Code file bytes/hash mismatch.");
    }
    private static void Request(string root, Artifact artifact, FrozenContract c, Budget budget)
        => Request(root, artifact, FromLegacy(c), budget);
    private static void Request(string root, Artifact artifact, NumericView c, Budget budget)
    {
        Require.That(artifact.Path == "request.json" && Io.SameHash(artifact.Sha256, c.RequestSha256), "Request identity mismatch."); Io.Artifact(root, artifact, 1_048_576, budget);
    }
    private static void Integers(string root, int[] ids, int[] markers, Artifact tokenArtifact, Artifact markerArtifact, FrozenContract c, Budget budget)
        => Integers(root, ids, markers, tokenArtifact, markerArtifact, FromLegacy(c), budget);
    private static void Integers(string root, int[] ids, int[] markers, Artifact tokenArtifact, Artifact markerArtifact, NumericView c, Budget budget)
    {
        Require.That(ids.Length == 46 && ids.All(id => id is >= 0 and < 256000) && markers.SequenceEqual(c.MarkerPositions) &&
            tokenArtifact.Path == "token-ids.i32" && markerArtifact.Path == "marker-positions.i32" && Io.SameHash(tokenArtifact.Sha256, c.TokenIdsSha256) && Io.SameHash(markerArtifact.Sha256, c.MarkerPositionsSha256), "Integer input metadata mismatch.");
        var tokens = Io.Artifact(root, tokenArtifact, 184, budget); var positions = Io.Artifact(root, markerArtifact, 8, budget);
        Require.That(tokens.Length == 184 && positions.Length == 8, "I32 byte length mismatch.");
        for (var i = 0; i < 46; i++) { budget.Check(); Require.That(BinaryPrimitives.ReadInt32LittleEndian(tokens.AsSpan(i * 4, 4)) == ids[i], "Token array differs from bytes."); }
        for (var i = 0; i < 2; i++) { budget.Check(); Require.That(BinaryPrimitives.ReadInt32LittleEndian(positions.AsSpan(i * 4, 4)) == markers[i], "Marker array differs from bytes."); }
    }
    private static void Metadata(TensorArtifact[] tensors, string root, bool actual, Budget budget)
    {
        Require.That(tensors.Length == (actual ? 267 : 25) && tensors.All(t => t is not null) && tensors.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() == tensors.Length &&
            tensors.Select(t => t.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == tensors.Length, "Checkpoint count/name/path duplicate.");
        for (var i = 0; i < tensors.Length; i++)
        {
            budget.Check(); var t = tensors[i]; string expectedName; var width = 768;
            if (!actual) expectedName = Checkpoints[i];
            else if (i < 2) expectedName = Checkpoints[i];
            else if (i == 266) expectedName = "encoder/final";
            else
            {
                var layer = (i - 2) / 12; var slot = (i - 2) % 12; expectedName = $"layer/{layer}/{LayerNames[slot]}";
                width = slot == 1 ? 2304 : slot == 8 ? 2304 : slot == 9 ? 1152 : 768;
            }
            Require.That(t.Name == expectedName && t.Shape.SequenceEqual([46, width]) && t.Dtype == "F32" && t.Layout == "little_endian_row_major" && t.Bytes == checked(46L * width * 4) && Io.ValidHash(t.Sha256), "Checkpoint metadata/name/order/shape/layout mismatch.");
            Io.Relative(root, t.Path, budget);
        }
    }
    private static void MarkerMetadata(TensorArtifact t, string root, Budget budget)
    {
        Require.That(t.Name == "marker-vectors.f32" && t.Path == "marker-vectors.f32" && t.Dtype == "F32" && t.Layout == "little_endian_row_major" && t.Shape.SequenceEqual([2, 768]) && t.Bytes == 6144 && Io.ValidHash(t.Sha256), "Marker vector metadata mismatch.");
        Io.Relative(root, t.Path, budget);
    }
    private static byte[] Tensor(string root, TensorArtifact t, Budget budget)
    {
        var bytes = Io.Artifact(root, new(t.Path, t.Sha256, t.Bytes), 423_936, budget);
        for (var i = 0; i < bytes.Length / 4; i++)
        { if ((i & 255) == 0) budget.Check(); Require.That(float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4))), "Tensor contains NaN/Infinity."); }
        return bytes;
    }
    private static Tolerance Tolerance(FrozenContract c, string name) => c.Tolerances[name.StartsWith("layer/", StringComparison.Ordinal) ? "layer_hidden" : name];
    private static Tolerance Tolerance(NumericView c, string name) => c.Tolerances[name.StartsWith("layer/", StringComparison.Ordinal) ? "layer_hidden" : name];
    private static NumericView FromLegacy(FrozenContract c) => new(c.SourceModelId, c.SourceRevision, c.SourceWeightsSha256, c.SourceWeightsBytes, c.ConfigSha256,
        c.TokenizerSha256, c.ConvertedEncoderSha256, c.CaseId, c.RequestSha256, c.TokenIdsSha256, c.MarkerPositionsSha256, c.Protocol, c.LengthPolicy,
        c.MarkerPositions, c.Checkpoints, c.MarkerVectorsName, c.Tolerances);
    private static TensorResult Values(string name, int[] shape, byte[] actual, byte[] reference, Tolerance tolerance, Budget budget)
    {
        Require.That(actual.Length == reference.Length, "Tensor byte lengths differ."); long failures = 0, maxIndex = 0; double maxAbsolute = 0, maxRelative = 0;
        for (var i = 0; i < actual.Length / 4; i++)
        {
            if ((i & 255) == 0) budget.Check(); double a = BinaryPrimitives.ReadSingleLittleEndian(actual.AsSpan(i * 4, 4)); double r = BinaryPrimitives.ReadSingleLittleEndian(reference.AsSpan(i * 4, 4));
            var error = Math.Abs(a - r); var relative = error / Math.Max(Math.Abs(r), 1e-12);
            if (error > maxAbsolute) { maxAbsolute = error; maxIndex = i; } maxRelative = Math.Max(maxRelative, relative);
            if (error > tolerance.Absolute + tolerance.Relative * Math.Abs(r)) failures++;
        }
        return new(name, shape, actual.Length / 4, failures, maxAbsolute, maxRelative, maxIndex, failures == 0);
    }
    private static void MarkerRows(byte[] final, byte[] marker, int[] positions, Budget budget)
    {
        for (var i = 0; i < 2; i++) { budget.Check(); Require.That(final.AsSpan(positions[i] * 768 * 4, 768 * 4).SequenceEqual(marker.AsSpan(i * 768 * 4, 768 * 4)), "Marker bytes differ from own final rows."); }
    }
}
