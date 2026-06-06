namespace WindowsAiAssistant.Runtime.Config;

public sealed class UiAutomationOptions
{
    public int MaxDepth { get; set; } = 4;
    public int MaxElementsPerStep { get; set; } = 80;
    public int CaptureTimeoutMs { get; set; } = 8000;
}
