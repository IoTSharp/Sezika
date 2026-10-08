namespace Sezika;

/// <summary>Diagnostic distributions from a reproducible, zero-update head; no quality or training claim.</summary>
public sealed record DiagnosticDecisionResponse(
    string ModelId,
    string Protocol,
    DiagnosticHeadIdentity Identity,
    string DiagnosticAssetSha256,
    string InitializationDescriptorSha256,
    string Backend,
    IReadOnlyList<IndependentDecisionAnswer> Answers)
{
    public DiagnosticHeadOrigin Origin => DiagnosticHeadOrigin.UntrainedDiagnostic;
    public int CompletedUpdates => 0;
    public double? ObservedLoss => null;
    public string Status => "untrained-diagnostic";
    public string EncoderSha256 => Identity.EncoderSha256;
    public string TokenizerSha256 => Identity.TokenizerSha256;
}

/// <summary>Explicit diagnostic execution. Owns the encoder and the required supplied pipeline.</summary>
public sealed class DiagnosticDecisionV1Runtime : IDisposable
{
    private readonly DiagnosticHeadAsset _asset;
    private readonly DecisionV1ExecutionSession _session;

    public DiagnosticDecisionV1Runtime(IndependentEncoderModelPackage model, DiagnosticHeadAsset asset,
        IDiagnosticDecisionPipeline pipeline, IndependentDecisionRuntimeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        try
        {
            ArgumentNullException.ThrowIfNull(asset);
            ArgumentNullException.ThrowIfNull(pipeline);
            var effective = options ?? new IndependentDecisionRuntimeOptions();
            effective.Validate();
            if (model.IsDisposed)
                throw new DecisionException("diagnostic_runtime_model_disposed", "The diagnostic encoder package is already disposed.");
            if (effective.MaxTokens > model.Encoder.Config.MaxTokens)
                throw new DecisionException("independent_runtime_options_invalid", "Diagnostic MaxTokens exceeds the encoder budget.");
            var identity = asset.Identity;
            if (identity is null || string.IsNullOrWhiteSpace(identity.ModelId) || identity.ModelId.Length > 256 ||
                !IsHash(identity.EncoderSha256) || !IsHash(identity.TokenizerSha256) || identity.HiddenSize is < 1 or > 2048 ||
                identity.Protocol != "decision-v1" || identity.LengthPolicy != "strict" ||
                !IsHash(asset.FileSha256) || !IsHash(asset.InitializationDescriptorSha256) ||
                asset.Head.HiddenSize != identity.HiddenSize ||
                asset.Origin != DiagnosticHeadOrigin.UntrainedDiagnostic || asset.CompletedUpdates != 0 || asset.ObservedLoss is not null ||
                asset.InitializationAlgorithm != DiagnosticHeadAssetStore.Algorithm ||
                asset.AlgorithmVersion != DiagnosticHeadAssetStore.AlgorithmVersion ||
                asset.InitializationDescriptorDomain != DiagnosticHeadAssetStore.DescriptorDomain)
                throw new DecisionException("diagnostic_runtime_identity_invalid", "A complete untrained diagnostic asset identity is required.");
            if (!string.Equals(identity.ModelId, model.ModelId, StringComparison.Ordinal) ||
                !string.Equals(identity.EncoderSha256, model.EncoderSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(identity.TokenizerSha256, model.TokenizerSha256, StringComparison.OrdinalIgnoreCase) ||
                identity.HiddenSize != model.Encoder.Config.HiddenSize)
                throw new DecisionException("diagnostic_runtime_identity_mismatch", "Diagnostic asset differs from the loaded encoder.");
            var executionIdentity = pipeline.Identity;
            if (executionIdentity is null ||
                !string.Equals(executionIdentity.ModelId, identity.ModelId, StringComparison.Ordinal) ||
                !string.Equals(executionIdentity.EncoderSha256, identity.EncoderSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(executionIdentity.TokenizerSha256, identity.TokenizerSha256, StringComparison.OrdinalIgnoreCase) ||
                executionIdentity.HiddenSize != identity.HiddenSize || executionIdentity.Protocol != identity.Protocol ||
                executionIdentity.LengthPolicy != identity.LengthPolicy ||
                !string.Equals(pipeline.DiagnosticAssetSha256, asset.FileSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(pipeline.InitializationDescriptorSha256, asset.InitializationDescriptorSha256, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(pipeline.Backend) || pipeline.Backend.Length > 128 ||
                pipeline.MaxTokens is < 2 or > 1024 || effective.MaxTokens > pipeline.MaxTokens)
                throw new DecisionException("diagnostic_runtime_identity_mismatch", "Diagnostic pipeline differs from the asset or token budget.");
            _asset = asset;
            _session = new DecisionV1ExecutionSession(model, pipeline.Score, pipeline, () => pipeline.Backend, effective, this);
        }
        catch (Exception primary)
        {
            List<Exception>? failures = null;
            try { pipeline?.Dispose(); }
            catch (Exception cleanup) { (failures ??= [primary]).Add(cleanup); }
            finally
            {
                try { model.Dispose(); }
                catch (Exception cleanup) { (failures ??= [primary]).Add(cleanup); }
            }
            if (failures is not null) throw new AggregateException("Diagnostic construction and cleanup failed.", failures);
            throw;
        }
    }

    public string ModelId => _session.ModelId;
    public string EncoderSha256 => _session.EncoderSha256;
    public string TokenizerSha256 => _session.TokenizerSha256;
    public string Backend => _session.Backend;
    public bool IsDisposed => _session.IsDisposed;
    public DiagnosticHeadIdentity Identity => _asset.Identity;
    public string DiagnosticAssetSha256 => _asset.FileSha256;
    public string InitializationDescriptorSha256 => _asset.InitializationDescriptorSha256;

    public DiagnosticDecisionResponse Evaluate(IndependentDecisionRequest request, CancellationToken cancellationToken = default)
        => _session.Evaluate<DiagnosticHeadAsset, DiagnosticDecisionResponse>(request, _asset,
            static (asset, modelId, _, _, backend, answers) => new DiagnosticDecisionResponse(modelId, "decision-v1",
                asset.Identity, asset.FileSha256, asset.InitializationDescriptorSha256, backend, answers), cancellationToken);

    public void Dispose() => _session.Dispose();

    private static bool IsHash(string? value)
    {
        if (value is null || value.Length != 64) return false;
        foreach (var character in value)
            if (character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F') and not (>= 'a' and <= 'f')) return false;
        return true;
    }
}
