using System.Collections.ObjectModel;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.App.Audio;
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
        new("vosk", "Vosk (yerel, önerilen)",
            "Türkçe komut dinleme için önerilir. Model bir kez indirilir, internet gerekmez."),
        new("windows", "Windows (yerleşik)",
            "Windows konuşma tanıma dil paketi yüklü olmalıdır."),
        new("whisper", "Whisper (gelişmiş)",
            "Yalnızca geliştirici modunda. Yerel ggml model dosyası gerekir.")
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
        SpeechReadinessService speechReadiness)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _localSettings = localSettings ?? throw new ArgumentNullException(nameof(localSettings));
        _microphonePermissions = microphonePermissions ?? throw new ArgumentNullException(nameof(microphonePermissions));
        _speechReadiness = speechReadiness ?? throw new ArgumentNullException(nameof(speechReadiness));
        ArgumentNullException.ThrowIfNull(microphoneDevices);

        MicrophoneOptions = new ObservableCollection<MicrophoneDevice>(microphoneDevices.ListDevices());
        RequestMicrophonePermissionCommand = new AsyncRelayCommand(RequestMicrophonePermissionAsync);
        RefreshSpeechDiagnostics();

        if (WindowsStartupService.IsRegistered())
        {
            _audio.StartWithWindows = true;
        }
    }

    public AsyncRelayCommand RequestMicrophonePermissionCommand { get; }

    public ObservableCollection<MicrophoneDevice> MicrophoneOptions { get; }

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

    public bool IsWakeWordServiceAvailable =>
        _speechReadiness.IsWakeWordServiceAvailable() || _microphonePermissions.CheckAccess() == MicrophoneAccessState.Granted;

    public bool IsSttServiceAvailable => _speechReadiness.IsSttServiceAvailable();

    public string WakeWordServiceSummary => _speechReadiness.DescribeWakeWordSupport();

    public string SttEngineSummary => _speechReadiness.DescribeActiveSttEngine();

    public string VoskModelPath
    {
        get => _audio.VoskModelPath;
        set { _audio.VoskModelPath = value ?? string.Empty; OnPropertyChanged(); }
    }

    public string WakeWordPhrase
    {
        get => _audio.WakeWordPhrase;
        set
        {
            _audio.WakeWordPhrase = string.IsNullOrWhiteSpace(value) ? "asistan" : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedWakeWord));
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

    public string SpeechLanguage
    {
        get => _audio.SpeechLanguage;
        set { _audio.SpeechLanguage = value ?? string.Empty; OnPropertyChanged(); }
    }

    public int SpeechListenTimeoutSeconds
    {
        get => _audio.SpeechListenTimeoutSeconds;
        set { _audio.SpeechListenTimeoutSeconds = Math.Clamp(value, 3, 120); OnPropertyChanged(); }
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
            _audio.SpeechEngine = string.IsNullOrWhiteSpace(value) ? "vosk" : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSpeechEngine));
            OnPropertyChanged(nameof(IsWhisperEngine));
            RefreshSpeechDiagnostics();
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

    public void Save()
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
        RefreshSpeechDiagnostics();
        StatusMessage =
            "Ayarlar kaydedildi. Sesli asistan, uyandırma kelimesi ve gelişmiş konuşma motoru değişiklikleri için uygulamayı yeniden başlatın.";
    }
}
