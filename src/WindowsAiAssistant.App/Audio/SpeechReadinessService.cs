using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

public sealed class SpeechReadinessService
{
    private readonly AudioOptions _options;
    private readonly MicrophonePermissionService _permissions;
    private readonly VoskWakeWordModelService _voskModels;
    private readonly WhisperModelService _whisperModels;

    public SpeechReadinessService(
        AudioOptions options,
        MicrophonePermissionService permissions,
        VoskWakeWordModelService voskModels,
        WhisperModelService whisperModels)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _voskModels = voskModels ?? throw new ArgumentNullException(nameof(voskModels));
        _whisperModels = whisperModels ?? throw new ArgumentNullException(nameof(whisperModels));
    }

    public bool UsesVoskForWakeWord() =>
        string.Equals(_options.WakeWordEngine, "vosk", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(_options.WakeWordEngine);

    public bool UsesVoskForStt() =>
        string.Equals(_options.SpeechEngine, "vosk", StringComparison.OrdinalIgnoreCase);

    public bool UsesWhisperForStt() =>
        string.Equals(_options.SpeechEngine, "whisper", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(_options.SpeechEngine) ||
        (string.Equals(_options.SpeechEngine, "windows", StringComparison.OrdinalIgnoreCase)
            && !IsWindowsSttLanguageSupported());

    public bool IsWindowsSttLanguageSupported() =>
        string.IsNullOrWhiteSpace(_options.SpeechLanguage) ||
        WindowsSpeechLanguageCatalog.IsTopicLanguageAvailable(_options.SpeechLanguage);

    public bool IsWakeWordServiceAvailable() =>
        UsesVoskForWakeWord() && _voskModels.IsWakeModelReady();

    public bool IsSttServiceAvailable() =>
        UsesWhisperForStt()
            ? _whisperModels.IsModelReady()
            : UsesVoskForStt()
                ? _voskModels.IsSttModelReady()
                : IsWindowsSttLanguageSupported();

    public string DescribeLanguageSupport()
    {
        if (UsesWhisperForStt())
        {
            return _whisperModels.DescribeAvailability();
        }

        if (UsesVoskForStt())
        {
            return _voskModels.DescribeSttAvailability();
        }

        return WindowsSpeechLanguageCatalog.DescribeTopicAvailability(_options.SpeechLanguage);
    }

    public string DescribeWakeWordSupport() =>
        UsesVoskForWakeWord()
            ? _voskModels.DescribeWakeAvailability()
            : "Uyandırma motoru yapılandırılmadı.";

    public string DescribeActiveSttEngine()
    {
        if (UsesWhisperForStt())
        {
            var descriptor = WhisperModelCatalog.Resolve(_options.WhisperModelVariant);
            return $"Aktif komut motoru: Whisper ({descriptor.DisplayName})";
        }

        if (UsesVoskForStt())
        {
            return "Aktif komut motoru: Vosk (yerel)";
        }

        if (string.Equals(_options.SpeechEngine, "windows", StringComparison.OrdinalIgnoreCase)
            && !IsWindowsSttLanguageSupported())
        {
            var descriptor = WhisperModelCatalog.Resolve(_options.WhisperModelVariant);
            return $"Aktif komut motoru: Whisper ({descriptor.DisplayName}, Windows Türkçe paketi olmadığı için otomatik seçildi)";
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
            var path = await _whisperModels.EnsureModelAsync(cancellationToken).ConfigureAwait(false);
            _options.WhisperModelPath = path;
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
        if (!UsesVoskForWakeWord())
        {
            throw new SpeechAccessException("Uyandırma motoru desteklenmiyor.");
        }

        var access = await _permissions.RequestAccessAsync(cancellationToken).ConfigureAwait(false);
        if (access != MicrophoneAccessState.Granted)
        {
            throw new SpeechAccessException(MicrophonePermissionService.Describe(access));
        }

        await _voskModels.EnsureWakeModelAsync(cancellationToken).ConfigureAwait(false);
    }
}
