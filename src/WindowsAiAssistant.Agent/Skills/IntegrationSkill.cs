using System.Text;
using WindowsAiAssistant.Agent.Planning;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Skills;

internal sealed class IntegrationSkill : IWorkflowSkill
{
    public WorkflowSkillDomain Domain => WorkflowSkillDomain.Integration;

    public bool CanHandle(string userGoal, DesktopObservation observation, ExecutionPlan? plan)
    {
        _ = observation;
        return GoalRoutingHints.TryBuildFastDecision(userGoal) is not null ||
               (plan is not null && plan.Domain.Contains("integration", StringComparison.OrdinalIgnoreCase));
    }

    public void AppendHints(StringBuilder builder, string userGoal, DesktopObservation observation, ExecutionPlan? plan)
    {
        _ = observation;
        _ = plan;
        builder.AppendLine("SKILL — INTEGRATION (sub-agent focus):");
        builder.AppendLine("- Use P1 backend actions (audio_power, network_status, perf_counter, clipboard, shell, ...).");
        builder.AppendLine("- Do NOT simulate system tasks via UI clicks.");
        if (GoalRoutingHints.TryBuildFastDecision(userGoal) is { } fast)
        {
            builder.AppendLine($"- Strong option detected: {fast.Action}.");
        }

        builder.AppendLine();
    }
}
