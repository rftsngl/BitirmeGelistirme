using System.Windows.Input;
using WindowsAiAssistant.App.Mvvm;

namespace WindowsAiAssistant.App.Models;

public enum ConversationItemKind
{
    UserMessage,
    AssistantStatus,
    PendingApproval,
    ResultCard,
    ErrorCard
}

public sealed class ConversationItem : ObservableObject
{
    private string _statusBadge = string.Empty;
    private string _resolutionNote = string.Empty;
    private bool _isApprovalResolved;

    public required ConversationItemKind Kind { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string SelectedTool { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public string SafetyDisposition { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string ToolExecutionResult { get; init; } = string.Empty;

    public ICommand? PrimaryCommand { get; init; }
    public ICommand? SecondaryCommand { get; init; }
    public string PrimaryCommandLabel { get; init; } = string.Empty;
    public string SecondaryCommandLabel { get; init; } = string.Empty;

    public string TimestampDisplay =>
        TimestampUtc.ToLocalTime().ToString("HH:mm:ss");

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

    public bool IsApprovalActionable =>
        Kind == ConversationItemKind.PendingApproval && !IsApprovalResolved;
}
