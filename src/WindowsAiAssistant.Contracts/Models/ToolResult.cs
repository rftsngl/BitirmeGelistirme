using WindowsAiAssistant.Contracts.Models.ActionPrimitives;

namespace WindowsAiAssistant.Contracts.Models;

public sealed class ToolResult
{
    public bool Success { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public ActionPrimitiveExecutionResult? PrimitiveExecution { get; init; }
}
