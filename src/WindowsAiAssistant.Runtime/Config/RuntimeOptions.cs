namespace WindowsAiAssistant.Runtime.Config;

public sealed class RuntimeOptions
{
    public string LogsDirectory { get; set; } = "logs/runs";
    public string ScreenshotsDirectory { get; set; } = "logs/screenshots";
    public UiAutomationOptions UiAutomation { get; set; } = new();
    public Policy.ActionPolicy ActionPolicy { get; set; } = new();
    public ComAutomationOptions ComAutomation { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
    public ObservationOptions Observation { get; set; } = new();
}
