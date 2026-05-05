using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Core.Routing;

public sealed class DefaultCapabilityInvocationRouter : ICapabilityInvocationRouter
{
    public string Name => "default";

    public bool TryRoute(
        AiDecision decision,
        AgentExecutionContext executionContext,
        out CapabilityInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(executionContext);

        invocation = default!;

        if (TryResolveExplicit(decision, executionContext, out invocation!))
        {
            return true;
        }

        if (decision.IsLegacyFallback &&
            TryResolveLegacyToolBridge(decision, executionContext, out invocation!))
        {
            return true;
        }

        if (TryResolveTypedIntent(executionContext, out invocation!))
        {
            return true;
        }

        if ((decision.IsLegacyFallback || decision.NextActionDecision is not null) &&
            TryResolveLegacyToolBridge(decision, executionContext, out invocation!))
        {
            return true;
        }

        invocation = default!;
        return false;
    }

    private static bool TryResolveExplicit(
        AiDecision decision,
        AgentExecutionContext context,
        out CapabilityInvocation invocation)
    {
        invocation = default!;

        if (string.IsNullOrWhiteSpace(decision.RoutedCapabilityName) ||
            decision.NextActionDecision?.ExecuteAction is null)
        {
            return false;
        }

        if (!TryCreateTargetReferenceFromActionTarget(decision.NextActionDecision.ExecuteAction.Target, out var target))
        {
            target = SelectExplicitCapabilityTarget(context, decision.RoutedCapabilityName);
            if (string.IsNullOrWhiteSpace(target.NormalizedValue) &&
                string.IsNullOrWhiteSpace(target.DisplayName) &&
                string.IsNullOrWhiteSpace(target.OriginalText))
            {
                return false;
            }
        }

        var capabilityName = decision.RoutedCapabilityName;
        var action = CapabilityActionBuilders.BuildExplicitCapabilityAction(
            capabilityName,
            decision.RoutedActionName,
            decision.NextActionDecision.ExecuteAction,
            target);

        var routingSource = "explicit-capability";
        ApplyObservationPolicyIfNeeded(
            context,
            decision.NextActionDecision.ExecuteAction,
            ref capabilityName,
            ref target,
            ref action,
            ref routingSource);

        if (string.IsNullOrWhiteSpace(action.CapabilityName) ||
            string.IsNullOrWhiteSpace(action.ActionName))
        {
            return false;
        }

        invocation = new CapabilityInvocation
        {
            RoutedCapabilityName = capabilityName,
            Target = target,
            Action = action,
            RoutingSource = routingSource
        };
        return true;
    }

    private static bool TryResolveTypedIntent(
        AgentExecutionContext context,
        out CapabilityInvocation invocation)
    {
        invocation = default!;

        switch (context.DetectedIntent)
        {
            case CommandIntentKind.OpenApplication:
                return TryResolveApplicationIntent(context, out invocation);
            case CommandIntentKind.FocusWindow:
                return TryResolveFocusIntent(context, out invocation);
            case CommandIntentKind.VerifyProcessRunning:
                return TryResolveProcessVerificationIntent(context, out invocation);
            case CommandIntentKind.VerifyForegroundAlignment:
                return TryResolveForegroundAlignmentIntent(context, out invocation);
            case CommandIntentKind.VerifyServiceStatus:
                return TryResolveServiceStatusIntent(context, out invocation);
            case CommandIntentKind.StartService:
            case CommandIntentKind.StopService:
                return TryResolveServiceControlIntent(context, out invocation);
            case CommandIntentKind.VerifyFileExists:
                return TryResolveFileVerificationIntent(context, out invocation);
            case CommandIntentKind.OpenExistingFile:
                return TryResolveFileOpenIntent(context, out invocation);
            default:
                return false;
        }
    }

