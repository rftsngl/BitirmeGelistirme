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
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Overlay ActionGate onay ekraninda kisa STT ile "evet/hayir" sesli onayi etkin.
    /// </summary>
    public bool VoiceApprovalEnabled { get; set; } = true;

    /// <summary>
    /// STT motoru: "windows" (varsayilan) veya "whisper" (yerel Whisper.net modeli).
    /// </summary>
    public string SpeechEngine { get; set; } = "windows";

    /// <summary>
    /// Whisper.net ggml model dosyasinin tam yolu (SpeechEngine=whisper icin gerekli).
    /// </summary>
    public string WhisperModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Yakalama yapilacak mikrofon cihaz indeksi. -1 = varsayilan cihaz.
    /// </summary>
    public int InputDeviceIndex { get; set; } = -1;

    /// <summary>
    /// Gelistirici Modu: LLM ciktilari, action ayrintilari, UIA agaci ve teknik loglari gosterir.
    /// </summary>
    public bool DeveloperModeEnabled { get; set; }
}
