using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class AiDecisionInput
{
    public string CorrelationId { get; init; } = string.Empty;
    public string RawInput { get; init; } = string.Empty;
    public string NormalizedInput { get; init; } = string.Empty;
    public DecisionInputSource Source { get; init; } = DecisionInputSource.Live;
    public string? ActiveProcessName { get; init; }
    public string? ActiveWindowTitle { get; init; }
    public string? ContextAdapterName { get; init; }
    public int ResolvedTargetCount { get; init; }
    public TargetKind? PrimaryTargetKind { get; init; }
    public TargetResolutionReasonKind? PrimaryTargetReason { get; init; }
    public bool HasContextualTarget { get; init; }
    public bool HasAdapterContext { get; init; }
    public bool ContextProvenanceConsistent { get; init; }
    public string ContextProvenanceSource { get; init; } = "live";
    public string? RuntimeId { get; init; }
    public int CurrentStepIndex { get; init; }
    public int MaxStepLimit { get; init; }
    public DecisionKind? PreviousDecisionKind { get; init; }
    public ActionType? PreviousActionType { get; init; }
    public bool PreviousDecisionExecuted { get; init; }
    public DecisionExecutionOutcome? PreviousExecutionOutcome { get; init; }
    public bool GoalStillActive { get; init; }
    public string? StepHistorySummary { get; init; }
    public DecisionLoopTerminalState? RuntimeTerminalState { get; init; }
    public DecisionExecutionFeedback? LastExecutionFeedback { get; init; }
}
