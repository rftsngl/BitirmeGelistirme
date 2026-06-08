namespace WindowsAiAssistant.Agent.Planning;

public static class SkillDomainFormatter
{
    public static string Format(WorkflowSkillDomain domain) =>
        domain switch
        {
            WorkflowSkillDomain.Conversation => "Sohbet",
            WorkflowSkillDomain.Integration => "Entegrasyon",
            WorkflowSkillDomain.Office => "Office",
            WorkflowSkillDomain.Window => "Pencere",
            _ => string.Empty
        };

    public static string Format(string? domainName)
    {
        if (string.IsNullOrWhiteSpace(domainName))
        {
            return string.Empty;
        }

        return Enum.TryParse<WorkflowSkillDomain>(domainName, ignoreCase: true, out var parsed)
            ? Format(parsed)
            : string.Empty;
    }
}
