namespace WindowsAiAssistant.Contracts.Models.ActionPrimitives;

public abstract record ActionPrimitive(ActionPrimitiveKind Kind);

public sealed record LaunchTargetPrimitive(string Target)
    : ActionPrimitive(ActionPrimitiveKind.LaunchTarget);

public sealed record TypeTextPrimitive(string Text)
    : ActionPrimitive(ActionPrimitiveKind.TypeText);

public sealed record PressKeyPrimitive(KeyboardKey Key)
    : ActionPrimitive(ActionPrimitiveKind.PressKey);

public sealed record PressShortcutPrimitive(KeyboardKey Key, IReadOnlyList<ShortcutModifierKey> Modifiers)
    : ActionPrimitive(ActionPrimitiveKind.PressShortcut);

public sealed record WaitPrimitive(TimeSpan Duration)
    : ActionPrimitive(ActionPrimitiveKind.Wait);
