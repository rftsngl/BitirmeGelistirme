using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

internal static class ClipboardReadPlaybook
{
    internal static AgentDecision? TryResolve(string userGoal, DesktopObservation? observation)
    {
        if (!FastPathGuard.IsSingleStepGoal(userGoal))
        {
            return null;
        }

        var text = FastPathGuard.Normalize(userGoal);
        if (!FastPathGuard.MatchesAny(text,
                "pano", "panoyu oku", "panoda ne var", "clipboard", "copy paste icerigi"))
        {
            return null;
        }

        _ = observation;

        return new AgentDecision
        {
            DecisionType = AgentDecisionType.ExecuteAction,
            Action = "clipboard",
            Reason = "Fast route: read clipboard contents.",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = "read"
            }
        };
    }
}
