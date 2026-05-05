using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Core;

public static class AiDecisionInputBuilder
{
    public static AiDecisionInput Build(CommandRequest request, DecisionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(summary);

        return new AiDecisionInput
        {
            CorrelationId = request.CorrelationId,
            RawInput = request.UserInput,
            NormalizedInput = request.UserInput.Trim().ToLowerInvariant(),
            Source = summary.Source,
            ActiveProcessName = summary.ActiveProcessName,
            ActiveWindowTitle = summary.ActiveWindowTitle,
            ContextAdapterName = summary.ContextAdapterName,
            ResolvedTargetCount = summary.ResolvedTargetCount,
            PrimaryTargetKind = summary.PrimaryTargetKind,
            PrimaryTargetReason = summary.PrimaryTargetReason,
            HasContextualTarget = summary.HasContextualTarget,
            HasAdapterContext = summary.HasAdapterContext,
            ContextProvenanceConsistent = summary.ContextProvenanceConsistent,
            ContextProvenanceSource = summary.ContextProvenanceSource,
            RuntimeId = summary.RuntimeId,
            CurrentStepIndex = summary.CurrentStepIndex,
            MaxStepLimit = summary.MaxStepLimit,
            PreviousDecisionKind = summary.PreviousDecisionKind,
            PreviousActionType = summary.PreviousActionType,
            PreviousDecisionExecuted = summary.PreviousDecisionExecuted,
            PreviousExecutionOutcome = summary.PreviousExecutionOutcome,
            GoalStillActive = summary.GoalStillActive,
            StepHistorySummary = summary.StepHistorySummary,
            RuntimeTerminalState = summary.RuntimeTerminalState,
            LastExecutionFeedback = summary.LastExecutionFeedback
        };
    }
}
