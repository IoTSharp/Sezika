using System.Diagnostics;
using Sezika;
using Sezika.Cuda;

namespace Sezika.IndependentModelTool;

public sealed record CudaExportedTrainingFeatures(MarkerFeatureSet Train, IReadOnlyList<MarkerFeatureExample> Development);

/// <summary>Frozen full-sequence CUDA forward; the host only copies explicit marker rows.</summary>
public static class IndependentCudaFeatureExporter
{
    public static CudaExportedTrainingFeatures ExportApproved(CudaDevice device, IndependentEncoderModelPackage model,
        ReviewedOriginalTrainingInput admitted, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device); ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(admitted);
        if (model.ModelId != IndependentModelLoader.ModelId ||
            !IndependentTrainingWorkflow.HashEqual(model.EncoderSha256, IndependentModelLoader.ConvertedEncoderSha256) ||
            !IndependentTrainingWorkflow.HashEqual(model.TokenizerSha256, IndependentModelLoader.TokenizerSha256) || admitted.Records.Count is < 2 or > 32)
            throw new DecisionException("training_admission_invalid", "Approved export requires the fixed independent model and bounded admitted snapshot.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var token = deadline.Token; var clock = Stopwatch.StartNew();
        var identity = new MarkerFeatureIdentity(model.ModelId, model.EncoderSha256, model.TokenizerSha256, admitted.ManifestSha256, model.Encoder.Config.HiddenSize);
        var train = new List<MarkerFeatureExample>(31); var development = new List<MarkerFeatureExample>(31);
        using (var encoder = new CudaModernBertEncoder(device, model.Encoder.Config, model.Encoder.Weights, token))
        {
            for (var index = 0; index < admitted.Records.Count && index < 32; index++)
            {
                token.ThrowIfCancellationRequested();
                if (clock.Elapsed >= TimeSpan.FromMinutes(10)) throw new DecisionException("training_deadline_exceeded", "CUDA feature export deadline expired.");
                var record = admitted.Records[index]; progress?.Invoke($"export {index + 1}/{admitted.Records.Count} {record.RecordId}");
                var feature = Export(model.Tokenizer, encoder, identity, record.Request, record.Question, record.RecordId,
                    record.Language, record.TargetIndex, token);
                (record.Split == "train" ? train : development).Add(feature);
            }
        }
        // Encoder resident weights/modules are gone before head SGD uploads its training features.
        return new(new(identity, train.ToArray()), development.ToArray());
    }

    public static MarkerFeatureExample Export(TokenizerJson tokenizer, CudaModernBertEncoder encoder, MarkerFeatureIdentity identity,
        IndependentDecisionRequest request, IndependentDecisionQuestion question, string recordId, string language, int targetIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var token = deadline.Token; var clock = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(recordId) || recordId.Length > 128 || language is not ("zh" or "en") ||
            identity.HiddenSize != encoder.Config.HiddenSize || identity.HiddenSize is < 1 or > 2048)
            throw new DecisionException("training_input_invalid", "CUDA feature record identity, language or width is invalid.");
        var sequence = IndependentDecisionV1SequenceBuilder.Build(tokenizer, request, question,
            new IndependentDecisionSequenceOptions { MaxTokens = Math.Min(1024, encoder.Config.MaxTokens) }, token);
        if ((uint)targetIndex >= (uint)sequence.MarkerPositions.Length) throw new DecisionException("training_label_invalid", "CUDA feature target is outside candidates.");
        var hidden = encoder.Encode(sequence.TokenIds, token);
        if (hidden.Length != checked(sequence.TokenIds.Length * identity.HiddenSize)) throw new DecisionException("training_shape_invalid", "CUDA encoder output is not the complete sequence tensor.");
        for (var index = 0; index < hidden.Length && index < 2_097_152; index++)
        {
            if ((index & 255) == 0)
            { token.ThrowIfCancellationRequested(); if (clock.Elapsed >= TimeSpan.FromMinutes(10)) throw new DecisionException("training_deadline_exceeded", "CUDA feature finite check deadline expired."); }
            if (!float.IsFinite(hidden[index])) throw new DecisionException("training_value_invalid", "CUDA full-sequence feature tensor is non-finite.");
        }
        var features = new float[sequence.MarkerPositions.Length][];
        for (var candidate = 0; candidate < features.Length && candidate < 32; candidate++)
        { token.ThrowIfCancellationRequested(); features[candidate] = hidden.AsSpan(checked(sequence.MarkerPositions[candidate] * identity.HiddenSize), identity.HiddenSize).ToArray(); }
        return new(recordId, sequence.Type, language, sequence.TokenIds, sequence.MarkerPositions, features, targetIndex);
    }
}
