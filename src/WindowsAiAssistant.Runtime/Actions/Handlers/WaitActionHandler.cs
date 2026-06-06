namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class WaitActionHandler : IActionHandler
{
    public string ActionName => "wait";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var delayMs = 500;
        if (action.Parameters.TryGetValue("seconds", out var secondsText) &&
            int.TryParse(secondsText, out var seconds) &&
            seconds > 0)
        {
            delayMs = Math.Min(seconds * 1000, 30_000);
        }
        else if ((action.Parameters.TryGetValue("ms", out var msText) ||
                  action.Parameters.TryGetValue("durationMs", out msText)) &&
                 int.TryParse(msText, out var durationMs) &&
                 durationMs > 0)
        {
            delayMs = Math.Min(durationMs, 30_000);
        }

        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        return new ActionResult
        {
            Success = true,
            Message = $"Beklendi ({delayMs} ms)."
        };
    }
}
