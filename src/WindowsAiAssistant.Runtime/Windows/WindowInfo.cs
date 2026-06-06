namespace WindowsAiAssistant.Runtime.Windows;

public sealed class WindowInfo
{
    public required string WindowId { get; init; }
    public nint Handle { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public bool IsForeground { get; init; }
    public bool IsMinimized { get; init; }
    public bool IsMaximized { get; init; }

    public string ToPromptLine() =>
        $"[{WindowId}] \"{Title}\" {ProcessName} (foreground={IsForeground}, minimized={IsMinimized}, maximized={IsMaximized})";
}
