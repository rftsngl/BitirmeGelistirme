namespace WindowsAiAssistant.Contracts.Models.Agent.Enums;

public enum CommandIntentKind
{
    Unknown,
    OpenApplication,
    FocusWindow,
    VerifyProcessRunning,
    VerifyForegroundAlignment,
    VerifyServiceStatus,
    StartService,
    StopService,
    VerifyFileExists,
    OpenExistingFile,
    PrimitiveInput
}
