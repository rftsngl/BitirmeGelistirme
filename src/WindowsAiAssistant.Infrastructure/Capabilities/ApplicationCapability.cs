using System.Diagnostics;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.ActionPrimitives;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Infrastructure.ActionPrimitives;
using WindowsAiAssistant.Infrastructure.Launch;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Infrastructure.Capabilities;

public sealed class ApplicationCapability : ICapability
{
    private readonly IActionPrimitiveExecutor _primitiveExecutor;

    private ApplicationCapability(IActionPrimitiveExecutor primitiveExecutor)
    {
        _primitiveExecutor = primitiveExecutor ?? throw new ArgumentNullException(nameof(primitiveExecutor));
    }

    public ApplicationCapability(ILauncher launcher)
        : this(CreatePrimitiveExecutor(launcher))
    {
    }

    public ApplicationCapability(ExecutionPolicySettings settings, Func<ProcessStartInfo, Process?> processStarter)
        : this(CreateLauncher(settings, processStarter))
    {
    }

    public string Name => "ApplicationCapability";

    public IReadOnlyCollection<TargetKind> SupportedTargetKinds { get; } =
    [
        TargetKind.Application,
        TargetKind.Process,
        TargetKind.Path
    ];

    public bool CanHandle(AgentAction action, AgentExecutionContext context)
    {
        if (action.Target is null)
        {
            return false;
        }

        if (action.Target.Kind is not (TargetKind.Application or TargetKind.Process or TargetKind.Path))
        {
            return false;
        }

        if (!action.ActionName.Equals("OpenApplication", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(action.Target.NormalizedValue) ||
               !string.IsNullOrWhiteSpace(action.Target.OriginalText);
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        AgentAction action,
        AgentExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        return ExecuteUsingGroundedTargetAsync(context, startedAt, cancellationToken);
    }

    private async Task<ActionExecutionResult> ExecuteUsingGroundedTargetAsync(
        AgentExecutionContext context,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var groundedTarget = context.PrimaryTargetGrounding;
        if (groundedTarget is null)
        {
            return CreateMissingGroundingFailureResult(startedAt);
        }

        if (groundedTarget.Disposition != TargetGroundingDisposition.Resolved ||
            groundedTarget.Target.Kind is not (GroundedTargetKind.KnownApplication or GroundedTargetKind.PathLike) ||
            groundedTarget.ExecutionSuitability != TargetExecutionSuitability.ExecutableHere)
        {
            return CreateGroundingFailureResult(groundedTarget, startedAt);
        }

        if (string.IsNullOrWhiteSpace(groundedTarget.Target.CanonicalValue))
        {
            return CreateGroundingFailureResult(groundedTarget, startedAt);
        }

        var primitiveRequest = new ActionPrimitiveExecutionRequest
        {
            CorrelationId = context.CorrelationId.ToString("N"),
            Primitive = new LaunchTargetPrimitive(groundedTarget.Target.CanonicalValue),
            RequestedAtUtc = context.CreatedAtUtc,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["capability"] = Name,
                ["groundedTargetKind"] = groundedTarget.Target.Kind.ToString()
            }
        };

        var primitiveResult = await _primitiveExecutor.ExecuteAsync(primitiveRequest, cancellationToken);
        return MapPrimitiveResult(primitiveResult, groundedTarget, startedAt);
    }

    private static ActionExecutionResult MapPrimitiveResult(
        ActionPrimitiveExecutionResult primitiveResult,
        TargetGroundingResult groundedTarget,
        DateTimeOffset startedAt)
    {
        var primitiveBlocked = !primitiveResult.Success && !string.IsNullOrWhiteSpace(primitiveResult.BlockedReason);
        var status = primitiveResult.Success
            ? ExecutionStatus.Succeeded
            : primitiveBlocked
                ? ExecutionStatus.Blocked
                : ExecutionStatus.Failed;

        var outputData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["groundingDisposition"] = groundedTarget.Disposition.ToString(),
            ["groundedTargetKind"] = groundedTarget.Target.Kind.ToString(),
            ["groundingReason"] = groundedTarget.Reason.ToString(),
            ["groundingExecutionSuitability"] = groundedTarget.ExecutionSuitability.ToString(),
            ["primitiveKind"] = primitiveResult.PrimitiveKind.ToString(),
            ["primitiveSuccess"] = primitiveResult.Success ? "true" : "false"
        };

        if (!string.IsNullOrWhiteSpace(groundedTarget.Target.CanonicalValue))
        {
            outputData["groundedCanonicalValue"] = groundedTarget.Target.CanonicalValue;
        }

        if (!string.IsNullOrWhiteSpace(primitiveResult.BlockedReason))
        {
            outputData["primitiveBlockedReason"] = primitiveResult.BlockedReason;
        }

        if (!string.IsNullOrWhiteSpace(primitiveResult.ErrorCode))
        {
            outputData["primitiveErrorCode"] = primitiveResult.ErrorCode;
        }

        if (primitiveResult.Metadata is not null)
        {
            foreach (var (key, value) in primitiveResult.Metadata)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                outputData[$"primitive_{key}"] = value;
            }
        }

