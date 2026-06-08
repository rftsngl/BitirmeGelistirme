namespace WindowsAiAssistant.Runtime.Automation;

/// <summary>
/// Maps LLM-provided visible labels to stable elementId values from the latest observation.
/// </summary>
public static class UiElementTargetResolver
{
    public static string? TryResolve(string? rawTarget, IReadOnlyList<UiElementSnapshot>? elements)
    {
        if (string.IsNullOrWhiteSpace(rawTarget) || elements is null || elements.Count == 0)
        {
            return null;
        }

        var target = NormalizeLabel(rawTarget);
        if (UiElementIdValidator.IsValidFormat(target))
        {
            return target;
        }

        var exactNameMatches = MatchByExactName(target, elements);
        if (exactNameMatches.Count == 1)
        {
            return exactNameMatches[0].ElementId;
        }

        if (exactNameMatches.Count > 1)
        {
            return PickBestCandidate(exactNameMatches)?.ElementId;
        }

        var automationMatches = MatchByAutomationId(target, elements);
        if (automationMatches.Count == 1)
        {
            return automationMatches[0].ElementId;
        }

        if (automationMatches.Count > 1)
        {
            return PickBestCandidate(automationMatches)?.ElementId;
        }

        var containsMatches = MatchByNameContains(target, elements);
        if (containsMatches.Count == 1)
        {
            return containsMatches[0].ElementId;
        }

        if (containsMatches.Count > 1)
        {
            return PickBestCandidate(containsMatches)?.ElementId;
        }

        return null;
    }

    private static List<UiElementSnapshot> MatchByExactName(string target, IReadOnlyList<UiElementSnapshot> elements)
    {
        var matches = new List<UiElementSnapshot>();
        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element.Name))
            {
                continue;
            }

            if (string.Equals(NormalizeLabel(element.Name), target, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(element);
            }
        }

        return matches;
    }

    private static List<UiElementSnapshot> MatchByAutomationId(string target, IReadOnlyList<UiElementSnapshot> elements)
    {
        var matches = new List<UiElementSnapshot>();
        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element.AutomationId))
            {
                continue;
            }

            if (string.Equals(element.AutomationId.Trim(), target, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(element);
            }
        }

        return matches;
    }

    private static List<UiElementSnapshot> MatchByNameContains(string target, IReadOnlyList<UiElementSnapshot> elements)
    {
        var matches = new List<UiElementSnapshot>();
        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element.Name))
            {
                continue;
            }

            var name = NormalizeLabel(element.Name);
            if (name.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                target.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(element);
            }
        }

        return matches;
    }

    private static UiElementSnapshot? PickBestCandidate(IReadOnlyList<UiElementSnapshot> candidates)
    {
        var enabled = candidates.Where(element => element.IsEnabled).ToList();
        var pool = enabled.Count > 0 ? enabled : candidates.ToList();

        var buttons = pool.Where(element =>
                element.ControlType.Contains("Button", StringComparison.OrdinalIgnoreCase) ||
                element.ElementId.StartsWith("btn-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (buttons.Count == 1)
        {
            return buttons[0];
        }

        if (buttons.Count > 1)
        {
            pool = buttons;
        }

        return pool
            .OrderBy(element => element.Name.Length)
            .ThenBy(element => element.ElementId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string NormalizeLabel(string value) =>
        value.Trim().Trim('"').Trim('\'');
}
