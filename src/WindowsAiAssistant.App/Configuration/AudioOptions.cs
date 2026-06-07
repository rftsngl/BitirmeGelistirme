namespace WindowsAiAssistant.App.Configuration;

public sealed class AudioOptions
{
    public bool BackgroundModeEnabled { get; set; } = true;
    public bool StartMinimizedToTray { get; set; } = true;
    public bool WakeWordEnabled { get; set; }

    /// <summary>
    /// Uyandirma kelimesi onay kimligi (or. asistan, bilgisayar, computer).
    /// </summary>
    public string WakeWordPhrase { get; set; } = "asistan";

    public bool GlobalHotKeyEnabled { get; set; } = true;
    public string GlobalHotKey { get; set; } = "Ctrl+Alt+A";
    public bool TextToSpeechEnabled { get; set; } = true;

    /// <summary>
    /// TTS motoru: edge (neural, dogal) veya windows (yerel).
    /// </summary>
    public string TtsEngine { get; set; } = "edge";

    /// <summary>
    /// TTS sesi (or. tr-TR-EmelNeural). Bos ise dil icin varsayilan neural ses secilir.
    /// </summary>
    public string TtsVoiceName { get; set; } = string.Empty;

    /// <summary>
    /// Konusma hizi carpani (0.5–2.0). 1.0 = normal.
    /// </summary>
    public double TtsSpeakingRate { get; set; } = 0.95;

    public string SpeechLanguage { get; set; } = "tr-TR";
    public int SpeechListenTimeoutSeconds { get; set; } = 8;

    /// <summary>
    /// Konusma bittikten sonra kaydi bitirmek icin gereken susma suresi (ms).
    /// </summary>
    public int SilenceEndMilliseconds { get; set; } = 700;

    /// <summary>
    /// Asistan cevap verdikten sonra takip komutu icin dinleme suresi (saniye).
    /// </summary>
    public int FollowUpListenTimeoutSeconds { get; set; } = 10;

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
    /// Vosk uyandirma model klasoru (gelistirici modu). Bos ise otomatik indirilen model kullanilir.
    /// </summary>
    public string VoskModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Yakalama yapilacak mikrofon cihaz indeksi. -1 = varsayilan cihaz.
    /// </summary>
    public int InputDeviceIndex { get; set; } = -1;

    /// <summary>
    /// Gelistirici Modu: LLM ciktilari, action ayrintilari, UIA agaci ve teknik loglari gosterir.
    /// </summary>
    public bool DeveloperModeEnabled { get; set; }
}
