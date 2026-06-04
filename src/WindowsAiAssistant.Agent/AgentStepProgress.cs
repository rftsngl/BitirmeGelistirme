namespace WindowsAiAssistant.Agent;

public sealed class AgentStepProgress
{
    public int StepIndex { get; init; }
    public int MaxSteps { get; init; }
    public required string Phase { get; init; }
    public string? Detail { get; init; }
}
