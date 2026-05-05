using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class TargetReference
{
    public TargetKind Kind { get; init; } = TargetKind.Unknown;
    public string OriginalText { get; init; } = string.Empty;
    public string NormalizedValue { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public double Confidence { get; init; }
    public TargetResolutionReasonKind ResolutionReasonKind { get; init; } = TargetResolutionReasonKind.Unknown;
    public string? ResolutionSourceText { get; init; }
    public IDictionary<string, string>? Metadata { get; init; }
}
