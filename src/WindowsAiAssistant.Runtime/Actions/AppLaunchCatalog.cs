using Microsoft.Win32;
using System.Diagnostics;

namespace WindowsAiAssistant.Runtime.Actions;

/// <summary>
/// Yaygin uygulama adlari icin kolaylik alias haritasi. Sabit bir izin listesi
/// DEGILDIR: bilinmeyen adlar Windows'a oldugu gibi gecirilebilir (App Paths/PATH
/// uzerinden cozulur). Tam yol veya komutlar icin 'launch'/'shell' kullanilir.
/// </summary>
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
            ["chrome"] = "chrome.exe",
            ["chrome.exe"] = "chrome.exe",
            ["google chrome"] = "chrome.exe",
            ["edge"] = "msedge.exe",
            ["msedge"] = "msedge.exe",
            ["msedge.exe"] = "msedge.exe",
            ["microsoft edge"] = "msedge.exe",
            ["firefox"] = "firefox.exe",
            ["firefox.exe"] = "firefox.exe",
            ["mozilla firefox"] = "firefox.exe",
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
            ["charmap"] = "charmap.exe",
            ["steam"] = "steam.exe",
            ["steam.exe"] = "steam.exe",
            ["discord"] = "discord.exe",
            ["discord.exe"] = "discord.exe",
            ["spotify"] = "spotify.exe",
            ["spotify.exe"] = "spotify.exe",
            ["vscode"] = "code.exe",
            ["code"] = "code.exe",
            ["visual studio code"] = "code.exe",
            ["word"] = "winword.exe",
            ["winword"] = "winword.exe",
            ["microsoft word"] = "winword.exe",
            ["excel"] = "excel.exe",
            ["powerpoint"] = "powerpnt.exe",
            ["powerpnt"] = "powerpnt.exe"
        };

    private static readonly IReadOnlyDictionary<string, string[]> ProcessNameHints =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["word"] = ["WINWORD"],
            ["winword"] = ["WINWORD"],
            ["winword.exe"] = ["WINWORD"],
            ["microsoft word"] = ["WINWORD"],
            ["excel"] = ["EXCEL"],
            ["excel.exe"] = ["EXCEL"],
            ["powerpoint"] = ["POWERPNT"],
            ["powerpnt"] = ["POWERPNT"],
            ["powerpnt.exe"] = ["POWERPNT"],
            ["chrome"] = ["chrome"],
            ["chrome.exe"] = ["chrome"],
            ["edge"] = ["msedge"],
            ["msedge"] = ["msedge"],
            ["msedge.exe"] = ["msedge"],
            ["firefox"] = ["firefox"],
            ["firefox.exe"] = ["firefox"],
            ["notepad"] = ["notepad"],
            ["notepad.exe"] = ["notepad"],
            ["code"] = ["Code"],
            ["vscode"] = ["Code"],
            ["visual studio code"] = ["Code"],
            ["discord"] = ["Discord"],
            ["spotify"] = ["Spotify"],
            ["steam"] = ["steam"],
            ["steam.exe"] = ["steam"]
        };

    private static readonly IReadOnlyDictionary<string, string[]> CommonRelativeInstallPaths =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["steam"] =
            [
                @"Steam\steam.exe"
            ],
            ["discord"] =
            [
                @"Discord\app-*\Discord.exe"
            ]
        };

    private static readonly char[] CommandLikeCharacters =
    {
        '&', '|', ';', '<', '>', '"', '\''
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

        error = $"'{normalized}' kisayol kataloglarinda yok (open_app yine de Windows uzerinden cozmeyi dener).";
        return false;
    }

    /// <summary>
    /// open_app icin esnek cozumleme: bilinen ad alias'a, bilinmeyen ad Windows'un
    /// kendi cozumlemesine (App Paths/PATH) birakilir. Tam yol/komut burada
    /// reddedilir; bunlar gated olan 'launch'/'shell' yetenekleriyle calistirilir.
    /// </summary>
    public static bool ResolveForOpenApp(string? target, out string executable, out string? error)
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
            error = "Tam yol veya komut icin 'launch' ya da 'shell' kullanin; open_app yalnizca uygulama adi alir.";
            return false;
        }

        if (Map.TryGetValue(normalized, out var mapped))
        {
            executable = mapped;
            return true;
        }

        if (normalized.Any(char.IsWhiteSpace) || normalized.IndexOfAny(CommandLikeCharacters) >= 0)
        {
            error = "Bilinmeyen uygulama adi arguman veya komut karakteri iceriyor. Tam yol/arguman icin 'launch' ya da 'shell' kullanin.";
            return false;
        }

        executable = normalized;
        return true;
    }

    /// <summary>
    /// open_app hedefi icin olasi calisan process adlarini dondurur (pencere yeniden kullanimi icin).
    /// </summary>
    public static bool TryResolveProcessNamesForOpenApp(string? target, out string[] processNames)
    {
        processNames = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var normalized = target.Trim();
        if (ProcessNameHints.TryGetValue(normalized, out var hinted))
        {
            processNames = hinted;
            return true;
        }

        if (Map.TryGetValue(normalized, out var mapped))
        {
            var stem = Path.GetFileNameWithoutExtension(mapped);
            if (!string.IsNullOrWhiteSpace(stem))
            {
                processNames = [stem];
                return true;
            }
        }

        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processNames = [Path.GetFileNameWithoutExtension(normalized)];
            return true;
        }

        return false;
    }

    /// <summary>
    /// PATH/App Paths disinda kalan kurulumlar icin registry ve bilinen klasorleri dener.
    /// </summary>
    public static string? TryFindInstalledExecutable(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var normalized = target.Trim().Trim('"', '\'');
        if (Path.IsPathRooted(normalized) && File.Exists(normalized))
        {
            return normalized;
        }

        var exeName = normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + ".exe";

        var fromRegistry = TryReadAppPathsRegistry(exeName);
        if (fromRegistry is not null)
        {
            return fromRegistry;
        }

        var appKey = Path.GetFileNameWithoutExtension(exeName);
        if (!CommonRelativeInstallPaths.TryGetValue(appKey, out var relatives))
        {
            return null;
        }

        foreach (var root in GetInstallSearchRoots())
        {
            foreach (var relative in relatives)
            {
                if (relative.Contains('*', StringComparison.Ordinal))
                {
                    var discovered = TryExpandWildcardPath(root, relative);
                    if (discovered is not null)
                    {
                        return discovered;
                    }

                    continue;
                }

                var candidate = Path.Combine(root, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? TryReadAppPathsRegistry(string exeName)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var appPaths = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
            var path = appPaths?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetInstallSearchRoots()
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)))
        {
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)))
        {
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            roots.Add(localAppData);
        }

        return roots;
    }

    private static string? TryExpandWildcardPath(string root, string relativePattern)
    {
        var parts = relativePattern.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        IEnumerable<string> current = [root];
        foreach (var part in parts)
        {
            current = current.SelectMany(dir =>
            {
                if (!Directory.Exists(dir))
                {
                    return [];
                }

                return part.Contains('*', StringComparison.Ordinal)
                    ? Directory.EnumerateDirectories(dir, part)
                    : [Path.Combine(dir, part)];
            });
        }

        return current
            .Select(path => File.Exists(path) ? path : null)
            .FirstOrDefault(path => path is not null);
    }
}
