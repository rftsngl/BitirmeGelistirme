using System.Collections.ObjectModel;
using System.Windows.Input;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class HistoryViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly AssistantViewModel _assistantViewModel;
    private HistoryViewItem? _selectedItem;
    private bool _isRefreshing;
    private string _statusMessage = "Eski runtime gecmisi kaldirildi.";

    public HistoryViewModel(INavigationService navigation, AssistantViewModel assistantViewModel)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _assistantViewModel = assistantViewModel ?? throw new ArgumentNullException(nameof(assistantViewModel));

        Items = [];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
        LoadToAssistantCommand = new RelayCommand(LoadToAssistant, () => SelectedItem is not null);
    }

    public ObservableCollection<HistoryViewItem> Items { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LoadToAssistantCommand { get; }

    public HistoryViewItem? SelectedItem
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

    private Task RefreshAsync()
    {
        IsRefreshing = true;
        Items.Clear();
        SelectedItem = null;
        StatusMessage = "Yeni AI-first runtime gecmis servisi henuz baglanmadi.";
        OnPropertyChanged(nameof(IsEmpty));
        IsRefreshing = false;
        return Task.CompletedTask;
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
