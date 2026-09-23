namespace Sezika;

public sealed record HeadTrainingExample(float[] Features, bool Label, string Language = "und", string Domain = "default", string Split = "train");

public sealed record TrainedDecisionHead(float[] Weights, float Bias, int Epochs, double FinalLogLoss);

/// <summary>
/// Deterministic frozen-encoder binary head trainer. It never updates encoder
/// weights and is intended for reproducible local calibration experiments.
/// </summary>
public static class DecisionHeadTrainer
{
    public static TrainedDecisionHead Train(
        IReadOnlyList<HeadTrainingExample> examples,
        int epochs = 100,
        float learningRate = 0.05f,
        CancellationToken cancellationToken = default)
    {
        if (examples is null || examples.Count < 2 || epochs is < 1 or > 10_000 || !float.IsFinite(learningRate) || learningRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(examples));
        var dimension = examples[0].Features.Length;
        if (dimension == 0 || examples.Any(example => example.Features.Length != dimension))
            throw new DecisionException("training_input_invalid", "Head feature dimensions must be non-zero and consistent.");
        if (examples.All(example => example.Label) || examples.All(example => !example.Label))
            throw new DecisionException("training_input_invalid", "Head training requires both labels.");
        var weights = new float[dimension];
        var bias = 0f;
        var loss = double.PositiveInfinity;
        for (var epoch = 0; epoch < epochs; epoch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gradients = new float[dimension];
            var biasGradient = 0f;
            loss = 0d;
            foreach (var example in examples)
            {
                var logit = bias;
                for (var i = 0; i < dimension; i++) logit += weights[i] * example.Features[i];
                var probability = 1d / (1d + Math.Exp(-Math.Clamp(logit, -40f, 40f)));
                var label = example.Label ? 1d : 0d;
                loss -= label * Math.Log(Math.Max(probability, 1e-15)) + (1d - label) * Math.Log(Math.Max(1d - probability, 1e-15));
                var error = (float)(probability - label);
                for (var i = 0; i < dimension; i++) gradients[i] += error * example.Features[i];
                biasGradient += error;
            }
            var scale = learningRate / examples.Count;
            for (var i = 0; i < dimension; i++) weights[i] -= scale * gradients[i];
            bias -= scale * biasGradient;
        }
        return new TrainedDecisionHead(weights, bias, epochs, loss / examples.Count);
    }
}
