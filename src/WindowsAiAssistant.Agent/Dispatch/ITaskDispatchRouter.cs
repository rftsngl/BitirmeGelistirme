using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Dispatch;

public interface ITaskDispatchRouter
{
    AgentDecision? TryResolve(string userGoal, DesktopObservation? observation);

    bool ShouldCompleteAfterRoute(string userGoal, string action);
}
