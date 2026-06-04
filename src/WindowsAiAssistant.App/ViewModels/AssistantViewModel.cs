using System.Collections.ObjectModel;
using System.Windows.Input;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class AssistantViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private bool _isBusy;
    private string _commandInput = string.Empty;
    private string _statusMessage = "Frontend hazir. Yeni AI-first runtime henuz baglanmadi.";

    public AssistantViewModel(INavigationService navigation, IActiveProviderStatus providerStatus)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        ProviderStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));

        Conversation = [];
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(CommandInput));
        ClearSessionCommand = new RelayCommand(ClearSession, () => Conversation.Count > 0 || !string.IsNullOrWhiteSpace(CommandInput));
        OpenSettingsCommand = new RelayCommand(() => _navigation.NavigateToSettings());
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
    public bool HasPendingApproval => false;

    public void LoadCommand(string commandText)
    {
        CommandInput = commandText ?? string.Empty;
        StatusMessage = "Komut yuklendi. Yeni runtime baglandiginda calistirilabilir.";
    }

    public void CleanupRuntimeSessionState()
    {
        ClearSession();
    }

    private Task SubmitAsync()
    {
        var commandText = CommandInput.Trim();
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return Task.CompletedTask;
        }

        IsBusy = true;
        Conversation.Add(new ConversationItem
        {
            Kind = ConversationItemKind.UserMessage,
            Title = "Sen",
            Body = commandText
        });
        Conversation.Add(new ConversationItem
        {
            Kind = ConversationItemKind.AssistantStatus,
            Title = "Runtime bekleniyor",
            Body = "Eski backend kaldirildi. Bu istek yeni AI-first AgentLoop baglandiginda islenecek."
        });

        CommandInput = string.Empty;
        StatusMessage = "Yeni AI-first runtime henuz baglanmadi.";
        IsBusy = false;
        OnPropertyChanged(nameof(HasConversation));
        RaiseCanExecuteChanged();
        return Task.CompletedTask;
    }

    private void ClearSession()
    {
        Conversation.Clear();
        CommandInput = string.Empty;
        StatusMessage = "Frontend hazir. Yeni AI-first runtime henuz baglanmadi.";
        OnPropertyChanged(nameof(HasConversation));
        RaiseCanExecuteChanged();
    }

    private void RaiseCanExecuteChanged()
    {
        (SubmitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ClearSessionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
