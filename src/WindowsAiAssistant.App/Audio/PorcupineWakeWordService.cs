using Pv;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Porcupine wake-word listener. AccessKey + .ppn keyword + opsiyonel .pv model gerekir.
/// Mikrofon: <see cref="AudioOptions.InputDeviceIndex"/> (-1 = varsayilan).
/// </summary>
public sealed class PorcupineWakeWordService : IWakeWordService
{
    private readonly AudioOptions _options;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;

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
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        Porcupine? porcupine = null;
        PvRecorder? recorder = null;

        try
        {
            var accessKey = SecretProtector.Unprotect(_options.PorcupineAccessKey.Trim());
            var keywordPaths = new[] { Path.GetFullPath(_options.PorcupineKeywordPath) };
            var sensitivities = new[] { Math.Clamp(_options.PorcupineSensitivity, 0.01f, 1f) };
            string? modelPath = string.IsNullOrWhiteSpace(_options.PorcupineModelPath) ||
                                !File.Exists(_options.PorcupineModelPath)
                ? null
                : Path.GetFullPath(_options.PorcupineModelPath);

            porcupine = Porcupine.FromKeywordPaths(
                accessKey,
                keywordPaths,
                modelPath,
                sensitivities);

            var deviceIndex = _options.InputDeviceIndex >= 0 ? _options.InputDeviceIndex : -1;
            recorder = PvRecorder.Create(porcupine.FrameLength, deviceIndex);
            recorder.Start();

            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = recorder.Read();
                var index = porcupine.Process(frame);
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
            // Porcupine yapilandirilmadi veya native yukleme basarisiz — hotkey fallback kalir.
        }
        finally
        {
            try
            {
                recorder?.Stop();
                recorder?.Dispose();
            }
            catch
            {
                // ignore
            }

            try
            {
                porcupine?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }
}
