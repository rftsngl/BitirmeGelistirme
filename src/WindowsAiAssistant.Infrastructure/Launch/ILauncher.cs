namespace WindowsAiAssistant.Infrastructure.Launch;

public interface ILauncher
{
    Task<LaunchExecution> LaunchAsync(LaunchTargetReference targetReference, CancellationToken cancellationToken = default);
}
