using System.ComponentModel;
using System.Runtime.CompilerServices;
using WindowsAiAssistant.Infrastructure;
using WindowsAiAssistant.Infrastructure.Secrets;
using WindowsAiAssistant.Infrastructure.State;

namespace WindowsAiAssistant.App.Services;

/// <summary>
/// Shared, observable view of the currently active model provider profile.
/// Backed by <see cref="ModelDecisionSettings"/>, <see cref="IActiveProfileStore"/>, and the secret store.
/// Use <see cref="Refresh"/> after any mutation to update bound consumers (Assistant header, settings page, etc.).
/// </summary>
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
    bool ProfileHasSavedKey(string profileId);
}

public sealed class ActiveProviderStatus : IActiveProviderStatus
{
    private readonly ModelDecisionSettings _settings;
    private readonly IActiveProfileStore _activeProfileStore;
    private readonly IModelProviderSecretStore _secretStore;

    public ActiveProviderStatus(
        ModelDecisionSettings settings,
        IActiveProfileStore activeProfileStore,
        IModelProviderSecretStore secretStore)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _activeProfileStore = activeProfileStore ?? throw new ArgumentNullException(nameof(activeProfileStore));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string EffectiveActiveProfileId =>
        _activeProfileStore.GetActiveProfileId() ?? _settings.ActiveProfile ?? string.Empty;

    public bool HasActiveProfile => ResolveProfile() is not null;

    public string DisplayName => ResolveProfile()?.DisplayName ?? "Aktif profil yok";
    public string Model => ResolveProfile()?.Model ?? "—";
    public string Kind => ResolveProfile()?.Kind.ToString() ?? "—";
    public string BaseUrl => ResolveProfile()?.BaseUrl ?? string.Empty;
    public bool IsEnabled => ResolveProfile()?.IsEnabled ?? false;
    public bool RequiresApiKey => ResolveProfile()?.RequiresApiKey ?? false;

    public bool HasSavedKey
    {
        get
        {
            var profile = ResolveProfile();
            if (profile is null)
            {
                return false;
            }

            return _secretStore.HasApiKey(profile.Id);
        }
    }

    public bool IsReady
    {
        get
        {
            var profile = ResolveProfile();
            if (profile is null || !profile.IsEnabled)
            {
                return false;
            }

            if (!profile.RequiresApiKey)
            {
                return true;
            }

            if (_secretStore.HasApiKey(profile.Id))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(profile.ApiKeyEnvVar) &&
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(profile.ApiKeyEnvVar)))
            {
                return true;
            }

            return false;
        }
    }

    public string ReadyReason
    {
        get
        {
            var profile = ResolveProfile();
            if (profile is null)
            {
                return "Aktif profil ayarlanmamış.";
            }

            if (!profile.IsEnabled)
            {
                return "Profil devre dışı.";
            }

            if (profile.RequiresApiKey && !HasSavedKey &&
                (string.IsNullOrWhiteSpace(profile.ApiKeyEnvVar) ||
                 string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(profile.ApiKeyEnvVar))))
            {
                return "API anahtarı eksik. Ayarlar > Profil > API Anahtarı altından kaydedin.";
            }

            return "Hazır.";
        }
    }

    public string ReadyBadge => IsReady ? "HAZIR" : "AYAR GEREKLİ";

    public bool ProfileHasSavedKey(string profileId) =>
        !string.IsNullOrWhiteSpace(profileId) && _secretStore.HasApiKey(profileId);

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

    private ModelDecisionProfile? ResolveProfile()
    {
        var id = EffectiveActiveProfileId;
        if (string.IsNullOrWhiteSpace(id) || _settings.Profiles is null)
        {
            return null;
        }

        return _settings.Profiles.FirstOrDefault(p =>
            p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
