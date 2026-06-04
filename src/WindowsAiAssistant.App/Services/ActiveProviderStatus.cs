using System.ComponentModel;
using System.Runtime.CompilerServices;
using WindowsAiAssistant.App.ProviderSettings;

namespace WindowsAiAssistant.App.Services;

public interface IActiveProviderStatus : INotifyPropertyChanged
{
    string EffectiveActiveProfileId { get; }
    bool HasActiveProfile { get; }
    string DisplayName { get; }
    string Model { get; }
    string Kind { get; }
    string BaseUrl { get; }
    bool IsEnabled { get; }
    bool RequiresApiKey { get; }
    bool HasSavedKey { get; }
    bool IsReady { get; }
    string ReadyReason { get; }
    string ReadyBadge { get; }

    void Refresh();
}

public sealed class ActiveProviderStatus : IActiveProviderStatus
{
    private readonly IProviderConfigurationService _configurationService;
    private ProviderProfile? _profile;
    private bool _hasSavedKey;

    public ActiveProviderStatus(IProviderConfigurationService configurationService)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string EffectiveActiveProfileId => _profile?.Id ?? string.Empty;
    public bool HasActiveProfile => _profile is not null;
    public string DisplayName => _profile?.DisplayName ?? "Saglayici secilmedi";
    public string Model => _profile?.Model ?? "-";
    public string Kind => _profile?.Kind.ToString() ?? "-";
    public string BaseUrl => _profile?.BaseUrl ?? string.Empty;
    public bool IsEnabled => _profile?.IsEnabled ?? false;
    public bool RequiresApiKey => _profile?.RequiresApiKey ?? true;
    public bool HasSavedKey => _hasSavedKey;

    public bool IsReady =>
        _profile is not null && _configurationService.IsProfileReady(_profile);

    public string ReadyReason
    {
        get
        {
            if (_profile is null)
            {
                return "Aktif saglayici profili secilmedi.";
            }

            if (!_profile.IsEnabled)
            {
                return "Profil devre disi.";
            }

            if (string.IsNullOrWhiteSpace(_profile.BaseUrl) || string.IsNullOrWhiteSpace(_profile.Model))
            {
                return "BaseUrl veya model eksik.";
            }

            if (_profile.RequiresApiKey && !_configurationService.IsProfileReady(_profile))
            {
                var envHint = string.IsNullOrWhiteSpace(_profile.ApiKeyEnvVar)
                    ? "API anahtari"
                    : $"API anahtari ({_profile.ApiKeyEnvVar})";
                return $"{envHint} ortam degiskeninde veya yerel ayarlarda bulunamadi.";
            }

            return "AgentLoop bu saglayici ile calismaya hazir.";
        }
    }

    public string ReadyBadge
    {
        get
        {
            if (_profile is null)
            {
                return "YOK";
            }

            if (!_profile.IsEnabled)
            {
                return "KAPALI";
            }

            return IsReady ? "HAZIR" : "EKSIK";
        }
    }

    public void SetProfile(ProviderProfile? profile, bool hasSavedKey)
    {
        _profile = profile;
        _hasSavedKey = hasSavedKey;
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(EffectiveActiveProfileId));
        OnPropertyChanged(nameof(HasActiveProfile));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(BaseUrl));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(RequiresApiKey));
        OnPropertyChanged(nameof(HasSavedKey));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(ReadyReason));
        OnPropertyChanged(nameof(ReadyBadge));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
