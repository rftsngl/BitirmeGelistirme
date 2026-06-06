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
    private readonly Configuration.AudioOptions _audioOptions;
    private readonly List<HistoryViewItem> _allItems = [];
    private HistoryViewItem? _selectedItem;
    private HistoryStepItem? _selectedStep;
    private bool _isRefreshing;
    private string _filterText = string.Empty;
    private string _triggerFilter = string.Empty;
    private string _statusFilter = string.Empty;
    private string _statusMessage = "Geçmiş kayıtları burada listelenir.";

    public HistoryViewModel(
        INavigationService navigation,
        AssistantViewModel assistantViewModel,
        RunLogger runLogger,
        RunLogReader runLogReader,
        Configuration.AudioOptions audioOptions)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _assistantViewModel = assistantViewModel ?? throw new ArgumentNullException(nameof(assistantViewModel));
        _runLogger = runLogger ?? throw new ArgumentNullException(nameof(runLogger));
        _runLogReader = runLogReader ?? throw new ArgumentNullException(nameof(runLogReader));
        _audioOptions = audioOptions ?? throw new ArgumentNullException(nameof(audioOptions));

        StatusMessage = "Geçmiş kayıtları burada listelenir.";
        Items = [];
        Steps = [];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
        LoadToAssistantCommand = new RelayCommand(LoadToAssistant, () => SelectedItem is not null);
        OpenLogFileCommand = new RelayCommand(OpenLogFile, () => SelectedItem is not null);
        OpenScreenshotCommand = new RelayCommand(OpenScreenshot, () => SelectedStep?.HasScreenshot == true);
        CopyUiTreeCommand = new RelayCommand(CopyUiTree, () => SelectedStep?.UiTreeRawJson.Length > 0);
        CopyCommandCommand = new RelayCommand(CopyCommand, () => SelectedItem is not null);
        CopyResultCommand = new RelayCommand(CopyResult, () => SelectedItem is not null && !string.IsNullOrWhiteSpace(SelectedItem.LastResult));
        ClearFiltersCommand = new RelayCommand(ClearFilters, () => HasActiveFilters);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
    }

    public ObservableCollection<HistoryViewItem> Items { get; }
    public ObservableCollection<HistoryStepItem> Steps { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LoadToAssistantCommand { get; }
    public ICommand OpenLogFileCommand { get; }
    public ICommand OpenScreenshotCommand { get; }
    public ICommand CopyUiTreeCommand { get; }
    public ICommand CopyCommandCommand { get; }
    public ICommand CopyResultCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }

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

    public IReadOnlyList<HistoryFilterOption> TriggerFilterOptions { get; } =
    [
        new("", "Tüm kaynaklar"),
        new("chat", "Sohbet"),
        new("voice_overlay", "Sesli asistan"),
        new("hotkey", "Kısayol")
    ];

    public IReadOnlyList<HistoryFilterOption> StatusFilterOptions { get; } =
    [
        new("", "Tüm durumlar"),
        new("ok", "Başarılı"),
        new("fail", "Başarısız"),
        new("gate", "Onay / engel"),
        new("limit", "Adım limiti")
    ];

    public string TriggerFilter
    {
        get => _triggerFilter;
        set
        {
            if (SetField(ref _triggerFilter, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetField(ref _statusFilter, value ?? string.Empty))
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
                (CopyCommandCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CopyResultCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(FilterText) ||
        !string.IsNullOrWhiteSpace(TriggerFilter) ||
        !string.IsNullOrWhiteSpace(StatusFilter);

    public int TotalRunCount => _allItems.Count;
    public int FilteredRunCount => Items.Count;
    public int SuccessfulRunCount => _allItems.Count(item => item.IsSuccessful);
    public int ProblemRunCount => _allItems.Count(item => item.StatusCategory is "fail" or "gate" or "limit");
    public bool CanClearHistory => _allItems.Count > 0;

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

    public bool DeveloperModeEnabled => _audioOptions.DeveloperModeEnabled;

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
            UpdateSummaryCounts();

            if (!string.IsNullOrWhiteSpace(selectedRunId))
            {
                SelectedItem = Items.FirstOrDefault(item => item.RunId == selectedRunId) ?? Items.FirstOrDefault();
            }
            else if (Items.Count > 0)
            {
                SelectedItem = Items[0];
            }

            StatusMessage = BuildStatusMessage();
        }
        finally
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanClearHistory));
            IsRefreshing = false;
        }

        return Task.CompletedTask;
    }

    private void ApplyFilter()
    {
        Items.Clear();
        var query = _filterText.Trim();
        IEnumerable<HistoryViewItem> source = _allItems;

        if (!string.IsNullOrWhiteSpace(_triggerFilter))
        {
            source = source.Where(item =>
                item.TriggerSource.Equals(_triggerFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_statusFilter))
        {
            source = source.Where(item =>
                item.StatusCategory.Equals(_statusFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(item =>
                item.CommandText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.FinalStatus.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.TriggerSource.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.LastResult.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in source)
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
        UpdateSummaryCounts();
        StatusMessage = BuildStatusMessage();
        (ClearFiltersCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void UpdateSummaryCounts()
    {
        OnPropertyChanged(nameof(TotalRunCount));
        OnPropertyChanged(nameof(FilteredRunCount));
        OnPropertyChanged(nameof(SuccessfulRunCount));
        OnPropertyChanged(nameof(ProblemRunCount));
        OnPropertyChanged(nameof(CanClearHistory));
    }

    private string BuildStatusMessage()
    {
        if (_allItems.Count == 0)
        {
            return DeveloperModeEnabled
                ? $"Henüz kayıt yok. Asistan çalıştıkça burada görünür. Log dizini: {_runLogger.LogsDirectoryFullPath}"
                : "Henüz geçmiş kaydı yok. Asistan bir komut çalıştırdığında burada listelenir.";
        }

        var filterHint = HasActiveFilters ? " (filtre uygulanıyor)" : string.Empty;
        return DeveloperModeEnabled
            ? $"{Items.Count} / {_allItems.Count} kayıt gösteriliyor{filterHint}."
            : $"{Items.Count} kayıt gösteriliyor{filterHint}. Toplam {_allItems.Count} kayıt.";
    }

    public async Task<bool> DeleteSelectedAsync()
    {
        if (SelectedItem is null || string.IsNullOrWhiteSpace(SelectedItem.LogFilePath))
        {
            return false;
        }

        var path = SelectedItem.LogFilePath;
        var deleted = await Task.Run(() => _runLogReader.TryDeleteRunFile(path)).ConfigureAwait(true);
        if (!deleted)
        {
            StatusMessage = "Kayıt silinemedi. Dosya başka bir süreç tarafından kullanılıyor olabilir.";
            return false;
        }

        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = "Seçili kayıt silindi.";
        return true;
    }

    public async Task<int> ClearAllAsync()
    {
        var deleted = await Task.Run(() =>
            _runLogReader.DeleteAllRunFiles(_runLogger.LogsDirectoryFullPath)).ConfigureAwait(true);

        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = deleted > 0
            ? $"{deleted} geçmiş kaydı silindi."
            : "Silinecek kayıt bulunamadı.";
        return deleted;
    }

    private void ClearFilters()
    {
        FilterText = string.Empty;
        TriggerFilter = string.Empty;
        StatusFilter = string.Empty;
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
                Title = $"Adım {step.StepIndex + 1}: {step.ActionName}",
                Subtitle = step.ResultMessage,
                Success = step.ActionSuccess,
                GateOutcome = step.GateOutcome,
                GateRisk = step.GateRisk,
                GateUserApproved = step.GateUserApproved,
                ObservationSummary = FormatJsonForDisplay(step.ObservationSummaryJson),
                WindowsSummary = FormatJsonForDisplay(step.WindowsSummaryJson),
                UiTreeSummary = FormatUiTreeForDisplay(step.UiTreeSummaryJson),
                UiTreeRawJson = step.UiTreeSummaryJson ?? string.Empty,
                UiTreeElements = ParseUiTreeElements(step.UiTreeSummaryJson),
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
            var hint = truncated ? $"\n[KESİLMİŞ — {raw.Length} karakter. Tam metni kopyalamak için 'UI Ağacı Kopyala' düğmesini kullanın.]" : $"\n[{raw.Length} karakter]";
            return formatted + hint;
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static IReadOnlyList<UiTreeElementRow> ParseUiTreeElements(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<UiTreeElementRow>();
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("elements", out var elements) ||
                elements.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<UiTreeElementRow>();
            }

            var rows = new List<UiTreeElementRow>();
            foreach (var element in elements.EnumerateArray())
            {
                rows.Add(new UiTreeElementRow
                {
                    ElementId = ReadProp(element, "ElementId"),
                    ControlType = ReadProp(element, "ControlType"),
                    Name = ReadProp(element, "Name"),
                    Value = ReadProp(element, "Value"),
                    IsEnabled = !string.Equals(ReadProp(element, "IsEnabled"), "false", StringComparison.OrdinalIgnoreCase)
                });
            }

            return rows;
        }
        catch (JsonException)
        {
            return Array.Empty<UiTreeElementRow>();
        }
    }

    private static string ReadProp(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText()
        };
    }

    private void CopyUiTree()
    {
        var raw = SelectedStep?.UiTreeRawJson;
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        CopyTextToClipboard(raw);
    }

    private void CopyCommand()
    {
        if (SelectedItem is null || string.IsNullOrWhiteSpace(SelectedItem.CommandText))
        {
            return;
        }

        CopyTextToClipboard(SelectedItem.CommandText);
        StatusMessage = "Komut panoya kopyalandı.";
    }

    private void CopyResult()
    {
        if (SelectedItem is null || string.IsNullOrWhiteSpace(SelectedItem.LastResult))
        {
            return;
        }

        CopyTextToClipboard(SelectedItem.LastResult);
        StatusMessage = "Sonuç panoya kopyalandı.";
    }

    private static void CopyTextToClipboard(string text)
    {
        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
        catch
        {
            // Best-effort clipboard write.
        }
    }

    private void OpenLogsFolder()
    {
        try
        {
            var path = _runLogger.LogsDirectoryFullPath;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // best effort
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

public sealed record HistoryFilterOption(string Value, string Label);
