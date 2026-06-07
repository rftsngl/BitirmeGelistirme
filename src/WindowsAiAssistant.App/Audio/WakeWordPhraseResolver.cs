using System.Text.Json;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

internal static class WakeWordPhraseResolver
{
    public static bool CanStart(AudioOptions options) =>
        options.WakeWordEnabled && !string.IsNullOrWhiteSpace(options.WakeWordPhrase);

    public static IReadOnlyList<string> ResolvePhrases(AudioOptions options)
    {
        var id = string.IsNullOrWhiteSpace(options.WakeWordPhrase)
            ? "asistan"
            : options.WakeWordPhrase.Trim();

        return id.ToLowerInvariant() switch
        {
            "asistan" => ["asistan", "hey asistan"],
            "hey-asistan" => ["hey asistan", "asistan"],
            "bilgisayar" => ["bilgisayar", "hey bilgisayar"],
            "computer" => ["computer", "hey computer"],
            "jarvis" => ["jarvis", "hey jarvis"],
            _ => [id]
        };
    }

    public static string BuildGrammarJson(IReadOnlyList<string> phrases)
    {
        var values = phrases
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .Select(phrase => JsonSerializer.Serialize(phrase.Trim()))
            .Append("\"[unk]\"");
        return $"[{string.Join(", ", values)}]";
    }

    /// <summary>
    /// Partial sonuclardaki yanlis pozitifleri azaltmak icin siki eslesme.
    /// </summary>
    public static bool MatchesAnyPhrase(string? recognizedText, IReadOnlyList<string> phrases)
    {
        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return false;
        }

        var normalized = Normalize(recognizedText);
        foreach (var phrase in phrases)
        {
            var target = Normalize(phrase);
            if (normalized == target)
            {
                return true;
            }

            if (IsFuzzyWakeMatch(normalized, target))
            {
                return true;
            }

            // Cok kelimeli ifadelerde kisa on ek: "eh hey asistan"
            if (target.Contains(' ', StringComparison.Ordinal)
                && normalized.EndsWith(target, StringComparison.Ordinal)
                && normalized.Length <= target.Length + 6)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFuzzyWakeMatch(string normalized, string target)
    {
        if (target.Length < 4)
        {
            return false;
        }

        if (normalized.Contains(target, StringComparison.Ordinal))
        {
            return normalized.Length <= target.Length + 8;
        }

        // Vosk TR modelinin sik urettigi varyantlar
        if (target == "asistan")
        {
            return normalized is "assistan"
                or "asistanım"
                or "a sistem"
                or "hey asistan"
                or "hey assistan";
        }

        return false;
    }

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant();
}
