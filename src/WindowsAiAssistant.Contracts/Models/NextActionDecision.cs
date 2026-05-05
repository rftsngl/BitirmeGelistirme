namespace WindowsAiAssistant.Contracts.Models;

public enum DecisionKind
{
    ExecuteAction = 0,
    AskObserve = 1,
    AskApproval = 2,
    Retry = 3,
    Stop = 4
}

public enum ActionType
{
    Unknown = 0,
    Launch = 1,
    Focus = 2,
    InputText = 3,
    Confirm = 4,
    Cancel = 5,
    Navigate = 6,
    PressKey = 7,
    PressShortcut = 8,
    Verify = 9,
    OpenFile = 10
}

public enum ActionTargetKind
{
    Unknown = 0,
    Application = 1,
    Process = 2,
    Window = 3,
    File = 4,
    Path = 5,
    Element = 6,
    Url = 7,
    Service = 8,
    Generic = 9
}

public enum StopDisposition
{
    Completed = 0,
    Blocked = 1,
    Aborted = 2
}

public sealed class ActionTarget
{
    public ActionTargetKind Kind { get; init; } = ActionTargetKind.Unknown;
    public string Reference { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public IDictionary<string, string>? Metadata { get; init; }
}

public sealed class ActionParameters
{
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class ExecuteActionPayload
{
    public ActionType ActionType { get; init; } = ActionType.Unknown;
    public ActionTarget Target { get; init; } = new();
    public ActionParameters Parameters { get; init; } = new();
    public string? CapabilityHint { get; init; }
    public string? RetryHint { get; init; }
}

public sealed class AskObservePayload
{
    public string ObservationRequest { get; init; } = string.Empty;
    public string? ObservationHint { get; init; }
}

public sealed class AskApprovalPayload
{
    public ExecuteActionPayload? ProposedAction { get; init; }
    public string? RequiresApprovalReason { get; init; }
}

public sealed class RetryPayload
{
    public ExecuteActionPayload? Action { get; init; }
    public string? RetryReason { get; init; }
    public int? RetryCountHint { get; init; }
}

public sealed class StopPayload
{
    public StopDisposition Disposition { get; init; } = StopDisposition.Blocked;
    public string? StopReason { get; init; }
}

public sealed class NextActionDecision
{
    public DecisionKind Kind { get; init; } = DecisionKind.Stop;
    public ExecuteActionPayload? ExecuteAction { get; init; }
    public AskObservePayload? AskObserve { get; init; }
    public AskApprovalPayload? AskApproval { get; init; }
    public RetryPayload? Retry { get; init; }
    public StopPayload? Stop { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Rationale { get; init; }
    public double? Confidence { get; init; }
    public string? RequiresApprovalReason { get; init; }
    public string? RetryReason { get; init; }
    public string? StopReason { get; init; }
    public IDictionary<string, string>? Metadata { get; init; }
}