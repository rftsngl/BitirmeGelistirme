namespace WindowsAiAssistant.Runtime.Actions;

internal static class AppLaunchCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["notepad"] = "notepad.exe",
            ["notepad.exe"] = "notepad.exe",
            ["calculator"] = "calc.exe",
            ["calc"] = "calc.exe",
            ["calc.exe"] = "calc.exe",
            ["paint"] = "mspaint.exe",
            ["mspaint"] = "mspaint.exe",
            ["explorer"] = "explorer.exe",
            ["explorer.exe"] = "explorer.exe",
            ["cmd"] = "cmd.exe",
            ["cmd.exe"] = "cmd.exe",
            ["komut istemi"] = "cmd.exe",
            ["powershell"] = "powershell.exe",
            ["powershell.exe"] = "powershell.exe",
            ["terminal"] = "wt.exe",
            ["windows terminal"] = "wt.exe",
            ["wordpad"] = "write.exe",
            ["write"] = "write.exe",
            ["snippingtool"] = "snippingtool.exe",
            ["snipping tool"] = "snippingtool.exe",
            ["ekran alintisi"] = "snippingtool.exe",
            ["taskmgr"] = "taskmgr.exe",
            ["task manager"] = "taskmgr.exe",
            ["gorev yoneticisi"] = "taskmgr.exe",
            ["control"] = "control.exe",
            ["denetim masasi"] = "control.exe",
            ["regedit"] = "regedit.exe",
            ["charmap"] = "charmap.exe"
        };

    public static bool TryResolve(string? target, out string executable, out string? error)
    {
        executable = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(target))
        {
            error = "open_app icin target veya parameters.app gerekli.";
            return false;
        }

        var normalized = target.Trim();
        if (normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains('/', StringComparison.Ordinal) ||
            normalized.Contains('\\', StringComparison.Ordinal))
        {
            error = "open_app yalnizca bilinen uygulama adlari ile calisir.";
            return false;
        }

        if (Map.TryGetValue(normalized, out var mapped))
        {
            executable = mapped;
            return true;
        }

        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            Map.TryGetValue(normalized, out mapped))
        {
            executable = mapped;
            return true;
        }

        error = $"Bilinmeyen uygulama: '{normalized}'. Desteklenen: notepad, calc, mspaint, explorer.";
        return false;
    }
}
