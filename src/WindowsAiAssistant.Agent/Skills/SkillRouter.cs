using System.Text;
using WindowsAiAssistant.Agent.Planning;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Skills;

public sealed class SkillRouter
{
    private readonly IReadOnlyList<IWorkflowSkill> _skills;

    public SkillRouter()
    {
        _skills =
        [
            new ConversationSkill(),
            new IntegrationSkill(),
            new OfficeSkill(),
            new WindowSkill(),
            new GenericDesktopSkill()
        ];
    }

    internal WorkflowSkillDomain ResolveDomain(string userGoal, DesktopObservation observation, ExecutionPlan? plan)
    {
        if (plan is not null && TryParsePlanDomain(plan.Domain, out var fromPlan))
        {
            return fromPlan;
        }

        foreach (var skill in _skills)
        {
            if (skill.Domain != WorkflowSkillDomain.GenericDesktop && skill.CanHandle(userGoal, observation, plan))
            {
                return skill.Domain;
            }
        }

        return WorkflowSkillDomain.GenericDesktop;
    }

    internal void AppendSkillHints(
        StringBuilder builder,
        WorkflowSkillDomain domain,
        string userGoal,
        DesktopObservation observation,
        ExecutionPlan? plan)
    {
        var skill = _skills.FirstOrDefault(item => item.Domain == domain) ?? _skills[^1];
        skill.AppendHints(builder, userGoal, observation, plan);
    }

    private static bool TryParsePlanDomain(string domain, out WorkflowSkillDomain parsed)
    {
        parsed = WorkflowSkillDomain.GenericDesktop;
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        var normalized = domain.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized switch
        {
            "conversation" => Set(WorkflowSkillDomain.Conversation, out parsed),
            "integration" or "system" => Set(WorkflowSkillDomain.Integration, out parsed),
            "office" or "word" or "excel" => Set(WorkflowSkillDomain.Office, out parsed),
            "window" or "windows" => Set(WorkflowSkillDomain.Window, out parsed),
            _ => false
        };
    }

    private static bool Set(WorkflowSkillDomain value, out WorkflowSkillDomain parsed)
    {
        parsed = value;
        return true;
    }
}
