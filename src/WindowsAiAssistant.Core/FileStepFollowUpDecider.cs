using System.IO;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Core;

public sealed class FileStepFollowUpDecider : IStepFollowUpDecider
{
    private const string FileVerificationCapabilityName = "FileVerificationCapability";
    private const string VerifyFileExistsActionName = "VerifyFileExists";
    private const string FileOpenCapabilityName = "FileOpenCapability";
    private const string OpenExistingFileActionName = "OpenExistingFile";
    private const string WindowProcessCapabilityName = "WindowProcessCapability";
    private const string FocusWindowActionName = "FocusWindow";

    private readonly ICapabilityRegistry _capabilityRegistry;

    public FileStepFollowUpDecider(ICapabilityRegistry capabilityRegistry)
    {
        _capabilityRegistry = capabilityRegistry ?? throw new ArgumentNullException(nameof(capabilityRegistry));
    }

    public StepDecision DecideNextStep(AgentStepState state, StepFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(feedback);

        if (feedback.ExecutedAction.CapabilityName.Equals(FileVerificationCapabilityName, StringComparison.OrdinalIgnoreCase) &&
            feedback.ExecutedAction.ActionName.Equals(VerifyFileExistsActionName, StringComparison.OrdinalIgnoreCase))
        {
            return DecideAfterVerify(feedback);
        }

        if (feedback.ExecutedAction.CapabilityName.Equals(FileOpenCapabilityName, StringComparison.OrdinalIgnoreCase) &&
            feedback.ExecutedAction.ActionName.Equals(OpenExistingFileActionName, StringComparison.OrdinalIgnoreCase))
        {
            return DecideAfterOpen(state, feedback);
        }

        return Stop(feedback.ExecutionResult.Message);
    }

    private StepDecision DecideAfterVerify(StepFeedback feedback)
    {
        if (!TryGetBoolOutput(feedback.ExecutionResult, out var fileExists, "fileExists", "exists") || !fileExists)
        {
            return Stop(feedback.ExecutionResult.Message);
        }

        if (_capabilityRegistry.FindByName(FileOpenCapabilityName) is null)
        {
            return Stop("File verification completed, but open capability is unavailable for follow-up.");
        }

        if (feedback.ExecutedAction.Target is null)
        {
            return Stop("File verification completed, but reusable file target is unavailable for follow-up.");
        }

        return new StepDecision
        {
            Disposition = StepContinuationDisposition.Continue,
            NextAction = BuildOpenFileAction(feedback.ExecutedAction.Target),
            Message = "File verification completed: file exists, continuing with open action."
        };
    }

    private StepDecision DecideAfterOpen(AgentStepState state, StepFeedback feedback)
    {
        if (!TryGetBoolOutput(feedback.ExecutionResult, out var fileOpened, "fileOpened", "opened") || !fileOpened)
        {
            return Stop(feedback.ExecutionResult.Message);
        }

        if (_capabilityRegistry.FindByName(WindowProcessCapabilityName) is null)
        {
            return Stop("File open completed successfully. Focus follow-up is skipped because focus capability is unavailable.");
        }

        var observation = feedback.RefreshedObservation ?? state.CurrentObservation ?? state.ExecutionContext.Observation;
        if (!TryCreateFileFlowFocusTarget(observation, feedback.ExecutedAction.Target, out var focusTarget))
        {
            return Stop("File open completed successfully. Focus follow-up is skipped because no focusable window/process target was resolved.");
        }

        return new StepDecision
        {
            Disposition = StepContinuationDisposition.Continue,
            NextAction = BuildFocusWindowAction(focusTarget),
            Message = "File open completed: continuing with window focus follow-up."
        };
    }

    private static bool TryGetBoolOutput(
        ActionExecutionResult executionResult,
        out bool value,
        params string[] keys)
    {
        value = false;
        if (executionResult.OutputData is null || keys.Length == 0)
        {
            return false;
        }

        foreach (var key in keys)
        {
            if (executionResult.OutputData.TryGetValue(key, out var raw) &&
                bool.TryParse(raw, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static AgentAction BuildOpenFileAction(TargetReference target)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["path"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = FileOpenCapabilityName,
            ActionName = OpenExistingFileActionName,
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Follow-up open step after successful file verification."
        };
    }

    private static AgentAction BuildFocusWindowAction(TargetReference target)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["target"] = target.NormalizedValue;
        }

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("processName", out var processName) &&
            !string.IsNullOrWhiteSpace(processName))
        {
            parameters["processName"] = processName;
        }

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("windowHandle", out var windowHandle) &&
            !string.IsNullOrWhiteSpace(windowHandle))
        {
            parameters["windowHandle"] = windowHandle;
        }

