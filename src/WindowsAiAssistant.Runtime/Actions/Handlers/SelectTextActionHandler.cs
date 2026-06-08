using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class SelectTextActionHandler : IActionHandler
{
    public string ActionName => "select_text";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode");
        if (string.IsNullOrWhiteSpace(mode))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "select_text icin parameters.mode gerekli (all|extend_left|extend_right|extend_up|extend_down|word|line)."
            });
        }

        if (!TryResolveShortcut(mode, action, out var shortcut, out var error))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = error ?? "select_text modu desteklenmiyor."
            });
        }

        try
        {
            var repeat = ReadRepeatCount(action);
            var sequence = DesktopInput.ToSendKeysShortcut(shortcut);
            for (var i = 0; i < repeat; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DesktopInput.SendShortcut(sequence);
            }

            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = repeat > 1
                    ? $"Metin secimi uygulandi: {mode} ({shortcut} x{repeat})"
                    : $"Metin secimi uygulandi: {mode} ({shortcut})"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = $"Metin secimi basarisiz: {ex.Message}"
            });
        }
    }

    private static bool TryResolveShortcut(
        string mode,
        AgentAction action,
        out string shortcut,
        out string? error)
    {
        shortcut = string.Empty;
        error = null;

        var overrideShortcut = ActionParameterReader.GetTargetOrParameter(action, "shortcut");
        if (!string.IsNullOrWhiteSpace(overrideShortcut))
        {
            shortcut = overrideShortcut;
            return true;
        }

        shortcut = mode.Trim().ToLowerInvariant() switch
        {
            "all" or "select_all" or "everything" => "Ctrl+A",
            "extend_left" or "left" => "Ctrl+Shift+Left",
            "extend_right" or "right" => "Ctrl+Shift+Right",
            "extend_up" or "up" => "Ctrl+Shift+Up",
            "extend_down" or "down" => "Ctrl+Shift+Down",
            "word" => "Ctrl+Shift+Right",
            "line" => "Ctrl+Shift+Down",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(shortcut))
        {
            error = $"select_text modu desteklenmiyor: {mode}";
            return false;
        }

        return true;
    }

    private static int ReadRepeatCount(AgentAction action)
    {
        if (!ActionParameterReader.TryGetInt(action, "count", out var count) &&
            !ActionParameterReader.TryGetInt(action, "repeat", out count))
        {
            return 1;
        }

        return Math.Clamp(count, 1, 50);
    }

}
