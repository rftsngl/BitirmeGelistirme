using System.Text.Json;
using NAudio.Wave;
using Vosk;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Vosk ile yerel uyandirma kelimesi dinler. Turkce ve Ingilizce phrase setleri desteklenir.
/// </summary>
public sealed class VoskWakeWordService : IWakeWordService
{
    private readonly AudioOptions _options;
    private readonly SpeechReadinessService _readiness;
    private readonly VoskWakeWordModelService _models;
    private readonly MicrophoneSessionCoordinator _microphone;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private int _cooldownGate;

    public VoskWakeWordService(
        AudioOptions options,
        SpeechReadinessService readiness,
        VoskWakeWordModelService models,
        MicrophoneSessionCoordinator microphone)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _microphone = microphone ?? throw new ArgumentNullException(nameof(microphone));
    }

    public bool IsListening => _listenCts is not null && !_listenCts.IsCancellationRequested;

    public event EventHandler? WakeWordDetected;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!WakeWordPhraseResolver.CanStart(_options) || IsListening)
        {
            return Task.CompletedTask;
        }

        _listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenTask = Task.Run(() => ListenLoopAsync(_listenCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_listenCts is null)
        {
            return;
        }

        await _listenCts.CancelAsync().ConfigureAwait(false);
        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }

        _listenCts.Dispose();
        _listenCts = null;
        _listenTask = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunListenSessionAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task RunListenSessionAsync(CancellationToken cancellationToken)
    {
        using var micSession = await _microphone.AcquireAsync(cancellationToken).ConfigureAwait(false);

        Model? model = null;
        VoskRecognizer? recognizer = null;
        WaveInEvent? waveIn = null;
        var recordingStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Pcm16Resampler? resampler = null;
        var captureSampleRate = MicrophoneCapture.TargetSampleRate;

        try
        {
            await _readiness.EnsureReadyForWakeWordAsync(cancellationToken).ConfigureAwait(false);
            var modelPath = await _models.EnsureWakeModelAsync(cancellationToken).ConfigureAwait(false);
            var phrases = WakeWordPhraseResolver.ResolvePhrases(_options);

            Vosk.Vosk.SetLogLevel(-1);
            model = new Model(modelPath);
            recognizer = new VoskRecognizer(model, 16000f, WakeWordPhraseResolver.BuildGrammarJson(phrases));

            waveIn = MicrophoneCapture.OpenWaveIn(_options, bufferMilliseconds: 50, out captureSampleRate);
            if (captureSampleRate != MicrophoneCapture.TargetSampleRate)
            {
                resampler = new Pcm16Resampler(captureSampleRate, MicrophoneCapture.TargetSampleRate);
            }

            var wakeVad = _options.WakeWordRequireSpeechEnergy
                ? new VoiceActivityDetector(
                    silenceEndMs: 300,
                    speechMultiplier: Math.Min(_options.VadSpeechMultiplier, 3.2),
                    calibrationMs: 400,
                    minSpeechMs: 120,
                    speechOnsetFrames: 2)
                : null;
            var utteranceHadSpeech = false;
            var utterancePeak = 0.0;

            waveIn.DataAvailable += (_, args) =>
            {
                if (recognizer is null)
                {
                    return;
                }

                try
                {
                    MicrophoneCapture.ProcessChunk(
                        args.Buffer.AsSpan(0, args.BytesRecorded),
                        captureSampleRate,
                        resampler,
                        _options,
                        (chunk, level) =>
                        {
                            utterancePeak = Math.Max(utterancePeak, level);

                            if (wakeVad is not null)
                            {
                                wakeVad.Process(chunk);
                                if (wakeVad.SpeechStarted)
                                {
                                    utteranceHadSpeech = true;
                                }
                            }
                            else if (level >= WakeMinPeak())
                            {
                                utteranceHadSpeech = true;
                            }

                            if (recognizer.AcceptWaveform(chunk, chunk.Length))
                            {
                                OnWakeFinal(recognizer, phrases, wakeVad, ref utteranceHadSpeech, ref utterancePeak);
                            }
                            else if (!_options.WakeWordFinalOnly
                                     && Volatile.Read(ref _cooldownGate) == 0
                                     && PassesEnergyGate(utteranceHadSpeech, utterancePeak))
                            {
                                TryTriggerFromJson(recognizer.PartialResult(), phrases, finalOnly: false);
                            }
                        });
                }
                catch
                {
                    // ignore frame errors
                }
            };
            waveIn.RecordingStopped += (_, _) => recordingStopped.TrySetResult();

            await recordingStopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            model?.Dispose();
        }
    }

    private void OnWakeFinal(
        VoskRecognizer recognizer,
        IReadOnlyList<string> phrases,
        VoiceActivityDetector? wakeVad,
        ref bool utteranceHadSpeech,
        ref double utterancePeak)
    {
        if (Volatile.Read(ref _cooldownGate) == 0
            && PassesEnergyGate(utteranceHadSpeech, utterancePeak))
        {
            TryTriggerFromJson(recognizer.Result(), phrases, finalOnly: true);
        }

        utteranceHadSpeech = false;
        utterancePeak = 0;
        wakeVad?.Reset();
    }

    private double WakeMinPeak() =>
        Math.Clamp(_options.WakeWordMinPeakLevel, 0.006, 0.05);

    private bool PassesEnergyGate(bool utteranceHadSpeech, double utterancePeak) =>
        !_options.WakeWordRequireSpeechEnergy
        || utteranceHadSpeech
        || utterancePeak >= WakeMinPeak();

    private void TryTriggerFromJson(string? json, IReadOnlyList<string> phrases, bool finalOnly)
    {
        if (!finalOnly && _options.WakeWordFinalOnly)
        {
            return;
        }

        var text = ExtractRecognizedText(json);
        if (!WakeWordPhraseResolver.MatchesAnyPhrase(text, phrases))
        {
            return;
        }

        Interlocked.Exchange(ref _cooldownGate, 1);
        WakeWordDetected?.Invoke(this, EventArgs.Empty);
        _ = ReleaseCooldownAsync();
    }

    private static string? ExtractRecognizedText(string? json)
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
                return textNode.GetString();
            }

            if (document.RootElement.TryGetProperty("partial", out var partialNode))
            {
                return partialNode.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task ReleaseCooldownAsync()
    {
        var cooldown = TimeSpan.FromSeconds(Math.Clamp(_options.WakeWordCooldownSeconds, 1, 30));
        try
        {
            await Task.Delay(cooldown).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _cooldownGate, 0);
        }
    }

}
