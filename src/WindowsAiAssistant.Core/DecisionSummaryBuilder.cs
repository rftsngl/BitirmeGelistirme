using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Core;

public static class DecisionSummaryBuilder
{
    public static DecisionSummary Build(DecisionInputBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var primaryTarget = bundle.ResolvedTargets.Count > 0 ? bundle.ResolvedTargets[0] : null;
        var hasAdapterContext = bundle.ContextAdapter is not null &&
                                !string.IsNullOrWhiteSpace(bundle.ContextAdapter.AdapterName);

        var activeProcessName = !string.IsNullOrWhiteSpace(bundle.Observation?.ActiveProcessName)
            ? bundle.Observation!.ActiveProcessName
            : bundle.ContextAdapter?.ProcessName;

        var activeWindowTitle = !string.IsNullOrWhiteSpace(bundle.Observation?.ActiveWindow?.Title)
            ? bundle.Observation!.ActiveWindow!.Title
            : bundle.ContextAdapter?.WindowTitle;

        var runtimeCycleState = bundle.RuntimeCycleState;
        var previousDecisionKind = runtimeCycleState?.CurrentDecision?.Kind;
        var previousActionType = runtimeCycleState?.CurrentDecision?.ExecuteAction?.ActionType ??
                                 runtimeCycleState?.LastExecutableAction?.ActionType;
        var previousExecutionOutcome = runtimeCycleState?.LastExecutionFeedback?.NormalizedOutcome;
        var previousDecisionExecuted = runtimeCycleState?.LastExecutionFeedback is not null &&
                                       previousExecutionOutcome != DecisionExecutionOutcome.NotExecuted;
        DecisionLoopTerminalState? runtimeTerminalState = runtimeCycleState is null || runtimeCycleState.TerminalState == DecisionLoopTerminalState.None
            ? null
            : runtimeCycleState.TerminalState;

        return new DecisionSummary
        {
            Source = bundle.Source,
            ActiveProcessName = activeProcessName,
            ActiveWindowTitle = activeWindowTitle,
            ContextAdapterName = hasAdapterContext ? bundle.ContextAdapter!.AdapterName : null,
            ResolvedTargetCount = bundle.ResolvedTargets.Count,
            PrimaryTargetKind = primaryTarget?.Kind,
            PrimaryTargetReason = primaryTarget?.ResolutionReasonKind,
            HasContextualTarget = bundle.ResolvedTargets.Any(t =>
                t.ResolutionReasonKind != TargetResolutionReasonKind.Unknown),
            HasAdapterContext = hasAdapterContext,
            ContextProvenanceConsistent = bundle.ContextProvenanceConsistent,
            ContextProvenanceSource = bundle.ContextProvenanceSource,
            RuntimeId = runtimeCycleState?.RuntimeId,
            CurrentStepIndex = runtimeCycleState?.CurrentStepIndex ?? 0,
            MaxStepLimit = runtimeCycleState?.MaxStepLimit ?? 0,
            PreviousDecisionKind = previousDecisionKind,
            PreviousActionType = previousActionType,
            PreviousDecisionExecuted = previousDecisionExecuted,
            PreviousExecutionOutcome = previousExecutionOutcome,
            GoalStillActive = runtimeCycleState?.GoalStillActive ?? false,
            StepHistorySummary = runtimeCycleState?.StepHistorySummary,
            RuntimeTerminalState = runtimeTerminalState,
            LastExecutionFeedback = runtimeCycleState?.LastExecutionFeedback
        };
    }
}
