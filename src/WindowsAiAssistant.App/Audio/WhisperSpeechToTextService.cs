using System.Text;
using NAudio.Wave;
using Whisper.net;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.Runtime.Audio;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Yerel Whisper.net ile komut dinleme. VAD ile konusma bitince transkribe eder.
/// </summary>
public sealed class WhisperSpeechToTextService : ISpeechToTextService, IDisposable
{
    private readonly AudioOptions _options;
    private readonly SpeechReadinessService _readiness;
    private readonly WhisperModelService _models;
    private readonly MicrophoneSessionCoordinator _microphone;
    private readonly object _gate = new();
    private WhisperFactory? _factory;
    private string? _loadedModelPath;

    public WhisperSpeechToTextService(
        AudioOptions options,
        SpeechReadinessService readiness,
        WhisperModelService models,
        MicrophoneSessionCoordinator microphone)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _microphone = microphone ?? throw new ArgumentNullException(nameof(microphone));
    }

    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        if (!_readiness.UsesWhisperForStt())
        {
            return;
        }

        try
        {
            await _readiness.EnsureReadyForListenAsync(cancellationToken).ConfigureAwait(false);
            _ = GetFactory();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "WhisperSpeechToTextService.Warmup");
        }
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
            samples = await CaptureSamplesAsync(cancellationToken, listenTimeoutSeconds, progress)
                .ConfigureAwait(false);
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
            CrashLog.Write(ex, "WhisperSpeechToTextService.Capture");
            return null;
        }

        if (samples is null || samples.Length == 0)
        {
            return null;
        }

        progress?.Report(new SpeechListenProgress { IsTranscribing = true });

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
            return SpeechTranscriptFilter.IsAcceptable(text, _options.SttMinTranscriptCharacters)
                ? text
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "WhisperSpeechToTextService.Transcribe");
            return null;
        }
    }

    private async Task<float[]?> CaptureSamplesAsync(
        CancellationToken cancellationToken,
        int? listenTimeoutSeconds,
        IProgress<SpeechListenProgress>? progress)
    {
        using var micSession = await _microphone.AcquireAsync("stt", cancellationToken).ConfigureAwait(false);

        var maxWaitSeconds = Math.Clamp(listenTimeoutSeconds ?? _options.SpeechListenTimeoutSeconds, 3, 60);
        var vad = new VoiceActivityDetector(
            _options.SilenceEndMilliseconds,
            _options.VadSpeechMultiplier,
            _options.VadCalibrationMilliseconds,
            _options.VadMinSpeechMilliseconds,
            speechOnsetFrames: 2);

        var pcm = new List<byte>();
        var captureDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturePeak = 0.0;
        var heardSpeech = false;
        var heardSpeechFlag = 0;
        var captureStartedUtc = DateTimeOffset.UtcNow;
        WaveInEvent? waveIn = null;
        Pcm16Resampler? resampler = null;
        var captureSampleRate = MicrophoneCapture.TargetSampleRate;
        var hardLimitSeconds = maxWaitSeconds + 5;

        try
        {
            waveIn = MicrophoneCapture.OpenWaveIn(_options, bufferMilliseconds: 80, out captureSampleRate);
            if (captureSampleRate != MicrophoneCapture.TargetSampleRate)
            {
                resampler = new Pcm16Resampler(captureSampleRate, MicrophoneCapture.TargetSampleRate);
            }

            waveIn.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0)
                {
                    return;
                }

                MicrophoneCapture.ProcessChunk(
                    e.Buffer.AsSpan(0, e.BytesRecorded),
                    captureSampleRate,
                    resampler,
                    _options,
                    (chunk, level) =>
                    {
                        var (_, isSpeech) = vad.Process(chunk);
                        capturePeak = Math.Max(capturePeak, level);
                        if (vad.SpeechStarted || level >= 0.008)
                        {
                            heardSpeech = true;
                            Interlocked.Exchange(ref heardSpeechFlag, 1);
                        }

                        lock (pcm)
                        {
                            pcm.AddRange(chunk);
                        }

                        progress?.Report(new SpeechListenProgress
                        {
                            AudioLevel = level,
                            IsSpeaking = isSpeech || level >= 0.008,
                            PartialTranscript = null
                        });

                        if (vad.ShouldEndAfterSpeech()
                            || vad.ExceededMaxWait(maxWaitSeconds)
                            || (DateTimeOffset.UtcNow - captureStartedUtc).TotalSeconds >= hardLimitSeconds)
                        {
                            captureDone.TrySetResult();
                        }
                    });
            };
            waveIn.RecordingStopped += (_, _) => captureDone.TrySetResult();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(hardLimitSeconds + 8));

            using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var watchdog = RunCaptureWatchdogAsync(
                captureDone,
                captureStartedUtc,
                maxWaitSeconds,
                hardLimitSeconds,
                () => Volatile.Read(ref heardSpeechFlag) != 0,
                watchdogCts.Token);

            try
            {
                await captureDone.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                captureDone.TrySetResult();
            }
            finally
            {
                await watchdogCts.CancelAsync().ConfigureAwait(false);
                try
                {
                    await watchdog.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected
                }
            }

            waveIn.StopRecording();
            cancellationToken.ThrowIfCancellationRequested();
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
        }

        byte[] buffer;
        lock (pcm)
        {
            buffer = pcm.ToArray();
        }

        if (buffer.Length < 1600)
        {
            return null;
        }

        if (!vad.SpeechStarted && !heardSpeech && capturePeak < 0.006)
        {
            return null;
        }

        return ConvertPcm16ToFloat(TrimLeadingSilence(buffer, capturePeak));
    }

    private static async Task RunCaptureWatchdogAsync(
        TaskCompletionSource captureDone,
        DateTimeOffset captureStartedUtc,
        int maxWaitSeconds,
        int hardLimitSeconds,
        Func<bool> hasHeardSpeech,
        CancellationToken cancellationToken)
    {
        while (!captureDone.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var elapsed = (DateTimeOffset.UtcNow - captureStartedUtc).TotalSeconds;
            if (elapsed >= hardLimitSeconds)
            {
                captureDone.TrySetResult();
                return;
            }

            if (elapsed >= maxWaitSeconds && !hasHeardSpeech())
            {
                captureDone.TrySetResult();
                return;
            }
        }
    }

    private static byte[] TrimLeadingSilence(byte[] pcm, double peakLevel)
    {
        if (pcm.Length < 3200)
        {
            return pcm;
        }

        var threshold = Math.Max(peakLevel * 0.12, 0.005);
        var frameBytes = 320;
        var start = 0;
        for (var i = 0; i <= pcm.Length - frameBytes; i += frameBytes)
        {
            if (VoiceActivityDetector.MeasureLevel(pcm.AsSpan(i, frameBytes)) >= threshold)
            {
                start = Math.Max(0, i - frameBytes);
                break;
            }
        }

        return start == 0 ? pcm : pcm.AsSpan(start).ToArray();
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
        var modelPath = _models.ResolveModelPathOrNull() ?? _options.WhisperModelPath;
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new SpeechAccessException("Whisper modeli bulunamadi.");
        }

        lock (_gate)
        {
            if (_factory is not null &&
                string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return _factory;
            }

            _factory?.Dispose();
            _factory = WhisperFactory.FromPath(modelPath);
            _loadedModelPath = modelPath;
            return _factory;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _factory?.Dispose();
            _factory = null;
            _loadedModelPath = null;
        }
    }
}
