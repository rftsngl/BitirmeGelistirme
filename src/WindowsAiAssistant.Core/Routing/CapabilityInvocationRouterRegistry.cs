using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Core.Routing;

public sealed class CapabilityInvocationRouterRegistry : ICapabilityInvocationRouterRegistry
{
    public CapabilityInvocationRouterRegistry(IEnumerable<ICapabilityInvocationRouter> routers)
    {
        ArgumentNullException.ThrowIfNull(routers);
        Routers = routers.ToArray();
    }

    public IReadOnlyList<ICapabilityInvocationRouter> Routers { get; }

    public bool TryRoute(
        AiDecision decision,
        AgentExecutionContext executionContext,
        out CapabilityInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(executionContext);

        foreach (var router in Routers)
        {
            if (router.TryRoute(decision, executionContext, out invocation))
            {
                return true;
            }
        }

        invocation = default!;
        return false;
    }
}
