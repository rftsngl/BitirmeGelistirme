using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.Infrastructure;

namespace WindowsAiAssistant.App.ProviderSettings;

public sealed class ProfileListItem : ObservableObject
{
    private bool _isActive;
    private bool _hasSavedKey;
    private bool _isBuiltIn;

    public ProfileListItem(ModelDecisionProfile profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public ModelDecisionProfile Profile { get; }

    public string Id => Profile.Id;
    public string DisplayName => Profile.DisplayName;
    public string Kind => Profile.Kind.ToString();
    public string Model => Profile.Model;
    public bool RequiresApiKey => Profile.RequiresApiKey;

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
                OnPropertyChanged(nameof(ShowKeyBadge));
                OnPropertyChanged(nameof(KeyBadgeText));
            }
        }
    }

    public bool IsBuiltIn
    {
        get => _isBuiltIn;
        set
        {
            if (SetField(ref _isBuiltIn, value))
            {
                OnPropertyChanged(nameof(OriginBadge));
            }
        }
    }

    public string ActiveBadge => IsActive ? "AKTİF" : string.Empty;

    public bool ShowKeyBadge => RequiresApiKey;

    public string KeyBadgeText =>
        !RequiresApiKey ? string.Empty :
        HasSavedKey ? "ANAHTAR ✓" : "ANAHTAR YOK";

    public string OriginBadge => IsBuiltIn ? "YERLEŞİK" : "ÖZEL";
}
