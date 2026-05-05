using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface ICapabilityInvocationRouterRegistry
{
    IReadOnlyList<ICapabilityInvocationRouter> Routers { get; }

    bool TryRoute(
        AiDecision decision,
        AgentExecutionContext executionContext,
        out CapabilityInvocation invocation);
}
