using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

internal static class SaveDocumentPlaybook
{
    internal static AgentDecision? TryResolve(string userGoal, DesktopObservation? observation)
    {
        if (!FastPathGuard.IsSingleStepGoal(userGoal))
        {
            return null;
        }

        var text = FastPathGuard.Normalize(userGoal);
        if (!FastPathGuard.MatchesAny(text,
                "kaydet", "dosyayi kaydet", "belgeyi kaydet", "save document", "save file", "ctrl+s"))
        {
            return null;
        }

        _ = observation;

        return new AgentDecision
        {
            DecisionType = AgentDecisionType.ExecuteAction,
            Action = "press_shortcut",
            Target = "Ctrl+S",
            Reason = "Fast route: save document via standard shortcut.",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }
}
