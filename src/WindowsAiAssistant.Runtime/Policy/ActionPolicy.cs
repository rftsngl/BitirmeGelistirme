namespace WindowsAiAssistant.Runtime.Policy;

public sealed class ActionPolicy
{
    public RiskHandling Normal { get; set; } = RiskHandling.Allow;
    public RiskHandling Sensitive { get; set; } = RiskHandling.RequireApproval;
    public RiskHandling Destructive { get; set; } = RiskHandling.RequireApproval;
    public bool AllowSessionRemember { get; set; }

    /// <summary>
    /// Shortcuts treated as Normal risk (auto-allow under default policy). Case-insensitive; Ctrl/Control aliases accepted.
    /// </summary>
    public IList<string> SafeShortcuts { get; set; } = new List<string>
    {
        "Ctrl+S", "Ctrl+N", "Ctrl+T", "Ctrl+W", "Ctrl+F", "Ctrl+P",
        "Ctrl+Z", "Ctrl+Y", "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+A",
        "Ctrl+H", "Ctrl+B", "Ctrl+O", "F5", "F3", "Escape", "Enter", "Tab"
    };
}
