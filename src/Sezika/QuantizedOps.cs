using System.Diagnostics;

namespace Sezika;

/// <summary>
/// Immutable, row-major W8A32 matrix: signed int8 weights, one symmetric
/// float scale per output row, and zero point 0. Activations and accumulation
/// remain float32. This is weight-only quantization, not integer activation inference.
/// </summary>
public sealed class Int8WeightMatrix
{
    private readonly sbyte[] _values;
    private readonly float[] _scales;

    private Int8WeightMatrix(sbyte[] values, float[] scales, int inputSize, int outputSize)
    {
        _values = values;
        _scales = scales;
        InputSize = inputSize;
        OutputSize = outputSize;
    }

    public int InputSize { get; }
    public int OutputSize { get; }
    /// <summary>Tensor payload bytes only; excludes object/array headers and retained float32 source weights.</summary>
    public long StorageBytes => checked((long)_values.Length + (long)_scales.Length * sizeof(float));

    public static Int8WeightMatrix Create(float[] weights, int inputSize, int outputSize, CancellationToken cancellationToken = default)
    {
        if (inputSize is < 1 or > 8192 || outputSize is < 1 or > 16384 || (long)inputSize * outputSize > 33_554_432)
            throw new DecisionException("model_tensor_shape_invalid", "Quantized matrix dimensions exceed the CPU encoder bounds.");
        ScalarOps.Shape(weights, checked(inputSize * outputSize));
        cancellationToken.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        var values = new sbyte[weights.Length];
        var scales = new float[outputSize];
        for (var row = 0; row < outputSize; row++)
        {
            CheckBound(started, cancellationToken);
            var offset = row * inputSize;
            var maximum = 0f;
            for (var column = 0; column < inputSize; column++)
            {
                var value = weights[offset + column];
                if (!float.IsFinite(value))
                    throw new DecisionException("model_quantization_non_finite", "Int8 weight quantization requires finite source weights.");
                maximum = MathF.Max(maximum, MathF.Abs(value));
            }
            // All-zero rows use scale 1 and stay exactly zero. Subnormal rows
            // must not produce a zero scale through float32 underflow.
            var scale = maximum == 0f ? 1f : MathF.Max(float.Epsilon, maximum / 127f);
            scales[row] = scale;
            for (var column = 0; column < inputSize; column++)
                values[offset + column] = (sbyte)Math.Clamp(MathF.Round(weights[offset + column] / scale, MidpointRounding.AwayFromZero), -127f, 127f);
        }
        CheckBound(started, cancellationToken);
        return new Int8WeightMatrix(values, scales, inputSize, outputSize);
    }

    internal ReadOnlySpan<sbyte> Values => _values;
    internal ReadOnlySpan<float> Scales => _scales;

    internal static void CheckBound(long started, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMinutes(10))
            throw new DecisionException("encoder_deadline_exceeded", "Quantized CPU operation exceeded its ten-minute hard limit.");
    }
}

public static class QuantizedOps
{
    /// <summary>
    /// Reads int8 weights directly without constructing a float32 matrix.
    /// Each weight is dequantized in the dot product; float32 reduction order
    /// matches the scalar reference. Bias, normalization and nonlinearities
    /// are never quantized.
    /// </summary>
    public static float[] Linear(float[] input, Int8WeightMatrix weights, float[]? bias, int rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (rows is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(rows));
        ScalarOps.Shape(input, checked(rows * weights.InputSize));
        if (bias is not null) ScalarOps.Shape(bias, weights.OutputSize);
        cancellationToken.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        var output = new float[checked(rows * weights.OutputSize)];
        var values = weights.Values;
        var scales = weights.Scales;
        for (var row = 0; row < rows; row++)
        {
            Int8WeightMatrix.CheckBound(started, cancellationToken);
            for (var column = 0; column < weights.OutputSize; column++)
            {
                if ((column & 63) == 0) Int8WeightMatrix.CheckBound(started, cancellationToken);
                var sum = 0f;
                var scale = scales[column];
                var weightOffset = column * weights.InputSize;
                var inputOffset = row * weights.InputSize;
                for (var index = 0; index < weights.InputSize; index++)
                    sum += input[inputOffset + index] * (values[weightOffset + index] * scale);
                output[row * weights.OutputSize + column] = sum + (bias is null ? 0f : bias[column]);
            }
        }
        Int8WeightMatrix.CheckBound(started, cancellationToken);
        return output;
    }
}

/// <summary>Prepared once per encoder/head; no quantization or weight expansion in requests.</summary>
internal sealed class Int8WeightCache
{
    private readonly Dictionary<float[], Int8WeightMatrix> _matrices = new(ReferenceEqualityComparer.Instance);
    public long StorageBytes { get; private set; }

    public void Add(float[] source, int inputSize, int outputSize, CancellationToken cancellationToken)
    {
        if (_matrices.TryGetValue(source, out var existing))
        {
            if (existing.InputSize != inputSize || existing.OutputSize != outputSize)
                throw new DecisionException("model_tensor_shape_invalid", "Shared quantized matrix sources must have identical dimensions.");
            return;
        }
        var matrix = Int8WeightMatrix.Create(source, inputSize, outputSize, cancellationToken);
        _matrices.Add(source, matrix);
        StorageBytes = checked(StorageBytes + matrix.StorageBytes);
    }

    public Int8WeightMatrix Get(float[] source) => _matrices[source];
}
