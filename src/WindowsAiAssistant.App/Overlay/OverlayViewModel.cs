using WindowsAiAssistant.App.Audio;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.Overlay;

public enum OverlayPhase
{
    Hidden,
    Listening,
    Transcribing,
    Running,
    ApprovalPending,
    ManualInput,
    Result,
    Error
}

public sealed class OverlayViewModel : ObservableObject
{
    private OverlayPhase _phase = OverlayPhase.Hidden;
    private string _statusText = "Hazır";
    private string _substatusText = string.Empty;
    private string _detailText = string.Empty;
    private string _developerDetailText = string.Empty;
    private string _transcriptText = string.Empty;
    private string _resultText = string.Empty;
    private string _liveTranscript = string.Empty;
    private string _manualInputText = string.Empty;
    private bool _isBusy;
    private bool _isSpeaking;
    private bool _isAssistantSpeaking;
    private bool _showDeveloperDetail;

    public OverlayPhase Phase
    {
        get => _phase;
        private set
        {
            if (SetField(ref _phase, value))
            {
                OnPropertyChanged(nameof(IsListening));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(ShowManualInput));
                OnPropertyChanged(nameof(IsCompactMode));
                OnPropertyChanged(nameof(ShowListeningDots));
                OnPropertyChanged(nameof(ShowThinkingDots));
                OnPropertyChanged(nameof(ShowCompactHint));
                OnPropertyChanged(nameof(ShowPillSubstatus));
                OnPropertyChanged(nameof(ShowProgressRing));
                OnPropertyChanged(nameof(ShowExpandedPanel));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string SubstatusText
    {
        get => _substatusText;
        private set
        {
            if (SetField(ref _substatusText, value))
            {
                OnPropertyChanged(nameof(ShowCompactHint));
                OnPropertyChanged(nameof(ShowPillSubstatus));
            }
        }
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetField(ref _detailText, value);
    }

    public string DeveloperDetailText
    {
        get => _developerDetailText;
        private set
        {
            if (SetField(ref _developerDetailText, value))
            {
                OnPropertyChanged(nameof(ShowDeveloperDetail));
            }
        }
    }

    public bool ShowDeveloperDetail =>
        _showDeveloperDetail && !string.IsNullOrWhiteSpace(DeveloperDetailText);

    public string TranscriptText
    {
        get => _transcriptText;
        private set => SetField(ref _transcriptText, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set => SetField(ref _resultText, value);
    }

    public string LiveTranscript
    {
        get => _liveTranscript;
        private set
        {
            if (SetField(ref _liveTranscript, value))
            {
                OnPropertyChanged(nameof(HasLiveTranscript));
                OnPropertyChanged(nameof(ShowCompactHint));
            }
        }
    }

    public bool HasLiveTranscript => !string.IsNullOrWhiteSpace(LiveTranscript);

    public bool IsSpeaking
    {
        get => _isSpeaking;
        private set
        {
            if (SetField(ref _isSpeaking, value))
            {
                OnPropertyChanged(nameof(ShowSpeakingActivity));
                OnPropertyChanged(nameof(ShowCompactHint));
            }
        }
    }

    public bool IsAssistantSpeaking
    {
        get => _isAssistantSpeaking;
        private set
        {
            if (SetField(ref _isAssistantSpeaking, value))
            {
                OnPropertyChanged(nameof(ShowSpeakingActivity));
                OnPropertyChanged(nameof(ShowListeningDots));
            }
        }
    }

    public bool ShowSpeakingActivity => IsSpeaking || IsAssistantSpeaking;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowProgressRing));
            }
        }
    }

    public bool IsListening => Phase == OverlayPhase.Listening;
    public bool IsRunning => Phase is OverlayPhase.Transcribing or OverlayPhase.Running;

    public bool IsAwaitingApproval => Phase == OverlayPhase.ApprovalPending;
    public bool ShowManualInput => Phase == OverlayPhase.ManualInput;

    public bool IsCompactMode =>
        Phase is OverlayPhase.Listening
            or OverlayPhase.Transcribing
            or OverlayPhase.Running;

    public bool ShowExpandedPanel => !IsCompactMode;

    public bool ShowListeningDots =>
        Phase == OverlayPhase.Listening && !IsAssistantSpeaking;

    public bool ShowThinkingDots =>
        Phase is OverlayPhase.Transcribing or OverlayPhase.Running;

    public bool ShowCompactHint =>
        Phase == OverlayPhase.Listening
        && !HasLiveTranscript
        && !IsSpeaking
        && !string.IsNullOrWhiteSpace(SubstatusText);

    public bool ShowPillSubstatus =>
        IsCompactMode
        && !HasLiveTranscript
        && !string.IsNullOrWhiteSpace(SubstatusText);

    public bool ShowProgressRing =>
        IsBusy && Phase is OverlayPhase.ApprovalPending;

    /// <summary>
    /// Yeni acilan uygulama odağı aldiginda oturumu kesmemek icin yalnizca güvenli fazlarda kapatilir.
    /// (open_app sonrasi TTS veya agent calisirken overlay kapanmamali.)
    /// </summary>
    public bool CanDismissOnFocusLoss =>
        !IsAssistantSpeaking
        && !IsSpeaking
        && Phase is OverlayPhase.ManualInput or OverlayPhase.Error
            or OverlayPhase.Result;

    public string ManualInputText
    {
        get => _manualInputText;
        set => SetField(ref _manualInputText, value);
    }

    public void ConfigureDeveloperMode(bool enabled) => _showDeveloperDetail = enabled;

    public void UpdateListenProgress(SpeechListenProgress progress)
    {
        IsSpeaking = progress.IsSpeaking;

        if (!string.IsNullOrWhiteSpace(progress.PartialTranscript))
        {
            LiveTranscript = progress.PartialTranscript!;
            StatusText = "Dinliyorum";
            SubstatusText = string.Empty;
            DetailText = string.Empty;
        }
        else if (progress.IsSpeaking)
        {
            StatusText = "Dinliyorum";
            SubstatusText = string.Empty;
        }
        else if (string.IsNullOrWhiteSpace(LiveTranscript))
        {
            StatusText = "Dinliyorum";
            SubstatusText = "Konuşabilirsiniz";
        }
    }

    public void SetManualInputPrompt(string detail)
    {
        Phase = OverlayPhase.ManualInput;
        StatusText = "Ses algılanmadı";
        SubstatusText = string.Empty;
        DetailText = detail;
        DeveloperDetailText = string.Empty;
        LiveTranscript = string.Empty;
        ManualInputText = string.Empty;
        IsBusy = false;
    }

    public void SetApprovalPending(PendingApprovalRequest request, bool voiceApprovalEnabled = false)
    {
        Phase = OverlayPhase.ApprovalPending;
        StatusText = "Onayınız gerekiyor";
        SubstatusText = voiceApprovalEnabled
            ? "\"Onayla\" veya \"Reddet\" diyebilirsiniz"
            : "Devam etmek için onaylayın";
        DetailText = request.GateDecision.Summary;
        ResultText = string.Empty;
        DeveloperDetailText = request.GateDecision.Reason;
        LiveTranscript = string.Empty;
        IsBusy = false;
    }

    public void ApprovePending(PendingApprovalRequest? request)
    {
        request?.Approve();
        if (Phase == OverlayPhase.ApprovalPending)
        {
            Phase = OverlayPhase.Running;
            StatusText = "Çalışıyor";
            IsBusy = true;
        }
    }

    public void DenyPending(PendingApprovalRequest? request)
    {
        request?.Deny();
        if (Phase == OverlayPhase.ApprovalPending)
        {
            Phase = OverlayPhase.Running;
            StatusText = "Çalışıyor";
            IsBusy = true;
        }
    }

    public void ResetForSession()
    {
        TranscriptText = string.Empty;
        ResultText = string.Empty;
        DetailText = string.Empty;
        DeveloperDetailText = string.Empty;
        LiveTranscript = string.Empty;
        StatusText = "Dinliyorum";
        SubstatusText = "Konuşabilirsiniz";
        Phase = OverlayPhase.Listening;
        IsBusy = true;
    }

    public void SetTranscribing()
    {
        Phase = OverlayPhase.Transcribing;
        StatusText = "Anlıyorum";
        SubstatusText = "Söylediklerinizi düzenliyorum…";
        DetailText = string.Empty;
        IsBusy = true;
    }

    public void SetCommandText(string transcript) => TranscriptText = transcript;

    public void SetRunning(string statusLabel, string? substatus = null, string? developerDetail = null)
    {
        Phase = OverlayPhase.Running;
        StatusText = statusLabel;
        SubstatusText = substatus ?? string.Empty;
        DetailText = string.Empty;
        DeveloperDetailText = developerDetail ?? string.Empty;
        LiveTranscript = string.Empty;
        IsBusy = true;
    }

    public void SetResult(string transcript, string result)
    {
        Phase = OverlayPhase.Result;
        TranscriptText = transcript;
        ResultText = result;
        StatusText = "Hazır";
        SubstatusText = string.Empty;
        DetailText = string.Empty;
        DeveloperDetailText = string.Empty;
        LiveTranscript = string.Empty;
        IsBusy = false;
        IsAssistantSpeaking = false;
    }

    public void SetAssistantSpeaking(bool speaking)
    {
        IsAssistantSpeaking = speaking;
        if (speaking)
        {
            StatusText = "Seslendiriyor";
            SubstatusText = string.Empty;
        }
        else if (Phase == OverlayPhase.Result)
        {
            StatusText = "Hazır";
        }
    }

    public void SetFollowUpListening()
    {
        Phase = OverlayPhase.Listening;
        StatusText = "Dinliyorum";
        SubstatusText = "Devam edebilirsiniz";
        TranscriptText = string.Empty;
        ResultText = string.Empty;
        DetailText = string.Empty;
        DeveloperDetailText = string.Empty;
        LiveTranscript = string.Empty;
        IsBusy = true;
    }

    public void SetError(string message)
    {
        Phase = OverlayPhase.Error;
        StatusText = "Bir sorun oluştu";
        SubstatusText = string.Empty;
        DetailText = message;
        DeveloperDetailText = string.Empty;
        LiveTranscript = string.Empty;
        IsBusy = false;
    }

    public void SetHidden()
    {
        Phase = OverlayPhase.Hidden;
        IsBusy = false;
        LiveTranscript = string.Empty;
    }
}
