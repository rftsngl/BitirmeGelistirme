using System.IO.Compression;
using System.Net.Http;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Vosk modellerini (uyandirma + komut dinleme) cozer ve gerektiginde indirir.
/// </summary>
public sealed class VoskWakeWordModelService
{
    private static readonly IReadOnlyDictionary<string, (string FolderName, string DownloadUrl)> Catalog =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["tr"] = (
                "vosk-model-small-tr-0.3",
                "https://alphacephei.com/vosk/models/vosk-model-small-tr-0.3.zip"),
            ["en"] = (
                "vosk-model-small-en-us-0.15",
                "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip")
        };

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
        TryResolveModelDirectory(ResolveLanguageKeyFromWakePhrase(wakeWordPhrase ?? _options.WakeWordPhrase), out _);

    public bool IsSttModelReady(string? speechLanguage = null) =>
        TryResolveModelDirectory(ResolveLanguageKeyFromSpeechLanguage(speechLanguage ?? _options.SpeechLanguage), out _);

    public string DescribeWakeAvailability(string? wakeWordPhrase = null)
    {
        var phrase = wakeWordPhrase ?? _options.WakeWordPhrase;
        if (TryResolveModelDirectory(ResolveLanguageKeyFromWakePhrase(phrase), out var path))
        {
            return $"Vosk uyandırma modeli hazır ({Path.GetFileName(path)}).";
        }

        var descriptor = Catalog[ResolveLanguageKeyFromWakePhrase(phrase)];
        return
            $"Vosk uyandırma modeli henüz indirilmedi ({descriptor.FolderName}). " +
            "İlk kullanımda otomatik indirilir (~35–40 MB). " +
            $"Beklenen konum: {GetModelDirectory(descriptor.FolderName)}";
    }

    public string DescribeSttAvailability(string? speechLanguage = null)
    {
        if (TryResolveModelDirectory(
                ResolveLanguageKeyFromSpeechLanguage(speechLanguage ?? _options.SpeechLanguage),
                out var path))
        {
            return $"Komut dinleme Vosk ile hazır ({Path.GetFileName(path)}). Tamamen yerel çalışır.";
        }

        var descriptor = Catalog[ResolveLanguageKeyFromSpeechLanguage(speechLanguage ?? _options.SpeechLanguage)];
        return
            $"Vosk komut modeli henüz indirilmedi ({descriptor.FolderName}). " +
            "Sesli komut verdiğinizde otomatik indirilir (~35–40 MB). " +
            $"Beklenen konum: {GetModelDirectory(descriptor.FolderName)}";
    }

    public Task<string> EnsureWakeModelAsync(CancellationToken cancellationToken = default) =>
        EnsureModelForLanguageKeyAsync(ResolveLanguageKeyFromWakePhrase(_options.WakeWordPhrase), cancellationToken);

    public Task<string> EnsureSttModelAsync(CancellationToken cancellationToken = default) =>
        EnsureModelForLanguageKeyAsync(
            ResolveLanguageKeyFromSpeechLanguage(_options.SpeechLanguage),
            cancellationToken);

    public Task<string> EnsureModelAsync(CancellationToken cancellationToken = default) =>
        EnsureWakeModelAsync(cancellationToken);

    private async Task<string> EnsureModelForLanguageKeyAsync(
        string languageKey,
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

        if (TryResolveModelDirectory(languageKey, out var existing))
        {
            return existing;
        }

        var descriptor = Catalog[languageKey];
        var targetDirectory = GetModelDirectory(descriptor.FolderName);
        TryPromoteStagedModel(descriptor.FolderName);
        if (TryResolveModelDirectory(languageKey, out existing))
        {
            return existing;
        }

        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TryPromoteStagedModel(descriptor.FolderName);
            if (TryResolveModelDirectory(languageKey, out existing))
            {
                return existing;
            }

            Directory.CreateDirectory(_modelsRoot);
            var zipPath = Path.Combine(_modelsRoot, $"{descriptor.FolderName}.zip");
            if (!File.Exists(zipPath))
            {
                await DownloadFileAsync(descriptor.DownloadUrl, zipPath, cancellationToken).ConfigureAwait(false);
            }

            ExtractModelZip(zipPath, _modelsRoot, descriptor.FolderName);

            if (!TryResolveModelDirectory(languageKey, out existing))
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

    private bool TryResolveModelDirectory(string languageKey, out string path)
    {
        path = string.Empty;
        var configured = ResolveConfiguredModelPath();
        if (!string.IsNullOrWhiteSpace(configured) && IsValidModelDirectory(configured))
        {
            path = configured;
            return true;
        }

        var descriptor = Catalog[languageKey];
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

    private static async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        await using var response = await client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(destination);
        await response.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
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
