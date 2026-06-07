namespace WindowsAiAssistant.App.Audio;

public sealed class SpeechListenProgress
{
    public double AudioLevel { get; init; }

    public bool IsSpeaking { get; init; }

    public string? PartialTranscript { get; init; }
}
