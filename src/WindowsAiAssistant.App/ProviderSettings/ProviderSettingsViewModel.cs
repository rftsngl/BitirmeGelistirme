using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.ProviderSettings;

public sealed class ProviderSettingsViewModel : ObservableObject
{
    private const string PlaceholderProfileId = "new-ai-runtime";
    private readonly ActiveProviderStatus _providerStatus;
    private readonly HashSet<string> _savedKeys = new(StringComparer.OrdinalIgnoreCase);
    private ProviderProfile? _selectedProfile;
    private ProviderProfileDraft? _editingDraft;
    private ConnectionTestSummary? _lastConnectionTest;
    private string? _activeProfileId;
    private string _statusMessage = "Bu ekran su anda yalnizca frontend durumunu gosterir.";
    private string _apiKeyInput = string.Empty;
    private bool _isEditingNew;
    private bool _isTestingConnection;

    public ProviderSettingsViewModel(ActiveProviderStatus providerStatus)
    {
        _providerStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));
        Profiles =
        [
            new ProviderProfile
            {
                Id = PlaceholderProfileId,
                DisplayName = "Yeni AI-first runtime",
                Kind = ModelProviderKind.OpenAICompatible,
                Model = "Henuz baglanmadi",
                EndpointPath = "chat/completions",
                IsEnabled = false
            }
        ];
        ProfileItems = [];
    }

    public IActiveProviderStatus ProviderStatus => _providerStatus;
    public ObservableCollection<ProviderProfile> Profiles { get; }
    public ObservableCollection<ProfileListItem> ProfileItems { get; }

    public ProviderProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value))
            {
                return;
            }

            _selectedProfile = value;
            ApiKeyInput = string.Empty;
            LastConnectionTest = null;
            NotifySelectionChanged();
        }
    }

    public string EffectiveActiveProfileId => _activeProfileId ?? string.Empty;
    public string ActiveProfileCaption => string.IsNullOrWhiteSpace(EffectiveActiveProfileId) ? "(yok)" : EffectiveActiveProfileId;
    public bool IsCurrentActive => SelectedProfile is not null && SelectedProfile.Id.Equals(EffectiveActiveProfileId, StringComparison.OrdinalIgnoreCase);
    public bool IsCurrentBuiltIn => SelectedProfile?.Id.Equals(PlaceholderProfileId, StringComparison.OrdinalIgnoreCase) == true;
    public bool IsCurrentUserDefined => SelectedProfile is not null && !IsCurrentBuiltIn;
    public string SetActiveButtonText => IsCurrentActive ? "Aktif" : "Aktif Yap";
    public bool SetActiveEnabled => SelectedProfile is { IsEnabled: true };
    public bool TestConnectionEnabled => SelectedProfile is not null;
    public bool EditEnabled => IsCurrentUserDefined;
    public bool DeleteEnabled => IsCurrentUserDefined;

    public string DetailDisplayName => SelectedProfile?.DisplayName ?? "-";
    public string DetailKind => SelectedProfile?.Kind.ToString() ?? "-";
    public string DetailBaseUrl => SelectedProfile?.BaseUrl ?? "-";
    public string DetailModel => SelectedProfile?.Model ?? "-";
    public string DetailEndpointPath => SelectedProfile?.EndpointPath ?? "-";
    public string DetailRequiresApiKey => SelectedProfile is null ? "-" : SelectedProfile.RequiresApiKey ? "Evet" : "Hayir";
    public string DetailIsEnabled => SelectedProfile is null ? "-" : SelectedProfile.IsEnabled ? "Evet" : "Hayir";
    public string DetailOriginLabel => SelectedProfile is null ? "-" : IsCurrentBuiltIn ? "Yerlesik frontend placeholder" : "Gecici UI profili";

    public bool HasSavedKeyForCurrent => SelectedProfile is not null && _savedKeys.Contains(SelectedProfile.Id);
    public string SavedKeyMask => HasSavedKeyForCurrent ? "********demo" : string.Empty;
    public string SavedKeyStatus => HasSavedKeyForCurrent ? "Anahtar yalnizca UI oturumunda tutuluyor." : "Kayitli anahtar yok.";
    public bool KeySaveRemoveEnabled => SelectedProfile is { RequiresApiKey: true };
    public bool ShowDisabledBanner => SelectedProfile is { IsEnabled: false };
    public string DisabledBannerText => ShowDisabledBanner ? "Profil devre disi. Yeni runtime baglandiginda etkinlestirilecek." : string.Empty;
    public bool ShowNoKeyMessage => SelectedProfile is { RequiresApiKey: false };
    public bool ShowSavedKeyRow => SelectedProfile is { RequiresApiKey: true };
    public Visibility KeyAreaVisibility => ShowSavedKeyRow ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DisabledBannerVisibility => ShowDisabledBanner ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoKeyMessageVisibility => ShowNoKeyMessage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SavedKeyRowVisibility => ShowSavedKeyRow ? Visibility.Visible : Visibility.Collapsed;

    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set => SetField(ref _apiKeyInput, value ?? string.Empty);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public ConnectionTestSummary? LastConnectionTest
    {
        get => _lastConnectionTest;
        private set
        {
            if (SetField(ref _lastConnectionTest, value))
            {
                OnPropertyChanged(nameof(HasLastConnectionTest));
            }
        }
    }

    public bool HasLastConnectionTest => LastConnectionTest is not null;

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        private set => SetField(ref _isTestingConnection, value);
    }

    public ProviderProfileDraft? EditingDraft
    {
        get => _editingDraft;
        private set
        {
            if (SetField(ref _editingDraft, value))
            {
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(EditingTitle));
            }
        }
    }

    public bool IsEditing => EditingDraft is not null;
    public string EditingTitle => _isEditingNew ? "Yeni profil" : "Profili duzenle";

    public void Load()
    {
        RebuildProfileItems();
        SelectedProfile ??= Profiles.FirstOrDefault();
        SyncProviderStatus();
    }

    public Task SetActiveAsync()
    {
        if (SelectedProfile is not { IsEnabled: true })
        {
            StatusMessage = "Etkin bir profil secin.";
            return Task.CompletedTask;
        }

        _activeProfileId = SelectedProfile.Id;
        RefreshFlags();
        SyncProviderStatus();
        NotifySelectionChanged();
        StatusMessage = "Profil yalnizca frontend oturumu icin aktif edildi.";
        return Task.CompletedTask;
    }

    public Task SaveKeyAsync()
    {
        if (SelectedProfile is not { RequiresApiKey: true } || string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            StatusMessage = "UI oturumu icin bir API anahtari girin.";
            return Task.CompletedTask;
        }

        _savedKeys.Add(SelectedProfile.Id);
        ApiKeyInput = string.Empty;
        RefreshFlags();
        SyncProviderStatus();
        NotifySelectionChanged();
        StatusMessage = "Anahtar yalnizca bellekte tutuluyor; kalici backend kaldirildi.";
        return Task.CompletedTask;
    }

    public Task RemoveKeyAsync()
    {
        if (SelectedProfile is not null)
        {
            _savedKeys.Remove(SelectedProfile.Id);
        }

        RefreshFlags();
        SyncProviderStatus();
        NotifySelectionChanged();
        StatusMessage = "Gecici anahtar kaldirildi.";
        return Task.CompletedTask;
    }

    public Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        IsTestingConnection = true;
        LastConnectionTest = new ConnectionTestSummary();
        StatusMessage = LastConnectionTest.Message;
        IsTestingConnection = false;
        return Task.CompletedTask;
    }

    public void BeginNewProfile()
    {
        _isEditingNew = true;
        EditingDraft = new ProviderProfileDraft { Id = SuggestNewId() };
    }

    public void BeginEditCurrent()
    {
        if (!IsCurrentUserDefined || SelectedProfile is null)
        {
            StatusMessage = "Yerlesik placeholder profil duzenlenemez.";
            return;
        }

        _isEditingNew = false;
        EditingDraft = ProviderProfileDraft.FromProfile(SelectedProfile);
    }

    public void CancelEdit()
    {
        EditingDraft = null;
        _isEditingNew = false;
    }

    public ProviderProfileSaveResult SaveDraft()
    {
        if (EditingDraft is null)
        {
            return ProviderProfileSaveResult.Fail("Aktif duzenleme yok.");
        }

        var profile = EditingDraft.ToProfile();
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            return ProviderProfileSaveResult.Fail("Profil kimligi ve gorunen ad gereklidir.");
        }

        if (_isEditingNew && Profiles.Any(item => item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return ProviderProfileSaveResult.Fail("Bu kimlige sahip bir profil zaten var.");
        }

        var existing = Profiles.FirstOrDefault(item => item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Profiles[Profiles.IndexOf(existing)] = profile;
        }
        else
        {
            Profiles.Add(profile);
        }

        EditingDraft = null;
        _isEditingNew = false;
        RebuildProfileItems();
        SelectedProfile = Profiles.First(item => item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
        StatusMessage = "Profil yalnizca frontend oturumunda kaydedildi.";
        return ProviderProfileSaveResult.Ok();
    }

    public ProviderProfileSaveResult DeleteCurrent()
    {
        if (!IsCurrentUserDefined || SelectedProfile is null)
        {
            return ProviderProfileSaveResult.Fail("Bu profil silinemez.");
        }

        var removedId = SelectedProfile.Id;
        Profiles.Remove(SelectedProfile);
        _savedKeys.Remove(removedId);
        if (removedId.Equals(_activeProfileId, StringComparison.OrdinalIgnoreCase))
        {
            _activeProfileId = null;
        }

        RebuildProfileItems();
        SelectedProfile = Profiles.FirstOrDefault();
        SyncProviderStatus();
        StatusMessage = "Gecici profil silindi.";
        return ProviderProfileSaveResult.Ok();
    }

    public void RefreshSavedKeyStatus()
    {
        NotifySelectionChanged();
    }

    public bool IsProfileActive(string profileId) =>
        !string.IsNullOrWhiteSpace(profileId) &&
        profileId.Equals(EffectiveActiveProfileId, StringComparison.OrdinalIgnoreCase);

    public bool ProfileHasSavedKey(string profileId) => _savedKeys.Contains(profileId);

    private void RebuildProfileItems()
    {
        ProfileItems.Clear();
        foreach (var profile in Profiles)
        {
            ProfileItems.Add(new ProfileListItem(profile, profile.Id.Equals(PlaceholderProfileId, StringComparison.OrdinalIgnoreCase))
            {
                IsActive = IsProfileActive(profile.Id),
                HasSavedKey = ProfileHasSavedKey(profile.Id)
            });
        }
    }

    private void RefreshFlags()
    {
        foreach (var item in ProfileItems)
        {
            item.IsActive = IsProfileActive(item.Id);
            item.HasSavedKey = ProfileHasSavedKey(item.Id);
        }
    }

    private void SyncProviderStatus()
    {
        var active = Profiles.FirstOrDefault(profile => IsProfileActive(profile.Id));
        _providerStatus.SetProfile(active, active is not null && ProfileHasSavedKey(active.Id));
    }

    private string SuggestNewId()
    {
        for (var i = 1; i < 1000; i++)
        {
            var candidate = i == 1 ? "custom-provider" : $"custom-provider-{i}";
            if (Profiles.All(profile => !profile.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return $"custom-provider-{Guid.NewGuid():N}";
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(EffectiveActiveProfileId));
        OnPropertyChanged(nameof(ActiveProfileCaption));
        OnPropertyChanged(nameof(IsCurrentActive));
        OnPropertyChanged(nameof(IsCurrentBuiltIn));
        OnPropertyChanged(nameof(IsCurrentUserDefined));
        OnPropertyChanged(nameof(SetActiveButtonText));
        OnPropertyChanged(nameof(SetActiveEnabled));
        OnPropertyChanged(nameof(TestConnectionEnabled));
        OnPropertyChanged(nameof(EditEnabled));
        OnPropertyChanged(nameof(DeleteEnabled));
        OnPropertyChanged(nameof(DetailDisplayName));
        OnPropertyChanged(nameof(DetailKind));
        OnPropertyChanged(nameof(DetailBaseUrl));
        OnPropertyChanged(nameof(DetailModel));
        OnPropertyChanged(nameof(DetailEndpointPath));
        OnPropertyChanged(nameof(DetailRequiresApiKey));
        OnPropertyChanged(nameof(DetailIsEnabled));
        OnPropertyChanged(nameof(DetailOriginLabel));
        OnPropertyChanged(nameof(HasSavedKeyForCurrent));
        OnPropertyChanged(nameof(SavedKeyMask));
        OnPropertyChanged(nameof(SavedKeyStatus));
        OnPropertyChanged(nameof(KeySaveRemoveEnabled));
        OnPropertyChanged(nameof(ShowDisabledBanner));
        OnPropertyChanged(nameof(DisabledBannerText));
        OnPropertyChanged(nameof(ShowNoKeyMessage));
        OnPropertyChanged(nameof(ShowSavedKeyRow));
        OnPropertyChanged(nameof(KeyAreaVisibility));
        OnPropertyChanged(nameof(DisabledBannerVisibility));
        OnPropertyChanged(nameof(NoKeyMessageVisibility));
        OnPropertyChanged(nameof(SavedKeyRowVisibility));
    }
}
