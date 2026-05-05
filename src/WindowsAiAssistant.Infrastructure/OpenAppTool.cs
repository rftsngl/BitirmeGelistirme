using System.Diagnostics;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.ActionPrimitives;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Infrastructure.ActionPrimitives;
using WindowsAiAssistant.Infrastructure.Launch;

namespace WindowsAiAssistant.Infrastructure;

public sealed class OpenAppTool : ITool
{
    private readonly IActionPrimitiveExecutor _primitiveExecutor;

    public OpenAppTool(IActionPrimitiveExecutor primitiveExecutor)
    {
        _primitiveExecutor = primitiveExecutor ?? throw new ArgumentNullException(nameof(primitiveExecutor));
    }

    public OpenAppTool(ILauncher launcher)
        : this(CreatePrimitiveExecutor(launcher))
    {
    }

    public OpenAppTool(ExecutionPolicySettings settings, Func<ProcessStartInfo, Process?> processStarter)
        : this(CreateLauncher(settings, processStarter))
    {
    }

    public string Name => "OpenAppTool";

    public async Task<ToolResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var groundedTarget = request.PrimaryTargetGrounding;
        if (groundedTarget is null)
        {
            return CreateMissingGroundingFailureResult();
        }

        if (groundedTarget.Disposition != TargetGroundingDisposition.Resolved ||
            groundedTarget.Target.Kind is not (GroundedTargetKind.KnownApplication or GroundedTargetKind.PathLike) ||
            groundedTarget.ExecutionSuitability != TargetExecutionSuitability.ExecutableHere)
        {
            return CreateGroundingFailureResult(groundedTarget);
        }

        if (string.IsNullOrWhiteSpace(groundedTarget.Target.CanonicalValue))
        {
            return new ToolResult
            {
                Success = false,
                Output = "Blocked execution: grounded target canonical value is missing.",
                Message = "OpenAppTool blocked: grounded launch target canonical value is required."
            };
        }

        var primitiveRequest = new ActionPrimitiveExecutionRequest
        {
            CorrelationId = request.CorrelationId,
            Primitive = new LaunchTargetPrimitive(groundedTarget.Target.CanonicalValue),
            RequestedAtUtc = request.RequestedAtUtc,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool"] = Name,
                ["groundedTargetKind"] = groundedTarget.Target.Kind.ToString()
            }
        };

        var primitiveResult = await _primitiveExecutor.ExecuteAsync(primitiveRequest, cancellationToken);
        return MapPrimitiveResult(primitiveResult);
    }

    private static ToolResult MapPrimitiveResult(ActionPrimitiveExecutionResult primitiveResult)
    {
        var output = string.IsNullOrWhiteSpace(primitiveResult.OutputText)
            ? primitiveResult.Success
                ? "Real launch attempted for allowlisted app."
                : "Blocked execution: launch primitive blocked."
            : primitiveResult.OutputText;

        return new ToolResult
        {
            Success = primitiveResult.Success,
            Output = output,
            Message = primitiveResult.Message,
            PrimitiveExecution = primitiveResult
        };
    }

    private static ToolResult CreateGroundingFailureResult(TargetGroundingResult groundedTarget)
    {
        if (groundedTarget.Reason == TargetGroundingReason.AllowListMissing)
        {
            return new ToolResult
            {
                Success = false,
                Output = "Blocked execution: allowlist is empty or missing.",
                Message = "OpenAppTool blocked: configuration is fail-closed."
            };
        }

        return groundedTarget.Disposition switch
        {
            TargetGroundingDisposition.Unsupported => new ToolResult
            {
                Success = false,
                Output = $"Blocked execution: grounded target kind '{groundedTarget.Target.Kind}' is not supported.",
                Message = groundedTarget.Message
            },
            TargetGroundingDisposition.Ambiguous => new ToolResult
            {
                Success = false,
                Output = "Blocked execution: target grounding is ambiguous.",
                Message = groundedTarget.Message
            },
            _ => new ToolResult
            {
                Success = false,
                Output = "Blocked execution: target grounding is unresolved.",
                Message = groundedTarget.Reason == TargetGroundingReason.ExecutablePathNotAllowlisted
                    ? "OpenAppTool blocked: executable path target is not allowlisted."
                    : groundedTarget.Message
            }
        };
    }

    private static ToolResult CreateMissingGroundingFailureResult()
    {
        return new ToolResult
        {
            Success = false,
            Output = "Blocked execution: target grounding is missing.",
            Message = "OpenAppTool blocked: grounded target was not provided by the common execution path."
        };
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
}
