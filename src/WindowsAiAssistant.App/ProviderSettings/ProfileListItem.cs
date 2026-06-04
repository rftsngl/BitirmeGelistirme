using WindowsAiAssistant.App.Mvvm;

namespace WindowsAiAssistant.App.ProviderSettings;

public sealed class ProfileListItem : ObservableObject
{
    private bool _isActive;
    private bool _hasSavedKey;

    public ProfileListItem(ProviderProfile profile, bool isBuiltIn)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        IsBuiltIn = isBuiltIn;
    }

    public ProviderProfile Profile { get; }
    public string Id => Profile.Id;
    public string DisplayName => Profile.DisplayName;
    public string Kind => Profile.Kind.ToString();
    public string Model => Profile.Model;
    public bool RequiresApiKey => Profile.RequiresApiKey;
    public bool IsBuiltIn { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetField(ref _isActive, value))
            {
                OnPropertyChanged(nameof(ActiveBadge));
            }
        }
    }

    public bool HasSavedKey
    {
        get => _hasSavedKey;
        set
        {
            if (SetField(ref _hasSavedKey, value))
            {
                OnPropertyChanged(nameof(KeyBadgeText));
            }
        }
    }

    public string ActiveBadge => IsActive ? "AKTIF" : string.Empty;
    public bool ShowKeyBadge => RequiresApiKey;
    public string KeyBadgeText => !RequiresApiKey ? string.Empty : HasSavedKey ? "ANAHTAR VAR" : "ANAHTAR YOK";
    public string OriginBadge => IsBuiltIn ? "YERLESIK" : "OZEL";
}
