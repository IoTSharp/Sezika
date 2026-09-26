using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal sealed class TraceCollector
{
    public const long MaxBytes = 128L * 1024 * 1024;
    private readonly Dictionary<string, int> _expected;
    private readonly Dictionary<string, float[]> _scalar;
    private readonly bool _reference;
    private readonly CancellationToken _token;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    public List<TensorDifference> Differences { get; } = [];
    public long RetainedBytes { get; private set; }
    public List<string> Missing => _expected.Keys.Where(name => !_seen.Contains(name)).ToList();

    public TraceCollector(int tokens, int hidden, int encoderLayers, int headLayers,
        Dictionary<string, float[]> scalar, bool reference, CancellationToken token)
    {
        if (tokens is < 2 or > 1024 || hidden is < 1 or > 4096 || encoderLayers is < 1 or > 64 || headLayers != 2)
            throw new InvalidDataException("Trace dimensions exceed the diagnostic limits.");
        var elements = checked(tokens * hidden);
        var bytes = checked(((long)encoderLayers + headLayers + 2) * elements * sizeof(float) + tokens * sizeof(float));
        if (bytes > MaxBytes) throw new InvalidDataException("Selected layer snapshots exceed the 128 MiB retained trace limit.");
        _expected = new(StringComparer.Ordinal) { ["embedding/norm"] = elements, ["encoder/final"] = elements, ["scorer/logits"] = tokens };
        for (var index = 0; index < encoderLayers; index++) _expected.Add($"layer/{index}/hidden", elements);
        for (var index = 0; index < headLayers; index++) _expected.Add($"head/{index}/hidden", elements);
        _scalar = scalar; _reference = reference; _token = token;
    }

    public void Observe(string name, float[] values)
    {
        _token.ThrowIfCancellationRequested();
        if (!_expected.TryGetValue(name, out var expectedLength)) return;
        if (!_seen.Add(name)) throw new InvalidDataException($"Duplicate trace: {name}.");
        if (values.Length != expectedLength) throw new InvalidDataException($"Trace shape mismatch: {name}.");
        if (_reference)
        {
            for (var index = 0; index < values.Length; index++)
            {
                if ((index & 4095) == 0) _token.ThrowIfCancellationRequested();
                if (!float.IsFinite(values[index])) throw new InvalidDataException($"Non-finite trace: {name}.");
            }
            RetainedBytes = checked(RetainedBytes + values.LongLength * sizeof(float));
            if (RetainedBytes > MaxBytes || !_scalar.TryAdd(name, values)) throw new InvalidDataException("Trace retention limit or duplicate scalar key.");
        }
        else
        {
            if (!_scalar.TryGetValue(name, out var reference)) throw new InvalidDataException($"Missing scalar trace: {name}.");
            Differences.Add(Compare(name, reference, values, _token));
        }
    }

    internal static TensorDifference Compare(string name, float[] expected, float[] actual, CancellationToken token)
    {
        if (expected.Length == 0 || expected.Length != actual.Length) throw new InvalidDataException("Trace shape mismatch.");
        double max = 0, sumSquares = 0, maxRelative = 0;
        var worst = 0;
        for (var index = 0; index < expected.Length; index++)
        {
            if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
            if (!float.IsFinite(expected[index]) || !float.IsFinite(actual[index])) throw new InvalidDataException("Non-finite trace value.");
            var error = Math.Abs((double)actual[index] - expected[index]);
            if (error > max) { max = error; worst = index; }
            sumSquares += error * error;
            maxRelative = Math.Max(maxRelative, error / Math.Max(1e-12, Math.Abs(expected[index])));
        }
        return new(name, expected.Length, max, Math.Sqrt(sumSquares / expected.Length), maxRelative,
            worst, Hash(expected, token), Hash(actual, token));
    }

    private static string Hash(float[] values, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var offset = 0; offset < values.Length; offset += 4096)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(MemoryMarshal.AsBytes(values.AsSpan(offset, Math.Min(4096, values.Length - offset))));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
