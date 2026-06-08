using WindowsAiAssistant.Agent.Planning;

namespace WindowsAiAssistant.Agent;

public sealed class AgentStepProgress
{
    public int StepIndex { get; init; }
    public int MaxSteps { get; init; }
    public required string Phase { get; init; }
    public string? Detail { get; init; }
    public string? PlanSummary { get; init; }
    public string? PlanHeadline { get; init; }
    public IReadOnlyList<PlanStepDisplayLine>? PlanSteps { get; init; }
    public string? PlanProgressLine { get; init; }
    public string? SkillDomain { get; init; }
    public int? PlanRevision { get; init; }
}
