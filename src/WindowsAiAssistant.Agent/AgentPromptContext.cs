using WindowsAiAssistant.Agent.Planning;

namespace WindowsAiAssistant.Agent;

public sealed class AgentPromptContext
{
    public ExecutionPlan? ExecutionPlan { get; init; }
    public int CurrentPlanStepIndex { get; init; }
    public WorkflowSkillDomain SkillDomain { get; init; } = WorkflowSkillDomain.GenericDesktop;
}
