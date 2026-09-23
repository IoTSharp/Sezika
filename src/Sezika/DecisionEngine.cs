using System.Text.Json;

namespace Sezika;

public sealed record DecisionEngineOptions
{
    public DecisionLimits Limits { get; init; } = DecisionLimits.Default;
    public DecisionResourceBudget Budget { get; init; } = new();
    public double MinimumConcentration { get; init; }
    public CalibrationProfile? Calibration { get; init; }

    public void Validate()
    {
        Limits.Validate();
        Budget.Validate();
        if (!double.IsFinite(MinimumConcentration) || MinimumConcentration < 0 || MinimumConcentration > 1)
            throw new DecisionException("decision_budget_invalid", "Minimum concentration must be between zero and one.");
    }
}

public sealed class DecisionEngine : IDecisionEngine, IDisposable
{
    private readonly DecisionModel _model;
    private readonly DecisionEngineOptions _options;
    private readonly IDecisionScorer _scorer;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private bool _disposed;

    public DecisionEngine(DecisionModel model, DecisionEngineOptions? options = null, IDecisionScorer? scorer = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _options = options ?? new DecisionEngineOptions();
        _scorer = scorer ?? new CpuDecisionScorer();
        _options.Validate();
        _options.Calibration?.Validate(model.Revision, model.TokenizerRevision, "decision");
    }

    public DecisionResponse Evaluate(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DecisionRequestValidator.Validate(request, _options.Limits);
        if (request.Questions.Count > _options.Budget.MaxQuestions)
            throw new DecisionException("decision_question_limit_exceeded", $"Question count exceeds the session budget ({_options.Budget.MaxQuestions}).");
        if (!string.Equals(request.Model, _model.ModelId, StringComparison.Ordinal))
            throw new DecisionException("decision_model_not_installed", $"Model '{request.Model}' is not loaded.");
        try
        {
            if (!_sessionGate.Wait(0, cancellationToken))
                throw new DecisionException("decision_session_busy", "The decision model session is busy.");
        }
        catch (OperationCanceledException exception)
        {
            throw new DecisionException("decision_cancelled", "Decision evaluation was cancelled before the session was acquired.", exception);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.Budget.Deadline);
        try
        {
            var answers = new Dictionary<string, Answer>(request.Questions.Count, StringComparer.Ordinal);
            var tokenCount = 0;
            foreach (var (questionId, question) in request.Questions)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var answer = EvaluateQuestion(request.State, question, deadline.Token, ref tokenCount);
                answers.Add(questionId, answer);
            }
            return new DecisionResponse
            {
                Model = _model.ModelId,
                ModelRevision = _model.Revision,
                TokenizerRevision = _model.TokenizerRevision,
                Backend = _model.Backend,
                Answers = answers,
                Usage = new DecisionUsage { QuestionCount = answers.Count, TokenCount = tokenCount, MicroBatchCount = answers.Count },
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
            _sessionGate.Release();
        }
    }

    private Answer EvaluateQuestion(JsonElement state, Question question, CancellationToken cancellationToken, ref int tokenCount)
    {
        var primitive = question switch
        {
            ChoiceQuestion => "choice",
            ScoreQuestion => "score",
            BooleanQuestion => "boolean",
            _ => throw new DecisionException("decision_question_type_unsupported", "Question type is unsupported."),
        };
        var calibration = _options.Calibration is null
            ? new CalibrationInfo { Status = "uncalibrated" }
            : new CalibrationInfo { Status = "calibrated", ProfileId = _options.Calibration.ProfileId, Scope = _options.Calibration.Scope };
        var instruction = ToPromptText(question.Instructions);
        return question switch
        {
            ChoiceQuestion choice => EvaluateChoice(state, instruction, choice, calibration, cancellationToken, ref tokenCount),
            ScoreQuestion score => EvaluateScore(state, instruction, score, calibration, cancellationToken, ref tokenCount),
            BooleanQuestion boolean => EvaluateBoolean(state, instruction, boolean, calibration, cancellationToken, ref tokenCount),
            _ => throw new DecisionException("decision_question_type_unsupported", $"Primitive '{primitive}' is unsupported."),
        };
    }

    private ChoiceAnswer EvaluateChoice(JsonElement state, string instruction, ChoiceQuestion question, CalibrationInfo calibration, CancellationToken cancellationToken, ref int tokenCount)
    {
        var labels = question.Criteria.Keys.ToArray();
        var pooledStates = new float[checked(labels.Length * _model.HeadWeights.Length)];
        for (var index = 0; index < labels.Length; index++)
        {
            EncodeCandidate(state, instruction, ToPromptText(question.Criteria[labels[index]]), pooledStates.AsSpan(index * _model.HeadWeights.Length, _model.HeadWeights.Length), cancellationToken, ref tokenCount);
        }
        var logits = new float[labels.Length];
        _scorer.Score(pooledStates, labels.Length, _model.HeadWeights, _model.HeadBias, logits, cancellationToken);
        var probabilities = DecisionMath.Softmax(logits, _options.Calibration?.Temperature ?? 1d);
        var selected = DecisionMath.ArgMax(probabilities);
        var concentration = DecisionMath.Concentration(probabilities);
        var abstained = concentration < _options.MinimumConcentration;
        return new ChoiceAnswer
        {
            Status = abstained ? "abstained" : "answered",
            AbstentionReason = abstained ? "low_concentration" : null,
            Calibration = calibration,
            Choice = labels[selected],
            Concentration = concentration,
            Logits = labels.Select((label, i) => (label, value: (double)logits[i])).ToDictionary(x => x.label, x => x.value, StringComparer.Ordinal),
            Probabilities = labels.Select((label, i) => (label, value: probabilities[i])).ToDictionary(x => x.label, x => x.value, StringComparer.Ordinal),
        };
    }

