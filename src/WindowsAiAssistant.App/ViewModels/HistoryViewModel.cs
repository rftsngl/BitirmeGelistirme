using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Input;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.Runtime.Logging;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class HistoryViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly AssistantViewModel _assistantViewModel;
    private readonly RunLogger _runLogger;
    private readonly RunLogReader _runLogReader;
    private readonly List<HistoryViewItem> _allItems = [];
    private HistoryViewItem? _selectedItem;
    private HistoryStepItem? _selectedStep;
    private bool _isRefreshing;
    private string _filterText = string.Empty;
    private string _statusMessage = "Calistirilan run kayitlari logs/runs altinda listelenir.";

    public HistoryViewModel(
        INavigationService navigation,
        AssistantViewModel assistantViewModel,
        RunLogger runLogger,
        RunLogReader runLogReader)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _assistantViewModel = assistantViewModel ?? throw new ArgumentNullException(nameof(assistantViewModel));
        _runLogger = runLogger ?? throw new ArgumentNullException(nameof(runLogger));
        _runLogReader = runLogReader ?? throw new ArgumentNullException(nameof(runLogReader));

        Items = [];
        Steps = [];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
        LoadToAssistantCommand = new RelayCommand(LoadToAssistant, () => SelectedItem is not null);
        OpenLogFileCommand = new RelayCommand(OpenLogFile, () => SelectedItem is not null);
        OpenScreenshotCommand = new RelayCommand(OpenScreenshot, () => SelectedStep?.HasScreenshot == true);
        CopyUiTreeCommand = new RelayCommand(CopyUiTree, () => SelectedStep?.UiTreeRawJson.Length > 0);
    }

    public ObservableCollection<HistoryViewItem> Items { get; }
    public ObservableCollection<HistoryStepItem> Steps { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LoadToAssistantCommand { get; }
    public ICommand OpenLogFileCommand { get; }
    public ICommand OpenScreenshotCommand { get; }
    public ICommand CopyUiTreeCommand { get; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }
    public HistoryViewItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                LoadStepsForSelection();
                (LoadToAssistantCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (OpenLogFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public HistoryStepItem? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (SetField(ref _selectedStep, value))
            {
                OnPropertyChanged(nameof(HasStepSelection));
                (OpenScreenshotCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CopyUiTreeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelection => SelectedItem is not null;
    public bool HasStepSelection => SelectedStep is not null;
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

    public Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            var selectedRunId = SelectedItem?.RunId;
            _allItems.Clear();
            Items.Clear();
            Steps.Clear();
            SelectedItem = null;
            SelectedStep = null;

            var runs = _runLogReader.ListRuns(_runLogger.LogsDirectoryFullPath);
            foreach (var run in runs)
            {
                _allItems.Add(ToHistoryItem(run));
            }

            ApplyFilter();

            if (!string.IsNullOrWhiteSpace(selectedRunId))
            {
                SelectedItem = Items.FirstOrDefault(item => item.RunId == selectedRunId) ?? Items.FirstOrDefault();
            }
            else if (Items.Count > 0)
            {
                SelectedItem = Items[0];
            }

            StatusMessage = _allItems.Count == 0
                ? $"Kayit bulunamadi. Dizin: {_runLogger.LogsDirectoryFullPath}"
                : $"{Items.Count} / {_allItems.Count} run listelendi. Dizin: {_runLogger.LogsDirectoryFullPath}";
        }
        finally
        {
            OnPropertyChanged(nameof(IsEmpty));
            IsRefreshing = false;
        }

        return Task.CompletedTask;
    }

    private void ApplyFilter()
    {
        Items.Clear();
        var query = _filterText.Trim();
        var source = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(item =>
                item.CommandText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.FinalStatus.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.TriggerSource.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var item in source)
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
        StatusMessage = _allItems.Count == 0
            ? $"Kayit yok. Dizin: {_runLogger.LogsDirectoryFullPath}"
            : string.IsNullOrWhiteSpace(query)
                ? $"{_allItems.Count} run listelendi."
                : $"{Items.Count} / {_allItems.Count} sonuc gosteriliyor (filtre: \"{query}\").";
    }

    private void LoadStepsForSelection()
    {
        Steps.Clear();
        SelectedStep = null;

        if (SelectedItem is null || string.IsNullOrWhiteSpace(SelectedItem.LogFilePath))
        {
            return;
        }

        var parsed = _runLogReader.TryParseFile(SelectedItem.LogFilePath);
        if (parsed is null)
        {
            return;
        }

        foreach (var step in parsed.Steps)
        {
            Steps.Add(new HistoryStepItem
            {
                StepIndex = step.StepIndex,
                Title = $"Adim {step.StepIndex + 1}: {step.ActionName}",
                Subtitle = step.ResultMessage,
                Success = step.ActionSuccess,
                GateOutcome = step.GateOutcome,
                GateRisk = step.GateRisk,
                GateUserApproved = step.GateUserApproved,
                ObservationSummary = FormatJsonForDisplay(step.ObservationSummaryJson),
                WindowsSummary = FormatJsonForDisplay(step.WindowsSummaryJson),
                UiTreeSummary = FormatUiTreeForDisplay(step.UiTreeSummaryJson),
                UiTreeRawJson = step.UiTreeSummaryJson ?? string.Empty,
                ParsedDecision = FormatJsonForDisplay(step.ParsedDecisionJson),
                GateDecision = FormatJsonForDisplay(step.GateDecisionJson),
                ActionResult = FormatJsonForDisplay(step.ActionResultJson),
                LlmRawOutput = FormatLongText(step.LlmRawOutput),
                ScreenshotPath = step.ScreenshotPath
            });
        }

        SelectedStep = Steps.FirstOrDefault();
    }

    private static HistoryViewItem ToHistoryItem(ParsedRunLog run) =>
        new()
        {
            RunId = run.RunId,
            LogFilePath = run.LogFilePath,
            CommandText = run.UserGoal,
            FinalStatus = run.FinalStatus,
            TimestampUtc = run.EndedAt,
            SelectedTool = run.LastAction,
            TriggerSource = run.TriggerSource,
            StepCount = run.Steps.Count,
            LastResult = run.LastResult
        };

    private static string FormatJsonForDisplay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "(yok)";
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static string FormatLongText(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? "(yok)" : raw.Trim();

    private static string FormatUiTreeForDisplay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "(yok)";
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var formatted = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            var truncated = raw.Contains("\"truncated\":true", StringComparison.Ordinal);
            var hint = truncated ? $"\n[KESILMIS — {raw.Length} karakter. Tam metni kopyalamak icin 'UI Agaci Kopyala' dugmesini kullanin.]" : $"\n[{raw.Length} karakter]";
            return formatted + hint;
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private void CopyUiTree()
    {
        var raw = SelectedStep?.UiTreeRawJson;
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(raw);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
        catch
        {
            // Best-effort clipboard write.
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

    private void OpenLogFile()
    {
        if (SelectedItem is null || string.IsNullOrWhiteSpace(SelectedItem.LogFilePath))
        {
            return;
        }

        TryRevealInExplorer(SelectedItem.LogFilePath);
    }

    private void OpenScreenshot()
    {
        if (SelectedStep?.ScreenshotPath is null)
        {
            return;
        }

        TryRevealInExplorer(SelectedStep.ScreenshotPath);
    }

    private static void TryRevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // best effort
        }
    }
}
