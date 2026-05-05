using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IContextAdapterResolver
{
    ContextAdapterContext? Resolve(ObservationSnapshot? observation);
}
