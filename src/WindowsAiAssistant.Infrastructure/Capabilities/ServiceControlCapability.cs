using System.Diagnostics;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Infrastructure.Capabilities;

public interface IServiceControlOperator
{
    ServiceStatusSnapshot GetStatus(string serviceName);
    bool StartService(string serviceName);
    bool StopService(string serviceName);
}

public sealed class ServiceControlCapability : ICapability
{
    private readonly IServiceControlOperator _serviceControlOperator;

    public ServiceControlCapability()
        : this(new ScServiceControlOperator())
    {
    }

    public ServiceControlCapability(IServiceControlOperator serviceControlOperator)
    {
        _serviceControlOperator = serviceControlOperator ?? throw new ArgumentNullException(nameof(serviceControlOperator));
    }

    public string Name => "ServiceControlCapability";

    public IReadOnlyCollection<TargetKind> SupportedTargetKinds { get; } =
    [
        TargetKind.Service
    ];

    public bool CanHandle(AgentAction action, AgentExecutionContext context)
    {
        if (action.Target is null)
        {
            return false;
        }

        if (action.Target.Kind != TargetKind.Service)
        {
            return false;
        }

        if (!IsSupportedAction(action.ActionName))
        {
            return false;
        }

        return TryExtractServiceName(action.Target, out _);
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        AgentAction action,
        AgentExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        if (action.Target is null || !TryExtractServiceName(action.Target, out var serviceName))
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Blocked,
                "ServiceControlCapability blocked: service target is required.",
                "Blocked execution: no valid service target available.",
                action.ActionName,
                null,
                false,
                false,
                false,
                startedAtUtc));
        }

        if (action.ActionName.Equals("StartService", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteStartService(serviceName, startedAtUtc);
        }

        if (action.ActionName.Equals("StopService", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteStopService(serviceName, startedAtUtc);
        }

        return Task.FromResult(CreateResult(
            ExecutionStatus.Blocked,
            "ServiceControlCapability blocked: unsupported action.",
            "Blocked execution: unsupported service control action.",
            action.ActionName,
            serviceName,
            false,
            false,
            false,
            startedAtUtc));
    }

    private Task<ActionExecutionResult> ExecuteStartService(string serviceName, DateTimeOffset startedAtUtc)
    {
        try
        {
            var before = _serviceControlOperator.GetStatus(serviceName);
            if (!before.Exists)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.Blocked,
                    "ServiceControlCapability blocked: target service was not found.",
                    $"Service control failed: '{serviceName}' was not found.",
                    "StartService",
                    serviceName,
                    false,
                    false,
                    false,
                    startedAtUtc));
            }

            if (before.IsRunning)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.VerificationFailed,
                    "ServiceControlCapability verification failed: service is already running.",
                    $"Service control no-op: '{serviceName}' is already running.",
                    "StartService",
                    serviceName,
                    true,
                    true,
                    true,
                    startedAtUtc));
            }

            var startIssued = _serviceControlOperator.StartService(serviceName);

            var after = _serviceControlOperator.GetStatus(serviceName);
            if (startIssued && after.Exists && after.IsRunning)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.Succeeded,
                    "ServiceControlCapability executed start service action.",
                    $"Service control succeeded: '{serviceName}' started.",
                    "StartService",
                    serviceName,
                    true,
                    false,
                    true,
                    startedAtUtc));
            }

            return Task.FromResult(CreateResult(
                ExecutionStatus.VerificationFailed,
                "ServiceControlCapability verification failed: service did not transition to running state.",
                $"Service control failed: '{serviceName}' did not reach running state.",
                "StartService",
                serviceName,
                after.Exists,
                false,
                after.IsRunning,
                startedAtUtc));
        }
        catch
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Failed,
                "ServiceControlCapability failed: start action threw an exception.",
                $"Service control failed: '{serviceName}' could not be started.",
                "StartService",
                serviceName,
                false,
                false,
                false,
                startedAtUtc));
        }
    }

    private Task<ActionExecutionResult> ExecuteStopService(string serviceName, DateTimeOffset startedAtUtc)
    {
        try
        {
            var before = _serviceControlOperator.GetStatus(serviceName);
            if (!before.Exists)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.Blocked,
                    "ServiceControlCapability blocked: target service was not found.",
                    $"Service control failed: '{serviceName}' was not found.",
                    "StopService",
                    serviceName,
                    false,
                    false,
                    false,
                    startedAtUtc));
            }

            if (!before.IsRunning)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.VerificationFailed,
                    "ServiceControlCapability verification failed: service is already stopped.",
                    $"Service control no-op: '{serviceName}' is already stopped.",
                    "StopService",
                    serviceName,
                    true,
                    false,
                    false,
                    startedAtUtc));
            }

            var stopIssued = _serviceControlOperator.StopService(serviceName);

            var after = _serviceControlOperator.GetStatus(serviceName);
            if (stopIssued && after.Exists && !after.IsRunning)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.Succeeded,
                    "ServiceControlCapability executed stop service action.",
                    $"Service control succeeded: '{serviceName}' stopped.",
                    "StopService",
                    serviceName,
                    true,
                    true,
                    false,
                    startedAtUtc));
            }

            return Task.FromResult(CreateResult(
                ExecutionStatus.VerificationFailed,
                "ServiceControlCapability verification failed: service did not transition to stopped state.",
                $"Service control failed: '{serviceName}' did not reach stopped state.",
                "StopService",
                serviceName,
                after.Exists,
                true,
                after.IsRunning,
                startedAtUtc));
        }
        catch
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Failed,
                "ServiceControlCapability failed: stop action threw an exception.",
                $"Service control failed: '{serviceName}' could not be stopped.",
                "StopService",
                serviceName,
                false,
                false,
                false,
                startedAtUtc));
        }
    }

    private static bool IsSupportedAction(string actionName)
    {
        return actionName.Equals("StartService", StringComparison.OrdinalIgnoreCase) ||
               actionName.Equals("StopService", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractServiceName(TargetReference target, out string serviceName)
    {
        serviceName = string.Empty;

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("serviceName", out var metadataServiceName) &&
            !string.IsNullOrWhiteSpace(metadataServiceName))
        {
            serviceName = NormalizeServiceName(metadataServiceName);
            return !string.IsNullOrWhiteSpace(serviceName);
        }

        if (!string.IsNullOrWhiteSpace(target.NormalizedValue) &&
            !long.TryParse(target.NormalizedValue, out _))
        {
            serviceName = NormalizeServiceName(target.NormalizedValue);
            return !string.IsNullOrWhiteSpace(serviceName);
        }

        if (!string.IsNullOrWhiteSpace(target.DisplayName))
        {
            serviceName = NormalizeServiceName(target.DisplayName);
            return !string.IsNullOrWhiteSpace(serviceName);
        }

        return false;
    }

    private static string NormalizeServiceName(string serviceName)
    {
        return serviceName.Trim().Trim('\'', '"').ToLowerInvariant();
    }

    private static ActionExecutionResult CreateResult(
        ExecutionStatus status,
        string message,
        string outputText,
        string actionName,
        string? serviceName,
        bool serviceExists,
        bool serviceRunningBefore,
        bool serviceRunningAfter,
        DateTimeOffset startedAtUtc)
    {
        var outputData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["serviceControlAction"] = actionName,
            ["serviceExists"] = serviceExists ? "true" : "false",
            ["serviceRunningBefore"] = serviceRunningBefore ? "true" : "false",
            ["serviceRunningAfter"] = serviceRunningAfter ? "true" : "false"
        };

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            outputData["serviceName"] = serviceName;
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

    private sealed class ScServiceControlOperator : IServiceControlOperator
    {
        public ServiceStatusSnapshot GetStatus(string serviceName)
        {
            var normalizedServiceName = NormalizeServiceName(serviceName);
            if (string.IsNullOrWhiteSpace(normalizedServiceName) || !OperatingSystem.IsWindows())
            {
                return new ServiceStatusSnapshot(false, false);
            }

            var combinedText = RunScCommand("query", normalizedServiceName);
            if (combinedText.IndexOf("1060", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new ServiceStatusSnapshot(false, false);
            }

            var hasState = combinedText.IndexOf("STATE", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hasState)
            {
                return new ServiceStatusSnapshot(false, false);
            }

            var isRunning = combinedText.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
            return new ServiceStatusSnapshot(true, isRunning);
        }

        public bool StartService(string serviceName)
        {
            var normalizedServiceName = NormalizeServiceName(serviceName);
            if (string.IsNullOrWhiteSpace(normalizedServiceName) || !OperatingSystem.IsWindows())
            {
                return false;
            }

            var combinedText = RunScCommand("start", normalizedServiceName);
            return combinedText.IndexOf("1060", StringComparison.OrdinalIgnoreCase) < 0;
        }

        public bool StopService(string serviceName)
        {
            var normalizedServiceName = NormalizeServiceName(serviceName);
            if (string.IsNullOrWhiteSpace(normalizedServiceName) || !OperatingSystem.IsWindows())
            {
                return false;
            }

            var combinedText = RunScCommand("stop", normalizedServiceName);
            return combinedText.IndexOf("1060", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string RunScCommand(string command, string serviceName)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "sc",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processStartInfo.ArgumentList.Add(command);
            processStartInfo.ArgumentList.Add(serviceName);

            using var process = Process.Start(processStartInfo);
            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return $"{output}\n{error}";
        }
    }
}