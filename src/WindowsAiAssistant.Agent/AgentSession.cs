namespace WindowsAiAssistant.Agent;

public sealed class AgentSession
{
    public required string RunId { get; init; }
    public required string UserGoal { get; init; }
    public IList<AgentStep> Steps { get; } = [];
    public bool IsComplete { get; set; }
}
