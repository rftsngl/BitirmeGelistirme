using System.Text.Json;
using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Runtime.Debugging;

/// <summary>Debug session NDJSON logger (session 3d556c).</summary>
public static class DebugAgentLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = ResolveLogPath();
    private static volatile bool _enabled;

    public static bool IsEnabled => _enabled;

    public static void Configure(RuntimeOptions? runtimeOptions)
    {
#if DEBUG
        _enabled = runtimeOptions?.Logging.EnableDebugAgentLog ?? true;
#else
        _enabled = runtimeOptions?.Logging.EnableDebugAgentLog ?? false;
#endif
    }

    public static void Write(
        string hypothesisId,
        string location,
        string message,
        object? data = null,
        string? runId = null)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["sessionId"] = "3d556c",
                ["hypothesisId"] = hypothesisId,
                ["location"] = location,
                ["message"] = message,
                ["data"] = data,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            if (!string.IsNullOrWhiteSpace(runId))
            {
                payload["runId"] = runId;
            }

            var line = JsonSerializer.Serialize(payload);
            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Debug logging must never break the agent loop.
        }
    }

    private static string ResolveLogPath()
    {
        var envRoot = Environment.GetEnvironmentVariable("BITIRME_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            return Path.Combine(envRoot, "debug-3d556c.log");
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "debug-3d556c.log");
            if (File.Exists(Path.Combine(dir, "WindowsAiAssistant.sln")) ||
                Directory.Exists(Path.Combine(dir, ".git")))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "debug-3d556c.log"));
    }
}
