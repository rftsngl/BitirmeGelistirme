using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Contracts.Models;

public enum DecisionRuntimeCompletionState
{
    Running = 0,
    Terminal = 1
}

public enum DecisionRuntimeBlockedState
{
    None = 0,
    Blocked = 1,
    Retrying = 2,
    RetryLimitReached = 3
}

public enum DecisionRuntimeOutcome
{
    InProgress = 0,
    Completed = 1,
    Blocked = 2,
    RetryableFailure = 3,
    Aborted = 4
}

public enum DecisionRuntimeTerminationReason
{
    None = 0,
    GoalCompleted = 1,
    NonRetryableFailure = 2,
    RetryLimitReached = 3,
    AwaitingApproval = 4,
    HardStop = 5,
    MaxStepLimitReached = 6,
    FatalException = 7,
    ApprovalRejected = 8,
    SafetyDenied = 9
}

public enum RuntimeSessionCompletionState
{
    InProgress = 0,
    Completed = 1,
    Blocked = 2,
    Aborted = 3
}

public enum DecisionLoopTerminalState
{
    None = 0,
    Completed = 1,
    Blocked = 2,
    AwaitingApproval = 3,
    Aborted = 4,
    MaxStepsReached = 5,
    Failed = 6
}

public enum DecisionLoopTransition
{
    Unknown = 0,
    Continue = 1,
    RefreshObservation = 2,
    RetryExecution = 3,
    AwaitApproval = 4,
    Stop = 5,
    Terminal = 6
}

public sealed class DecisionCycleStepRecord
{
    public int StepIndex { get; init; }
    public DecisionKind DecisionKind { get; init; } = DecisionKind.Stop;
    public ActionType? ActionType { get; init; }
    public string? TargetSummary { get; init; }
    public DecisionExecutionOutcome ExecutionOutcome { get; init; } = DecisionExecutionOutcome.Unknown;
    public DecisionLoopTransition Transition { get; init; } = DecisionLoopTransition.Unknown;
    public DecisionLoopTerminalState TerminalState { get; init; } = DecisionLoopTerminalState.None;
    public string? Reason { get; init; }
}

public sealed class RuntimeGoalState
{
    public string GoalId { get; init; } = Guid.NewGuid().ToString("N");
    public string Description { get; init; } = string.Empty;
}

public sealed class RuntimeStepState
{
    public int StepIndex { get; init; }
    public DecisionKind DecisionKind { get; init; } = DecisionKind.Stop;
    public ActionType? ActionType { get; init; }
    public string? TargetReference { get; init; }
    public string? Summary { get; init; }
}

public sealed class RuntimeStepResultState
{
    public DecisionExecutionOutcome Outcome { get; init; } = DecisionExecutionOutcome.Unknown;
    public bool Succeeded { get; init; }
    public bool Failed { get; init; }
    public bool Blocked { get; init; }
    public string? Message { get; init; }
    public string? ProducedTarget { get; init; }
}

public sealed class RuntimeSessionState
{
    public string RuntimeSessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string OriginalCommand { get; set; } = string.Empty;
    public RuntimeGoalState CurrentGoal { get; set; } = new();
    public RuntimeStepState? CurrentStep { get; set; }
    public RuntimeStepResultState? LastStepResult { get; set; }
    public int RetryCount { get; set; }
    public int BlockedCount { get; set; }
    public RuntimeSessionCompletionState CompletionState { get; set; } = RuntimeSessionCompletionState.InProgress;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DecisionCycleRuntimeState
{
    public string RuntimeId { get; set; } = Guid.NewGuid().ToString("N");
    public string OriginalUserRequest { get; set; } = string.Empty;
    public int CurrentStepIndex { get; set; }
    public int MaxStepLimit { get; set; } = 1;
    public NextActionDecision? CurrentDecision { get; set; }
    public ExecuteActionPayload? LastExecutableAction { get; set; }
    public DecisionExecutionFeedback? LastExecutionFeedback { get; set; }
    public string? LastVerificationSummary { get; set; }
    public VerificationStatus? LastVerificationStatus { get; set; }
    public DecisionVerificationKind LastVerificationKind { get; set; } = DecisionVerificationKind.Unknown;
    public DecisionVerificationOutcome LastVerificationOutcome { get; set; } = DecisionVerificationOutcome.Unknown;
    public string? LastVerificationReason { get; set; }
    public string? LastVerificationTarget { get; set; }
    public DateTimeOffset? LastVerificationAtUtc { get; set; }
    public string? LastObservationSummary { get; set; }
    public bool AwaitingApproval { get; set; }
    public DecisionRuntimeCompletionState CompletionState { get; set; } = DecisionRuntimeCompletionState.Running;
    public DecisionLoopTerminalState TerminalState { get; set; } = DecisionLoopTerminalState.None;
    public DecisionRuntimeBlockedState BlockedState { get; set; } = DecisionRuntimeBlockedState.None;
    public int RetryCount { get; set; }
    public int MaxRetryLimit { get; set; } = 1;
    public bool GoalStillActive { get; set; }
    public DecisionRuntimeOutcome Outcome { get; set; } = DecisionRuntimeOutcome.InProgress;
    public DecisionRuntimeTerminationReason TerminationReason { get; set; } = DecisionRuntimeTerminationReason.None;
    public string StepHistorySummary { get; set; } = string.Empty;
    public IReadOnlyList<DecisionCycleStepRecord> StepHistory { get; set; } = [];
    public RuntimeSessionState RuntimeSession { get; set; } = new();
}
