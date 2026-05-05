using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Infrastructure.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure;

public sealed class BasicStepSafetyEvaluator : IStepSafetyEvaluator
{
    private readonly IReadOnlyList<string> _deniedPatterns;
    private readonly IReadOnlyList<string> _requiresApprovalTextPatterns;
    private readonly IReadOnlyList<string> _requiresApprovalShortcutPatterns;
    private readonly IReadOnlyList<string> _requiresApprovalTargets;
    private readonly HashSet<string> _allowedRealApps;
    private readonly HashSet<string> _allowedExecutablePaths;
    private readonly IReadOnlyDictionary<string, string> _appAliases;

    private static readonly IReadOnlyDictionary<ActionType, SafetyRiskLevel> ActionRiskPolicyMatrix =
        new Dictionary<ActionType, SafetyRiskLevel>
        {
            [ActionType.Focus] = SafetyRiskLevel.Low,
            [ActionType.Navigate] = SafetyRiskLevel.Low,
            [ActionType.Verify] = SafetyRiskLevel.Low,
            [ActionType.InputText] = SafetyRiskLevel.Low,
            [ActionType.PressKey] = SafetyRiskLevel.Low,
            [ActionType.PressShortcut] = SafetyRiskLevel.Low,
            [ActionType.Confirm] = SafetyRiskLevel.Low,
            [ActionType.Cancel] = SafetyRiskLevel.Low,
            [ActionType.Launch] = SafetyRiskLevel.Medium,
            [ActionType.OpenFile] = SafetyRiskLevel.High
        };

    public BasicStepSafetyEvaluator(ExecutionPolicySettings settings)
    {
        _deniedPatterns = (settings.DeniedPatterns ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant())
            .ToList();

        _requiresApprovalTargets = (settings.RequiresApprovalTargets ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant())
            .ToList();

        _requiresApprovalTextPatterns = (settings.RequiresApprovalTextPatterns ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant())
            .ToList();

        _requiresApprovalShortcutPatterns = (settings.RequiresApprovalShortcutPatterns ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal))
            .ToList();

        _allowedRealApps = (settings.AllowedRealApps ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(NormalizeIdentifier)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _allowedExecutablePaths = (settings.AllowedExecutablePaths ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(NormalizePath)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _appAliases = CreateNormalizedAliasMap(settings.AppAliases);
    }

    public Task<StepSafetyDecision> EvaluateAsync(StepSafetyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_deniedPatterns.Count == 0 || _requiresApprovalTargets.Count == 0)
        {
            return Task.FromResult(Deny("Blocked by policy: configuration is missing or invalid (fail-closed)."));
        }

        if (request.ExecuteAction is not null)
        {
            return Task.FromResult(EvaluateExecuteAction(request.ExecuteAction));
        }

        if (request.AgentAction is not null)
        {
            return Task.FromResult(EvaluateAgentAction(request.AgentAction));
        }

        return Task.FromResult(Deny("Blocked by policy: step action contract is missing."));
    }

    private StepSafetyDecision EvaluateExecuteAction(ExecuteActionPayload executeAction)
    {
        return executeAction.ActionType switch
        {
            ActionType.InputText => EvaluateTextAction(executeAction),
            ActionType.PressShortcut => EvaluateShortcutAction(executeAction),
            ActionType.PressKey => EvaluateKeyAction(executeAction, defaultKey: null),
            ActionType.Confirm => EvaluateKeyAction(executeAction, defaultKey: "enter"),
            ActionType.Cancel => EvaluateKeyAction(executeAction, defaultKey: "escape"),
            ActionType.Launch => EvaluateLaunchAction(executeAction),
            ActionType.OpenFile => EvaluateOpenFileAction(executeAction),
            ActionType.Focus or ActionType.Navigate or ActionType.Verify =>
                Allow(GetBaseRiskLevel(executeAction.ActionType), "Allowed by action-aware policy matrix."),
            _ => Deny("Blocked by policy: execute-action payload is malformed or unsupported.")
        };
    }

