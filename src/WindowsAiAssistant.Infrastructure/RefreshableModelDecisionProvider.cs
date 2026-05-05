using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Infrastructure;

/// <summary>
/// Delegates to an inner provider created by the factory, caching until <see cref="Refresh"/> is called.
/// Used so UI can change active profile without restarting the process.
/// </summary>
public sealed class RefreshableModelDecisionProvider : IModelDecisionProvider
{
    private readonly Func<IModelDecisionProvider> _factory;
    private IModelDecisionProvider? _cached;

    public RefreshableModelDecisionProvider(Func<IModelDecisionProvider> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Drops the cached provider so the next call rebuilds from current configuration / active profile.
    /// </summary>
    public void Refresh()
    {
        _cached = null;
    }

    public Task<ModelDecisionResult> TryDecideAsync(
        ModelFacingObservationPackage observationPackage,
        CancellationToken cancellationToken = default)
    {
        _cached ??= _factory();
        return _cached.TryDecideAsync(observationPackage, cancellationToken);
    }
}
