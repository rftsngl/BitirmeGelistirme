using System.Text;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

internal static class SelectTextPromptHints
{
    internal static void AppendIfRelevant(StringBuilder builder, string userGoal, DesktopObservation observation)
    {
        _ = observation;

        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return;
        }

        var text = FastPathGuard.Normalize(userGoal);
        if (!FastPathGuard.MatchesAny(text,
                "metin sec", "metni sec", "select text", "kelimeyi sec", "cumleyi sec",
                "paragrafi sec", "satiri sec", "surukleyerek sec", "drag select", "mouse drag"))
        {
            return;
        }

        builder.AppendLine("SUGGESTED — TEXT SELECTION GOAL (you decide):");
        builder.AppendLine("- Prefer keyboard selection before free-coordinate mouse:");
        builder.AppendLine("  * select all: select_text mode=all OR press_shortcut Ctrl+A");
        builder.AppendLine("  * extend selection: select_text mode=extend_left|extend_right|extend_up|extend_down");
        builder.AppendLine("  * word/line: double click via mouse_click on elementId center, or Ctrl+Shift+Arrow");
        builder.AppendLine("- For range by pointer: mouse_drag with startX,startY,endX,endY (Sensitive — needs approval).");
        builder.AppendLine("- Do NOT invent coordinates when uiElements provides a text field elementId.");
        builder.AppendLine();
    }
}
