using WindowsAiAssistant.Contracts.Models.ActionPrimitives;
using WindowsAiAssistant.Infrastructure.Launch;

namespace WindowsAiAssistant.Infrastructure.ActionPrimitives;

public sealed class LaunchTargetPrimitiveHandler : IActionPrimitiveHandler
{
    private readonly ILauncher _launcher;

    public LaunchTargetPrimitiveHandler(ILauncher launcher)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    public bool CanHandle(ActionPrimitive primitive)
    {
        return primitive.Kind == ActionPrimitiveKind.LaunchTarget;
    }

    public async Task<ActionPrimitiveExecutionResult> ExecuteAsync(
        ActionPrimitiveExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        if (request.Primitive is not LaunchTargetPrimitive launchPrimitive)
        {
            return CreateBlocked(
                startedAt,
                "LaunchTarget primitive blocked: invalid payload type.",
                "Primitive payload did not match LaunchTarget.");
        }

        if (string.IsNullOrWhiteSpace(launchPrimitive.Target))
        {
            return CreateBlocked(
                startedAt,
                "LaunchTarget primitive blocked: empty target.",
                "Launch target was empty.");
        }

        var targetReference = new LaunchTargetReference
        {
            RawReference = launchPrimitive.Target,
            PreferredKind = LaunchTargetKind.RegisteredApplication
        };

        var launch = await _launcher.LaunchAsync(targetReference, cancellationToken);
        var normalizedTarget = launch.Request.Target.NormalizedReference;
        var originalTarget = launch.Request.Target.OriginalReference;

        if (launch.Success)
        {
            var successTargetText = string.IsNullOrWhiteSpace(normalizedTarget)
                ? originalTarget
                : normalizedTarget;

            return new ActionPrimitiveExecutionResult
            {
                Success = true,
                PrimitiveKind = ActionPrimitiveKind.LaunchTarget,
                Message = "LaunchTarget primitive executed real launch path.",
                OutputText = $"Real launch attempted for target: {successTargetText}",
                ErrorCode = null,
                BlockedReason = null,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Metadata = CreateMetadata(launch)
            };
        }

        return launch.FailureReason switch
        {
            LaunchFailureReason.EmptyTarget => CreateBlocked(
                startedAt,
                "LaunchTarget primitive blocked: no target provided.",
                "Launch target was empty.",
                CreateMetadata(launch)),
            LaunchFailureReason.UnsupportedTargetKind => CreateBlocked(
                startedAt,
                "LaunchTarget primitive blocked: unsupported launch target kind.",
                "Unsupported launch target kind.",
                CreateMetadata(launch)),
            LaunchFailureReason.AllowListMissing => CreateBlocked(
                startedAt,
                "LaunchTarget primitive blocked: allowlist configuration is fail-closed.",
                "Allowlist is empty or missing.",
                CreateMetadata(launch)),
            LaunchFailureReason.NotAllowlisted => CreateBlocked(
                startedAt,
                "LaunchTarget primitive blocked: non-allowlisted target.",
                $"Target '{originalTarget}' is not allowlisted.",
                CreateMetadata(launch)),
            LaunchFailureReason.LaunchReturnedNoProcess => CreateFailure(
                startedAt,
                "LaunchTarget primitive attempted real launch but no process was created.",
                $"Real execution failed: {normalizedTarget} returned no process.",
                "LaunchReturnedNoProcess",
                CreateMetadata(launch)),
            LaunchFailureReason.LaunchException => CreateFailure(
                startedAt,
                $"LaunchTarget primitive launch failed: {launch.ExceptionMessage}",
                $"Real execution failed for {normalizedTarget}.",
                "LaunchException",
                CreateMetadata(launch)),
            _ => CreateFailure(
                startedAt,
                "LaunchTarget primitive failed: launch strategy reported unsupported outcome.",
                "Real execution failed for requested app.",
                "UnsupportedLaunchOutcome",
                CreateMetadata(launch))
        };
    }

    private static Dictionary<string, string> CreateMetadata(LaunchExecution launch)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["targetKind"] = launch.Request.Target.Kind.ToString(),
            ["targetOriginal"] = launch.Request.Target.OriginalReference,
            ["targetNormalized"] = launch.Request.Target.NormalizedReference,
            ["failureReason"] = launch.FailureReason.ToString()
        };

        if (!string.IsNullOrWhiteSpace(launch.StrategyName))
        {
            metadata["launchStrategy"] = launch.StrategyName;
        }

        if (!string.IsNullOrWhiteSpace(launch.ExceptionMessage))
        {
            metadata["exceptionMessage"] = launch.ExceptionMessage;
        }

        return metadata;
    }

    private static ActionPrimitiveExecutionResult CreateBlocked(
        DateTimeOffset startedAt,
        string message,
        string blockedReason,
        IDictionary<string, string>? metadata = null)
    {
        return new ActionPrimitiveExecutionResult
        {
            Success = false,
            PrimitiveKind = ActionPrimitiveKind.LaunchTarget,
            Message = message,
            OutputText = "Blocked execution: launch primitive blocked.",
            ErrorCode = null,
            BlockedReason = blockedReason,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Metadata = metadata
        };
    }

    private static ActionPrimitiveExecutionResult CreateFailure(
        DateTimeOffset startedAt,
        string message,
        string outputText,
        string errorCode,
        IDictionary<string, string>? metadata = null)
    {
        return new ActionPrimitiveExecutionResult
        {
            Success = false,
            PrimitiveKind = ActionPrimitiveKind.LaunchTarget,
            Message = message,
            OutputText = outputText,
            ErrorCode = errorCode,
            BlockedReason = null,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Metadata = metadata
        };
    }
}
