namespace WindowsAiAssistant.Runtime.Logging;

public sealed class AgentRunLog
{
    public required string RunId { get; init; }
    public int StepIndex { get; init; }
    public required string UserGoal { get; init; }
    public string? LlmRawOutput { get; init; }
    public string? ObservationSummaryJson { get; init; }
    public string? ParsedDecisionJson { get; init; }
    public string? ActionResultJson { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
