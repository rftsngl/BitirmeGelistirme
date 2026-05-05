namespace WindowsAiAssistant.Infrastructure.Launch;

public sealed class LaunchTarget
{
    public LaunchTargetKind Kind { get; init; } = LaunchTargetKind.Unknown;
    public string OriginalReference { get; init; } = string.Empty;
    public string NormalizedReference { get; init; } = string.Empty;
}
