using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Infrastructure;

public sealed class UnavailableModelDecisionProvider : IModelDecisionProvider
{
    private readonly string _reason;

    public UnavailableModelDecisionProvider(string reason)
    {
        _reason = string.IsNullOrWhiteSpace(reason) ? "provider-unavailable" : reason.Trim();
    }

    public Task<ModelDecisionResult> TryDecideAsync(
        ModelFacingObservationPackage observationPackage,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ModelDecisionResult
        {
            Status = ModelDecisionStatus.NotAvailable,
            Reason = _reason
        });
    }
}
