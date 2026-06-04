namespace WindowsAiAssistant.Runtime.Actions;

public interface IActionHandler
{
    string ActionName { get; }

    Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default);
}
