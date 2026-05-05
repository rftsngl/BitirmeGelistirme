using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface ICapabilityInvocationRouter
{
    string Name { get; }

    bool TryRoute(
        AiDecision decision,
        AgentExecutionContext executionContext,
        out CapabilityInvocation invocation);
}
