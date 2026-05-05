namespace WindowsAiAssistant.Infrastructure.Launch;

public interface ILaunchStrategyResolver
{
    ILaunchStrategy? Resolve(LaunchRequest request);
}
