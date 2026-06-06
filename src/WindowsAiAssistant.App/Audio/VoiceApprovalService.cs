using WindowsAiAssistant.App.Configuration;

namespace WindowsAiAssistant.App.Audio;

/// <summary>
/// ActionGate onay ekraninda kisa STT ile evet/hayir dinler. Overlay ve sohbet UI ortak kullanir.
/// </summary>
public sealed class VoiceApprovalService
{
    private readonly ISpeechToTextService _speechToText;
    private readonly AudioOptions _options;
    private CancellationTokenSource? _listenCts;

    public VoiceApprovalService(ISpeechToTextService speechToText, AudioOptions options)
    {
        _speechToText = speechToText ?? throw new ArgumentNullException(nameof(speechToText));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Cancel()
    {
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        _listenCts = null;
    }

    public async Task ListenForDecisionAsync(
        Func<bool, Task> onDecision,
        CancellationToken cancellationToken = default)
    {
        if (!_options.VoiceApprovalEnabled)
        {
            return;
        }

        Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenCts = cts;

        try
        {
            for (var attempt = 0; attempt < 2 && !cts.IsCancellationRequested; attempt++)
            {
                string? heard;
                try
                {
                    heard = await _speechToText.ListenOnceAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    return;
                }

                var decision = VoiceApprovalInterpreter.Interpret(heard);
                if (decision is null)
                {
                    continue;
                }

                await onDecision(decision.Value).ConfigureAwait(false);
                return;
            }
        }
        finally
        {
            if (ReferenceEquals(_listenCts, cts))
            {
                _listenCts = null;
            }

            cts.Dispose();
        }
    }
}
