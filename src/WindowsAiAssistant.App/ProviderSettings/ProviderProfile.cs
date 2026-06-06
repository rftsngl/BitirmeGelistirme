namespace WindowsAiAssistant.App.ProviderSettings;

public enum ModelProviderKind
{
    OpenAICompatible,
    Gemini,
    Local
}

public enum ModelEndpointStyle
{
    OpenAiChatCompletions,
    GeminiGenerateContent
}

public enum ModelAuthScheme
{
    Bearer,
    Raw,
    None
}

public sealed class ProviderProfile
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public ModelProviderKind Kind { get; init; } = ModelProviderKind.OpenAICompatible;
    public string BaseUrl { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public ModelEndpointStyle EndpointStyle { get; init; } = ModelEndpointStyle.OpenAiChatCompletions;
    public string EndpointPath { get; init; } = "chat/completions";
    public bool RequiresApiKey { get; init; } = true;
    public string ApiKeyEnvVar { get; init; } = string.Empty;
    public string ApiKeyHeaderName { get; init; } = "Authorization";
    public ModelAuthScheme AuthScheme { get; init; } = ModelAuthScheme.Bearer;
    public bool IsEnabled { get; init; } = true;
    public bool IsBuiltIn { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public bool VisionEnabled { get; init; }
    public int RequestTimeoutSeconds { get; init; } = 120;
}

public sealed class ProviderProfileSaveResult
{
    public bool Success { get; private init; }
    public string Message { get; private init; } = string.Empty;

    public static ProviderProfileSaveResult Ok() => new() { Success = true };
    public static ProviderProfileSaveResult Fail(string message) => new() { Success = false, Message = message };
}

public enum ProviderConnectionTestStatus
{
    Success,
    Failed,
    Cancelled
}

public sealed class ConnectionTestSummary
{
    public ProviderConnectionTestStatus Status { get; init; } = ProviderConnectionTestStatus.Failed;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.Now;
    public TimeSpan Duration { get; init; }

    public string Title => Status switch
    {
        ProviderConnectionTestStatus.Success => "Bağlantı başarılı",
        ProviderConnectionTestStatus.Cancelled => "Test iptal edildi",
        _ => "Bağlantı başarısız"
    };

    public string CompletedDisplay => CompletedAt.ToLocalTime().ToString("HH:mm:ss");
    public string DurationDisplay => $"{(int)Duration.TotalMilliseconds} ms";
}
