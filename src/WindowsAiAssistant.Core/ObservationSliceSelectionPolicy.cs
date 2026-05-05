using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Core;

internal static class ObservationSliceSelectionPolicy
{
    private static readonly TimeSpan MaxOperationalObservationAge = TimeSpan.FromMinutes(2);
    private static readonly string[] CurrentContextHints =
    [
        "current window",
        "this window",
        "active window",
        "foreground window",
        "current app",
        "this app",
        "active app",
        "foreground app"
    ];

    public static bool HasForegroundSignal(ObservationSnapshot? observation)
    {
        return observation?.ActiveWindow?.IsForeground == true ||
               observation?.ActiveWindow?.Handle is long ||
               !string.IsNullOrWhiteSpace(observation?.ActiveWindow?.Title) ||
               !string.IsNullOrWhiteSpace(observation?.ActiveProcessName) ||
               !string.IsNullOrWhiteSpace(observation?.ActiveWindow?.ProcessName);
    }

    public static bool IsOperationallyUsable(ObservationSnapshot? observation, out string reason)
    {
        reason = string.Empty;
        if (observation is null)
        {
            reason = "missing";
            return false;
        }

        if (DateTimeOffset.UtcNow - observation.CapturedAtUtc > MaxOperationalObservationAge)
        {
            reason = "stale";
            return false;
        }

        var desktopSummary = observation.DesktopStateSummary ?? string.Empty;
        if (desktopSummary.Contains("secure desktop", StringComparison.OrdinalIgnoreCase) ||
            desktopSummary.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
            desktopSummary.Contains("no real observation", StringComparison.OrdinalIgnoreCase))
        {
            reason = "untrusted_desktop_context";
            return false;
        }

        if (!HasForegroundSignal(observation))
        {
            reason = "foreground_signal_missing";
            return false;
        }

        reason = "ok";
        return true;
    }

    public static bool IsCurrentContextIntent(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        foreach (var hint in CurrentContextHints)
        {
            if (commandText.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsStrongTargetConflict(TargetReference? target, ObservationSnapshot? observation)
    {
        if (target is null || observation is null)
        {
            return false;
        }

        if (ObservationTargetAlignment.TryExtractWindowHandle(target, out var targetHandle) &&
            observation.ActiveWindow?.Handle is long activeHandle &&
            targetHandle > 0 &&
            activeHandle > 0 &&
            targetHandle != activeHandle)
        {
            return true;
        }

        if (ObservationTargetAlignment.TryExtractTargetProcessName(target, out var targetProcessName) &&
            ObservationTargetAlignment.TryExtractObservedProcessName(observation, out var observedProcessName) &&
            !targetProcessName.Equals(observedProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var shouldEvaluateTitleConflict =
            target.Kind == Contracts.Models.Agent.Enums.TargetKind.Window ||
            (target.Metadata is not null && target.Metadata.ContainsKey("windowTitle"));
        var titleAffinity = ObservationTargetAlignment.ComputeWindowTitleAffinityScore(target, observation.ActiveWindow?.Title);
        if (shouldEvaluateTitleConflict &&
            !string.IsNullOrWhiteSpace(observation.ActiveWindow?.Title) &&
            titleAffinity <= 0)
        {
            return true;
        }

        return false;
    }
}
