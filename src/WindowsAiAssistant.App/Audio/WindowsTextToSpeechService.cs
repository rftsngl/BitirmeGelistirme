using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

public sealed class WindowsTextToSpeechService : ITextToSpeechService
{
    private readonly AudioOptions _options;
    private readonly object _gate = new();
    private MediaPlayer? _player;

    public WindowsTextToSpeechService(AudioOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public bool IsEnabled => _options.TextToSpeechEnabled;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var spoken = text.Length > 500 ? text[..500] + "..." : text;

        using var synthesizer = new SpeechSynthesizer();
        if (!string.IsNullOrWhiteSpace(_options.SpeechLanguage))
        {
            var voice = SpeechSynthesizer.AllVoices
                .FirstOrDefault(item =>
                    item.Language.StartsWith(_options.SpeechLanguage, StringComparison.OrdinalIgnoreCase));
            if (voice is not null)
            {
                synthesizer.Voice = voice;
            }
        }

        var stream = await synthesizer.SynthesizeTextToStreamAsync(spoken).AsTask().ConfigureAwait(false);
        var player = new MediaPlayer();
        var playbackFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        player.MediaEnded += (_, _) => playbackFinished.TrySetResult();
        player.MediaFailed += (_, _) => playbackFinished.TrySetResult();
        player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
        player.Play();

        lock (_gate)
        {
            _player?.Dispose();
            _player = player;
        }

        using var registration = cancellationToken.Register(StopSpeaking);
        try
        {
            await playbackFinished.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StopSpeaking();
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_player, player))
                {
                    _player.Dispose();
                    _player = null;
                }
            }
        }
    }

    public void StopSpeaking()
    {
        lock (_gate)
        {
            if (_player is null)
            {
                return;
            }

            try
            {
                _player.Pause();
                _player.Dispose();
            }
            catch
            {
                // ignore dispose races
            }
            finally
            {
                _player = null;
            }
        }
    }
}
