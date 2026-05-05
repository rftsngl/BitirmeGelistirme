namespace WindowsAiAssistant.Infrastructure.Launch;

public enum LaunchFailureReason
{
    None,
    EmptyTarget,
    UnsupportedTargetKind,
    AllowListMissing,
    NotAllowlisted,
    LaunchReturnedNoProcess,
    LaunchException
}
