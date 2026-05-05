namespace WindowsAiAssistant.Contracts.Models;

public sealed class RecentCommandSummary
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string CommandText { get; init; } = string.Empty;
    public string FinalStatus { get; init; } = string.Empty;
    public string SafetyDisposition { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public string SelectedTool { get; init; } = string.Empty;
    public string ExecutionMode { get; init; } = string.Empty;
    public string ApprovalDecision { get; init; } = string.Empty;
    public string StepExecutionState { get; init; } = string.Empty;
    public string StepVerificationState { get; init; } = string.Empty;
    public string RuntimeClosureState { get; init; } = string.Empty;
    public string RuntimeTerminalState { get; init; } = string.Empty;
    public string RuntimeTerminationReason { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
