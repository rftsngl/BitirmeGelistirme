using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Contracts.Models;

public sealed class CommandResult
{
    public string Status { get; init; } = "Denied";
    public string Safety { get; init; } = "Restricted";
    public string RiskLevel { get; init; } = "High";
    public string Decision { get; init; } = string.Empty;
    public string SelectedTool { get; init; } = string.Empty;
    public string ToolExecutionResult { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public VerificationStatus? VerificationStatus { get; init; }
    public string? VerificationReason { get; init; }
    public PendingApprovalSnapshot? PendingApprovalSnapshot { get; init; }
    public string? RuntimeId { get; init; }
    public int RuntimeStepCount { get; init; }
    public DecisionLoopTerminalState? RuntimeTerminalState { get; init; }
    public DecisionCycleRuntimeState? DecisionCycleState { get; init; }
}
