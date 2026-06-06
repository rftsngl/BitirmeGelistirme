namespace WindowsAiAssistant.App.Audio;

public interface ITextToSpeechService
{
    bool IsEnabled { get; }

    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    void StopSpeaking();
}
