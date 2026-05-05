namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class AgentStepState
{
    public ExecutionContext ExecutionContext { get; init; } = new();
    public ObservationSnapshot? CurrentObservation { get; init; }
    public AgentAction? LastAction { get; init; }
    public ActionExecutionResult? LastResult { get; init; }
    public int StepIndex { get; init; }
}
