using System.Text;
using WindowsAiAssistant.Agent.Dispatch;
using WindowsAiAssistant.Agent.Planning;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Skills;

internal sealed class OfficeSkill : IWorkflowSkill
{
    public WorkflowSkillDomain Domain => WorkflowSkillDomain.Office;

    public bool CanHandle(string userGoal, DesktopObservation observation, ExecutionPlan? plan)
    {
        if (plan is not null &&
            (plan.Domain.Contains("office", StringComparison.OrdinalIgnoreCase) ||
             plan.Skill.Contains("word", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var text = FastPathGuard.Normalize(userGoal);
        return FastPathGuard.MatchesAny(text,
            "word", "winword", "excel", "powerpoint", "belge", "belgesi", "bos belge", "boş belge", "baslik", "başlık");
    }

    public void AppendHints(StringBuilder builder, string userGoal, DesktopObservation observation, ExecutionPlan? plan)
    {
        _ = userGoal;
        _ = plan;
        builder.AppendLine("SKILL — OFFICE (sub-agent focus):");
        var hasWord = observation.Windows.Any(window =>
            window.ProcessName.Equals("WINWORD", StringComparison.OrdinalIgnoreCase));
        if (hasWord)
        {
            builder.AppendLine("- Word is open: focus_window first; com_invoke Documents.Add or Ctrl+N for new doc.");
        }
        else
        {
            builder.AppendLine("- Word not visible: open_app word, wait, then Documents.Add or Ctrl+N.");
        }

        builder.AppendLine("- Avoid start-screen click_element when COM/shortcut works.");
        builder.AppendLine("- Do not open a second blank document in the same run.");
        builder.AppendLine();
    }
}
