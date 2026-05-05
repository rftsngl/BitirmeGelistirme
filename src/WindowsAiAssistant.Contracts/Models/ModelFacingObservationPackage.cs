using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Contracts.Models;

public enum ModelSignalAvailability
{
    Unknown,
    NotAvailable,
    Partial,
    Available
}

public enum ModelTargetPresence
{
    Unknown,
    NoTarget,
    HasTarget
}

public enum ModelTargetGroundingStatus
{
    Unknown,
    NotEvaluated,
    Resolved,
    Unresolved,
    Unsupported,
    Ambiguous
}

public enum ModelExecutionOutcome
{
    Unknown,
    NotExecuted,
    InProgress,
    Completed,
    Blocked,
    Failed
}

public sealed class ModelFacingObservationPackage
{
    public string CorrelationId { get; init; } = string.Empty;
    public string CommandText { get; init; } = string.Empty;

    public ModelCommandObservation Command { get; init; } = new();
    public ModelTargetObservation Target { get; init; } = new();
    public ModelContextObservation Context { get; init; } = new();
    public ModelLastExecutionObservation LastExecution { get; init; } = new();
    public ModelDecisionCycleObservation DecisionCycle { get; init; } = new();
    public ModelSafetyObservation Safety { get; init; } = new();

    public string? IntentSummary { get; init; }
    public string? TargetSummary { get; init; }
    public string? ContextSummary { get; init; }
    public string? LastExecutionOrStepSummary { get; init; }
    public string? SafetyOrApprovalContextSummary { get; init; }
    public IReadOnlyDictionary<string, string> RelevantFlags { get; init; } = EmptyRelevantFlags.Instance;

    private static class EmptyRelevantFlags
    {
        public static readonly IReadOnlyDictionary<string, string> Instance =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ModelCommandObservation
{
    public string? NormalizedInput { get; init; }
    public string? IntentSummary { get; init; }
    public bool HasAiDecisionInput { get; init; }
}

public sealed class ModelTargetObservation
{
    public ModelTargetPresence Presence { get; init; } = ModelTargetPresence.Unknown;
    public ModelTargetGroundingStatus GroundingStatus { get; init; } = ModelTargetGroundingStatus.Unknown;
    public int ResolvedTargetCount { get; init; }
    public TargetKind? PrimaryTargetKind { get; init; }
    public TargetResolutionReasonKind? PrimaryTargetReason { get; init; }
    public GroundedTargetKind? GroundedTargetKind { get; init; }
    public string? CanonicalValue { get; init; }
}

public sealed class ModelContextObservation
{
    public ModelSignalAvailability Availability { get; init; } = ModelSignalAvailability.Unknown;
    public string? ActiveProcessName { get; init; }
    public string? ContextAdapterName { get; init; }
    public DecisionInputSource? DecisionInputSource { get; init; }
    public string? ContextProvenanceSource { get; init; }
    public bool HasAdapterContext { get; init; }
    public bool ContextProvenanceConsistent { get; init; }
}

public sealed class ModelLastExecutionObservation
{
    public ModelSignalAvailability RuntimeStateAvailability { get; init; } = ModelSignalAvailability.Unknown;
    public ModelExecutionOutcome Outcome { get; init; } = ModelExecutionOutcome.Unknown;
    public string? CapabilityName { get; init; }
    public string? ActionName { get; init; }
    public ExecutionStatus? ActionStatus { get; init; }
    public VerificationStatus? VerificationStatus { get; init; }
    public ModelSignalAvailability VerificationAvailability { get; init; } = ModelSignalAvailability.Unknown;
    public string? BlockedReason { get; init; }
    public string? FailureReason { get; init; }
    public bool? PrimitiveSucceeded { get; init; }
}

public sealed class ModelDecisionCycleObservation
{
    public ModelSignalAvailability Availability { get; init; } = ModelSignalAvailability.Unknown;
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

public sealed class ModelSafetyObservation
{
    public ModelSignalAvailability Availability { get; init; } = ModelSignalAvailability.Unknown;
    public SafetyDisposition? Disposition { get; init; }
    public SafetyRiskLevel? RiskLevel { get; init; }
    public bool? ApprovalRequired { get; init; }
    public bool? ApprovalPath { get; init; }
    public bool? SnapshotUsed { get; init; }
    public bool? SnapshotFallback { get; init; }
}