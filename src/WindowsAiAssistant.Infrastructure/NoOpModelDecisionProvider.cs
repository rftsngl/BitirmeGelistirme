using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Infrastructure;

public sealed class NoOpModelDecisionProvider : IModelDecisionProvider
{
    public Task<ModelDecisionResult> TryDecideAsync(ModelFacingObservationPackage observationPackage, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ModelDecisionResult
        {
            Status = ModelDecisionStatus.NotAvailable,
            Reason = "Model decision provider is not configured."
        });
    }
}
