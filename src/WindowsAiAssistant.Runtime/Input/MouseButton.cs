namespace WindowsAiAssistant.Runtime.Input;

public enum MouseButton
{
    Left,
    Right,
    Middle
}

public static class MouseButtonParser
{
    public static bool TryParse(string? value, out MouseButton button)
    {
        button = MouseButton.Left;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "left" or "l" or "0" => Set(MouseButton.Left, out button),
            "right" or "r" or "2" => Set(MouseButton.Right, out button),
            "middle" or "mid" or "m" or "3" => Set(MouseButton.Middle, out button),
            _ => false
        };
    }

    private static bool Set(MouseButton value, out MouseButton button)
    {
        button = value;
        return true;
    }
}
