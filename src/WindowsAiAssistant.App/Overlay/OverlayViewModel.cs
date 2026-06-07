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
    private const int WaveformBarCount = 12;

    private OverlayPhase _phase = OverlayPhase.Hidden;
    private string _statusText = "Hazır";
    private string _detailText = string.Empty;
    private string _transcriptText = string.Empty;
    private string _resultText = string.Empty;
    private string _liveTranscript = string.Empty;
    private string _manualInputText = string.Empty;
    private bool _isBusy;
    private bool _isSpeaking;
    private bool _isAssistantSpeaking;
    private double _audioLevel;
    private int _waveformTick;
    private IReadOnlyList<double> _waveformBarHeights = CreateBarHeights(0, false, 0);

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
                OnPropertyChanged(nameof(ShowLiveListenPanel));
                OnPropertyChanged(nameof(IsCompactMode));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetField(ref _detailText, value);
    }

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
            }
        }
    }

    public bool HasLiveTranscript => !string.IsNullOrWhiteSpace(LiveTranscript);

    public IReadOnlyList<double> WaveformBarHeights
    {
        get => _waveformBarHeights;
        private set => SetField(ref _waveformBarHeights, value);
    }

    public bool IsSpeaking
    {
        get => _isSpeaking;
        private set
        {
            if (SetField(ref _isSpeaking, value))
            {
                OnPropertyChanged(nameof(ShowSpeakingActivity));
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
            }
        }
    }

    public bool ShowSpeakingActivity => IsSpeaking || IsAssistantSpeaking;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool IsListening => Phase == OverlayPhase.Listening;
    public bool IsRunning => Phase is OverlayPhase.Transcribing or OverlayPhase.Running;
    public bool IsAwaitingApproval => Phase == OverlayPhase.ApprovalPending;
    public bool ShowManualInput => Phase == OverlayPhase.ManualInput;
    public bool ShowLiveListenPanel => Phase == OverlayPhase.Listening;
    public bool IsCompactMode => Phase is OverlayPhase.Listening or OverlayPhase.Transcribing;

    public bool CanDismissOnFocusLoss =>
        Phase is OverlayPhase.ManualInput or OverlayPhase.Result or OverlayPhase.Error;

    public string ManualInputText
    {
        get => _manualInputText;
        set => SetField(ref _manualInputText, value);
    }

    public void UpdateListenProgress(SpeechListenProgress progress)
    {
        _audioLevel = progress.AudioLevel;
        IsSpeaking = progress.IsSpeaking;
        _waveformTick++;

        if (!string.IsNullOrWhiteSpace(progress.PartialTranscript))
        {
            LiveTranscript = progress.PartialTranscript!;
            StatusText = "Dinliyorum…";
            DetailText = string.Empty;
        }
        else if (progress.IsSpeaking)
        {
            StatusText = "Konuşuyorsunuz…";
            DetailText = string.Empty;
        }
        else if (string.IsNullOrWhiteSpace(LiveTranscript))
        {
            StatusText = "Dinliyorum…";
            DetailText = "Konuşmaya başlayın";
        }

        WaveformBarHeights = CreateBarHeights(_audioLevel, progress.IsSpeaking, _waveformTick);
    }

    public void SetManualInputPrompt(string detail)
    {
        Phase = OverlayPhase.ManualInput;
        StatusText = "Ses algılanmadı";
        DetailText = detail;
        LiveTranscript = string.Empty;
        ManualInputText = string.Empty;
        IsBusy = false;
    }

    public void SetApprovalPending(PendingApprovalRequest request, bool voiceApprovalEnabled = false)
    {
        Phase = OverlayPhase.ApprovalPending;
        StatusText = "Onay gerekli";
        DetailText = voiceApprovalEnabled
            ? $"{request.GateDecision.Summary}\n(\"Onayla\" / \"Reddet\" diyebilir veya butonu kullanabilirsiniz.)"
            : request.GateDecision.Summary;
        ResultText = request.GateDecision.Reason;
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
        DetailText = "Konuşmaya başlayın";
        LiveTranscript = string.Empty;
        StatusText = "Dinliyorum…";
        Phase = OverlayPhase.Listening;
        IsBusy = true;
        WaveformBarHeights = CreateBarHeights(0, false, 0);
    }

    public void SetTranscribing()
    {
        Phase = OverlayPhase.Transcribing;
        StatusText = "Anlıyorum…";
        DetailText = string.Empty;
        IsBusy = true;
    }

    public void SetCommandText(string transcript) => TranscriptText = transcript;

    public void SetRunning(string detail)
    {
        Phase = OverlayPhase.Running;
        StatusText = "Çalışıyor";
        DetailText = detail;
        LiveTranscript = string.Empty;
    }

    public void SetResult(string transcript, string result)
    {
        Phase = OverlayPhase.Result;
        TranscriptText = transcript;
        ResultText = result;
        StatusText = "Tamamlandı";
        DetailText = string.Empty;
        LiveTranscript = string.Empty;
        IsBusy = false;
        IsAssistantSpeaking = false;
    }

    public void SetAssistantSpeaking(bool speaking)
    {
        IsAssistantSpeaking = speaking;
        if (speaking)
        {
            StatusText = "Seslendiriliyor…";
            DetailText = string.Empty;
        }
        else if (Phase == OverlayPhase.Result)
        {
            StatusText = "Tamamlandı";
        }
    }

    public void SetFollowUpListening()
    {
        Phase = OverlayPhase.Listening;
        StatusText = "Dinliyorum…";
        DetailText = "Devam etmek için konuşun";
        TranscriptText = string.Empty;
        LiveTranscript = string.Empty;
        IsBusy = true;
        WaveformBarHeights = CreateBarHeights(0, false, 0);
    }

    public void SetError(string message)
    {
        Phase = OverlayPhase.Error;
        StatusText = "Hata";
        DetailText = message;
        LiveTranscript = string.Empty;
        IsBusy = false;
    }

    public void SetHidden()
    {
        Phase = OverlayPhase.Hidden;
        IsBusy = false;
        LiveTranscript = string.Empty;
    }

    private static IReadOnlyList<double> CreateBarHeights(double level, bool isSpeaking, int tick)
    {
        var bars = new double[WaveformBarCount];
        var energy = Math.Clamp(level * (isSpeaking ? 14.0 : 6.0), 0.05, 1.0);

        for (var i = 0; i < WaveformBarCount; i++)
        {
            var wave = Math.Abs(Math.Sin((i * 0.65) + (tick * 0.45)));
            var height = 6 + (energy * (10 + (wave * 18)));
            bars[i] = Math.Clamp(height, 6, 32);
        }

        return bars;
    }
}
