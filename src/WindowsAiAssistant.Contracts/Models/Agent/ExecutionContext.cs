using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class ExecutionContext
{
    public Guid CorrelationId { get; init; }
    public string RawInput { get; init; } = string.Empty;
    public string NormalizedInput { get; init; } = string.Empty;
    public CommandIntentKind DetectedIntent { get; init; } = CommandIntentKind.Unknown;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? SessionId { get; init; }
    public ObservationSnapshot? Observation { get; init; }
    public CommandObservationSnapshot? RichObservation { get; init; }
    public ContextAdapterContext? ContextAdapter { get; init; }
    public TargetGroundingResult? PrimaryTargetGrounding { get; init; }
    public ExecutionRuntimeState? RuntimeState { get; init; }
    public IReadOnlyList<TargetReference> ResolvedTargets { get; init; } = [];
    public IDictionary<string, string>? Metadata { get; init; }
}
