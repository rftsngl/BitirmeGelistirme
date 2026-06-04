namespace WindowsAiAssistant.Runtime.Actions;

public sealed class ActionResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
}
