using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Core;
using WindowsAiAssistant.Infrastructure.Legacy;

namespace WindowsAiAssistant.Infrastructure;

public sealed class ModelBackedDecisionClient : IAiDecisionClient
{
    private readonly IAuditLogger _auditLogger;
    private readonly IModelDecisionProvider _modelDecisionProvider;
    private readonly ModelDecisionSettings _settings;

    public ModelBackedDecisionClient(
        IModelDecisionProvider modelDecisionProvider,
        ModelDecisionSettings settings,
        IAuditLogger auditLogger)
    {
        _modelDecisionProvider = modelDecisionProvider;
        _settings = settings;
        _auditLogger = auditLogger;
    }

    public async Task<AiDecision> DecideAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var observationPackage = request.ModelObservationPackage ??
                                 ModelFacingObservationPackageBuilder.Build(request, request.AiDecisionInput);

        if (!_settings.Enabled)
        {
            await LogModelDecisionAsync(request, "ModelDecisionBlocked", ModelDecisionStatus.NotAvailable, "feature-disabled", cancellationToken);
            return CreateFailClosedDecision("Model decision provider is disabled by configuration.");
        }

        await LogModelDecisionAsync(request, "ModelDecisionAttempted", ModelDecisionStatus.NotAvailable, "attempt-started", cancellationToken);

        try
        {
            using var timeoutCts = CreateTimeoutTokenSource(cancellationToken);
            var attemptToken = timeoutCts?.Token ?? cancellationToken;

            var modelResult = await _modelDecisionProvider.TryDecideAsync(observationPackage, attemptToken);
            if (TryGetUsableDecision(modelResult, out var modelDecision))
            {
                await LogModelDecisionAsync(
                    request,
                    "ModelDecisionUsed",
                    modelResult!.Status,
                    modelResult.Reason,
                    cancellationToken,
                    modelResult);
                return EnsureModelDecision(modelDecision!);
            }

            await LogModelDecisionAsync(
                request,
                "ModelDecisionBlocked",
                modelResult?.Status ?? ModelDecisionStatus.InvalidResponse,
                modelResult?.Reason ?? "null-response",
                cancellationToken,
                modelResult);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await LogModelDecisionAsync(request, "ModelDecisionBlocked", ModelDecisionStatus.NotAvailable, "timeout", cancellationToken);
            return CreateFailClosedDecision("Model decision provider timed out before returning a valid decision.");
        }
        catch (Exception ex)
        {
            await LogModelDecisionAsync(request, "ModelDecisionBlocked", ModelDecisionStatus.NotAvailable, $"exception:{ex.GetType().Name}", cancellationToken);
            return CreateFailClosedDecision($"Model decision provider failed: {ex.GetType().Name}.");
        }

