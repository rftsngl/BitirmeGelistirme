namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class ContextAdapterContext
{
    public string AdapterName { get; init; } = string.Empty;
    public string? ProcessName { get; init; }
    public string? WindowTitle { get; init; }
    public long? WindowHandle { get; init; }
    public IDictionary<string, string>? Metadata { get; init; }
}
