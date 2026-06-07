using NAudio.MediaFoundation;
using NAudio.Wave;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// UI thread disinda guvenli ses calma. WinUI MediaPlayer arka planda COM cokmesine yol acabilir.
/// </summary>
internal sealed class NaudioAudioPlayback : IDisposable
{
    private static int _mediaFoundationStarted;
    private readonly object _gate = new();
    private WaveOutEvent? _output;
    private IDisposable? _reader;

    public static void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref _mediaFoundationStarted, 1, 0) == 0)
        {
            MediaFoundationApi.Startup();
        }
    }

    public Task PlayFileAsync(string path, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        return Task.Run(() => PlayCore(() => new MediaFoundationReader(path), cancellationToken), cancellationToken);
    }

    public async Task PlayWaveStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var data = buffer.ToArray();
        await Task.Run(
                () => PlayCore(() => new WaveFileReader(new MemoryStream(data)), cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    private void PlayCore(Func<WaveStream> readerFactory, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            StopCore();

            var reader = readerFactory();
            _reader = reader;
            _output = new WaveOutEvent();
            _output.Init(reader);

            using var finished = new ManualResetEventSlim(false);
            _output.PlaybackStopped += (_, _) => finished.Set();
            _output.Play();

            while (!cancellationToken.IsCancellationRequested)
            {
                if (finished.Wait(80))
                {
                    break;
                }

                if (_output.PlaybackState == PlaybackState.Stopped)
                {
                    break;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                StopCore();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private void StopCore()
    {
        if (_output is not null)
        {
            try
            {
                _output.Stop();
                _output.Dispose();
            }
            catch
            {
                // ignore
            }

            _output = null;
        }

        if (_reader is not null)
        {
            try
            {
                _reader.Dispose();
            }
            catch
            {
                // ignore
            }

            _reader = null;
        }
    }
}
