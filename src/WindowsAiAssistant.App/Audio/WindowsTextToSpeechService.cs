using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

public sealed class WindowsTextToSpeechService : ITextToSpeechService
{
    private readonly AudioOptions _options;
    private readonly NaudioAudioPlayback _playback = new();

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
        var voice = SpeechVoiceResolver.ResolveVoice(_options.SpeechLanguage, _options.TtsVoiceName);
        if (voice is not null)
        {
            synthesizer.Voice = voice;
        }

        synthesizer.Options.SpeakingRate = Math.Clamp(_options.TtsSpeakingRate, 0.5, 2.0);
        synthesizer.Options.AudioPitch = 1.0;
        synthesizer.Options.AudioVolume = 1.0;

        var stream = await synthesizer.SynthesizeTextToStreamAsync(spoken).AsTask().ConfigureAwait(false);
        using var memory = new MemoryStream();
        await CopyToMemoryStreamAsync(stream, memory, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;

        await _playback.PlayWaveStreamAsync(memory, cancellationToken).ConfigureAwait(false);
    }

    public void StopSpeaking() => _playback.Stop();

    private static async Task CopyToMemoryStreamAsync(
        IRandomAccessStreamWithContentType stream,
        MemoryStream destination,
        CancellationToken cancellationToken)
    {
        using var input = stream.AsStreamForRead();
        await input.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }
}
