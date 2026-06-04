using WindowsAiAssistant.App.Mvvm;

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
    private string _temperatureText = string.Empty;
    private string _maxTokensText = string.Empty;
    private bool _visionEnabled;
    private int _requestTimeoutSeconds = 120;

    public string Id { get => _id; set => SetField(ref _id, value ?? string.Empty); }
    public string DisplayName { get => _displayName; set => SetField(ref _displayName, value ?? string.Empty); }
    public ModelProviderKind Kind
    {
        get => _kind;
        set
        {
            if (SetField(ref _kind, value))
            {
                ApplyKindDefaults(value);
            }
        }
    }

    public string BaseUrl { get => _baseUrl; set => SetField(ref _baseUrl, value ?? string.Empty); }
    public string Model { get => _model; set => SetField(ref _model, value ?? string.Empty); }
    public ModelEndpointStyle EndpointStyle { get => _endpointStyle; set => SetField(ref _endpointStyle, value); }
    public string EndpointPath { get => _endpointPath; set => SetField(ref _endpointPath, value ?? string.Empty); }
    public bool RequiresApiKey { get => _requiresApiKey; set => SetField(ref _requiresApiKey, value); }
    public string ApiKeyEnvVar { get => _apiKeyEnvVar; set => SetField(ref _apiKeyEnvVar, value ?? string.Empty); }
    public string ApiKeyHeaderName { get => _apiKeyHeaderName; set => SetField(ref _apiKeyHeaderName, value ?? string.Empty); }
    public ModelAuthScheme AuthScheme { get => _authScheme; set => SetField(ref _authScheme, value); }
    public bool IsEnabled { get => _isEnabled; set => SetField(ref _isEnabled, value); }
    public string TemperatureText { get => _temperatureText; set => SetField(ref _temperatureText, value ?? string.Empty); }
    public string MaxTokensText { get => _maxTokensText; set => SetField(ref _maxTokensText, value ?? string.Empty); }
    public bool VisionEnabled { get => _visionEnabled; set => SetField(ref _visionEnabled, value); }
    public int RequestTimeoutSeconds { get => _requestTimeoutSeconds; set => SetField(ref _requestTimeoutSeconds, value); }

    public ProviderProfile ToProfile() =>
        new()
        {
            Id = Id.Trim(),
            DisplayName = DisplayName.Trim(),
            Kind = Kind,
            BaseUrl = BaseUrl.Trim(),
            Model = Model.Trim(),
            EndpointStyle = EndpointStyle,
            EndpointPath = EndpointPath.Trim(),
            RequiresApiKey = RequiresApiKey,
            ApiKeyEnvVar = ApiKeyEnvVar.Trim(),
            ApiKeyHeaderName = string.IsNullOrWhiteSpace(ApiKeyHeaderName) ? "Authorization" : ApiKeyHeaderName.Trim(),
            AuthScheme = AuthScheme,
            IsEnabled = IsEnabled,
            Temperature = ParseNullableDouble(TemperatureText),
            MaxTokens = ParseNullableInt(MaxTokensText),
            VisionEnabled = VisionEnabled,
            RequestTimeoutSeconds = Math.Clamp(RequestTimeoutSeconds, 5, 600)
        };

    public static ProviderProfileDraft FromProfile(ProviderProfile profile) =>
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
            TemperatureText = profile.Temperature?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            MaxTokensText = profile.MaxTokens?.ToString() ?? string.Empty,
            VisionEnabled = profile.VisionEnabled,
            RequestTimeoutSeconds = profile.RequestTimeoutSeconds
        };

    private void ApplyKindDefaults(ModelProviderKind kind)
    {
        switch (kind)
        {
            case ModelProviderKind.Gemini:
                EndpointStyle = ModelEndpointStyle.GeminiGenerateContent;
                EndpointPath = "v1beta/models/{model}:generateContent";
                AuthScheme = ModelAuthScheme.Raw;
                ApiKeyHeaderName = "x-goog-api-key";
                if (string.IsNullOrWhiteSpace(ApiKeyEnvVar))
                {
                    ApiKeyEnvVar = "GEMINI_API_KEY";
                }

                if (string.IsNullOrWhiteSpace(BaseUrl))
                {
                    BaseUrl = "https://generativelanguage.googleapis.com";
                }

                break;

            case ModelProviderKind.Local:
                EndpointStyle = ModelEndpointStyle.OpenAiChatCompletions;
                EndpointPath = "chat/completions";
                RequiresApiKey = false;
                AuthScheme = ModelAuthScheme.None;
                if (string.IsNullOrWhiteSpace(BaseUrl))
                {
                    BaseUrl = "http://localhost:11434/v1";
                }

                break;

            default:
                EndpointStyle = ModelEndpointStyle.OpenAiChatCompletions;
                EndpointPath = "chat/completions";
                AuthScheme = ModelAuthScheme.Bearer;
                ApiKeyHeaderName = "Authorization";
                if (string.IsNullOrWhiteSpace(ApiKeyEnvVar))
                {
                    ApiKeyEnvVar = "OPENAI_API_KEY";
                }

                break;
        }
    }

    private static double? ParseNullableDouble(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int? ParseNullableInt(string text) =>
        int.TryParse(text, out var value) ? value : null;
}
