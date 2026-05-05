namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class ForegroundWindowObservation
{
    public string? Title { get; init; }
    public long? Handle { get; init; }
    public bool IsForeground { get; init; }
}