using System.Security.Cryptography;
using System.Text;
using FlaUI.Core.Definitions;

namespace WindowsAiAssistant.Runtime.Automation;

internal static class UiElementIdGenerator
{
    public static string Create(ControlType controlType, string? automationId, string? name, int[] runtimeId)
    {
        var prefix = ToPrefix(controlType);
        var normalized = NormalizeToken(automationId) ?? NormalizeToken(name) ?? "unknown";
        var hash = HashRuntimeId(runtimeId);
        return $"{prefix}-{normalized}-{hash}";
    }

    private static string ToPrefix(ControlType controlType) =>
        controlType switch
        {
            ControlType.Button => "btn",
            ControlType.Edit => "txt",
            ControlType.CheckBox => "chk",
            ControlType.ComboBox => "cmb",
            ControlType.ListItem => "itm",
            ControlType.MenuItem => "mnu",
            ControlType.TabItem => "tab",
            ControlType.Hyperlink => "lnk",
            ControlType.RadioButton => "rad",
            ControlType.TreeItem => "tre",
            ControlType.Window => "win",
            ControlType.Document => "doc",
            ControlType.Text => "txt",
            _ => "el"
        };

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .ToArray());

        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Length <= 15 ? normalized : normalized[..15];
    }

    private static string HashRuntimeId(int[] runtimeId)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('-', runtimeId));
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..4].ToLowerInvariant();
    }
}
