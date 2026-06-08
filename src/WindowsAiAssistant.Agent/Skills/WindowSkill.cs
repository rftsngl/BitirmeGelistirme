using System.Text;
using WindowsAiAssistant.Agent.Dispatch;
using WindowsAiAssistant.Agent.Planning;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Skills;

internal sealed class WindowSkill : IWorkflowSkill
{
    public WorkflowSkillDomain Domain => WorkflowSkillDomain.Window;

    public bool CanHandle(string userGoal, DesktopObservation observation, ExecutionPlan? plan)
    {
        if (plan is not null && plan.Domain.Contains("window", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var text = FastPathGuard.Normalize(userGoal);
        return FastPathGuard.MatchesAny(text,
            "acik pencere", "açık pencere", "pencere list", "pencere ozet", "pencere özet",
            "hangi pencere", "visible window", "list_windows") ||
               (text.Contains("pencere", StringComparison.Ordinal) && text.Contains("ozet", StringComparison.Ordinal));
    }

    public void AppendHints(StringBuilder builder, string userGoal, DesktopObservation observation, ExecutionPlan? plan)
    {
        _ = userGoal;
        _ = plan;
        builder.AppendLine("SKILL — WINDOW (sub-agent focus):");
        builder.AppendLine("- Use list_windows OR summarize from visibleWindows in observation.");
        builder.AppendLine("- Do NOT click_element to list windows.");
        if (observation.Windows.Count > 0)
        {
            builder.AppendLine($"- visibleWindows already has {observation.Windows.Count} entries.");
        }

        builder.AppendLine();
    }
}
