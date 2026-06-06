using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.App.ProviderSettings;

public sealed class ProviderSettingsDocument
{
    public string? ActiveProfileId { get; set; }
    public List<ProviderProfileRecord> Profiles { get; set; } = [];
    public Dictionary<string, string> ApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProviderProfileRecord
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ModelProviderKind Kind { get; set; } = ModelProviderKind.OpenAICompatible;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public ModelEndpointStyle EndpointStyle { get; set; } = ModelEndpointStyle.OpenAiChatCompletions;
    public string EndpointPath { get; set; } = "chat/completions";
    public bool RequiresApiKey { get; set; } = true;
    public string ApiKeyEnvVar { get; set; } = string.Empty;
    public string ApiKeyHeaderName { get; set; } = "Authorization";
    public ModelAuthScheme AuthScheme { get; set; } = ModelAuthScheme.Bearer;
    public bool IsEnabled { get; set; } = true;
    public bool IsBuiltIn { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public bool VisionEnabled { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 120;

    public ProviderProfile ToProfile() =>
        new()
        {
            Id = Id,
            DisplayName = DisplayName,
            Kind = Kind,
            BaseUrl = BaseUrl,
            Model = Model,
            EndpointStyle = EndpointStyle,
            EndpointPath = EndpointPath,
            RequiresApiKey = RequiresApiKey,
            ApiKeyEnvVar = ApiKeyEnvVar,
            ApiKeyHeaderName = ApiKeyHeaderName,
            AuthScheme = AuthScheme,
            IsEnabled = IsEnabled,
            IsBuiltIn = IsBuiltIn,
            Temperature = Temperature,
            MaxTokens = MaxTokens,
            VisionEnabled = VisionEnabled,
            RequestTimeoutSeconds = RequestTimeoutSeconds
        };

    public static ProviderProfileRecord FromProfile(ProviderProfile profile) =>
        new()
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            Kind = profile.Kind,
            BaseUrl = profile.BaseUrl,
            Model = profile.Model,
            EndpointStyle = profile.EndpointStyle,
            EndpointPath = profile.EndpointPath,
            RequiresApiKey = profile.RequiresApiKey,
            ApiKeyEnvVar = profile.ApiKeyEnvVar,
            ApiKeyHeaderName = profile.ApiKeyHeaderName,
            AuthScheme = profile.AuthScheme,
            IsEnabled = profile.IsEnabled,
            IsBuiltIn = profile.IsBuiltIn,
            Temperature = profile.Temperature,
            MaxTokens = profile.MaxTokens,
            VisionEnabled = profile.VisionEnabled,
            RequestTimeoutSeconds = profile.RequestTimeoutSeconds
        };
}

public interface IProviderConfigurationService
{
    IReadOnlyList<ProviderProfile> Profiles { get; }
    string? ActiveProfileId { get; }
    ProviderProfile? ActiveProfile { get; }

    void Initialize();
    void Save();
    void SetActiveProfile(string profileId);
    void UpsertProfile(ProviderProfile profile);
    void DeleteProfile(string profileId);
    bool HasSavedApiKey(string profileId);
    void SaveApiKey(string profileId, string apiKey);
    void RemoveApiKey(string profileId);
    string? ResolveApiKey(ProviderProfile profile);
    ProviderOptions ToRuntimeOptions(ProviderProfile profile);
    void ApplyActiveProfileToRuntime();
    bool IsProfileReady(ProviderProfile profile);
}

public sealed class ProviderConfigurationService : IProviderConfigurationService
{
    public const string AppsettingsDefaultProfileId = "appsettings-default";
    public const string GeminiTemplateProfileId = "gemini-template";
    public const string LocalTemplateProfileId = "local-template";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ProviderOptions _runtimeOptions;
    private readonly AgentOptions _agentOptions;
    private readonly string _settingsPath;
    private ProviderSettingsDocument _document = new();

    public ProviderConfigurationService(ProviderOptions runtimeOptions, AgentOptions agentOptions)
    {
        _runtimeOptions = runtimeOptions ?? throw new ArgumentNullException(nameof(runtimeOptions));
        _agentOptions = agentOptions ?? throw new ArgumentNullException(nameof(agentOptions));
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = Path.Combine(appData, "WindowsAiAssistant", "provider-settings.json");
    }

    public IReadOnlyList<ProviderProfile> Profiles =>
        _document.Profiles.Select(record => record.ToProfile()).ToList();

    public string? ActiveProfileId => _document.ActiveProfileId;

    public ProviderProfile? ActiveProfile =>
        Profiles.FirstOrDefault(profile =>
            profile.Id.Equals(_document.ActiveProfileId, StringComparison.OrdinalIgnoreCase));

