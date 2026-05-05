namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class ForegroundProcessObservation
{
    public string? Name { get; init; }
    public int? ProcessId { get; init; }
}