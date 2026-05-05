using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Infrastructure;

public sealed class ModelDecisionSettings
{
    public bool Enabled { get; init; } = true;
    public string ActiveProfile { get; init; } = string.Empty;
    public List<ModelDecisionProfile> Profiles { get; init; } = [];
    public int TimeoutMilliseconds { get; init; } = 10000;
    public RuntimeSliceFallbackPolicy RuntimeSliceFallbackPolicy { get; init; } = RuntimeSliceFallbackPolicy.FailClosed;
}
