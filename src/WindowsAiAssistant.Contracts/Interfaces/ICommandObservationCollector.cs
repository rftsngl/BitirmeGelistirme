using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface ICommandObservationCollector
{
    Task<CommandObservationSnapshot> CollectAsync(
        CommandObservationRequest request,
        CancellationToken cancellationToken = default);
}