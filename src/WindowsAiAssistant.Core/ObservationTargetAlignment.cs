using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Core;

internal static class ObservationTargetAlignment
{
    public static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.ToLowerInvariant();
    }

    public static bool TryExtractTargetProcessName(TargetReference target, out string processName)
    {
        processName = string.Empty;

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("processName", out var metadataProcessName) &&
            !string.IsNullOrWhiteSpace(metadataProcessName))
        {
            processName = NormalizeProcessName(metadataProcessName);
            return !string.IsNullOrWhiteSpace(processName);
        }

        if (!string.IsNullOrWhiteSpace(target.NormalizedValue) &&
            !long.TryParse(target.NormalizedValue, out _))
        {
            processName = NormalizeProcessName(target.NormalizedValue);
            return !string.IsNullOrWhiteSpace(processName);
        }

        if (!string.IsNullOrWhiteSpace(target.DisplayName))
        {
            processName = NormalizeProcessName(target.DisplayName);
            return !string.IsNullOrWhiteSpace(processName);
        }

        return false;
    }

    public static bool TryExtractObservedProcessName(ObservationSnapshot? observation, out string processName)
    {
        processName = NormalizeProcessName(observation?.ActiveProcessName ?? observation?.ActiveWindow?.ProcessName);
        return !string.IsNullOrWhiteSpace(processName);
    }

    public static bool TryExtractWindowHandle(TargetReference target, out long windowHandle)
    {
        windowHandle = 0;

        if (long.TryParse(target.NormalizedValue, out var parsedNormalized) && parsedNormalized > 0)
        {
            windowHandle = parsedNormalized;
            return true;
        }

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("windowHandle", out var metadataHandle) &&
            long.TryParse(metadataHandle, out var parsedMetadata) &&
            parsedMetadata > 0)
        {
            windowHandle = parsedMetadata;
            return true;
        }

        return false;
    }

    public static bool TryExtractTargetWindowTitle(TargetReference target, out string windowTitle)
    {
        windowTitle = string.Empty;

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("windowTitle", out var metadataWindowTitle) &&
            !string.IsNullOrWhiteSpace(metadataWindowTitle))
        {
            windowTitle = metadataWindowTitle.Trim();
            return true;
        }

        if (!string.IsNullOrWhiteSpace(target.DisplayName))
        {
            windowTitle = target.DisplayName.Trim();
            return true;
        }

        return false;
    }

    public static int ComputeWindowTitleAffinityScore(TargetReference target, string? activeWindowTitle)
    {
        if (string.IsNullOrWhiteSpace(activeWindowTitle) ||
            !TryExtractTargetWindowTitle(target, out var candidateTitle))
        {
            return 0;
        }

        var normalizedActive = NormalizeWindowTitle(activeWindowTitle);
        var normalizedCandidate = NormalizeWindowTitle(candidateTitle);
        if (string.IsNullOrWhiteSpace(normalizedActive) || string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            return 0;
        }

        if (normalizedActive.Equals(normalizedCandidate, StringComparison.Ordinal))
        {
            return 18;
        }

        if (normalizedActive.Contains(normalizedCandidate, StringComparison.Ordinal) ||
            normalizedCandidate.Contains(normalizedActive, StringComparison.Ordinal))
        {
            return 12;
        }

        var activeTokens = normalizedActive.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidateTokens = normalizedCandidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (activeTokens.Length == 0 || candidateTokens.Length == 0)
        {
            return 0;
        }

        var overlap = 0;
        foreach (var token in candidateTokens)
        {
            if (activeTokens.Contains(token, StringComparer.Ordinal))
            {
                overlap++;
            }
        }

        return overlap switch
        {
            >= 3 => 10,
            2 => 7,
            1 => 4,
            _ => 0
        };
    }

    private static string NormalizeWindowTitle(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return string.Join(
            ' ',
            normalized
                .Split([' ', '\t', '-', '_', '|', ':', '.', ',', ';', '(', ')', '[', ']', '{', '}'], StringSplitOptions.RemoveEmptyEntries));
    }
}
