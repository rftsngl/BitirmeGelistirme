namespace WindowsAiAssistant.Contracts.Models.Agent.Enums;

public enum TargetGroundingReason
{
    None,
    EmptyInput,
    AllowListMissing,
    NoKnownApplicationMatch,
    MultipleKnownApplicationCandidates,
    ExecutablePathNotAllowlisted,
    UnsupportedPathLikeTarget,
    UnsupportedDocumentTarget,
    UnsupportedUrlTarget
}
