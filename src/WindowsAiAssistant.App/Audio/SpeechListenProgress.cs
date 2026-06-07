namespace WindowsAiAssistant.App.Audio;

public sealed class SpeechListenProgress
{
    public double AudioLevel { get; init; }

    public bool IsSpeaking { get; init; }

    public string? PartialTranscript { get; init; }

    /// <summary>
    /// Mikrofon kaydi bitti, Whisper transkripsiyonu calisiyor.
    /// </summary>
    public bool IsTranscribing { get; init; }
}
