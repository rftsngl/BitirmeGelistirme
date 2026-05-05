using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models.Verification;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Core;

public sealed class StepRuntimeCoordinator
{
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly IObservationProvider _observationProvider;
    private readonly IStepSafetyEvaluator? _stepSafetyEvaluator;
    private readonly IStepFollowUpDecider _stepFollowUpDecider;

    public StepRuntimeCoordinator(
        ICapabilityRegistry capabilityRegistry,
        IObservationProvider observationProvider,
        IStepFollowUpDecider stepFollowUpDecider,
        IStepSafetyEvaluator? stepSafetyEvaluator = null)
    {
        _capabilityRegistry = capabilityRegistry ?? throw new ArgumentNullException(nameof(capabilityRegistry));
        _observationProvider = observationProvider ?? throw new ArgumentNullException(nameof(observationProvider));
        _stepFollowUpDecider = stepFollowUpDecider ?? throw new ArgumentNullException(nameof(stepFollowUpDecider));
        _stepSafetyEvaluator = stepSafetyEvaluator;
    }

    public async Task<StepOutcome> ExecuteStepAsync(
        AgentStepState state,
        StepDecision decision,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleStepAsync(state, decision, cancellationToken);
    }

    public async Task<StepOutcome> ExecutePathAsync(
        AgentStepState state,
        StepDecision decision,
        CancellationToken cancellationToken = default)
    {
        return await ExecutePathAsync(state, decision, _stepFollowUpDecider, cancellationToken);
    }

    public const int DefaultMaxPathSteps = 4;

    public async Task<StepOutcome> ExecutePathAsync(
        AgentStepState state,
        StepDecision decision,
        IStepFollowUpDecider followUpDecider,
        CancellationToken cancellationToken = default)
    {
        return await ExecutePathAsync(state, decision, followUpDecider, DefaultMaxPathSteps, cancellationToken);
    }

    public async Task<StepOutcome> ExecutePathAsync(
        AgentStepState state,
        StepDecision decision,
        IStepFollowUpDecider followUpDecider,
        int maxSteps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(followUpDecider);

        if (maxSteps < 1)
        {
            maxSteps = 1;
        }

        var feedbackHistory = new List<StepFeedback>();
        var currentOutcome = await ExecuteSingleStepAsync(state, decision, cancellationToken);
        if (currentOutcome.Feedback is not null)
        {
            feedbackHistory.Add(currentOutcome.Feedback);
        }

        var aggregatedMessage = currentOutcome.Message;
        var stepCount = 1;

        while (currentOutcome.Disposition == StepContinuationDisposition.Continue &&
               currentOutcome.Feedback is not null &&
               stepCount < maxSteps)
        {
            var followUpDecision = followUpDecider.DecideNextStep(currentOutcome.State, currentOutcome.Feedback);
            if (followUpDecision.Disposition != StepContinuationDisposition.Continue ||
                followUpDecision.NextAction is null)
            {
                var stopDisposition = followUpDecision.Disposition == StepContinuationDisposition.RequiresApproval
                    ? StepContinuationDisposition.RequiresApproval
                    : StepContinuationDisposition.Stop;
                var stopMessage = string.IsNullOrWhiteSpace(followUpDecision.Message)
                    ? aggregatedMessage
                    : followUpDecision.Message;
                return FinalizePathOutcome(currentOutcome, feedbackHistory, stopDisposition, stopMessage);
            }

            var nextOutcome = await ExecuteSingleStepAsync(currentOutcome.State, followUpDecision, cancellationToken);
            if (nextOutcome.Feedback is not null)
            {
                feedbackHistory.Add(nextOutcome.Feedback);
            }

            aggregatedMessage = BuildAppendedMessage(aggregatedMessage, nextOutcome.Message);
            currentOutcome = nextOutcome;
            stepCount++;
        }

        var finalDisposition = currentOutcome.Disposition == StepContinuationDisposition.RequiresApproval
            ? StepContinuationDisposition.RequiresApproval
            : StepContinuationDisposition.Stop;

        return FinalizePathOutcome(currentOutcome, feedbackHistory, finalDisposition, aggregatedMessage);
    }

