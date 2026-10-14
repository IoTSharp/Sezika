using System.Numerics;

namespace Sezika.ReferenceTool;

// Independent C# mathematical port of Hugging Face Transformers v4.57.1
// ModernBERT eager inference (Apache-2.0). No Sezika core is referenced.
internal static class ModernBertReference
{
    internal const int Hidden = 768;
    internal const int Intermediate = 1152;
    internal static float[] Forward(OriginalWeights weights, int[] tokens, Action<string, float[]> checkpoint, Budget budget)
    {
        if (tokens.Length != 46 || tokens.Any(token => token is < 0 or >= 256000)) throw new InvalidDataException("Only fixed46 original-token rows are admitted.");
        var result = ForwardFromEmbeddings((name, shape) => weights.Read(name, shape), weights.Embeddings(tokens), tokens.Length, checkpoint, budget);
        weights.Complete(); return result;
    }
    internal static float[] ForwardFromEmbeddings(Func<string, int[], float[]> read, float[] embeddingRows, int tokenCount,
        Action<string, float[]> checkpoint, Budget budget, float globalRopeTheta = 160000f, float localRopeTheta = 160000f)
    {
        if (tokenCount is < 1 or > 1024 || embeddingRows.Length != tokenCount * Hidden || !float.IsFinite(globalRopeTheta) || !float.IsFinite(localRopeTheta) || globalRopeTheta is < 1 or > 1000000 || localRopeTheta is < 1 or > 1000000) throw new InvalidDataException("Reference embedding/token/theta bounds differ.");
        Finite(embeddingRows); var hidden = embeddingRows; checkpoint("embedding/raw", hidden);
        hidden = Normalize(hidden, tokenCount, Hidden, read("model.embeddings.norm.weight", [Hidden]), 1e-5f, budget);
        checkpoint("embedding/norm", hidden);
        for (var layer = 0; layer < 22; layer++)
        {
            budget.Check(); var prefix = "model.layers." + layer + ".";
            var normalized = layer == 0 ? hidden : Normalize(hidden, tokenCount, Hidden, read(prefix + "attn_norm.weight", [Hidden]), 1e-5f, budget);
            var qkv = Linear(normalized, tokenCount, Hidden, read(prefix + "attn.Wqkv.weight", [3 * Hidden, Hidden]), 3 * Hidden, budget);
            Rotary(qkv, tokenCount, 12, 64, layer % 3 == 0 ? globalRopeTheta : localRopeTheta, budget);
            var attended = Attention(qkv, tokenCount, 12, 64, layer % 3 == 0 ? -1 : 64, budget);
            var projected = Linear(attended, tokenCount, Hidden, read(prefix + "attn.Wo.weight", [Hidden, Hidden]), Hidden, budget);
            Add(hidden, projected, budget);
            normalized = Normalize(hidden, tokenCount, Hidden, read(prefix + "mlp_norm.weight", [Hidden]), 1e-5f, budget);
            var gated = Linear(normalized, tokenCount, Hidden, read(prefix + "mlp.Wi.weight", [2 * Intermediate, Hidden]), 2 * Intermediate, budget);
            var activated = new float[tokenCount * Intermediate];
            for (var token = 0; token < tokenCount; token++)
            {
                budget.Check();
                for (var feature = 0; feature < Intermediate; feature++) activated[token * Intermediate + feature] = Gelu(gated[token * 2 * Intermediate + feature]) * gated[token * 2 * Intermediate + Intermediate + feature];
            }
            projected = Linear(activated, tokenCount, Intermediate, read(prefix + "mlp.Wo.weight", [Hidden, Intermediate]), Hidden, budget);
            Add(hidden, projected, budget); checkpoint("layer/" + layer + "/hidden", hidden);
        }
        hidden = Normalize(hidden, tokenCount, Hidden, read("model.final_norm.weight", [Hidden]), 1e-5f, budget);
        checkpoint("encoder/final", hidden); return hidden;
    }
    internal static float[] Linear(float[] input, int rows, int columns, float[] weight, int outputColumns, Budget budget)
    {
        Shape(rows, columns, input.Length); if (outputColumns is < 1 or > 3072 || weight.Length != checked(columns * outputColumns)) throw new InvalidDataException("Reference linear shape differs.");
        var output = new float[checked(rows * outputColumns)];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < outputColumns; column++)
            {
                if ((column & 63) == 0) budget.Check();
                output[row * outputColumns + column] = Dot(input.AsSpan(row * columns, columns), weight.AsSpan(column * columns, columns));
            }
        }
        Finite(output); return output;
    }
    internal static float[] Normalize(float[] input, int rows, int columns, float[] scale, float epsilon, Budget budget)
    {
        Shape(rows, columns, input.Length); if (scale.Length != columns || !float.IsFinite(epsilon) || epsilon < 0) throw new InvalidDataException("Reference norm shape/epsilon differs.");
        var output = new float[input.Length];
        for (var row = 0; row < rows; row++)
        {
            budget.Check(); double sum = 0;
            for (var feature = 0; feature < columns; feature++) sum += input[row * columns + feature];
            var mean = sum / columns; double variance = 0;
            for (var feature = 0; feature < columns; feature++) { var difference = input[row * columns + feature] - mean; variance += difference * difference; }
            var inverse = 1 / Math.Sqrt(variance / columns + epsilon);
            for (var feature = 0; feature < columns; feature++) output[row * columns + feature] = (float)((input[row * columns + feature] - mean) * inverse) * scale[feature];
        }
        Finite(output); return output;
    }
    internal static void Rotary(float[] qkv, int tokens, int heads, int dimension, float theta, Budget budget)
    {
        if (tokens is < 1 or > 1024 || heads is < 1 or > 12 || dimension is < 2 or > 64 || (dimension & 1) != 0 || qkv.Length != tokens * 3 * heads * dimension || theta <= 0) throw new InvalidDataException("Reference rotary shape differs.");
        var half = dimension / 2; var width = heads * dimension;
        for (var token = 0; token < tokens; token++)
        {
            budget.Check();
            for (var feature = 0; feature < half; feature++)
            {
                var inverseFrequency = 1f / MathF.Pow(theta, 2f * feature / dimension); var angle = token * inverseFrequency;
                var cosine = MathF.Cos(angle); var sine = MathF.Sin(angle);
                for (var head = 0; head < heads; head++)
                {
                    RotatePair(qkv, token * 3 * width + head * dimension + feature, half, cosine, sine);
                    RotatePair(qkv, token * 3 * width + width + head * dimension + feature, half, cosine, sine);
                }
            }
        }
        Finite(qkv);
    }
    internal static void RotatePair(float[] values, int index, int half, float cosine, float sine)
    {
        var first = values[index]; var second = values[index + half]; values[index] = first * cosine - second * sine; values[index + half] = second * cosine + first * sine;
    }
    internal static float[] Attention(float[] qkv, int tokens, int heads, int dimension, int halfWindow, Budget budget)
    {
        var width = checked(heads * dimension);
        if (tokens is < 1 or > 1024 || heads is < 1 or > 12 || dimension is < 1 or > 64 || halfWindow is < -1 or > 64 || qkv.Length != tokens * 3 * width) throw new InvalidDataException("Reference attention shape differs.");
        var output = new float[tokens * width]; var scores = new float[tokens]; var scale = 1f / MathF.Sqrt(dimension);
        for (var query = 0; query < tokens; query++)
        {
            budget.Check();
            for (var head = 0; head < heads; head++)
            {
                for (var key = 0; key < tokens; key++) scores[key] = halfWindow >= 0 && Math.Abs(query - key) > halfWindow ? float.MinValue :
                    Dot(qkv.AsSpan(query * 3 * width + head * dimension, dimension), qkv.AsSpan(key * 3 * width + width + head * dimension, dimension)) * scale;
                var probabilities = Softmax(scores);
                for (var feature = 0; feature < dimension; feature++)
                {
                    float value = 0; for (var key = 0; key < tokens; key++) value += probabilities[key] * qkv[key * 3 * width + 2 * width + head * dimension + feature];
                    output[query * width + head * dimension + feature] = value;
                }
            }
        }
        Finite(output); return output;
    }
    internal static float[] Softmax(float[] values)
    {
        if (values.Length is < 1 or > 1024) throw new InvalidDataException("Reference softmax count bound1024.");
        Finite(values); var maximum = values.Max(); var output = new float[values.Length]; float sum = 0;
        for (var index = 0; index < values.Length; index++) { output[index] = MathF.Exp(values[index] - maximum); sum += output[index]; }
        if (!float.IsFinite(sum) || sum <= 0) throw new InvalidDataException("Invalid reference softmax sum.");
        for (var index = 0; index < values.Length; index++) output[index] /= sum; Finite(output); return output;
    }
    internal static float Gelu(float value)
    {
        // erf-form GELU; independent A&S7.1.26 erf approximation (max error1.5e-7).
        var x = Math.Abs((double)value / Math.Sqrt(2)); var t = 1 / (1 + .3275911 * x);
        var polynomial = (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - .284496736) * t + .254829592) * t;
        var erf = 1 - polynomial * Math.Exp(-x * x); if (value < 0) erf = -erf;
        return (float)(.5 * value * (1 + erf));
    }
    internal static void Finite(float[] values) { for (var i = 0; i < values.Length; i++) if (!float.IsFinite(values[i])) throw new InvalidDataException("Non-finite reference tensor."); }
    private static void Add(float[] destination, float[] values, Budget budget) { if (destination.Length != values.Length) throw new InvalidDataException("Residual shape differs."); for (var index = 0; index < destination.Length; index++) { if ((index & 4095) == 0) budget.Check(); destination[index] += values[index]; } Finite(destination); }
    private static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var width = Vector<float>.Count; var accumulated = Vector<float>.Zero; var index = 0;
        for (; index <= left.Length - width; index += width) accumulated += new Vector<float>(left.Slice(index, width)) * new Vector<float>(right.Slice(index, width));
        float result = 0; for (var lane = 0; lane < width; lane++) result += accumulated[lane]; for (; index < left.Length; index++) result += left[index] * right[index]; return result;
    }
    private static void Shape(int rows, int columns, int length) { if (rows is < 1 or > 1024 || columns is < 1 or > 3072 || length != checked(rows * columns)) throw new InvalidDataException("Reference matrix shape differs."); }
}
