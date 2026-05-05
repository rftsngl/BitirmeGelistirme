namespace WindowsAiAssistant.Infrastructure.Launch;

public sealed class Launcher : ILauncher
{
    private readonly ILaunchStrategyResolver _strategyResolver;
    private readonly ILaunchTargetResolver _targetResolver;

    public Launcher(ILaunchTargetResolver targetResolver, ILaunchStrategyResolver strategyResolver)
    {
        _targetResolver = targetResolver ?? throw new ArgumentNullException(nameof(targetResolver));
        _strategyResolver = strategyResolver ?? throw new ArgumentNullException(nameof(strategyResolver));
    }

    public async Task<LaunchExecution> LaunchAsync(
        LaunchTargetReference targetReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetReference);

        var target = _targetResolver.Resolve(targetReference);
        var request = new LaunchRequest
        {
            TargetReference = targetReference,
            Target = target
        };

        if (string.IsNullOrWhiteSpace(target.OriginalReference))
        {
            return CreateFailure(request, LaunchFailureReason.EmptyTarget);
        }

        var strategy = _strategyResolver.Resolve(request);
        if (strategy is null)
        {
            return CreateFailure(request, LaunchFailureReason.UnsupportedTargetKind);
        }

        return await strategy.ExecuteAsync(request, cancellationToken);
    }

    private static LaunchExecution CreateFailure(LaunchRequest request, LaunchFailureReason failureReason)
    {
        return new LaunchExecution
        {
            Success = false,
            StrategyName = string.Empty,
            Request = request,
            FailureReason = failureReason
        };
    }
}
