namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class StepFeedback
{
    public AgentAction ExecutedAction { get; init; } = new();
    public ActionExecutionResult ExecutionResult { get; init; } = new();
    public ObservationSnapshot? RefreshedObservation { get; init; }
    public string ObservationRefreshStatus { get; init; } = "unavailable";
}
