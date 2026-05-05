using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.Infrastructure;

namespace WindowsAiAssistant.App.ProviderSettings;

public sealed class ProviderProfileDraft : ObservableObject
{
    private string _id = string.Empty;
    private string _displayName = string.Empty;
    private ModelProviderKind _kind = ModelProviderKind.OpenAICompatible;
    private string _baseUrl = string.Empty;
    private string _model = string.Empty;
    private ModelEndpointStyle _endpointStyle = ModelEndpointStyle.OpenAiChatCompletions;
    private string _endpointPath = "chat/completions";
    private bool _requiresApiKey = true;
    private string _apiKeyEnvVar = string.Empty;
    private string _apiKeyHeaderName = "Authorization";
    private ModelAuthScheme _authScheme = ModelAuthScheme.Bearer;
    private bool _isEnabled = true;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value ?? string.Empty);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value ?? string.Empty);
    }

    public ModelProviderKind Kind
    {
        get => _kind;
        set => SetField(ref _kind, value);
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set => SetField(ref _baseUrl, value ?? string.Empty);
    }

    public string Model
    {
        get => _model;
        set => SetField(ref _model, value ?? string.Empty);
    }

    public ModelEndpointStyle EndpointStyle
    {
        get => _endpointStyle;
        set => SetField(ref _endpointStyle, value);
    }

    public string EndpointPath
    {
        get => _endpointPath;
        set => SetField(ref _endpointPath, value ?? string.Empty);
    }

    public bool RequiresApiKey
    {
        get => _requiresApiKey;
        set => SetField(ref _requiresApiKey, value);
    }

    public string ApiKeyEnvVar
    {
        get => _apiKeyEnvVar;
        set => SetField(ref _apiKeyEnvVar, value ?? string.Empty);
    }

    public string ApiKeyHeaderName
    {
        get => _apiKeyHeaderName;
        set => SetField(ref _apiKeyHeaderName, value ?? string.Empty);
    }

    public ModelAuthScheme AuthScheme
    {
        get => _authScheme;
        set => SetField(ref _authScheme, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public ModelDecisionProfile ToProfile() =>
        new()
        {
            Id = (Id ?? string.Empty).Trim(),
            DisplayName = (DisplayName ?? string.Empty).Trim(),
            Kind = Kind,
            BaseUrl = (BaseUrl ?? string.Empty).Trim(),
            Model = (Model ?? string.Empty).Trim(),
            EndpointStyle = EndpointStyle,
            EndpointPath = (EndpointPath ?? string.Empty).Trim(),
            RequiresApiKey = RequiresApiKey,
            ApiKeyEnvVar = (ApiKeyEnvVar ?? string.Empty).Trim(),
            ApiKeyHeaderName = string.IsNullOrWhiteSpace(ApiKeyHeaderName) ? "Authorization" : ApiKeyHeaderName.Trim(),
            AuthScheme = AuthScheme,
            IsEnabled = IsEnabled
        };

    public static ProviderProfileDraft FromProfile(ModelDecisionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new ProviderProfileDraft
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
            IsEnabled = profile.IsEnabled
        };
    }
}
