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
    private readonly IndependentEncoderModelPackage _model;
    private readonly IIndependentDecisionPipeline _pipeline;
    private readonly IndependentDecisionRuntimeOptions _options;
    private readonly object _lifecycleGate = new();
    private int _disposed;
    private bool _evaluationActive;
    private bool _disposeRequested;

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
                pipeline.MaxTokens is < 2 or > 1024 || effective.MaxTokens > pipeline.MaxTokens)
                throw new DecisionException("independent_runtime_identity_mismatch", "The independent execution pipeline differs from the loaded assets or token budget.");

            _model = model;
            _pipeline = pipeline;
            _options = effective;
        }
        catch
        {
            try { pipeline?.Dispose(); }
            finally { model.Dispose(); }
            throw;
        }
    }

    public string ModelId => _model.ModelId;
    public string EncoderSha256 => _model.EncoderSha256;
    public string TokenizerSha256 => _model.TokenizerSha256;
    public string Backend => _pipeline.Backend;
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Evaluates every question in a bounded request with one shared deadline.</summary>
    public IndependentDecisionResponse Evaluate(
        IndependentDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_evaluationActive)
                throw new DecisionException("decision_session_busy", "The independent runtime cannot reenter an active evaluation.");
            ArgumentNullException.ThrowIfNull(request);
            if (request.SchemaVersion != 1 || !string.Equals(request.ModelId, _model.ModelId, StringComparison.Ordinal))
                throw new DecisionException("independent_runtime_model_mismatch", "decision-v1 request model_id does not match the loaded independent model.");
            if (request.Questions is null || request.Questions.Count is < 1 or > 32)
                throw new DecisionException("decision_v1_question_invalid", "decision-v1 questions must contain 1 to 32 items.");

            _evaluationActive = true;
            try
            {
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lifetime.CancelAfter(_options.Deadline);
                try
                {
                    var answers = new IndependentDecisionAnswer[request.Questions.Count];
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    for (var index = 0; index < answers.Length; index++)
                    {
                        lifetime.Token.ThrowIfCancellationRequested();
                        var question = request.Questions[index] ?? throw new DecisionException("decision_v1_question_invalid", "decision-v1 question is null.");
                        if (string.IsNullOrWhiteSpace(question.Id) || question.Id.Length > 65_536)
                            throw new DecisionException("decision_v1_text_invalid", "decision-v1 question id must be non-empty and bounded.");
                        if (!ids.Add(question.Id))
                            throw new DecisionException("decision_v1_question_invalid", "Question IDs must be unique.");
                        answers[index] = EvaluateQuestion(request, question, lifetime.Token);
                    }
                    return new IndependentDecisionResponse(_model.ModelId, "decision-v1", answers)
                    {
                        EncoderSha256 = _model.EncoderSha256,
                        TokenizerSha256 = _model.TokenizerSha256,
                        Backend = _pipeline.Backend,
                    };
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new DecisionException("independent_runtime_deadline_exceeded", "Independent decision-v1 evaluation exceeded its deadline.", exception);
                }
            }
            finally
            {
                _evaluationActive = false;
                if (_disposeRequested) DisposeResources();
            }
        }
    }

    private IndependentDecisionAnswer EvaluateQuestion(
        IndependentDecisionRequest request,
        IndependentDecisionQuestion question,
        CancellationToken cancellationToken)
    {
        var sequence = IndependentDecisionV1SequenceBuilder.Build(
            _model.Tokenizer,
            request,
            question,
            new IndependentDecisionSequenceOptions
            {
                MaxTokens = _options.MaxTokens,
                MaxCharacters = _options.MaxCharacters,
                Deadline = _options.Deadline,
            },
            cancellationToken);
        var logits = _pipeline.Score(sequence.TokenIds, sequence.Type, sequence.MarkerPositions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (logits is null || logits.Length != sequence.MarkerPositions.Length || Array.Exists(logits, value => !double.IsFinite(value)))
            throw new DecisionException("independent_runtime_shape_invalid", "Independent pipeline must return exactly one finite logit per candidate.");
        var probabilities = Softmax(logits);
        var selected = 0;
        for (var candidate = 1; candidate < probabilities.Length; candidate++)
            if (probabilities[candidate] > probabilities[selected]) selected = candidate;
        var concentration = probabilities.Max();
        var candidateIds = sequence.CandidateIds ?? throw new DecisionException("independent_runtime_shape_invalid", "Independent sequence has no candidate identity.");
        return new IndependentDecisionAnswer(question.Id, sequence.Type, candidateIds, probabilities, selected, concentration);
    }

    private static double[] Softmax(ReadOnlySpan<double> logits)
    {
        if (logits.Length is < 2 or > 32) throw new DecisionException("independent_runtime_shape_invalid", "Independent logits have an invalid candidate count.");
        var maximum = logits[0];
        for (var index = 1; index < logits.Length; index++) maximum = Math.Max(maximum, logits[index]);
        var probabilities = new double[logits.Length];
        var sum = 0d;
        for (var index = 0; index < logits.Length; index++) sum += probabilities[index] = Math.Exp(logits[index] - maximum);
        if (!double.IsFinite(sum) || sum <= 0d) throw new DecisionException("independent_runtime_value_invalid", "Independent logits produced a non-finite probability sum.");
        for (var index = 0; index < probabilities.Length; index++)
        {
            probabilities[index] /= sum;
            if (!double.IsFinite(probabilities[index])) throw new DecisionException("independent_runtime_value_invalid", "Independent probability is non-finite.");
        }
        return probabilities;
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            Interlocked.Exchange(ref _disposed, 1);
            if (_evaluationActive)
            {
                _disposeRequested = true;
                return;
            }
            DisposeResources();
        }
    }

    private void DisposeResources()
    {
        try { _pipeline.Dispose(); }
        finally { _model.Dispose(); }
    }
}
