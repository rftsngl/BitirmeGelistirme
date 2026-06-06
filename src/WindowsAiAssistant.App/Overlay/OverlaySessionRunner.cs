using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.App.Audio;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.Overlay;

public sealed class OverlaySessionRunner
{
    private readonly OverlayViewModel _viewModel;
    private readonly AgentLoop _agentLoop;
    private readonly ISpeechToTextService _speechToText;
    private readonly ITextToSpeechService _textToSpeech;
    private readonly IActiveProviderStatus _providerStatus;
    private readonly AudioOptions _audioOptions;
    private readonly ActionApprovalCoordinator _approvalCoordinator;
    private CancellationTokenSource? _sessionCts;
    private PendingApprovalRequest? _overlayApprovalRequest;

    public OverlaySessionRunner(
        OverlayViewModel viewModel,
        AgentLoop agentLoop,
        ISpeechToTextService speechToText,
        ITextToSpeechService textToSpeech,
        IActiveProviderStatus providerStatus,
        AudioOptions audioOptions,
        ActionApprovalCoordinator approvalCoordinator)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _agentLoop = agentLoop ?? throw new ArgumentNullException(nameof(agentLoop));
        _speechToText = speechToText ?? throw new ArgumentNullException(nameof(speechToText));
        _textToSpeech = textToSpeech ?? throw new ArgumentNullException(nameof(textToSpeech));
        _providerStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));
        _audioOptions = audioOptions ?? throw new ArgumentNullException(nameof(audioOptions));
        _approvalCoordinator = approvalCoordinator ?? throw new ArgumentNullException(nameof(approvalCoordinator));
    }

    public void CancelActiveSession()
    {
        _sessionCts?.Cancel();
        _textToSpeech.StopSpeaking();
    }

    private AssistantOverlayWindow? _activeWindow;

    public async Task RunAsync(AssistantOverlayWindow window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        _activeWindow = window;

        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var token = _sessionCts.Token;
        await window.DispatcherQueue.EnqueueAsync(() =>
        {
            _viewModel.ResetForSession();
            window.ShowAndPosition();
            return Task.CompletedTask;
        }).ConfigureAwait(true);

        if (!_providerStatus.IsReady)
        {
            await window.DispatcherQueue.EnqueueAsync(() =>
            {
                _viewModel.SetError(_providerStatus.ReadyReason);
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            await ScheduleAutoCloseAsync(window, token).ConfigureAwait(true);
            return;
        }

        string? transcript;
        try
        {
            await window.DispatcherQueue.EnqueueAsync(() =>
            {
                _viewModel.SetTranscribing();
                return Task.CompletedTask;
            }).ConfigureAwait(true);

            transcript = await _speechToText.ListenOnceAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await window.DispatcherQueue.EnqueueAsync(() =>
            {
                _viewModel.SetHidden();
                window.HideOverlay();
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            return;
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            await window.DispatcherQueue.EnqueueAsync(() =>
            {
                _viewModel.SetError("Konusma algilanamadi. Tekrar deneyin veya Ctrl+Alt+A ile acin.");
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            await ScheduleAutoCloseAsync(window, token).ConfigureAwait(true);
            return;
        }

        await window.DispatcherQueue.EnqueueAsync(() =>
        {
            _viewModel.SetCommandText(transcript);
            _viewModel.SetRunning("Agent baslatiliyor...");
            return Task.CompletedTask;
        }).ConfigureAwait(true);

        var progress = new Progress<AgentStepProgress>(update =>
        {
            window.DispatcherQueue.TryEnqueue(() =>
            {
                var step = Math.Min(update.StepIndex + 1, update.MaxSteps);
                _viewModel.SetRunning(
                    string.IsNullOrWhiteSpace(update.Detail)
                        ? $"Adim {step}/{update.MaxSteps}: {update.Phase}"
                        : $"Adim {step}/{update.MaxSteps}: {update.Phase} — {update.Detail}");
            });
        });

        AgentLoopResult result;
        _approvalCoordinator.SetOverlayMode(true);
        _approvalCoordinator.OverlayApprovalRequested += OnOverlayApprovalRequested;
        try
        {
            result = await _agentLoop.RunAsync(transcript, token, progress, "voice_overlay").ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await window.DispatcherQueue.EnqueueAsync(() =>
            {
                _viewModel.SetHidden();
                window.HideOverlay();
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            return;
        }
        finally
        {
            _approvalCoordinator.OverlayApprovalRequested -= OnOverlayApprovalRequested;
            _approvalCoordinator.SetOverlayMode(false);
            _overlayApprovalRequest = null;
            _activeWindow = null;
        }

        if (!result.Success)
        {
            await window.DispatcherQueue.EnqueueAsync(() =>
            {
                _viewModel.SetError(result.ErrorMessage ?? "Agent calismasi basarisiz.");
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            await ScheduleAutoCloseAsync(window, token).ConfigureAwait(true);
            return;
        }

        var assistantMessage = result.AssistantMessage ?? "Tamamlandi.";
        await window.DispatcherQueue.EnqueueAsync(() =>
        {
            _viewModel.SetResult(transcript, assistantMessage);
            return Task.CompletedTask;
        }).ConfigureAwait(true);

        if (_textToSpeech.IsEnabled)
        {
            try
            {
                await _textToSpeech.SpeakAsync(assistantMessage, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // overlay closed during TTS
            }
        }

        await ScheduleAutoCloseAsync(window, token).ConfigureAwait(true);
    }

    private void OnOverlayApprovalRequested(object? sender, PendingApprovalRequest request)
    {
        _overlayApprovalRequest = request;
        var window = _activeWindow;
        if (window is null)
        {
            return;
        }

        window.DispatcherQueue.TryEnqueue(() => _viewModel.SetApprovalPending(request));
    }

    public void ApproveOverlayPending() => _viewModel.ApprovePending(_overlayApprovalRequest);

    public void DenyOverlayPending() => _viewModel.DenyPending(_overlayApprovalRequest);

    private async Task ScheduleAutoCloseAsync(AssistantOverlayWindow window, CancellationToken token)
    {
        var delaySeconds = Math.Clamp(_audioOptions.OverlayAutoCloseSeconds, 3, 120);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }

        await window.DispatcherQueue.EnqueueAsync(() =>
        {
            _viewModel.SetHidden();
            window.HideOverlay();
            return Task.CompletedTask;
        }).ConfigureAwait(true);
    }
}

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this DispatcherQueue queue, Func<Task> action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(async () =>
            {
                try
                {
                    await action().ConfigureAwait(false);
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
        {
            tcs.TrySetException(new InvalidOperationException("Dispatcher kuyrugu reddetti."));
        }

        return tcs.Task;
    }
}
