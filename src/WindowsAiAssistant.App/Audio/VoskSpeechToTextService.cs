using System.Text.Json;
using NAudio.Wave;
using Vosk;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Vosk ile yerel komut dinleme. VAD ile konusma bitince kaydi hemen sonlandirir.
/// </summary>
public sealed class VoskSpeechToTextService : ISpeechToTextService, IDisposable
{
    private readonly AudioOptions _options;
    private readonly SpeechReadinessService _readiness;
    private readonly VoskWakeWordModelService _models;
    private readonly object _modelGate = new();
    private Model? _cachedModel;
    private string? _cachedModelPath;

    public VoskSpeechToTextService(
        AudioOptions options,
        SpeechReadinessService readiness,
        VoskWakeWordModelService models)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    public async Task<string?> ListenOnceAsync(
        CancellationToken cancellationToken = default,
        int? listenTimeoutSeconds = null,
        IProgress<SpeechListenProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _readiness.EnsureReadyForListenAsync(cancellationToken).ConfigureAwait(false);

        var modelPath = await _models.EnsureSttModelAsync(cancellationToken).ConfigureAwait(false);
        var model = GetOrLoadModel(modelPath);

        VoskRecognizer? recognizer = null;
        WaveInEvent? waveIn = null;
        var captureDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Vosk.Vosk.SetLogLevel(-1);
            recognizer = new VoskRecognizer(model, 16000f);
            recognizer.SetMaxAlternatives(0);
            recognizer.SetWords(false);

            var maxWaitSeconds = Math.Clamp(listenTimeoutSeconds ?? _options.SpeechListenTimeoutSeconds, 3, 60);
            var vad = new VoiceActivityDetector(_options.SilenceEndMilliseconds);
            var deviceNumber = ResolveDeviceNumber(_options.InputDeviceIndex);

            waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 30
            };

            waveIn.DataAvailable += (_, args) =>
            {
                if (args.BytesRecorded <= 0 || recognizer is null)
                {
                    return;
                }

                try
                {
                    var span = args.Buffer.AsSpan(0, args.BytesRecorded);
                    var (level, isSpeech) = vad.Process(span);

                    recognizer.AcceptWaveform(args.Buffer, args.BytesRecorded);

                    string? partial = null;
                    var partialJson = recognizer.PartialResult();
                    partial = ExtractPartialText(partialJson);

                    progress?.Report(new SpeechListenProgress
                    {
                        AudioLevel = level,
                        IsSpeaking = isSpeech,
                        PartialTranscript = partial
                    });

                    if (vad.ShouldEndAfterSpeech())
                    {
                        captureDone.TrySetResult();
                    }
                    else if (vad.ExceededMaxWait(maxWaitSeconds))
                    {
                        captureDone.TrySetResult();
                    }
                }
                catch
                {
                    // frame hatalarini yut
                }
            };

            waveIn.RecordingStopped += (_, _) => captureDone.TrySetResult();

            try
            {
                waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                throw new SpeechAccessException(
                    "Mikrofon başka bir oturum tarafından kullanılıyor olabilir. Lütfen tekrar deneyin.",
                    ex);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxWaitSeconds + 20));

            var completed = await Task.WhenAny(
                captureDone.Task,
                Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);

            if (completed != captureDone.Task && cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            waveIn.StopRecording();
            await captureDone.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return ExtractText(recognizer.FinalResult());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SpeechAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SpeechAccessException($"Vosk komut dinleme başarısız: {ex.Message}", ex);
        }
        finally
        {
            if (waveIn is not null)
            {
                try
                {
                    waveIn.StopRecording();
                    waveIn.Dispose();
                }
                catch
                {
                    // ignore
                }
            }

            recognizer?.Dispose();
        }
    }

    private Model GetOrLoadModel(string modelPath)
    {
        lock (_modelGate)
        {
            if (_cachedModel is not null &&
                string.Equals(_cachedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedModel;
            }

            _cachedModel?.Dispose();
            Vosk.Vosk.SetLogLevel(-1);
            _cachedModel = new Model(modelPath);
            _cachedModelPath = modelPath;
            return _cachedModel;
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

    private static string? ExtractPartialText(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("partial", out var partialNode))
            {
                var text = partialNode.GetString()?.Trim();
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
        lock (_modelGate)
        {
            _cachedModel?.Dispose();
            _cachedModel = null;
            _cachedModelPath = null;
        }
    }
}
