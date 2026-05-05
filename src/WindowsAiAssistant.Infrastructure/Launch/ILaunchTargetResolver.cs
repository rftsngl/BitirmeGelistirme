namespace WindowsAiAssistant.Infrastructure.Launch;

public interface ILaunchTargetResolver
{
    LaunchTarget Resolve(LaunchTargetReference targetReference);
}
