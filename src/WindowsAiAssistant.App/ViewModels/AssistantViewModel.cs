using System.Collections.ObjectModel;
using System.Windows.Input;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class AssistantViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly AgentLoop _agentLoop;
    private CancellationTokenSource? _runCts;
    private bool _isBusy;
    private string _commandInput = string.Empty;
    private string _statusMessage = "Agent hazir. Mesajinizi gonderin.";

    public AssistantViewModel(
        INavigationService navigation,
        IActiveProviderStatus providerStatus,
        AgentLoop agentLoop)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        ProviderStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));
        _agentLoop = agentLoop ?? throw new ArgumentNullException(nameof(agentLoop));

        Conversation = [];
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(CommandInput));
        CancelRunCommand = new RelayCommand(CancelRun, () => IsBusy);
        ClearSessionCommand = new RelayCommand(ClearSession, () => Conversation.Count > 0 || !string.IsNullOrWhiteSpace(CommandInput));
        OpenSettingsCommand = new RelayCommand(() => _navigation.NavigateToSettings());
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
    public bool HasPendingApproval => false;

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
        StatusMessage = "Iptal istendi...";
    }

    private async Task SubmitAsync()
    {
        var commandText = CommandInput.Trim();
        if (string.IsNullOrWhiteSpace(commandText))
        {
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
                .RunAsync(commandText, _runCts.Token, progress)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                Conversation.Add(new ConversationItem
                {
                    Kind = ConversationItemKind.ErrorCard,
                    Title = "Agent hatasi",
                    Body = result.ErrorMessage ?? "Istek tamamlanamadi."
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

            var badge = result.ReachedMaxSteps ? "Adim limiti" : "Tamamlandi";
            Conversation.Add(new ConversationItem
            {
                Kind = ConversationItemKind.ResultCard,
                Title = "Asistan",
                Body = result.AssistantMessage ?? string.Empty,
                StatusBadge = badge,
                StepInfo = $"{result.Session.Steps.Count} adim calistirildi",
                ObservationSummary = result.ObservationSummary ?? "(gozlem yok)",
                LogPath = result.LogFilePath ?? string.Empty
            });
            StatusMessage = result.ReachedMaxSteps
                ? $"Adim limitine ulasildi ({result.Session.Steps.Count} adim). Log: {result.LogFilePath}"
                : $"Tamamlandi — {result.Session.Steps.Count} adim. Log: {result.LogFilePath}";
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
