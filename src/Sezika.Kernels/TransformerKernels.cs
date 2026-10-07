using ILGPU;
using ILGPU.Algorithms;

namespace Sezika.Kernels;

// Explicit grouping intentionally removes the hidden implicit-index argument.
// All loops are bounded by validated model/sequence dimensions at the host boundary.
public static class TransformerKernels
{
    public static void VectorAdd(ArrayView<float> a, ArrayView<float> b, ArrayView<float> output, int count)
    {
        int i = Grid.GlobalIndex.X;
        if (i < count) output[i] = a[i] + b[i];
    }

    // One question, <=32 candidates. The host validates the complete shape.
    public static void MarkerCrossEntropy(ArrayView<float> logits, ArrayView<float> delta,
        ArrayView<float> loss, int candidates, int type, int target)
    {
        if (Grid.GlobalIndex.X != 0) return;
        float maximum = -float.MaxValue;
        loss[0] = float.NaN;
        for (int candidate = 0; candidate < candidates; candidate++) delta[candidate] = 0;
        for (int candidate = 0; candidate < candidates; candidate++)
        {
            float value = logits[candidate * 3 + type];
            if (!Finite(value)) return;
            maximum = XMath.Max(maximum, value);
        }
        float sum = 0;
        for (int candidate = 0; candidate < candidates; candidate++)
        {
            float value = XMath.Exp(logits[candidate * 3 + type] - maximum);
            delta[candidate] = value;
            sum += value;
        }
        if (!Finite(sum) || sum <= 0) return;
        for (int candidate = 0; candidate < candidates; candidate++)
            delta[candidate] = delta[candidate] / sum - (candidate == target ? 1f : 0f);
        loss[0] = XMath.Log(sum) + (maximum - logits[target * 3 + type]);
    }

    public static void MarkerGradientSgd(ArrayView<float> features, ArrayView<float> weights,
        ArrayView<float> delta, ArrayView<float> gradient, ArrayView<float> next,
        int candidates, int hidden, int type, float learningRate)
    {
        int index = Grid.GlobalIndex.X;
        if (index >= hidden * 3) return;
        if (index / hidden != type) { gradient[index] = 0; next[index] = weights[index]; return; }
        float sum = 0;
        for (int candidate = 0; candidate < candidates; candidate++)
            sum += delta[candidate] * features[candidate * hidden + index % hidden];
        gradient[index] = sum;
        next[index] = weights[index] - learningRate * sum;
    }

    // Independent validation has no racing status writes or floating atomics.
    public static void MarkerUpdateStatus(ArrayView<float> gradient, ArrayView<float> next,
        ArrayView<float> loss, ArrayView<float> status, int count)
    {
        if (Grid.GlobalIndex.X != 0) return;
        status[0] = loss[0];
        status[1] = 0;
        if (!Finite(loss[0]) || loss[0] < 0) return;
        for (int index = 0; index < count; index++)
            if (!Finite(gradient[index]) || !Finite(next[index])) return;
        status[1] = 1;
    }

    private static bool Finite(float value) => value >= -float.MaxValue && value <= float.MaxValue;

    public static void Linear(ArrayView<float> a, ArrayView<float> b, ArrayView<float> output, int rows, int shared, int columns, int transposed)
    {
        int i = Grid.GlobalIndex.X;
        if (i >= rows * columns) return;
        int row = i / columns, column = i % columns;
        float sum = 0;
        for (int k = 0; k < shared; k++) sum += a[row * shared + k] * b[transposed != 0 ? column * shared + k : k * columns + column];
        output[i] = sum;
    }

    public static void Embedding(ArrayView<int> ids, ArrayView<float> weights, ArrayView<float> output, int rows, int hidden)
    {
        int i = Grid.GlobalIndex.X;
        if (i < rows * hidden) output[i] = weights[ids[i / hidden] * hidden + i % hidden];
    }

    public static void Norm(ArrayView<float> input, ArrayView<float> gamma, ArrayView<float> beta, ArrayView<float> output, int rows, int hidden, float epsilon, int hasBeta)
    {
        int row = Grid.GlobalIndex.X;
        if (row >= rows) return;
        float mean = 0;
        for (int d = 0; d < hidden; d++) mean += input[row * hidden + d];
        mean /= hidden;
        float variance = 0;
        for (int d = 0; d < hidden; d++) { float delta = input[row * hidden + d] - mean; variance += delta * delta; }
        float inverse = 1f / XMath.Sqrt(variance / hidden + epsilon);
        for (int d = 0; d < hidden; d++) output[row * hidden + d] = (input[row * hidden + d] - mean) * inverse * gamma[d] + (hasBeta != 0 ? beta[d] : 0);
    }

