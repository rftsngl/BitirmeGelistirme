namespace WindowsAiAssistant.Contracts.Models.Agent.Enums;

public enum ExecutionStatus
{
    Planned,
    Blocked,
    Skipped,
    Attempted,
    Succeeded,
    PartiallySucceeded,
    Failed,
    VerificationFailed
}
