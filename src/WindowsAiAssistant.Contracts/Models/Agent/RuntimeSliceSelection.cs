namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class RuntimeSliceSelection
{
    public required string SliceName { get; init; }
    public required IRuntimeSliceResult Result { get; init; }
}
