using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IContextAdapter
{
    string Name { get; }
    bool CanHandle(ObservationSnapshot observation);
    ContextAdapterContext BuildContext(ObservationSnapshot observation);
}
