using WindowsAiAssistant.Contracts.Models.Agent;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IRuntimeSliceRegistry
{
    IReadOnlyList<IRuntimeSlice> Slices { get; }

    Task<RuntimeSliceSelection?> TrySelectAndRunAsync(
        AgentExecutionContext executionContext,
        AgentAction proposedAction,
        CancellationToken cancellationToken = default);
}
