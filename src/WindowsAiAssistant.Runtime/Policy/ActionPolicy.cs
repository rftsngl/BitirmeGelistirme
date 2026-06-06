namespace WindowsAiAssistant.Runtime.Policy;

public sealed class ActionPolicy
{
    public RiskHandling Normal { get; set; } = RiskHandling.Allow;
    public RiskHandling Sensitive { get; set; } = RiskHandling.RequireApproval;
    public RiskHandling Destructive { get; set; } = RiskHandling.RequireApproval;
    public bool AllowSessionRemember { get; set; }
}
