namespace WindowsAiAssistant.Runtime.Policy;

public sealed class GateDecision
{
    public required GateOutcome Outcome { get; init; }
    public required ActionRisk Risk { get; init; }
    public required string Reason { get; init; }
    public required string Summary { get; init; }
    public string ApprovalKey { get; init; } = string.Empty;
}
