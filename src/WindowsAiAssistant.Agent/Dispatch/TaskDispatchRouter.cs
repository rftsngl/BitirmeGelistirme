using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

/// <summary>
/// Deterministic goal → action routing before LLM invocation.
/// </summary>
public sealed class TaskDispatchRouter : ITaskDispatchRouter
{
    public AgentDecision? TryResolve(string userGoal, DesktopObservation? observation)
    {
        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return null;
        }

        return GoalRoutingHints.TryBuildFastDecision(userGoal) ??
               NewDocumentPlaybook.TryResolve(userGoal, observation) ??
               SaveDocumentPlaybook.TryResolve(userGoal, observation) ??
               SelectAllPlaybook.TryResolve(userGoal, observation) ??
               ClipboardReadPlaybook.TryResolve(userGoal, observation);
    }

    public bool ShouldCompleteAfterRoute(string userGoal, string action)
    {
        var routed = TryResolve(userGoal, observation: null);
        return routed is not null &&
               string.Equals(routed.Action, action, StringComparison.OrdinalIgnoreCase);
    }
}
