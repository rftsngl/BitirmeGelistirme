using System.Text.Json;
using WindowsAiAssistant.Runtime.Automation;
using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Runtime.Observation;

public sealed class DesktopObservation
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string ActiveWindowTitle { get; init; } = string.Empty;
    public string ActiveProcessName { get; init; } = string.Empty;
    public int ActiveProcessId { get; init; }
    public int ScreenWidth { get; init; }
    public int ScreenHeight { get; init; }
    public int CursorX { get; init; }
    public int CursorY { get; init; }
    public string? LastActionResult { get; init; }
    public string? LastUserGoal { get; init; }
    public ScreenshotObservation? Screenshot { get; init; }
    public IReadOnlyList<WindowInfo> Windows { get; init; } = Array.Empty<WindowInfo>();
    public UiElementTree? UiTree { get; init; }
    public IReadOnlyList<MonitorDescription> Monitors { get; init; } = Array.Empty<MonitorDescription>();

    public string ToShortSummary()
    {
        var window = string.IsNullOrWhiteSpace(ActiveWindowTitle) ? "(baslik yok)" : ActiveWindowTitle;
        var process = string.IsNullOrWhiteSpace(ActiveProcessName) ? "(bilinmiyor)" : ActiveProcessName;
        var screenshot = Screenshot is null ? string.Empty : " · screenshot";
        var uiCount = UiTree?.Elements.Count ?? 0;
        var uiPart = uiCount > 0 ? $" · {uiCount} ui element" : string.Empty;
        return $"{window} · {process} · {ScreenWidth}x{ScreenHeight}{screenshot}{uiPart}";
    }

    public string ToPromptSummary()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"timestamp: {Timestamp:O}");
        builder.AppendLine($"activeWindowTitle: {ActiveWindowTitle}");
        builder.AppendLine($"activeProcessName: {ActiveProcessName}");
        builder.AppendLine($"activeProcessId: {ActiveProcessId}");
        builder.AppendLine($"screenWidth: {ScreenWidth}");
        builder.AppendLine($"screenHeight: {ScreenHeight}");
        builder.AppendLine($"cursorX: {CursorX}");
        builder.AppendLine($"cursorY: {CursorY}");
        builder.AppendLine($"lastUserGoal: {LastUserGoal ?? "(none)"}");
        builder.AppendLine($"lastActionResult: {LastActionResult ?? "(none)"}");
        builder.AppendLine($"screenshotPath: {Screenshot?.FilePath ?? "(none)"}");
        builder.AppendLine(
            $"screenshotSize: {(Screenshot is null ? "(none)" : $"{Screenshot.Width}x{Screenshot.Height}")}");

        if (Monitors.Count > 0)
        {
            builder.AppendLine($"monitors ({Monitors.Count}):");
            foreach (var m in Monitors)
            {
                var primary = m.IsPrimary ? " [primary]" : string.Empty;
                builder.AppendLine($"  [{m.Index}] {m.Width}x{m.Height} @({m.X},{m.Y}) scale={m.Scale:F2}{primary}");
            }
        }
        else
        {
            builder.AppendLine("monitors: (not enumerated)");
        }

        if (Windows.Count > 0)
        {
            builder.AppendLine($"visibleWindows ({Windows.Count}):");
            foreach (var window in Windows.Take(20))
            {
                builder.AppendLine($"  {window.ToPromptLine()}");
            }
        }
        else
        {
            builder.AppendLine("visibleWindows: (none)");
        }

        if (UiTree is not null)
        {
            builder.AppendLine(UiTree.ToPromptSummary());
        }
        else
        {
            builder.AppendLine("uiElements (active window): (not captured)");
        }

        return builder.ToString().TrimEnd();
    }

    public string WindowsToJson() =>
        JsonSerializer.Serialize(Windows.Select(window => new
        {
            window.WindowId,
            window.Title,
            window.ProcessName,
            foreground = window.IsForeground,
            minimized = window.IsMinimized,
            maximized = window.IsMaximized
        }));

    public string? UiTreeToJson() =>
        UiTree is null
            ? null
            : JsonSerializer.Serialize(new
            {
                windowTitle = UiTree.WindowTitle,
                elementCount = UiTree.Elements.Count,
                truncated = UiTree.Truncated,
                elements = UiTree.Elements.Take(40).Select(element => new
                {
                    element.ElementId,
                    element.ControlType,
                    element.Name,
                    element.Value,
                    element.IsEnabled
                })
            });

    public string ToJsonSummary() =>
        JsonSerializer.Serialize(new
        {
            timestamp = Timestamp,
            activeWindowTitle = ActiveWindowTitle,
            activeProcessName = ActiveProcessName,
            activeProcessId = ActiveProcessId,
            screenWidth = ScreenWidth,
            screenHeight = ScreenHeight,
            cursorX = CursorX,
            cursorY = CursorY,
            lastUserGoal = LastUserGoal,
            lastActionResult = LastActionResult,
            screenshotPath = Screenshot?.FilePath,
            screenshotWidth = Screenshot?.Width,
            screenshotHeight = Screenshot?.Height,
            windows = Windows.Select(window => new
            {
                window.WindowId,
                window.Title,
                window.ProcessName,
                foreground = window.IsForeground,
                minimized = window.IsMinimized,
                maximized = window.IsMaximized
            }),
            uiTree = UiTree is null
                ? null
                : new
                {
                    windowTitle = UiTree.WindowTitle,
                    elementCount = UiTree.Elements.Count,
                    truncated = UiTree.Truncated,
                    elements = UiTree.Elements.Take(40).Select(element => new
                    {
                        element.ElementId,
                        element.ControlType,
                        element.Name,
                        element.Value,
                        element.IsEnabled
                    })
                }
        });
}
