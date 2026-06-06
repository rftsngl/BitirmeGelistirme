namespace WindowsAiAssistant.App.Audio;

#pragma warning disable CS0067

public sealed class NullWakeWordService : IWakeWordService
{
    public bool IsListening => false;

    public event EventHandler? WakeWordDetected;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
