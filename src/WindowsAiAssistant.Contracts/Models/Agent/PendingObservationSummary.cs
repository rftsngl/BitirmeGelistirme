namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class PendingObservationSummary
{
    public string? ActiveProcessName { get; init; }
    public string? ActiveWindowTitle { get; init; }
    public long? ActiveWindowHandle { get; init; }
}