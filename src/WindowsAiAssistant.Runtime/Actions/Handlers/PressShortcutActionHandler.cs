using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class PressShortcutActionHandler : IActionHandler
{
    public string ActionName => "press_shortcut";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var shortcut = action.Target;
        if (string.IsNullOrWhiteSpace(shortcut) &&
            action.Parameters.TryGetValue("shortcut", out var shortcutParameter))
        {
            shortcut = shortcutParameter;
        }

        if (string.IsNullOrWhiteSpace(shortcut) &&
            action.Parameters.TryGetValue("keys", out var keysParameter))
        {
            shortcut = keysParameter;
        }

        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "press_shortcut icin target veya parameters.shortcut gerekli."
            });
        }

        try
        {
            var sequence = DesktopInput.ToSendKeysShortcut(shortcut);
            DesktopInput.SendShortcut(sequence);
            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"Kisayol gonderildi: {shortcut}"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = $"Kisayol gonderilemedi: {ex.Message}"
            });
        }
    }
}
