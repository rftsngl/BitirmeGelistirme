using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Core;
using WindowsAiAssistant.Infrastructure;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class AssistantViewModel : ObservableObject
{
    private readonly IAuditLogger _auditLogger;
    private readonly AssistantOrchestrator _orchestrator;
    private readonly INavigationService _navigation;
    private readonly DispatcherQueue _dispatcherQueue;

    private PendingApprovalSnapshot? _pendingApprovalSnapshot;
    private CommandRequest? _pendingRequest;
    private string _pendingSelectedTool = "None";
    private SafetyRiskLevel _pendingRiskLevel = SafetyRiskLevel.Medium;
    private ConversationItem? _pendingApprovalItem;
    private bool _isBusy;
    private string _commandInput = string.Empty;
    private string _statusMessage = "Komut yazıp Gönder'e basın.";

    public AssistantViewModel(
        AssistantOrchestrator orchestrator,
        IAuditLogger auditLogger,
        INavigationService navigation,
        IActiveProviderStatus providerStatus)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        ProviderStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                          ?? DispatcherQueueController.CreateOnCurrentThread().DispatcherQueue;

        Conversation = [];
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(CommandInput));
        ClearSessionCommand = new RelayCommand(ClearSession, () => Conversation.Count > 0 || !string.IsNullOrWhiteSpace(CommandInput));
        OpenSettingsCommand = new RelayCommand(() => _navigation.NavigateToSettings());
        ProviderStatus.Refresh();
    }

    public IActiveProviderStatus ProviderStatus { get; }

    public ObservableCollection<ConversationItem> Conversation { get; }
    public ICommand SubmitCommand { get; }
    public ICommand ClearSessionCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    public string CommandInput
    {
        get => _commandInput;
        set
        {
            if (SetField(ref _commandInput, value))
            {
                RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public bool HasConversation => Conversation.Count > 0;
    public bool HasPendingApproval => _pendingRequest is not null;

    public void LoadCommand(string commandText)
    {
        CommandInput = commandText ?? string.Empty;
        StatusMessage = "Geçmişten yüklendi. Gönder'e basarak çalıştırabilirsiniz.";
    }

    private async Task SubmitAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var commandText = CommandInput.Trim();
        if (string.IsNullOrWhiteSpace(commandText))
        {
            StatusMessage = "Lütfen önce bir komut girin.";
            return;
        }

        IsBusy = true;
        try
        {
            var request = new CommandRequest { UserInput = commandText };
            AddItem(new ConversationItem
            {
                Kind = ConversationItemKind.UserMessage,
                Title = "Sen",
                Body = commandText
            });
            CommandInput = string.Empty;
            OnPropertyChanged(nameof(HasConversation));

            await LogCommandSubmittedAsync(request).ConfigureAwait(true);

            StatusMessage = "Model çağrılıyor…";
            var result = await _orchestrator.HandleAsync(request).ConfigureAwait(true);
            HandleResult(request, result);
            await LogCommandCompletedAsync(request, result, string.Empty).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AddItem(new ConversationItem
            {
                Kind = ConversationItemKind.ErrorCard,
                Title = "Beklenmeyen hata",
                Body = ex.Message
            });
            StatusMessage = "Bir hata oluştu.";
        }
        finally
        {
            IsBusy = false;
            RaiseCanExecuteChanged();
        }
    }

    private void HandleResult(CommandRequest request, CommandResult result)
    {
        if (result.Status == "PendingApproval")
        {
            _pendingRequest = request;
            _pendingApprovalSnapshot = result.PendingApprovalSnapshot;
            _pendingSelectedTool = string.IsNullOrWhiteSpace(result.SelectedTool) ? "None" : result.SelectedTool;
            if (!Enum.TryParse(result.RiskLevel, out _pendingRiskLevel))
            {
                _pendingRiskLevel = SafetyRiskLevel.Medium;
            }

            var approvalItem = new ConversationItem
            {
                Kind = ConversationItemKind.PendingApproval,
                Title = "Onay Gerekli",
                Body = string.IsNullOrWhiteSpace(result.Message)
                    ? "Bu komut için kullanıcı onayı bekleniyor."
                    : result.Message,
                SelectedTool = _pendingSelectedTool,
                RiskLevel = _pendingRiskLevel.ToString(),
                SafetyDisposition = result.Safety,
                Decision = result.Decision,
                ToolExecutionResult = result.ToolExecutionResult ?? string.Empty,
                StatusBadge = "BEKLİYOR",
                PrimaryCommand = new AsyncRelayCommand(ApproveAsync),
                SecondaryCommand = new AsyncRelayCommand(RejectAsync),
                PrimaryCommandLabel = "Onayla",
                SecondaryCommandLabel = "Reddet"
            };
            _pendingApprovalItem = approvalItem;
            AddItem(approvalItem);
            OnPropertyChanged(nameof(HasPendingApproval));
            StatusMessage = "Onay bekleniyor.";
            return;
        }

        ClearPendingApproval();
        AddItem(BuildResultItem(result));
        StatusMessage = result.Status switch
        {
            "Completed" => "Tamamlandı.",
            "Blocked" => "İşlem engellendi.",
            _ => result.Status
        };
    }

    private static ConversationItem BuildResultItem(CommandResult result)
    {
        var isError = string.Equals(result.Status, "Error", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(result.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(result.Safety, "Blocked", StringComparison.OrdinalIgnoreCase);

        return new ConversationItem
        {
            Kind = isError ? ConversationItemKind.ErrorCard : ConversationItemKind.ResultCard,
            Title = isError ? "Sonuç (engellendi)" : "Sonuç",
            Body = string.IsNullOrWhiteSpace(result.Message) ? "Komut işlendi." : result.Message,
            SelectedTool = string.IsNullOrWhiteSpace(result.SelectedTool) ? "None" : result.SelectedTool,
            RiskLevel = result.RiskLevel,
            SafetyDisposition = result.Safety,
            Decision = result.Decision,
            ToolExecutionResult = result.ToolExecutionResult ?? string.Empty,
            StatusBadge = result.Status?.ToUpperInvariant() ?? string.Empty
        };
    }

    private async Task ApproveAsync()
    {
        if (_pendingRequest is null)
        {
            return;
        }

        var pendingRequest = _pendingRequest;
        var pendingItem = _pendingApprovalItem;

        IsBusy = true;
        try
        {
            await LogApprovalDecisionAsync(pendingRequest, "Approved", "User approved pending command.").ConfigureAwait(true);
            var result = await _orchestrator
                .ExecuteApprovedAsync(pendingRequest, _pendingRiskLevel, _pendingApprovalSnapshot)
                .ConfigureAwait(true);

            if (pendingItem is not null)
            {
                pendingItem.IsApprovalResolved = true;
                pendingItem.StatusBadge = "ONAYLANDI";
                pendingItem.ResolutionNote = "Kullanıcı tarafından onaylandı.";
            }

            AddItem(BuildResultItem(result));
            await LogCommandCompletedAsync(pendingRequest, result, "Approved").ConfigureAwait(true);
            ClearPendingApproval();
            StatusMessage = "Onaylandı, işlem tamamlandı.";
        }
        catch (Exception ex)
        {
            AddItem(new ConversationItem
            {
                Kind = ConversationItemKind.ErrorCard,
                Title = "Onay sırasında hata",
                Body = ex.Message
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RejectAsync()
    {
        if (_pendingRequest is null)
        {
            return;
        }

        var pendingRequest = _pendingRequest;
        var pendingItem = _pendingApprovalItem;

        IsBusy = true;
        try
        {
            await LogApprovalDecisionAsync(pendingRequest, "Rejected", "User rejected pending command.").ConfigureAwait(true);
            var result = await _orchestrator
                .RejectPendingAsync(pendingRequest, _pendingRiskLevel, _pendingApprovalSnapshot, _pendingSelectedTool)
                .ConfigureAwait(true);

            if (pendingItem is not null)
            {
                pendingItem.IsApprovalResolved = true;
                pendingItem.StatusBadge = "REDDEDİLDİ";
                pendingItem.ResolutionNote = "Kullanıcı tarafından reddedildi.";
            }

            AddItem(BuildResultItem(result));
            await LogCommandCompletedAsync(pendingRequest, result, "Rejected").ConfigureAwait(true);
            ClearPendingApproval();
            StatusMessage = "Reddedildi.";
        }
        catch (Exception ex)
        {
            AddItem(new ConversationItem
            {
                Kind = ConversationItemKind.ErrorCard,
                Title = "Red sırasında hata",
                Body = ex.Message
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearSession()
    {
        Conversation.Clear();
        CommandInput = string.Empty;
        StatusMessage = HasPendingApproval
            ? "Gorunum temizlendi. Bekleyen onay durumu korunuyor."
            : "Gorunum temizlendi.";
        OnPropertyChanged(nameof(HasConversation));
        RaiseCanExecuteChanged();
    }

    public void CleanupRuntimeSessionState()
    {
        _orchestrator.ClearTransientSessionState();
        ClearPendingApproval();
        StatusMessage = "Runtime/oturum durumu temizlendi.";
        RaiseCanExecuteChanged();
    }

    private void ClearPendingApproval()
    {
        _pendingRequest = null;
        _pendingApprovalSnapshot = null;
        _pendingSelectedTool = "None";
        _pendingRiskLevel = SafetyRiskLevel.Medium;
        _pendingApprovalItem = null;
        OnPropertyChanged(nameof(HasPendingApproval));
    }

    private void AddItem(ConversationItem item)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            Conversation.Add(item);
            OnPropertyChanged(nameof(HasConversation));
            RaiseCanExecuteChanged();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                Conversation.Add(item);
                OnPropertyChanged(nameof(HasConversation));
                RaiseCanExecuteChanged();
            });
        }
    }

    private void RaiseCanExecuteChanged()
    {
        (SubmitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ClearSessionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private Task LogCommandSubmittedAsync(CommandRequest request) =>
        _auditLogger.LogAsync(new AuditEvent
        {
            CorrelationId = request.CorrelationId,
            EventType = "CommandSubmitted",
            CommandText = request.UserInput,
            SafetyDisposition = "Unknown",
            RiskLevel = "Unknown",
            SelectedTool = "None",
            ExecutionMode = "not-executed",
            Outcome = "Submitted",
            Message = "User submitted a command.",
            ApprovalDecision = string.Empty
        });

    private Task LogApprovalDecisionAsync(CommandRequest request, string approvalDecision, string message)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["approval_decision_source"] = "ui",
            ["approval_pending_snapshot_present"] = _pendingApprovalSnapshot is null ? "false" : "true",
            ["pending_step_approval_key_present"] = string.IsNullOrWhiteSpace(_pendingApprovalSnapshot?.PendingStepApprovalKey) ? "false" : "true",
            ["approved_step_approval_key_present"] = string.IsNullOrWhiteSpace(_pendingApprovalSnapshot?.ApprovedStepApprovalKey) ? "false" : "true"
        };

        if (_pendingApprovalSnapshot?.RuntimeState is not null)
        {
            metadata["approval_step_index"] = _pendingApprovalSnapshot.RuntimeState.CurrentStepIndex.ToString();
            metadata["runtimeId"] = _pendingApprovalSnapshot.RuntimeState.RuntimeId;
        }

        return _auditLogger.LogAsync(new AuditEvent
        {
            CorrelationId = request.CorrelationId,
            EventType = "ApprovalDecision",
            CommandText = request.UserInput,
            SafetyDisposition = SafetyDisposition.RequiresApproval.ToString(),
            RiskLevel = _pendingRiskLevel.ToString(),
            SelectedTool = _pendingSelectedTool,
            ExecutionMode = "not-executed",
            Outcome = approvalDecision,
            Message = message,
            ApprovalDecision = approvalDecision,
            Metadata = metadata
        });
    }

    private Task LogCommandCompletedAsync(CommandRequest request, CommandResult result, string approvalDecision) =>
        _auditLogger.LogAsync(new AuditEvent
        {
            CorrelationId = request.CorrelationId,
            EventType = "CommandCompleted",
            CommandText = request.UserInput,
            SafetyDisposition = result.Safety,
            RiskLevel = result.RiskLevel,
            SelectedTool = result.SelectedTool,
            ExecutionMode = "not-executed",
            Outcome = result.Status,
            Message = result.Message,
            ApprovalDecision = approvalDecision
        });
}
