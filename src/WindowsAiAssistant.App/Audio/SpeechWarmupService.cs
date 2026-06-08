namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// STT motorunu uygulama acilisinda arka planda hazirlar (Whisper cold start azaltma).
/// </summary>
public sealed class SpeechWarmupService
{
    private readonly SwitchableSpeechToTextService _speechToText;

    public SpeechWarmupService(SwitchableSpeechToTextService speechToText) =>
        _speechToText = speechToText ?? throw new ArgumentNullException(nameof(speechToText));

    public Task WarmupAsync(CancellationToken cancellationToken = default) =>
        _speechToText.WarmupAsync(cancellationToken);
}
