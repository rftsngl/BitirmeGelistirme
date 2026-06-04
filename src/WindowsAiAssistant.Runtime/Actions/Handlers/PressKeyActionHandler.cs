using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class PressKeyActionHandler : IActionHandler
{
    public string ActionName => "press_key";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = action.Target;
        if (string.IsNullOrWhiteSpace(key) &&
            action.Parameters.TryGetValue("key", out var keyParameter))
        {
            key = keyParameter;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "press_key icin target veya parameters.key gerekli."
            });
        }

        try
        {
            var sequence = DesktopInput.ToSendKeysKey(key);
            DesktopInput.SendShortcut(sequence);
            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"Tus gonderildi: {key}"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = $"Tus gonderilemedi: {ex.Message}"
            });
        }
    }
}
