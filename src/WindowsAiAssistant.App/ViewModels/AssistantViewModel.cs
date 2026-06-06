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

namespace WindowsAiAssistant.App.ViewModels;

public sealed class AssistantViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly AgentLoop _agentLoop;
    private readonly ActionApprovalCoordinator _approvalCoordinator;
    private readonly AgentRunCoordinator _runCoordinator;
    private readonly VoiceApprovalService _voiceApproval;
    private readonly AudioOptions _audioOptions;
    private CancellationTokenSource? _runCts;
    private bool _isBusy;
    private string _commandInput = string.Empty;
    private string _statusMessage = "Agent hazir. Mesajinizi gonderin.";

    public AssistantViewModel(
        INavigationService navigation,
        IActiveProviderStatus providerStatus,
        AgentLoop agentLoop,
        ActionApprovalCoordinator approvalCoordinator,
        AgentRunCoordinator runCoordinator,
        VoiceApprovalService voiceApproval,
        AudioOptions audioOptions)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        ProviderStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));
        _agentLoop = agentLoop ?? throw new ArgumentNullException(nameof(agentLoop));
        _approvalCoordinator = approvalCoordinator ?? throw new ArgumentNullException(nameof(approvalCoordinator));
        _runCoordinator = runCoordinator ?? throw new ArgumentNullException(nameof(runCoordinator));
        _voiceApproval = voiceApproval ?? throw new ArgumentNullException(nameof(voiceApproval));
        _audioOptions = audioOptions ?? throw new ArgumentNullException(nameof(audioOptions));

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

    private bool CanSubmit() =>
        !IsBusy && !string.IsNullOrWhiteSpace(CommandInput) && ProviderStatus.IsReady;

    private void OnProviderStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IActiveProviderStatus.IsReady) or nameof(IActiveProviderStatus.ReadyBadge) or "")
        {
            RaiseCanExecuteChanged();
        }
    }

    public void LoadCommand(string commandText)
    {
        CommandInput = commandText ?? string.Empty;
        StatusMessage = "Komut yuklendi. Gonder ile agent calistirilir.";
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
        StatusMessage = "Iptal istendi...";
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
                Title = "Saglayici hazir degil",
                Body = ProviderStatus.ReadyReason,
                PrimaryCommand = OpenSettingsCommand,
                PrimaryCommandLabel = "Saglayici Ayarlari"
            });
            OnPropertyChanged(nameof(HasConversation));
            StatusMessage = ProviderStatus.ReadyReason;
            return;
        }

        if (!_runCoordinator.TryEnterRun())
        {
            StatusMessage = "Baska bir agent oturumu (sesli popup) calisiyor. Lutfen bekleyin.";
            return;
        }

        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();

        IsBusy = true;
        StatusMessage = "Agent calisiyor (adim 1)...";
        Conversation.Add(new ConversationItem
        {
            Kind = ConversationItemKind.UserMessage,
            Title = "Sen",
            Body = commandText
        });

        CommandInput = string.Empty;
        OnPropertyChanged(nameof(HasConversation));
        RaiseCanExecuteChanged();

        var progress = new Progress<AgentStepProgress>(UpdateProgress);

        try
        {
            var result = await _agentLoop
                .RunAsync(commandText, _runCts.Token, progress, "chat")
                .ConfigureAwait(true);

            if (!result.Success)
            {
                var isProviderError = result.ErrorKind == AgentErrorKind.Provider;
                Conversation.Add(new ConversationItem
                {
                    Kind = ConversationItemKind.ErrorCard,
                    Title = ResolveErrorTitle(result.ErrorKind),
                    Body = result.ErrorMessage ?? "Istek tamamlanamadi.",
                    PrimaryCommand = isProviderError ? OpenSettingsCommand : null,
                    PrimaryCommandLabel = isProviderError ? "Saglayici Ayarlari" : string.Empty
                });
                StatusMessage = result.ErrorMessage ?? "Istek tamamlanamadi.";
                return;
            }

            foreach (var step in result.Session.Steps)
            {
                var actionName = step.ParsedDecision?.Action ?? "step";
                if (step.ParsedDecision?.Action is "respond")
                {
                    continue;
                }

                if (step.ActionResult is { Success: true } actionResult)
                {
                    Conversation.Add(new ConversationItem
                    {
                        Kind = ConversationItemKind.AssistantStatus,
                        Title = $"Adim {step.Index + 1}: {actionName}",
                        Body = actionResult.Message
                    });
                }
            }

            var physicalSteps = result.Session.Steps
                .Count(step => step.ParsedDecision?.Action is not ("respond" or "ask_user" or "stop"));
            var hasRespondStep = result.Session.Steps
                .Any(step => step.ParsedDecision?.Action is "respond" or "ask_user");
            string stepInfoText;
            if (hasRespondStep && physicalSteps > 0)
            {
                stepInfoText = $"{physicalSteps} fiziksel adim + yanit";
            }
            else if (physicalSteps == 0 && hasRespondStep)
            {
                stepInfoText = "Dogrudan yanit (fiziksel adim yok)";
            }
            else
            {
                stepInfoText = $"{physicalSteps} adim";
            }

            var badge = result.ReachedMaxSteps ? $"Adim limiti ({result.Session.Steps.Count}/{result.Session.Steps.Count})" : "Tamamlandi";
            var logPath = result.LogFilePath ?? string.Empty;
            Conversation.Add(new ConversationItem
            {
                Kind = ConversationItemKind.ResultCard,
                Title = "Asistan",
                Body = result.AssistantMessage ?? string.Empty,
                StatusBadge = badge,
                StepInfo = stepInfoText,
                ObservationSummary = result.ObservationSummary ?? "(gozlem yok)",
                LogPath = logPath,
                PrimaryCommand = string.IsNullOrWhiteSpace(logPath) ? null : new RelayCommand(() => OpenLog(logPath)),
                PrimaryCommandLabel = string.IsNullOrWhiteSpace(logPath) ? string.Empty : "Logu Ac"
            });
            StatusMessage = result.ReachedMaxSteps
                ? $"Adim limitine ulasildi ({result.Session.Steps.Count} adim, {physicalSteps} fiziksel). Log: {result.LogFilePath}"
                : $"Tamamlandi — {stepInfoText}. Log: {result.LogFilePath}";
        }
        catch (OperationCanceledException)
        {
            Conversation.Add(new ConversationItem
            {
                Kind = ConversationItemKind.ErrorCard,
                Title = "Iptal",
                Body = "Agent calismasi kullanici tarafindan durduruldu."
            });
            StatusMessage = "Calisma iptal edildi.";
        }
        catch (Exception ex)
        {
            Conversation.Add(new ConversationItem
            {
                Kind = ConversationItemKind.ErrorCard,
                Title = "Beklenmeyen hata",
                Body = ex.Message
            });
            StatusMessage = "Istek tamamlanamadi.";
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
        StatusMessage = string.IsNullOrWhiteSpace(progress.Detail)
            ? $"Adim {stepNumber}/{progress.MaxSteps}: {progress.Phase}"
            : $"Adim {stepNumber}/{progress.MaxSteps}: {progress.Phase} — {progress.Detail}";
    }

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
            Kind = ConversationItemKind.PendingApproval,
            Title = "Islem onayi gerekli",
            Body = body,
            SelectedTool = request.Action.Action,
            RiskLevel = ActionRiskDisplay.ToUiLabel(request.GateDecision.Risk),
            StatusBadge = "BEKLIYOR",
            PrimaryCommandLabel = "Onayla",
            SecondaryCommandLabel = "Reddet",
            PrimaryCommand = new RelayCommand(() => ResolveApproval(request, item!, approved: true)),
            SecondaryCommand = new RelayCommand(() => ResolveApproval(request, item!, approved: false))
        };

        Conversation.Add(item);
        OnPropertyChanged(nameof(HasConversation));
        StatusMessage = $"Onay bekleniyor: {request.GateDecision.Summary}";

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
        item.StatusBadge = approved ? "ONAYLANDI" : "REDDEDILDI";
        item.ResolutionNote = approved ? "Devam ediliyor..." : "Islem reddedildi.";
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
        AgentErrorKind.Provider => "Saglayici / baglanti hatasi",
        AgentErrorKind.Decision => "Karar cozumlenemedi",
        AgentErrorKind.Action => "Eylem basarisiz",
        _ => "Agent hatasi"
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
        Conversation.Clear();
        CommandInput = string.Empty;
        StatusMessage = "Agent hazir. Mesajinizi gonderin.";
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
