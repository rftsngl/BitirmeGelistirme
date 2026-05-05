using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class DecisionInputBundle
{
    public Guid CorrelationId { get; init; }
    public string RawInput { get; init; } = string.Empty;
    public string NormalizedInput { get; init; } = string.Empty;
    public ObservationSnapshot? Observation { get; init; }
    public ContextAdapterContext? ContextAdapter { get; init; }
    public IReadOnlyList<TargetReference> ResolvedTargets { get; init; } = [];
    public bool ContextProvenanceConsistent { get; init; }
    public string ContextProvenanceSource { get; init; } = "live";
    public DecisionInputSource Source { get; init; } = DecisionInputSource.Live;
    public DecisionCycleRuntimeState? RuntimeCycleState { get; init; }
}
