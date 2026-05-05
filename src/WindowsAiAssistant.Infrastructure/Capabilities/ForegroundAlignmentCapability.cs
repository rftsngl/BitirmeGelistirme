using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Infrastructure.Capabilities;

public sealed class ForegroundAlignmentCapability : ICapability
{
    public string Name => "ForegroundAlignmentCapability";

    public IReadOnlyCollection<TargetKind> SupportedTargetKinds { get; } =
    [
        TargetKind.Window,
        TargetKind.Process,
        TargetKind.Application
    ];

    public bool CanHandle(AgentAction action, AgentExecutionContext context)
    {
        return action.Target is not null &&
               action.Target.Kind is TargetKind.Window or TargetKind.Process or TargetKind.Application &&
               action.ActionName.Equals("VerifyForegroundAlignment", StringComparison.OrdinalIgnoreCase);
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        AgentAction action,
        AgentExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        if (action.Target is null)
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Blocked,
                "ForegroundAlignmentCapability blocked: no target was provided.",
                "Blocked execution: no foreground alignment target available.",
                "none",
                "indeterminate",
                false,
                null,
                null,
                null,
                null,
                startedAtUtc));
        }

        return action.Target.Kind switch
        {
            TargetKind.Window => ExecuteWindowAlignment(action.Target, context.Observation, startedAtUtc),
            TargetKind.Process or TargetKind.Application => ExecuteProcessAlignment(action.Target, context.Observation, startedAtUtc),
            _ => Task.FromResult(CreateResult(
                ExecutionStatus.Blocked,
                "ForegroundAlignmentCapability blocked: unsupported target kind.",
                "Blocked execution: unsupported target kind for foreground alignment.",
                "none",
                "indeterminate",
                false,
                null,
                null,
                null,
                null,
                startedAtUtc))
        };
    }

    private static Task<ActionExecutionResult> ExecuteWindowAlignment(
        TargetReference target,
        ObservationSnapshot? observation,
        DateTimeOffset startedAtUtc)
    {
        var hasTargetHandle = TryExtractWindowHandle(target, out var targetHandle);
        var hasForegroundHandle = TryExtractForegroundWindowHandle(observation, out var foregroundHandle);

        if (!hasTargetHandle || !hasForegroundHandle)
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Blocked,
                "ForegroundAlignmentCapability blocked: window handle comparison is not available.",
                "Foreground alignment indeterminate: no comparable foreground window handle.",
                "none",
                "indeterminate",
                false,
                null,
                null,
                targetHandle,
                foregroundHandle,
                startedAtUtc));
        }

        var isMatched = targetHandle == foregroundHandle;
        return Task.FromResult(CreateResult(
            isMatched ? ExecutionStatus.Succeeded : ExecutionStatus.VerificationFailed,
            isMatched
                ? "ForegroundAlignmentCapability verified that target window is foreground-aligned."
                : "ForegroundAlignmentCapability verification failed: target window is not the current foreground window.",
            isMatched
                ? $"Foreground alignment succeeded: window handle '{targetHandle}' matches foreground window."
                : $"Foreground alignment failed: window handle '{targetHandle}' does not match foreground window '{foregroundHandle}'.",
            "window_handle",
            isMatched ? "matched" : "not_matched",
            isMatched,
            null,
            null,
            targetHandle,
            foregroundHandle,
            startedAtUtc));
    }

    private static Task<ActionExecutionResult> ExecuteProcessAlignment(
        TargetReference target,
        ObservationSnapshot? observation,
        DateTimeOffset startedAtUtc)
    {
        if (!TryExtractProcessName(target, out var targetProcess) ||
            !TryExtractForegroundProcessName(observation, out var foregroundProcess))
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Blocked,
                "ForegroundAlignmentCapability blocked: process comparison is not available.",
                "Foreground alignment indeterminate: no comparable foreground process name.",
                "none",
                "indeterminate",
                false,
                null,
                null,
                null,
                null,
                startedAtUtc));
        }

        var isMatched = targetProcess.Equals(foregroundProcess, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(CreateResult(
            isMatched ? ExecutionStatus.Succeeded : ExecutionStatus.VerificationFailed,
            isMatched
                ? "ForegroundAlignmentCapability verified that target process/application is foreground-aligned."
                : "ForegroundAlignmentCapability verification failed: target process/application is not the current foreground process.",
            isMatched
                ? $"Foreground alignment succeeded: process '{targetProcess}' matches foreground process."
                : $"Foreground alignment failed: process '{targetProcess}' does not match foreground process '{foregroundProcess}'.",
            "process_name",
            isMatched ? "matched" : "not_matched",
            isMatched,
            targetProcess,
            foregroundProcess,
            null,
            null,
            startedAtUtc));
    }

    private static bool TryExtractWindowHandle(TargetReference target, out long windowHandle)
    {
        windowHandle = 0;

        if (long.TryParse(target.NormalizedValue, out var parsedNormalized) && parsedNormalized > 0)
        {
            windowHandle = parsedNormalized;
            return true;
        }

        if (target.Metadata is null)
        {
            return false;
        }

        if (target.Metadata.TryGetValue("windowHandle", out var metadataHandle) &&
            long.TryParse(metadataHandle, out var parsedMetadata) &&
            parsedMetadata > 0)
        {
            windowHandle = parsedMetadata;
            return true;
        }

        return false;
    }

    private static bool TryExtractForegroundWindowHandle(ObservationSnapshot? observation, out long windowHandle)
    {
        windowHandle = 0;

        var activeWindow = observation?.ActiveWindow;
        if (activeWindow?.Handle is long handle && handle > 0)
        {
            windowHandle = handle;
            return true;
        }

        return false;
    }

    private static bool TryExtractProcessName(TargetReference target, out string processName)
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

    private static bool TryExtractForegroundProcessName(ObservationSnapshot? observation, out string processName)
    {
        processName = string.Empty;

        var activeProcessName = observation?.ActiveProcessName;
        if (!string.IsNullOrWhiteSpace(activeProcessName))
        {
            processName = NormalizeProcessName(activeProcessName);
            return !string.IsNullOrWhiteSpace(processName);
        }

        var activeWindowProcessName = observation?.ActiveWindow?.ProcessName;
        if (!string.IsNullOrWhiteSpace(activeWindowProcessName))
        {
            processName = NormalizeProcessName(activeWindowProcessName);
            return !string.IsNullOrWhiteSpace(processName);
        }

        return false;
    }

    private static string NormalizeProcessName(string processName)
    {
        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.ToLowerInvariant();
    }

    private static ActionExecutionResult CreateResult(
        ExecutionStatus status,
        string message,
        string outputText,
        string matchBasis,
        string alignmentResult,
        bool isAligned,
        string? targetProcessName,
        string? foregroundProcessName,
        long? targetWindowHandle,
        long? foregroundWindowHandle,
        DateTimeOffset startedAtUtc)
    {
        var outputData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alignmentMatchBasis"] = matchBasis,
            ["alignmentResult"] = alignmentResult,
            ["isAligned"] = isAligned ? "true" : "false"
        };

        if (!string.IsNullOrWhiteSpace(targetProcessName))
        {
            outputData["targetProcessName"] = targetProcessName;
        }

        if (!string.IsNullOrWhiteSpace(foregroundProcessName))
        {
            outputData["foregroundProcessName"] = foregroundProcessName;
        }

        if (targetWindowHandle.HasValue)
        {
            outputData["targetWindowHandle"] = targetWindowHandle.Value.ToString();
        }

        if (foregroundWindowHandle.HasValue)
        {
            outputData["foregroundWindowHandle"] = foregroundWindowHandle.Value.ToString();
        }

        return new ActionExecutionResult
        {
            Status = status,
            Message = message,
            IsVerified = status == ExecutionStatus.Succeeded,
            OutputText = outputText,
            OutputData = outputData,
            ErrorCode = null,
            UsedFallback = false,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }
}