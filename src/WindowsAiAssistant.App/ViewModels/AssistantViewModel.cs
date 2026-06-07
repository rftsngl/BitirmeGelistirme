using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.App.Audio;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.Runtime.Policy;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class AssistantViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly AgentLoop _agentLoop;
    private readonly ActionApprovalCoordinator _approvalCoordinator;
    private readonly AgentRunCoordinator _runCoordinator;
    private readonly VoiceApprovalService _voiceApproval;
    private readonly AudioOptions _audioOptions;
    private readonly ActionPolicy _actionPolicy;
    private CancellationTokenSource? _runCts;
    private ConversationItem? _liveActivityItem;
    private bool _isBusy;
    private string _commandInput = string.Empty;
    private string _statusMessage = "Asistan hazır. Mesajınızı gönderin.";

    public AssistantViewModel(
        INavigationService navigation,
        IActiveProviderStatus providerStatus,
        AgentLoop agentLoop,
        ActionApprovalCoordinator approvalCoordinator,
        AgentRunCoordinator runCoordinator,
        VoiceApprovalService voiceApproval,
        AudioOptions audioOptions,
        ActionPolicy actionPolicy)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        ProviderStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));
        _agentLoop = agentLoop ?? throw new ArgumentNullException(nameof(agentLoop));
        _approvalCoordinator = approvalCoordinator ?? throw new ArgumentNullException(nameof(approvalCoordinator));
        _runCoordinator = runCoordinator ?? throw new ArgumentNullException(nameof(runCoordinator));
        _voiceApproval = voiceApproval ?? throw new ArgumentNullException(nameof(voiceApproval));
        _audioOptions = audioOptions ?? throw new ArgumentNullException(nameof(audioOptions));
        _actionPolicy = actionPolicy ?? throw new ArgumentNullException(nameof(actionPolicy));

        Conversation = [];
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, CanSubmit);
        CancelRunCommand = new RelayCommand(CancelRun, () => IsBusy);
        ClearSessionCommand = new RelayCommand(ClearSession, () => Conversation.Count > 0 || !string.IsNullOrWhiteSpace(CommandInput));
        OpenSettingsCommand = new RelayCommand(() => _navigation.NavigateToSettings());

        ProviderStatus.PropertyChanged += OnProviderStatusChanged;
        _approvalCoordinator.ApprovalRequested += OnApprovalRequested;
    }

    public IActiveProviderStatus ProviderStatus { get; }
    public ObservableCollection<ConversationItem> Conversation { get; }
    public ICommand SubmitCommand { get; }
    public ICommand CancelRunCommand { get; }
    public ICommand ClearSessionCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    public event Action? ScrollToEndRequested;

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
        private set
        {
            if (SetField(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(UserFriendlyStatus));
            }
        }
    }

    public string UserFriendlyStatus
    {
        get
        {
            if (!ProviderStatus.IsReady)
            {
                return "Model ayarlanmalı";
            }

            if (IsBusy && _liveActivityItem is not null && !string.IsNullOrWhiteSpace(_liveActivityItem.LiveStatusLine))
            {
                return _liveActivityItem.LiveStatusLine;
            }

            if (IsBusy)
            {
                return "Çalışıyor…";
            }

            if (_audioOptions.GlobalHotKeyEnabled || _audioOptions.WakeWordEnabled)
            {
                return "Sesli asistan aktif";
            }

            return string.IsNullOrWhiteSpace(_statusMessage) ? "Hazır" : _statusMessage;
        }
    }

    public bool DeveloperModeEnabled => _audioOptions.DeveloperModeEnabled;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(UserFriendlyStatus));
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;
    public bool HasConversation => Conversation.Count > 0;

    private bool CanSubmit() =>
        !IsBusy && !string.IsNullOrWhiteSpace(CommandInput) && ProviderStatus.IsReady;

    private void OnProviderStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IActiveProviderStatus.IsReady) or nameof(IActiveProviderStatus.ReadyBadge) or "")
        {
            OnPropertyChanged(nameof(UserFriendlyStatus));
            RaiseCanExecuteChanged();
        }
    }

    public void LoadCommand(string commandText)
    {
        CommandInput = commandText ?? string.Empty;
        StatusMessage = "Komut yüklendi. 'Gönder' ile asistan çalıştırılır.";
    }

    public void CleanupRuntimeSessionState()
    {
        CancelRun();
        ClearSession();
    }

    private void CancelRun()
    {
        if (_runCts is null)
        {
            return;
        }

        _runCts.Cancel();
        _voiceApproval.Cancel();
        StatusMessage = "İptal istendi...";
    }

    private async Task SubmitAsync()
    {
        var commandText = CommandInput.Trim();
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return;
        }

        if (!ProviderStatus.IsReady)
        {
            Conversation.Add(new ConversationItem
            {
                Kind = ConversationItemKind.ErrorCard,
                Title = "Sağlayıcı hazır değil",
                Body = ProviderStatus.ReadyReason,
                PrimaryCommand = OpenSettingsCommand,
                PrimaryCommandLabel = "Ayarlar"
            });
            OnPropertyChanged(nameof(HasConversation));
            StatusMessage = ProviderStatus.ReadyReason;
            return;
        }

        if (!_runCoordinator.TryEnterRun())
        {
            StatusMessage = "Başka bir asistan oturumu çalışıyor. Lütfen bekleyin.";
            return;
        }

        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();

        IsBusy = true;
        StatusMessage = "Başlıyor…";

        Conversation.Add(new ConversationItem
        {
            DeveloperModeEnabled = DeveloperModeEnabled,
            Kind = ConversationItemKind.UserMessage,
            Title = "Sen",
            Body = commandText
        });

        _liveActivityItem = CreateLiveActivityItem();
        Conversation.Add(_liveActivityItem);

        CommandInput = string.Empty;
        OnPropertyChanged(nameof(HasConversation));
        RaiseCanExecuteChanged();
        RequestScrollToEnd();

        var progress = new Progress<AgentStepProgress>(UpdateProgress);
        UpdateProgress(new AgentStepProgress
        {
            StepIndex = 0,
            MaxSteps = 1,
            Phase = "basladi",
            Detail = commandText
        });

        try
        {
            var result = await _agentLoop
                .RunAsync(commandText, _runCts.Token, progress, "chat")
                .ConfigureAwait(true);

            if (!result.Success)
            {
                FinalizeLiveActivity(success: false);
                var isProviderError = result.ErrorKind == AgentErrorKind.Provider;
                Conversation.Add(new ConversationItem
                {
                    DeveloperModeEnabled = DeveloperModeEnabled,
                    Kind = ConversationItemKind.ErrorCard,
                    Title = ResolveErrorTitle(result.ErrorKind),
                    Body = result.ErrorMessage ?? "İstek tamamlanamadı.",
                    PrimaryCommand = isProviderError ? OpenSettingsCommand : null,
                    PrimaryCommandLabel = isProviderError ? "Ayarlar" : string.Empty
                });
                StatusMessage = result.ErrorMessage ?? "İstek tamamlanamadı.";
                RequestScrollToEnd();
                return;
            }

            FinalizeLiveActivity(success: true);

            var physicalSteps = result.Session.Steps
                .Count(step => step.ParsedDecision?.Action is not ("respond" or "ask_user" or "stop"));
            var hasRespondStep = result.Session.Steps
                .Any(step => step.ParsedDecision?.Action is "respond" or "ask_user");
            string stepInfoText;
            if (hasRespondStep && physicalSteps > 0)
            {
                stepInfoText = $"{physicalSteps} fiziksel adım + yanıt";
            }
            else if (physicalSteps == 0 && hasRespondStep)
            {
                stepInfoText = "Doğrudan yanıt (fiziksel adım yok)";
            }
            else
            {
                stepInfoText = $"{physicalSteps} adım";
            }

            var badge = result.ReachedMaxSteps ? $"Adım limiti ({result.Session.Steps.Count}/{result.Session.Steps.Count})" : "Tamamlandı";
            var logPath = result.LogFilePath ?? string.Empty;
            var showLogAction = DeveloperModeEnabled && !string.IsNullOrWhiteSpace(logPath);
            Conversation.Add(new ConversationItem
            {
                DeveloperModeEnabled = DeveloperModeEnabled,
                Kind = ConversationItemKind.ResultCard,
                Title = "Asistan",
                Body = result.AssistantMessage ?? string.Empty,
                StatusBadge = badge,
                StepInfo = stepInfoText,
                ObservationSummary = result.ObservationSummary ?? "(gozlem yok)",
                LogPath = logPath,
                PrimaryCommand = showLogAction ? new RelayCommand(() => OpenLog(logPath)) : null,
                PrimaryCommandLabel = showLogAction ? "Logu Aç" : string.Empty
            });
            StatusMessage = result.ReachedMaxSteps
                ? DeveloperModeEnabled
                    ? $"Adım limitine ulaşıldı ({result.Session.Steps.Count} adım, {physicalSteps} fiziksel). Log: {result.LogFilePath}"
                    : $"Adım limitine ulaşıldı ({result.Session.Steps.Count} adım, {physicalSteps} fiziksel)."
                : DeveloperModeEnabled
                    ? $"Tamamlandı — {stepInfoText}. Log: {result.LogFilePath}"
                    : "Tamamlandı.";
            RequestScrollToEnd();
        }
        catch (OperationCanceledException)
        {
            FinalizeLiveActivity(success: false);
            Conversation.Add(new ConversationItem
            {
                DeveloperModeEnabled = DeveloperModeEnabled,
                Kind = ConversationItemKind.ErrorCard,
                Title = "İptal edildi",
                Body = "Asistan çalışması durduruldu."
            });
            StatusMessage = "Çalışma iptal edildi.";
            RequestScrollToEnd();
        }
        catch (Exception ex)
        {
            FinalizeLiveActivity(success: false);
            Conversation.Add(new ConversationItem
            {
                DeveloperModeEnabled = DeveloperModeEnabled,
                Kind = ConversationItemKind.ErrorCard,
                Title = "Beklenmeyen hata",
                Body = ex.Message
            });
            StatusMessage = "İstek tamamlanamadı.";
            RequestScrollToEnd();
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            IsBusy = false;
            _runCoordinator.ExitRun();
            RaiseCanExecuteChanged();
        }
    }

    private void UpdateProgress(AgentStepProgress progress)
    {
        var stepNumber = Math.Min(progress.StepIndex + 1, progress.MaxSteps);
        var (label, detail) = ActivityPhaseFormatter.Format(progress);

        if (_liveActivityItem is not null)
        {
            _liveActivityItem.CurrentStepIndex = stepNumber;
            _liveActivityItem.MaxSteps = progress.MaxSteps;
            _liveActivityItem.LiveStatusLine = label;
            _liveActivityItem.LiveDetailLine = detail;
            AppendTimelineEntry(_liveActivityItem, label, detail, progress.Phase);
            OnPropertyChanged(nameof(UserFriendlyStatus));
        }

        StatusMessage = string.IsNullOrWhiteSpace(detail)
            ? $"{label} — adım {stepNumber}/{progress.MaxSteps}"
            : $"{label} — {detail}";
        RequestScrollToEnd();
    }

    private static ConversationItem CreateLiveActivityItem() =>
        new()
        {
            Kind = ConversationItemKind.LiveActivity,
            Title = "Asistan",
            Body = string.Empty,
            IsActive = true
        };

    private static void AppendTimelineEntry(ConversationItem item, string label, string detail, string phase)
    {
        foreach (var entry in item.Timeline.Where(entry => entry.IsActive))
        {
            entry.State = phase.Contains("fail", StringComparison.OrdinalIgnoreCase)
                ? TimelineEntryState.Failed
                : TimelineEntryState.Completed;
        }

        var duplicate = item.Timeline.LastOrDefault(entry =>
            entry.Label.Equals(label, StringComparison.Ordinal) &&
            entry.State == TimelineEntryState.Active);
        if (duplicate is not null)
        {
            duplicate.Detail = detail;
            return;
        }

        item.Timeline.Add(new ActivityTimelineEntry
        {
            Label = label,
            Detail = detail,
            State = TimelineEntryState.Active
        });
        item.NotifyTimelineChanged();
    }

    private void FinalizeLiveActivity(bool success)
    {
        if (_liveActivityItem is null)
        {
            return;
        }

        foreach (var entry in _liveActivityItem.Timeline.Where(entry => entry.IsActive))
        {
            entry.State = success ? TimelineEntryState.Completed : TimelineEntryState.Failed;
        }

        _liveActivityItem.IsActive = false;
        Conversation.Remove(_liveActivityItem);
        _liveActivityItem = null;
        OnPropertyChanged(nameof(UserFriendlyStatus));
    }

    private void RequestScrollToEnd() => ScrollToEndRequested?.Invoke();

    private void OnApprovalRequested(object? sender, PendingApprovalRequest request)
    {
        ConversationItem? item = null;
        var body = $"{request.GateDecision.Reason}{Environment.NewLine}{request.GateDecision.Summary}";
        if (_audioOptions.VoiceApprovalEnabled)
        {
            body += $"{Environment.NewLine}(\"Onayla\" / \"Reddet\" diyebilir veya butonu kullanabilirsiniz.)";
        }

        item = new ConversationItem
        {
            DeveloperModeEnabled = DeveloperModeEnabled,
            Kind = ConversationItemKind.PendingApproval,
            Title = "Bu işlem onay gerektiriyor",
            Body = body,
            SelectedTool = ActionDisplayHelper.ToUserFriendlyLabel(request.Action.Action),
            RiskLevel = ActionDisplayHelper.ToRiskLabel(request.GateDecision.Risk.ToString()),
            StatusBadge = "BEKLİYOR",
            ShowSessionRemember = _actionPolicy.AllowSessionRemember || DeveloperModeEnabled,
            PrimaryCommandLabel = "Onayla",
            SecondaryCommandLabel = "Reddet",
            PrimaryCommand = new RelayCommand(() => ResolveApproval(request, item!, approved: true)),
            SecondaryCommand = new RelayCommand(() => ResolveApproval(request, item!, approved: false))
        };

        Conversation.Add(item);
        OnPropertyChanged(nameof(HasConversation));
        StatusMessage = $"Onay bekleniyor: {request.GateDecision.Summary}";
        UpdateProgress(new AgentStepProgress
        {
            StepIndex = _liveActivityItem?.CurrentStepIndex > 0 ? _liveActivityItem.CurrentStepIndex - 1 : 0,
            MaxSteps = _liveActivityItem?.MaxSteps ?? 1,
            Phase = "onay",
            Detail = request.GateDecision.Summary
        });
        RequestScrollToEnd();

        _ = _voiceApproval.ListenForDecisionAsync(
            approved =>
            {
                ResolveApproval(request, item!, approved);
                return Task.CompletedTask;
            },
            _runCts?.Token ?? CancellationToken.None);
    }

    private void ResolveApproval(PendingApprovalRequest request, ConversationItem item, bool approved)
    {
        if (item.IsApprovalResolved)
        {
            return;
        }

        _voiceApproval.Cancel();
        item.IsApprovalResolved = true;
        item.StatusBadge = approved ? "ONAYLANDI" : "REDDEDİLDİ";
        item.ResolutionNote = approved ? "Devam ediliyor..." : "İşlem reddedildi.";
        if (approved)
        {
            request.Approve(rememberForSession: item.AllowForSession);
        }
        else
        {
            request.Deny();
        }
    }

    private static string ResolveErrorTitle(AgentErrorKind errorKind) => errorKind switch
    {
        AgentErrorKind.Provider => "Sağlayıcı / bağlantı hatası",
        AgentErrorKind.Decision => "Karar çözümlenemedi",
        AgentErrorKind.Action => "Eylem başarısız",
        _ => "Asistan hatası"
    };

    private static void OpenLog(string logPath)
    {
        try
        {
            if (File.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{logPath}\"")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                var directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
                }
            }
        }
        catch
        {
            // Best-effort; log path remains visible/selectable in the card.
        }
    }

    private void ClearSession()
    {
        FinalizeLiveActivity(success: false);
        Conversation.Clear();
        CommandInput = string.Empty;
        StatusMessage = "Asistan hazır. Mesajınızı gönderin.";
        OnPropertyChanged(nameof(HasConversation));
        RaiseCanExecuteChanged();
    }

    private void RaiseCanExecuteChanged()
    {
        (SubmitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelRunCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearSessionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
