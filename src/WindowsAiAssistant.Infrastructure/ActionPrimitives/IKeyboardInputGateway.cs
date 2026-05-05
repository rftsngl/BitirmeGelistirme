using WindowsAiAssistant.Contracts.Models.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure.ActionPrimitives;

public interface IKeyboardInputGateway
{
    nint GetForegroundWindowHandle();

    KeyboardDispatchResult SendText(string text);

    KeyboardDispatchResult SendKey(KeyboardKey key);

    KeyboardDispatchResult SendShortcut(IReadOnlyList<ShortcutModifierKey> modifiers, KeyboardKey key);
}

public sealed class KeyboardDispatchResult
{
    public bool Success { get; init; }
    public int ExpectedInputCount { get; init; }
    public int SentInputCount { get; init; }
    public string? ErrorCode { get; init; }
    public string Message { get; init; } = string.Empty;
}
