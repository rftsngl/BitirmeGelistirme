using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Infrastructure.Context;

public sealed class ContextAdapterResolver : IContextAdapterResolver
{
    private readonly IReadOnlyList<IContextAdapter> _adapters;

    public ContextAdapterResolver(IEnumerable<IContextAdapter> adapters)
    {
        _adapters = adapters.ToList();
    }

    public ContextAdapterContext? Resolve(ObservationSnapshot? observation)
    {
        if (observation is null)
        {
            return null;
        }

        foreach (var adapter in _adapters)
        {
            if (adapter.CanHandle(observation))
            {
                return adapter.BuildContext(observation);
            }
        }

        return null;
    }
}
