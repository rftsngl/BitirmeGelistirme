using System.Text.Json;

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

    public string ToShortSummary()
    {
        var window = string.IsNullOrWhiteSpace(ActiveWindowTitle) ? "(baslik yok)" : ActiveWindowTitle;
        var process = string.IsNullOrWhiteSpace(ActiveProcessName) ? "(bilinmiyor)" : ActiveProcessName;
        var screenshot = Screenshot is null ? string.Empty : " · screenshot";
        return $"{window} · {process} · {ScreenWidth}x{ScreenHeight}{screenshot}";
    }

    public string ToPromptSummary()
    {
        return $"""
            timestamp: {Timestamp:O}
            activeWindowTitle: {ActiveWindowTitle}
            activeProcessName: {ActiveProcessName}
            activeProcessId: {ActiveProcessId}
            screenWidth: {ScreenWidth}
            screenHeight: {ScreenHeight}
            cursorX: {CursorX}
            cursorY: {CursorY}
            lastUserGoal: {LastUserGoal ?? "(none)"}
            lastActionResult: {LastActionResult ?? "(none)"}
            screenshotPath: {Screenshot?.FilePath ?? "(none)"}
            screenshotSize: {(Screenshot is null ? "(none)" : $"{Screenshot.Width}x{Screenshot.Height}")}
            """;
    }

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
            screenshotHeight = Screenshot?.Height
        });
}
