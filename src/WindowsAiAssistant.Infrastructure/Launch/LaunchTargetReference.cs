namespace WindowsAiAssistant.Infrastructure.Launch;

public sealed class LaunchTargetReference
{
    public string RawReference { get; init; } = string.Empty;
    public LaunchTargetKind? PreferredKind { get; init; }
}
