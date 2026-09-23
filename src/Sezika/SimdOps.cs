using System.Numerics;

namespace Sezika;

/// <summary>
/// Explicitly vectorized CPU primitives. Matrix layout is kept identical to
/// <see cref="ScalarOps"/> ([output,input]), so scalar remains the oracle and
/// this class can be compared operation by operation.
/// </summary>
public static class SimdOps
{
    public static float[] Linear(float[] input, float[] weights, float[]? bias, int rows, int inputSize, int outputSize, CancellationToken cancellationToken)
    {
        ScalarOps.Shape(input, checked(rows * inputSize));
        ScalarOps.Shape(weights, checked(inputSize * outputSize));
        if (bias is not null) ScalarOps.Shape(bias, outputSize);
        var output = new float[checked(rows * outputSize)];
        var width = Vector<float>.Count;
        for (var row = 0; row < rows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputOffset = row * inputSize;
            var outputOffset = row * outputSize;
            for (var column = 0; column < outputSize; column++)
            {
                var weightOffset = column * inputSize;
                var sum = Vector<float>.Zero;
                var scalar = 0f;
                var index = 0;
                for (; index <= inputSize - width; index += width)
                {
                    if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                    sum += new Vector<float>(input, inputOffset + index) * new Vector<float>(weights, weightOffset + index);
                }
                for (var lane = 0; lane < width; lane++) scalar += sum[lane];
                for (; index < inputSize; index++) scalar += input[inputOffset + index] * weights[weightOffset + index];
                output[outputOffset + column] = scalar + (bias is null ? 0 : bias[column]);
            }
        }
        return output;
    }

    public static float[] Norm(float[] input, int rows, int width, float[] gamma, float[]? beta, float epsilon, CancellationToken cancellationToken)
    {
        ScalarOps.Shape(input, checked(rows * width));
        ScalarOps.Shape(gamma, width);
        if (beta is not null) ScalarOps.Shape(beta, width);
        var output = new float[input.Length];
        var vectorWidth = Vector<float>.Count;
        for (var row = 0; row < rows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = row * width;
            var sumVector = Vector<float>.Zero;
            var index = 0;
            for (; index <= width - vectorWidth; index += vectorWidth)
            {
                if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                var values = new Vector<float>(input, start + index);
                sumVector += values;
            }
            var meanSum = 0f;
            for (var lane = 0; lane < vectorWidth; lane++) meanSum += sumVector[lane];
            for (; index < width; index++) meanSum += input[start + index];
            var mean = meanSum / width;
            var varianceVector = Vector<float>.Zero;
            index = 0;
            for (; index <= width - vectorWidth; index += vectorWidth)
            {
                if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                var values = new Vector<float>(input, start + index) - new Vector<float>(mean);
                varianceVector += values * values;
            }
            var variance = 0f;
            for (var lane = 0; lane < vectorWidth; lane++) variance += varianceVector[lane];
            for (; index < width; index++)
            {
                var delta = input[start + index] - mean;
                variance += delta * delta;
            }
            var inverse = 1f / MathF.Sqrt(variance / width + epsilon);
            for (index = 0; index < width; index++)
            {
                if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                output[start + index] = (input[start + index] - mean) * inverse * gamma[index] + (beta is null ? 0 : beta[index]);
            }
        }
        return output;
    }

    public static float[] Add(float[] a, float[] b, CancellationToken cancellationToken)
    {
        ScalarOps.Shape(b, a.Length);
        var output = new float[a.Length];
        var width = Vector<float>.Count;
        var index = 0;
        for (; index <= a.Length - width; index += width)
        {
            if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            (new Vector<float>(a, index) + new Vector<float>(b, index)).CopyTo(output, index);
        }
        for (; index < a.Length; index++) output[index] = a[index] + b[index];
        return output;
    }

    public static float[] GatedGelu(float[] up, int rows, int width, CancellationToken cancellationToken)
    {
        ScalarOps.Shape(up, checked(rows * width * 2));
        var output = new float[checked(rows * width)];
        var vectorWidth = Vector<float>.Count;
        var gelu = new float[width];
        for (var row = 0; row < rows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = row * width * 2;
            for (var index = 0; index < width; index++) gelu[index] = ScalarOps.Gelu(up[offset + index]);
            var target = row * width;
            var indexVector = 0;
            for (; indexVector <= width - vectorWidth; indexVector += vectorWidth)
            {
                if ((indexVector & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                (new Vector<float>(gelu, indexVector) * new Vector<float>(up, offset + width + indexVector)).CopyTo(output, target + indexVector);
            }
            for (; indexVector < width; indexVector++) output[target + indexVector] = gelu[indexVector] * up[offset + width + indexVector];
        }
        return output;
    }
}
