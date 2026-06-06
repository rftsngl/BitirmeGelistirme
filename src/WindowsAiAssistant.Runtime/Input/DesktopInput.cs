using System.Runtime.InteropServices;
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

    // SendInput Unicode injection — works in apps where clipboard paste (Ctrl+V) is
    // blocked or behaves unexpectedly (some terminals, custom editors, secure fields).
    public static void TypeTextViaSendInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        var inputs = new List<KeyboardNativeMethods.Input>(text.Length * 2);
        foreach (var ch in text)
        {
            inputs.Add(KeyboardNativeMethods.UnicodeKey(ch, keyUp: false));
            inputs.Add(KeyboardNativeMethods.UnicodeKey(ch, keyUp: true));
        }

        var array = inputs.ToArray();
        _ = KeyboardNativeMethods.SendInput((uint)array.Length, array, Marshal.SizeOf<KeyboardNativeMethods.Input>());
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

internal static class KeyboardNativeMethods
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;

    public static Input UnicodeKey(char character, bool keyUp) =>
        new()
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KeyboardInputData
                {
                    wVk = 0,
                    wScan = character,
                    dwFlags = KeyeventfUnicode | (keyUp ? KeyeventfKeyup : 0),
                    time = 0,
                    dwExtraInfo = nuint.Zero
                }
            }
        };

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInputData ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInputData
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }
}
