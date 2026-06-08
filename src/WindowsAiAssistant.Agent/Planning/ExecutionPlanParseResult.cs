namespace WindowsAiAssistant.Agent.Planning;

public sealed class ExecutionPlanParseResult
{
    public bool Success { get; init; }
    public ExecutionPlan? Plan { get; init; }
    public string? ErrorMessage { get; init; }

    public static ExecutionPlanParseResult Ok(ExecutionPlan plan) =>
        new() { Success = true, Plan = plan };

    public static ExecutionPlanParseResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
