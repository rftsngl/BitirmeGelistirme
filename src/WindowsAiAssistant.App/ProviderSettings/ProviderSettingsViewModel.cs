using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.Infrastructure;
using WindowsAiAssistant.Infrastructure.Secrets;
using WindowsAiAssistant.Infrastructure.State;

namespace WindowsAiAssistant.App.ProviderSettings;

public sealed class ProviderSettingsViewModel : INotifyPropertyChanged
{
    private readonly ModelDecisionSettings _settings;
    private readonly IActiveProfileStore _activeProfileStore;
    private readonly IModelProviderSecretStore _secretStore;
    private readonly RefreshableModelDecisionProvider _refreshable;
    private readonly ProviderConnectionTester _connectionTester;
    private readonly IUserProviderProfileStore _userProfileStore;
    private readonly IBuiltInProfileSet _builtInProfileSet;
    private readonly IActiveProviderStatus _providerStatus;

    private ModelDecisionProfile? _selectedProfile;
    private string _statusMessage = string.Empty;
    private string _apiKeyInput = string.Empty;
    private ProviderProfileDraft? _editingDraft;
    private bool _isEditingNew;
    private ConnectionTestSummary? _lastConnectionTest;
    private bool _isTestingConnection;

    public ProviderSettingsViewModel(
        ModelDecisionSettings settings,
        IActiveProfileStore activeProfileStore,
        IModelProviderSecretStore secretStore,
        RefreshableModelDecisionProvider refreshable,
        ProviderConnectionTester connectionTester,
        IUserProviderProfileStore userProfileStore,
        IBuiltInProfileSet builtInProfileSet,
        IActiveProviderStatus providerStatus)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _activeProfileStore = activeProfileStore ?? throw new ArgumentNullException(nameof(activeProfileStore));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _refreshable = refreshable ?? throw new ArgumentNullException(nameof(refreshable));
        _connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));
        _userProfileStore = userProfileStore ?? throw new ArgumentNullException(nameof(userProfileStore));
        _builtInProfileSet = builtInProfileSet ?? throw new ArgumentNullException(nameof(builtInProfileSet));
        _providerStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));
        Profiles = new ObservableCollection<ModelDecisionProfile>();
        ProfileItems = new ObservableCollection<ProfileListItem>();
    }

    public IActiveProviderStatus ProviderStatus => _providerStatus;

    public ObservableCollection<ProfileListItem> ProfileItems { get; }

    public ObservableCollection<ModelDecisionProfile> Profiles { get; }

    public ModelDecisionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value))
            {
                return;
            }

            _selectedProfile = value;
            _apiKeyInput = string.Empty;
            _lastConnectionTest = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetailDisplayName));
            OnPropertyChanged(nameof(DetailKind));
            OnPropertyChanged(nameof(DetailBaseUrl));
            OnPropertyChanged(nameof(DetailModel));
            OnPropertyChanged(nameof(DetailEndpointPath));
            OnPropertyChanged(nameof(DetailRequiresApiKey));
            OnPropertyChanged(nameof(DetailIsEnabled));
            OnPropertyChanged(nameof(DetailOriginLabel));
            OnPropertyChanged(nameof(IsCurrentBuiltIn));
            OnPropertyChanged(nameof(IsCurrentUserDefined));
            OnPropertyChanged(nameof(IsCurrentActive));
            OnPropertyChanged(nameof(KeySaveRemoveEnabled));
            OnPropertyChanged(nameof(SetActiveEnabled));
            OnPropertyChanged(nameof(SetActiveButtonText));
            OnPropertyChanged(nameof(TestConnectionEnabled));
            OnPropertyChanged(nameof(EditEnabled));
            OnPropertyChanged(nameof(DeleteEnabled));
            OnPropertyChanged(nameof(SavedKeyStatus));
            OnPropertyChanged(nameof(SavedKeyMask));
            OnPropertyChanged(nameof(HasSavedKeyForCurrent));
            OnPropertyChanged(nameof(DisabledBannerText));
            OnPropertyChanged(nameof(ShowDisabledBanner));
            OnPropertyChanged(nameof(ShowNoKeyMessage));
            OnPropertyChanged(nameof(ShowSavedKeyRow));
            OnPropertyChanged(nameof(DisabledBannerVisibility));
            OnPropertyChanged(nameof(NoKeyMessageVisibility));
            OnPropertyChanged(nameof(SavedKeyRowVisibility));
            OnPropertyChanged(nameof(KeyAreaVisibility));
            OnPropertyChanged(nameof(LastConnectionTest));
            OnPropertyChanged(nameof(HasLastConnectionTest));
        }
    }

    public bool IsCurrentActive =>
        SelectedProfile is not null &&
        SelectedProfile.Id.Equals(EffectiveActiveProfileId, StringComparison.OrdinalIgnoreCase);

    public string SetActiveButtonText =>
        IsCurrentActive ? "Aktif (seçili)" : "Aktif Yap";

    public bool HasSavedKeyForCurrent =>
        SelectedProfile is { RequiresApiKey: true } &&
        _secretStore.HasApiKey(SelectedProfile.Id);

    public string SavedKeyMask
    {
        get
        {
            if (SelectedProfile is null || !SelectedProfile.RequiresApiKey)
            {
                return string.Empty;
            }

            if (!_secretStore.TryGetApiKey(SelectedProfile.Id, out var key) || string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return MaskKey(key!);
        }
    }

    public ConnectionTestSummary? LastConnectionTest
    {
        get => _lastConnectionTest;
        private set
        {
            if (ReferenceEquals(_lastConnectionTest, value))
            {
                return;
            }

            _lastConnectionTest = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLastConnectionTest));
        }
    }

    public bool HasLastConnectionTest => _lastConnectionTest is not null;

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        private set
        {
            if (_isTestingConnection == value)
            {
                return;
            }

            _isTestingConnection = value;
            OnPropertyChanged();
        }
    }

    public Visibility KeyAreaVisibility =>
        SelectedProfile?.RequiresApiKey == true ? Visibility.Visible : Visibility.Collapsed;

    public bool KeySaveRemoveEnabled =>
        SelectedProfile is { RequiresApiKey: true };

    public Visibility DisabledBannerVisibility =>
        ShowDisabledBanner ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowDisabledBanner =>
        SelectedProfile is { IsEnabled: false };

    public Visibility NoKeyMessageVisibility =>
        ShowNoKeyMessage ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowNoKeyMessage =>
        SelectedProfile is { RequiresApiKey: false };

    public Visibility SavedKeyRowVisibility =>
        ShowSavedKeyRow ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowSavedKeyRow =>
        SelectedProfile?.RequiresApiKey == true;

    /// <summary>
    /// Effective active profile: user override file, else appsettings default.
    /// </summary>
    public string EffectiveActiveProfileId =>
        _activeProfileStore.GetActiveProfileId() ?? _settings.ActiveProfile ?? string.Empty;

    public string ActiveProfileCaption =>
        string.IsNullOrWhiteSpace(EffectiveActiveProfileId)
            ? "(yok)"
            : EffectiveActiveProfileId;

    public string DetailDisplayName => SelectedProfile?.DisplayName ?? "—";
    public string DetailKind => SelectedProfile?.Kind.ToString() ?? "—";
    public string DetailBaseUrl => SelectedProfile?.BaseUrl ?? "—";
    public string DetailModel => SelectedProfile?.Model ?? "—";
    public string DetailEndpointPath => SelectedProfile?.EndpointPath ?? "—";
    public string DetailRequiresApiKey => SelectedProfile is null ? "—" : SelectedProfile.RequiresApiKey ? "Evet" : "Hayır";
    public string DetailIsEnabled => SelectedProfile is null ? "—" : SelectedProfile.IsEnabled ? "Evet" : "Hayır";

    public string DetailOriginLabel =>
        SelectedProfile is null ? "—" :
        IsCurrentBuiltIn ? "Yerleşik (uygulama ile birlikte gelir)" : "Kullanıcı tanımlı";

    public bool IsCurrentBuiltIn =>
        SelectedProfile is not null && _builtInProfileSet.IsBuiltIn(SelectedProfile.Id);

    public bool IsCurrentUserDefined =>
        SelectedProfile is not null && !IsCurrentBuiltIn;

    public bool SetActiveEnabled =>
        SelectedProfile is { IsEnabled: true };

    public bool TestConnectionEnabled =>
        SelectedProfile is { IsEnabled: true };

    public bool EditEnabled => IsCurrentUserDefined;

    public bool DeleteEnabled => IsCurrentUserDefined;

    public string SavedKeyStatus
    {
        get
        {
            if (SelectedProfile is null || !SelectedProfile.RequiresApiKey)
            {
                return string.Empty;
            }

            return _secretStore.HasApiKey(SelectedProfile.Id)
                ? "Kayıtlı anahtar mevcut."
                : "Kayıtlı anahtar yok.";
        }
    }

    public string DisabledBannerText =>
        SelectedProfile is { IsEnabled: false }
            ? "Profil devre dışı. Düzenleyerek etkinleştirebilirsiniz."
            : string.Empty;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public ProviderProfileDraft? EditingDraft
    {
        get => _editingDraft;
        private set
        {
            if (ReferenceEquals(_editingDraft, value))
            {
                return;
            }

            _editingDraft = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(EditingTitle));
        }
    }

    public bool IsEditing => EditingDraft is not null;

    public string EditingTitle => _isEditingNew ? "Yeni profil" : "Profili düzenle";

    /// <summary>
    /// Buffer for API key input (cleared after successful save). Not persisted in VM across sessions.
    /// </summary>
    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set
        {
            if (_apiKeyInput == value)
            {
                return;
            }

            _apiKeyInput = value;
            OnPropertyChanged();
        }
    }

    public void Load()
    {
        Profiles.Clear();
        foreach (var p in _settings.Profiles ?? [])
        {
            Profiles.Add(p);
        }

        RebuildProfileItems();

        var effectiveId = EffectiveActiveProfileId;
        SelectedProfile = Profiles.FirstOrDefault(x =>
                           x.Id.Equals(effectiveId, StringComparison.OrdinalIgnoreCase))
                       ?? Profiles.FirstOrDefault();

        OnPropertyChanged(nameof(EffectiveActiveProfileId));
        OnPropertyChanged(nameof(ActiveProfileCaption));
        StatusMessage = string.Empty;
    }

    private void RebuildProfileItems()
    {
        ProfileItems.Clear();
        foreach (var profile in Profiles)
        {
            ProfileItems.Add(new ProfileListItem(profile)
            {
                IsActive = IsProfileActive(profile.Id),
                HasSavedKey = ProfileHasSavedKey(profile.Id),
                IsBuiltIn = _builtInProfileSet.IsBuiltIn(profile.Id)
            });
        }
    }

    public ProfileListItem? FindListItem(string profileId) =>
        ProfileItems.FirstOrDefault(item =>
            item.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));

    private void RefreshListItemFlags()
    {
        var activeId = EffectiveActiveProfileId;
        foreach (var item in ProfileItems)
        {
            item.IsActive = !string.IsNullOrWhiteSpace(activeId) &&
                            item.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase);
            item.HasSavedKey = ProfileHasSavedKey(item.Id);
        }
    }

    public Task SetActiveAsync()
    {
        if (SelectedProfile is null || !SelectedProfile.IsEnabled)
        {
            StatusMessage = "Etkin bir profil seçin.";
            return Task.CompletedTask;
        }

        _activeProfileStore.SetActiveProfileId(SelectedProfile.Id);
        _refreshable.Refresh();
        _providerStatus.Refresh();
        RefreshListItemFlags();
        OnPropertyChanged(nameof(EffectiveActiveProfileId));
        OnPropertyChanged(nameof(ActiveProfileCaption));
        OnPropertyChanged(nameof(IsCurrentActive));
        OnPropertyChanged(nameof(SetActiveButtonText));
        StatusMessage = "Aktif profil güncellendi.";
        return Task.CompletedTask;
    }

    public Task SaveKeyAsync()
    {
        if (SelectedProfile is null || !SelectedProfile.RequiresApiKey)
        {
            StatusMessage = "Bu profil için API anahtarı gerekmiyor.";
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            StatusMessage = "Kaydetmeden önce API anahtarını girin.";
            return Task.CompletedTask;
        }

        _secretStore.SaveApiKey(SelectedProfile.Id, ApiKeyInput.Trim());
        ApiKeyInput = string.Empty;
        _providerStatus.Refresh();
        RefreshListItemFlags();
        OnPropertyChanged(nameof(SavedKeyStatus));
        OnPropertyChanged(nameof(SavedKeyMask));
        OnPropertyChanged(nameof(HasSavedKeyForCurrent));
        OnPropertyChanged(nameof(SavedKeyRowVisibility));
        StatusMessage = "Anahtar kaydedildi.";
        return Task.CompletedTask;
    }

    public Task RemoveKeyAsync()
    {
        if (SelectedProfile is null || !SelectedProfile.RequiresApiKey)
        {
            StatusMessage = "Bu profil için kayıtlı anahtar bulunmuyor.";
            return Task.CompletedTask;
        }

        _secretStore.DeleteApiKey(SelectedProfile.Id);
        _providerStatus.Refresh();
        RefreshListItemFlags();
        OnPropertyChanged(nameof(SavedKeyStatus));
        OnPropertyChanged(nameof(SavedKeyMask));
        OnPropertyChanged(nameof(HasSavedKeyForCurrent));
        OnPropertyChanged(nameof(SavedKeyRowVisibility));
        StatusMessage = "Kayıtlı anahtar silindi.";
        return Task.CompletedTask;
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile is null || !SelectedProfile.IsEnabled)
        {
            StatusMessage = "Test için etkin bir profil seçin.";
            return;
        }

        IsTestingConnection = true;
        try
        {
            StatusMessage = "Test ediliyor…";
            var startedAt = DateTimeOffset.Now;
            var timeoutMs = _settings.TimeoutMilliseconds > 0 ? _settings.TimeoutMilliseconds : 10000;
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);

            var result = await _connectionTester.TestAsync(SelectedProfile, timeout, cancellationToken).ConfigureAwait(true);
            var elapsed = DateTimeOffset.Now - startedAt;

            LastConnectionTest = new ConnectionTestSummary
            {
                Status = result.Status,
                Message = result.UserMessage,
                CompletedAt = DateTimeOffset.Now,
                Duration = elapsed
            };
            StatusMessage = result.UserMessage;
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    public void RefreshSavedKeyStatus()
    {
        OnPropertyChanged(nameof(SavedKeyStatus));
        OnPropertyChanged(nameof(SavedKeyMask));
        OnPropertyChanged(nameof(HasSavedKeyForCurrent));
    }

    public bool IsProfileActive(string profileId) =>
        !string.IsNullOrWhiteSpace(profileId) &&
        profileId.Equals(EffectiveActiveProfileId, StringComparison.OrdinalIgnoreCase);

    public bool ProfileHasSavedKey(string profileId) =>
        !string.IsNullOrWhiteSpace(profileId) && _secretStore.HasApiKey(profileId);

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var visibleChars = key.Length >= 4 ? 4 : key.Length;
        var suffix = key[^visibleChars..];
        return $"••••••••{suffix}";
    }

    public void BeginNewProfile()
    {
        _isEditingNew = true;
        EditingDraft = new ProviderProfileDraft
        {
            Id = SuggestNewId()
        };
        StatusMessage = string.Empty;
    }

    public void BeginEditCurrent()
    {
        if (SelectedProfile is null || !IsCurrentUserDefined)
        {
            StatusMessage = "Yalnızca kullanıcı tanımlı profiller düzenlenebilir.";
            return;
        }

        _isEditingNew = false;
        EditingDraft = ProviderProfileDraft.FromProfile(SelectedProfile);
        StatusMessage = string.Empty;
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
            return ProviderProfileSaveResult.Fail("Aktif düzenleme yok.");
        }

        var draft = EditingDraft;
        if (string.IsNullOrWhiteSpace(draft.Id))
        {
            return ProviderProfileSaveResult.Fail("Profil kimliği gerekli.");
        }

        if (string.IsNullOrWhiteSpace(draft.DisplayName))
        {
            return ProviderProfileSaveResult.Fail("Görünen ad gerekli.");
        }

        var trimmedId = draft.Id.Trim();
        if (_isEditingNew && _builtInProfileSet.IsBuiltIn(trimmedId))
        {
            return ProviderProfileSaveResult.Fail("Bu kimlik yerleşik bir profile ait. Farklı bir kimlik seçin.");
        }

        if (_isEditingNew && _settings.Profiles.Any(p => p.Id.Equals(trimmedId, StringComparison.OrdinalIgnoreCase)))
        {
            return ProviderProfileSaveResult.Fail("Bu kimliğe sahip bir profil zaten var.");
        }

        var profile = draft.ToProfile();
        _userProfileStore.Save(profile);
        UpsertInRuntime(profile);
        _refreshable.Refresh();
        _providerStatus.Refresh();
        RebuildProfileItems();
        SelectedProfile = Profiles.FirstOrDefault(p =>
            p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
        EditingDraft = null;
        _isEditingNew = false;
        StatusMessage = "Profil kaydedildi.";
        return ProviderProfileSaveResult.Ok();
    }

    public ProviderProfileSaveResult DeleteCurrent()
    {
        if (SelectedProfile is null)
        {
            return ProviderProfileSaveResult.Fail("Önce silmek istediğiniz profili seçin.");
        }

        if (!IsCurrentUserDefined)
        {
            return ProviderProfileSaveResult.Fail("Yalnızca kullanıcı tanımlı profiller silinebilir.");
        }

        var idToRemove = SelectedProfile.Id;
        _userProfileStore.Delete(idToRemove);
        if (_secretStore.HasApiKey(idToRemove))
        {
            _secretStore.DeleteApiKey(idToRemove);
        }

        if (string.Equals(EffectiveActiveProfileId, idToRemove, StringComparison.OrdinalIgnoreCase))
        {
            _activeProfileStore.ClearOverride();
        }

        RemoveFromRuntime(idToRemove);
        _refreshable.Refresh();
        _providerStatus.Refresh();
        RebuildProfileItems();
        SelectedProfile = Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(EffectiveActiveProfileId));
        OnPropertyChanged(nameof(ActiveProfileCaption));
        StatusMessage = "Profil silindi.";
        return ProviderProfileSaveResult.Ok();
    }

    private void UpsertInRuntime(ModelDecisionProfile profile)
    {
        var index = _settings.Profiles.FindIndex(p =>
            p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _settings.Profiles[index] = profile;
        }
        else
        {
            _settings.Profiles.Add(profile);
        }

        var observableIndex = -1;
        for (var i = 0; i < Profiles.Count; i++)
        {
            if (Profiles[i].Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
            {
                observableIndex = i;
                break;
            }
        }

        if (observableIndex >= 0)
        {
            Profiles[observableIndex] = profile;
        }
        else
        {
            Profiles.Add(profile);
        }
    }

    private void RemoveFromRuntime(string profileId)
    {
        _settings.Profiles.RemoveAll(p =>
            p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        for (var i = Profiles.Count - 1; i >= 0; i--)
        {
            if (Profiles[i].Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            {
                Profiles.RemoveAt(i);
            }
        }
    }

    private string SuggestNewId()
    {
        var baseId = "custom-provider";
        var existing = new HashSet<string>(
            _settings.Profiles.Select(p => p.Id),
            StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseId))
        {
            return baseId;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseId}-{i}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseId}-{Guid.NewGuid():N}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ProviderProfileSaveResult
{
    public bool Success { get; private init; }
    public string Message { get; private init; } = string.Empty;

    public static ProviderProfileSaveResult Ok() => new() { Success = true };
    public static ProviderProfileSaveResult Fail(string message) => new() { Success = false, Message = message };
}

public sealed class ConnectionTestSummary
{
    public ProviderConnectionTestStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.Now;
    public TimeSpan Duration { get; init; }

    public bool IsSuccess => Status == ProviderConnectionTestStatus.Success;

    public string Severity => Status switch
    {
        ProviderConnectionTestStatus.Success => "Success",
        ProviderConnectionTestStatus.MissingKey => "Warning",
        ProviderConnectionTestStatus.Timeout => "Warning",
        ProviderConnectionTestStatus.InvalidResponse => "Warning",
        _ => "Error"
    };

    public string Title => Status switch
    {
        ProviderConnectionTestStatus.Success => "Bağlantı başarılı",
        ProviderConnectionTestStatus.MissingKey => "API anahtarı eksik",
        ProviderConnectionTestStatus.Timeout => "Zaman aşımı",
        ProviderConnectionTestStatus.HttpError => "HTTP hatası",
        ProviderConnectionTestStatus.InvalidResponse => "Geçersiz yanıt",
        ProviderConnectionTestStatus.NetworkFailure => "Ağ hatası",
        _ => "Bağlantı başarısız"
    };

    public string CompletedDisplay => CompletedAt.ToLocalTime().ToString("HH:mm:ss");
    public string DurationDisplay => $"{(int)Duration.TotalMilliseconds} ms";
}
