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
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    private readonly AudioOptions _options;
    private readonly SpeechReadinessService _readiness;
    private readonly VoskWakeWordModelService _models;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private int _cooldownGate;

    public VoskWakeWordService(
        AudioOptions options,
        SpeechReadinessService readiness,
        VoskWakeWordModelService models)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _models = models ?? throw new ArgumentNullException(nameof(models));
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
        Model? model = null;
        VoskRecognizer? recognizer = null;
        WaveInEvent? waveIn = null;
        var recordingStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await _readiness.EnsureReadyForWakeWordAsync(cancellationToken).ConfigureAwait(false);
            var modelPath = await _models.EnsureWakeModelAsync(cancellationToken).ConfigureAwait(false);
            var phrases = WakeWordPhraseResolver.ResolvePhrases(_options);

            Vosk.Vosk.SetLogLevel(-1);
            model = new Model(modelPath);
            recognizer = new VoskRecognizer(model, 16000f, WakeWordPhraseResolver.BuildGrammarJson(phrases));

            waveIn = new WaveInEvent
            {
                DeviceNumber = ResolveDeviceNumber(_options.InputDeviceIndex),
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };

            waveIn.DataAvailable += (_, args) =>
            {
                if (Volatile.Read(ref _cooldownGate) != 0 || recognizer is null)
                {
                    return;
                }

                try
                {
                    if (recognizer.AcceptWaveform(args.Buffer, args.BytesRecorded))
                    {
                        TryTriggerFromJson(recognizer.Result(), phrases);
                    }
                    else
                    {
                        TryTriggerFromJson(recognizer.PartialResult(), phrases);
                    }
                }
                catch
                {
                    // ignore frame errors
                }
            };
            waveIn.RecordingStopped += (_, _) => recordingStopped.TrySetResult();

            waveIn.StartRecording();
            await recordingStopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // expected
        }
        catch
        {
            // Model, mikrofon veya Vosk hatasi — hotkey fallback kalir.
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

    private void TryTriggerFromJson(string? json, IReadOnlyList<string> phrases)
    {
        var text = ExtractRecognizedText(json);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var normalized = Normalize(text);
        if (!phrases.Any(phrase => normalized.Contains(Normalize(phrase), StringComparison.Ordinal)))
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

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant();

    private async Task ReleaseCooldownAsync()
    {
        try
        {
            await Task.Delay(Cooldown).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _cooldownGate, 0);
        }
    }

    private static int ResolveDeviceNumber(int requested)
    {
        if (requested < 0)
        {
            return -1;
        }

        return requested < WaveInEvent.DeviceCount ? requested : -1;
    }
}
