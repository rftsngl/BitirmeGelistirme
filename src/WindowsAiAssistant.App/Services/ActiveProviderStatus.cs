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
    private ProviderProfile? _profile;
    private bool _hasSavedKey;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string EffectiveActiveProfileId => _profile?.Id ?? string.Empty;
    public bool HasActiveProfile => _profile is not null;
    public string DisplayName => _profile?.DisplayName ?? "Yeni AI runtime bekleniyor";
    public string Model => _profile?.Model ?? "-";
    public string Kind => _profile?.Kind.ToString() ?? "FrontendOnly";
    public string BaseUrl => _profile?.BaseUrl ?? string.Empty;
    public bool IsEnabled => _profile?.IsEnabled ?? false;
    public bool RequiresApiKey => _profile?.RequiresApiKey ?? false;
    public bool HasSavedKey => _hasSavedKey;
    public bool IsReady => IsEnabled && (!RequiresApiKey || HasSavedKey);
    public string ReadyReason => IsReady ? "Hazir." : "Yeni AI-first backend henuz baglanmadi.";
    public string ReadyBadge => IsReady ? "HAZIR" : "RUNTIME YOK";

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
