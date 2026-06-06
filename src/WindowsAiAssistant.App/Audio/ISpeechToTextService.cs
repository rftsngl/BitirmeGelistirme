namespace WindowsAiAssistant.App.Audio;

public interface ISpeechToTextService
{
    Task<string?> ListenOnceAsync(CancellationToken cancellationToken = default);
}
