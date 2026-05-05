namespace WindowsAiAssistant.App.Models;

public sealed class CapabilityViewItem
{
    public required string Name { get; init; }
    public required string IconGlyph { get; init; }
    public required IReadOnlyList<string> TargetKindLabels { get; init; }
    public string Description { get; init; } = string.Empty;

    public string TargetKindsDisplay =>
        TargetKindLabels.Count == 0 ? "—" : string.Join(", ", TargetKindLabels);
}
