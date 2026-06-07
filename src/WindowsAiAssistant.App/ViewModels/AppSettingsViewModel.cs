using System.Collections.ObjectModel;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.App.Audio;
using WindowsAiAssistant.App.Background;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.Runtime.Config;
using WindowsAiAssistant.Runtime.Policy;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class AppSettingsViewModel : ObservableObject
{
    private const int MinAgentSteps = 1;
    private const int MaxAgentSteps = 40;
    private const int MinPromptHistorySteps = 1;
    private const int MaxPromptHistorySteps = 20;

    private readonly AgentOptions _agent;
    private readonly AudioOptions _audio;
    private readonly RuntimeOptions _runtime;
    private readonly LocalAppSettingsService _localSettings;
    private readonly MicrophonePermissionService _microphonePermissions;
    private readonly SpeechReadinessService _speechReadiness;
    private readonly SwitchableSpeechToTextService _speechToText;
    private readonly BackgroundAssistantHost _backgroundHost;
    private readonly SpeechModelInventoryService _speechModelInventory;
    private string _statusMessage = string.Empty;
    private string _microphonePermissionSummary = string.Empty;
    private string _speechLanguageSummary = string.Empty;

    public IReadOnlyList<SettingsChoice> RiskHandlingChoices { get; } =
    [
        new("Allow", "Doğrudan çalıştır",
            "Asistan bu tür işlemleri sizden onay istemeden uygular."),
        new("RequireApproval", "Önce onay iste",
            "İşlem yapılmadan önce sohbet veya sesli onay ekranında sizden izin istenir."),
        new("Deny", "Engelle",
            "Bu tür işlemler hiç çalıştırılmaz.")
    ];

    public IReadOnlyList<SettingsChoice> SpeechEngineChoices { get; } =
    [
        new("whisper", "Whisper (gelişmiş, önerilen)",
            "Türkçe + İngilizce karma komutlar için en iyi doğruluk. medium model build ile gelir."),
        new("vosk", "Vosk (yerel, hızlı)",
            "Hızlı ve tamamen yerel. Türkçe için tek halka açık küçük model; yabancı kelimelerde daha zayıf."),
        new("windows", "Windows (yerleşik)",
            "Windows konuşma tanıma dil paketi yüklü olmalıdır.")
    ];

    public IReadOnlyList<SettingsChoice> TtsEngineChoices { get; } =
    [
        new("edge", "Edge (doğal neural)",
            "tr-TR-EmelNeural gibi doğal sesler. İnternet bağlantısı gerekir."),
        new("windows", "Windows (yerel)",
            "Sistemde yüklü ses (ör. Microsoft Tolga). İnternet gerekmez.")
    ];

    public IReadOnlyList<SettingsChoice> WakeWordChoices { get; } =
    [
        new("asistan", "Asistan", "«Asistan» veya «Hey asistan» deyin (Türkçe Vosk modeli)."),
        new("hey-asistan", "Hey asistan", "«Hey asistan» deyin (Türkçe Vosk modeli)."),
        new("bilgisayar", "Bilgisayar", "«Bilgisayar» deyin (Türkçe Vosk modeli)."),
        new("computer", "Computer", "İngilizce «Computer» deyin (İngilizce Vosk modeli)."),
        new("jarvis", "Jarvis", "İngilizce «Jarvis» deyin (İngilizce Vosk modeli).")
    ];

    public AppSettingsViewModel(
        AgentOptions agent,
        AudioOptions audio,
        RuntimeOptions runtime,
        LocalAppSettingsService localSettings,
        MicrophoneDeviceService microphoneDevices,
        MicrophonePermissionService microphonePermissions,
        SpeechReadinessService speechReadiness,
        SwitchableSpeechToTextService speechToText,
        BackgroundAssistantHost backgroundHost,
        SpeechModelInventoryService speechModelInventory)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _localSettings = localSettings ?? throw new ArgumentNullException(nameof(localSettings));
        _microphonePermissions = microphonePermissions ?? throw new ArgumentNullException(nameof(microphonePermissions));
        _speechReadiness = speechReadiness ?? throw new ArgumentNullException(nameof(speechReadiness));
        _speechToText = speechToText ?? throw new ArgumentNullException(nameof(speechToText));
        _backgroundHost = backgroundHost ?? throw new ArgumentNullException(nameof(backgroundHost));
        _speechModelInventory = speechModelInventory ?? throw new ArgumentNullException(nameof(speechModelInventory));
        ArgumentNullException.ThrowIfNull(microphoneDevices);

        MicrophoneOptions = new ObservableCollection<MicrophoneDevice>(microphoneDevices.ListDevices());
        SpeechModels = new ObservableCollection<SpeechModelDownloadItemViewModel>();
        SpeechModelGroups = new ObservableCollection<SpeechModelGroupViewModel>();
        RequestMicrophonePermissionCommand = new AsyncRelayCommand(RequestMicrophonePermissionAsync);
        RefreshSpeechModelInventoryCommand = new RelayCommand(RefreshSpeechModelInventory);
        DownloadRequiredSpeechModelsCommand = new AsyncRelayCommand(
            DownloadRequiredSpeechModelsAsync,
            () => HasMissingRequiredSpeechModels);
        RefreshSpeechDiagnostics();
        RefreshSpeechModelInventory();

        if (WindowsStartupService.IsRegistered())
        {
            _audio.StartWithWindows = true;
        }
    }

    public AsyncRelayCommand RequestMicrophonePermissionCommand { get; }

    public RelayCommand RefreshSpeechModelInventoryCommand { get; }

    public AsyncRelayCommand DownloadRequiredSpeechModelsCommand { get; }

    public ObservableCollection<MicrophoneDevice> MicrophoneOptions { get; }

    public ObservableCollection<SpeechModelDownloadItemViewModel> SpeechModels { get; }

    public ObservableCollection<SpeechModelGroupViewModel> SpeechModelGroups { get; }

    public int SpeechModelsDownloadedCount => SpeechModels.Count(item => item.IsDownloaded);

    public int SpeechModelsTotalCount => SpeechModels.Count;

    public int SpeechModelsMissingCount =>
        SpeechModels.Count(item => item.State == SpeechModelDownloadState.NotDownloaded);

    public bool HasMissingRequiredSpeechModels =>
        SpeechModels.Any(item => item.IsActiveForSettings && item.CanDownload);

    public string SpeechModelsSummaryMessage =>
        $"{SpeechModelsDownloadedCount} / {SpeechModelsTotalCount} model indirildi · {SpeechModelsMissingCount} eksik";

    public bool BackgroundModeEnabled
    {
        get => _audio.BackgroundModeEnabled;
        set { _audio.BackgroundModeEnabled = value; OnPropertyChanged(); }
    }

    public bool StartMinimizedToTray
    {
        get => _audio.StartMinimizedToTray;
        set { _audio.StartMinimizedToTray = value; OnPropertyChanged(); }
    }

    public bool StartWithWindows
    {
        get => _audio.StartWithWindows;
        set { _audio.StartWithWindows = value; OnPropertyChanged(); }
    }

    public bool WakeWordEnabled
    {
        get => _audio.WakeWordEnabled;
        set { _audio.WakeWordEnabled = value; OnPropertyChanged(); }
    }

    public bool IsWakeWordServiceAvailable => _speechReadiness.IsWakeWordServiceAvailable();

    public bool IsSttServiceAvailable => _speechReadiness.IsSttServiceAvailable();

    public string WakeWordServiceSummary => _speechReadiness.DescribeWakeWordSupport();

    public string SttEngineSummary => _speechReadiness.DescribeActiveSttEngine();

    public string VoskModelPath
    {
        get => _audio.VoskModelPath;
        set { _audio.VoskModelPath = value ?? string.Empty; OnPropertyChanged(); }
    }

    public IReadOnlyList<SettingsChoice> VoskModelVariantChoices =>
        VoskModelCatalog.GetVariantChoices(
                VoskWakeWordModelService.ResolveLanguageKeyFromSpeechLanguage(_audio.SpeechLanguage))
            .Select(choice => new SettingsChoice(choice.Id, choice.Label, choice.Description))
            .ToList();

    public string VoskModelVariant
    {
        get => _audio.VoskModelVariant;
        set
        {
            _audio.VoskModelVariant = string.IsNullOrWhiteSpace(value) ? "small" : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedVoskModelVariant));
            OnPropertyChanged(nameof(VoskModelVariantSummary));
            OnPropertyChanged(nameof(SttEngineSummary));
            RefreshSpeechModelInventory();
        }
    }

    public SettingsChoice? SelectedVoskModelVariant
    {
        get => VoskModelVariantChoices.FirstOrDefault(choice =>
                   choice.Value.Equals(_audio.VoskModelVariant, StringComparison.OrdinalIgnoreCase))
               ?? VoskModelVariantChoices.FirstOrDefault();
        set
        {
            if (value is null)
            {
                return;
            }

            VoskModelVariant = value.Value;
        }
    }

    public string VoskModelVariantSummary
    {
        get
        {
            var lang = VoskWakeWordModelService.ResolveLanguageKeyFromSpeechLanguage(_audio.SpeechLanguage);
            var descriptor = VoskModelCatalog.Resolve(lang, _audio.VoskModelVariant);
            return descriptor.Note ?? $"{descriptor.DisplayName} — {descriptor.FolderName} ({descriptor.SizeHint})";
        }
    }

    public string WakeWordPhrase
    {
        get => _audio.WakeWordPhrase;
        set
        {
            _audio.WakeWordPhrase = string.IsNullOrWhiteSpace(value) ? "asistan" : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedWakeWord));
            RefreshSpeechModelInventory();
        }
    }

    public SettingsChoice? SelectedWakeWord
    {
        get => WakeWordChoices.FirstOrDefault(choice =>
            choice.Value.Equals(_audio.WakeWordPhrase, StringComparison.OrdinalIgnoreCase))
               ?? WakeWordChoices[0];
        set
        {
            if (value is null)
            {
                return;
            }

            WakeWordPhrase = value.Value;
        }
    }

    public bool GlobalHotKeyEnabled
    {
        get => _audio.GlobalHotKeyEnabled;
        set { _audio.GlobalHotKeyEnabled = value; OnPropertyChanged(); }
    }

    public string GlobalHotKey
    {
        get => _audio.GlobalHotKey;
        set { _audio.GlobalHotKey = value ?? string.Empty; OnPropertyChanged(); }
    }

    public bool TextToSpeechEnabled
    {
        get => _audio.TextToSpeechEnabled;
        set { _audio.TextToSpeechEnabled = value; OnPropertyChanged(); }
    }

    public string TtsEngine
    {
        get => _audio.TtsEngine;
        set
        {
            _audio.TtsEngine = string.IsNullOrWhiteSpace(value) ? "edge" : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTtsEngine));
        }
    }

    public SettingsChoice? SelectedTtsEngine
    {
        get => TtsEngineChoices.FirstOrDefault(choice =>
            choice.Value.Equals(_audio.TtsEngine, StringComparison.OrdinalIgnoreCase))
               ?? TtsEngineChoices[0];
        set
        {
            if (value is null)
            {
                return;
            }

            TtsEngine = value.Value;
        }
    }

    public string TtsVoiceName
    {
        get => _audio.TtsVoiceName;
        set { _audio.TtsVoiceName = value ?? string.Empty; OnPropertyChanged(); }
    }

    public string SpeechLanguage
    {
        get => _audio.SpeechLanguage;
        set
        {
            _audio.SpeechLanguage = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VoskModelVariantChoices));
            OnPropertyChanged(nameof(SelectedVoskModelVariant));
            OnPropertyChanged(nameof(VoskModelVariantSummary));
            RefreshSpeechModelInventory();
        }
    }

    public int SpeechListenTimeoutSeconds
    {
        get => _audio.SpeechListenTimeoutSeconds;
        set { _audio.SpeechListenTimeoutSeconds = Math.Clamp(value, 3, 120); OnPropertyChanged(); }
    }

    public int SilenceEndMilliseconds
    {
        get => _audio.SilenceEndMilliseconds;
        set { _audio.SilenceEndMilliseconds = Math.Clamp(value, 400, 3000); OnPropertyChanged(); }
    }

    public double VadSpeechMultiplier
    {
        get => _audio.VadSpeechMultiplier;
        set { _audio.VadSpeechMultiplier = Math.Clamp(value, 1.5, 8.0); OnPropertyChanged(); }
    }

    public int VadMinSpeechMilliseconds
    {
        get => _audio.VadMinSpeechMilliseconds;
        set { _audio.VadMinSpeechMilliseconds = Math.Clamp(value, 200, 2000); OnPropertyChanged(); }
    }

    public double MicGainTargetPeak
    {
        get => _audio.MicGainTargetPeak;
        set { _audio.MicGainTargetPeak = Math.Clamp(value, 0.2, 0.8); OnPropertyChanged(); }
    }

    public int SttMinTranscriptCharacters
    {
        get => _audio.SttMinTranscriptCharacters;
        set { _audio.SttMinTranscriptCharacters = Math.Clamp(value, 2, 20); OnPropertyChanged(); }
    }

    public bool WakeWordFinalOnly
    {
        get => _audio.WakeWordFinalOnly;
        set { _audio.WakeWordFinalOnly = value; OnPropertyChanged(); }
    }

    public int WakeWordCooldownSeconds
    {
        get => _audio.WakeWordCooldownSeconds;
        set { _audio.WakeWordCooldownSeconds = Math.Clamp(value, 1, 30); OnPropertyChanged(); }
    }

    public bool WakeWordRequireSpeechEnergy
    {
        get => _audio.WakeWordRequireSpeechEnergy;
        set { _audio.WakeWordRequireSpeechEnergy = value; OnPropertyChanged(); }
    }

    public int OverlayAutoCloseSeconds
    {
        get => _audio.OverlayAutoCloseSeconds;
        set { _audio.OverlayAutoCloseSeconds = Math.Clamp(value, 3, 120); OnPropertyChanged(); }
    }

    public bool VoiceApprovalEnabled
    {
        get => _audio.VoiceApprovalEnabled;
        set { _audio.VoiceApprovalEnabled = value; OnPropertyChanged(); }
    }

    public string SpeechEngine
    {
        get => _audio.SpeechEngine;
        set
        {
            _audio.SpeechEngine = string.IsNullOrWhiteSpace(value) ? "whisper" : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSpeechEngine));
            OnPropertyChanged(nameof(IsWhisperEngine));
            OnPropertyChanged(nameof(ShowVoskModelSettings));
            OnPropertyChanged(nameof(ShowWhisperModelSettings));
            RefreshSpeechDiagnostics();
            RefreshSpeechModelInventory();
        }
    }

    public SettingsChoice? SelectedSpeechEngine
    {
        get => SpeechEngineChoices.FirstOrDefault(choice =>
            choice.Value.Equals(_audio.SpeechEngine, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null)
            {
                return;
            }

            SpeechEngine = value.Value;
        }
    }

    public bool IsWhisperEngine =>
        string.Equals(_audio.SpeechEngine, "whisper", StringComparison.OrdinalIgnoreCase);

    public bool ShowVoskModelSettings =>
        string.Equals(_audio.SpeechEngine, "vosk", StringComparison.OrdinalIgnoreCase);

    public bool ShowWhisperModelSettings => IsWhisperEngine;

    public string WakeWordEngineSummary =>
        "Uyandırma: Vosk (hafif, yalnızca «asistan» gibi kısa kelimeler). Komut dinleme motorundan bağımsızdır.";

    public IReadOnlyList<SettingsChoice> WhisperModelVariantChoices =>
        WhisperModelCatalog.GetVariantChoices()
            .Select(choice => new SettingsChoice(choice.Id, choice.Label, choice.Description))
            .ToList();

    public string WhisperModelVariant
    {
        get => _audio.WhisperModelVariant;
        set
        {
            _audio.WhisperModelVariant = string.IsNullOrWhiteSpace(value) ? "small" : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedWhisperModelVariant));
            OnPropertyChanged(nameof(WhisperModelVariantSummary));
            OnPropertyChanged(nameof(SttEngineSummary));
            RefreshSpeechModelInventory();
        }
    }

    public SettingsChoice? SelectedWhisperModelVariant
    {
        get => WhisperModelVariantChoices.FirstOrDefault(choice =>
                   choice.Value.Equals(_audio.WhisperModelVariant, StringComparison.OrdinalIgnoreCase))
               ?? WhisperModelVariantChoices.FirstOrDefault();
        set
        {
            if (value is null)
            {
                return;
            }

            WhisperModelVariant = value.Value;
        }
    }

    public string WhisperModelVariantSummary
    {
        get
        {
            var descriptor = WhisperModelCatalog.Resolve(_audio.WhisperModelVariant);
            return descriptor.Note ?? $"{descriptor.DisplayName} — {descriptor.FileName} ({descriptor.SizeHint})";
        }
    }

    public string WhisperModelPath
    {
        get => _audio.WhisperModelPath;
        set { _audio.WhisperModelPath = value ?? string.Empty; OnPropertyChanged(); }
    }

    public MicrophoneDevice? SelectedMicrophone
    {
        get => MicrophoneOptions.FirstOrDefault(device => device.Index == _audio.InputDeviceIndex)
               ?? MicrophoneOptions.FirstOrDefault();
        set
        {
            _audio.InputDeviceIndex = value?.Index ?? -1;
            OnPropertyChanged();
        }
    }

    public bool DeveloperModeEnabled
    {
        get => _audio.DeveloperModeEnabled;
        set
        {
            _audio.DeveloperModeEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDeveloperMode));
        }
    }

    public bool IsDeveloperMode => _audio.DeveloperModeEnabled;

    public int MaxSteps
    {
        get => _agent.MaxSteps;
        set
        {
            var next = Math.Clamp(value, MinAgentSteps, MaxAgentSteps);
            if (_agent.MaxSteps == next)
            {
                return;
            }

            _agent.MaxSteps = next;
            OnPropertyChanged();
        }
    }

    public int MaxPriorStepsInPrompt
    {
        get => _agent.MaxPriorStepsInPrompt;
        set
        {
            var next = Math.Clamp(value, MinPromptHistorySteps, MaxPromptHistorySteps);
            if (_agent.MaxPriorStepsInPrompt == next)
            {
                return;
            }

            _agent.MaxPriorStepsInPrompt = next;
            OnPropertyChanged();
        }
    }

    public string NormalHandling
    {
        get => _runtime.ActionPolicy.Normal.ToString();
        set
        {
            if (Enum.TryParse<RiskHandling>(value, out var parsed))
            {
                _runtime.ActionPolicy.Normal = parsed;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedNormalHandling));
            }
        }
    }

    public SettingsChoice? SelectedNormalHandling
    {
        get => FindRiskChoice(_runtime.ActionPolicy.Normal);
        set
        {
            if (value is not null)
            {
                NormalHandling = value.Value;
            }
        }
    }

    public string SensitiveHandling
    {
        get => _runtime.ActionPolicy.Sensitive.ToString();
        set
        {
            if (Enum.TryParse<RiskHandling>(value, out var parsed))
            {
                _runtime.ActionPolicy.Sensitive = parsed;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedSensitiveHandling));
            }
        }
    }

    public SettingsChoice? SelectedSensitiveHandling
    {
        get => FindRiskChoice(_runtime.ActionPolicy.Sensitive);
        set
        {
            if (value is not null)
            {
                SensitiveHandling = value.Value;
            }
        }
    }

    public string DestructiveHandling
    {
        get => _runtime.ActionPolicy.Destructive.ToString();
        set
        {
            if (Enum.TryParse<RiskHandling>(value, out var parsed))
            {
                _runtime.ActionPolicy.Destructive = parsed;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedDestructiveHandling));
            }
        }
    }

    public SettingsChoice? SelectedDestructiveHandling
    {
        get => FindRiskChoice(_runtime.ActionPolicy.Destructive);
        set
        {
            if (value is not null)
            {
                DestructiveHandling = value.Value;
            }
        }
    }

    private SettingsChoice? FindRiskChoice(RiskHandling handling) =>
        RiskHandlingChoices.FirstOrDefault(choice =>
            choice.Value.Equals(handling.ToString(), StringComparison.OrdinalIgnoreCase));

    public bool AllowSessionRemember
    {
        get => _runtime.ActionPolicy.AllowSessionRemember;
        set { _runtime.ActionPolicy.AllowSessionRemember = value; OnPropertyChanged(); }
    }

    public bool EnableChromiumAccessibility
    {
        get => _runtime.UiAutomation.EnableChromiumAccessibility;
        set { _runtime.UiAutomation.EnableChromiumAccessibility = value; OnPropertyChanged(); }
    }

    public bool EnableUia2Fallback
    {
        get => _runtime.UiAutomation.EnableUia2Fallback;
        set { _runtime.UiAutomation.EnableUia2Fallback = value; OnPropertyChanged(); }
    }

    public int Uia2FallbackMinElements
    {
        get => _runtime.UiAutomation.Uia2FallbackMinElements;
        set
        {
            _runtime.UiAutomation.Uia2FallbackMinElements = Math.Clamp(value, 1, 20);
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string MicrophonePermissionSummary
    {
        get => _microphonePermissionSummary;
        private set => SetField(ref _microphonePermissionSummary, value);
    }

    public string SpeechLanguageSummary
    {
        get => _speechLanguageSummary;
        private set => SetField(ref _speechLanguageSummary, value);
    }

    public bool IsMicrophoneGranted =>
        _microphonePermissions.CheckAccess() == MicrophoneAccessState.Granted;

    public void RefreshSpeechDiagnostics()
    {
        MicrophonePermissionSummary = MicrophonePermissionService.Describe(_microphonePermissions.CheckAccess());
        SpeechLanguageSummary = _speechReadiness.DescribeLanguageSupport();
        OnPropertyChanged(nameof(IsMicrophoneGranted));
        OnPropertyChanged(nameof(IsWakeWordServiceAvailable));
        OnPropertyChanged(nameof(IsSttServiceAvailable));
        OnPropertyChanged(nameof(WakeWordServiceSummary));
        OnPropertyChanged(nameof(SttEngineSummary));
    }

    public async Task RequestMicrophonePermissionAsync()
    {
        var state = await _microphonePermissions.RequestAccessAsync().ConfigureAwait(true);
        RefreshSpeechDiagnostics();
        StatusMessage = MicrophonePermissionService.Describe(state);
    }

    public async Task SaveAsync()
    {
        var agentSnapshot = new AgentOptions
        {
            MaxSteps = Math.Clamp(_agent.MaxSteps, MinAgentSteps, MaxAgentSteps),
            MaxPriorStepsInPrompt = Math.Clamp(
                _agent.MaxPriorStepsInPrompt,
                MinPromptHistorySteps,
                MaxPromptHistorySteps)
        };

        var audioSnapshot = new AudioOptions();
        LocalAppSettingsService.CopyAudio(_audio, audioSnapshot);
        var policySnapshot = new ActionPolicy
        {
            Normal = _runtime.ActionPolicy.Normal,
            Sensitive = _runtime.ActionPolicy.Sensitive,
            Destructive = _runtime.ActionPolicy.Destructive,
            AllowSessionRemember = _runtime.ActionPolicy.AllowSessionRemember
        };

        var uiAutomationSnapshot = new UiAutomationOptions();
        LocalAppSettingsService.CopyUiAutomation(_runtime.UiAutomation, uiAutomationSnapshot);

        _localSettings.SaveSettings(agentSnapshot, audioSnapshot, policySnapshot, uiAutomationSnapshot, StartWithWindows);
        _speechToText.Reload();
        RefreshSpeechDiagnostics();
        RefreshSpeechModelInventory();

        var applyNote = await _backgroundHost.ApplyRuntimeSettingsAsync().ConfigureAwait(true);
        StatusMessage = applyNote is null
            ? "Ayarlar kaydedildi ve uygulandı."
            : $"Ayarlar kaydedildi. {applyNote}";
    }

    public void RefreshSpeechModelInventory()
    {
        var catalog = _speechModelInventory.ListAll();
        var existing = SpeechModels.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        SpeechModels.Clear();

        foreach (var info in catalog)
        {
            if (existing.TryGetValue(info.Id, out var current)
                && current.State is SpeechModelDownloadState.Downloading or SpeechModelDownloadState.Failed)
            {
                if (current.State == SpeechModelDownloadState.Failed)
                {
                    current.ApplyInventoryState(_speechModelInventory.GetState(info.Id));
                }

                SpeechModels.Add(current);
                continue;
            }

            var item = new SpeechModelDownloadItemViewModel(info, DownloadSpeechModelAsync);
            item.ApplyInventoryState(_speechModelInventory.GetState(info.Id));
            SpeechModels.Add(item);
        }

        RebuildSpeechModelGroups();
        UpdateSpeechModelSummary();
    }

    private void RebuildSpeechModelGroups()
    {
        SpeechModelGroups.Clear();

        var definitions = new (string Category, string Description)[]
        {
            ("Komut dinleme (Whisper)", "Sesli komut tanıma için Whisper ggml modelleri."),
            ("Uyandırma (Vosk)", "«Asistan» gibi uyandırma kelimeleri için hafif Vosk modelleri."),
            ("Komut dinleme (Vosk)", "Whisper yerine Vosk ile komut dinleme seçtiyseniz gerekir.")
        };

        foreach (var (category, description) in definitions)
        {
            var items = SpeechModels
                .Where(item => item.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (items.Count == 0)
            {
                continue;
            }

            SpeechModelGroups.Add(new SpeechModelGroupViewModel(category, description, items));
        }
    }

    private void UpdateSpeechModelSummary()
    {
        OnPropertyChanged(nameof(SpeechModelsDownloadedCount));
        OnPropertyChanged(nameof(SpeechModelsTotalCount));
        OnPropertyChanged(nameof(SpeechModelsMissingCount));
        OnPropertyChanged(nameof(HasMissingRequiredSpeechModels));
        OnPropertyChanged(nameof(SpeechModelsSummaryMessage));
        DownloadRequiredSpeechModelsCommand.RaiseCanExecuteChanged();
    }

    private async Task DownloadRequiredSpeechModelsAsync()
    {
        foreach (var item in SpeechModels
                     .Where(model => model.IsActiveForSettings && model.CanDownload)
                     .ToList())
        {
            await DownloadSpeechModelAsync(item).ConfigureAwait(true);
        }
    }

    private async Task DownloadSpeechModelAsync(SpeechModelDownloadItemViewModel item)
    {
        item.BeginDownload();
        try
        {
            var progress = new Progress<ModelDownloadProgress>(update => item.ReportProgress(update));
            await _speechModelInventory.DownloadAsync(item.Id, progress).ConfigureAwait(true);
            item.MarkDownloaded();
            RefreshSpeechDiagnostics();
            RefreshSpeechModelInventory();
            StatusMessage = $"{item.Title} indirildi.";
        }
        catch (Exception ex)
        {
            item.MarkFailed(ex.Message);
            UpdateSpeechModelSummary();
            StatusMessage = $"{item.Title} indirilemedi: {ex.Message}";
        }
    }
}
