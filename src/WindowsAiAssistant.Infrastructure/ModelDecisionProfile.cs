namespace WindowsAiAssistant.Infrastructure;

public sealed class ModelDecisionProfile
{
    public string Id { get; init; } = string.Empty;
    public ModelProviderKind Kind { get; init; } = ModelProviderKind.Disabled;
    public string DisplayName { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public ModelEndpointStyle EndpointStyle { get; init; } = ModelEndpointStyle.Unsupported;
    public string EndpointPath { get; init; } = string.Empty;
    public bool RequiresApiKey { get; init; } = true;
    public string ApiKeyEnvVar { get; init; } = string.Empty;
    public string ApiKeyHeaderName { get; init; } = "Authorization";
    public ModelAuthScheme AuthScheme { get; init; } = ModelAuthScheme.Bearer;
    public bool IsEnabled { get; init; } = true;
}
