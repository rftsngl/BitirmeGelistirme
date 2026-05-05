namespace WindowsAiAssistant.Contracts.Models.Verification;

public enum VerificationPrimitiveKind
{
    ProcessPresence,
    ForegroundAlignment,
    TextInputPostcondition,
    KeyInteractionPostcondition,
    FileOpenPostcondition,
    NavigationDestination
}

public enum VerificationStatus
{
    Verified,
    NotVerified,
    Unsupported,
    Inconclusive
}
