namespace WindowsAiAssistant.Agent;

public enum DecisionParseErrorCode
{
    None = 0,
    EmptyOutput,
    MissingJson,
    InvalidJson,
    InvalidRoot,
    MissingDecisionType,
    LegacyTypeField,
    UnsupportedDecisionType,
    MissingAction,
    UnsupportedAction,
    TypeActionMismatch,
    MissingRequiredField,
    InvalidElementId,
    Other
}