    private async Task<StepOutcome> ExecuteSingleStepAsync(
        AgentStepState state,
        StepDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.NextAction is null)
        {
            return new StepOutcome
            {
                State = state,
                Decision = decision,
                FeedbackHistory = [],
                Disposition = decision.Disposition == StepContinuationDisposition.RequiresApproval
                    ? StepContinuationDisposition.RequiresApproval
                    : StepContinuationDisposition.Stop,
                Message = string.IsNullOrWhiteSpace(decision.Message)
                    ? "No next action was selected for this step."
                    : decision.Message
            };
        }

        if (decision.Disposition == StepContinuationDisposition.RequiresApproval)
        {
            return new StepOutcome
            {
                State = state,
                Decision = decision,
                FeedbackHistory = [],
                Disposition = StepContinuationDisposition.RequiresApproval,
                Message = string.IsNullOrWhiteSpace(decision.Message)
                    ? "Step execution requires approval before it can continue."
                    : decision.Message
            };
        }

        var safetyDecision = await EvaluateStepSafetyAsync(state, decision, cancellationToken);
        if (safetyDecision?.Disposition == SafetyDisposition.Denied)
        {
            return new StepOutcome
            {
                State = state,
                Decision = decision,
                FeedbackHistory = [],
                SafetyDecision = safetyDecision,
                Disposition = StepContinuationDisposition.Stop,
                Message = safetyDecision.Reason
            };
        }

        if (safetyDecision?.Disposition == SafetyDisposition.RequiresApproval)
        {
            return new StepOutcome
            {
                State = state,
                Decision = decision,
                FeedbackHistory = [],
                SafetyDecision = safetyDecision,
                Disposition = StepContinuationDisposition.RequiresApproval,
                Message = string.IsNullOrWhiteSpace(safetyDecision.Reason)
                    ? "Step execution requires approval before it can continue."
                    : safetyDecision.Reason
            };
        }

        var capability = _capabilityRegistry.FindByName(decision.NextAction.CapabilityName);
        if (capability is null)
        {
            return new StepOutcome
            {
                State = state,
                Decision = decision,
                FeedbackHistory = [],
                Disposition = StepContinuationDisposition.Stop,
                Message = $"Capability '{decision.NextAction.CapabilityName}' is not available for this step."
            };
        }

        if (!capability.CanHandle(decision.NextAction, state.ExecutionContext))
        {
            return new StepOutcome
            {
                State = state,
                Decision = decision,
                FeedbackHistory = [],
                Disposition = StepContinuationDisposition.Stop,
                Message = $"Capability '{capability.Name}' cannot handle the requested action '{decision.NextAction.ActionName}'."
            };
        }

        var executionResult = await capability.ExecuteAsync(decision.NextAction, state.ExecutionContext, cancellationToken);

        ObservationSnapshot? refreshedObservation = null;
        var observationRefreshStatus = "captured";
        try
        {
            refreshedObservation = await _observationProvider.CaptureAsync(cancellationToken);
            if (refreshedObservation is null)
            {
                observationRefreshStatus = "unavailable";
            }
        }
        catch
        {
            observationRefreshStatus = "capture-failed";
        }

        var feedback = new StepFeedback
        {
            ExecutedAction = decision.NextAction,
            ExecutionResult = executionResult,
            RefreshedObservation = refreshedObservation,
            ObservationRefreshStatus = observationRefreshStatus
        };

        var updatedObservation = refreshedObservation ?? state.CurrentObservation ?? state.ExecutionContext.Observation;
        var updatedContext = UpdateExecutionContextObservation(state.ExecutionContext, updatedObservation);
        var updatedState = new AgentStepState
        {
            ExecutionContext = updatedContext,
            CurrentObservation = updatedObservation,
            LastAction = decision.NextAction,
            LastResult = executionResult,
            StepIndex = state.StepIndex + 1
        };

