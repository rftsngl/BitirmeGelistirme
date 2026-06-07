using System.Text.Json;
using NAudio.Wave;
using Vosk;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Vosk ile yerel komut dinleme (overlay STT).
/// </summary>
public sealed class VoskSpeechToTextService : ISpeechToTextService, IDisposable
{
    private readonly AudioOptions _options;
    private readonly SpeechReadinessService _readiness;
    private readonly VoskWakeWordModelService _models;

    public VoskSpeechToTextService(
        AudioOptions options,
        SpeechReadinessService readiness,
        VoskWakeWordModelService models)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    public async Task<string?> ListenOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _readiness.EnsureReadyForListenAsync(cancellationToken).ConfigureAwait(false);

        var modelPath = await _models.EnsureSttModelAsync(cancellationToken).ConfigureAwait(false);
        var pcm = await CapturePcmAsync(cancellationToken).ConfigureAwait(false);
        if (pcm.Length == 0)
        {
            return null;
        }

        Model? model = null;
        VoskRecognizer? recognizer = null;
        try
        {
            Vosk.Vosk.SetLogLevel(-1);
            model = new Model(modelPath);
            recognizer = new VoskRecognizer(model, 16000f);
            recognizer.SetMaxAlternatives(0);
            recognizer.SetWords(false);

            const int chunkSize = 4000;
            for (var offset = 0; offset < pcm.Length; offset += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(chunkSize, pcm.Length - offset);
                var chunk = new byte[length];
                Buffer.BlockCopy(pcm, offset, chunk, 0, length);
                recognizer.AcceptWaveform(chunk, length);
            }

            return ExtractText(recognizer.FinalResult());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            recognizer?.Dispose();
            model?.Dispose();
        }
    }

    private async Task<byte[]> CapturePcmAsync(CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Clamp(_options.SpeechListenTimeoutSeconds, 3, 60);
        var deviceNumber = ResolveDeviceNumber(_options.InputDeviceIndex);
        var pcm = new List<byte>();

        using var waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50
        };

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waveIn.DataAvailable += (_, args) =>
        {
            lock (pcm)
            {
                pcm.AddRange(args.Buffer.Take(args.BytesRecorded));
            }
        };
        waveIn.RecordingStopped += (_, _) => stopped.TrySetResult();

        waveIn.StartRecording();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await Task.Delay(Timeout.Infinite, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // timeout veya iptal
        }

        waveIn.StopRecording();
        await stopped.Task.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        lock (pcm)
        {
            return pcm.ToArray();
        }
    }

    private static string? ExtractText(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("text", out var textNode))
            {
                var text = textNode.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static int ResolveDeviceNumber(int requested)
    {
        if (requested < 0)
        {
            return -1;
        }

        return requested < WaveInEvent.DeviceCount ? requested : -1;
    }

    public void Dispose()
    {
    }
}
