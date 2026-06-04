using System.Windows.Forms;

namespace WindowsAiAssistant.Runtime.Input;

internal static class DesktopInput
{
    public static void SendShortcut(string sendKeysSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sendKeysSequence);
        SendKeys.SendWait(sendKeysSequence);
    }

    public static void TypeTextViaClipboard(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var previous = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        try
        {
            Clipboard.SetText(text);
            SendKeys.SendWait("^v");
        }
        finally
        {
            if (previous is not null)
            {
                Clipboard.SetText(previous);
            }
        }
    }

    public static string ToSendKeysShortcut(string shortcut)
    {
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException("Gecersiz shortcut.");
        }

        var key = parts[^1];
        var modifiers = parts[..^1];
        var prefix = string.Empty;

        foreach (var modifier in modifiers)
        {
            prefix += modifier.ToLowerInvariant() switch
            {
                "ctrl" or "control" => "^",
                "alt" => "%",
                "shift" => "+",
                _ => throw new ArgumentException($"Desteklenmeyen modifier: {modifier}")
            };
        }

        if (key.Length == 1)
        {
            return prefix + key.ToLowerInvariant();
        }

        return prefix + "{" + key.ToUpperInvariant() + "}";
    }

    public static string ToSendKeysKey(string key)
    {
        return key.Trim().ToLowerInvariant() switch
        {
            "enter" or "return" => "{ENTER}",
            "tab" => "{TAB}",
            "escape" or "esc" => "{ESC}",
            "backspace" => "{BACKSPACE}",
            "delete" or "del" => "{DELETE}",
            "up" => "{UP}",
            "down" => "{DOWN}",
            "left" => "{LEFT}",
            "right" => "{RIGHT}",
            "home" => "{HOME}",
            "end" => "{END}",
            "space" => " ",
            var k when k.Length == 1 => k,
            var k => "{" + k.ToUpperInvariant() + "}"
        };
    }
}