    private static bool TryResolveApplicationIntent(AgentExecutionContext context, out CapabilityInvocation invocation)
    {
        invocation = default!;

        if (TryCreateApplicationTargetFromGrounding(context.PrimaryTargetGrounding, out var groundedApplicationTarget))
        {
            invocation = BuildInvocation(
                CapabilityActionBuilders.ApplicationCapabilityName,
                groundedApplicationTarget,
                CapabilityActionBuilders.BuildApplicationCapabilityAction(groundedApplicationTarget, CapabilityActionBuilders.ApplicationCapabilityName));
            return true;
        }

        var resolvedApplicationTarget = context.ResolvedTargets.FirstOrDefault(t =>
            t.Kind is TargetKind.Application or TargetKind.Process);
        if (resolvedApplicationTarget is not null)
        {
            if (resolvedApplicationTarget.Kind == TargetKind.Process &&
                resolvedApplicationTarget.ResolutionReasonKind == TargetResolutionReasonKind.Unknown)
            {
                // Do not promote unknown process guesses into launch-capable app targets.
                resolvedApplicationTarget = null;
            }
            else if (resolvedApplicationTarget.ResolutionReasonKind is TargetResolutionReasonKind.ExplicitKeyword
                     or TargetResolutionReasonKind.Unknown)
            {
                // Explicit keyword hits without grounding should stay on the tool path.
                resolvedApplicationTarget = null;
            }
        }

        if (resolvedApplicationTarget is not null)
        {
            var applicationTarget = resolvedApplicationTarget.Kind == TargetKind.Process
                ? CloneTargetWithKind(resolvedApplicationTarget, TargetKind.Application)
                : resolvedApplicationTarget;

            invocation = BuildInvocation(
                CapabilityActionBuilders.ApplicationCapabilityName,
                applicationTarget,
                CapabilityActionBuilders.BuildApplicationCapabilityAction(applicationTarget, CapabilityActionBuilders.ApplicationCapabilityName));
            return true;
        }

        if (!string.IsNullOrWhiteSpace(context.ContextAdapter?.AdapterName))
        {
            var adapterTarget = new TargetReference
            {
                Kind = TargetKind.Application,
                OriginalText = context.ContextAdapter.AdapterName,
                NormalizedValue = context.ContextAdapter.AdapterName,
                DisplayName = context.ContextAdapter.AdapterName,
                ResolutionReasonKind = TargetResolutionReasonKind.AdapterContext,
                ResolutionSourceText = context.ContextAdapter.AdapterName
            };

            invocation = BuildInvocation(
                CapabilityActionBuilders.ApplicationCapabilityName,
                adapterTarget,
                CapabilityActionBuilders.BuildApplicationCapabilityAction(adapterTarget, CapabilityActionBuilders.ApplicationCapabilityName));
            return true;
        }

        return false;
    }

    private static bool TryResolveFocusIntent(AgentExecutionContext context, out CapabilityInvocation invocation)
    {
        invocation = default!;
        var focusTarget = SelectFocusTargetForCapability(context.ResolvedTargets, context.Observation);
        if (focusTarget is null)
        {
            return false;
        }

        invocation = BuildInvocation(
            CapabilityActionBuilders.WindowProcessCapabilityName,
            focusTarget,
            CapabilityActionBuilders.BuildWindowProcessCapabilityAction(focusTarget, CapabilityActionBuilders.WindowProcessCapabilityName));
        return true;
    }

    private static bool TryResolveProcessVerificationIntent(AgentExecutionContext context, out CapabilityInvocation invocation)
    {
        invocation = default!;

        var processTarget = context.ResolvedTargets.FirstOrDefault(t =>
            t.Kind is TargetKind.Process or TargetKind.Application);

        if (processTarget is null &&
            !TryCreateProcessTargetFromGrounding(context.PrimaryTargetGrounding, out processTarget))
        {
            return false;
        }

        invocation = BuildInvocation(
            CapabilityActionBuilders.ProcessVerificationCapabilityName,
            processTarget,
            CapabilityActionBuilders.BuildProcessVerificationCapabilityAction(processTarget, CapabilityActionBuilders.ProcessVerificationCapabilityName));
        return true;
    }

    private static bool TryResolveForegroundAlignmentIntent(AgentExecutionContext context, out CapabilityInvocation invocation)
    {
        invocation = default!;
        var alignmentTarget = SelectForegroundAlignmentTargetForCapability(context.ResolvedTargets, context.Observation);
        if (alignmentTarget is null)
        {
            return false;
        }

        invocation = BuildInvocation(
            CapabilityActionBuilders.ForegroundAlignmentCapabilityName,
            alignmentTarget,
            CapabilityActionBuilders.BuildForegroundAlignmentCapabilityAction(alignmentTarget, CapabilityActionBuilders.ForegroundAlignmentCapabilityName));
        return true;
    }

    private static bool TryResolveServiceStatusIntent(AgentExecutionContext context, out CapabilityInvocation invocation)
    {
        invocation = default!;
        var serviceStatusTarget = context.ResolvedTargets.FirstOrDefault(t => t.Kind == TargetKind.Service);
        if (serviceStatusTarget is null)
        {
            return false;
        }

        invocation = BuildInvocation(
            CapabilityActionBuilders.ServiceStatusCapabilityName,
            serviceStatusTarget,
            CapabilityActionBuilders.BuildServiceStatusCapabilityAction(serviceStatusTarget, CapabilityActionBuilders.ServiceStatusCapabilityName));
        return true;
    }

