using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Policy;

public sealed class DenyByDefaultApprovalHandler : IActionApprovalHandler
{
    public Task<bool> RequestApprovalAsync(
        GateDecision gateDecision,
        AgentAction action,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
