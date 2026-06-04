namespace WindowsAiAssistant.Runtime.Config;

public sealed class ProviderOptions
{
    public string Provider { get; set; } = "OpenAICompatible";
    public string BaseUrl { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "/chat/completions";
    public string Model { get; set; } = string.Empty;
    public string ApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";
    public int RequestTimeoutSeconds { get; set; } = 120;
    public bool RequiresApiKey { get; set; } = true;
    public string AuthScheme { get; set; } = "Bearer";
    public string ApiKeyHeaderName { get; set; } = "Authorization";
    public string? RuntimeApiKey { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public bool VisionEnabled { get; set; }
}
