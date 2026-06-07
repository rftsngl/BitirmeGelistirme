using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

public sealed class SpeechReadinessService
{
    private readonly AudioOptions _options;
    private readonly MicrophonePermissionService _permissions;
    private readonly VoskWakeWordModelService _voskModels;

    public SpeechReadinessService(
        AudioOptions options,
        MicrophonePermissionService permissions,
        VoskWakeWordModelService voskModels)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _voskModels = voskModels ?? throw new ArgumentNullException(nameof(voskModels));
    }

    public bool UsesVoskForStt() =>
        string.Equals(_options.SpeechEngine, "vosk", StringComparison.OrdinalIgnoreCase) ||
        (!UsesWhisperForStt() && !IsWindowsSttLanguageSupported());

    public bool UsesWhisperForStt() =>
        _options.DeveloperModeEnabled &&
        string.Equals(_options.SpeechEngine, "whisper", StringComparison.OrdinalIgnoreCase);

    public bool IsWindowsSttLanguageSupported() =>
        string.IsNullOrWhiteSpace(_options.SpeechLanguage) ||
        WindowsSpeechLanguageCatalog.IsTopicLanguageAvailable(_options.SpeechLanguage);

    public bool IsWakeWordServiceAvailable() => _voskModels.IsWakeModelReady();

    public bool IsSttServiceAvailable() =>
        UsesVoskForStt()
            ? _voskModels.IsSttModelReady()
            : UsesWhisperForStt()
                ? !string.IsNullOrWhiteSpace(_options.WhisperModelPath) && File.Exists(_options.WhisperModelPath)
                : IsWindowsSttLanguageSupported();

    public string DescribeLanguageSupport()
    {
        if (UsesWhisperForStt())
        {
            return !string.IsNullOrWhiteSpace(_options.WhisperModelPath) && File.Exists(_options.WhisperModelPath)
                ? $"Komut dinleme Whisper ile hazır ({_options.WhisperModelPath})."
                : "Whisper modeli bulunamadı. Geliştirici modunda model yolunu ayarlayın.";
        }

        if (UsesVoskForStt())
        {
            return _voskModels.DescribeSttAvailability();
        }

        return WindowsSpeechLanguageCatalog.DescribeTopicAvailability(_options.SpeechLanguage);
    }

    public string DescribeWakeWordSupport() =>
        _voskModels.DescribeWakeAvailability();

    public string DescribeActiveSttEngine()
    {
        if (UsesWhisperForStt())
        {
            return "Aktif komut motoru: Whisper";
        }

        if (UsesVoskForStt())
        {
            return string.Equals(_options.SpeechEngine, "vosk", StringComparison.OrdinalIgnoreCase)
                ? "Aktif komut motoru: Vosk (yerel)"
                : "Aktif komut motoru: Vosk (Windows Türkçe paketi olmadığı için otomatik seçildi)";
        }

        return "Aktif komut motoru: Windows (yerleşik)";
    }

    public async Task EnsureReadyForListenAsync(CancellationToken cancellationToken = default)
    {
        var access = await _permissions.RequestAccessAsync(cancellationToken).ConfigureAwait(false);
        if (access != MicrophoneAccessState.Granted)
        {
            throw new SpeechAccessException(MicrophonePermissionService.Describe(access));
        }

        if (UsesWhisperForStt())
        {
            if (string.IsNullOrWhiteSpace(_options.WhisperModelPath) || !File.Exists(_options.WhisperModelPath))
            {
                throw new SpeechAccessException(
                    "Whisper modeli bulunamadı. Geliştirici modunda model yolunu ayarlayın veya konuşma tanıma motorunu Vosk olarak seçin.");
            }

            return;
        }

        if (UsesVoskForStt())
        {
            await _voskModels.EnsureSttModelAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!IsWindowsSttLanguageSupported())
        {
            throw new SpeechAccessException(DescribeLanguageSupport());
        }
    }

    public async Task EnsureReadyForWakeWordAsync(CancellationToken cancellationToken = default)
    {
        var access = await _permissions.RequestAccessAsync(cancellationToken).ConfigureAwait(false);
        if (access != MicrophoneAccessState.Granted)
        {
            throw new SpeechAccessException(MicrophonePermissionService.Describe(access));
        }

        await _voskModels.EnsureWakeModelAsync(cancellationToken).ConfigureAwait(false);
    }
}
