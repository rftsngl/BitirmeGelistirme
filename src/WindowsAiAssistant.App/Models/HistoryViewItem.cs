namespace WindowsAiAssistant.App.Models;

public sealed class HistoryViewItem
{
    public string RunId { get; init; } = string.Empty;
    public string LogFilePath { get; init; } = string.Empty;
    public string CommandText { get; init; } = string.Empty;
    public string FinalStatus { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string SelectedTool { get; init; } = string.Empty;
    public string TriggerSource { get; init; } = string.Empty;
    public int StepCount { get; init; }
    public string LastResult { get; init; } = string.Empty;

    public string TimestampDisplay => TimestampUtc.ToLocalTime().ToString("g");
    public string StepCountDisplay => $"{StepCount} adim";
    public string TriggerDisplay => TriggerSource switch
    {
        "chat" => "Sohbet",
        "voice_overlay" => "Sesli popup",
        "hotkey" => "Klavye kisayolu",
        _ => TriggerSource
    };
}

public sealed class HistoryStepItem
{
    public int StepIndex { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string GateOutcome { get; init; } = string.Empty;
    public string GateRisk { get; init; } = string.Empty;
    public string? GateUserApproved { get; init; }
    public string ObservationSummary { get; init; } = string.Empty;
    public string WindowsSummary { get; init; } = string.Empty;
    public string UiTreeSummary { get; init; } = string.Empty;
    public string ParsedDecision { get; init; } = string.Empty;
    public string GateDecision { get; init; } = string.Empty;
    public string ActionResult { get; init; } = string.Empty;
    public string LlmRawOutput { get; init; } = string.Empty;
    public string? ScreenshotPath { get; init; }
    public string UiTreeRawJson { get; init; } = string.Empty;
    public bool HasScreenshot => !string.IsNullOrWhiteSpace(ScreenshotPath) && File.Exists(ScreenshotPath);
    public string StatusBadge => Success ? "OK" : "FAIL";
    public bool UiTreeTruncated => UiTreeRawJson.Contains("\"truncated\":true", StringComparison.Ordinal);
    public string UiTreeCharInfo => UiTreeRawJson.Length > 0 ? $"{UiTreeRawJson.Length} karakter" : string.Empty;
    public string GateRiskDisplay => GateRisk switch
    {
        "Safe" => "low",
        "Normal" => "low",
        "Sensitive" => "medium",
        "Destructive" => "critical",
        _ => GateRisk.ToLowerInvariant()
    };
}
