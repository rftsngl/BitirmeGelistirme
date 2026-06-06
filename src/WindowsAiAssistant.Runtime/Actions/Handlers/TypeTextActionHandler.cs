using WindowsAiAssistant.Runtime.Input;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class TypeTextActionHandler : IActionHandler
{
    private readonly ForegroundWindowService _foregroundWindow;

    public TypeTextActionHandler(ForegroundWindowService foregroundWindow) =>
        _foregroundWindow = foregroundWindow ?? throw new ArgumentNullException(nameof(foregroundWindow));

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

        var (windowTitle, processName, _) = _foregroundWindow.GetForegroundInfo();
        if (string.IsNullOrWhiteSpace(processName))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "Odakli bir pencere yok. Once focus_window veya launch ile bir uygulamayi one getirin."
            });
        }

        var useSendInput = action.Parameters.TryGetValue("method", out var method) &&
                           method.Trim().Equals("sendinput", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (useSendInput)
            {
                DesktopInput.TypeTextViaSendInput(text);
            }
            else
            {
                DesktopInput.TypeTextViaClipboard(text);
            }

            var mode = useSendInput ? "SendInput" : "clipboard";
            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"Metin yazildi ({text.Length} karakter, {mode}) -> {windowTitle}."
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
