using System.Collections.ObjectModel;
using System.Windows.Input;
using WindowsAiAssistant.Agent.Planning;
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
    private string _planSummary = string.Empty;
    private string _planProgressLine = string.Empty;
    private string _planHeadline = string.Empty;
    private string _skillDomainLabel = string.Empty;
    private string _planRevisionLabel = string.Empty;

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
        MaxSteps > 0 ? $"Adım {Math.Clamp(CurrentStepIndex, 1, MaxSteps)} / {MaxSteps}" : string.Empty;

    public string PlanSummary
    {
        get => _planSummary;
        set
        {
            if (SetField(ref _planSummary, value))
            {
                OnPropertyChanged(nameof(HasPlanSummary));
            }
        }
    }

    public string PlanProgressLine
    {
        get => _planProgressLine;
        set
        {
            if (SetField(ref _planProgressLine, value))
            {
                OnPropertyChanged(nameof(HasPlanProgressLine));
            }
        }
    }

    public bool HasPlanSummary => !string.IsNullOrWhiteSpace(PlanSummary);

    public bool HasPlanProgressLine => !string.IsNullOrWhiteSpace(PlanProgressLine);

    public string PlanHeadline
    {
        get => _planHeadline;
        set
        {
            if (SetField(ref _planHeadline, value))
            {
                OnPropertyChanged(nameof(HasPlanHeadline));
                OnPropertyChanged(nameof(HasPlanPanel));
            }
        }
    }

    public string SkillDomainLabel
    {
        get => _skillDomainLabel;
        set
        {
            if (SetField(ref _skillDomainLabel, value))
            {
                OnPropertyChanged(nameof(HasSkillDomainLabel));
            }
        }
    }

    public string PlanRevisionLabel
    {
        get => _planRevisionLabel;
        set
        {
            if (SetField(ref _planRevisionLabel, value))
            {
                OnPropertyChanged(nameof(HasPlanRevisionLabel));
            }
        }
    }

    public bool HasPlanHeadline => !string.IsNullOrWhiteSpace(PlanHeadline);

    public bool HasSkillDomainLabel => !string.IsNullOrWhiteSpace(SkillDomainLabel);

    public bool HasPlanRevisionLabel => !string.IsNullOrWhiteSpace(PlanRevisionLabel);

    public ObservableCollection<PlanStepDisplayLine> PlanSteps { get; } = [];

    public bool HasPlanSteps => PlanSteps.Count > 0;

    public bool HasPlanPanel => HasPlanSteps || HasPlanHeadline || HasPlanProgressLine;

    public void NotifyPlanChanged()
    {
        OnPropertyChanged(nameof(HasPlanSteps));
        OnPropertyChanged(nameof(HasPlanPanel));
    }

    public ObservableCollection<ActivityTimelineEntry> Timeline { get; } = [];

    public bool HasTimeline => Timeline.Count > 0;

    public void NotifyTimelineChanged() => OnPropertyChanged(nameof(HasTimeline));

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
