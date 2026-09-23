using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sezika;

public sealed record TensorManifest
{
    public required string Name { get; init; }
    public required string File { get; init; }
    public required string Sha256 { get; init; }
    public required string Dtype { get; init; }
    public required int[] Shape { get; init; }
}

public sealed record ModelManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string ModelId { get; init; }
    public required string Revision { get; init; }
    public required string Architecture { get; init; }
    public required string TokenizerRevision { get; init; }
    public required string TokenizerFile { get; init; }
    public required string WeightsFile { get; init; }
    public required TensorManifest[] Tensors { get; init; }
    public required TransformerConfig Encoder { get; init; }
    public required string HeadWeightsTensor { get; init; }
    public float HeadBias { get; init; }
    public string? FinalNormTensor { get; init; }
    public long MaxResidentBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public string? CalibrationFile { get; init; }
    public string? License { get; init; }
}

public static class ModelAssetVerifier
{
    public static ModelManifest LoadAndVerify(string packageDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory)) throw new ArgumentException("Package directory is required.", nameof(packageDirectory));
        var root = Path.GetFullPath(packageDirectory);
        if (!Directory.Exists(root)) throw new DecisionException("decision_model_not_installed", "Model package directory does not exist.");
        var manifestPath = CombineWithin(root, "model.json");
        if (!File.Exists(manifestPath)) throw new DecisionException("decision_manifest_missing", "model.json is missing.");
        ModelManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(File.ReadAllBytes(manifestPath), DecisionJsonContext.Default.ModelManifest);
        }
        catch (JsonException exception)
        {
            throw new DecisionException("decision_manifest_invalid", exception.Message, exception);
        }
        if (manifest is null) throw new DecisionException("decision_manifest_invalid", "model.json is empty.");
        ValidateManifest(manifest);
        var total = 0L;
        foreach (var tensor in manifest.Tensors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = CombineWithin(root, tensor.File);
            VerifyFile(path, tensor.Sha256, cancellationToken);
            var info = new FileInfo(path);
            total = checked(total + info.Length);
            if (total > manifest.MaxResidentBytes) throw new DecisionException("decision_model_memory_limit_exceeded", "Model assets exceed the resident byte budget.");
        }
        VerifyFile(CombineWithin(root, manifest.TokenizerFile), null, cancellationToken);
        VerifyFile(CombineWithin(root, manifest.WeightsFile), null, cancellationToken);
        return manifest;
    }

    private static void ValidateManifest(ModelManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.ModelId) || string.IsNullOrWhiteSpace(manifest.Revision) ||
            string.IsNullOrWhiteSpace(manifest.Architecture) || string.IsNullOrWhiteSpace(manifest.TokenizerRevision) ||
            manifest.Tensors is null || manifest.Tensors.Length == 0 || manifest.MaxResidentBytes <= 0 ||
            manifest.Encoder is null || string.IsNullOrWhiteSpace(manifest.HeadWeightsTensor))
            throw new DecisionException("decision_manifest_invalid", "Model manifest fields are incomplete or unsupported.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tensor in manifest.Tensors)
        {
            if (tensor.Shape is null || tensor.Shape.Length == 0 || tensor.Shape.Length > 8 || string.IsNullOrWhiteSpace(tensor.Name) || !names.Add(tensor.Name) ||
                !tensor.Sha256.All(Uri.IsHexDigit) || tensor.Sha256.Length != 64)
                throw new DecisionException("decision_manifest_invalid", "Tensor manifest contains an invalid entry.");
            long elements = 1;
            foreach (var dimension in tensor.Shape)
            {
                if (dimension <= 0 || (elements = checked(elements * dimension)) > 100_000_000)
                    throw new DecisionException("decision_manifest_memory_limit_exceeded", "Tensor shape exceeds the configured limit.");
            }
        }
        manifest.Encoder.Validate();
    }

    private static string CombineWithin(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new DecisionException("decision_manifest_path_invalid", "Model asset paths must be relative.");
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)) throw new DecisionException("decision_manifest_path_invalid", "Model asset escapes its package directory.");
        return full;
    }

    private static void VerifyFile(string path, string? expectedSha256, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new DecisionException("decision_asset_missing", $"Required model asset '{Path.GetFileName(path)}' is missing.");
        if (expectedSha256 is null) return;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasher.AppendData(buffer, 0, read);
        }
        var actual = Convert.ToHexString(hasher.GetHashAndReset());
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new DecisionException("decision_asset_hash_mismatch", $"Asset '{Path.GetFileName(path)}' failed SHA-256 verification.");
    }
}

public sealed record SafeTensor
{
    public required string Name { get; init; }
    public required string Dtype { get; init; }
    public required int[] Shape { get; init; }
    public required float[] Values { get; init; }
}

