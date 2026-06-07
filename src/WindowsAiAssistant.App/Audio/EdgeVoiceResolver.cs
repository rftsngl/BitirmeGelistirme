namespace WindowsAiAssistant.App.Audio;

internal static class EdgeVoiceResolver
{
    private static readonly string[] TurkishPreferred =
    [
        "tr-TR-EmelNeural",
        "tr-TR-AhmetNeural"
    ];

    private static readonly string[] EnglishPreferred =
    [
        "en-US-JennyNeural",
        "en-US-AriaNeural",
        "en-GB-SoniaNeural"
    ];

    public static string ResolveShortName(string? speechLanguage, string? preferredVoice)
    {
        if (!string.IsNullOrWhiteSpace(preferredVoice))
        {
            var trimmed = preferredVoice.Trim();
            if (trimmed.Contains("Neural", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains('-'))
            {
                return trimmed;
            }
        }

        var prefix = ResolveLanguagePrefix(speechLanguage);
        var preferred = prefix == "tr" ? TurkishPreferred : EnglishPreferred;
        return preferred[0];
    }

    private static string ResolveLanguagePrefix(string? speechLanguage)
    {
        if (string.IsNullOrWhiteSpace(speechLanguage))
        {
            return "tr";
        }

        return speechLanguage.Split('-')[0].Trim().ToLowerInvariant();
    }
}
