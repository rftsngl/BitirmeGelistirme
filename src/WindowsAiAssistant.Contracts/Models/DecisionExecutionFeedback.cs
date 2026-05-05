using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Contracts.Models;

public enum DecisionExecutionOutcome
{
    Unknown = 0,
    NotExecuted = 1,
    Succeeded = 2,
    Failed = 3,
    Blocked = 4
}

public enum DecisionFailureCategory
{
    None = 0,
    ExecutionFailed = 1,
    BlockedByPolicy = 2,
    BlockedByApproval = 3,
    RetryLimitReached = 4,
    ObservationRequired = 5,
    InvalidDecision = 6,
    Unknown = 7
}

public enum DecisionVerificationKind
{
    Unknown = 0,
    ProcessPresence = 1,
    ForegroundAlignment = 2,
    ExecutionDerived = 3,
    FileOpenPostcondition = 4,
    NavigationDestination = 5,
    TextInputPostcondition = 6,
    KeyInteractionPostcondition = 7
}

public enum DecisionVerificationOutcome
{
    Unknown = 0,
    VerifiedSuccess = 1,
    VerifiedFailure = 2,
    Inconclusive = 3,
    Unsupported = 4
}

public sealed class DecisionVerificationSummary
{
    public VerificationStatus? Status { get; init; }
    public DecisionVerificationKind Kind { get; init; } = DecisionVerificationKind.Unknown;
    public DecisionVerificationOutcome Outcome { get; init; } = DecisionVerificationOutcome.Unknown;
    public string? Summary { get; init; }
    public string? Reason { get; init; }
    public bool? TargetReached { get; init; }
    public string? TargetContext { get; init; }
    public DateTimeOffset? EvaluatedAtUtc { get; init; }
    public bool? RetryableFailure { get; init; }
}

public sealed class DecisionApprovalSummary
{
    public bool ApprovalRequired { get; init; }
    public bool ApprovalPath { get; init; }
    public bool? Approved { get; init; }
    public string? Reason { get; init; }
}

public sealed class DecisionExecutionFeedback
{
    public string ExecutedActionSummary { get; init; } = string.Empty;
    public bool ExecutionSucceeded { get; init; }
    public bool ExecutionFailed { get; init; }
    public bool ExecutionBlocked { get; init; }
    public string ExecutionMessage { get; init; } = string.Empty;
    public DecisionExecutionOutcome NormalizedOutcome { get; init; } = DecisionExecutionOutcome.Unknown;
    public string? ProducedResult { get; init; }
    public string? ProducedTarget { get; init; }
    public DecisionFailureCategory FailureCategory { get; init; } = DecisionFailureCategory.None;
    public string? BlockedReason { get; init; }
    public DecisionVerificationSummary? Verification { get; init; }
    public DecisionApprovalSummary? Approval { get; init; }
}
