using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

public enum SpeechModelDownloadState
{
    NotDownloaded,
    Downloading,
    Downloaded,
    Failed
}

public sealed record SpeechModelInfo(
    string Id,
    string Category,
    string DisplayName,
    string SizeHint,
    string Detail,
    bool IsActiveForSettings);

public sealed class SpeechModelInventoryService
{
    private readonly AudioOptions _options;
    private readonly WhisperModelService _whisperModels;
    private readonly VoskWakeWordModelService _voskModels;

    public SpeechModelInventoryService(
        AudioOptions options,
        WhisperModelService whisperModels,
        VoskWakeWordModelService voskModels)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _whisperModels = whisperModels ?? throw new ArgumentNullException(nameof(whisperModels));
        _voskModels = voskModels ?? throw new ArgumentNullException(nameof(voskModels));
    }

    public IReadOnlyList<SpeechModelInfo> ListAll()
    {
        var speechLanguage = VoskWakeWordModelService.ResolveLanguageKeyFromSpeechLanguage(_options.SpeechLanguage);
        var wakeLanguage = VoskWakeWordModelService.ResolveLanguageKeyFromWakePhrase(_options.WakeWordPhrase);
        var activeWhisper = WhisperModelCatalog.NormalizeVariant(_options.WhisperModelVariant);
        var activeVoskStt = VoskModelCatalog.NormalizeVariant(_options.VoskModelVariant);
        var usesWhisper = UsesWhisperForStt();
        var usesVoskStt = UsesVoskForStt();

        var items = new List<SpeechModelInfo>();

        foreach (var (variantId, descriptor) in WhisperModelCatalog.AllVariants())
        {
            items.Add(new SpeechModelInfo(
                WhisperId(variantId),
                "Komut dinleme (Whisper)",
                descriptor.DisplayName,
                descriptor.SizeHint,
                descriptor.FileName,
                usesWhisper && variantId.Equals(activeWhisper, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var languageKey in new[] { "tr", "en" })
        {
            var wakeDescriptor = VoskModelCatalog.Resolve(languageKey, "small");
            items.Add(new SpeechModelInfo(
                WakeId(languageKey),
                "Uyandırma (Vosk)",
                languageKey.Equals("en", StringComparison.OrdinalIgnoreCase) ? "İngilizce uyandırma" : "Türkçe uyandırma",
                wakeDescriptor.SizeHint,
                wakeDescriptor.FolderName,
                wakeLanguage.Equals(languageKey, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var languageKey in new[] { "tr", "en" })
        {
            foreach (var variant in new[] { "small", "accurate" })
            {
                var descriptor = VoskModelCatalog.Resolve(languageKey, variant);
                items.Add(new SpeechModelInfo(
                    SttId(languageKey, variant),
                    "Komut dinleme (Vosk)",
                    $"{descriptor.DisplayName} ({(languageKey.Equals("en", StringComparison.OrdinalIgnoreCase) ? "EN" : "TR")})",
                    descriptor.SizeHint,
                    descriptor.FolderName,
                    usesVoskStt
                    && speechLanguage.Equals(languageKey, StringComparison.OrdinalIgnoreCase)
                    && variant.Equals(activeVoskStt, StringComparison.OrdinalIgnoreCase)));
            }
        }

        return items;
    }

    public SpeechModelDownloadState GetState(string modelId)
    {
        if (TryParseWhisper(modelId, out var whisperVariant))
        {
            return _whisperModels.IsVariantReady(whisperVariant)
                ? SpeechModelDownloadState.Downloaded
                : SpeechModelDownloadState.NotDownloaded;
        }

        if (TryParseWake(modelId, out var wakeLanguage))
        {
            return _voskModels.IsWakeLanguageReady(wakeLanguage)
                ? SpeechModelDownloadState.Downloaded
                : SpeechModelDownloadState.NotDownloaded;
        }

        if (TryParseStt(modelId, out var sttLanguage, out var sttVariant))
        {
            return _voskModels.IsSttLanguageReady(sttLanguage, sttVariant)
                ? SpeechModelDownloadState.Downloaded
                : SpeechModelDownloadState.NotDownloaded;
        }

        return SpeechModelDownloadState.NotDownloaded;
    }

    public async Task DownloadAsync(
        string modelId,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (TryParseWhisper(modelId, out var whisperVariant))
        {
            await _whisperModels.EnsureVariantAsync(whisperVariant, progress, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (TryParseWake(modelId, out var wakeLanguage))
        {
            await _voskModels.EnsureWakeLanguageAsync(wakeLanguage, progress, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (TryParseStt(modelId, out var sttLanguage, out var sttVariant))
        {
            await _voskModels.EnsureSttLanguageAsync(sttLanguage, sttVariant, progress, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        throw new ArgumentException($"Bilinmeyen model kimliği: {modelId}", nameof(modelId));
    }

    public static string DescribeState(SpeechModelDownloadState state) =>
        state switch
        {
            SpeechModelDownloadState.Downloaded => "İndirildi",
            SpeechModelDownloadState.Downloading => "İndiriliyor",
            SpeechModelDownloadState.Failed => "Hata",
            _ => "İndirilmedi"
        };

    private bool UsesWhisperForStt() =>
        string.Equals(_options.SpeechEngine, "whisper", StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(_options.SpeechEngine)
        || (string.Equals(_options.SpeechEngine, "windows", StringComparison.OrdinalIgnoreCase)
            && !WindowsSpeechLanguageCatalog.IsTopicLanguageAvailable(_options.SpeechLanguage));

    private bool UsesVoskForStt() =>
        string.Equals(_options.SpeechEngine, "vosk", StringComparison.OrdinalIgnoreCase);

    private static string WhisperId(string variant) => $"whisper:{variant}";

    private static string WakeId(string languageKey) => $"vosk-wake:{languageKey}";

    private static string SttId(string languageKey, string variant) => $"vosk-stt:{languageKey}:{variant}";

    private static bool TryParseWhisper(string modelId, out string variant)
    {
        variant = string.Empty;
        if (!modelId.StartsWith("whisper:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        variant = modelId["whisper:".Length..];
        return !string.IsNullOrWhiteSpace(variant);
    }

    private static bool TryParseWake(string modelId, out string languageKey)
    {
        languageKey = string.Empty;
        if (!modelId.StartsWith("vosk-wake:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        languageKey = modelId["vosk-wake:".Length..];
        return languageKey is "tr" or "en";
    }

    private static bool TryParseStt(string modelId, out string languageKey, out string variant)
    {
        languageKey = string.Empty;
        variant = string.Empty;
        if (!modelId.StartsWith("vosk-stt:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = modelId.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        languageKey = parts[1];
        variant = parts[2].Equals("accurate", StringComparison.OrdinalIgnoreCase) ? "accurate" : "small";
        return languageKey is "tr" or "en";
    }
}
