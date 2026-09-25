using System.Text.Json;

namespace Sezika;

/// <summary>
/// Typed request evaluator for the pinned Laya/mmBERT marker-head contract.
/// The generic <see cref="DecisionEngine"/> remains the small reference model
/// path; this evaluator constructs the real marker sequence and runs the two
/// layer decision head from <see cref="ModernBertModelPackage"/>.
/// </summary>
public sealed class ModernBertDecisionEngine : IDecisionEngine, IDisposable
{
    private readonly ModernBertModelPackage _model;
    private readonly IMarkerDecisionPipeline _pipeline;
    private readonly string _backend;
    private readonly DecisionLimits _limits;
    private readonly DecisionResourceBudget _budget;
    private readonly double _minimumConcentration;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly object _lifecycleSync = new();
    private bool _disposed;
    private bool _sessionActive;
    private bool _resourcesDisposed;

    /// <summary>Creates a session and transfers ownership of the supplied model package and optional pipeline.</summary>
    /// <remarks>An injected pipeline must use this model's tokenizer/weights contract. Its encoder/device lifetime remains the caller's responsibility.</remarks>
    public ModernBertDecisionEngine(
        ModernBertModelPackage model,
        DecisionLimits? limits = null,
        DecisionResourceBudget? budget = null,
        double minimumConcentration = 0d)
        : this(model, null, "cpu-modernbert-marker-head", limits, budget, minimumConcentration) { }

    public ModernBertDecisionEngine(
        ModernBertModelPackage model,
        IMarkerDecisionPipeline? pipeline,
        string backend,
        DecisionLimits? limits = null,
        DecisionResourceBudget? budget = null,
        double minimumConcentration = 0d)
    {
        ArgumentNullException.ThrowIfNull(model);
        try
        {
            pipeline ??= new ModernBertDecisionPipeline(model.Encoder, model.Head);
            var effectiveLimits = limits ?? DecisionLimits.Default;
            effectiveLimits.Validate();
            var effectiveBudget = budget ?? new DecisionResourceBudget();
            effectiveBudget.Validate();
            if (model.IsDisposed)
                throw new DecisionException("decision_model_unloaded", "The model package has already been unloaded.");
            var residentBytes = checked(model.EstimatedResidentBytes +
                (pipeline is ModernBertDecisionPipeline cpu ? cpu.QuantizedWeightBytes : 0));
            if (residentBytes > effectiveBudget.MaxResidentBytes)
                throw new DecisionException("decision_model_memory_limit_exceeded", $"The loaded model requires {residentBytes} bytes, above the session limit ({effectiveBudget.MaxResidentBytes}).");
            if (!double.IsFinite(minimumConcentration) || minimumConcentration < 0 || minimumConcentration > 1)
                throw new ArgumentOutOfRangeException(nameof(minimumConcentration));

            _model = model;
            _pipeline = pipeline;
            ArgumentException.ThrowIfNullOrWhiteSpace(backend);
            _backend = backend;
            _limits = effectiveLimits;
            _budget = effectiveBudget;
            _minimumConcentration = minimumConcentration;
        }
        catch
        {
            // The engine owns the package after construction is attempted;
            // reject paths must release all model tensors as well.
            try { pipeline?.Dispose(); }
            finally { try { model.Dispose(); } finally { _sessionGate.Dispose(); } }
            throw;
        }
    }

