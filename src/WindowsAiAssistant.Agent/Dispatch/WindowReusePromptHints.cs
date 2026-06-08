using System.Text;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

internal static class WindowReusePromptHints
{
    internal static void Append(StringBuilder builder, DesktopObservation observation)
    {
        if (observation.Windows.Count == 0)
        {
            return;
        }

        builder.AppendLine("SUGGESTED — WINDOW REUSE (you decide):");
        builder.AppendLine("- BEFORE open_app or launch: scan visibleWindows in observation.");
        builder.AppendLine("- If the target app already has a window listed, use focus_window on that windowId/title — do NOT open_app again.");
        builder.AppendLine("- After open_app succeeds once, continue in the SAME app window (focus_window if focus drifted); do not spawn duplicate instances.");
        builder.AppendLine("- Multi-step goals (A sonra B): reuse the window you already opened for step B unless the user asked for a new instance.");
        builder.AppendLine();
    }
}
