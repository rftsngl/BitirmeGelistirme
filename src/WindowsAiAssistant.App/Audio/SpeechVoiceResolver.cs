using Windows.Media.SpeechSynthesis;

namespace WindowsAiAssistant.App.Audio;

internal static class SpeechVoiceResolver
{
    private static readonly string[] PreferredVoiceHints =
    [
        "Tolga",
        "Emel",
        "Aria",
        "Jenny",
        "Natural",
        "Online",
        "Neural"
    ];

    public static VoiceInformation? ResolveVoice(string? speechLanguage, string? preferredVoiceId = null)
    {
        var voices = SpeechSynthesizer.AllVoices;
        if (voices.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredVoiceId))
        {
            var explicitVoice = voices.FirstOrDefault(voice =>
                voice.Id.Equals(preferredVoiceId, StringComparison.OrdinalIgnoreCase) ||
                voice.DisplayName.Contains(preferredVoiceId, StringComparison.OrdinalIgnoreCase));
            if (explicitVoice is not null)
            {
                return explicitVoice;
            }
        }

        var languagePrefix = ResolveLanguagePrefix(speechLanguage);
        var languageMatches = voices
            .Where(voice => voice.Language.StartsWith(languagePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (languageMatches.Count == 0)
        {
            return voices.FirstOrDefault();
        }

        var naturalMatches = languageMatches
            .Where(IsNaturalVoice)
            .ToList();

        foreach (var hint in PreferredVoiceHints)
        {
            var hinted = naturalMatches.FirstOrDefault(voice =>
                voice.DisplayName.Contains(hint, StringComparison.OrdinalIgnoreCase) ||
                voice.Id.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (hinted is not null)
            {
                return hinted;
            }
        }

        if (naturalMatches.Count > 0)
        {
            return naturalMatches[0];
        }

        foreach (var hint in PreferredVoiceHints)
        {
            var hinted = languageMatches.FirstOrDefault(voice =>
                voice.DisplayName.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (hinted is not null)
            {
                return hinted;
            }
        }

        return languageMatches[0];
    }

    private static bool IsNaturalVoice(VoiceInformation voice) =>
        voice.Id.Contains("Online", StringComparison.OrdinalIgnoreCase) ||
        voice.Id.Contains("Neural", StringComparison.OrdinalIgnoreCase) ||
        voice.DisplayName.Contains("Online", StringComparison.OrdinalIgnoreCase) ||
        voice.DisplayName.Contains("Natural", StringComparison.OrdinalIgnoreCase) ||
        voice.DisplayName.Contains("Neural", StringComparison.OrdinalIgnoreCase);

    private static string ResolveLanguagePrefix(string? speechLanguage)
    {
        if (string.IsNullOrWhiteSpace(speechLanguage))
        {
            return "tr";
        }

        return speechLanguage.Split('-')[0].Trim().ToLowerInvariant();
    }
}
