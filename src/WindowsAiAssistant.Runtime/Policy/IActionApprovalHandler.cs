using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Policy;

public interface IActionApprovalHandler
{
    Task<bool> RequestApprovalAsync(
        GateDecision gateDecision,
        AgentAction action,
        CancellationToken cancellationToken = default);
}
