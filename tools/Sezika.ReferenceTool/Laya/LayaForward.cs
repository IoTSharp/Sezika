namespace Sezika.ReferenceTool.Laya;

// Independent port of the frozen DecisionModel; no Sezika encoder/head/tokenizer is referenced.
internal static class LayaForward
{
    internal static (float[] Logits, float[] Probabilities, float[] Actions, LayaPrediction Prediction) Run(LayaWeights weights, LayaSequence sequence, Budget budget)
    {
        const int hiddenSize = 768; var rows = sequence.Ids.Length;
        if (rows is < 1 or > 1024 || sequence.Markers.Length is < 1 or > 64) throw new InvalidDataException("Fixed Laya forward row/marker scope differs.");
        var hidden = ModernBertReference.ForwardFromEmbeddings((name, shape) => weights.Read("encoder." + name[6..], shape),
            weights.Embeddings(sequence.Ids), rows, (name, _) => { budget.Check(); Console.WriteLine("independent-encoder " + name); }, budget, 160000, 160000);
        var type = weights.Read("type_emb.weight", 3, hiddenSize);
        for (var row = 0; row < rows; row++) { budget.Check(); for (var feature = 0; feature < hiddenSize; feature++) hidden[row * hiddenSize + feature] += type[sequence.Question.TypeIndex * hiddenSize + feature]; }
        for (var layer = 0; layer < 2; layer++)
        {
            budget.Check(); var prefix = "head.layers." + layer + ".";
            var normalized = Norm(hidden, rows, weights, prefix + "norm1", budget);
            var qkv = Dense(normalized, rows, hiddenSize, hiddenSize * 3, weights, prefix + "self_attn.in_proj", budget, inProjection: true);
            var attended = ModernBertReference.Attention(qkv, rows, 12, 64, -1, budget);
            var projection = Dense(attended, rows, hiddenSize, hiddenSize, weights, prefix + "self_attn.out_proj", budget); Add(hidden, projection, budget);
            normalized = Norm(hidden, rows, weights, prefix + "norm2", budget);
            var feedForward = Dense(normalized, rows, hiddenSize, 4 * hiddenSize, weights, prefix + "linear1", budget);
            for (var index = 0; index < feedForward.Length; index++) { if ((index & 4095) == 0) budget.Check(); feedForward[index] = Math.Max(0, feedForward[index]); }
            projection = Dense(feedForward, rows, 4 * hiddenSize, hiddenSize, weights, prefix + "linear2", budget); Add(hidden, projection, budget);
        }
        var markerRows = new float[sequence.Markers.Length * hiddenSize];
        for (var index = 0; index < sequence.Markers.Length; index++) { budget.Check(); Array.Copy(hidden, sequence.Markers[index] * hiddenSize, markerRows, index * hiddenSize, hiddenSize); }
        var scorer = Norm(markerRows, sequence.Markers.Length, weights, "scorer.0", budget);
        scorer = Dense(scorer, sequence.Markers.Length, hiddenSize, hiddenSize, weights, "scorer.1", budget); Gelu(scorer, budget);
        var logits = Dense(scorer, sequence.Markers.Length, hiddenSize, 1, weights, "scorer.3", budget);
        var probabilities = ModernBertReference.Softmax(logits); var ordered = probabilities.OrderDescending().ToArray(); var top1 = ordered[0]; var top2 = ordered.Length > 1 ? ordered[1] : 0f;
        var count = Math.Max(2, probabilities.Length); float entropy = 0;
        foreach (var probability in probabilities) entropy -= probability * MathF.Log(Math.Max(probability, 1e-9f)); entropy /= MathF.Log(count);
        var actionInput = new float[hiddenSize + 4]; Array.Copy(hidden, actionInput, hiddenSize); actionInput[hiddenSize] = top1;
        actionInput[hiddenSize + 1] = top1 - top2; actionInput[hiddenSize + 2] = entropy; actionInput[hiddenSize + 3] = count / 255f;
        var actions = Dense(actionInput, 1, hiddenSize + 4, 256, weights, "act_head.0", budget); Gelu(actions, budget);
        actions = ModernBertReference.Softmax(Dense(actions, 1, 256, 2, weights, "act_head.2", budget));
        var best = 0; for (var index = 1; index < probabilities.Length; index++) if (probabilities[index] > probabilities[best]) best = index;
        LayaPrediction prediction;
        if (sequence.Question.Type == "choice") prediction = new(sequence.Question.Labels[best], null, null, null);
        else if (sequence.Question.Type == "score") { double expected = 0; for (var index = 0; index < probabilities.Length; index++) expected += index * (double)probabilities[index]; prediction = new(null, expected, null, null); }
        else { if (probabilities.Length != 2) throw new InvalidDataException("Noul semantic false/true order differs."); prediction = new(null, null, probabilities[1] >= .5, probabilities[1]); }
        return (logits, probabilities, actions, prediction);
    }
    private static float[] Norm(float[] input, int rows, LayaWeights weights, string name, Budget budget)
    {
        var result = ModernBertReference.Normalize(input, rows, 768, weights.Read(name + ".weight", 768), 1e-5f, budget); var bias = weights.Read(name + ".bias", 768);
        for (var row = 0; row < rows; row++) { budget.Check(); for (var column = 0; column < 768; column++) result[row * 768 + column] += bias[column]; } return result;
    }
    private static float[] Dense(float[] input, int rows, int columns, int output, LayaWeights weights, string name, Budget budget, bool inProjection = false)
    {
        var result = ModernBertReference.Linear(input, rows, columns, weights.Read(name + (inProjection ? "_weight" : ".weight"), output, columns), output, budget);
        var bias = weights.Read(name + (inProjection ? "_bias" : ".bias"), output);
        for (var row = 0; row < rows; row++) { budget.Check(); for (var column = 0; column < output; column++) result[row * output + column] += bias[column]; }
        if (result.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Non-finite independently computed dense output."); return result;
    }
    private static void Add(float[] target, float[] addition, Budget budget) { if (target.Length != addition.Length) throw new InvalidDataException("Residual shapes differ."); for (var i = 0; i < target.Length; i++) { if ((i & 4095) == 0) budget.Check(); target[i] += addition[i]; if (!float.IsFinite(target[i])) throw new InvalidDataException("Non-finite residual."); } }
    private static void Gelu(float[] values, Budget budget) { for (var i = 0; i < values.Length; i++) { if ((i & 4095) == 0) budget.Check(); values[i] = ModernBertReference.Gelu(values[i]); } }
}
