using System.Diagnostics;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Infrastructure.Capabilities;

public interface IRunningProcessInspector
{
    bool IsProcessRunning(string processName);
}

public sealed class ProcessVerificationCapability : ICapability
{
    private readonly IRunningProcessInspector _processInspector;

    public ProcessVerificationCapability()
        : this(new RunningProcessInspector())
    {
    }

    public ProcessVerificationCapability(IRunningProcessInspector processInspector)
    {
        _processInspector = processInspector ?? throw new ArgumentNullException(nameof(processInspector));
    }

    public string Name => "ProcessVerificationCapability";

    public IReadOnlyCollection<TargetKind> SupportedTargetKinds { get; } =
    [
        TargetKind.Process,
        TargetKind.Application
    ];

    public bool CanHandle(AgentAction action, AgentExecutionContext context)
    {
        if (action.Target is null)
        {
            return false;
        }

        if (action.Target.Kind is not (TargetKind.Process or TargetKind.Application))
        {
            return false;
        }

        if (!action.ActionName.Equals("VerifyProcessRunning", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryExtractProcessName(action.Target, out _);
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        AgentAction action,
        AgentExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        if (action.Target is null || !TryExtractProcessName(action.Target, out var processName))
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Blocked,
                "ProcessVerificationCapability blocked: process/application target is required.",
                "Blocked execution: no valid process target available.",
                null,
                false,
                startedAtUtc));
        }

        try
        {
            var isRunning = _processInspector.IsProcessRunning(processName);
            if (isRunning)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.Succeeded,
                    "ProcessVerificationCapability verified that process is running.",
                    $"Process verification succeeded: '{processName}' is running.",
                    processName,
                    true,
                    startedAtUtc));
            }

            return Task.FromResult(CreateResult(
                ExecutionStatus.VerificationFailed,
                "ProcessVerificationCapability verification failed: process is not running.",
                $"Process verification failed: '{processName}' is not running.",
                processName,
                false,
                startedAtUtc));
        }
        catch
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Failed,
                "ProcessVerificationCapability failed: process verification threw an exception.",
                $"Process verification failed: '{processName}' could not be verified.",
                processName,
                false,
                startedAtUtc));
        }
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
        string? processName,
        bool isRunning,
        DateTimeOffset startedAtUtc)
    {
        var outputData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["isRunning"] = isRunning ? "true" : "false"
        };

        if (!string.IsNullOrWhiteSpace(processName))
        {
            outputData["processName"] = processName;
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

    private sealed class RunningProcessInspector : IRunningProcessInspector
    {
        public bool IsProcessRunning(string processName)
        {
            var normalized = NormalizeProcessName(processName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            Process[]? processes = null;
            try
            {
                processes = Process.GetProcessesByName(normalized);
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (processes is not null)
                {
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
            }
        }
    }
}
