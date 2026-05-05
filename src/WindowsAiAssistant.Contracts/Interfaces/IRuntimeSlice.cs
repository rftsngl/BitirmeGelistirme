using WindowsAiAssistant.Contracts.Models.Agent;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IRuntimeSlice
{
    string Name { get; }
    int Priority { get; }

    Task<IRuntimeSliceResult> TryRunAsync(
        AgentExecutionContext executionContext,
        AgentAction proposedAction,
        CancellationToken cancellationToken = default);
}
