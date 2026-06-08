namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// STT motoru ayarlardan degistiginde yeniden olusturulabilir delegasyon katmani.
/// </summary>
public sealed class SwitchableSpeechToTextService : ISpeechToTextService, IDisposable
{
    private readonly object _gate = new();
    private readonly Func<ISpeechToTextService> _factory;
    private ISpeechToTextService _inner;

    public SwitchableSpeechToTextService(Func<ISpeechToTextService> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _inner = _factory();
    }

    public void Reload()
    {
        lock (_gate)
        {
            DisposeInner();
            _inner = _factory();
        }
    }

    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        ISpeechToTextService inner;
        lock (_gate)
        {
            inner = _inner;
        }

        if (inner is WhisperSpeechToTextService whisper)
        {
            await whisper.WarmupAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<string?> ListenOnceAsync(
        CancellationToken cancellationToken = default,
        int? listenTimeoutSeconds = null,
        IProgress<SpeechListenProgress>? progress = null)
    {
        lock (_gate)
        {
            return _inner.ListenOnceAsync(cancellationToken, listenTimeoutSeconds, progress);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DisposeInner();
        }
    }

    private void DisposeInner()
    {
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
