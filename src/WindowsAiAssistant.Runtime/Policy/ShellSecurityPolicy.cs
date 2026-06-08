namespace WindowsAiAssistant.Runtime.Policy;

public static class ShellSecurityPolicy
{
    public static readonly string[] DestructivePatterns =
    [
        "rm ", "rmdir", "rd ", "del ", "erase ", "remove-item", "remove-itemproperty",
        "clear-content", "clear-recyclebin", "format ", "format-volume", "diskpart", "shutdown",
        "restart-computer", "stop-computer", "stop-process", "taskkill /f",
        "reg delete", "cipher /w", "fsutil", "takeown", "icacls", "net user",
        "bcdedit", "mkfs", "dd if=", "-verb runas", "runas ", "sc delete",
        "schtasks /delete", "invoke-expression", "iex ", "downloadstring",
        "invoke-webrequest", "wget ", "curl -o", "start-bitstransfer"
    ];

    public static bool IsDestructive(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var normalized = command.ToLowerInvariant();
        return DestructivePatterns.Any(pattern =>
            normalized.Contains(pattern, StringComparison.Ordinal));
    }

    public static string SanitizeForAudit(string command, int maxChars = 500)
    {
        var trimmed = command.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return trimmed.Length <= maxChars
            ? trimmed
            : trimmed[..maxChars] + $"... (+{trimmed.Length - maxChars} karakter)";
    }
}