    private StepSafetyDecision EvaluateAgentAction(AgentAction agentAction)
    {
        if (string.IsNullOrWhiteSpace(agentAction.ActionName))
        {
            return Deny("Blocked by policy: agent action name is missing.");
        }

        if (agentAction.ActionName.Equals("OpenApplication", StringComparison.OrdinalIgnoreCase))
        {
            var target = FirstNonEmpty(
                TryGetActionParameter(agentAction.Parameters, ["app", "path", "target"]),
                agentAction.Target?.NormalizedValue,
                agentAction.Target?.DisplayName,
                agentAction.Target?.OriginalText);

            return EvaluateLaunchTarget(target, StepApprovalIdentityBuilder.BuildForAgentAction(agentAction));
        }

        if (agentAction.ActionName.Equals("OpenExistingFile", StringComparison.OrdinalIgnoreCase))
        {
            var fileTarget = FirstNonEmpty(
                TryGetActionParameter(agentAction.Parameters, ["file", "path", "target"]),
                agentAction.Target?.NormalizedValue,
                agentAction.Target?.DisplayName,
                agentAction.Target?.OriginalText);

            return EvaluateOpenFileTarget(fileTarget);
        }

        if (agentAction.ActionName.Equals("VerifyProcessRunning", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasExpectedTargetKind(agentAction, TargetKind.Process, TargetKind.Application))
            {
                return Deny("Blocked by policy: process verification requires a resolved process/application target.");
            }

            return Allow(SafetyRiskLevel.Low, "Allowed by action-aware policy matrix for process verification.");
        }

        if (agentAction.ActionName.Equals("FocusWindow", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasExpectedTargetKind(agentAction, TargetKind.Window, TargetKind.Process, TargetKind.Application))
            {
                return Deny("Blocked by policy: focus action requires a resolved window/process target.");
            }

            return Allow(SafetyRiskLevel.Low, "Allowed by action-aware policy matrix for window focus.");
        }

        if (agentAction.ActionName.Equals("VerifyForegroundAlignment", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasExpectedTargetKind(agentAction, TargetKind.Window, TargetKind.Process, TargetKind.Application))
            {
                return Deny("Blocked by policy: foreground-alignment verification requires a resolved window/process target.");
            }

            return Allow(SafetyRiskLevel.Low, "Allowed by action-aware policy matrix for foreground alignment verification.");
        }

        if (agentAction.ActionName.Equals("VerifyServiceStatus", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasExpectedTargetKind(agentAction, TargetKind.Service))
            {
                return Deny("Blocked by policy: service verification requires a resolved service target.");
            }

            return Allow(SafetyRiskLevel.Low, "Allowed by action-aware policy matrix for service verification.");
        }

        if (agentAction.ActionName.Equals("VerifyFileExists", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasExpectedTargetKind(agentAction, TargetKind.File))
            {
                return Deny("Blocked by policy: file verification requires a resolved file target.");
            }

            return Allow(SafetyRiskLevel.Low, "Allowed by action-aware policy matrix for file verification.");
        }

        if (agentAction.ActionName.Equals("StartService", StringComparison.OrdinalIgnoreCase) ||
            agentAction.ActionName.Equals("StopService", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasExpectedTargetKind(agentAction, TargetKind.Service))
            {
                return Deny("Blocked by policy: service control requires a resolved service target.");
            }

            return RequireApproval(
                StepApprovalIdentityBuilder.BuildForAgentAction(agentAction),
                SafetyRiskLevel.VeryHigh,
                "Policy requires explicit approval for service control actions.");
        }

        return Deny("Blocked by policy: agent action is not covered by the action-aware policy matrix.");
    }

    private StepSafetyDecision EvaluateTextAction(ExecuteActionPayload executeAction)
    {
        var text = TryGetActionParameter(executeAction.Parameters.Values, ["text", "input", "value"]);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Deny("Blocked by policy: text input payload is empty.");
        }

        var normalizedText = text.Trim().ToLowerInvariant();
        if (ContainsDeniedPattern(normalizedText))
        {
            return Deny("Blocked by policy: text input payload contains denied content.");
        }

        var approvalKey = StepApprovalIdentityBuilder.BuildForExecuteAction(executeAction);
        if (text.IndexOfAny(['\r', '\n']) >= 0 || text.Length > 120)
        {
            return RequireApproval(
                approvalKey,
                SafetyRiskLevel.VeryHigh,
                "Policy requires explicit approval for multi-line or long text input.");
        }

        if (_requiresApprovalTextPatterns.Any(pattern => normalizedText.Contains(pattern, StringComparison.Ordinal)))
        {
            return RequireApproval(
                approvalKey,
                SafetyRiskLevel.VeryHigh,
                "Policy requires explicit approval for sensitive text input content.");
        }

