namespace WindowsAiAssistant.App.Audio;

public interface ISpeechToTextService
{
    Task<string?> ListenOnceAsync(
        CancellationToken cancellationToken = default,
        int? listenTimeoutSeconds = null,
        IProgress<SpeechListenProgress>? progress = null);
}
