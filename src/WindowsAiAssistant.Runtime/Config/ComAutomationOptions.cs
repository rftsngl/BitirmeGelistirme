namespace WindowsAiAssistant.Runtime.Config;

public sealed class ComAutomationOptions
{
    public List<ComAllowedOperation> AllowedOperations { get; set; } = [];
}

public sealed class ComAllowedOperation
{
    public string ProgId { get; set; } = string.Empty;
    public string[] Methods { get; set; } = [];
}
