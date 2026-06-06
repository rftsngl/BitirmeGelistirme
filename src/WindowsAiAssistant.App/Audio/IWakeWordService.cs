namespace WindowsAiAssistant.App.Audio;

public interface IWakeWordService : IAsyncDisposable
{
    bool IsListening { get; }

    event EventHandler? WakeWordDetected;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