        return new ActionExecutionResult
        {
            Status = status,
            Message = primitiveResult.Message,
            IsVerified = false,
            OutputText = primitiveResult.OutputText,
            OutputData = outputData,
            ErrorCode = primitiveResult.ErrorCode,
            UsedFallback = false,
            PrimitiveExecution = primitiveResult,
            Verification = null,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ActionExecutionResult CreateGroundingFailureResult(
        TargetGroundingResult groundedTarget,
        DateTimeOffset startedAt)
    {
        var outputText = groundedTarget.Reason == TargetGroundingReason.AllowListMissing
            ? "Blocked execution: allowlist is empty or missing."
            : groundedTarget.Disposition switch
            {
                TargetGroundingDisposition.Unsupported =>
                    $"Blocked execution: grounded target kind '{groundedTarget.Target.Kind}' is not supported.",
                TargetGroundingDisposition.Ambiguous =>
                    "Blocked execution: target grounding is ambiguous.",
                _ =>
                    "Blocked execution: target grounding is unresolved."
            };

        var message = groundedTarget.Reason == TargetGroundingReason.AllowListMissing
            ? "ApplicationCapability blocked: configuration is fail-closed."
            : groundedTarget.Message;

        return CreateResult(
            ExecutionStatus.Blocked,
            message,
            outputText,
            groundedTarget,
            startedAt);
    }

    private static ActionExecutionResult CreateMissingGroundingFailureResult(DateTimeOffset startedAt)
    {
        return CreateResult(
            ExecutionStatus.Blocked,
            "ApplicationCapability blocked: grounded target was not provided by the common execution path.",
            "Blocked execution: target grounding is missing.",
            new TargetGroundingResult
            {
                Disposition = TargetGroundingDisposition.Unresolved,
                InputSource = TargetGroundingInputSource.RawInputFallback,
                ExecutionSuitability = TargetExecutionSuitability.NotExecutable,
                Target = new GroundedTarget
                {
                    Kind = GroundedTargetKind.Unknown,
                    OriginalText = string.Empty,
                    CanonicalValue = string.Empty,
                    Confidence = 0.0
                },
                Reason = TargetGroundingReason.EmptyInput,
                Message = "No grounded target was supplied to the application launch path."
            },
            startedAt);
    }

    private static ILauncher CreateLauncher(
        ExecutionPolicySettings settings,
        Func<ProcessStartInfo, Process?> processStarter)
    {
        return new Launcher(
            new LaunchTargetResolver(),
            new LaunchStrategyResolver(
            [
                new ShellExecutablePathLaunchStrategy(settings, processStarter),
                new ShellApplicationLaunchStrategy(settings, processStarter)
            ]));
    }

    private static IActionPrimitiveExecutor CreatePrimitiveExecutor(ILauncher launcher)
    {
        return new ActionPrimitiveExecutor(
        [
            new LaunchTargetPrimitiveHandler(launcher)
        ]);
    }

    private static ActionExecutionResult CreateResult(
        ExecutionStatus status,
        string message,
        string outputText,
        TargetGroundingResult groundedTarget,
        DateTimeOffset startedAt)
    {
        var outputData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["groundingDisposition"] = groundedTarget.Disposition.ToString(),
            ["groundedTargetKind"] = groundedTarget.Target.Kind.ToString(),
            ["groundingReason"] = groundedTarget.Reason.ToString(),
            ["groundingExecutionSuitability"] = groundedTarget.ExecutionSuitability.ToString()
        };

        if (!string.IsNullOrWhiteSpace(groundedTarget.Target.CanonicalValue))
        {
            outputData["groundedCanonicalValue"] = groundedTarget.Target.CanonicalValue;
        }

        return new ActionExecutionResult
        {
            Status = status,
            Message = message,
            IsVerified = false,
            OutputText = outputText,
            OutputData = outputData,
            ErrorCode = null,
            UsedFallback = false,
            PrimitiveExecution = null,
            Verification = null,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
