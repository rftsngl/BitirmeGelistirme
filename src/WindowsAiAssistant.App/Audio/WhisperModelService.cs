using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Whisper ggml modellerini cozer. Derleme ciktisindaki models/whisper onceliklidir.
/// </summary>
public sealed class WhisperModelService
{
    private readonly AudioOptions _options;
    private readonly string _userModelsRoot;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public WhisperModelService(AudioOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _userModelsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAiAssistant",
            "models",
            "whisper");
    }

    public string BundledModelsRoot =>
        Path.Combine(AppContext.BaseDirectory, "models", "whisper");

    public string UserModelsRoot => _userModelsRoot;

    public bool IsModelReady() => IsVariantReady(_options.WhisperModelVariant);

    public bool IsVariantReady(string? variant)
    {
        var descriptor = WhisperModelCatalog.Resolve(variant);
        return File.Exists(GetBundledModelPath(descriptor.FileName))
               || File.Exists(Path.Combine(_userModelsRoot, descriptor.FileName));
    }

    public string DescribeAvailability()
    {
        if (IsModelReady())
        {
            var descriptor = WhisperModelCatalog.Resolve(_options.WhisperModelVariant);
            var path = ResolveModelPathOrNull();
            var source = IsBundledPath(path) ? "uygulama ile birlikte" : "yerel";
            return $"Komut dinleme Whisper ile hazır ({descriptor.FileName}, {descriptor.SizeHint}, {source}).";
        }

        var pending = WhisperModelCatalog.Resolve(_options.WhisperModelVariant);
        return
            $"Whisper modeli indirilmedi ({pending.FileName}, {pending.SizeHint}). " +
            "Ayarlar'dan indirebilir veya ilk komut dinlemede otomatik indirilir.";
    }

    public Task<string> EnsureModelAsync(CancellationToken cancellationToken = default) =>
        EnsureVariantAsync(_options.WhisperModelVariant, progress: null, cancellationToken);

    public async Task<string> EnsureVariantAsync(
        string? variant,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var configured = ResolveConfiguredModelPath();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured))
            {
                throw new SpeechAccessException($"Whisper model dosyası bulunamadı: {configured}");
            }

            return configured;
        }

        var descriptor = WhisperModelCatalog.Resolve(variant);
        var bundledPath = GetBundledModelPath(descriptor.FileName);
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        var userPath = Path.Combine(_userModelsRoot, descriptor.FileName);
        if (File.Exists(userPath))
        {
            return userPath;
        }

        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(bundledPath))
            {
                return bundledPath;
            }

            if (File.Exists(userPath))
            {
                return userPath;
            }

            Directory.CreateDirectory(_userModelsRoot);
            var tempPath = userPath + ".download";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            await ModelFileDownloader
                .DownloadFileAsync(descriptor.DownloadUrl, tempPath, progress, cancellationToken)
                .ConfigureAwait(false);
            File.Move(tempPath, userPath, overwrite: true);
            return userPath;
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    public string? ResolveModelPathOrNull()
    {
        var configured = ResolveConfiguredModelPath();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var descriptor = WhisperModelCatalog.Resolve(_options.WhisperModelVariant);
        var bundledPath = GetBundledModelPath(descriptor.FileName);
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        var userPath = Path.Combine(_userModelsRoot, descriptor.FileName);
        return File.Exists(userPath) ? userPath : null;
    }

    private string GetBundledModelPath(string fileName) =>
        Path.Combine(BundledModelsRoot, fileName);

    private bool IsBundledPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.StartsWith(BundledModelsRoot, StringComparison.OrdinalIgnoreCase);

    private string? ResolveConfiguredModelPath()
    {
        if (string.IsNullOrWhiteSpace(_options.WhisperModelPath))
        {
            return null;
        }

        return Path.GetFullPath(_options.WhisperModelPath.Trim());
    }
}
