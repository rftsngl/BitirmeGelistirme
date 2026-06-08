using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

internal static class SelectAllPlaybook
{
    internal static AgentDecision? TryResolve(string userGoal, DesktopObservation? observation)
    {
        if (!FastPathGuard.IsSingleStepGoal(userGoal))
        {
            return null;
        }

        var text = FastPathGuard.Normalize(userGoal);
        if (!FastPathGuard.MatchesAny(text,
                "tumunu sec", "hepsini sec", "select all", "ctrl+a", "tümünü seç", "hepsini seç"))
        {
            return null;
        }

        _ = observation;

        return new AgentDecision
        {
            DecisionType = AgentDecisionType.ExecuteAction,
            Action = "press_shortcut",
            Target = "Ctrl+A",
            Reason = "Fast route: select all via standard shortcut.",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }
}
