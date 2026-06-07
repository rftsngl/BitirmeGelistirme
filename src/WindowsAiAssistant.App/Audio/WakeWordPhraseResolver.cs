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
            "asistan" => ["asistan", "hey asistan", "asistan dinle"],
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
}
