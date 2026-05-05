namespace WindowsAiAssistant.Infrastructure;

public sealed class ExecutionPolicySettings
{
    public List<string> AllowedRealApps { get; init; } = [];
    public List<string> AllowedExecutablePaths { get; init; } = [];
    public Dictionary<string, string> AppAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> RequiresApprovalTargets { get; init; } = [];
    public List<string> RequiresApprovalTextPatterns { get; init; } = [];
    public List<string> RequiresApprovalShortcutPatterns { get; init; } = [];
    public List<string> DeniedPatterns { get; init; } = [];
}