        return Allow(GetBaseRiskLevel(ActionType.InputText), "Allowed by action-aware policy matrix for text input.");
    }

    private StepSafetyDecision EvaluateShortcutAction(ExecuteActionPayload executeAction)
    {
        var shortcut = TryGetActionParameter(executeAction.Parameters.Values, ["shortcut", "value"]);
        if (string.IsNullOrWhiteSpace(shortcut) ||
            !KeyboardInputParser.TryParseShortcut(shortcut, out _, out _))
        {
            return Deny("Blocked by policy: shortcut payload is malformed or unsupported.");
        }

        if (_requiresApprovalShortcutPatterns.Count == 0)
        {
            return Deny("Blocked by policy: shortcut approval policy is missing (fail-closed).");
        }

        var normalizedShortcut = shortcut.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (ContainsDeniedPattern(normalizedShortcut))
        {
            return Deny("Blocked by policy: shortcut payload contains denied content.");
        }

        var approvalKey = StepApprovalIdentityBuilder.BuildForExecuteAction(executeAction);
        if (_requiresApprovalShortcutPatterns.Contains(normalizedShortcut, StringComparer.OrdinalIgnoreCase))
        {
            return RequireApproval(
                approvalKey,
                SafetyRiskLevel.VeryHigh,
                "Policy requires explicit approval for sensitive keyboard shortcuts.");
        }

        return Allow(GetBaseRiskLevel(ActionType.PressShortcut), "Allowed by action-aware policy matrix for keyboard shortcuts.");
    }

    private StepSafetyDecision EvaluateKeyAction(ExecuteActionPayload executeAction, string? defaultKey)
    {
        var key = TryGetActionParameter(executeAction.Parameters.Values, ["key", "value"]);
        if (string.IsNullOrWhiteSpace(key))
        {
            key = defaultKey;
        }

        if (string.IsNullOrWhiteSpace(key) ||
            key.Contains(' ', StringComparison.Ordinal) ||
            !KeyboardInputParser.TryParseKey(key, out _))
        {
            return Deny("Blocked by policy: key payload is malformed or unsupported.");
        }

        if (ContainsDeniedPattern(key))
        {
            return Deny("Blocked by policy: key payload contains denied content.");
        }

        return Allow(GetBaseRiskLevel(executeAction.ActionType), "Allowed by action-aware policy matrix for key actions.");
    }

    private StepSafetyDecision EvaluateLaunchAction(ExecuteActionPayload executeAction)
    {
        var target = FirstNonEmpty(
            TryGetActionParameter(executeAction.Parameters.Values, ["app", "path", "target"]),
            executeAction.Target.Reference);

        return EvaluateLaunchTarget(target, StepApprovalIdentityBuilder.BuildForExecuteAction(executeAction));
    }

    private StepSafetyDecision EvaluateLaunchTarget(string? target, string? approvalKey)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return RequireApproval(
                approvalKey,
                SafetyRiskLevel.VeryHigh,
                "Policy requires explicit approval because launch target is ambiguous.");
        }

        var normalizedTarget = target.Trim().ToLowerInvariant();
        if (_requiresApprovalTargets.Contains(normalizedTarget, StringComparer.OrdinalIgnoreCase))
        {
            return RequireApproval(
                approvalKey,
                SafetyRiskLevel.VeryHigh,
                "Policy requires explicit approval for elevated shell or registry actions.");
        }

        if (LooksLikePathTarget(target))
        {
            var normalizedPath = NormalizePath(target);
            if (_allowedExecutablePaths.Contains(normalizedPath))
            {
                return Allow(
                    SafetyRiskLevel.High,
                    "Allowed by action-aware policy matrix: allowlisted executable path launch is high risk.");
            }

            if (ContainsDeniedPattern(normalizedTarget))
            {
                return Deny("Blocked by policy: destructive or critical operation detected.");
            }

            return RequireApproval(
                approvalKey,
                SafetyRiskLevel.VeryHigh,
                "Policy requires explicit approval for non-allowlisted executable path launches.");
        }

        if (ContainsDeniedPattern(normalizedTarget))
        {
            return Deny("Blocked by policy: destructive or critical operation detected.");
        }

        var canonicalApp = ResolveCanonicalAppIdentifier(target);
        if (_allowedRealApps.Contains(canonicalApp))
        {
            return Allow(
                SafetyRiskLevel.Medium,
                "Allowed by action-aware policy matrix: allowlisted application launch is medium risk.");
        }

        return RequireApproval(
            approvalKey,
            SafetyRiskLevel.VeryHigh,
            "Policy requires explicit approval for launch actions with uncertain targets.");
    }

    private StepSafetyDecision EvaluateOpenFileAction(ExecuteActionPayload executeAction)
    {
        var target = FirstNonEmpty(
            TryGetActionParameter(executeAction.Parameters.Values, ["file", "path", "target"]),
            executeAction.Target.Reference);

        return EvaluateOpenFileTarget(target);
    }

    private StepSafetyDecision EvaluateOpenFileTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return RequireApproval(
                null,
                SafetyRiskLevel.VeryHigh,
                "Policy requires explicit approval because file target is ambiguous.");
        }

        var normalizedTarget = target.Trim().ToLowerInvariant();
        if (ContainsDeniedPattern(normalizedTarget))
        {
            return Deny("Blocked by policy: destructive or critical operation detected.");
        }

        return Allow(SafetyRiskLevel.High, "Allowed by action-aware policy matrix: file open is high risk.");
    }

    private bool ContainsDeniedPattern(string value)
    {
        return _deniedPatterns.Any(pattern => value.Contains(pattern, StringComparison.Ordinal));
    }

    private static bool HasExpectedTargetKind(AgentAction agentAction, params TargetKind[] expectedKinds)
    {
        return agentAction.Target is not null && expectedKinds.Contains(agentAction.Target.Kind);
    }

    private static SafetyRiskLevel GetBaseRiskLevel(ActionType actionType)
    {
        return ActionRiskPolicyMatrix.TryGetValue(actionType, out var riskLevel)
            ? riskLevel
            : SafetyRiskLevel.High;
    }

    private string ResolveCanonicalAppIdentifier(string rawTarget)
    {
        var canonical = NormalizeIdentifier(rawTarget);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return string.Empty;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (_appAliases.TryGetValue(canonical, out var alias) &&
               !string.IsNullOrWhiteSpace(alias) &&
               visited.Add(canonical))
        {
            canonical = alias;
        }

        return canonical;
    }

    private static IReadOnlyDictionary<string, string> CreateNormalizedAliasMap(IDictionary<string, string>? appAliases)
    {
        if (appAliases is null || appAliases.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in appAliases)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            normalized[NormalizeIdentifier(pair.Key)] = NormalizeIdentifier(pair.Value);
        }

        return normalized;
    }

    private static bool LooksLikePathTarget(string target)
    {
        var normalized = target.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Contains('\\', StringComparison.Ordinal) ||
            normalized.Contains('/', StringComparison.Ordinal))
        {
            return true;
        }

        if (normalized.Length > 2 &&
            char.IsLetter(normalized[0]) &&
            normalized[1] == ':')
        {
            return true;
        }

        if (normalized.StartsWith(".\\", StringComparison.Ordinal) ||
            normalized.StartsWith("..\\", StringComparison.Ordinal) ||
            normalized.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return true;
        }

        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeIdentifier(string value)
    {
        var normalized = value.Trim().Trim('"', '\'').ToLowerInvariant();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized;
    }

    private static string NormalizePath(string value)
    {
        var normalized = value.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = normalized.Replace('/', '\\');

        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
        }

        return normalized.ToLowerInvariant();
    }

    private static string? TryGetActionParameter(
        IReadOnlyDictionary<string, string>? parameters,
        IEnumerable<string> candidateKeys)
    {
        if (parameters is null)
        {
            return null;
        }

        foreach (var candidateKey in candidateKeys)
        {
            foreach (var pair in parameters)
            {
                if (!pair.Key.Equals(candidateKey, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                return pair.Value;
            }
        }

        return null;
    }

    private static string? TryGetActionParameter(
        IDictionary<string, string>? parameters,
        IEnumerable<string> candidateKeys)
    {
        if (parameters is null)
        {
            return null;
        }

        return TryGetActionParameter(
            (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase),
            candidateKeys);
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
    }

    private static StepSafetyDecision Allow(SafetyRiskLevel riskLevel, string reason)
    {
        return new StepSafetyDecision
        {
            Disposition = SafetyDisposition.Allowed,
            RiskLevel = riskLevel,
            Reason = reason
        };
    }

    private static StepSafetyDecision Deny(string reason)
    {
        return new StepSafetyDecision
        {
            Disposition = SafetyDisposition.Denied,
            RiskLevel = SafetyRiskLevel.High,
            Reason = reason
        };
    }

    private static StepSafetyDecision RequireApproval(string? approvalKey, SafetyRiskLevel riskLevel, string reason)
    {
        return new StepSafetyDecision
        {
            Disposition = SafetyDisposition.RequiresApproval,
            RiskLevel = riskLevel,
            Reason = reason,
            ApprovalKey = approvalKey
        };
    }
}