    public void Initialize()
    {
        if (File.Exists(_settingsPath))
        {
            LoadFromDisk();
        }
        else
        {
            _document = CreateDefaultDocument();
            Save();
        }

        if (string.IsNullOrWhiteSpace(_document.ActiveProfileId) ||
            Profiles.All(profile => !profile.Id.Equals(_document.ActiveProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            _document.ActiveProfileId = Profiles.FirstOrDefault(profile => profile.IsEnabled)?.Id ??
                                        Profiles.FirstOrDefault()?.Id;
        }

        ApplyActiveProfileToRuntime();
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(_document, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public void SetActiveProfile(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (Profiles.All(profile => !profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Profil bulunamadi: {profileId}");
        }

        _document.ActiveProfileId = profileId;
        Save();
        ApplyActiveProfileToRuntime();
    }

    public void UpsertProfile(ProviderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var record = ProviderProfileRecord.FromProfile(profile);
        var existing = _document.Profiles.FirstOrDefault(item =>
            item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            record.IsBuiltIn = existing.IsBuiltIn;
            _document.Profiles[_document.Profiles.IndexOf(existing)] = record;
        }
        else
        {
            _document.Profiles.Add(record);
        }

        Save();
        if (profile.Id.Equals(_document.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyActiveProfileToRuntime();
        }
    }

    public void DeleteProfile(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var existing = _document.Profiles.FirstOrDefault(item =>
            item.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        if (existing.IsBuiltIn)
        {
            throw new InvalidOperationException("Yerlesik profiller silinemez.");
        }

        _document.Profiles.Remove(existing);
        _document.ApiKeys.Remove(profileId);
        if (profileId.Equals(_document.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
        {
            _document.ActiveProfileId = Profiles.FirstOrDefault(profile => profile.IsEnabled)?.Id ??
                                        Profiles.FirstOrDefault()?.Id;
            ApplyActiveProfileToRuntime();
        }

        Save();
    }

    public bool HasSavedApiKey(string profileId) =>
        _document.ApiKeys.TryGetValue(profileId, out var key) && !string.IsNullOrWhiteSpace(key);

    public void SaveApiKey(string profileId, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _document.ApiKeys[profileId] = SecretProtector.Protect(apiKey.Trim());
        Save();
        if (profileId.Equals(_document.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyActiveProfileToRuntime();
        }
    }

    public void RemoveApiKey(string profileId)
    {
        _document.ApiKeys.Remove(profileId);
        Save();
        if (profileId.Equals(_document.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyActiveProfileToRuntime();
        }
    }

    public string? ResolveApiKey(ProviderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.RequiresApiKey)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.ApiKeyEnvVar))
        {
            var envKey = Environment.GetEnvironmentVariable(profile.ApiKeyEnvVar);
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                return envKey.Trim();
            }
        }

        if (_document.ApiKeys.TryGetValue(profile.Id, out var savedKey) &&
            !string.IsNullOrWhiteSpace(savedKey))
        {
            try
            {
                return SecretProtector.Unprotect(savedKey).Trim();
            }
            catch
            {
                return savedKey.Trim();
            }
        }

        return null;
    }

    public ProviderOptions ToRuntimeOptions(ProviderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var endpoint = profile.EndpointPath.Trim();
        if (!endpoint.StartsWith('/'))
        {
            endpoint = "/" + endpoint;
        }

        return new ProviderOptions
        {
            Provider = MapProviderKind(profile.Kind),
            BaseUrl = profile.BaseUrl.Trim(),
            Endpoint = endpoint,
            Model = profile.Model.Trim(),
            ApiKeyEnvironmentVariable = profile.ApiKeyEnvVar.Trim(),
            RequestTimeoutSeconds = Math.Clamp(profile.RequestTimeoutSeconds, 5, 600),
            RequiresApiKey = profile.RequiresApiKey,
            AuthScheme = profile.AuthScheme.ToString(),
            ApiKeyHeaderName = profile.ApiKeyHeaderName,
            RuntimeApiKey = ResolveApiKey(profile),
            Temperature = profile.Temperature,
            MaxTokens = profile.MaxTokens,
            VisionEnabled = profile.VisionEnabled
        };
    }

    public void ApplyActiveProfileToRuntime()
    {
        var active = ActiveProfile;
        if (active is null)
        {
            return;
        }

        var mapped = ToRuntimeOptions(active);
        CopyToRuntime(mapped);
    }

    public bool IsProfileReady(ProviderProfile profile)
    {
        if (!profile.IsEnabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(profile.BaseUrl) || string.IsNullOrWhiteSpace(profile.Model))
        {
            return false;
        }

        if (profile.RequiresApiKey && string.IsNullOrWhiteSpace(ResolveApiKey(profile)))
        {
            return false;
        }

        return true;
    }

    private void LoadFromDisk()
    {
        var json = File.ReadAllText(_settingsPath);
        _document = JsonSerializer.Deserialize<ProviderSettingsDocument>(json, JsonOptions) ?? new ProviderSettingsDocument();
        _document.Profiles ??= [];
        _document.ApiKeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        EnsureBuiltInProfiles();
        MigratePlaintextApiKeys();
    }

    private void MigratePlaintextApiKeys()
    {
        var migrated = false;
        foreach (var key in _document.ApiKeys.Keys.ToList())
        {
            var value = _document.ApiKeys[key];
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                continue;
            }

            _document.ApiKeys[key] = SecretProtector.Protect(value);
            migrated = true;
        }

        if (migrated)
        {
            Save();
        }
    }

    private ProviderSettingsDocument CreateDefaultDocument()
    {
        var defaults = BuildDefaultProfiles();
        return new ProviderSettingsDocument
        {
            ActiveProfileId = AppsettingsDefaultProfileId,
            Profiles = defaults.Select(ProviderProfileRecord.FromProfile).ToList()
        };
    }

    private void EnsureBuiltInProfiles()
    {
        var defaults = BuildDefaultProfiles();
        foreach (var builtIn in defaults)
        {
            var existing = _document.Profiles.FirstOrDefault(item =>
                item.Id.Equals(builtIn.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _document.Profiles.Add(ProviderProfileRecord.FromProfile(builtIn));
            }
        }
    }

    private IReadOnlyList<ProviderProfile> BuildDefaultProfiles()
    {
        var model = _agentOptions.Model;
        return
        [
            new ProviderProfile
            {
                Id = AppsettingsDefaultProfileId,
                DisplayName = "OpenAI Uyumlu (appsettings)",
                Kind = ModelProviderKind.OpenAICompatible,
                BaseUrl = model.BaseUrl,
                Model = model.Model,
                EndpointStyle = ModelEndpointStyle.OpenAiChatCompletions,
                EndpointPath = model.Endpoint.TrimStart('/'),
                RequiresApiKey = model.RequiresApiKey,
                ApiKeyEnvVar = model.ApiKeyEnvironmentVariable,
                ApiKeyHeaderName = "Authorization",
                AuthScheme = ModelAuthScheme.Bearer,
                IsEnabled = true,
                IsBuiltIn = true,
                RequestTimeoutSeconds = model.RequestTimeoutSeconds
            },
            new ProviderProfile
            {
                Id = GeminiTemplateProfileId,
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
                IsEnabled = false,
                IsBuiltIn = true
            },
            new ProviderProfile
            {
                Id = LocalTemplateProfileId,
                DisplayName = "Yerel (LM Studio / Ollama)",
                Kind = ModelProviderKind.Local,
                BaseUrl = "http://localhost:11434/v1",
                Model = "llama3.2",
                EndpointStyle = ModelEndpointStyle.OpenAiChatCompletions,
                EndpointPath = "chat/completions",
                RequiresApiKey = false,
                AuthScheme = ModelAuthScheme.None,
                IsEnabled = false,
                IsBuiltIn = true
            }
        ];
    }

    private static string MapProviderKind(ModelProviderKind kind) =>
        kind switch
        {
            ModelProviderKind.Gemini => "Gemini",
            ModelProviderKind.Local => "OpenAICompatible",
            _ => "OpenAICompatible"
        };

    private void CopyToRuntime(ProviderOptions source)
    {
        _runtimeOptions.Provider = source.Provider;
        _runtimeOptions.BaseUrl = source.BaseUrl;
        _runtimeOptions.Endpoint = source.Endpoint;
        _runtimeOptions.Model = source.Model;
        _runtimeOptions.ApiKeyEnvironmentVariable = source.ApiKeyEnvironmentVariable;
        _runtimeOptions.RequestTimeoutSeconds = source.RequestTimeoutSeconds;
        _runtimeOptions.RequiresApiKey = source.RequiresApiKey;
        _runtimeOptions.AuthScheme = source.AuthScheme;
        _runtimeOptions.ApiKeyHeaderName = source.ApiKeyHeaderName;
        _runtimeOptions.RuntimeApiKey = source.RuntimeApiKey;
        _runtimeOptions.Temperature = source.Temperature;
        _runtimeOptions.MaxTokens = source.MaxTokens;
        _runtimeOptions.VisionEnabled = source.VisionEnabled;
    }
}
