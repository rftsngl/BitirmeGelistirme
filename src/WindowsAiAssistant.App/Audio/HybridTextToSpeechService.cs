using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// Varsayilan Edge neural TTS; baglanti yoksa Windows yerel sese duser.
/// </summary>
public sealed class HybridTextToSpeechService : ITextToSpeechService
{
    private readonly AudioOptions _options;
    private readonly EdgeTextToSpeechService _edge;
    private readonly WindowsTextToSpeechService _windows;

    public HybridTextToSpeechService(
        AudioOptions options,
        EdgeTextToSpeechService edge,
        WindowsTextToSpeechService windows)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _edge = edge ?? throw new ArgumentNullException(nameof(edge));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
    }

    public bool IsEnabled => _options.TextToSpeechEnabled;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (UsesWindowsOnly())
        {
            await _windows.SpeakAsync(text, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await _edge.SpeakAsync(text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _windows.SpeakAsync(text, cancellationToken).ConfigureAwait(false);
        }
    }

    public void StopSpeaking()
    {
        _edge.StopSpeaking();
        _windows.StopSpeaking();
    }

    private bool UsesWindowsOnly() =>
        string.Equals(_options.TtsEngine, "windows", StringComparison.OrdinalIgnoreCase);
}
