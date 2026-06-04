namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class StopActionHandler : IActionHandler
{
    public string ActionName => "stop";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var message = action.Parameters.TryGetValue("message", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : "Agent durduruldu.";

        return Task.FromResult(new ActionResult
        {
            Success = true,
            Message = message
        });
    }
}
