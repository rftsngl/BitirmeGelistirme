using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Windows SpeechRecognizer icin yuklu dilleri cozer.
/// Ses paketi (TTS) ile konusma tanima (STT/wake-word) farkli Windows ozellikleridir.
/// </summary>
internal static class WindowsSpeechLanguageCatalog
{
    public static IReadOnlyList<Language> GrammarLanguages =>
        SpeechRecognizer.SupportedGrammarLanguages.ToList();

    public static IReadOnlyList<Language> TopicLanguages =>
        SpeechRecognizer.SupportedTopicLanguages.ToList();

    public static bool IsGrammarLanguageAvailable(string? languageTag) =>
        TryResolveGrammarLanguage(languageTag, out _);

    public static bool IsTopicLanguageAvailable(string? languageTag) =>
        TryResolveTopicLanguage(languageTag, out _);

    public static bool HasAnyGrammarLanguage => GrammarLanguages.Count > 0;

    public static bool TryResolveGrammarLanguage(string? preferredTag, out Language? language)
    {
        language = ResolveFromList(GrammarLanguages, preferredTag);
        return language is not null;
    }

    public static bool TryResolveTopicLanguage(string? preferredTag, out Language? language)
    {
        language = ResolveFromList(TopicLanguages, preferredTag);
        return language is not null;
    }

    public static SpeechRecognizer CreateGrammarRecognizer(string? preferredTag)
    {
        if (TryResolveGrammarLanguage(preferredTag, out var language) && language is not null)
        {
            return new SpeechRecognizer(language);
        }

        return new SpeechRecognizer();
    }

    public static SpeechRecognizer CreateTopicRecognizer(string? preferredTag)
    {
        if (TryResolveTopicLanguage(preferredTag, out var language) && language is not null)
        {
            return new SpeechRecognizer(language);
        }

        return new SpeechRecognizer();
    }

    public static string DescribeGrammarAvailability(string? preferredTag)
    {
        if (string.IsNullOrWhiteSpace(preferredTag))
        {
            return HasAnyGrammarLanguage
                ? "Windows uyandırma kelimesi kullanılabilir."
                : BuildMissingPackageMessage(preferredTag, GrammarLanguages);
        }

        if (IsGrammarLanguageAvailable(preferredTag))
        {
            return $"Windows uyandırma kelimesi hazır ({preferredTag}).";
        }

        if (HasAnyGrammarLanguage)
        {
            var installed = FormatLanguageList(GrammarLanguages);
            return
                $"{preferredTag} konuşma tanıma paketi yüklü değil; yalnızca ses (TTS) paketi yetmez. " +
                $"Sistemde algılanan konuşma tanıma dilleri: {installed}. " +
                "Türkçe için Ayarlar → Zaman ve dil → Dil ve bölge → Türkçe → Seçenekler → Konuşma tanıma indirin. " +
                "Şimdilik uyandırma kelimesi İngilizce (Computer/Jarvis) ile çalışabilir.";
        }

        return BuildMissingPackageMessage(preferredTag, GrammarLanguages);
    }

    public static string DescribeTopicAvailability(string? preferredTag)
    {
        if (string.IsNullOrWhiteSpace(preferredTag))
        {
            return TopicLanguages.Count > 0
                ? "Windows konuşma tanıma kullanılabilir."
                : BuildMissingPackageMessage(preferredTag, TopicLanguages);
        }

        if (IsTopicLanguageAvailable(preferredTag))
        {
            return $"Windows konuşma tanıma dili hazır ({preferredTag}).";
        }

        return BuildMissingPackageMessage(preferredTag, TopicLanguages);
    }

    private static string BuildMissingPackageMessage(string? preferredTag, IReadOnlyList<Language> installed)
    {
        var requested = string.IsNullOrWhiteSpace(preferredTag) ? "Seçili dil" : preferredTag;
        var installedText = installed.Count > 0
            ? $"Sistemde algılanan konuşma tanıma dilleri: {FormatLanguageList(installed)}."
            : "Sistemde hiç konuşma tanıma dili algılanmadı.";

        return
            $"{requested} için konuşma tanıma paketi yüklü değil. " +
            "Ayarlar → Zaman ve dil → Dil ve bölge ekranındaki «Yüklü ses paketleri» yalnızca seslendirme (TTS) içindir; uyandırma ve komut dinleme için aynı dilin «Konuşma tanıma» özelliğini indirmeniz gerekir. " +
            installedText;
    }

    private static string FormatLanguageList(IReadOnlyList<Language> languages) =>
        string.Join(", ", languages.Select(language => $"{language.DisplayName} ({language.LanguageTag})"));

    private static Language? ResolveFromList(IReadOnlyList<Language> languages, string? preferredTag)
    {
        if (languages.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(preferredTag))
        {
            return languages[0];
        }

        var exact = languages.FirstOrDefault(language =>
            language.LanguageTag.Equals(preferredTag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var parentTag = preferredTag;
        while (TryGetParentLanguageTag(parentTag, out parentTag))
        {
            var parentMatch = languages.FirstOrDefault(language =>
                language.LanguageTag.Equals(parentTag, StringComparison.OrdinalIgnoreCase));
            if (parentMatch is not null)
            {
                return parentMatch;
            }
        }

        return null;
    }

    private static bool TryGetParentLanguageTag(string languageTag, out string parentTag)
    {
        parentTag = string.Empty;
        var separator = languageTag.LastIndexOf('-');
        if (separator <= 0)
        {
            return false;
        }

        parentTag = languageTag[..separator];
        return !string.IsNullOrWhiteSpace(parentTag);
    }
}
