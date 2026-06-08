using System.Text;
using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Agent;

internal static class StrategyRecoveryPromptBuilder
{
    internal static void AppendPriorFailureHints(StringBuilder builder, IEnumerable<AgentStep> priorSteps)
    {
        var failed = priorSteps
            .OrderBy(step => step.Index)
            .LastOrDefault(step => step.ActionResult?.Success == false);

        if (failed?.ActionResult?.Message is not { Length: > 0 } message)
        {
            return;
        }

        builder.AppendLine("RECOVERY — previous step failed:");
        builder.AppendLine($"- lastActionResult: {message}");
        builder.AppendLine("- Pick a DIFFERENT tool or tier (P1 integration → shell → respond from observation).");
        builder.AppendLine("- Do NOT repeat the same failed action+target combination.");
        builder.AppendLine();

        if (message.Contains("parse", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("elementId", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Karar", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("Parse/decision recovery:");
            builder.AppendLine("- Return valid JSON only.");
            builder.AppendLine("- If UI ids are missing: use list_windows, shell, network_status, audio_power, or respond from visibleWindows.");
            builder.AppendLine("- Never invent elementIds or click overlay/chat UI.");
            builder.AppendLine();
        }
    }

    internal static void AppendParseRetryStrategy(
        StringBuilder builder,
        DecisionParseResult failed,
        string? userGoal,
        IReadOnlyList<UiElementSnapshot>? uiElements)
    {
        builder.AppendLine("STRATEGY CHANGE required — do not repeat the invalid choice.");

        if (failed.ErrorCode == DecisionParseErrorCode.InvalidElementId)
        {
            builder.AppendLine("- Abandon click_element / UI automation for this step.");
            if (IsWindowListingGoal(userGoal))
            {
                builder.AppendLine("- For window listing: use list_windows OR respond from visibleWindows in observation.");
            }
            else if (uiElements is null || uiElements.Count == 0)
            {
                builder.AppendLine("- uiElements empty: use P1 integrations, shell, list_windows, or respond without desktop action.");
            }
        }
        else
        {
            builder.AppendLine("- Re-read TOOL PRIORITY; choose a different action family than your last invalid reply.");
        }

        builder.AppendLine();
    }

    private static bool IsWindowListingGoal(string? userGoal)
    {
        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return false;
        }

        var text = userGoal.Trim().ToLowerInvariant();
        return text.Contains("pencere", StringComparison.Ordinal) ||
               text.Contains("window", StringComparison.Ordinal);
    }
}
