using WindowsAiAssistant.Contracts.Models.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure.ActionPrimitives;

public static class KeyboardInputParser
{
    private static readonly IReadOnlyDictionary<string, KeyboardKey> KeyAliases =
        new Dictionary<string, KeyboardKey>(StringComparer.OrdinalIgnoreCase)
        {
            ["backspace"] = KeyboardKey.Backspace,
            ["tab"] = KeyboardKey.Tab,
            ["enter"] = KeyboardKey.Enter,
            ["return"] = KeyboardKey.Enter,
            ["esc"] = KeyboardKey.Escape,
            ["escape"] = KeyboardKey.Escape,
            ["space"] = KeyboardKey.Space,
            ["del"] = KeyboardKey.Delete,
            ["delete"] = KeyboardKey.Delete,
            ["left"] = KeyboardKey.Left,
            ["leftarrow"] = KeyboardKey.Left,
            ["up"] = KeyboardKey.Up,
            ["uparrow"] = KeyboardKey.Up,
            ["right"] = KeyboardKey.Right,
            ["rightarrow"] = KeyboardKey.Right,
            ["down"] = KeyboardKey.Down,
            ["downarrow"] = KeyboardKey.Down
        };

    private static readonly IReadOnlyDictionary<string, ShortcutModifierKey> ModifierAliases =
        new Dictionary<string, ShortcutModifierKey>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = ShortcutModifierKey.Control,
            ["control"] = ShortcutModifierKey.Control,
            ["alt"] = ShortcutModifierKey.Alt,
            ["shift"] = ShortcutModifierKey.Shift,
            ["win"] = ShortcutModifierKey.Windows,
            ["windows"] = ShortcutModifierKey.Windows
        };

    public static bool TryParseKey(string rawText, out KeyboardKey key)
    {
        key = default;

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        var normalized = NormalizeToken(rawText);

        if (KeyAliases.TryGetValue(normalized, out key))
        {
            return true;
        }

        if (normalized.Length == 1)
        {
            var singleChar = normalized[0];
            if (singleChar is >= 'a' and <= 'z')
            {
                key = (KeyboardKey)(singleChar - 'a' + (int)KeyboardKey.A);
                return true;
            }

            if (singleChar is >= '0' and <= '9')
            {
                key = (KeyboardKey)(singleChar - '0' + (int)KeyboardKey.D0);
                return true;
            }
        }

        if (normalized.Length is 2 or 3 &&
            normalized.StartsWith('f') &&
            int.TryParse(normalized[1..], out var functionKeyIndex) &&
            functionKeyIndex is >= 1 and <= 12)
        {
            key = (KeyboardKey)(functionKeyIndex - 1 + (int)KeyboardKey.F1);
            return true;
        }

        return false;
    }

    public static bool TryParseShortcut(
        string rawText,
        out KeyboardKey key,
        out IReadOnlyList<ShortcutModifierKey> modifiers)
    {
        key = default;
        modifiers = [];

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        var tokens = rawText
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeToken)
            .ToArray();

        if (tokens.Length < 2)
        {
            return false;
        }

        var parsedModifiers = new List<ShortcutModifierKey>();

        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (!ModifierAliases.TryGetValue(tokens[i], out var modifier))
            {
                return false;
            }

            if (!parsedModifiers.Contains(modifier))
            {
                parsedModifiers.Add(modifier);
            }
        }

        if (!TryParseKey(tokens[^1], out key))
        {
            return false;
        }

        modifiers = parsedModifiers;
        return parsedModifiers.Count > 0;
    }

    private static string NormalizeToken(string value)
    {
        return value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
