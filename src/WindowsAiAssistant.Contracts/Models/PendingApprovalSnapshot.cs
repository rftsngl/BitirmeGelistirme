using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Models;

public sealed class PendingApprovalSnapshot
{
    public string CorrelationId { get; init; } = string.Empty;
    public string CommandText { get; init; } = string.Empty;
    public AiDecision Decision { get; init; } = new();
    public NextActionDecision? PendingDecisionContract { get; init; }
    public NextActionDecision? ApprovedDecisionContract { get; init; }
    public IReadOnlyList<TargetReference> ResolvedTargets { get; init; } = [];
    public ContextAdapterContext? ContextAdapter { get; init; }
    public PendingObservationSummary? ObservationSummary { get; init; }
    public DecisionCycleRuntimeState? RuntimeState { get; init; }
    public string? PendingStepApprovalKey { get; init; }
    public string? ApprovedStepApprovalKey { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, string>? Metadata { get; init; }
}
