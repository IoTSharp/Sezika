using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Sezika;

public enum MarkerQuestionType { Choice = 0, Score = 1, Boolean = 2 }

/// <summary>Identity asserted by the producer of frozen, full-sequence encoder features.</summary>
public sealed record MarkerFeatureIdentity(
    string ModelId, string EncoderSha256, string TokenizerSha256, string DataManifestSha256,
    int HiddenSize, string Protocol = "decision-v1", string LengthPolicy = "strict");

/// <summary>One complete question sequence; Features[i] is the frozen final hidden row at MarkerPositions[i].</summary>
public sealed record MarkerFeatureExample(
    string RecordId, MarkerQuestionType Type, string Language, int[] TokenIds,
    int[] MarkerPositions, float[][] Features, int TargetIndex);

public sealed record MarkerFeatureSet(MarkerFeatureIdentity Identity, IReadOnlyList<MarkerFeatureExample> Examples);

public sealed record MarkerTrainingOptions(int Seed, int MaxSteps, float LearningRate, TimeSpan MaxDuration);

public sealed record MarkerTrainingProgress(int CompletedSteps, int MaxSteps, double LastLoss);

public sealed record MarkerGradient(double Loss, double[] Logits, double[] Probabilities, double[] WeightGradient);

/// <summary>Only the three linear head rows are trainable; no encoder state is held here.</summary>
public sealed class LinearMarkerHead
{
    private readonly float[] _weights;
    public int HiddenSize { get; }

    public LinearMarkerHead(int hiddenSize, float[] weights)
    {
        if (hiddenSize is < 1 or > 2048 || weights is null || weights.Length != checked(3 * hiddenSize) ||
            Array.Exists(weights, value => !float.IsFinite(value)))
            throw new DecisionException("training_shape_invalid", "Linear marker head must contain three finite rows of the declared width.");
        HiddenSize = hiddenSize;
        _weights = (float[])weights.Clone();
    }

    public float[] CopyWeights() => (float[])_weights.Clone();

    public double[] Score(MarkerQuestionType type, ReadOnlySpan<float[]> markerFeatures, Action? check = null)
    {
        if ((uint)type > 2 || markerFeatures.Length is < 2 or > 32 ||
            (type == MarkerQuestionType.Boolean && markerFeatures.Length != 2) ||
            (type == MarkerQuestionType.Score && markerFeatures.Length > 10))
            throw new DecisionException("training_shape_invalid", "Marker type or candidate count is invalid.");
        var logits = new double[markerFeatures.Length];
        for (var candidate = 0; candidate < logits.Length; candidate++)
        {
            var features = markerFeatures[candidate];
            if (features is null || features.Length != HiddenSize)
                throw new DecisionException("training_shape_invalid", "Marker feature width is invalid.");
            double sum = 0;
            for (var index = 0; index < HiddenSize; index++)
            {
                if ((index & 255) == 0) check?.Invoke();
                if (!float.IsFinite(features[index])) throw new DecisionException("training_value_invalid", "Marker feature is non-finite.");
                sum += (double)_weights[(int)type * HiddenSize + index] * features[index];
            }
            if (!double.IsFinite(sum)) throw new DecisionException("training_value_invalid", "Marker logit is non-finite.");
            logits[candidate] = sum;
        }
        return logits;
    }

    public MarkerGradient Gradient(MarkerFeatureExample example, Action? check = null)
    {
        ArgumentNullException.ThrowIfNull(example);
        var logits = Score(example.Type, example.Features, check);
        if ((uint)example.TargetIndex >= (uint)logits.Length)
            throw new DecisionException("training_label_invalid", "Hard label is outside the candidate range.");
        var max = logits.Max();
        var probabilities = new double[logits.Length];
        double sum = 0;
        for (var index = 0; index < logits.Length; index++) sum += probabilities[index] = Math.Exp(logits[index] - max);
        var gradient = new double[_weights.Length];
        for (var candidate = 0; candidate < logits.Length; candidate++)
        {
            check?.Invoke();
            probabilities[candidate] /= sum;
            var error = probabilities[candidate] - (candidate == example.TargetIndex ? 1d : 0d);
            for (var index = 0; index < HiddenSize; index++)
            {
                if ((index & 255) == 0) check?.Invoke();
                gradient[(int)example.Type * HiddenSize + index] += error * example.Features[candidate][index];
            }
        }
        var loss = max + Math.Log(sum) - logits[example.TargetIndex];
        if (!double.IsFinite(loss) || Array.Exists(gradient, value => !double.IsFinite(value)))
            throw new DecisionException("training_value_invalid", "Loss or gradient is non-finite.");
        return new MarkerGradient(loss, logits, probabilities, gradient);
    }
}

