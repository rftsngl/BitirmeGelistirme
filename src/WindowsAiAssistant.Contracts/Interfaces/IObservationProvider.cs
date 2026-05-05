using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IObservationProvider
{
    Task<ObservationSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}
