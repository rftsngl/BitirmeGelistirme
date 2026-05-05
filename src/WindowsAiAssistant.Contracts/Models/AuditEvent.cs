namespace WindowsAiAssistant.Contracts.Models;

public sealed class AuditEvent
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string CorrelationId { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string CommandText { get; init; } = string.Empty;
    public string SafetyDisposition { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public string SelectedTool { get; init; } = string.Empty;
    public string ExecutionMode { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ApprovalDecision { get; init; } = string.Empty;
    public IDictionary<string, string>? Metadata { get; init; }
}