        var disposition = ShouldContinue(executionResult)
            ? StepContinuationDisposition.Continue
            : StepContinuationDisposition.Stop;

        return new StepOutcome
        {
            State = updatedState,
            Decision = decision,
            Feedback = feedback,
            FeedbackHistory = [feedback],
            Disposition = disposition,
            Message = string.IsNullOrWhiteSpace(executionResult.Message)
                ? "Step executed."
                : executionResult.Message
        };
    }

    private static StepOutcome FinalizePathOutcome(
        StepOutcome outcome,
        IReadOnlyList<StepFeedback> feedbackHistory,
        StepContinuationDisposition disposition,
        string message)
    {
        return new StepOutcome
        {
            State = outcome.State,
            Decision = outcome.Decision,
            Feedback = outcome.Feedback,
            FeedbackHistory = feedbackHistory,
            SafetyDecision = outcome.SafetyDecision,
            Disposition = disposition,
            Message = message
        };
    }

    private static string BuildAppendedMessage(string aggregatedMessage, string nextStepMessage)
    {
        if (string.IsNullOrWhiteSpace(aggregatedMessage))
        {
            return nextStepMessage;
        }

        if (string.IsNullOrWhiteSpace(nextStepMessage))
        {
            return aggregatedMessage;
        }

        return $"{aggregatedMessage} Follow-up step result: {nextStepMessage}";
    }

    private static bool ShouldContinue(ActionExecutionResult executionResult)
    {
        if (executionResult.Verification is not null &&
            executionResult.Verification.Status != VerificationStatus.Verified)
        {
            return false;
        }

        return executionResult.Status is ExecutionStatus.Succeeded
            or ExecutionStatus.Attempted
            or ExecutionStatus.PartiallySucceeded;
    }

    private static AgentExecutionContext UpdateExecutionContextObservation(
        AgentExecutionContext executionContext,
        ObservationSnapshot? observation)
    {
        return new AgentExecutionContext
        {
            CorrelationId = executionContext.CorrelationId,
            RawInput = executionContext.RawInput,
            NormalizedInput = executionContext.NormalizedInput,
            CreatedAtUtc = executionContext.CreatedAtUtc,
            SessionId = executionContext.SessionId,
            Observation = observation,
            ContextAdapter = executionContext.ContextAdapter,
            PrimaryTargetGrounding = executionContext.PrimaryTargetGrounding,
            ResolvedTargets = executionContext.ResolvedTargets,
            Metadata = executionContext.Metadata
        };
    }

    private async Task<StepSafetyDecision?> EvaluateStepSafetyAsync(
        AgentStepState state,
        StepDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.NextAction is null)
        {
            return null;
        }

        if (_stepSafetyEvaluator is null)
        {
            return new StepSafetyDecision
            {
                Disposition = SafetyDisposition.Allowed,
                RiskLevel = SafetyRiskLevel.Low,
                Reason = "Step safety evaluator unavailable. Defaulted to allow."
            };
        }

        try
        {
            var safetyDecision = await _stepSafetyEvaluator.EvaluateAsync(
                new StepSafetyRequest
                {
                    Source = StepSafetyRequestSource.StepRuntime,
                    CommandText = state.ExecutionContext.RawInput,
                    StepIndex = state.StepIndex + 1,
                    AgentAction = decision.NextAction
                },
                cancellationToken);

            return safetyDecision ??
                   FailClosedStepSafetyDecision("Blocked by policy: step safety evaluator returned no decision (fail-closed).");
        }
        catch
        {
            return FailClosedStepSafetyDecision("Blocked by policy: step safety evaluation failed (fail-closed).");
        }
    }

    private static StepSafetyDecision FailClosedStepSafetyDecision(string reason)
    {
        return new StepSafetyDecision
        {
            Disposition = SafetyDisposition.Denied,
            RiskLevel = SafetyRiskLevel.High,
            Reason = reason
        };
    }
}