    private static bool TryResolveServiceControlIntent(AgentExecutionContext context, out CapabilityInvocation invocation)
    {
        invocation = default!;
        var serviceControlTarget = context.ResolvedTargets.FirstOrDefault(t => t.Kind == TargetKind.Service);
        if (serviceControlTarget is null)
        {
            return false;
        }

        var actionName = context.DetectedIntent == CommandIntentKind.StartService ? "StartService" : "StopService";
        invocation = BuildInvocation(
            CapabilityActionBuilders.ServiceControlCapabilityName,
            serviceControlTarget,
            CapabilityActionBuilders.BuildServiceControlCapabilityAction(
                serviceControlTarget,
                CapabilityActionBuilders.ServiceControlCapabilityName,
                actionName));
        return true;
    }

    private static bool TryResolveFileVerificationIntent(AgentExecutionContext context, out CapabilityInvocation invocation)
    {
        invocation = default!;
        var fileVerifyTarget = context.ResolvedTargets.FirstOrDefault(t => t.Kind == TargetKind.File);
        if (fileVerifyTarget is null &&
            !TryCreateFileTargetFromGrounding(context.PrimaryTargetGrounding, out fileVerifyTarget))
        {
            return false;
        }


        invocation = BuildInvocation(
            CapabilityActionBuilders.FileVerificationCapabilityName,
            fileVerifyTarget,
            CapabilityActionBuilders.BuildFileVerificationCapabilityAction(fileVerifyTarget, CapabilityActionBuilders.FileVerificationCapabilityName));
        return true;
    }

    private static bool TryResolveFileOpenIntent(AgentExecutionContext context, out CapabilityInvocation invocation)
    {
        invocation = default!;
        var fileOpenTarget = context.ResolvedTargets.FirstOrDefault(t => t.Kind == TargetKind.File);
        if (fileOpenTarget is null &&
            !TryCreateFileTargetFromGrounding(context.PrimaryTargetGrounding, out fileOpenTarget))
        {
            return false;
        }


        invocation = BuildInvocation(
            CapabilityActionBuilders.FileOpenCapabilityName,
            fileOpenTarget,
            CapabilityActionBuilders.BuildFileOpenCapabilityAction(fileOpenTarget, CapabilityActionBuilders.FileOpenCapabilityName));
        return true;
    }

    private static bool TryResolveLegacyToolBridge(
        AiDecision decision,
        AgentExecutionContext context,
        out CapabilityInvocation invocation)
    {
        invocation = default!;

        if (decision.SelectedToolName.Equals("OpenAppTool", StringComparison.OrdinalIgnoreCase) &&
            TryApplicationLegacyScenario(context, out var applicationTarget))
        {
            invocation = BuildLegacyInvocation(
                CapabilityActionBuilders.ApplicationCapabilityName,
                applicationTarget,
                CapabilityActionBuilders.BuildApplicationCapabilityAction(applicationTarget, CapabilityActionBuilders.ApplicationCapabilityName));
            return true;
        }

        if (decision.SelectedToolName.Equals("FocusWindowTool", StringComparison.OrdinalIgnoreCase) &&
            TryResolveLegacyFocusTarget(context, out var focusTarget))
        {
            invocation = BuildLegacyInvocation(
                CapabilityActionBuilders.WindowProcessCapabilityName,
                focusTarget,
                CapabilityActionBuilders.BuildWindowProcessCapabilityAction(focusTarget, CapabilityActionBuilders.WindowProcessCapabilityName));
            return true;
        }

        if (decision.SelectedToolName.Equals("VerifyProcessTool", StringComparison.OrdinalIgnoreCase) &&
            TryProcessVerificationLegacyScenario(context, out var verificationTarget))
        {
            invocation = BuildLegacyInvocation(
                CapabilityActionBuilders.ProcessVerificationCapabilityName,
                verificationTarget,
                CapabilityActionBuilders.BuildProcessVerificationCapabilityAction(verificationTarget, CapabilityActionBuilders.ProcessVerificationCapabilityName));
            return true;
        }

        if (decision.SelectedToolName.Equals("VerifyForegroundAlignmentTool", StringComparison.OrdinalIgnoreCase) &&
            SelectForegroundAlignmentTargetForCapability(context.ResolvedTargets, context.Observation) is { } alignmentTarget)
        {
            invocation = BuildLegacyInvocation(
                CapabilityActionBuilders.ForegroundAlignmentCapabilityName,
                alignmentTarget,
                CapabilityActionBuilders.BuildForegroundAlignmentCapabilityAction(alignmentTarget, CapabilityActionBuilders.ForegroundAlignmentCapabilityName));
            return true;
        }

        if (decision.SelectedToolName.Equals("VerifyServiceStatusTool", StringComparison.OrdinalIgnoreCase) &&
            TryResolveLegacyServiceTarget(decision, context, out var serviceStatusTarget))
        {
            invocation = BuildLegacyInvocation(
                CapabilityActionBuilders.ServiceStatusCapabilityName,
                serviceStatusTarget,
                CapabilityActionBuilders.BuildServiceStatusCapabilityAction(serviceStatusTarget, CapabilityActionBuilders.ServiceStatusCapabilityName));
            return true;
        }

        if (TryServiceControlLegacyScenario(decision, context, out var serviceControlTarget, out var serviceControlActionName))
        {
            invocation = BuildLegacyInvocation(
                CapabilityActionBuilders.ServiceControlCapabilityName,
                serviceControlTarget,
                CapabilityActionBuilders.BuildServiceControlCapabilityAction(
                    serviceControlTarget,
                    CapabilityActionBuilders.ServiceControlCapabilityName,
                    serviceControlActionName));
            return true;
        }

        if (decision.SelectedToolName.Equals("VerifyFileExistsTool", StringComparison.OrdinalIgnoreCase) &&
            TryResolveLegacyFileTarget(decision, context, out var fileVerifyTarget))
        {
            invocation = BuildLegacyInvocation(
                CapabilityActionBuilders.FileVerificationCapabilityName,
                fileVerifyTarget,
                CapabilityActionBuilders.BuildFileVerificationCapabilityAction(fileVerifyTarget, CapabilityActionBuilders.FileVerificationCapabilityName));
            return true;
        }

        if (decision.SelectedToolName.Equals("OpenExistingFileTool", StringComparison.OrdinalIgnoreCase) &&
            TryResolveLegacyFileTarget(decision, context, out var fileOpenTarget))
        {
            invocation = BuildLegacyInvocation(
                CapabilityActionBuilders.FileOpenCapabilityName,
                fileOpenTarget,
                CapabilityActionBuilders.BuildFileOpenCapabilityAction(fileOpenTarget, CapabilityActionBuilders.FileOpenCapabilityName));
            return true;
        }

        return false;
    }

