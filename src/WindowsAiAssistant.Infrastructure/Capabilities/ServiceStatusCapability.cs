using System.Diagnostics;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Infrastructure.Capabilities;

public interface IServiceStatusInspector
{
    ServiceStatusSnapshot GetStatus(string serviceName);
}

public readonly record struct ServiceStatusSnapshot(bool Exists, bool IsRunning);

public sealed class ServiceStatusCapability : ICapability
{
    private readonly IServiceStatusInspector _serviceStatusInspector;

    public ServiceStatusCapability()
        : this(new ScServiceStatusInspector())
    {
    }

    public ServiceStatusCapability(IServiceStatusInspector serviceStatusInspector)
    {
        _serviceStatusInspector = serviceStatusInspector ?? throw new ArgumentNullException(nameof(serviceStatusInspector));
    }

    public string Name => "ServiceStatusCapability";

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

        if (!action.ActionName.Equals("VerifyServiceStatus", StringComparison.OrdinalIgnoreCase))
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
                "ServiceStatusCapability blocked: service target is required.",
                "Blocked execution: no valid service target available.",
                null,
                false,
                false,
                startedAtUtc));
        }

        try
        {
            var snapshot = _serviceStatusInspector.GetStatus(serviceName);
            if (!snapshot.Exists)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.Blocked,
                    "ServiceStatusCapability blocked: target service was not found.",
                    $"Service verification indeterminate: '{serviceName}' was not found.",
                    serviceName,
                    false,
                    false,
                    startedAtUtc));
            }

            if (snapshot.IsRunning)
            {
                return Task.FromResult(CreateResult(
                    ExecutionStatus.Succeeded,
                    "ServiceStatusCapability verified that service is running.",
                    $"Service verification succeeded: '{serviceName}' is running.",
                    serviceName,
                    true,
                    true,
                    startedAtUtc));
            }

            return Task.FromResult(CreateResult(
                ExecutionStatus.VerificationFailed,
                "ServiceStatusCapability verification failed: service is not running.",
                $"Service verification failed: '{serviceName}' is not running.",
                serviceName,
                true,
                false,
                startedAtUtc));
        }
        catch
        {
            return Task.FromResult(CreateResult(
                ExecutionStatus.Failed,
                "ServiceStatusCapability failed: service status verification threw an exception.",
                $"Service verification failed: '{serviceName}' could not be verified.",
                serviceName,
                false,
                false,
                startedAtUtc));
        }
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
        string? serviceName,
        bool serviceExists,
        bool serviceRunning,
        DateTimeOffset startedAtUtc)
    {
        var outputData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["serviceExists"] = serviceExists ? "true" : "false",
            ["serviceRunning"] = serviceRunning ? "true" : "false"
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

    private sealed class ScServiceStatusInspector : IServiceStatusInspector
    {
        public ServiceStatusSnapshot GetStatus(string serviceName)
        {
            var normalizedServiceName = NormalizeServiceName(serviceName);
            if (string.IsNullOrWhiteSpace(normalizedServiceName) || !OperatingSystem.IsWindows())
            {
                return new ServiceStatusSnapshot(false, false);
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "sc",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processStartInfo.ArgumentList.Add("query");
            processStartInfo.ArgumentList.Add(normalizedServiceName);

            using var process = Process.Start(processStartInfo);
            if (process is null)
            {
                return new ServiceStatusSnapshot(false, false);
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            var combinedText = $"{output}\n{error}";
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
    }
}