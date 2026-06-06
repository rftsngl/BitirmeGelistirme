using System.Text.RegularExpressions;

namespace WindowsAiAssistant.Runtime.Automation;

public static class UiElementIdValidator
{
    private static readonly Regex ElementIdPattern = new(
        @"^(btn|txt|chk|cmb|itm|mnu|tab|lnk|rad|tre|win|doc|el)-[a-z0-9_-]+-[a-f0-9]{4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValidFormat(string? elementId) =>
        !string.IsNullOrWhiteSpace(elementId) && ElementIdPattern.IsMatch(elementId.Trim());

    public static bool IsUiAutomationAction(string action) =>
        action.Equals("click_element", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("focus_element", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("read_element", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("set_value", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("select_element", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("expand_collapse", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("invoke_toggle", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("scroll", StringComparison.OrdinalIgnoreCase);

    public static string? GetElementTarget(string action, string? target, IReadOnlyDictionary<string, string> parameters)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            return target.Trim();
        }

        if (parameters.TryGetValue("elementId", out var fromParams) && !string.IsNullOrWhiteSpace(fromParams))
        {
            return fromParams.Trim();
        }

        return action.Equals("mouse_click", StringComparison.OrdinalIgnoreCase) &&
               parameters.TryGetValue("elementId", out var clickId) &&
               !string.IsNullOrWhiteSpace(clickId)
            ? clickId.Trim()
            : null;
    }
}