public sealed class MarkerTrainingState
{
    internal readonly float[] Weights;
    public MarkerFeatureIdentity Identity { get; }
    public string FeatureSha256 { get; }
    public int Seed { get; }
    public float LearningRate { get; }
    public int CompletedSteps { get; }
    public double LastLoss { get; }
    public LinearMarkerHead Head => new(Identity.HiddenSize, Weights);

    internal MarkerTrainingState(MarkerFeatureIdentity identity, string featureSha256, int seed,
        float learningRate, int completedSteps, double lastLoss, float[] weights)
    {
        Identity = identity;
        FeatureSha256 = featureSha256;
        Seed = seed;
        LearningRate = learningRate;
        CompletedSteps = completedSteps;
        LastLoss = lastLoss;
        Weights = (float[])weights.Clone();
    }
}

/// <summary>Bounded SGD over precomputed complete-sequence marker rows; the encoder remains outside this trainer.</summary>
public static class IndependentMarkerHeadTrainer
{
    internal const int MaxExamples = 32;
    private const int MaxTokens = 1024;

    public static MarkerTrainingState Train(MarkerFeatureSet set, MarkerTrainingOptions options,
        MarkerTrainingState? resume = null, Action<MarkerTrainingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxSteps is < 1 or > 10_000 || !float.IsFinite(options.LearningRate) ||
            options.LearningRate <= 0 || options.LearningRate > 1 ||
            options.MaxDuration <= TimeSpan.Zero || options.MaxDuration > TimeSpan.FromMinutes(10))
            throw new DecisionException("training_budget_invalid", "Step, learning rate or wall-clock budget is invalid.");
        var clock = Stopwatch.StartNew();
        void CheckBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed > options.MaxDuration) throw new DecisionException("training_deadline_exceeded", "Training wall-clock budget expired.");
        }
        var prepared = Prepare(set, CheckBudget);
        // Hashes are serialized as hexadecimal identities.  Their spelling is
        // case-insensitive, so a checkpoint loaded from a manifest that uses
        // lower-case hex must still resume against the same feature bytes.
        if (resume is not null && (!IdentityMatches(resume.Identity, prepared.Identity) ||
            !resume.FeatureSha256.Equals(prepared.Sha256, StringComparison.OrdinalIgnoreCase) ||
            resume.Seed != options.Seed || resume.LearningRate != options.LearningRate ||
            resume.CompletedSteps < 0 || resume.CompletedSteps > options.MaxSteps ||
            resume.Weights.Length != checked(3 * prepared.Identity.HiddenSize) ||
            Array.Exists(resume.Weights, value => !float.IsFinite(value))))
            throw new DecisionException("training_resume_mismatch", "Checkpoint does not match the features or training configuration.");

        var weights = resume?.Head.CopyWeights() ?? new float[checked(3 * prepared.Identity.HiddenSize)];
        var lastLoss = resume?.LastLoss ?? 0d;
        var completed = resume?.CompletedSteps ?? 0;
        var order = MakeOrder(prepared.Examples.Length, options.Seed);
        progress?.Invoke(new MarkerTrainingProgress(completed, options.MaxSteps, lastLoss));
        for (var step = completed; step < options.MaxSteps; step++)
        {
            CheckBudget();
            var example = prepared.Examples[order[step % order.Length]];
            var gradient = new LinearMarkerHead(prepared.Identity.HiddenSize, weights).Gradient(example, CheckBudget);
            var next = (float[])weights.Clone();
            for (var index = 0; index < next.Length; index++)
            {
                if ((index & 255) == 0) CheckBudget();
                var value = (double)next[index] - options.LearningRate * gradient.WeightGradient[index];
                if (!double.IsFinite(value) || value > float.MaxValue || value < -float.MaxValue)
                    throw new DecisionException("training_value_invalid", "SGD update is non-finite.");
                next[index] = (float)value;
            }
            weights = next;
            lastLoss = gradient.Loss;
            completed = step + 1;
            progress?.Invoke(new MarkerTrainingProgress(completed, options.MaxSteps, lastLoss));
        }
        CheckBudget();
        return new MarkerTrainingState(prepared.Identity, prepared.Sha256, options.Seed,
            options.LearningRate, completed, lastLoss, weights);
    }

    internal static PreparedFeatures Prepare(MarkerFeatureSet set, Action check)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(check);
        var identity = set.Identity;
        ValidateIdentity(identity, "training_identity_invalid");
        if (set.Examples is null || set.Examples.Count is < 1 or > MaxExamples)
            throw new DecisionException("training_input_invalid", "Training requires 1-32 complete questions.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var examples = new MarkerFeatureExample[set.Examples.Count];
        for (var item = 0; item < examples.Length; item++)
        {
            check();
            var example = set.Examples[item];
            if (example is null || string.IsNullOrWhiteSpace(example.RecordId) || example.RecordId.Length > 128 ||
                !seen.Add(example.RecordId) || example.Language is not ("zh" or "en") ||
                (uint)example.Type > 2 || example.TokenIds is null || example.TokenIds.Length is < 2 or > MaxTokens ||
                example.MarkerPositions is null || example.Features is null ||
                example.MarkerPositions.Length != example.Features.Length)
                throw new DecisionException("training_input_invalid", "Question record, language or complete sequence is invalid.");
            var count = example.MarkerPositions.Length;
            if ((example.Type == MarkerQuestionType.Boolean && count != 2) ||
                (example.Type == MarkerQuestionType.Score && count is < 2 or > 10) ||
                (example.Type == MarkerQuestionType.Choice && count is < 2 or > 32) ||
                (uint)example.TargetIndex >= (uint)count)
                throw new DecisionException("training_label_invalid", "Candidate count or hard label is invalid.");
            var tokens = (int[])example.TokenIds.Clone();
            if (Array.Exists(tokens, token => token < 0)) throw new DecisionException("training_input_invalid", "Token ID is negative.");
            var markers = (int[])example.MarkerPositions.Clone();
            var features = new float[count][];
            for (var candidate = 0; candidate < count; candidate++)
            {
                check();
                var candidateFeatures = example.Features[candidate];
                if ((uint)markers[candidate] >= (uint)tokens.Length ||
                    (candidate > 0 && markers[candidate] <= markers[candidate - 1]) ||
                    candidateFeatures is null || candidateFeatures.Length != identity.HiddenSize)
                    throw new DecisionException("training_shape_invalid", "Marker position or hidden width is invalid.");
                features[candidate] = (float[])candidateFeatures.Clone();
                for (var index = 0; index < features[candidate].Length; index++)
                {
                    if ((index & 255) == 0) check();
                    if (!float.IsFinite(features[candidate][index]))
                        throw new DecisionException("training_value_invalid", "Marker feature is non-finite.");
                }
            }
            examples[item] = new MarkerFeatureExample(example.RecordId, example.Type, example.Language,
                tokens, markers, features, example.TargetIndex);
        }
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteIdentity(writer, identity);
            writer.Write(examples.Length);
            foreach (var example in examples)
            {
                check();
                WriteText(writer, example.RecordId);
                writer.Write((int)example.Type);
                WriteText(writer, example.Language);
                writer.Write(example.TargetIndex);
                writer.Write(example.TokenIds.Length);
                foreach (var token in example.TokenIds) writer.Write(token);
                writer.Write(example.MarkerPositions.Length);
                foreach (var marker in example.MarkerPositions) writer.Write(marker);
                foreach (var feature in example.Features)
                    for (var index = 0; index < feature.Length; index++)
                    {
                        if ((index & 255) == 0) check();
                        writer.Write(feature[index]);
                    }
            }
        }
        check();
        return new PreparedFeatures(identity, examples, Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length))));
    }

    private static int[] MakeOrder(int count, int seed)
    {
        var order = Enumerable.Range(0, count).ToArray();
        uint state = unchecked((uint)seed) ^ 0x9E3779B9u;
        if (state == 0) state = 0xA341316Cu;
        for (var index = count - 1; index > 0; index--)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var other = (int)(state % (uint)(index + 1));
            (order[index], order[other]) = (order[other], order[index]);
        }
        return order;
    }

    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    internal static bool IdentityMatches(MarkerFeatureIdentity? left, MarkerFeatureIdentity? right) =>
        left is not null && right is not null && left.HiddenSize == right.HiddenSize &&
        string.Equals(left.ModelId, right.ModelId, StringComparison.Ordinal) &&
        string.Equals(left.Protocol, right.Protocol, StringComparison.Ordinal) &&
        string.Equals(left.LengthPolicy, right.LengthPolicy, StringComparison.Ordinal) &&
        string.Equals(left.EncoderSha256, right.EncoderSha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.TokenizerSha256, right.TokenizerSha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.DataManifestSha256, right.DataManifestSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>Validate the independent model contract before serializing an asset.</summary>
    internal static void ValidateIdentity(MarkerFeatureIdentity? identity, string errorCode)
    {
        if (identity is null || identity.HiddenSize is < 1 or > 2048 ||
            string.IsNullOrWhiteSpace(identity.ModelId) || identity.ModelId.Length > 128 ||
            !identity.ModelId.StartsWith("sezika/", StringComparison.Ordinal) ||
            identity.Protocol != "decision-v1" || identity.LengthPolicy != "strict" ||
            !IsHash(identity.EncoderSha256) || !IsHash(identity.TokenizerSha256) ||
            !IsHash(identity.DataManifestSha256))
            throw new DecisionException(errorCode, "Independent model, protocol or source identity is invalid.");
    }

    internal static void WriteText(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 256) throw new DecisionException("training_identity_invalid", "Checkpoint text field is too long.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    internal static string ReadText(BinaryReader reader)
    {
        var size = reader.ReadInt32();
        if (size is < 0 or > 256) throw new DecisionException("training_checkpoint_invalid", "Checkpoint text length is invalid.");
        var bytes = reader.ReadBytes(size);
        if (bytes.Length != size) throw new EndOfStreamException();
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    internal static void WriteIdentity(BinaryWriter writer, MarkerFeatureIdentity identity)
    {
        WriteText(writer, identity.ModelId);
        WriteText(writer, identity.EncoderSha256);
        WriteText(writer, identity.TokenizerSha256);
        WriteText(writer, identity.DataManifestSha256);
        writer.Write(identity.HiddenSize);
        WriteText(writer, identity.Protocol);
        WriteText(writer, identity.LengthPolicy);
    }

    internal sealed record PreparedFeatures(MarkerFeatureIdentity Identity, MarkerFeatureExample[] Examples, string Sha256);
}

/// <summary>Versioned, hash-bound training state only; it is not an inference model package.</summary>
public static class MarkerTrainingCheckpoint
{
    private static readonly byte[] Magic = "SZMK1"u8.ToArray();
    private const int MaxBytes = 128 * 1024;
    private static readonly TimeSpan MaxIoDuration = TimeSpan.FromSeconds(30);

    public static string SaveNew(string path, MarkerTrainingState state, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        void CheckIoBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed > MaxIoDuration) throw new DecisionException("training_checkpoint_deadline_exceeded", "Checkpoint save wall-clock budget expired.");
        }
        IndependentMarkerHeadTrainer.ValidateIdentity(state.Identity, "training_checkpoint_invalid");
        if (!IndependentMarkerHeadTrainer.IsHash(state.FeatureSha256) || state.CompletedSteps is < 0 or > 10_000 ||
            !float.IsFinite(state.LearningRate) || state.LearningRate <= 0 || state.LearningRate > 1 ||
            !double.IsFinite(state.LastLoss) || state.LastLoss < 0)
            throw new DecisionException("training_checkpoint_invalid", "Training state is invalid.");
        _ = new LinearMarkerHead(state.Identity.HiddenSize, state.Weights);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(1);
            IndependentMarkerHeadTrainer.WriteIdentity(writer, state.Identity);
            IndependentMarkerHeadTrainer.WriteText(writer, state.FeatureSha256);
            writer.Write(state.Seed);
            writer.Write(state.LearningRate);
            writer.Write(state.CompletedSteps);
            writer.Write(state.LastLoss);
            writer.Write(state.Weights.Length);
            for (var index = 0; index < state.Weights.Length; index++)
            {
                if ((index & 255) == 0) CheckIoBudget();
                writer.Write(state.Weights[index]);
            }
        }
        if (stream.Length + 32 > MaxBytes) throw new DecisionException("training_checkpoint_invalid", "Checkpoint exceeds size limit.");
        var payload = stream.ToArray();
        var checksum = SHA256.HashData(payload);
        var bytes = new byte[payload.Length + checksum.Length];
        payload.CopyTo(bytes, 0);
        checksum.CopyTo(bytes, payload.Length);
        var fileHash = Convert.ToHexString(SHA256.HashData(bytes));
        // Write beside the destination and publish with a single rename.  A
        // cancelled or interrupted write therefore cannot leave a partial
        // checkpoint at the requested path (and retries remain possible).
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination)!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            CheckIoBudget();
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return fileHash;
    }

    public static MarkerTrainingState Load(string path, MarkerFeatureSet set, string expectedFileSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IndependentMarkerHeadTrainer.IsHash(expectedFileSha256))
            throw new DecisionException("training_checkpoint_hash_invalid", "Expected checkpoint SHA-256 is required.");
        cancellationToken.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        void CheckIoBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed > MaxIoDuration) throw new DecisionException("training_checkpoint_deadline_exceeded", "Checkpoint load wall-clock budget expired.");
        }
        var prepared = IndependentMarkerHeadTrainer.Prepare(set, CheckIoBudget);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is < 64 or > MaxBytes) throw new DecisionException("training_checkpoint_invalid", "Checkpoint size is invalid.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        CheckIoBudget();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedFileSha256, StringComparison.OrdinalIgnoreCase) ||
            !SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length - 32)))
            throw new DecisionException("training_checkpoint_hash_invalid", "Checkpoint SHA-256 does not match.");
        try
        {
            using var stream = new MemoryStream(bytes, 0, bytes.Length - 32, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic) || reader.ReadInt32() != 1)
                throw new DecisionException("training_checkpoint_invalid", "Checkpoint magic or version is invalid.");
            var identity = new MarkerFeatureIdentity(
                IndependentMarkerHeadTrainer.ReadText(reader), IndependentMarkerHeadTrainer.ReadText(reader),
                IndependentMarkerHeadTrainer.ReadText(reader), IndependentMarkerHeadTrainer.ReadText(reader),
                reader.ReadInt32(), IndependentMarkerHeadTrainer.ReadText(reader), IndependentMarkerHeadTrainer.ReadText(reader));
            var featureHash = IndependentMarkerHeadTrainer.ReadText(reader);
            if (!IndependentMarkerHeadTrainer.IdentityMatches(identity, prepared.Identity) ||
                !featureHash.Equals(prepared.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new DecisionException("training_resume_mismatch", "Checkpoint model, protocol or data identity differs.");
            var seed = reader.ReadInt32();
            var rate = reader.ReadSingle();
            var steps = reader.ReadInt32();
            var loss = reader.ReadDouble();
            var count = reader.ReadInt32();
            if (!float.IsFinite(rate) || rate <= 0 || rate > 1 || steps is < 0 or > 10_000 ||
                !double.IsFinite(loss) || loss < 0 || count != checked(3 * identity.HiddenSize))
                throw new DecisionException("training_checkpoint_invalid", "Checkpoint optimizer state or shape is invalid.");
            var weights = new float[count];
            for (var index = 0; index < count; index++)
            {
                if ((index & 255) == 0) CheckIoBudget();
                weights[index] = reader.ReadSingle();
            }
            if (stream.Position != stream.Length || Array.Exists(weights, value => !float.IsFinite(value)))
                throw new DecisionException("training_checkpoint_invalid", "Checkpoint tensor or trailing data is invalid.");
            return new MarkerTrainingState(identity, featureHash, seed, rate, steps, loss, weights);
        }
        catch (Exception exception) when (exception is EndOfStreamException or DecoderFallbackException or OverflowException)
        {
            throw new DecisionException("training_checkpoint_invalid", "Checkpoint is malformed.");
        }
    }
}
