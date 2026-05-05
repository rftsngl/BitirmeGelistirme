using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Core.Routing;

internal static class CapabilityActionBuilders
{
    public const string ApplicationCapabilityName = "ApplicationCapability";
    public const string WindowProcessCapabilityName = "WindowProcessCapability";
    public const string ProcessVerificationCapabilityName = "ProcessVerificationCapability";
    public const string ForegroundAlignmentCapabilityName = "ForegroundAlignmentCapability";
    public const string ServiceStatusCapabilityName = "ServiceStatusCapability";
    public const string ServiceControlCapabilityName = "ServiceControlCapability";
    public const string FileVerificationCapabilityName = "FileVerificationCapability";
    public const string FileOpenCapabilityName = "FileOpenCapability";

    public static AgentAction BuildApplicationCapabilityAction(TargetReference target, string capabilityName)
    {
        var parameterKey = target.Kind == TargetKind.Path ? "path" : "app";
        return new AgentAction
        {
            CapabilityName = capabilityName,
            ActionName = "OpenApplication",
            Target = target,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [parameterKey] = target.NormalizedValue
            },
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Capability routing selected ApplicationCapability."
        };
    }

    public static AgentAction BuildWindowProcessCapabilityAction(TargetReference target, string capabilityName)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (TryExtractWindowHandle(target, out var windowHandle))
        {
            parameters["windowHandle"] = windowHandle.ToString();
        }

        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["target"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = capabilityName,
            ActionName = "FocusWindow",
            Target = target,
            Parameters = parameters,
            RequiresForeground = true,
            RequiresObservation = false,
            Notes = "Capability routing selected WindowProcessCapability."
        };
    }

    public static AgentAction BuildProcessVerificationCapabilityAction(TargetReference target, string capabilityName)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["process"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = capabilityName,
            ActionName = "VerifyProcessRunning",
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Capability routing selected ProcessVerificationCapability."
        };
    }

    public static AgentAction BuildForegroundAlignmentCapabilityAction(TargetReference target, string capabilityName)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["target"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = capabilityName,
            ActionName = "VerifyForegroundAlignment",
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = true,
            Notes = "Capability routing selected ForegroundAlignmentCapability."
        };
    }

    public static AgentAction BuildServiceStatusCapabilityAction(TargetReference target, string capabilityName)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["service"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = capabilityName,
            ActionName = "VerifyServiceStatus",
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Capability routing selected ServiceStatusCapability."
        };
    }

    public static AgentAction BuildServiceControlCapabilityAction(
        TargetReference target,
        string capabilityName,
        string actionName)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["service"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = capabilityName,
            ActionName = actionName,
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Capability routing selected ServiceControlCapability."
        };
    }

    public static AgentAction BuildFileVerificationCapabilityAction(TargetReference target, string capabilityName)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["file"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = capabilityName,
            ActionName = "VerifyFileExists",
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Capability routing selected FileVerificationCapability."
        };
    }

    public static AgentAction BuildFileOpenCapabilityAction(TargetReference target, string capabilityName)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["file"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = capabilityName,
            ActionName = "OpenExistingFile",
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Capability routing selected FileOpenCapability."
        };
    }

    public static AgentAction BuildExplicitCapabilityAction(
        string capabilityName,
        string? routedActionName,
        ExecuteActionPayload executeAction,
        TargetReference target)
    {
        if (capabilityName.Equals(ApplicationCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            return BuildApplicationCapabilityAction(target, capabilityName);
        }

        if (capabilityName.Equals(WindowProcessCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            return BuildWindowProcessCapabilityAction(target, capabilityName);
        }

        if (capabilityName.Equals(ProcessVerificationCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            return BuildProcessVerificationCapabilityAction(target, capabilityName);
        }

        if (capabilityName.Equals(ForegroundAlignmentCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            return BuildForegroundAlignmentCapabilityAction(target, capabilityName);
        }

        if (capabilityName.Equals(ServiceStatusCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            return BuildServiceStatusCapabilityAction(target, capabilityName);
        }

        if (capabilityName.Equals(ServiceControlCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            var actionName = string.IsNullOrWhiteSpace(routedActionName)
                ? "StartService"
                : routedActionName;
            return BuildServiceControlCapabilityAction(target, capabilityName, actionName);
        }

        if (capabilityName.Equals(FileVerificationCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            return BuildFileVerificationCapabilityAction(target, capabilityName);
        }

        if (capabilityName.Equals(FileOpenCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            return BuildFileOpenCapabilityAction(target, capabilityName);
        }

        return new AgentAction
        {
            CapabilityName = capabilityName,
            ActionName = string.IsNullOrWhiteSpace(routedActionName)
                ? executeAction.ActionType.ToString()
                : routedActionName,
            Target = target,
            Parameters = new Dictionary<string, string>(executeAction.Parameters.Values, StringComparer.OrdinalIgnoreCase),
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Capability routing selected an explicit capability hint."
        };
    }

    public static bool TryExtractWindowHandle(TargetReference target, out long windowHandle)
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
}