        return CreateFailClosedDecision("Model decision provider did not return a valid next-action decision.");
    }

    private CancellationTokenSource? CreateTimeoutTokenSource(CancellationToken cancellationToken)
    {
        if (_settings.TimeoutMilliseconds <= 0)
        {
            return null;
        }

        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_settings.TimeoutMilliseconds);
        return timeoutCts;
    }

    private static bool TryGetUsableDecision(ModelDecisionResult? result, out AiDecision? decision)
    {
        decision = null;

        if (result is null)
        {
            return false;
        }

        if (result.Status != ModelDecisionStatus.DecisionAvailable)
        {
            return false;
        }

        if (result.NextActionDecision is not null &&
            TryBuildDecisionFromNextAction(result.NextActionDecision, result.Reason, out var nextActionDecision))
        {
            decision = nextActionDecision;
            return true;
        }

        if (result.Decision is null)
        {
            return false;
        }

        var legacyDecision = result.Decision;
        if (string.IsNullOrWhiteSpace(legacyDecision.SelectedToolName) &&
            string.IsNullOrWhiteSpace(legacyDecision.DecisionText))
        {
            return false;
        }

        decision = result.NextActionDecision is null
            ? legacyDecision
            : new AiDecision
            {
                SelectedToolName = legacyDecision.SelectedToolName,
                ToolArgument = legacyDecision.ToolArgument,
                DecisionText = legacyDecision.DecisionText,
                NextActionDecision = result.NextActionDecision,
                RoutedCapabilityName = legacyDecision.RoutedCapabilityName,
                RoutedActionName = legacyDecision.RoutedActionName,
                IsLegacyFallback = legacyDecision.IsLegacyFallback
            };

        return true;
    }

    private static bool TryBuildDecisionFromNextAction(
        NextActionDecision nextActionDecision,
        string? modelReason,
        out AiDecision decision)
    {
        var selectedToolName = string.Empty;
        string? toolArgument = null;
        string? routedActionName = null;

        if (nextActionDecision.Kind == DecisionKind.ExecuteAction &&
            nextActionDecision.ExecuteAction is not null)
        {
            TryMapExecuteActionToTool(
                nextActionDecision.ExecuteAction,
                out selectedToolName,
                out toolArgument,
                out routedActionName);
        }

        var decisionText = BuildDecisionText(nextActionDecision, modelReason);
        if (string.IsNullOrWhiteSpace(decisionText) && string.IsNullOrWhiteSpace(selectedToolName))
        {
            decision = new AiDecision();
            return false;
        }

        decision = new AiDecision
        {
            SelectedToolName = selectedToolName,
            ToolArgument = toolArgument,
            DecisionText = decisionText,
            NextActionDecision = nextActionDecision,
            RoutedCapabilityName = nextActionDecision.ExecuteAction?.CapabilityHint,
            RoutedActionName = routedActionName,
            IsLegacyFallback = false
        };

        return true;
    }

    private static void TryMapExecuteActionToTool(
        ExecuteActionPayload executeAction,
        out string selectedToolName,
        out string? toolArgument,
        out string? routedActionName)
    {
        selectedToolName = MapToolNameForActionType(executeAction.ActionType);
        toolArgument = ExtractToolArgument(executeAction);
        routedActionName = executeAction.ActionType.ToString();
    }

    private static string MapToolNameForActionType(ActionType actionType)
    {
        return actionType switch
        {
            ActionType.Launch => "OpenAppTool",
            ActionType.Focus => "FocusWindowTool",
            ActionType.InputText => "TypeTextTool",
            ActionType.Confirm => "PressKeyTool",
            ActionType.Cancel => "PressKeyTool",
            ActionType.PressKey => "PressKeyTool",
            ActionType.PressShortcut => "PressShortcutTool",
            ActionType.Verify => "VerifyProcessTool",
            ActionType.OpenFile => "OpenExistingFileTool",
            _ => string.Empty
        };
    }

    private static string? ExtractToolArgument(ExecuteActionPayload executeAction)
    {
        var actionType = executeAction.ActionType;

        if (actionType == ActionType.InputText &&
            TryGetParameterValue(executeAction.Parameters, ["text", "input", "value"], out var textArgument))
        {
            return textArgument;
        }

        if ((actionType == ActionType.PressKey || actionType == ActionType.Confirm || actionType == ActionType.Cancel) &&
            TryGetParameterValue(executeAction.Parameters, ["key", "value"], out var keyArgument))
        {
            return keyArgument;
        }

        if (actionType == ActionType.Confirm)
        {
            return "enter";
        }

        if (actionType == ActionType.Cancel)
        {
            return "escape";
        }

        if (actionType == ActionType.PressShortcut &&
            TryGetParameterValue(executeAction.Parameters, ["shortcut", "value"], out var shortcutArgument))
        {
            return shortcutArgument;
        }

        if (actionType == ActionType.OpenFile &&
            TryGetParameterValue(executeAction.Parameters, ["file", "path", "target"], out var fileArgument))
        {
            return fileArgument;
        }

        if (actionType == ActionType.Launch &&
            TryGetParameterValue(executeAction.Parameters, ["app", "target", "path"], out var launchArgument))
        {
            return launchArgument;
        }

        return string.IsNullOrWhiteSpace(executeAction.Target.Reference)
            ? null
            : executeAction.Target.Reference;
    }

    private static bool TryGetParameterValue(
        ActionParameters parameters,
        IEnumerable<string> candidateKeys,
        out string value)
    {
        value = string.Empty;

        foreach (var candidateKey in candidateKeys)
        {
            foreach (var pair in parameters.Values)
            {
                if (!pair.Key.Equals(candidateKey, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                value = pair.Value;
                return true;
            }
        }

        return false;
    }

    private static string BuildDecisionText(NextActionDecision nextActionDecision, string? modelReason)
    {
        if (!string.IsNullOrWhiteSpace(nextActionDecision.Message))
        {
            return nextActionDecision.Message;
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.Rationale))
        {
            return nextActionDecision.Rationale;
        }

        if (!string.IsNullOrWhiteSpace(modelReason))
        {
            return modelReason;
        }

        return $"Model returned next-action decision kind: {nextActionDecision.Kind}.";
    }

    private static AiDecision EnsureModelDecision(AiDecision decision)
    {
        if (!decision.IsLegacyFallback)
        {
            return decision;
        }

        return new AiDecision
        {
            SelectedToolName = decision.SelectedToolName,
            ToolArgument = decision.ToolArgument,
            DecisionText = decision.DecisionText,
            NextActionDecision = decision.NextActionDecision,
            RoutedCapabilityName = decision.RoutedCapabilityName,
            RoutedActionName = decision.RoutedActionName,
            IsLegacyFallback = false
        };
    }

    private static AiDecision CreateFailClosedDecision(string reason)
    {
        return new AiDecision
        {
            SelectedToolName = string.Empty,
            ToolArgument = null,
            DecisionText = reason,
            IsLegacyFallback = false,
            NextActionDecision = new NextActionDecision
            {
                Kind = DecisionKind.Stop,
                Message = reason,
                Rationale = "model-provider-fail-closed",
                StopReason = reason,
                Stop = new StopPayload
                {
                    Disposition = StopDisposition.Blocked,
                    StopReason = reason
                },
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["modelDecisionFailClosed"] = "true"
                }
            }
        };
    }

    private async Task LogModelDecisionAsync(
        CommandRequest request,
        string eventType,
        ModelDecisionStatus status,
        string? reason,
        CancellationToken cancellationToken,
        ModelDecisionResult? modelResult = null)
    {
        try
        {
            var usedLiveModelPath = string.Equals(eventType, "ModelDecisionUsed", StringComparison.Ordinal);
            var usedFallbackPath = string.Equals(eventType, "ModelDecisionBlocked", StringComparison.Ordinal);
            var modelSource = modelResult?.NextActionDecision is not null
                ? "next-action-contract"
                : modelResult?.Decision is not null
                    ? "legacy-decision"
                    : "none";

            await _auditLogger.LogAsync(new AuditEvent
            {
                CorrelationId = request.CorrelationId,
                EventType = eventType,
                CommandText = request.UserInput,
                SafetyDisposition = string.Empty,
                RiskLevel = string.Empty,
                SelectedTool = "None",
                ExecutionMode = "not-executed",
                Outcome = eventType,
                Message = reason ?? string.Empty,
                ApprovalDecision = string.Empty,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["model_decision_enabled"] = _settings.Enabled ? "true" : "false",
                    ["model_decision_status"] = status.ToString(),
                    ["model_decision_reason"] = reason ?? string.Empty,
                    ["model_observation_package_available"] = request.ModelObservationPackage is null ? "false" : "true",
                    ["model_provider_type"] = _modelDecisionProvider.GetType().Name,
                    ["model_decision_used_live_path"] = usedLiveModelPath ? "true" : "false",
                    ["model_decision_used_fallback"] = usedFallbackPath ? "true" : "false",
                    ["model_next_action_contract_present"] = modelResult?.NextActionDecision is null ? "false" : "true",
                    ["model_decision_source"] = modelSource,
                    ["model_observation_correlation_id"] = request.ModelObservationPackage?.CorrelationId ?? string.Empty
                }
            }, cancellationToken);
        }
        catch
        {
            // Fail-closed: model seam logging failures must not change decision flow.
        }
    }
}
