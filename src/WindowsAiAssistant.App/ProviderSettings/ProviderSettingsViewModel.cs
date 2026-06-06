using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App.ProviderSettings;

public sealed class ProviderSettingsViewModel : ObservableObject
{
    private readonly ActiveProviderStatus _providerStatus;
    private readonly IProviderConfigurationService _configurationService;
    private readonly ProviderConnectionTester _connectionTester;
    private readonly INavigationService _navigation;
    private ProviderProfile? _selectedProfile;
    private ProviderProfileDraft? _editingDraft;
    private ConnectionTestSummary? _lastConnectionTest;
    private string _statusMessage = "Sağlayıcı profilleri yükleniyor...";
    private string _apiKeyInput = string.Empty;
    private bool _isEditingNew;
    private bool _isTestingConnection;

    public ProviderSettingsViewModel(
        ActiveProviderStatus providerStatus,
        IProviderConfigurationService configurationService,
        ProviderConnectionTester connectionTester,
        INavigationService navigation)
    {
        _providerStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Profiles = [];
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

    public string EffectiveActiveProfileId => _configurationService.ActiveProfileId ?? string.Empty;
    public string ActiveProfileCaption => string.IsNullOrWhiteSpace(EffectiveActiveProfileId) ? "(yok)" : EffectiveActiveProfileId;
    public bool IsCurrentActive => SelectedProfile is not null && SelectedProfile.Id.Equals(EffectiveActiveProfileId, StringComparison.OrdinalIgnoreCase);
    public bool IsCurrentBuiltIn => SelectedProfile?.IsBuiltIn == true;
    public bool IsCurrentUserDefined => SelectedProfile is not null && !IsCurrentBuiltIn;
    public string SetActiveButtonText => IsCurrentActive ? "Aktif" : "Aktif Yap";
    public bool SetActiveEnabled => SelectedProfile is { IsEnabled: true };
    public bool TestConnectionEnabled => SelectedProfile is not null && !IsTestingConnection;
    public bool EditEnabled => SelectedProfile is not null;
    public bool DeleteEnabled => IsCurrentUserDefined;

    public string DetailDisplayName => SelectedProfile?.DisplayName ?? "-";
    public string DetailKind => SelectedProfile?.Kind.ToString() ?? "-";
    public string DetailBaseUrl => SelectedProfile?.BaseUrl ?? "-";
    public string DetailModel => SelectedProfile?.Model ?? "-";
    public string DetailEndpointPath => SelectedProfile?.EndpointPath ?? "-";
    public string DetailRequiresApiKey => SelectedProfile is null ? "-" : SelectedProfile.RequiresApiKey ? "Evet" : "Hayır";
    public string DetailIsEnabled => SelectedProfile is null ? "-" : SelectedProfile.IsEnabled ? "Evet" : "Hayır";
    public string DetailTemperature => SelectedProfile?.Temperature?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(varsayılan)";
    public string DetailMaxTokens => SelectedProfile?.MaxTokens?.ToString() ?? "(varsayılan)";
    public string DetailVisionEnabled => SelectedProfile is null ? "-" : SelectedProfile.VisionEnabled ? "Evet" : "Hayır";
    public string DetailOriginLabel => SelectedProfile is null ? "-" : IsCurrentBuiltIn ? "Yerleşik profil" : "Özel profil";

    public bool HasSavedKeyForCurrent =>
        SelectedProfile is not null && _configurationService.HasSavedApiKey(SelectedProfile.Id);

    public bool HasEnvKeyForCurrent =>
        SelectedProfile is not null &&
        !string.IsNullOrWhiteSpace(SelectedProfile.ApiKeyEnvVar) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SelectedProfile.ApiKeyEnvVar));

    public string SavedKeyMask => HasSavedKeyForCurrent ? "********" : string.Empty;
    public string SavedKeyStatus => HasEnvKeyForCurrent
        ? $"Ortam değişkeni ({SelectedProfile?.ApiKeyEnvVar}) mevcut."
        : HasSavedKeyForCurrent
            ? "Anahtar yerel kullanıcı ayarlarında saklanıyor."
            : "Kayıtlı anahtar yok.";
    public bool KeySaveRemoveEnabled => SelectedProfile is { RequiresApiKey: true };
    public bool ShowDisabledBanner => SelectedProfile is { IsEnabled: false };
    public string DisabledBannerText => ShowDisabledBanner ? "Profil devre dışı. Aktif yapmadan önce etkinleştirin." : string.Empty;
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
                OnPropertyChanged(nameof(CanTryInAssistant));
            }
        }
    }

    public bool HasLastConnectionTest => LastConnectionTest is not null;

    public bool CanTryInAssistant =>
        LastConnectionTest?.Status == ProviderConnectionTestStatus.Success && IsCurrentActive;

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        private set
        {
            if (SetField(ref _isTestingConnection, value))
            {
                OnPropertyChanged(nameof(TestConnectionEnabled));
            }
        }
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
    public string EditingTitle => _isEditingNew ? "Yeni profil" : "Profili düzenle";

    public void Load()
    {
        ReloadProfilesFromService();
        SelectedProfile ??= Profiles.FirstOrDefault(profile =>
            profile.Id.Equals(_configurationService.ActiveProfileId, StringComparison.OrdinalIgnoreCase)) ??
            Profiles.FirstOrDefault();
        SyncProviderStatus();
        StatusMessage = "Sağlayıcı profilleri yüklendi. Aktif profili seçin veya düzenleyin.";
    }

    public Task SetActiveAsync()
    {
        if (SelectedProfile is not { IsEnabled: true })
        {
            StatusMessage = "Etkin bir profil seçin.";
            return Task.CompletedTask;
        }

        _configurationService.SetActiveProfile(SelectedProfile.Id);
        RefreshFlags();
        SyncProviderStatus();
        NotifySelectionChanged();
        StatusMessage = $"'{SelectedProfile.DisplayName}' aktif sağlayıcı olarak ayarlandı.";
        return Task.CompletedTask;
    }

    public Task SaveKeyAsync()
    {
        if (SelectedProfile is not { RequiresApiKey: true } || string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            StatusMessage = "Kaydetmek için bir API anahtarı girin.";
            return Task.CompletedTask;
        }

        _configurationService.SaveApiKey(SelectedProfile.Id, ApiKeyInput);
        ApiKeyInput = string.Empty;
        RefreshFlags();
        SyncProviderStatus();
        NotifySelectionChanged();
        StatusMessage = "API anahtarı yerel kullanıcı ayarlarına kaydedildi.";
        return Task.CompletedTask;
    }

    public Task RemoveKeyAsync()
    {
        if (SelectedProfile is not null)
        {
            _configurationService.RemoveApiKey(SelectedProfile.Id);
        }

        RefreshFlags();
        SyncProviderStatus();
        NotifySelectionChanged();
        StatusMessage = "Yerel API anahtarı kaldırıldı.";
        return Task.CompletedTask;
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        IsTestingConnection = true;
        StatusMessage = "Bağlantı test ediliyor...";

        try
        {
            LastConnectionTest = await _connectionTester
                .TestProfileAsync(SelectedProfile, cancellationToken)
                .ConfigureAwait(true);
            StatusMessage = LastConnectionTest.Message;
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    public void BeginNewProfile()
    {
        _isEditingNew = true;
        EditingDraft = new ProviderProfileDraft { Id = SuggestNewId() };
    }

    public void BeginFromPreset(string presetKey)
    {
        _isEditingNew = true;
        EditingDraft = presetKey switch
        {
            "openai" => new ProviderProfileDraft
            {
                Id = SuggestNewId("openai"),
                DisplayName = "OpenAI (GPT-4.1)",
                Kind = ModelProviderKind.OpenAICompatible,
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4.1-mini",
                EndpointStyle = ModelEndpointStyle.OpenAiChatCompletions,
                EndpointPath = "chat/completions",
                RequiresApiKey = true,
                ApiKeyEnvVar = "OPENAI_API_KEY",
                ApiKeyHeaderName = "Authorization",
                AuthScheme = ModelAuthScheme.Bearer,
                IsEnabled = true
            },
            "gemini" => new ProviderProfileDraft
            {
                Id = SuggestNewId("gemini"),
                DisplayName = "Google Gemini",
                Kind = ModelProviderKind.Gemini,
                BaseUrl = "https://generativelanguage.googleapis.com",
                Model = "gemini-2.0-flash",
                EndpointStyle = ModelEndpointStyle.GeminiGenerateContent,
                EndpointPath = "v1beta/models/{model}:generateContent",
                RequiresApiKey = true,
                ApiKeyEnvVar = "GEMINI_API_KEY",
                ApiKeyHeaderName = "x-goog-api-key",
                AuthScheme = ModelAuthScheme.Raw,
                IsEnabled = true
            },
            "ollama" => new ProviderProfileDraft
            {
                Id = SuggestNewId("ollama"),
                DisplayName = "Ollama (yerel)",
                Kind = ModelProviderKind.Local,
                BaseUrl = "http://localhost:11434/v1",
                Model = "llama3.2",
                EndpointStyle = ModelEndpointStyle.OpenAiChatCompletions,
                EndpointPath = "chat/completions",
                RequiresApiKey = false,
                AuthScheme = ModelAuthScheme.None,
                IsEnabled = true
            },
            "lmstudio" => new ProviderProfileDraft
            {
                Id = SuggestNewId("lmstudio"),
                DisplayName = "LM Studio (yerel)",
                Kind = ModelProviderKind.Local,
                BaseUrl = "http://localhost:1234/v1",
                Model = "local-model",
                EndpointStyle = ModelEndpointStyle.OpenAiChatCompletions,
                EndpointPath = "chat/completions",
                RequiresApiKey = false,
                AuthScheme = ModelAuthScheme.None,
                IsEnabled = true
            },
            _ => new ProviderProfileDraft { Id = SuggestNewId() }
        };
    }

    public void BeginEditCurrent()
    {
        if (SelectedProfile is null)
        {
            StatusMessage = "Düzenlenecek profil seçilmedi.";
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
            return ProviderProfileSaveResult.Fail("Aktif düzenleme yok.");
        }

        var profile = EditingDraft.ToProfile();
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            return ProviderProfileSaveResult.Fail("Profil kimliği ve görünen ad gereklidir.");
        }

        if (_isEditingNew && Profiles.Any(item => item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return ProviderProfileSaveResult.Fail("Bu kimliğe sahip bir profil zaten var.");
        }

        _configurationService.UpsertProfile(profile);
        EditingDraft = null;
        _isEditingNew = false;
        ReloadProfilesFromService();
        SelectedProfile = Profiles.First(item => item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
        StatusMessage = "Profil kaydedildi.";
        return ProviderProfileSaveResult.Ok();
    }

    public ProviderProfileSaveResult DeleteCurrent()
    {
        if (!IsCurrentUserDefined || SelectedProfile is null)
        {
            return ProviderProfileSaveResult.Fail("Bu profil silinemez.");
        }

        var removedId = SelectedProfile.Id;
        try
        {
            _configurationService.DeleteProfile(removedId);
        }
        catch (InvalidOperationException ex)
        {
            return ProviderProfileSaveResult.Fail(ex.Message);
        }

        ReloadProfilesFromService();
        SelectedProfile = Profiles.FirstOrDefault();
        SyncProviderStatus();
        StatusMessage = "Profil silindi.";
        return ProviderProfileSaveResult.Ok();
    }

    public void RefreshSavedKeyStatus()
    {
        NotifySelectionChanged();
    }

    public bool IsProfileActive(string profileId) =>
        !string.IsNullOrWhiteSpace(profileId) &&
        profileId.Equals(EffectiveActiveProfileId, StringComparison.OrdinalIgnoreCase);

    public bool ProfileHasSavedKey(string profileId) => _configurationService.HasSavedApiKey(profileId);

    private void ReloadProfilesFromService()
    {
        Profiles.Clear();
        foreach (var profile in _configurationService.Profiles)
        {
            Profiles.Add(profile);
        }

        RebuildProfileItems();
    }

    private void RebuildProfileItems()
    {
        ProfileItems.Clear();
        foreach (var profile in Profiles)
        {
            ProfileItems.Add(new ProfileListItem(profile, profile.IsBuiltIn)
            {
                IsActive = IsProfileActive(profile.Id),
                HasSavedKey = ProfileHasSavedKey(profile.Id) ||
                              (!string.IsNullOrWhiteSpace(profile.ApiKeyEnvVar) &&
                               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(profile.ApiKeyEnvVar)))
            });
        }
    }

    private void RefreshFlags()
    {
        foreach (var item in ProfileItems)
        {
            item.IsActive = IsProfileActive(item.Id);
            item.HasSavedKey = ProfileHasSavedKey(item.Id) ||
                               (!string.IsNullOrWhiteSpace(item.Profile.ApiKeyEnvVar) &&
                                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(item.Profile.ApiKeyEnvVar)));
        }
    }

    private void SyncProviderStatus()
    {
        var active = Profiles.FirstOrDefault(profile => IsProfileActive(profile.Id)) ??
                     _configurationService.ActiveProfile;
        _providerStatus.SetProfile(
            active,
            active is not null && (_configurationService.HasSavedApiKey(active.Id) ||
                                   (!string.IsNullOrWhiteSpace(active.ApiKeyEnvVar) &&
                                    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(active.ApiKeyEnvVar)))));
    }

    private string SuggestNewId(string? prefix = null)
    {
        prefix ??= "custom-provider";
        for (var i = 1; i < 1000; i++)
        {
            var candidate = i == 1 ? prefix : $"{prefix}-{i}";
            if (Profiles.All(profile => !profile.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return $"custom-provider-{Guid.NewGuid():N}";
    }

    public void NavigateToAssistant() => _navigation.NavigateToAssistant();

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
        OnPropertyChanged(nameof(DetailTemperature));
        OnPropertyChanged(nameof(DetailMaxTokens));
        OnPropertyChanged(nameof(DetailVisionEnabled));
        OnPropertyChanged(nameof(DetailOriginLabel));
        OnPropertyChanged(nameof(HasSavedKeyForCurrent));
        OnPropertyChanged(nameof(HasEnvKeyForCurrent));
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
        OnPropertyChanged(nameof(CanTryInAssistant));
    }
}
