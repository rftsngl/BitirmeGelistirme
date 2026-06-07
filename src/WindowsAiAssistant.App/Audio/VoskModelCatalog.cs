namespace WindowsAiAssistant.App.Audio;

internal sealed record VoskModelDescriptor(
    string FolderName,
    string DownloadUrl,
    string DisplayName,
    string SizeHint,
    string? Note = null);

/// <summary>
/// Resmi Vosk model katalogu. Türkçe icin halka acik yalnizca kucuk model vardir;
/// Ingilizce icin daha buyuk lgraph modeli desteklenir.
/// </summary>
internal static class VoskModelCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, VoskModelDescriptor>> Catalog =
        new Dictionary<string, IReadOnlyDictionary<string, VoskModelDescriptor>>(StringComparer.OrdinalIgnoreCase)
        {
            ["tr"] = new Dictionary<string, VoskModelDescriptor>(StringComparer.OrdinalIgnoreCase)
            {
                ["small"] = new(
                    "vosk-model-small-tr-0.3",
                    "https://alphacephei.com/vosk/models/vosk-model-small-tr-0.3.zip",
                    "Hızlı (küçük)",
                    "~35 MB"),
                ["accurate"] = new(
                    "vosk-model-small-tr-0.3",
                    "https://alphacephei.com/vosk/models/vosk-model-small-tr-0.3.zip",
                    "Gelişmiş (Türkçe)",
                    "~35 MB",
                    "Halka açık büyük Türkçe Vosk modeli yok. Yabancı kelime ve karma dil için Ayarlar'dan Whisper motorunu seçin " +
                    "veya contact@alphacephei.com üzerinden büyük model talep edin.")
            },
            ["en"] = new Dictionary<string, VoskModelDescriptor>(StringComparer.OrdinalIgnoreCase)
            {
                ["small"] = new(
                    "vosk-model-small-en-us-0.15",
                    "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip",
                    "Hızlı (küçük)",
                    "~40 MB"),
                ["accurate"] = new(
                    "vosk-model-en-us-0.22-lgraph",
                    "https://alphacephei.com/vosk/models/vosk-model-en-us-0.22-lgraph.zip",
                    "Dengeli (önerilen)",
                    "~128 MB",
                    "İngilizce komutlar ve uyandırma için küçük modele göre belirgin şekilde daha iyi tanıma.")
            }
        };

    public static VoskModelDescriptor Resolve(string languageKey, string? variant)
    {
        var lang = languageKey.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "tr";
        var normalizedVariant = NormalizeVariant(variant);
        return Catalog[lang][normalizedVariant];
    }

    public static IReadOnlyList<(string Id, string Label, string Description)> GetVariantChoices(string languageKey)
    {
        var lang = languageKey.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "tr";
        return Catalog[lang]
            .Select(pair =>
            {
                var descriptor = pair.Value;
                var description = $"{descriptor.SizeHint}. {descriptor.Note ?? descriptor.DisplayName}";
                return (pair.Key, $"{descriptor.DisplayName} ({descriptor.SizeHint})", description.Trim());
            })
            .ToList();
    }

    public static string NormalizeVariant(string? variant) =>
        string.Equals(variant, "accurate", StringComparison.OrdinalIgnoreCase) ? "accurate" : "small";
}
