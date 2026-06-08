using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

internal static class NewDocumentPlaybook
{
    internal static AgentDecision? TryResolve(string userGoal, DesktopObservation? observation)
    {
        if (!FastPathGuard.IsSingleStepGoal(userGoal))
        {
            return null;
        }

        var text = FastPathGuard.Normalize(userGoal);
        if (!FastPathGuard.MatchesAny(text,
                "yeni belge", "yeni dosya", "yeni sayfa", "bos belge", "boş belge",
                "new document", "new file", "blank document"))
        {
            return null;
        }

        _ = observation;

        return new AgentDecision
        {
            DecisionType = AgentDecisionType.ExecuteAction,
            Action = "press_shortcut",
            Target = "Ctrl+N",
            Reason = "Fast route: new document/file via standard shortcut.",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }
}