    public DecisionResponse Evaluate(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_model.IsDisposed)
                throw new DecisionException("decision_model_unloaded", "The model package has been unloaded.");
            DecisionRequestValidator.Validate(request, _limits);
            if (request.Questions.Count > _budget.MaxQuestions)
                throw new DecisionException("decision_question_limit_exceeded", $"Question count exceeds the session budget ({_budget.MaxQuestions}).");
            if (!string.Equals(request.Model, _model.ModelId, StringComparison.Ordinal))
                throw new DecisionException("decision_model_not_installed", $"Model '{request.Model}' is not loaded.");
            try
            {
                if (!_sessionGate.Wait(0, cancellationToken))
                    throw new DecisionException("decision_session_busy", "The decision model session is busy.");
                _sessionActive = true;
            }
            catch (OperationCanceledException exception)
            {
                throw new DecisionException("decision_cancelled", "Decision evaluation was cancelled before the session was acquired.", exception);
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_budget.Deadline);
        var tokenCount = 0;
        var workspaceBytes = 0L;
        try
        {
            var answers = new Dictionary<string, Answer>(request.Questions.Count, StringComparer.Ordinal);
            foreach (var (questionId, question) in request.Questions)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var answer = EvaluateQuestion(request.State, question, deadline.Token, ref tokenCount, ref workspaceBytes);
                answers.Add(questionId, answer);
            }
            return new DecisionResponse
            {
                Model = _model.ModelId,
                ModelRevision = _model.Revision,
                TokenizerRevision = _model.TokenizerRevision,
                Backend = _backend,
                Answers = answers,
                Usage = new DecisionUsage
                {
                    QuestionCount = answers.Count,
                    TokenCount = tokenCount,
                    // The current cross-encoder evaluates one question per
                    // forward pass. This is an explicit micro-batch size of
                    // one; the configured upper bound prevents a future
                    // implementation from silently creating an unbounded batch.
                    MicroBatchCount = answers.Count,
                    WorkspaceBytes = workspaceBytes,
                },
            };
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DecisionException("decision_deadline_exceeded", "Decision evaluation exceeded its deadline.", exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new DecisionException("decision_cancelled", "Decision evaluation was cancelled.", exception);
        }
        finally
        {
            lock (_lifecycleSync)
            {
                _sessionGate.Release();
                _sessionActive = false;
                if (_disposed)
                    DisposeResourcesLocked();
            }
        }
    }

    private Answer EvaluateQuestion(JsonElement state, Question question, CancellationToken cancellationToken, ref int tokenCount, ref long workspaceBytes)
    {
        var typeId = question switch
        {
            ChoiceQuestion => 0,
            ScoreQuestion => 1,
            BooleanQuestion => 2,
            _ => throw new DecisionException("decision_question_type_unsupported", "Question type is unsupported."),
        };

        var criteria = question switch
        {
            ChoiceQuestion choice => choice.Criteria.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => (Key: item.Key, Value: item.Value)).ToArray(),
            ScoreQuestion score => score.Criteria.Select((value, index) => (Key: index.ToString(System.Globalization.CultureInfo.InvariantCulture), Value: value)).ToArray(),
            BooleanQuestion boolean when boolean.Criteria is not null =>
                new[] { (Key: "true", Value: boolean.Criteria.WhenTrue), (Key: "false", Value: boolean.Criteria.WhenFalse) },
            _ => throw new DecisionException("decision_criteria_required", "Boolean criteria are required."),
        };

        var tokenBudget = Math.Min(_limits.MaxTokensPerQuestion, Math.Min(_model.Encoder.Config.MaxTokens, _model.HeadMaxTokens));
        var (tokens, markers) = MarkerSequenceBuilder.Build(_model.Tokenizer, state, question.Instructions,
            criteria.Select(item => item.Value).ToArray(), tokenBudget, cancellationToken);
        tokenCount = checked(tokenCount + tokens.Length);
        if (tokenCount > _budget.MaxTokens)
            throw new DecisionException("decision_token_budget_exceeded", "The request exceeds the total token budget.");
        var requiredWorkspace = EstimateWorkspaceBytes(tokens.Length, _model.Encoder.Config.HiddenSize, criteria.Length);
        if (requiredWorkspace > _budget.MaxWorkspaceBytes)
            throw new DecisionException("decision_workspace_limit_exceeded", $"The encoded question requires {requiredWorkspace} workspace bytes, above the session limit ({_budget.MaxWorkspaceBytes}).");
        workspaceBytes = Math.Max(workspaceBytes, requiredWorkspace);

        var logits = _pipeline.Score(tokens, typeId, markers, cancellationToken);
        var temperature = _model.Temperature[typeId];
        var probabilities = DecisionMath.Softmax(logits, temperature);
        var concentration = DecisionMath.Concentration(probabilities);
        var abstained = concentration < _minimumConcentration;
        var calibration = new CalibrationInfo { Status = "uncalibrated" };

        return question switch
        {
            ChoiceQuestion => new ChoiceAnswer
            {
                Status = abstained ? "abstained" : "answered", AbstentionReason = abstained ? "low_concentration" : null,
                Calibration = calibration, Choice = criteria[DecisionMath.ArgMax(probabilities)].Key, Concentration = concentration,
                Logits = criteria.Select((item, index) => (item.Key, Value: (double)logits[index]))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                Probabilities = criteria.Select((item, index) => (item.Key, Value: (double)probabilities[index]))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            },
            ScoreQuestion => new ScoreAnswer
            {
                Status = abstained ? "abstained" : "answered", AbstentionReason = abstained ? "low_concentration" : null,
                Calibration = calibration, Score = probabilities.Select((value, index) => index * (double)value).Sum(), Concentration = concentration,
                Legend = criteria.ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal),
                Logits = criteria.Select((item, index) => (item.Key, Value: (double)logits[index]))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                Probabilities = criteria.Select((item, index) => (item.Key, Value: (double)probabilities[index]))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            },
            BooleanQuestion => new BooleanAnswer
            {
                Status = abstained ? "abstained" : "answered", AbstentionReason = abstained ? "low_concentration" : null,
                Calibration = calibration, ProbabilityTrue = probabilities[0],
                Logits = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["true"] = logits[0], ["false"] = logits[1],
                },
            },
            _ => throw new DecisionException("decision_question_type_unsupported", "Question type is unsupported."),
        };
    }

    private static long EstimateWorkspaceBytes(int tokenCount, int hiddenSize, int candidateCount)
    {
        if (tokenCount <= 0 || hiddenSize <= 0 || candidateCount <= 0)
            throw new DecisionException("decision_workspace_invalid", "Workspace dimensions must be positive.");
        // Bounded estimate for encoder/head intermediates. It is deliberately
        // conservative and checked before native/GPU allocation is possible.
        return checked((long)tokenCount * hiddenSize * sizeof(float) * 12L +
                       (long)candidateCount * hiddenSize * sizeof(float) * 8L);
    }

    /// <summary>
    /// Rejects new requests immediately. If a bounded request is in flight,
    /// its completion path releases the model and gate after the last access.
    /// </summary>
    public void Dispose()
    {
        lock (_lifecycleSync)
        {
            if (_disposed) return;
            _disposed = true;
            if (!_sessionActive)
                DisposeResourcesLocked();
        }
    }

    private void DisposeResourcesLocked()
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;
        try { _pipeline.Dispose(); }
        finally { try { _model.Dispose(); } finally { _sessionGate.Dispose(); } }
    }
}
