namespace WindowsAiAssistant.Runtime.Observation;

public sealed class ScreenshotObservation
{
    public string FilePath { get; init; } = string.Empty;
    public string Base64Png { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}
