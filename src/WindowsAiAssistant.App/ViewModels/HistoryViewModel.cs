using System.Collections.ObjectModel;
using System.Windows.Input;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class HistoryViewModel : ObservableObject
{
    private const int RefreshLimit = 25;
    private readonly ICommandHistoryService _commandHistoryService;
    private readonly INavigationService _navigation;
    private readonly AssistantViewModel _assistantViewModel;

    private RecentCommandSummary? _selectedItem;
    private bool _isRefreshing;
    private string _statusMessage = "Geçmişi görüntülemek için 'Yenile'ye basın.";

    public HistoryViewModel(
        ICommandHistoryService commandHistoryService,
        INavigationService navigation,
        AssistantViewModel assistantViewModel)
    {
        _commandHistoryService = commandHistoryService ?? throw new ArgumentNullException(nameof(commandHistoryService));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _assistantViewModel = assistantViewModel ?? throw new ArgumentNullException(nameof(assistantViewModel));

        Items = [];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
        LoadToAssistantCommand = new RelayCommand(LoadToAssistant, () => SelectedItem is not null);
    }

    public ObservableCollection<RecentCommandSummary> Items { get; }

    public ICommand RefreshCommand { get; }
    public ICommand LoadToAssistantCommand { get; }

    public RecentCommandSummary? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                (LoadToAssistantCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelection => SelectedItem is not null;

    public bool IsEmpty => Items.Count == 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetField(ref _isRefreshing, value))
            {
                (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            StatusMessage = "Yenileniyor…";
            var items = await _commandHistoryService.GetRecentAsync(RefreshLimit).ConfigureAwait(true);
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(nameof(IsEmpty));
            StatusMessage = Items.Count == 0
                ? "Kayıt bulunamadı."
                : $"{Items.Count} kayıt yüklendi.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void LoadToAssistant()
    {
        if (SelectedItem is null)
        {
            return;
        }

        _assistantViewModel.LoadCommand(SelectedItem.CommandText);
        _navigation.NavigateToAssistant();
    }
}
