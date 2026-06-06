namespace WindowsAiAssistant.App.Configuration;

public sealed class AudioOptions
{
    public bool BackgroundModeEnabled { get; set; } = true;
    public bool StartMinimizedToTray { get; set; } = true;
    public bool WakeWordEnabled { get; set; }
    public string PorcupineAccessKey { get; set; } = string.Empty;
    public string PorcupineKeywordPath { get; set; } = string.Empty;
    public string PorcupineModelPath { get; set; } = string.Empty;
    public float PorcupineSensitivity { get; set; } = 0.5f;
    public bool GlobalHotKeyEnabled { get; set; } = true;
    public string GlobalHotKey { get; set; } = "Ctrl+Alt+A";
    public bool TextToSpeechEnabled { get; set; } = true;
    public string SpeechLanguage { get; set; } = "tr-TR";
    public int SpeechListenTimeoutSeconds { get; set; } = 12;
    public int OverlayAutoCloseSeconds { get; set; } = 10;
}
