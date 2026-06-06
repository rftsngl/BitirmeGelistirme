using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Porcupine wake-word listener. Requires AccessKey + .ppn keyword + .pv model paths in AudioOptions.
/// Falls back silently when configuration is incomplete.
/// </summary>
public sealed class PorcupineWakeWordService : IWakeWordService
{
    private readonly AudioOptions _options;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private object? _porcupine;
    private object? _recorder;

    public PorcupineWakeWordService(AudioOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public bool IsListening => _listenCts is not null && !_listenCts.IsCancellationRequested;

    public event EventHandler? WakeWordDetected;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.WakeWordEnabled ||
            string.IsNullOrWhiteSpace(_options.PorcupineAccessKey) ||
            string.IsNullOrWhiteSpace(_options.PorcupineKeywordPath) ||
            !File.Exists(_options.PorcupineKeywordPath))
        {
            return Task.CompletedTask;
        }

        if (IsListening)
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
        DisposeNative();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        DisposeNative();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var porcupineType = Type.GetType("Pv.Porcupine, Porcupine");
            var recorderType = Type.GetType("Pv.PvRecorder, PvRecorder");
            if (porcupineType is null || recorderType is null)
            {
                return;
            }

            var keywordPaths = new[] { Path.GetFullPath(_options.PorcupineKeywordPath) };
            var modelPath = string.IsNullOrWhiteSpace(_options.PorcupineModelPath) ||
                            !File.Exists(_options.PorcupineModelPath)
                ? null
                : Path.GetFullPath(_options.PorcupineModelPath);

            var sensitivities = new[] { Math.Clamp(_options.PorcupineSensitivity, 0.01f, 1f) };

            var fromKeywordPaths = porcupineType.GetMethod("FromKeywordPaths",
                [typeof(string), typeof(IEnumerable<string>), typeof(IEnumerable<float>), typeof(string)]);
            if (fromKeywordPaths is null)
            {
                return;
            }

            _porcupine = fromKeywordPaths.Invoke(null,
                [_options.PorcupineAccessKey.Trim(), keywordPaths, sensitivities, modelPath]);
            if (_porcupine is null)
            {
                return;
            }

            var frameLength = (int)(porcupineType.GetProperty("FrameLength")?.GetValue(_porcupine) ?? 512);
            var sampleRate = (int)(porcupineType.GetProperty("SampleRate")?.GetValue(_porcupine) ?? 16000);

            var recorderCtor = recorderType.GetConstructor([typeof(int), typeof(int)]);
            _recorder = recorderCtor?.Invoke([frameLength, 1]);
            if (_recorder is null)
            {
                return;
            }

            recorderType.GetMethod("Start")?.Invoke(_recorder, null);
            var readMethod = recorderType.GetMethod("Read");
            var processMethod = porcupineType.GetMethod("Process", [typeof(short[])]);
            if (readMethod is null || processMethod is null)
            {
                return;
            }

            var frame = new short[frameLength];
            while (!cancellationToken.IsCancellationRequested)
            {
                readMethod.Invoke(_recorder, [frame]);
                var index = (int)(processMethod.Invoke(_porcupine, [frame]) ?? -1);
                if (index >= 0)
                {
                    WakeWordDetected?.Invoke(this, EventArgs.Empty);
                    await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                }

                await Task.Yield();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // expected
        }
        catch (Exception)
        {
            // Porcupine not configured or native load failed — hotkey remains fallback.
        }
        finally
        {
            DisposeNative();
        }
    }

    private void DisposeNative()
    {
        try
        {
            if (_recorder is not null)
            {
                var recorderType = _recorder.GetType();
                recorderType.GetMethod("Stop")?.Invoke(_recorder, null);
                recorderType.GetMethod("Dispose")?.Invoke(_recorder, null);
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            if (_porcupine is not null)
            {
                _porcupine.GetType().GetMethod("Dispose")?.Invoke(_porcupine, null);
            }
        }
        catch
        {
            // ignore
        }

        _recorder = null;
        _porcupine = null;
    }
}