    private static bool TryApplicationLegacyScenario(AgentExecutionContext context, out TargetReference target)
    {
        target = new TargetReference();

        var resolved = context.ResolvedTargets.FirstOrDefault(t =>
            t.Kind is TargetKind.Application or TargetKind.Process);
        if (resolved is not null)
        {
            if (resolved.Kind == TargetKind.Process &&
                resolved.ResolutionReasonKind == TargetResolutionReasonKind.Unknown)
            {
                resolved = null;
            }
        }

        if (resolved is not null)
        {
            target = resolved.Kind == TargetKind.Process
                ? CloneTargetWithKind(resolved, TargetKind.Application)
                : resolved;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(context.ContextAdapter?.AdapterName))
        {
            target = new TargetReference
            {
                Kind = TargetKind.Application,
                OriginalText = context.ContextAdapter.AdapterName,
                NormalizedValue = context.ContextAdapter.AdapterName,
                DisplayName = context.ContextAdapter.AdapterName,
                ResolutionReasonKind = TargetResolutionReasonKind.AdapterContext,
                ResolutionSourceText = context.ContextAdapter.AdapterName
            };
            return true;
        }

        return TryCreateApplicationTargetFromGrounding(context.PrimaryTargetGrounding, out target);
    }

    private static bool TryProcessVerificationLegacyScenario(AgentExecutionContext context, out TargetReference target)
    {
        target = new TargetReference();

        var processTarget = context.ResolvedTargets.FirstOrDefault(t =>
            t.Kind is TargetKind.Process or TargetKind.Application);

        if (processTarget is not null)
        {
            target = processTarget.Kind == TargetKind.Application
                ? CloneTargetWithKind(processTarget, TargetKind.Process)
                : processTarget;
            return true;
        }

        return TryCreateProcessTargetFromGrounding(context.PrimaryTargetGrounding, out target);
    }

    private static bool TryServiceControlLegacyScenario(
        AiDecision decision,
        AgentExecutionContext context,
        out TargetReference target,
        out string actionName)
    {
        target = new TargetReference();
        actionName = string.Empty;

        if (decision.SelectedToolName.Equals("StartServiceTool", StringComparison.OrdinalIgnoreCase))
        {
            actionName = "StartService";
        }
        else if (decision.SelectedToolName.Equals("StopServiceTool", StringComparison.OrdinalIgnoreCase))
        {
            actionName = "StopService";
        }
        else
        {
            return false;
        }

        if (!TryResolveLegacyServiceTarget(decision, context, out var serviceTarget))
        {
            return false;
        }

        target = serviceTarget;
        return true;
    }

    private static bool TryResolveLegacyFocusTarget(
        AgentExecutionContext context,
        out TargetReference target)
    {
        target = SelectFocusTargetForCapability(context.ResolvedTargets, context.Observation) ?? new TargetReference();
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            return true;
        }

