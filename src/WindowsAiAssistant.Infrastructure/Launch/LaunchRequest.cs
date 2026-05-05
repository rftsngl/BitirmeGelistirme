namespace WindowsAiAssistant.Infrastructure.Launch;

public sealed class LaunchRequest
{
    public LaunchTargetReference TargetReference { get; init; } = new();
    public LaunchTarget Target { get; init; } = new();
}
