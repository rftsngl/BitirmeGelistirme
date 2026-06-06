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
    Result,
    Error
}

public sealed class OverlayViewModel : ObservableObject
{
    private OverlayPhase _phase = OverlayPhase.Hidden;
    private string _statusText = "Hazir";
    private string _detailText = string.Empty;
    private string _transcriptText = string.Empty;
    private string _resultText = string.Empty;
    private bool _isBusy;

    public OverlayPhase Phase
    {
        get => _phase;
        private set
        {
            if (SetField(ref _phase, value))
            {
                OnPropertyChanged(nameof(IsListening));
                OnPropertyChanged(nameof(IsRunning));
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

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool IsListening => Phase == OverlayPhase.Listening;
    public bool IsRunning => Phase is OverlayPhase.Transcribing or OverlayPhase.Running;
    public bool IsAwaitingApproval => Phase == OverlayPhase.ApprovalPending;

    public void SetApprovalPending(PendingApprovalRequest request)
    {
        Phase = OverlayPhase.ApprovalPending;
        StatusText = "Onay gerekli";
        DetailText = request.GateDecision.Summary;
        ResultText = request.GateDecision.Reason;
        IsBusy = false;
    }

    public void ApprovePending(PendingApprovalRequest? request)
    {
        request?.Approve();
        if (Phase == OverlayPhase.ApprovalPending)
        {
            Phase = OverlayPhase.Running;
            StatusText = "Calisiyor";
            IsBusy = true;
        }
    }

    public void DenyPending(PendingApprovalRequest? request)
    {
        request?.Deny();
        if (Phase == OverlayPhase.ApprovalPending)
        {
            Phase = OverlayPhase.Running;
            StatusText = "Calisiyor";
            IsBusy = true;
        }
    }

    public void ResetForSession()
    {
        TranscriptText = string.Empty;
        ResultText = string.Empty;
        DetailText = string.Empty;
        StatusText = "Dinliyorum...";
        Phase = OverlayPhase.Listening;
        IsBusy = true;
    }

    public void SetTranscribing()
    {
        Phase = OverlayPhase.Transcribing;
        StatusText = "Konusmaniz algilaniyor...";
    }

    public void SetCommandText(string transcript) => TranscriptText = transcript;

    public void SetRunning(string detail)
    {
        Phase = OverlayPhase.Running;
        StatusText = "Calisiyor";
        DetailText = detail;
    }

    public void SetResult(string transcript, string result)
    {
        Phase = OverlayPhase.Result;
        TranscriptText = transcript;
        ResultText = result;
        StatusText = "Tamamlandi";
        DetailText = string.Empty;
        IsBusy = false;
    }

    public void SetError(string message)
    {
        Phase = OverlayPhase.Error;
        StatusText = "Hata";
        DetailText = message;
        IsBusy = false;
    }

    public void SetHidden()
    {
        Phase = OverlayPhase.Hidden;
        IsBusy = false;
    }
}
