using WindowsAiAssistant.Runtime.Input;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class TypeTextActionHandler : IActionHandler
{
    private readonly ForegroundWindowService _foregroundWindow;

    public TypeTextActionHandler(ForegroundWindowService foregroundWindow) =>
        _foregroundWindow = foregroundWindow ?? throw new ArgumentNullException(nameof(foregroundWindow));

    public string ActionName => "type_text";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!action.Parameters.TryGetValue("text", out var text) || string.IsNullOrEmpty(text))
        {
            return new ActionResult
            {
                Success = false,
                Message = "type_text icin parameters.text gerekli."
            };
        }

        var (windowTitle, processName, _) = _foregroundWindow.GetForegroundInfo();
        if (string.IsNullOrWhiteSpace(processName))
        {
            return new ActionResult
            {
                Success = false,
                Message = "Odakli bir pencere yok. Once focus_window veya launch ile bir uygulamayi one getirin."
            };
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
                await DesktopInput.TypeTextViaClipboardAsync(text, cancellationToken).ConfigureAwait(false);
            }

            var mode = useSendInput ? "SendInput" : "clipboard";
            return new ActionResult
            {
                Success = true,
                Message = $"Metin yazildi ({text.Length} karakter, {mode}) -> {windowTitle}."
            };
        }
        catch (Exception ex)
        {
            return new ActionResult
            {
                Success = false,
                Message = $"Metin yazilamadi: {ex.Message}"
            };
        }
    }
}