    public static void SplitQkv(ArrayView<float> fused, ArrayView<float> q, ArrayView<float> k, ArrayView<float> v, int rows, int hidden)
    {
        int i = Grid.GlobalIndex.X;
        if (i >= rows * hidden) return;
        int source = i / hidden * hidden * 3 + i % hidden;
        q[i] = fused[source]; k[i] = fused[source + hidden]; v[i] = fused[source + hidden * 2];
    }

    public static void Rope(ArrayView<float> q, ArrayView<float> k, int rows, int heads, int headDimension, float theta)
    {
        int i = Grid.GlobalIndex.X;
        int half = headDimension / 2;
        if (i >= rows * heads * half) return;
        int pair = i % half, head = i / half % heads, position = i / (half * heads);
        float angle = position * XMath.Pow(theta, -(2f * pair / headDimension));
        float cosine = XMath.Cos(angle), sine = XMath.Sin(angle);
        int offset = position * heads * headDimension + head * headDimension + pair;
        float q0 = q[offset], q1 = q[offset + half], k0 = k[offset], k1 = k[offset + half];
        q[offset] = q0 * cosine - q1 * sine; q[offset + half] = q1 * cosine + q0 * sine;
        k[offset] = k0 * cosine - k1 * sine; k[offset + half] = k1 * cosine + k0 * sine;
    }

    public static void AttentionScores(ArrayView<float> q, ArrayView<float> k, ArrayView<float> scores, int rows, int heads, int headDimension, int localWindow)
    {
        int i = Grid.GlobalIndex.X;
        if (i >= heads * rows * rows) return;
        int column = i % rows, row = i / rows % rows, head = i / (rows * rows);
        // Host passes the already-derived half-window; keep the same mask as the scalar oracle.
        if (localWindow > 0 && XMath.Abs(row - column) > localWindow) { scores[i] = -1e30f; return; }
        float sum = 0;
        int qOffset = (row * heads + head) * headDimension, kOffset = (column * heads + head) * headDimension;
        for (int d = 0; d < headDimension; d++) sum += q[qOffset + d] * k[kOffset + d];
        scores[i] = sum / XMath.Sqrt((float)headDimension);
    }

    public static void Softmax(ArrayView<float> values, int rows, int columns)
    {
        int row = Grid.GlobalIndex.X;
        if (row >= rows) return;
        int offset = row * columns;
        float max = -float.MaxValue;
        for (int column = 0; column < columns; column++) max = XMath.Max(max, values[offset + column]);
        float sum = 0;
        for (int column = 0; column < columns; column++) { float value = XMath.Exp(values[offset + column] - max); values[offset + column] = value; sum += value; }
        for (int column = 0; column < columns; column++) values[offset + column] /= sum;
    }

    public static void AttentionContext(ArrayView<float> scores, ArrayView<float> v, ArrayView<float> output, int rows, int heads, int headDimension)
    {
        int i = Grid.GlobalIndex.X;
        if (i >= rows * heads * headDimension) return;
        int d = i % headDimension, head = i / headDimension % heads, row = i / (headDimension * heads);
        float sum = 0;
        for (int column = 0; column < rows; column++) sum += scores[(head * rows + row) * rows + column] * v[(column * heads + head) * headDimension + d];
        output[i] = sum;
    }

    public static void Add(ArrayView<float> input, ArrayView<float> update, int count)
    {
        int i = Grid.GlobalIndex.X;
        if (i < count) input[i] += update[i];
    }

    public static void Activate(ArrayView<float> input, ArrayView<float> output, int count, int relu)
    {
        int i = Grid.GlobalIndex.X;
        if (i < count) output[i] = relu != 0 ? XMath.Max(0, input[i]) : Gelu(input[i]);
    }

    public static void GatedGelu(ArrayView<float> input, ArrayView<float> output, int rows, int intermediate)
    {
        int i = Grid.GlobalIndex.X;
        if (i >= rows * intermediate) return;
        int source = i / intermediate * intermediate * 2 + i % intermediate;
        output[i] = Gelu(input[source]) * input[source + intermediate];
    }

    public static void RowBias(ArrayView<float> input, ArrayView<float> bias, int rows, int hidden)
    {
        int i = Grid.GlobalIndex.X;
        if (i < rows * hidden) input[i] += bias[i % hidden];
    }

    public static void TypeEmbeddingAdd(ArrayView<float> input, ArrayView<float> weights, int rows, int hidden, int typeId)
    {
        int i = Grid.GlobalIndex.X;
        if (i < rows * hidden) input[i] += weights[typeId * hidden + i % hidden];
    }

    // erf approximation (Abramowitz-Stegun 7.1.26), < 1.5e-7 maximum erf error.
    private static float Gelu(float x)
    {
        float z = XMath.Abs(x) * 0.7071067811865475f;
        float t = 1f / (1f + 0.3275911f * z);
        float erf = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * XMath.Exp(-z * z);
        if (x < 0) erf = -erf;
        return 0.5f * x * (1f + erf);
    }
}