        return new AgentAction
        {
            CapabilityName = WindowProcessCapabilityName,
            ActionName = FocusWindowActionName,
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Follow-up focus step after successful file open."
        };
    }

    private static bool TryCreateFileFlowFocusTarget(
        ObservationSnapshot? observation,
        TargetReference? fallbackTarget,
        out TargetReference target)
    {
        target = new TargetReference();

        var activeWindow = observation?.ActiveWindow;
        if (activeWindow?.Handle is long handle && handle > 0)
        {
            target = new TargetReference
            {
                Kind = TargetKind.Window,
                OriginalText = "current window",
                NormalizedValue = handle.ToString(),
                DisplayName = string.IsNullOrWhiteSpace(activeWindow.Title)
                    ? handle.ToString()
                    : activeWindow.Title,
                Confidence = 0.7,
                ResolutionReasonKind = TargetResolutionReasonKind.ObservationContext,
                ResolutionSourceText = "post-open observation",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["windowHandle"] = handle.ToString(),
                    ["processName"] = activeWindow.ProcessName ?? string.Empty,
                    ["windowTitle"] = activeWindow.Title ?? string.Empty
                }
            };

            return true;
        }

        var processName = observation?.ActiveProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return TryCreateFallbackFocusTarget(fallbackTarget, out target);
        }

        target = new TargetReference
        {
            Kind = TargetKind.Process,
            OriginalText = "current app",
            NormalizedValue = ObservationTargetAlignment.NormalizeProcessName(processName),
            DisplayName = processName,
            Confidence = 0.65,
            ResolutionReasonKind = TargetResolutionReasonKind.ObservationContext,
            ResolutionSourceText = "post-open observation",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["processName"] = processName
            }
        };

        return true;
    }

    private static bool TryCreateFallbackFocusTarget(TargetReference? fallbackTarget, out TargetReference target)
    {
        target = new TargetReference();
        if (fallbackTarget is null)
        {
            return false;
        }

        if (fallbackTarget.Metadata is not null &&
            fallbackTarget.Metadata.TryGetValue("windowHandle", out var windowHandleText) &&
            long.TryParse(windowHandleText, out var windowHandle) &&
            windowHandle > 0)
        {
            var processName = fallbackTarget.Metadata.TryGetValue("processName", out var fallbackProcessName)
                ? fallbackProcessName
                : string.Empty;
            target = new TargetReference
            {
                Kind = TargetKind.Window,
                OriginalText = "fallback window",
                NormalizedValue = windowHandle.ToString(),
                DisplayName = string.IsNullOrWhiteSpace(fallbackTarget.DisplayName)
                    ? windowHandle.ToString()
                    : fallbackTarget.DisplayName,
                Confidence = 0.6,
                ResolutionReasonKind = TargetResolutionReasonKind.ObservationContext,
                ResolutionSourceText = "follow-up fallback target",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["windowHandle"] = windowHandle.ToString(),
                    ["processName"] = processName ?? string.Empty,
                    ["windowTitle"] = fallbackTarget.DisplayName ?? string.Empty
                }
            };
            return true;
        }

        var processNameCandidate = TryGetFallbackProcessName(fallbackTarget);
        if (string.IsNullOrWhiteSpace(processNameCandidate))
        {
            return false;
        }

        target = new TargetReference
        {
            Kind = TargetKind.Process,
            OriginalText = "fallback process",
            NormalizedValue = ObservationTargetAlignment.NormalizeProcessName(processNameCandidate),
            DisplayName = processNameCandidate,
            Confidence = 0.55,
            ResolutionReasonKind = TargetResolutionReasonKind.ObservationContext,
            ResolutionSourceText = "follow-up fallback target",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["processName"] = processNameCandidate
            }
        };

        return true;
    }

    private static string? TryGetFallbackProcessName(TargetReference fallbackTarget)
    {
        if (fallbackTarget.Metadata is not null &&
            fallbackTarget.Metadata.TryGetValue("processName", out var processName) &&
            !string.IsNullOrWhiteSpace(processName))
        {
            return processName;
        }

        if (!string.IsNullOrWhiteSpace(fallbackTarget.NormalizedValue) &&
            !fallbackTarget.NormalizedValue.Contains(Path.DirectorySeparatorChar) &&
            !fallbackTarget.NormalizedValue.Contains(Path.AltDirectorySeparatorChar))
        {
            return fallbackTarget.NormalizedValue;
        }

        return null;
    }

    private static StepDecision Stop(string message)
    {
        return new StepDecision
        {
            Disposition = StepContinuationDisposition.Stop,
            NextAction = null,
            Message = message
        };
    }
}
