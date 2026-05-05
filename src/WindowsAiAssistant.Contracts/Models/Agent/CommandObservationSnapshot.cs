namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class CommandObservationSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string OriginalUserCommand { get; init; } = string.Empty;
    public ForegroundWindowObservation? ForegroundWindow { get; init; }
    public ForegroundProcessObservation? ForegroundProcess { get; init; }
    public GroundingObservationSummary? GroundingSummary { get; init; }
    public SafetyObservationSummary? SafetySummary { get; init; }
    public ExecutionRuntimeState? RuntimeState { get; init; }
    public ObservationCollectionStatus CollectionStatus { get; init; } = ObservationCollectionStatus.Partial;
    public string? CollectionMessage { get; init; }
}