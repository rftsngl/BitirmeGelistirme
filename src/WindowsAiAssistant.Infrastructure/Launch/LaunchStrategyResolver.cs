namespace WindowsAiAssistant.Infrastructure.Launch;

public sealed class LaunchStrategyResolver : ILaunchStrategyResolver
{
    private readonly IReadOnlyList<ILaunchStrategy> _strategies;

    public LaunchStrategyResolver(IEnumerable<ILaunchStrategy> strategies)
    {
        _strategies = strategies.ToList();
    }

    public ILaunchStrategy? Resolve(LaunchRequest request)
    {
        return _strategies.FirstOrDefault(strategy => strategy.CanHandle(request));
    }
}
