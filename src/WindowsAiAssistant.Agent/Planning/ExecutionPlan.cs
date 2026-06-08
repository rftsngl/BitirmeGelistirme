namespace WindowsAiAssistant.Agent.Planning;

public sealed class ExecutionPlan
{
    public string GoalType { get; init; } = "desktop_multi";
    public string Domain { get; init; } = "generic_desktop";
    public string Skill { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Preconditions { get; init; } = [];
    public IReadOnlyList<ExecutionPlanStep> Steps { get; init; } = [];
    public IReadOnlyList<string> AntiPatterns { get; init; } = [];
    public int EstimatedSteps { get; init; }
}

public sealed class ExecutionPlanStep
{
    public int Order { get; init; }
    public string Intent { get; init; } = string.Empty;
    public IReadOnlyList<string> PreferredActions { get; init; } = [];
    public string SuccessCheck { get; init; } = string.Empty;
}
