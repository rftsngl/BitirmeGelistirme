namespace WindowsAiAssistant.App.Audio;

internal sealed record WhisperModelDescriptor(
    string FileName,
    string DownloadUrl,
    string DisplayName,
    string SizeHint,
    string? Note = null);

internal static class WhisperModelCatalog
{
    private static readonly IReadOnlyDictionary<string, WhisperModelDescriptor> Variants =
        new Dictionary<string, WhisperModelDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["base"] = new(
                "ggml-base.bin",
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
                "Hızlı (base)",
                "~142 MB",
                "Düşük gecikme; karma dilde orta doğruluk."),
            ["small"] = new(
                "ggml-small.bin",
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
                "Dengeli (önerilen)",
                "~466 MB",
                "Türkçe + İngilizce karma komutlar için iyi denge."),
            ["medium"] = new(
                "ggml-medium.bin",
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
                "Yüksek doğruluk",
                "~1,5 GB",
                "En iyi tanıma; ilk indirme ve işlem daha yavaş.")
        };

    public static WhisperModelDescriptor Resolve(string? variant) =>
        Variants.TryGetValue(NormalizeVariant(variant), out var descriptor)
            ? descriptor
            : Variants["small"];

    public static IReadOnlyList<(string Id, string Label, string Description)> GetVariantChoices() =>
        Variants.Select(pair =>
        {
            var descriptor = pair.Value;
            var description = $"{descriptor.SizeHint}. {descriptor.Note ?? descriptor.DisplayName}";
            return (pair.Key, $"{descriptor.DisplayName} ({descriptor.SizeHint})", description.Trim());
        }).ToList();

    public static string NormalizeVariant(string? variant) =>
        variant?.Trim().ToLowerInvariant() switch
        {
            "base" => "base",
            "small" => "small",
            _ => "medium"
        };

    public static IReadOnlyList<(string Id, WhisperModelDescriptor Descriptor)> AllVariants() =>
        Variants.Select(pair => (pair.Key, pair.Value)).ToList();
}
