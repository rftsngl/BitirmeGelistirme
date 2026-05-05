namespace WindowsAiAssistant.Contracts.Models.Agent.Enums;

public enum TargetExecutionSuitability
{
    Unknown = 0,
    ExecutableHere = 1,
    ResolvedButNotExecutableHere = 2,
    UnsupportedForCurrentLauncher = 3,
    NotExecutable = 4
}