        var activeWindow = context.Observation?.ActiveWindow;
        if (activeWindow is not null)
        {
            target = new TargetReference
            {
                Kind = TargetKind.Window,
                OriginalText = activeWindow.Title ?? activeWindow.ProcessName ?? "active-window",
                NormalizedValue = activeWindow.Title ?? activeWindow.ProcessName ?? "active-window",
                DisplayName = activeWindow.Title,
                ResolutionReasonKind = TargetResolutionReasonKind.ObservationContext,
                ResolutionSourceText = "active-window",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["windowHandle"] = activeWindow.Handle?.ToString() ?? string.Empty,
                    ["processName"] = activeWindow.ProcessName ?? string.Empty
                }
            };
            return true;
        }

        if (string.IsNullOrWhiteSpace(context.Observation?.ActiveProcessName))
        {
            return false;
        }

        var activeProcessName = context.Observation!.ActiveProcessName!;
        target = new TargetReference
        {
            Kind = TargetKind.Process,
            OriginalText = activeProcessName,
            NormalizedValue = activeProcessName,
            DisplayName = activeProcessName,
            ResolutionReasonKind = TargetResolutionReasonKind.ObservationContext,
            ResolutionSourceText = "active-process"
        };
        return true;
    }

    private static bool TryResolveLegacyServiceTarget(
        AiDecision decision,
        AgentExecutionContext context,
        out TargetReference target)
    {
        var resolved = context.ResolvedTargets.FirstOrDefault(t => t.Kind == TargetKind.Service);
        if (resolved is not null)
        {
            target = resolved;
            return true;
        }

        if (string.IsNullOrWhiteSpace(decision.ToolArgument))
        {
            target = new TargetReference();
            return false;
        }

        target = new TargetReference
        {
            Kind = TargetKind.Service,
            OriginalText = decision.ToolArgument,
            NormalizedValue = decision.ToolArgument,
            DisplayName = decision.ToolArgument,
            ResolutionReasonKind = TargetResolutionReasonKind.ExplicitKeyword,
            ResolutionSourceText = "legacy-tool-argument"
        };
        return true;
    }

    private static bool TryResolveLegacyFileTarget(
        AiDecision decision,
        AgentExecutionContext context,
        out TargetReference target)
    {
        var resolved = context.ResolvedTargets.FirstOrDefault(t =>
            t.Kind is TargetKind.File or TargetKind.Path);
        if (resolved is not null)
        {
            target = resolved.Kind == TargetKind.Path
                ? CloneTargetWithKind(resolved, TargetKind.File)
                : resolved;
            return true;
        }

        if (TryCreateFileTargetFromGrounding(context.PrimaryTargetGrounding, out target))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(decision.ToolArgument))
        {
            target = new TargetReference();
            return false;
        }

        target = new TargetReference
        {
            Kind = TargetKind.File,
            OriginalText = decision.ToolArgument,
            NormalizedValue = decision.ToolArgument,
            DisplayName = decision.ToolArgument,
            ResolutionReasonKind = TargetResolutionReasonKind.ExplicitKeyword,
            ResolutionSourceText = "legacy-tool-argument",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["filePath"] = decision.ToolArgument
            }
        };
        return true;
    }

    private static TargetReference CloneTargetWithKind(TargetReference target, TargetKind kind)
    {
        return new TargetReference
        {
            Kind = kind,
            OriginalText = target.OriginalText,
            NormalizedValue = target.NormalizedValue,
            DisplayName = target.DisplayName,
            Confidence = target.Confidence,
            ResolutionReasonKind = target.ResolutionReasonKind,
            ResolutionSourceText = target.ResolutionSourceText,
            Metadata = target.Metadata is null
                ? null
                : new Dictionary<string, string>(target.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static CapabilityInvocation BuildInvocation(string capabilityName, TargetReference target, AgentAction action)
    {
        return new CapabilityInvocation
        {
            RoutedCapabilityName = capabilityName,
            Target = target,
            Action = action,
            RoutingSource = "typed-intent"
        };
    }

    private static CapabilityInvocation BuildLegacyInvocation(string capabilityName, TargetReference target, AgentAction action)
    {
        return new CapabilityInvocation
        {
            RoutedCapabilityName = capabilityName,
            Target = target,
            Action = action,
            RoutingSource = "legacy-tool-bridge"
        };
    }

    private static TargetReference? SelectFocusTargetForCapability(
        IReadOnlyList<TargetReference> targets,
        ObservationSnapshot? observation)
    {
        return SelectObservationWeightedTarget(
            targets,
            observation,
            preferWindowTarget: true);
    }

    private static TargetReference? SelectForegroundAlignmentTargetForCapability(
        IReadOnlyList<TargetReference> targets,
        ObservationSnapshot? observation)
    {
        return SelectObservationWeightedTarget(
            targets,
            observation,
            preferWindowTarget: false);
    }

    private static TargetReference SelectExplicitCapabilityTarget(AgentExecutionContext context, string capabilityName)
    {
        if (capabilityName.Equals(CapabilityActionBuilders.ApplicationCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            return TryCreateApplicationTargetFromGrounding(context.PrimaryTargetGrounding, out var applicationTarget)
                ? applicationTarget
                : new TargetReference();
        }

        if (capabilityName.Equals(CapabilityActionBuilders.WindowProcessCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            return SelectFocusTargetForCapability(context.ResolvedTargets, context.Observation) ?? new TargetReference();
        }

        if (capabilityName.Equals(CapabilityActionBuilders.ForegroundAlignmentCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            return SelectForegroundAlignmentTargetForCapability(context.ResolvedTargets, context.Observation) ?? new TargetReference();
        }

        if (capabilityName.Equals(CapabilityActionBuilders.ProcessVerificationCapabilityName, StringComparison.OrdinalIgnoreCase))
        {
            var processTarget = context.ResolvedTargets.FirstOrDefault(t =>
                t.Kind is TargetKind.Process or TargetKind.Application);
            if (processTarget is not null)
            {
                return processTarget;
            }

            if (TryCreateProcessTargetFromGrounding(context.PrimaryTargetGrounding, out processTarget))
            {
                return processTarget;
            }
        }

        return context.ResolvedTargets.FirstOrDefault() ?? new TargetReference();
    }

    private static bool TryCreateTargetReferenceFromActionTarget(ActionTarget actionTarget, out TargetReference target)
    {
        target = new TargetReference();

        if (actionTarget is null ||
            string.IsNullOrWhiteSpace(actionTarget.Reference))
        {
            return false;
        }

        target = new TargetReference
        {
            Kind = MapActionTargetKind(actionTarget.Kind),
            OriginalText = actionTarget.Reference,
            NormalizedValue = actionTarget.Reference,
            DisplayName = string.IsNullOrWhiteSpace(actionTarget.DisplayName)
                ? actionTarget.Reference
                : actionTarget.DisplayName,
            Confidence = 1.0,
            ResolutionReasonKind = TargetResolutionReasonKind.Unknown,
            ResolutionSourceText = "explicit-capability",
            Metadata = actionTarget.Metadata is null
                ? null
                : new Dictionary<string, string>(actionTarget.Metadata, StringComparer.OrdinalIgnoreCase)
        };
        return true;
    }

    private static void ApplyObservationPolicyIfNeeded(
        AgentExecutionContext context,
        ExecuteActionPayload executeAction,
        ref string capabilityName,
        ref TargetReference target,
        ref AgentAction action,
        ref string routingSource)
    {
        if (executeAction.ActionType is not (ActionType.Focus or ActionType.Verify))
        {
            return;
        }

        var observation = context.Observation;
        if (observation?.ActiveWindow is null && string.IsNullOrWhiteSpace(observation?.ActiveProcessName))
        {
            return;
        }

        if (!IsStrongTargetConflictWithObservation(target, observation))
        {
            if (TryResolveObservationPreferredTarget(context.ResolvedTargets, observation, out var preferredTarget))
            {
                target = preferredTarget;
                action = CapabilityActionBuilders.BuildExplicitCapabilityAction(
                    capabilityName,
                    action.ActionName,
                    executeAction,
                    target);
                routingSource = "explicit-capability-observation-soft";
            }

            return;
        }

        // Hard override: when explicit target strongly conflicts with foreground context,
        // switch to a safer alignment check before executing risky focus/process-window actions.
        capabilityName = CapabilityActionBuilders.ForegroundAlignmentCapabilityName;
        target = TryBuildForegroundObservationTarget(observation, out var observationTarget)
            ? observationTarget
            : target;
        action = CapabilityActionBuilders.BuildForegroundAlignmentCapabilityAction(target, capabilityName);
        routingSource = "explicit-capability-observation-hard";
    }

    private static bool TryResolveObservationPreferredTarget(
        IReadOnlyList<TargetReference> targets,
        ObservationSnapshot? observation,
        out TargetReference target)
    {
        target = SelectObservationWeightedTarget(targets, observation, preferWindowTarget: false) ?? new TargetReference();
        return !string.IsNullOrWhiteSpace(target.NormalizedValue) ||
               !string.IsNullOrWhiteSpace(target.DisplayName);
    }

    private static TargetReference? SelectObservationWeightedTarget(
        IReadOnlyList<TargetReference> targets,
        ObservationSnapshot? observation,
        bool preferWindowTarget)
    {
        if (targets.Count == 0)
        {
            return null;
        }

        var activeWindowHandle = observation?.ActiveWindow?.Handle;
        var activeProcessName = ObservationTargetAlignment.NormalizeProcessName(observation?.ActiveProcessName ?? observation?.ActiveWindow?.ProcessName);
        var activeWindowTitle = observation?.ActiveWindow?.Title;

        TargetReference? best = null;
        var bestScore = int.MinValue;

        foreach (var candidate in targets)
        {
            var score = 0;

            if (preferWindowTarget && candidate.Kind == TargetKind.Window)
            {
                score += 8;
            }
            else if (!preferWindowTarget && candidate.Kind is TargetKind.Process or TargetKind.Application)
            {
                score += 8;
            }
            else if (candidate.Kind == TargetKind.Window)
            {
                score += 3;
            }
            else if (candidate.Kind is TargetKind.Process or TargetKind.Application)
            {
                score += 3;
            }

            if (candidate.ResolutionReasonKind == TargetResolutionReasonKind.ObservationContext)
            {
                score += 5;
            }

            if (activeWindowHandle.HasValue &&
                CapabilityActionBuilders.TryExtractWindowHandle(candidate, out var candidateHandle) &&
                candidateHandle == activeWindowHandle.Value)
            {
                score += 20;
            }

            var candidateProcess = ObservationTargetAlignment.NormalizeProcessName(TryGetTargetProcessName(candidate));
            if (!string.IsNullOrWhiteSpace(activeProcessName) &&
                !string.IsNullOrWhiteSpace(candidateProcess) &&
                activeProcessName.Equals(candidateProcess, StringComparison.OrdinalIgnoreCase))
            {
                score += 12;
            }

            var titleAffinityScore = ObservationTargetAlignment.ComputeWindowTitleAffinityScore(candidate, activeWindowTitle);
            score += titleAffinityScore;

            if (preferWindowTarget &&
                candidate.Kind == TargetKind.Window &&
                !activeWindowHandle.HasValue &&
                !string.IsNullOrWhiteSpace(activeWindowTitle))
            {
                // Ambiguity breaking: when window handle is unavailable, title similarity becomes
                // the primary deterministic signal for "current window" style intents.
                score += titleAffinityScore > 0 ? 8 : -4;
            }

            if (best is null || score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool IsStrongTargetConflictWithObservation(
        TargetReference target,
        ObservationSnapshot? observation)
    {
        if (observation is null)
        {
            return false;
        }

        if (CapabilityActionBuilders.TryExtractWindowHandle(target, out var targetHandle) &&
            observation.ActiveWindow?.Handle is long activeHandle &&
            targetHandle > 0 &&
            activeHandle > 0 &&
            targetHandle != activeHandle)
        {
            var targetProcess = ObservationTargetAlignment.NormalizeProcessName(TryGetTargetProcessName(target));
            var activeProcess = ObservationTargetAlignment.NormalizeProcessName(observation.ActiveProcessName ?? observation.ActiveWindow?.ProcessName);
            if (!string.IsNullOrWhiteSpace(targetProcess) &&
                !string.IsNullOrWhiteSpace(activeProcess) &&
                !targetProcess.Equals(activeProcess, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string TryGetTargetProcessName(TargetReference target)
    {
        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("processName", out var processName) &&
            !string.IsNullOrWhiteSpace(processName))
        {
            return processName;
        }

        return target.Kind is TargetKind.Process or TargetKind.Application
            ? target.NormalizedValue
            : string.Empty;
    }

    private static bool TryBuildForegroundObservationTarget(
        ObservationSnapshot? observation,
        out TargetReference target)
    {
        target = new TargetReference();
        if (observation is null)
        {
            return false;
        }

        if (observation.ActiveWindow?.Handle is long handle && handle > 0)
        {
            target = new TargetReference
            {
                Kind = TargetKind.Window,
                OriginalText = "active-window",
                NormalizedValue = handle.ToString(),
                DisplayName = observation.ActiveWindow.Title ?? handle.ToString(),
                ResolutionReasonKind = TargetResolutionReasonKind.ObservationContext,
                ResolutionSourceText = "observation-hard-override",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["windowHandle"] = handle.ToString(),
                    ["processName"] = observation.ActiveWindow.ProcessName ?? string.Empty,
                    ["windowTitle"] = observation.ActiveWindow.Title ?? string.Empty
                }
            };
            return true;
        }

        var activeProcess = observation.ActiveProcessName ?? observation.ActiveWindow?.ProcessName;
        if (string.IsNullOrWhiteSpace(activeProcess))
        {
            return false;
        }

        target = new TargetReference
        {
            Kind = TargetKind.Process,
            OriginalText = "active-process",
            NormalizedValue = ObservationTargetAlignment.NormalizeProcessName(activeProcess),
            DisplayName = activeProcess,
            ResolutionReasonKind = TargetResolutionReasonKind.ObservationContext,
            ResolutionSourceText = "observation-hard-override",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["processName"] = activeProcess
            }
        };
        return true;
    }

    private static TargetKind MapActionTargetKind(ActionTargetKind actionTargetKind)
    {
        return actionTargetKind switch
        {
            ActionTargetKind.Application => TargetKind.Application,
            ActionTargetKind.Process => TargetKind.Process,
            ActionTargetKind.Window => TargetKind.Window,
            ActionTargetKind.File => TargetKind.File,
            ActionTargetKind.Path => TargetKind.Path,
            ActionTargetKind.Url => TargetKind.Url,
            ActionTargetKind.Service => TargetKind.Service,
            _ => TargetKind.Unknown
        };
    }

    private static bool TryCreateApplicationTargetFromGrounding(
        TargetGroundingResult? grounding,
        out TargetReference target)
    {
        target = new TargetReference();

        if (grounding is null ||
            grounding.Disposition != TargetGroundingDisposition.Resolved ||
            grounding.ExecutionSuitability != TargetExecutionSuitability.ExecutableHere ||
            grounding.Target.Kind is not (GroundedTargetKind.KnownApplication or GroundedTargetKind.PathLike) ||
            string.IsNullOrWhiteSpace(grounding.Target.CanonicalValue))
        {
            return false;
        }

        var targetKind = grounding.Target.Kind == GroundedTargetKind.PathLike
            ? TargetKind.Path
            : TargetKind.Application;

        target = new TargetReference
        {
            Kind = targetKind,
            OriginalText = grounding.Target.OriginalText,
            NormalizedValue = grounding.Target.CanonicalValue,
            DisplayName = grounding.Target.CanonicalValue,
            Confidence = grounding.Target.Confidence,
            ResolutionReasonKind = TargetResolutionReasonKind.ExplicitKeyword,
            ResolutionSourceText = "grounded-target",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["groundingDisposition"] = grounding.Disposition.ToString(),
                ["groundedTargetKind"] = grounding.Target.Kind.ToString(),
                ["groundingReason"] = grounding.Reason.ToString(),
                ["groundingExecutionSuitability"] = grounding.ExecutionSuitability.ToString()
            }
        };

        return true;
    }

    private static bool TryCreateFileTargetFromGrounding(
        TargetGroundingResult? grounding,
        out TargetReference target)
    {
        target = new TargetReference();

        if (grounding is null ||
            grounding.Disposition != TargetGroundingDisposition.Resolved ||
            grounding.ExecutionSuitability != TargetExecutionSuitability.ResolvedButNotExecutableHere ||
            grounding.Target.Kind != GroundedTargetKind.DocumentLike ||
            string.IsNullOrWhiteSpace(grounding.Target.CanonicalValue))
        {
            return false;
        }

        target = new TargetReference
        {
            Kind = TargetKind.File,
            OriginalText = grounding.Target.OriginalText,
            NormalizedValue = grounding.Target.CanonicalValue,
            DisplayName = grounding.Target.CanonicalValue,
            Confidence = grounding.Target.Confidence,
            ResolutionReasonKind = TargetResolutionReasonKind.Unknown,
            ResolutionSourceText = "grounded-target",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["filePath"] = grounding.Target.CanonicalValue,
                ["groundingDisposition"] = grounding.Disposition.ToString(),
                ["groundedTargetKind"] = grounding.Target.Kind.ToString(),
                ["groundingReason"] = grounding.Reason.ToString(),
                ["groundingExecutionSuitability"] = grounding.ExecutionSuitability.ToString()
            }
        };

        return true;
    }

    private static bool TryCreateProcessTargetFromGrounding(
        TargetGroundingResult? grounding,
        out TargetReference target)
    {
        target = new TargetReference();

        if (grounding is null ||
            grounding.Disposition != TargetGroundingDisposition.Resolved ||
            grounding.ExecutionSuitability != TargetExecutionSuitability.ExecutableHere ||
            grounding.Target.Kind != GroundedTargetKind.KnownApplication ||
            string.IsNullOrWhiteSpace(grounding.Target.CanonicalValue))
        {
            return false;
        }

        target = new TargetReference
        {
            Kind = TargetKind.Process,
            OriginalText = grounding.Target.OriginalText,
            NormalizedValue = grounding.Target.CanonicalValue,
            DisplayName = grounding.Target.CanonicalValue,
            Confidence = grounding.Target.Confidence,
            ResolutionReasonKind = TargetResolutionReasonKind.ExplicitKeyword,
            ResolutionSourceText = "grounded-target",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["processName"] = grounding.Target.CanonicalValue
            }
        };

        return true;
    }
}