    private ScoreAnswer EvaluateScore(JsonElement state, string instruction, ScoreQuestion question, CalibrationInfo calibration, CancellationToken cancellationToken, ref int tokenCount)
    {
        var pooledStates = new float[checked(question.Criteria.Length * _model.HeadWeights.Length)];
        var legend = new Dictionary<string, JsonElement>(question.Criteria.Length, StringComparer.Ordinal);
        for (var index = 0; index < question.Criteria.Length; index++)
        {
            EncodeCandidate(state, instruction, ToPromptText(question.Criteria[index]), pooledStates.AsSpan(index * _model.HeadWeights.Length, _model.HeadWeights.Length), cancellationToken, ref tokenCount);
            legend[index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = question.Criteria[index].Clone();
        }
        var logits = new float[question.Criteria.Length];
        _scorer.Score(pooledStates, question.Criteria.Length, _model.HeadWeights, _model.HeadBias, logits, cancellationToken);
        var probabilities = DecisionMath.Softmax(logits, _options.Calibration?.Temperature ?? 1d);
        var expected = 0d;
        for (var index = 0; index < probabilities.Length; index++) expected += index * probabilities[index];
        var concentration = DecisionMath.Concentration(probabilities);
        var abstained = concentration < _options.MinimumConcentration;
        return new ScoreAnswer
        {
            Status = abstained ? "abstained" : "answered",
            AbstentionReason = abstained ? "low_concentration" : null,
            Calibration = calibration,
            Score = expected,
            Concentration = concentration,
            Legend = legend,
            Logits = Enumerable.Range(0, logits.Length).ToDictionary(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture), i => (double)logits[i], StringComparer.Ordinal),
            Probabilities = probabilities.Select((value, i) => (key: i.ToString(System.Globalization.CultureInfo.InvariantCulture), value)).ToDictionary(x => x.key, x => x.value, StringComparer.Ordinal),
        };
    }

    private BooleanAnswer EvaluateBoolean(JsonElement state, string instruction, BooleanQuestion question, CalibrationInfo calibration, CancellationToken cancellationToken, ref int tokenCount)
    {
        var criteria = question.Criteria ?? throw new DecisionException("decision_criteria_required", "Boolean criteria are required.");
        var pooledStates = new float[checked(2 * _model.HeadWeights.Length)];
        EncodeCandidate(state, instruction, ToPromptText(criteria.WhenTrue), pooledStates.AsSpan(0, _model.HeadWeights.Length), cancellationToken, ref tokenCount);
        EncodeCandidate(state, instruction, ToPromptText(criteria.WhenFalse), pooledStates.AsSpan(_model.HeadWeights.Length, _model.HeadWeights.Length), cancellationToken, ref tokenCount);
        var logits = new float[2];
        _scorer.Score(pooledStates, 2, _model.HeadWeights, _model.HeadBias, logits, cancellationToken);
        var trueLogit = logits[0];
        var falseLogit = logits[1];
        var probability = 1d / (1d + Math.Exp((double)falseLogit - trueLogit));
        var confidence = Math.Max(probability, 1d - probability);
        var abstained = confidence < _options.MinimumConcentration;
        return new BooleanAnswer
        {
            Status = abstained ? "abstained" : "answered",
            AbstentionReason = abstained ? "low_concentration" : null,
            Calibration = calibration,
            ProbabilityTrue = probability,
            Logits = new Dictionary<string, double>(StringComparer.Ordinal) { ["true"] = trueLogit, ["false"] = falseLogit },
        };
    }

    private void EncodeCandidate(JsonElement state, string instruction, string criteria, Span<float> pooled, CancellationToken cancellationToken, ref int tokenCount)
    {
        var prompt = $"state: {ToPromptText(state)}\ninstructions: {instruction}\ncriteria: {criteria}";
        var tokens = _model.Tokenizer.Encode(prompt, _options.Limits.MaxTokensPerQuestion);
        tokenCount += tokens.Length;
        if (tokenCount > _options.Budget.MaxTokens) throw new DecisionException("decision_token_limit_exceeded", "The request exceeds the total token budget.");
        var hidden = _model.Encoder.Encode(tokens, cancellationToken);
        var count = hidden.Length / pooled.Length;
        pooled.Clear();
        for (var token = 1; token < count - 1; token++)
        {
            for (var i = 0; i < pooled.Length; i++) pooled[i] += hidden[token * pooled.Length + i];
        }
        var denominator = Math.Max(1, count - 2);
        for (var i = 0; i < pooled.Length; i++) pooled[i] /= denominator;
    }

    private static string ToPromptText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _sessionGate.Dispose();
        }
    }
}
