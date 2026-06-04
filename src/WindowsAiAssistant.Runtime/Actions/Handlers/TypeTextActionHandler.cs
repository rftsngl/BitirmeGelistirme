using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class TypeTextActionHandler : IActionHandler
{
    public string ActionName => "type_text";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!action.Parameters.TryGetValue("text", out var text) || string.IsNullOrEmpty(text))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "type_text icin parameters.text gerekli."
            });
        }

        try
        {
            DesktopInput.TypeTextViaClipboard(text);
            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"Metin yazildi ({text.Length} karakter)."
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = $"Metin yazilamadi: {ex.Message}"
            });
        }
    }
}
