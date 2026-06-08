namespace WindowsAiAssistant.Runtime.Audio;

/// <summary>
/// WaveIn tek seferde bir oturum kullanir; uyandirma ve komut dinleme cakismasini onler.
/// </summary>
public sealed class MicrophoneSessionCoordinator
{
    private readonly SemaphoreSlim _exclusive = new(1, 1);
    private string? _currentOwner;

    public string? CurrentOwner => _currentOwner;

    public bool IsHeld => _exclusive.CurrentCount == 0;

    public async Task<IDisposable> AcquireAsync(string owner, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        await _exclusive.WaitAsync(cancellationToken).ConfigureAwait(false);
        _currentOwner = owner;
        MicSessionLog.Write($"acquire owner={owner}");
        return new ReleaseHandle(this, owner);
    }

    private void Release(string owner)
    {
        _currentOwner = null;
        MicSessionLog.Write($"release owner={owner}");
        _exclusive.Release();
    }

    private sealed class ReleaseHandle : IDisposable
    {
        private readonly MicrophoneSessionCoordinator _coordinator;
        private readonly string _owner;
        private int _released;

        public ReleaseHandle(MicrophoneSessionCoordinator coordinator, string owner)
        {
            _coordinator = coordinator;
            _owner = owner;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            _coordinator.Release(_owner);
        }
    }
}
