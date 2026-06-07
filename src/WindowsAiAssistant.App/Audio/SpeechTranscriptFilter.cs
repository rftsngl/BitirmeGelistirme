namespace WindowsAiAssistant.App.Audio;

internal static class SpeechTranscriptFilter
{
    public static bool IsAcceptable(string? transcript, int minCharacters)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return false;
        }

        var trimmed = transcript.Trim();
        if (trimmed.Length < Math.Max(1, minCharacters))
        {
            return false;
        }

        var letterCount = trimmed.Count(char.IsLetter);
        return letterCount >= Math.Max(2, minCharacters - 1);
    }
}
