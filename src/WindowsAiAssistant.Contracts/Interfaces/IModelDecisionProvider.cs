using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IModelDecisionProvider
{
    Task<ModelDecisionResult> TryDecideAsync(ModelFacingObservationPackage observationPackage, CancellationToken cancellationToken = default);
}
