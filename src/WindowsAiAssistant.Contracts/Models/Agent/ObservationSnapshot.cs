namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class ObservationSnapshot
{
    public WindowContext? ActiveWindow { get; init; }
    public string? ActiveProcessName { get; init; }
    public string? ClipboardTextPreview { get; init; }
    public bool HasSelection { get; init; }
    public string? SelectionTextPreview { get; init; }
    public string? DesktopStateSummary { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
