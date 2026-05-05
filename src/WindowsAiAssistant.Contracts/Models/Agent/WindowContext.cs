namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class WindowContext
{
    public string? Title { get; init; }
    public string? ProcessName { get; init; }
    public long? Handle { get; init; }
    public bool IsForeground { get; init; }
}
