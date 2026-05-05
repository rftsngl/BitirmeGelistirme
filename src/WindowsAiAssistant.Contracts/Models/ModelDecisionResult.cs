namespace WindowsAiAssistant.Contracts.Models;

public enum ModelDecisionStatus
{
    DecisionAvailable = 0,
    NoDecision = 1,
    NotAvailable = 2,
    InvalidResponse = 3
}

public sealed class ModelDecisionResult
{
    public ModelDecisionStatus Status { get; init; } = ModelDecisionStatus.NoDecision;
    public NextActionDecision? NextActionDecision { get; init; }
    public AiDecision? Decision { get; init; }
    public string? Reason { get; init; }
}
