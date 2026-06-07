using System.IO.Compression;
using System.Net.Http;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Vosk modellerini (uyandirma + komut dinleme) cozer ve gerektiginde indirir.
/// </summary>
public sealed class VoskWakeWordModelService
{
    private readonly AudioOptions _options;
    private readonly string _modelsRoot;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public VoskWakeWordModelService(AudioOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _modelsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAiAssistant",
            "models");
    }

    public string ModelsRoot => _modelsRoot;

    public bool IsWakeModelReady(string? wakeWordPhrase = null) =>
        IsWakeLanguageReady(ResolveLanguageKeyFromWakePhrase(wakeWordPhrase ?? _options.WakeWordPhrase));

    public bool IsWakeLanguageReady(string languageKey) =>
        TryResolveWakeModelDirectory(languageKey, out _);

    public bool IsSttModelReady(string? speechLanguage = null) =>
        IsSttLanguageReady(
            ResolveLanguageKeyFromSpeechLanguage(speechLanguage ?? _options.SpeechLanguage),
            _options.VoskModelVariant);

    public bool IsSttLanguageReady(string languageKey, string? variant) =>
        TryResolveModelDirectory(languageKey, ResolveSttDescriptor(languageKey, variant), out _);

    public string DescribeWakeAvailability(string? wakeWordPhrase = null)
    {
        var phrase = wakeWordPhrase ?? _options.WakeWordPhrase;
        if (TryResolveWakeModelDirectory(ResolveLanguageKeyFromWakePhrase(phrase), out var path))
        {
            return $"Vosk uyandırma modeli hazır ({Path.GetFileName(path)}).";
        }

        var descriptor = ResolveWakeDescriptor(ResolveLanguageKeyFromWakePhrase(phrase));
        return
            $"Vosk uyandırma modeli henüz indirilmedi ({descriptor.FolderName}). " +
            $"İlk kullanımda otomatik indirilir ({descriptor.SizeHint}). " +
            $"Beklenen konum: {GetModelDirectory(descriptor.FolderName)}";
    }

    public string DescribeSttAvailability(string? speechLanguage = null)
    {
        if (TryResolveSttModelDirectory(
                ResolveLanguageKeyFromSpeechLanguage(speechLanguage ?? _options.SpeechLanguage),
                out var path))
        {
            var readyDescriptor = ResolveSttDescriptor(
                ResolveLanguageKeyFromSpeechLanguage(speechLanguage ?? _options.SpeechLanguage));
            var note = string.IsNullOrWhiteSpace(readyDescriptor.Note) ? string.Empty : $" {readyDescriptor.Note}";
            return $"Komut dinleme Vosk ile hazır ({Path.GetFileName(path)}, {readyDescriptor.SizeHint}).{note}";
        }

        var descriptor = ResolveSttDescriptor(
            ResolveLanguageKeyFromSpeechLanguage(speechLanguage ?? _options.SpeechLanguage));
        return
            $"Vosk komut modeli henüz indirilmedi ({descriptor.FolderName}, {descriptor.SizeHint}). " +
            "Sesli komut verdiğinizde otomatik indirilir. " +
            $"Beklenen konum: {GetModelDirectory(descriptor.FolderName)}";
    }

    public Task<string> EnsureWakeModelAsync(CancellationToken cancellationToken = default) =>
        EnsureWakeLanguageAsync(
            ResolveLanguageKeyFromWakePhrase(_options.WakeWordPhrase),
            progress: null,
            cancellationToken);

    public Task<string> EnsureWakeLanguageAsync(
        string languageKey,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default) =>
        EnsureWakeModelForLanguageKeyAsync(languageKey, progress, cancellationToken);

    public Task<string> EnsureSttModelAsync(CancellationToken cancellationToken = default) =>
        EnsureSttLanguageAsync(
            ResolveLanguageKeyFromSpeechLanguage(_options.SpeechLanguage),
            _options.VoskModelVariant,
            progress: null,
            cancellationToken);

    public Task<string> EnsureSttLanguageAsync(
        string languageKey,
        string? variant,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default) =>
        EnsureModelForLanguageKeyAsync(languageKey, variant, progress, cancellationToken);

    private async Task<string> EnsureModelForLanguageKeyAsync(
        string languageKey,
        string? variant,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var configured = ResolveConfiguredModelPath();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!IsValidModelDirectory(configured))
            {
                throw new SpeechAccessException(
                    $"Vosk model klasörü geçersiz: {configured}. 'am/final.mdl' veya 'final.mdl' bulunamadı.");
            }

            return configured;
        }

        var descriptor = ResolveSttDescriptor(languageKey, variant);
        if (TryResolveModelDirectory(languageKey, descriptor, out var existing))
        {
            return existing;
        }

        var targetDirectory = GetModelDirectory(descriptor.FolderName);
        TryPromoteStagedModel(descriptor.FolderName);
        if (TryResolveModelDirectory(languageKey, descriptor, out existing))
        {
            return existing;
        }

        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TryPromoteStagedModel(descriptor.FolderName);
            if (TryResolveModelDirectory(languageKey, descriptor, out existing))
            {
                return existing;
            }

            Directory.CreateDirectory(_modelsRoot);
            var zipPath = Path.Combine(_modelsRoot, $"{descriptor.FolderName}.zip");
            if (!File.Exists(zipPath))
            {
                await DownloadFileAsync(descriptor.DownloadUrl, zipPath, progress, 0, 88, cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(new ModelDownloadProgress(90, "Çıkarılıyor"));
            ExtractModelZip(zipPath, _modelsRoot, descriptor.FolderName);
            progress?.Report(new ModelDownloadProgress(100, "Hazır"));

            if (!TryResolveModelDirectory(languageKey, descriptor, out existing))
            {
                throw new SpeechAccessException(
                    $"Vosk modeli çıkarıldı ancak doğrulanamadı: {targetDirectory}");
            }

            return existing;
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private async Task<string> EnsureWakeModelForLanguageKeyAsync(
        string languageKey,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (TryResolveWakeModelDirectory(languageKey, out var existing))
        {
            return existing;
        }

        var descriptor = ResolveWakeDescriptor(languageKey);
        var targetDirectory = GetModelDirectory(descriptor.FolderName);

        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TryPromoteStagedModel(descriptor.FolderName);
            if (TryResolveWakeModelDirectory(languageKey, out existing))
            {
                return existing;
            }

            Directory.CreateDirectory(_modelsRoot);
            var zipPath = Path.Combine(_modelsRoot, $"{descriptor.FolderName}.zip");
            if (!File.Exists(zipPath))
            {
                await DownloadFileAsync(descriptor.DownloadUrl, zipPath, progress, 0, 88, cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(new ModelDownloadProgress(90, "Çıkarılıyor"));
            ExtractModelZip(zipPath, _modelsRoot, descriptor.FolderName);
            progress?.Report(new ModelDownloadProgress(100, "Hazır"));

            if (!TryResolveWakeModelDirectory(languageKey, out existing))
            {
                throw new SpeechAccessException(
                    $"Vosk uyandırma modeli doğrulanamadı: {targetDirectory}");
            }

            return existing;
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private bool TryResolveWakeModelDirectory(string languageKey, out string path) =>
        TryResolveModelDirectory(languageKey, ResolveWakeDescriptor(languageKey), out path);

    private bool TryResolveSttModelDirectory(string languageKey, out string path) =>
        TryResolveModelDirectory(languageKey, ResolveSttDescriptor(languageKey), out path);

    private bool TryResolveModelDirectory(string languageKey, VoskModelDescriptor descriptor, out string path)
    {
        path = string.Empty;
        var configured = ResolveConfiguredModelPath();
        if (!string.IsNullOrWhiteSpace(configured) && IsValidModelDirectory(configured))
        {
            path = configured;
            return true;
        }

        var expected = GetModelDirectory(descriptor.FolderName);
        if (IsValidModelDirectory(expected))
        {
            path = expected;
            return true;
        }

        if (!Directory.Exists(_modelsRoot))
        {
            return false;
        }

        foreach (var candidate in Directory.GetDirectories(_modelsRoot))
        {
            if (candidate.EndsWith("_extract", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsValidModelDirectory(candidate))
            {
                continue;
            }

            var name = Path.GetFileName(candidate);
            if (name.Equals(descriptor.FolderName, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(descriptor.FolderName, StringComparison.OrdinalIgnoreCase))
            {
                path = candidate;
                return true;
            }
        }

        var staged = FindModelDirectory(Path.Combine(_modelsRoot, "_extract"));
        if (!string.IsNullOrWhiteSpace(staged))
        {
            path = staged;
            return true;
        }

        return false;
    }

    private static VoskModelDescriptor ResolveWakeDescriptor(string languageKey) =>
        VoskModelCatalog.Resolve(languageKey, "small");

    private VoskModelDescriptor ResolveSttDescriptor(string languageKey, string? variant = null) =>
        VoskModelCatalog.Resolve(languageKey, variant ?? _options.VoskModelVariant);

    private string? ResolveConfiguredModelPath()
    {
        if (string.IsNullOrWhiteSpace(_options.VoskModelPath))
        {
            return null;
        }

        return Path.GetFullPath(_options.VoskModelPath.Trim());
    }

    private string GetModelDirectory(string folderName) => Path.Combine(_modelsRoot, folderName);

    public static string ResolveLanguageKeyFromWakePhrase(string? wakeWordPhrase)
    {
        var id = string.IsNullOrWhiteSpace(wakeWordPhrase) ? "asistan" : wakeWordPhrase.Trim();
        return id.Equals("computer", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("jarvis", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "tr";
    }

    public static string ResolveLanguageKeyFromSpeechLanguage(string? speechLanguage)
    {
        if (string.IsNullOrWhiteSpace(speechLanguage))
        {
            return "tr";
        }

        var primary = speechLanguage.Split('-')[0].Trim().ToLowerInvariant();
        return primary is "en" ? "en" : "tr";
    }

    private static bool IsValidModelDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        if (File.Exists(Path.Combine(path, "am", "final.mdl")))
        {
            return true;
        }

        // vosk-model-small-tr-0.3 gibi bazi modeller final.mdl dosyasini kok dizinde tutar.
        return File.Exists(Path.Combine(path, "final.mdl"));
    }

    private static string? FindModelDirectory(string root)
    {
        if (IsValidModelDirectory(root))
        {
            return root;
        }

        if (!Directory.Exists(root))
        {
            return null;
        }

        foreach (var directory in Directory.GetDirectories(root))
        {
            var nested = FindModelDirectory(directory);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }

        return null;
    }

    private void TryPromoteStagedModel(string expectedFolderName)
    {
        var stagedRoot = Path.Combine(_modelsRoot, "_extract");
        var stagedModel = FindModelDirectory(stagedRoot);
        if (string.IsNullOrWhiteSpace(stagedModel))
        {
            return;
        }

        var finalDirectory = GetModelDirectory(expectedFolderName);
        if (Directory.Exists(finalDirectory))
        {
            return;
        }

        Directory.CreateDirectory(_modelsRoot);
        Directory.Move(stagedModel, finalDirectory);

        try
        {
            if (Directory.Exists(stagedRoot))
            {
                Directory.Delete(stagedRoot, recursive: true);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<ModelDownloadProgress>? progress,
        double progressStart,
        double progressEnd,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        var scaled = progress is null
            ? null
            : new Progress<ModelDownloadProgress>(report =>
            {
                if (report.Percent < 0)
                {
                    progress.Report(new ModelDownloadProgress(-1, report.Stage));
                    return;
                }

                var scaledPercent = progressStart + (report.Percent / 100.0) * (progressEnd - progressStart);
                progress.Report(new ModelDownloadProgress(scaledPercent, report.Stage));
            });

        await ModelFileDownloader
            .DownloadFileAsync(url, destination, scaled, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ExtractModelZip(string zipPath, string destinationRoot, string expectedFolderName)
    {
        var extractRoot = Path.Combine(destinationRoot, "_extract");
        var existingModel = FindModelDirectory(extractRoot);
        if (string.IsNullOrWhiteSpace(existingModel))
        {
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, recursive: true);
            }

            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(zipPath, extractRoot);
        }

        var extractedModelDir = FindModelDirectory(extractRoot);
        if (string.IsNullOrWhiteSpace(extractedModelDir))
        {
            throw new SpeechAccessException(
                "İndirilen Vosk modeli arşivi tanınamadı. Beklenen yapı: am/final.mdl veya final.mdl.");
        }

        var finalDirectory = Path.Combine(destinationRoot, expectedFolderName);
        if (Directory.Exists(finalDirectory))
        {
            Directory.Delete(finalDirectory, recursive: true);
        }

        Directory.Move(extractedModelDir, finalDirectory);
        Directory.Delete(extractRoot, recursive: true);

        try
        {
            File.Delete(zipPath);
        }
        catch
        {
            // ignore
        }
    }
}