/// <summary>Reads the safe-tensors container without deserializing executable or pickle content.</summary>
public static class SafeTensorReader
{
    public static IReadOnlyDictionary<string, SafeTensor> Read(string path, long maxHeaderBytes = 16 * 1024 * 1024, long maxElements = 100_000_000, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        Span<byte> lengthBytes = stackalloc byte[8];
        ReadExactly(stream, lengthBytes);
        var headerLength = BinaryPrimitives.ReadUInt64LittleEndian(lengthBytes);
        if (headerLength == 0 || headerLength > (ulong)maxHeaderBytes || headerLength > (ulong)(stream.Length - 8)) throw new DecisionException("decision_tensor_header_invalid", "Safe-tensors header is outside the allowed range.");
        var header = new byte[(int)headerLength];
        ReadExactly(stream, header);
        using var document = JsonDocument.Parse(header, new JsonDocumentOptions { MaxDepth = 16 });
        var result = new Dictionary<string, SafeTensor>(StringComparer.Ordinal);
        var descriptors = new List<(string name, string dtype, int[] shape, int count, ulong start, ulong end)>();
        var ranges = new List<(ulong start, ulong end, string name)>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.NameEquals("__metadata__")) continue;
            var element = property.Value;
            if (!element.TryGetProperty("dtype", out var dtypeElement) || !element.TryGetProperty("shape", out var shapeElement) || !element.TryGetProperty("data_offsets", out var offsetsElement))
                throw new DecisionException("decision_tensor_header_invalid", $"Tensor '{property.Name}' is missing required fields.");
            var dtype = dtypeElement.GetString() ?? string.Empty;
            var shape = shapeElement.EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var offsets = offsetsElement.EnumerateArray().Select(x => x.GetUInt64()).ToArray();
            if (shape.Length == 0 || offsets.Length != 2 || offsets[1] <= offsets[0]) throw new DecisionException("decision_tensor_header_invalid", $"Tensor '{property.Name}' has invalid shape or offsets.");
            long elements = 1;
            foreach (var dimension in shape) elements = checked(elements * dimension);
            if (elements <= 0 || elements > maxElements) throw new DecisionException("decision_tensor_memory_limit_exceeded", $"Tensor '{property.Name}' is too large.");
            var bytesPerElement = dtype switch { "F32" => 4, "F16" or "BF16" => 2, _ => 0 };
            if (bytesPerElement == 0 || (ulong)(elements * bytesPerElement) != offsets[1] - offsets[0]) throw new DecisionException("decision_tensor_dtype_unsupported", $"Tensor '{property.Name}' has unsupported dtype or byte length.");
            var absoluteStart = checked((ulong)8 + headerLength + offsets[0]);
            var absoluteEnd = checked((ulong)8 + headerLength + offsets[1]);
            if (absoluteEnd > (ulong)stream.Length) throw new DecisionException("decision_tensor_bounds_invalid", $"Tensor '{property.Name}' exceeds the file.");
            ranges.Add((absoluteStart, absoluteEnd, property.Name));
            descriptors.Add((property.Name, dtype, shape, checked((int)elements), absoluteStart, absoluteEnd));
        }
        ranges.Sort((left, right) => left.start.CompareTo(right.start));
        for (var i = 1; i < ranges.Count; i++) if (ranges[i].start < ranges[i - 1].end) throw new DecisionException("decision_tensor_overlap", "Safe-tensors ranges overlap.");
        foreach (var descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(descriptor.name, new SafeTensor
            {
                Name = descriptor.name,
                Dtype = descriptor.dtype,
                Shape = descriptor.shape,
                Values = ReadValues(stream, descriptor.start, descriptor.count, descriptor.dtype, cancellationToken),
            });
        }
        return result;
    }

    private static float[] ReadValues(FileStream stream, ulong offset, int count, string dtype, CancellationToken cancellationToken)
    {
        stream.Position = checked((long)offset);
        var bytes = new byte[checked(count * (dtype == "F32" ? 4 : 2))];
        ReadExactly(stream, bytes);
        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = dtype switch
            {
                "F32" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * 4, 4))),
                "F16" => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2))),
                "BF16" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2)) << 16),
                _ => throw new DecisionException("decision_tensor_dtype_unsupported", "Unsupported tensor dtype."),
            };
            if (!float.IsFinite(values[i])) throw new DecisionException("decision_tensor_numeric_invalid", "Tensor contains NaN or infinity.");
            cancellationToken.ThrowIfCancellationRequested();
        }
        return values;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer);
            if (read == 0) throw new DecisionException("decision_tensor_truncated", "Safe-tensors file is truncated.");
            buffer = buffer[read..];
        }
    }
    private static void ReadExactly(Stream stream, byte[] buffer) => ReadExactly(stream, buffer.AsSpan());
}
