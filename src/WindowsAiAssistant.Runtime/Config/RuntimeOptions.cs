namespace WindowsAiAssistant.Runtime.Config;

public sealed class RuntimeOptions
{
    public string LogsDirectory { get; set; } = "logs/runs";
    public string ScreenshotsDirectory { get; set; } = "logs/screenshots";
}
