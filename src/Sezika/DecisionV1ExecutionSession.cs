namespace Sezika;

internal delegate double[] DecisionV1Scorer(ReadOnlySpan<int> tokenIds, MarkerQuestionType type,
    ReadOnlySpan<int> markerPositions, CancellationToken cancellationToken);

internal delegate TResult DecisionV1ResponseFactory<TState, TResult>(TState state, string modelId,
    string encoderSha256, string tokenizerSha256, string backend, IndependentDecisionAnswer[] answers);

/// <summary>Shared bounded execution and lifecycle only; contains no head or training identity.</summary>
internal sealed class DecisionV1ExecutionSession : IDisposable
{
    private readonly IndependentEncoderModelPackage _model;
    private readonly DecisionV1Scorer _score;
    private readonly IDisposable _pipelineOwner;
    private readonly Func<string> _backend;
    private readonly IndependentDecisionRuntimeOptions _options;
    private readonly object _runtimeOwner;
    private readonly object _lifecycleGate = new();
    private int _disposed;
    private bool _evaluationActive;
    private bool _disposeRequested;

    internal DecisionV1ExecutionSession(IndependentEncoderModelPackage model, DecisionV1Scorer score,
        IDisposable pipelineOwner, Func<string> backend, IndependentDecisionRuntimeOptions options, object runtimeOwner)
    {
        _model = model; _score = score; _pipelineOwner = pipelineOwner;
        _backend = backend; _options = options; _runtimeOwner = runtimeOwner;
    }

    internal string ModelId => _model.ModelId;
    internal string EncoderSha256 => _model.EncoderSha256;
    internal string TokenizerSha256 => _model.TokenizerSha256;
    internal string Backend => _backend();
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public TResult Evaluate<TState, TResult>(
        IndependentDecisionRequest request,
        TState state,
        DecisionV1ResponseFactory<TState, TResult> responseFactory,
        CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, _runtimeOwner);
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
                    return responseFactory(state, _model.ModelId, _model.EncoderSha256,
                        _model.TokenizerSha256, _backend(), answers);
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
        var logits = _score(sequence.TokenIds, sequence.Type, sequence.MarkerPositions, cancellationToken);
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
        try { _pipelineOwner.Dispose(); }
        finally { _model.Dispose(); }
    }
}
