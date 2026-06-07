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
    public int SpeechListenTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Konusma bittikten sonra kaydi bitirmek icin gereken susma suresi (ms).
    /// Yuksek = yabanci kelimeler ve duraklamalar icin daha fazla sure.
    /// </summary>
    public int SilenceEndMilliseconds { get; set; } = 1400;

    /// <summary>
    /// VAD konusma esigi carpani. Yuksek = daha az yanlis tetikleme (ortam gurultusu).
    /// </summary>
    public double VadSpeechMultiplier { get; set; } = 3.2;

    /// <summary>
    /// Kayit sonlandirmadan once gereken minimum konusma suresi (ms).
    /// </summary>
    public int VadMinSpeechMilliseconds { get; set; } = 380;

    /// <summary>
    /// Ortam gurultusu kalibrasyon suresi (ms).
    /// </summary>
    public int VadCalibrationMilliseconds { get; set; } = 500;

    /// <summary>
    /// Mikrofon kazanc hedefi (0.2–0.8). Dusuk = daha az amplifikasyon.
    /// </summary>
    public double MicGainTargetPeak { get; set; } = 0.55;

    /// <summary>
    /// Kabul edilecek minimum transcript uzunlugu (karakter).
    /// </summary>
    public int SttMinTranscriptCharacters { get; set; } = 2;

    /// <summary>
    /// Uyandirma yalnizca kesin (final) Vosk sonucunda tetiklensin; partial sonuclar yok sayilir.
    /// </summary>
    public bool WakeWordFinalOnly { get; set; } = true;

    /// <summary>
    /// Uyandirma tetiklendikten sonra tekrar dinleme bekleme suresi (saniye).
    /// </summary>
    public int WakeWordCooldownSeconds { get; set; } = 8;

    /// <summary>
    /// Uyandirma tetiklenmeden once mikrofonda gercek konusma algilanmali (arka plan gurultusunu filtreler).
    /// </summary>
    public bool WakeWordRequireSpeechEnergy { get; set; } = true;

    /// <summary>
    /// Uyandirma icin minimum ses seviyesi (RMS). Dusuk = daha kolay tetiklenir.
    /// </summary>
    public double WakeWordMinPeakLevel { get; set; } = 0.011;

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
    /// Uyandirma kelimesi motoru. Yalnizca "vosk" desteklenir (hafif, grammar tabanli).
    /// </summary>
    public string WakeWordEngine { get; set; } = "vosk";

    /// <summary>
    /// Komut dinleme motoru: vosk, whisper veya windows.
    /// </summary>
    public string SpeechEngine { get; set; } = "whisper";

    /// <summary>
    /// Whisper model boyutu: base, small (onerilen), medium.
    /// </summary>
    public string WhisperModelVariant { get; set; } = "medium";

    /// <summary>
    /// Manuel Whisper ggml model dosya yolu. Bos ise otomatik indirilen model kullanilir.
    /// </summary>
    public string WhisperModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Vosk model kalitesi: small (hizli) veya accurate (dile gore daha iyi; TR icin not ile ayni kucuk model).
    /// </summary>
    public string VoskModelVariant { get; set; } = "small";

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
