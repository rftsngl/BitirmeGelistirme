using WindowsAiAssistant.App.Mvvm;

namespace WindowsAiAssistant.App.Models;

public enum TimelineEntryState
{
    Active,
    Completed,
    Failed
}

public sealed class ActivityTimelineEntry : ObservableObject
{
    private TimelineEntryState _state;
    private string _detail = string.Empty;

    public required string Label { get; init; }

    public string Detail
    {
        get => _detail;
        set
        {
            if (SetField(ref _detail, value))
            {
                OnPropertyChanged(nameof(HasDetail));
            }
        }
    }

    public TimelineEntryState State
    {
        get => _state;
        set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsFailed));
            }
        }
    }

    public bool IsActive => State == TimelineEntryState.Active;
    public bool IsCompleted => State == TimelineEntryState.Completed;
    public bool IsFailed => State == TimelineEntryState.Failed;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}
