using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Sezika.ReferenceTool;

internal sealed class OriginalWeights : IDisposable
{
    internal const string OriginalSha = "8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e";
    internal const long OriginalBytes = 1231188142;
    private readonly FileStream _stream;
    private readonly ZipArchive _archive;
    private readonly Budget _budget;
    private readonly Dictionary<string, TensorDescriptor> _descriptors;
    private readonly HashSet<string> _loaded = new(StringComparer.Ordinal);
    private readonly List<WeightTensorProof> _proofs = [];
    internal WeightTensorProof[] Proofs => _proofs.ToArray();
    internal OriginalWeights(FilePin source, Budget budget)
    {
        _budget = budget;
        if (source.Bytes != OriginalBytes || source.Sha256.ToLowerInvariant() != OriginalSha) throw new InvalidDataException("Fixed original mmBERT weights identity differs.");
        budget.Verify(source); _stream = new(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            _archive = new(_stream, ZipArchiveMode.Read, true);
            if (_archive.Entries.Count is < 1 or > 4096 || _archive.Entries.Select(item => item.FullName).Distinct(StringComparer.Ordinal).Count() != _archive.Entries.Count) throw new InvalidDataException("ZIP entry count/duplicate differs.");
            var entry = _archive.GetEntry("pytorch_model/data.pkl") ?? throw new InvalidDataException("Fixed PyTorch pickle missing.");
            if (entry.Length is < 1 or > 4194304) throw new InvalidDataException("Pickle byte bound4MiB.");
            using var input = entry.Open(); var bytes = new byte[checked((int)entry.Length)]; input.ReadExactly(bytes);
            _descriptors = new RestrictedPickle(bytes, budget).Read();
            if (_descriptors.Keys.Count(name => name.StartsWith("model.", StringComparison.Ordinal)) != 134) throw new InvalidDataException("Expected exactly134 original encoder tensors.");
        }
        catch { _archive?.Dispose(); _stream.Dispose(); throw; }
    }
    internal float[] Read(string name, params int[] shape) => ReadCore(name, shape, null);
    internal float[] Embeddings(int[] tokenIds) => ReadCore("model.embeddings.tok_embeddings.weight", [256000, 768], tokenIds);
    private float[] ReadCore(string name, int[] shape, int[]? selectedRows)
    {
        _budget.Check();
        if (!_loaded.Add(name) || !_descriptors.TryGetValue(name, out var tensor) || !tensor.Shape.SequenceEqual(shape.Select(value => (long)value))) throw new InvalidDataException("Missing/duplicate/wrong-shaped original tensor: " + name);
        long stride = 1; for (var axis = shape.Length - 1; axis >= 0; axis--) { if (tensor.Stride[axis] != stride) throw new InvalidDataException("Only original contiguous FP32 tensors are admitted."); stride = checked(stride * shape[axis]); }
        var entry = _archive.GetEntry("pytorch_model/data/" + tensor.StorageKey) ?? throw new InvalidDataException("Original storage missing.");
        var bytes = checked(tensor.Numel * 4); var offset = checked(tensor.Offset * 4);
        if (entry.Length > 2147483648 || offset > entry.Length - bytes) throw new InvalidDataException("Original storage byte extent differs.");
        var result = new float[selectedRows is null ? checked((int)tensor.Numel) : checked(selectedRows.Length * 768)];
        using var input = entry.Open(); var buffer = new byte[1048576];
        long skipped = 0; for (var block = 0; block < 2049 && skipped < offset; block++) { _budget.Check(); var count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, offset - skipped)); if (count == 0) throw new EndOfStreamException(); skipped += count; }
        if (skipped != offset) throw new InvalidDataException("Storage skip bound2049.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long consumed = 0;
        for (var block = 0; block < 2049 && consumed < bytes; block++)
        {
            _budget.Check(); var count = (int)Math.Min(buffer.Length, bytes - consumed); input.ReadExactly(buffer.AsSpan(0, count)); hash.AppendData(buffer, 0, count);
            if (selectedRows is null)
            {
                for (var index = 0; index < count; index += 4) { var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(index, 4))); if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite original weight."); result[checked((int)((consumed + index) / 4))] = value; }
            }
            else
            {
                for (var token = 0; token < selectedRows.Length; token++)
                {
                    var rowStart = checked((long)selectedRows[token] * 768 * 4); var start = Math.Max(rowStart, consumed); var end = Math.Min(rowStart + 3072, consumed + count);
                    for (var position = start; position < end; position += 4) { var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan((int)(position - consumed), 4))); if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite selected original embedding."); result[token * 768 + (int)((position - rowStart) / 4)] = value; }
                }
            }
            consumed += count;
        }
        if (consumed != bytes) throw new InvalidDataException("Original tensor read bound2049.");
        _proofs.Add(new(name, tensor.StorageKey, tensor.Offset, tensor.Shape, bytes, Convert.ToHexStringLower(hash.GetHashAndReset())));
        Console.WriteLine($"original-tensor {_proofs.Count}/134 {name}"); return result;
    }
    internal void Complete() { if (_loaded.Count != 134 || _descriptors.Keys.Any(name => name.StartsWith("model.", StringComparison.Ordinal) && !_loaded.Contains(name))) throw new InvalidDataException("Original tensor denominator incomplete."); }
    public void Dispose() { _archive.Dispose(); _stream.Dispose(); }
}
