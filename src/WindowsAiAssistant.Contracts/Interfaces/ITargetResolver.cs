using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface ITargetResolver
{
    Task<IReadOnlyList<TargetReference>> ResolveTargetsAsync(
        CommandRequest request,
        ObservationSnapshot? observation,
        CancellationToken cancellationToken = default);
}
