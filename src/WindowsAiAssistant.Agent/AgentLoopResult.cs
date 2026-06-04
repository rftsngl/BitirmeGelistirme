namespace WindowsAiAssistant.Agent;

public sealed class AgentLoopResult
{
    public required AgentSession Session { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AssistantMessage { get; init; }
    public bool ReachedMaxSteps { get; init; }
    public string? ObservationSummary { get; init; }
    public string? LogFilePath { get; init; }
}
