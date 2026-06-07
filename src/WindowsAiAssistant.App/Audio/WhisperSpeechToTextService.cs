using System.Text;
using NAudio.Wave;
using Whisper.net;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Yerel Whisper.net modeli ile STT. Mikrofonu 16kHz mono yakalar ve segmentleri
/// metne donusturur. Model dosyasi yapilandirilmamissa null doner; cagiran taraf
/// manuel girise duser.
/// </summary>
public sealed class WhisperSpeechToTextService : ISpeechToTextService, IDisposable
{
    private readonly AudioOptions _options;
    private readonly SpeechReadinessService _readiness;
    private readonly object _gate = new();
    private WhisperFactory? _factory;

    public WhisperSpeechToTextService(AudioOptions options, SpeechReadinessService readiness)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public async Task<string?> ListenOnceAsync(
        CancellationToken cancellationToken = default,
        int? listenTimeoutSeconds = null,
        IProgress<SpeechListenProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _readiness.EnsureReadyForListenAsync(cancellationToken).ConfigureAwait(false);

        float[]? samples;
        try
        {
            samples = await CaptureSamplesAsync(cancellationToken, listenTimeoutSeconds).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }

        if (samples is null || samples.Length == 0)
        {
            return null;
        }

        try
        {
            var factory = GetFactory();
            using var processor = factory.CreateBuilder()
                .WithLanguage(MapLanguage(_options.SpeechLanguage))
                .Build();

            var builder = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
            {
                builder.Append(segment.Text);
            }

            var text = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<float[]?> CaptureSamplesAsync(CancellationToken cancellationToken, int? listenTimeoutSeconds = null)
    {
        var timeoutSeconds = Math.Clamp(listenTimeoutSeconds ?? _options.SpeechListenTimeoutSeconds, 3, 60);
        var deviceNumber = ResolveDeviceNumber(_options.InputDeviceIndex);
        var pcm = new List<byte>();

        using var waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50
        };

        var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        waveIn.DataAvailable += (_, e) =>
        {
            lock (pcm)
            {
                pcm.AddRange(e.Buffer.Take(e.BytesRecorded));
            }
        };
        waveIn.RecordingStopped += (_, _) => stopped.TrySetResult(true);

        waveIn.StartRecording();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await Task.Delay(Timeout.Infinite, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Sure doldu veya oturum iptal edildi; kaydi durdurup degerlendir.
        }

        waveIn.StopRecording();
        await stopped.Task.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        byte[] buffer;
        lock (pcm)
        {
            buffer = pcm.ToArray();
        }

        return ConvertPcm16ToFloat(buffer);
    }

    private static int ResolveDeviceNumber(int requested)
    {
        if (requested < 0)
        {
            return -1;
        }

        return requested < WaveInEvent.DeviceCount ? requested : -1;
    }

    private static float[] ConvertPcm16ToFloat(byte[] pcm)
    {
        var sampleCount = pcm.Length / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var value = (short)(pcm[i * 2] | (pcm[(i * 2) + 1] << 8));
            samples[i] = value / 32768f;
        }

        return samples;
    }

    private static string MapLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "auto";
        }

        var twoLetter = language.Split('-')[0].Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(twoLetter) ? "auto" : twoLetter;
    }

    private WhisperFactory GetFactory()
    {
        lock (_gate)
        {
            return _factory ??= WhisperFactory.FromPath(_options.WhisperModelPath);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _factory?.Dispose();
            _factory = null;
        }
    }
}
