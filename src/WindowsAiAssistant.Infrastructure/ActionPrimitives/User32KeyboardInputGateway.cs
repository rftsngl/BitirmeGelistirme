using System.Runtime.InteropServices;
using WindowsAiAssistant.Contracts.Models.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure.ActionPrimitives;

public sealed class User32KeyboardInputGateway : IKeyboardInputGateway
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    public nint GetForegroundWindowHandle()
    {
        return GetForegroundWindow();
    }

    public KeyboardDispatchResult SendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return new KeyboardDispatchResult
            {
                Success = false,
                ExpectedInputCount = 0,
                SentInputCount = 0,
                ErrorCode = "EmptyText",
                Message = "TypeText primitive blocked: text payload is empty."
            };
        }

        var inputs = new List<INPUT>(text.Length * 2);

        foreach (var character in text)
        {
            if (character == '\r')
            {
                continue;
            }

            if (character == '\n')
            {
                inputs.Add(CreateVirtualKeyInput((ushort)KeyboardKey.Enter, keyUp: false));
                inputs.Add(CreateVirtualKeyInput((ushort)KeyboardKey.Enter, keyUp: true));
                continue;
            }

            inputs.Add(CreateUnicodeInput(character, keyUp: false));
            inputs.Add(CreateUnicodeInput(character, keyUp: true));
        }

        return SendInputs(inputs, "TypeTextSendInputFailed", "TypeText primitive keyboard dispatch failed.");
    }

    public KeyboardDispatchResult SendKey(KeyboardKey key)
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput((ushort)key, keyUp: false),
            CreateVirtualKeyInput((ushort)key, keyUp: true)
        };

        return SendInputs(inputs, "PressKeySendInputFailed", "PressKey primitive keyboard dispatch failed.");
    }

    public KeyboardDispatchResult SendShortcut(IReadOnlyList<ShortcutModifierKey> modifiers, KeyboardKey key)
    {
        ArgumentNullException.ThrowIfNull(modifiers);

        var normalizedModifiers = modifiers
            .Distinct()
            .ToArray();

        if (normalizedModifiers.Length == 0)
        {
            return new KeyboardDispatchResult
            {
                Success = false,
                ExpectedInputCount = 0,
                SentInputCount = 0,
                ErrorCode = "NoModifiers",
                Message = "PressShortcut primitive blocked: at least one modifier key is required."
            };
        }

        var inputs = new List<INPUT>((normalizedModifiers.Length * 2) + 2);

        foreach (var modifier in normalizedModifiers)
        {
            inputs.Add(CreateVirtualKeyInput((ushort)modifier, keyUp: false));
        }

        inputs.Add(CreateVirtualKeyInput((ushort)key, keyUp: false));
        inputs.Add(CreateVirtualKeyInput((ushort)key, keyUp: true));

        for (var i = normalizedModifiers.Length - 1; i >= 0; i--)
        {
            inputs.Add(CreateVirtualKeyInput((ushort)normalizedModifiers[i], keyUp: true));
        }

        return SendInputs(inputs, "PressShortcutSendInputFailed", "PressShortcut primitive keyboard dispatch failed.");
    }

    private static KeyboardDispatchResult SendInputs(
        IReadOnlyList<INPUT> inputs,
        string failureCode,
        string failureMessage)
    {
        if (inputs.Count == 0)
        {
            return new KeyboardDispatchResult
            {
                Success = false,
                ExpectedInputCount = 0,
                SentInputCount = 0,
                ErrorCode = "NoInputs",
                Message = "Keyboard dispatch blocked: no native inputs were produced."
            };
        }

        var inputArray = inputs.ToArray();
        var sentCount = (int)SendInput((uint)inputArray.Length, inputArray, Marshal.SizeOf<INPUT>());

        if (sentCount != inputArray.Length)
        {
            var nativeError = Marshal.GetLastWin32Error();
            return new KeyboardDispatchResult
            {
                Success = false,
                ExpectedInputCount = inputArray.Length,
                SentInputCount = sentCount,
                ErrorCode = failureCode,
                Message = $"{failureMessage} Native error: {nativeError}."
            };
        }

        return new KeyboardDispatchResult
        {
            Success = true,
            ExpectedInputCount = inputArray.Length,
            SentInputCount = sentCount,
            ErrorCode = null,
            Message = "Keyboard input dispatched to foreground window."
        };
    }

    private static INPUT CreateUnicodeInput(char character, bool keyUp)
    {
        return new INPUT
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                KeyboardInput = new KEYBDINPUT
                {
                    Vk = 0,
                    Scan = character,
                    Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
                    Time = 0,
                    ExtraInfo = nint.Zero
                }
            }
        };
    }

    private static INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
    {
        return new INPUT
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                KeyboardInput = new KEYBDINPUT
                {
                    Vk = virtualKey,
                    Scan = 0,
                    Flags = keyUp ? KeyEventKeyUp : 0,
                    Time = 0,
                    ExtraInfo = nint.Zero
                }
            }
        };
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInputStructure);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT KeyboardInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
