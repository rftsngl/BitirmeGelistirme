namespace WindowsAiAssistant.Infrastructure.Launch;

public interface ILaunchStrategy
{
    string Name { get; }
    bool CanHandle(LaunchRequest request);
    Task<LaunchExecution> ExecuteAsync(LaunchRequest request, CancellationToken cancellationToken = default);
}
