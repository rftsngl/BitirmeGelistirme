namespace WindowsAiAssistant.Runtime.Automation;

public sealed class UiElementSnapshot
{
    public required string ElementId { get; init; }
    public string ControlType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string AutomationId { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public bool IsEnabled { get; init; } = true;

    public string ToPromptLine()
    {
        var namePart = string.IsNullOrWhiteSpace(Name) ? "\"\"" : $"\"{Name}\"";
        var valuePart = string.IsNullOrWhiteSpace(Value) ? string.Empty : $" value=\"{Truncate(Value, 40)}\"";
        var automationPart = string.IsNullOrWhiteSpace(AutomationId)
            ? string.Empty
            : $" automationId=\"{Truncate(AutomationId, 24)}\"";
        return $"{ElementId} {ControlType} {namePart}{valuePart}{automationPart} enabled={IsEnabled}";
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}

public sealed class UiElementTree
{
    public nint WindowHandle { get; init; }
    public string WindowTitle { get; init; } = string.Empty;
    public IReadOnlyList<UiElementSnapshot> Elements { get; init; } = Array.Empty<UiElementSnapshot>();
    public bool Truncated { get; init; }
    public int TotalCaptured { get; init; }

    public string ToPromptSummary()
    {
        if (Elements.Count == 0)
        {
            return "uiElements (active window): (none captured)";
        }

        var lines = Elements.Take(40).Select(element => $"  {element.ToPromptLine()}");
        var header = $"uiElements (active window): {Elements.Count}" +
                     (Truncated ? " (truncated; use focus_window then observe again)" : string.Empty);
        return header + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}

public sealed class UiElementReference
{
    public required string ElementId { get; init; }
    public nint WindowHandle { get; init; }
    public int[] RuntimeId { get; init; } = Array.Empty<int>();
    public bool UsedUia2 { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}
