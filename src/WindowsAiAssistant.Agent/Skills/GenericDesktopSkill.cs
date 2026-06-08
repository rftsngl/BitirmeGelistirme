using System.Text;
using WindowsAiAssistant.Agent.Planning;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Skills;

internal sealed class GenericDesktopSkill : IWorkflowSkill
{
    public WorkflowSkillDomain Domain => WorkflowSkillDomain.GenericDesktop;

    public bool CanHandle(string userGoal, DesktopObservation observation, ExecutionPlan? plan) => true;

    public void AppendHints(StringBuilder builder, string userGoal, DesktopObservation observation, ExecutionPlan? plan)
    {
        _ = userGoal;
        _ = plan;
        builder.AppendLine("SKILL — GENERIC DESKTOP (sub-agent focus):");
        builder.AppendLine("- Follow TOOL PRIORITY: integrations → launch → COM/shortcut → shell → UI last.");
        builder.AppendLine("- Reuse open windows; one action per step; verify via lastActionResult.");
        if (observation.Windows.Count > 0)
        {
            builder.AppendLine($"- {observation.Windows.Count} visible window(s) available for focus_window.");
        }

        builder.AppendLine();
    }
}
