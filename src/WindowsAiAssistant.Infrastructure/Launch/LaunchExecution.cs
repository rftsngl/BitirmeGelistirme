namespace WindowsAiAssistant.Infrastructure.Launch;

public sealed class LaunchExecution
{
    public bool Success { get; init; }
    public string StrategyName { get; init; } = string.Empty;
    public LaunchRequest Request { get; init; } = new();
    public LaunchFailureReason FailureReason { get; init; } = LaunchFailureReason.None;
    public string? ExceptionMessage { get; init; }
}
