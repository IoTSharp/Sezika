namespace Sezika;

/// <summary>Bounded options for the independent decision-v1 runtime facade.</summary>
public sealed record IndependentDecisionRuntimeOptions
{
    public int MaxTokens { get; init; } = 1024;
    public int MaxCharacters { get; init; } = 262_144;
    public TimeSpan Deadline { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaxTokens is < 2 or > 8192 || MaxCharacters is < 1 or > 1_048_576 ||
            Deadline <= TimeSpan.Zero || Deadline > TimeSpan.FromMinutes(5))
            throw new DecisionException("independent_runtime_options_invalid", "Independent runtime limits are outside the bounded range.");
    }
}

/// <summary>One uncalibrated typed answer produced by the independent marker head.</summary>
public sealed record IndependentDecisionAnswer(
    string QuestionId,
    MarkerQuestionType Type,
    IReadOnlyList<string> CandidateIds,
    IReadOnlyList<double> Probabilities,
    int SelectedIndex,
    double Concentration,
    string Status = "uncalibrated");

/// <summary>Typed decision-v1 response. Probabilities are a distribution, not an accuracy claim or execution authorization.</summary>
public sealed record IndependentDecisionResponse(
    string ModelId,
    string Protocol,
    IReadOnlyList<IndependentDecisionAnswer> Answers,
    string Status = "uncalibrated")
{
    public string EncoderSha256 { get; init; } = string.Empty;
    public string TokenizerSha256 { get; init; } = string.Empty;
    public string Backend { get; init; } = "cpu-independent-linear-marker-head";
}

/// <summary>
/// Executes the independent encoder and separately supplied marker head locally.
/// The runtime owns both assets and never falls back to the legacy Laya loader.
/// </summary>
public sealed class IndependentDecisionV1Runtime : IDisposable
{
    private readonly DecisionV1ExecutionSession _session;

    /// <summary>
    /// Loads the hash-bound encoder and a separately hash-bound inference head.
    /// The caller supplies the feature identity from the training report so a
    /// head cannot be selected by filename or silently rebound to another data
    /// manifest.
    /// </summary>
    public static IndependentDecisionV1Runtime Load(
        string packageDirectory,
        string headPath,
        MarkerFeatureIdentity headIdentity,
        string expectedFeatureSha256,
        string expectedHeadSha256,
        IndependentDecisionRuntimeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(headPath);
        ArgumentNullException.ThrowIfNull(headIdentity);
        var model = IndependentModelLoader.Load(packageDirectory, cancellationToken);
        try
        {
            var head = IndependentMarkerHeadAssetStore.Load(
                headPath, headIdentity, expectedFeatureSha256, expectedHeadSha256, cancellationToken);
            return new IndependentDecisionV1Runtime(model, head, options);
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }

    public IndependentDecisionV1Runtime(
        IndependentEncoderModelPackage model,
        IndependentMarkerHeadAsset head,
        IndependentDecisionRuntimeOptions? options = null)
        : this(model, head, options, null) { }

    /// <summary>Transfers ownership of model and pipeline; a null pipeline selects the explicit CPU implementation.</summary>
    public IndependentDecisionV1Runtime(
        IndependentEncoderModelPackage model,
        IndependentMarkerHeadAsset head,
        IndependentDecisionRuntimeOptions? options,
        IIndependentDecisionPipeline? pipeline)
    {
        ArgumentNullException.ThrowIfNull(model);
        try
        {
            ArgumentNullException.ThrowIfNull(head);
            var effective = options ?? new IndependentDecisionRuntimeOptions();
            effective.Validate();
            if (effective.MaxTokens > model.Encoder.Config.MaxTokens)
                throw new DecisionException("independent_runtime_options_invalid", "Independent runtime MaxTokens exceeds the loaded encoder budget.");
            IndependentMarkerHeadTrainer.ValidateIdentity(head.Identity, "independent_runtime_identity_invalid");
            if (!IndependentMarkerHeadTrainer.IsHash(head.FeatureSha256) || !IndependentMarkerHeadTrainer.IsHash(head.FileSha256))
                throw new DecisionException("independent_runtime_identity_invalid", "Independent head feature and file hashes are required.");
            if (model.IsDisposed)
                throw new DecisionException("independent_runtime_model_disposed", "The independent encoder package is already disposed.");
            if (!string.Equals(head.Identity.ModelId, model.ModelId, StringComparison.Ordinal) ||
                !string.Equals(head.Identity.EncoderSha256, model.EncoderSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(head.Identity.TokenizerSha256, model.TokenizerSha256, StringComparison.OrdinalIgnoreCase) ||
                head.Identity.HiddenSize != model.Encoder.Config.HiddenSize ||
                head.Identity.Protocol != "decision-v1" || head.Identity.LengthPolicy != "strict")
                throw new DecisionException("independent_runtime_identity_mismatch", "The independent head is not bound to the loaded encoder and decision-v1 contract.");

            pipeline ??= new IndependentCpuDecisionPipeline(model.Encoder, head);
            if (!IndependentMarkerHeadTrainer.IdentityMatches(pipeline.Identity, head.Identity) ||
                !string.Equals(pipeline.FeatureSha256, head.FeatureSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(pipeline.HeadSha256, head.FileSha256, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(pipeline.Backend) || pipeline.Backend.Length > 128 ||
                pipeline.MaxTokens is < 2 or > ModernBertConfig.MaximumSupportedTokens || effective.MaxTokens > pipeline.MaxTokens)
                throw new DecisionException("independent_runtime_identity_mismatch", "The independent execution pipeline differs from the loaded assets or token budget.");

            _session = new DecisionV1ExecutionSession(model, pipeline.Score, pipeline,
                () => pipeline.Backend, effective, this);
        }
        catch
        {
            try { pipeline?.Dispose(); }
            finally { model.Dispose(); }
            throw;
        }
    }

    public string ModelId => _session.ModelId;
    public string EncoderSha256 => _session.EncoderSha256;
    public string TokenizerSha256 => _session.TokenizerSha256;
    public string Backend => _session.Backend;
    public bool IsDisposed => _session.IsDisposed;

    /// <summary>Evaluates every question in a bounded request with one shared deadline.</summary>
    public IndependentDecisionResponse Evaluate(
        IndependentDecisionRequest request,
        CancellationToken cancellationToken = default)
        => _session.Evaluate<object?, IndependentDecisionResponse>(request, null,
            static (_, modelId, encoderSha256, tokenizerSha256, backend, answers) =>
                new IndependentDecisionResponse(modelId, "decision-v1", answers)
                {
                    EncoderSha256 = encoderSha256,
                    TokenizerSha256 = tokenizerSha256,
                    Backend = backend,
                }, cancellationToken);

    public void Dispose() => _session.Dispose();
}
