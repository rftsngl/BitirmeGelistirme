using WindowsAiAssistant.App.Audio.EdgeTts;
using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Microsoft Edge neural TTS (tr-TR-EmelNeural vb.) — doğal ses, internet gerekir.
/// </summary>
public sealed class EdgeTextToSpeechService : ITextToSpeechService
{
    private readonly AudioOptions _options;
    private readonly EdgeTtsClient _client = new();
    private readonly NaudioAudioPlayback _playback = new();

    public EdgeTextToSpeechService(AudioOptions options) =>
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
        var voiceShortName = EdgeVoiceResolver.ResolveShortName(_options.SpeechLanguage, _options.TtsVoiceName);
        var rate = FormatSpeakingRate(_options.TtsSpeakingRate);
        var tempPath = Path.Combine(Path.GetTempPath(), $"waa-tts-{Guid.NewGuid():N}.mp3");

        try
        {
            await _client.SaveMp3Async(
                    spoken,
                    voiceShortName,
                    tempPath,
                    rate,
                    "+0%",
                    cancellationToken)
                .ConfigureAwait(false);

            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                throw new InvalidOperationException("Edge TTS ses dosyası oluşturulamadı.");
            }

            await _playback.PlayFileAsync(tempPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    public void StopSpeaking() => _playback.Stop();

    private static string FormatSpeakingRate(double rate)
    {
        var clamped = Math.Clamp(rate, 0.5, 2.0);
        var percent = (int)Math.Round((clamped - 1.0) * 100);
        return percent >= 0 ? $"+{percent}%" : $"{percent}%";
    }
}
