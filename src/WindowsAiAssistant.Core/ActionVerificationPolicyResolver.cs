using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Core;

internal enum ActionVerificationMode
{
    None = 0,
    StrictExternal = 1,
    ExecutionDerived = 2
}

internal enum ExecutionDerivedVerificationStrategy
{
    None = 0,
    TextDelta = 1,
    ContextTransition = 2,
    FileOpenEffect = 3,
    KeyInteraction = 4
}

internal sealed class ActionVerificationPolicy
{
    public ActionType ActionType { get; init; } = ActionType.Unknown;
    public bool RequiresVerification { get; init; }
    public DecisionVerificationKind VerificationKind { get; init; } = DecisionVerificationKind.Unknown;
    public ActionVerificationMode Mode { get; init; } = ActionVerificationMode.None;
    public ExecutionDerivedVerificationStrategy ExecutionDerivedStrategy { get; init; } = ExecutionDerivedVerificationStrategy.None;
    public bool InconclusiveRetryable { get; init; }
}

internal static class ActionVerificationPolicyResolver
{
    public static ActionVerificationPolicy Resolve(ActionType? actionType)
    {
        var resolvedActionType = actionType ?? ActionType.Unknown;

        return resolvedActionType switch
        {
            ActionType.Launch => CreateStrictPolicy(ActionType.Launch, DecisionVerificationKind.ProcessPresence),
            ActionType.Focus => CreateStrictPolicy(ActionType.Focus, DecisionVerificationKind.ForegroundAlignment),
            ActionType.Verify => CreateStrictPolicy(ActionType.Verify, DecisionVerificationKind.ProcessPresence),
            ActionType.InputText => CreateStrictPolicy(ActionType.InputText, DecisionVerificationKind.TextInputPostcondition),
            ActionType.Navigate => CreateStrictPolicy(ActionType.Navigate, DecisionVerificationKind.NavigationDestination),
            ActionType.Confirm => CreateStrictPolicy(ActionType.Confirm, DecisionVerificationKind.KeyInteractionPostcondition),
            ActionType.Cancel => CreateStrictPolicy(ActionType.Cancel, DecisionVerificationKind.KeyInteractionPostcondition),
            ActionType.PressKey => CreateStrictPolicy(ActionType.PressKey, DecisionVerificationKind.KeyInteractionPostcondition),
            ActionType.PressShortcut => CreateStrictPolicy(ActionType.PressShortcut, DecisionVerificationKind.KeyInteractionPostcondition),
            ActionType.OpenFile => CreateStrictPolicy(ActionType.OpenFile, DecisionVerificationKind.FileOpenPostcondition),
            _ => new ActionVerificationPolicy
            {
                ActionType = resolvedActionType
            }
        };
    }

    public static DecisionVerificationKind MapKind(VerificationPrimitiveKind primitiveKind)
    {
        return primitiveKind switch
        {
            VerificationPrimitiveKind.ProcessPresence => DecisionVerificationKind.ProcessPresence,
            VerificationPrimitiveKind.ForegroundAlignment => DecisionVerificationKind.ForegroundAlignment,
            VerificationPrimitiveKind.TextInputPostcondition => DecisionVerificationKind.TextInputPostcondition,
            VerificationPrimitiveKind.KeyInteractionPostcondition => DecisionVerificationKind.KeyInteractionPostcondition,
            VerificationPrimitiveKind.FileOpenPostcondition => DecisionVerificationKind.FileOpenPostcondition,
            VerificationPrimitiveKind.NavigationDestination => DecisionVerificationKind.NavigationDestination,
            _ => DecisionVerificationKind.Unknown
        };
    }

    private static ActionVerificationPolicy CreateStrictPolicy(
        ActionType actionType,
        DecisionVerificationKind kind)
    {
        return new ActionVerificationPolicy
        {
            ActionType = actionType,
            RequiresVerification = true,
            VerificationKind = kind,
            Mode = ActionVerificationMode.StrictExternal,
            InconclusiveRetryable = true
        };
    }

    private static ActionVerificationPolicy CreateExecutionDerivedPolicy(
        ActionType actionType,
        ExecutionDerivedVerificationStrategy strategy,
        bool inconclusiveRetryable = false)
    {
        return new ActionVerificationPolicy
        {
            ActionType = actionType,
            RequiresVerification = true,
            VerificationKind = DecisionVerificationKind.ExecutionDerived,
            Mode = ActionVerificationMode.ExecutionDerived,
            ExecutionDerivedStrategy = strategy,
            InconclusiveRetryable = inconclusiveRetryable
        };
    }
}
