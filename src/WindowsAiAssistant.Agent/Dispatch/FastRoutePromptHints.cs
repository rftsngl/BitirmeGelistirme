using System.Text;
using System.Text.Json;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

/// <summary>
/// Deterministic route önerileri — LLM karar verir; otomatik çalıştırılmaz.
/// </summary>
internal static class FastRoutePromptHints
{
    internal static void Append(StringBuilder builder, string userGoal, DesktopObservation observation)
    {
        var router = new TaskDispatchRouter();
        var suggested = router.TryResolve(userGoal, observation);
        if (suggested is not null)
        {
            builder.AppendLine("SUGGESTED ROUTE (optional — you decide whether it fits the goal):");
            builder.AppendLine($"- action: {suggested.Action}");
            if (!string.IsNullOrWhiteSpace(suggested.Target))
            {
                builder.AppendLine($"- target: {suggested.Target}");
            }

            if (suggested.Parameters.Count > 0)
            {
                builder.AppendLine($"- parameters: {SerializeParameters(suggested.Parameters)}");
            }

            if (!string.IsNullOrWhiteSpace(suggested.Reason))
            {
                builder.AppendLine($"- why: {suggested.Reason}");
            }

            builder.AppendLine("- Use this ONLY if you agree it achieves the user goal; otherwise pick another P1–P3 tool.");
            builder.AppendLine();
        }

        AppendWindowListingHints(builder, userGoal, observation);
    }

    private static void AppendWindowListingHints(
        StringBuilder builder,
        string userGoal,
        DesktopObservation observation)
    {
        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return;
        }

        var text = Normalize(userGoal);
        if (!MatchesAny(text,
                "acik pencere", "açık pencere", "pencere list", "pencere ozet", "pencere özet",
                "hangi pencere", "gorunur pencere", "görünür pencere", "open windows",
                "list windows", "window list", "pencereleri ozet", "pencereleri özet"))
        {
            return;
        }

        builder.AppendLine("WINDOW LISTING GOAL (you decide the best path):");
        builder.AppendLine("- Preferred: execute_action list_windows (no target needed), then complete with Turkish summary from lastActionResult.");
        builder.AppendLine("- Alternative: decisionType=complete action=respond using visibleWindows already in observation (no click_element).");
        builder.AppendLine("- Do NOT use click_element for this goal — uiElements may be empty.");
        if (observation.Windows.Count > 0)
        {
            builder.AppendLine($"- visibleWindows already lists {observation.Windows.Count} window(s) in observation.");
        }

        builder.AppendLine();
    }

    private static string SerializeParameters(IReadOnlyDictionary<string, string> parameters) =>
        JsonSerializer.Serialize(parameters);

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant()
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');

    private static bool MatchesAny(string normalized, params string[] phrases) =>
        phrases.Any(phrase => normalized.Contains(Normalize(phrase), StringComparison.Ordinal));
}
