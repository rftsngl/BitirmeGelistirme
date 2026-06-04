namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class AskUserActionHandler : IActionHandler
{
    public string ActionName => "ask_user";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RespondActionHandler.ExecuteMessageAction(action, ActionName));
    }
}
