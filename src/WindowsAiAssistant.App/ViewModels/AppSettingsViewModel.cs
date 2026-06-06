using System.Collections.ObjectModel;
using WindowsAiAssistant.App.Audio;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.Runtime.Config;
using WindowsAiAssistant.Runtime.Policy;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class AppSettingsViewModel : ObservableObject
{
    private readonly AudioOptions _audio;
    private readonly RuntimeOptions _runtime;
    private readonly LocalAppSettingsService _localSettings;
    private string _statusMessage = string.Empty;

    public AppSettingsViewModel(
        AudioOptions audio,
        RuntimeOptions runtime,
        LocalAppSettingsService localSettings,
        MicrophoneDeviceService microphoneDevices)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _localSettings = localSettings ?? throw new ArgumentNullException(nameof(localSettings));
        ArgumentNullException.ThrowIfNull(microphoneDevices);

        RiskHandlingOptions = new ObservableCollection<string>(
            Enum.GetNames(typeof(RiskHandling)));

        SpeechEngineOptions = new ObservableCollection<string> { "windows", "whisper" };
        MicrophoneOptions = new ObservableCollection<MicrophoneDevice>(microphoneDevices.ListDevices());

        if (WindowsStartupService.IsRegistered())
        {
            _audio.StartWithWindows = true;
        }
    }

    public ObservableCollection<string> RiskHandlingOptions { get; }
    public ObservableCollection<string> SpeechEngineOptions { get; }
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

    public string PorcupineAccessKey
    {
        get => _audio.PorcupineAccessKey;
        set { _audio.PorcupineAccessKey = value ?? string.Empty; OnPropertyChanged(); }
    }

    public string PorcupineKeywordPath
    {
        get => _audio.PorcupineKeywordPath;
        set { _audio.PorcupineKeywordPath = value ?? string.Empty; OnPropertyChanged(); }
    }

    public string PorcupineModelPath
    {
        get => _audio.PorcupineModelPath;
        set { _audio.PorcupineModelPath = value ?? string.Empty; OnPropertyChanged(); }
    }

    public double PorcupineSensitivity
    {
        get => _audio.PorcupineSensitivity;
        set { _audio.PorcupineSensitivity = (float)Math.Clamp(value, 0.01, 1.0); OnPropertyChanged(); }
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
            _audio.SpeechEngine = string.IsNullOrWhiteSpace(value) ? "windows" : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWhisperEngine));
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

    public string NormalHandling
    {
        get => _runtime.ActionPolicy.Normal.ToString();
        set
        {
            if (Enum.TryParse<RiskHandling>(value, out var parsed))
            {
                _runtime.ActionPolicy.Normal = parsed;
                OnPropertyChanged();
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
            }
        }
    }

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

    public void Save()
    {
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

        _localSettings.SaveAudioAndPolicy(audioSnapshot, policySnapshot, uiAutomationSnapshot, StartWithWindows);
        StatusMessage =
            "Ayarlar kaydedildi (appsettings.Local.json). " +
            "Hotkey, wake-word, STT motoru ve UI otomasyon secenekleri icin uygulamayi yeniden baslatin.";
    }
}
