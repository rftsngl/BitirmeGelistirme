using System.Text;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

internal static class PlaybookPromptHints
{
    internal static void Append(StringBuilder builder, string userGoal, DesktopObservation observation)
    {
        if (NewDocumentPlaybook.TryResolve(userGoal, observation) is not null)
        {
            builder.AppendLine("SUGGESTED — NEW DOCUMENT GOAL (you decide):");
            builder.AppendLine("- Strong option: press_shortcut with target Ctrl+N (or com_invoke when progId is known).");
            builder.AppendLine("- Do NOT click ribbon/menu New buttons when Ctrl+N applies.");
            builder.AppendLine();
        }

        if (SaveDocumentPlaybook.TryResolve(userGoal, observation) is not null)
        {
            builder.AppendLine("SUGGESTED — SAVE DOCUMENT GOAL (you decide):");
            builder.AppendLine("- Strong option: press_shortcut with target Ctrl+S.");
            builder.AppendLine("- Do NOT navigate Save dialogs via click_element unless Ctrl+S failed.");
            builder.AppendLine();
        }

        if (ClipboardReadPlaybook.TryResolve(userGoal, observation) is not null)
        {
            builder.AppendLine("SUGGESTED — CLIPBOARD READ GOAL (you decide):");
            builder.AppendLine("- Strong option: clipboard with parameters.mode=read.");
            builder.AppendLine("- Do NOT open apps or use UI to read clipboard.");
            builder.AppendLine();
        }

        if (SelectAllPlaybook.TryResolve(userGoal, observation) is not null)
        {
            builder.AppendLine("SUGGESTED — SELECT ALL GOAL (you decide):");
            builder.AppendLine("- Strong option: press_shortcut with target Ctrl+A.");
            builder.AppendLine("- Do NOT use mouse_drag unless Ctrl+A is unavailable in the focused control.");
            builder.AppendLine();
        }

        SelectTextPromptHints.AppendIfRelevant(builder, userGoal, observation);

        WindowReusePromptHints.Append(builder, observation);
        OfficeWorkflowPlaybook.AppendIfRelevant(builder, userGoal, observation);
    }
}
