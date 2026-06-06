using System.Collections.ObjectModel;
using System.Windows.Input;
using WindowsAiAssistant.App.Mvvm;

namespace WindowsAiAssistant.App.Models;

public enum ConversationItemKind
{
    UserMessage,
    AssistantStatus,
    LiveActivity,
    PendingApproval,
    ResultCard,
    ErrorCard
}

public sealed class ConversationItem : ObservableObject
{
    private string _statusBadge = string.Empty;
    private string _resolutionNote = string.Empty;
    private bool _isApprovalResolved;
    private bool _isActive;
    private string _liveStatusLine = string.Empty;
    private string _liveDetailLine = string.Empty;
    private int _currentStepIndex;
    private int _maxSteps;

    public required ConversationItemKind Kind { get; init; }
    public bool DeveloperModeEnabled { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string SelectedTool { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public string SafetyDisposition { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string ToolExecutionResult { get; init; } = string.Empty;
    public string StepInfo { get; init; } = string.Empty;
    public string ObservationSummary { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
    public bool ShowSessionRemember { get; init; }

    public ICommand? PrimaryCommand { get; init; }
    public ICommand? SecondaryCommand { get; init; }
    public string PrimaryCommandLabel { get; init; } = string.Empty;
    public string SecondaryCommandLabel { get; init; } = string.Empty;

    public string TimestampDisplay =>
        TimestampUtc.ToLocalTime().ToString("HH:mm");

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public string LiveStatusLine
    {
        get => _liveStatusLine;
        set => SetField(ref _liveStatusLine, value);
    }

    public string LiveDetailLine
    {
        get => _liveDetailLine;
        set
        {
            if (SetField(ref _liveDetailLine, value))
            {
                OnPropertyChanged(nameof(HasLiveDetail));
            }
        }
    }

    public bool HasLiveDetail => !string.IsNullOrWhiteSpace(LiveDetailLine);

    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        set
        {
            if (SetField(ref _currentStepIndex, value))
            {
                OnPropertyChanged(nameof(StepProgressDisplay));
            }
        }
    }

    public int MaxSteps
    {
        get => _maxSteps;
        set
        {
            if (SetField(ref _maxSteps, value))
            {
                OnPropertyChanged(nameof(StepProgressDisplay));
            }
        }
    }

    public string StepProgressDisplay =>
        MaxSteps > 0 ? $"Adım {Math.Clamp(CurrentStepIndex, 1, MaxSteps)}/{MaxSteps}" : string.Empty;

    public ObservableCollection<ActivityTimelineEntry> Timeline { get; } = [];

    public bool HasTimeline => Timeline.Count > 0;

    public string StatusBadge
    {
        get => _statusBadge;
        set => SetField(ref _statusBadge, value);
    }

    public string ResolutionNote
    {
        get => _resolutionNote;
        set => SetField(ref _resolutionNote, value);
    }

    public bool IsApprovalResolved
    {
        get => _isApprovalResolved;
        set
        {
            if (SetField(ref _isApprovalResolved, value))
            {
                OnPropertyChanged(nameof(IsApprovalActionable));
            }
        }
    }

    private bool _allowForSession;
    public bool AllowForSession
    {
        get => _allowForSession;
        set => SetField(ref _allowForSession, value);
    }

    public bool IsApprovalActionable =>
        Kind == ConversationItemKind.PendingApproval && !IsApprovalResolved;
}
