using WindowsAiAssistant.App.Audio;
using WindowsAiAssistant.App.Mvvm;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class SpeechModelDownloadItemViewModel : ObservableObject
{
    private SpeechModelDownloadState _state = SpeechModelDownloadState.NotDownloaded;
    private double _progressPercent;
    private bool _isIndeterminate;
    private string _statusText = "İndirilmedi";
    private string _progressStageText = string.Empty;
    private string? _errorMessage;

    public SpeechModelDownloadItemViewModel(
        SpeechModelInfo info,
        Func<SpeechModelDownloadItemViewModel, Task> downloadAsync)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        DownloadCommand = new AsyncRelayCommand(
            () => downloadAsync(this),
            () => CanDownload);
    }

    public SpeechModelInfo Info { get; }

    public string Id => Info.Id;

    public string Category => Info.Category;

    public string Title => Info.DisplayName;

    public string SizeHint => Info.SizeHint;

    public string FileName => Info.Detail;

    public string MetaLine => $"{SizeHint} · {FileName}";

    public bool IsActiveForSettings => Info.IsActiveForSettings;

    public SpeechModelDownloadState State
    {
        get => _state;
        private set
        {
            if (SetField(ref _state, value))
            {
                NotifyPresentationPropertiesChanged();
                DownloadCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string ProgressStageText
    {
        get => _progressStageText;
        private set => SetField(ref _progressStageText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasErrorMessage));
            }
        }
    }

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

    public double ProgressPercent
    {
        get => _progressPercent;
        private set
        {
            if (SetField(ref _progressPercent, value))
            {
                OnPropertyChanged(nameof(ProgressLabel));
            }
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set
        {
            if (SetField(ref _isIndeterminate, value))
            {
                OnPropertyChanged(nameof(ProgressLabel));
            }
        }
    }

    public string ProgressLabel =>
        IsIndeterminate ? "Hesaplanıyor…" : $"%{Math.Clamp((int)Math.Round(ProgressPercent), 0, 100)}";

    public bool ShowProgress => State == SpeechModelDownloadState.Downloading;

    public bool CanDownload => State is SpeechModelDownloadState.NotDownloaded or SpeechModelDownloadState.Failed;

    public bool IsDownloaded => State == SpeechModelDownloadState.Downloaded;

    public bool ShowReadyIcon => State == SpeechModelDownloadState.Downloaded;

    public bool ShowDownloadButton => CanDownload;

    public AsyncRelayCommand DownloadCommand { get; }

    public void ApplyInventoryState(SpeechModelDownloadState state)
    {
        if (State == SpeechModelDownloadState.Downloading)
        {
            return;
        }

        State = state;
        StatusText = SpeechModelInventoryService.DescribeState(state);
        ProgressStageText = string.Empty;
        if (state != SpeechModelDownloadState.Failed)
        {
            ErrorMessage = null;
        }

        ProgressPercent = state == SpeechModelDownloadState.Downloaded ? 100 : 0;
        IsIndeterminate = false;
    }

    public void BeginDownload()
    {
        State = SpeechModelDownloadState.Downloading;
        StatusText = "İndiriliyor";
        ProgressStageText = "Bağlanılıyor…";
        ErrorMessage = null;
        ProgressPercent = 0;
        IsIndeterminate = true;
    }

    public void ReportProgress(ModelDownloadProgress progress)
    {
        ProgressStageText = string.IsNullOrWhiteSpace(progress.Stage) ? "İndiriliyor" : progress.Stage;
        if (progress.Percent >= 0)
        {
            IsIndeterminate = false;
            ProgressPercent = Math.Clamp(progress.Percent, 0, 100);
            StatusText = "İndiriliyor";
        }
        else
        {
            IsIndeterminate = true;
            StatusText = "İndiriliyor";
        }
    }

    public void MarkDownloaded()
    {
        State = SpeechModelDownloadState.Downloaded;
        StatusText = "İndirildi";
        ProgressStageText = string.Empty;
        ProgressPercent = 100;
        IsIndeterminate = false;
        ErrorMessage = null;
    }

    public void MarkFailed(string message)
    {
        State = SpeechModelDownloadState.Failed;
        StatusText = "Hata";
        ProgressStageText = string.Empty;
        ErrorMessage = message;
        IsIndeterminate = false;
    }

    private void NotifyPresentationPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(IsDownloaded));
        OnPropertyChanged(nameof(ShowReadyIcon));
        OnPropertyChanged(nameof(ShowDownloadButton));
    }
}
