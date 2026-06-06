namespace WindowsAiAssistant.Agent;

public enum AgentErrorKind
{
    None,
    Provider,
    Decision,
    Action,
    Unexpected
}

public sealed class AgentLoopResult
{
    public required AgentSession Session { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public AgentErrorKind ErrorKind { get; init; } = AgentErrorKind.None;
    public string? AssistantMessage { get; init; }
    public bool ReachedMaxSteps { get; init; }
    public string? ObservationSummary { get; init; }
    public string? LogFilePath { get; init; }
}
