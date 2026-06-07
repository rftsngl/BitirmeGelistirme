namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// WaveIn tek seferde bir oturum kullanir; uyandirma ve komut dinleme cakismasini onler.
/// </summary>
public sealed class MicrophoneSessionCoordinator
{
    private readonly SemaphoreSlim _exclusive = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _exclusive.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new ReleaseHandle(_exclusive);
    }

    public async Task ReleaseWithSettleAsync(CancellationToken cancellationToken = default)
    {
        if (_exclusive.CurrentCount == 0)
        {
            _exclusive.Release();
            try
            {
                await Task.Delay(280, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // settle delay iptal edildiyse sorun degil
            }
        }
    }

    private sealed class ReleaseHandle : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private int _released;

        public ReleaseHandle(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            _gate.Release();
        }
    }
}
