using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Runtime.Policy;

public sealed class ActionGate
{
    private static readonly HashSet<string> AssistantForegroundBlockedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "type_text",
        "press_key",
        "press_shortcut",
        "click_element",
        "focus_element",
        "set_value",
        "select_element",
        "expand_collapse",
        "invoke_toggle",
        "scroll",
        "mouse_click",
        "mouse_scroll",
        "mouse_drag"
    };
    private static readonly HashSet<string> SafeActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "respond",
        "ask_user",
        "stop",
        "wait",
        "read_element",
        "list_windows",
        "wmi_query",
        "capture_screen",
        "event_log",
        "network_status",
        "perf_counter",
        "file_search",
        "audio_power"
    };

    private static readonly HashSet<string> KnownActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "respond", "ask_user", "stop", "wait",
        "open_app", "open_url", "type_text", "press_key", "press_shortcut",
        "click_element", "focus_element", "read_element", "set_value",
        "select_element", "expand_collapse", "invoke_toggle", "scroll",
        "focus_window", "window_state", "move_window", "list_windows", "launch",
        "mouse_click", "mouse_scroll", "mouse_drag", "shell",
        "capture_screen", "notify", "wmi_query", "schedule_task", "jump_list",
        "com_invoke", "verify_user", "global_hook",
        "service_control", "event_log", "registry_op", "clipboard", "install_package",
        "network_status", "audio_power", "perf_counter", "file_search", "notification_listen",
        "shell_session", "file_watch", "credential_store"
    };

    private static readonly string[] DestructiveShellPatterns =
    {
        "rm ", "rmdir", "rd ", "del ", "erase ", "remove-item", "remove-itemproperty",
        "clear-content", "format ", "format-volume", "diskpart", "shutdown",
        "restart-computer", "stop-computer", "stop-process", "taskkill /f",
        "reg delete", "cipher /w", "fsutil", "takeown", "icacls", "net user",
        "bcdedit", "mkfs", "dd if=", "-verb runas", "runas ", "sc delete",
        "schtasks /delete"
    };

    private readonly ActionPolicy _policy;
    private readonly ForegroundWindowService _foregroundWindow;
    private readonly HashSet<string> _sessionApprovals = new(StringComparer.OrdinalIgnoreCase);

    public ActionGate(ActionPolicy policy, ForegroundWindowService foregroundWindow)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _foregroundWindow = foregroundWindow ?? throw new ArgumentNullException(nameof(foregroundWindow));
    }

    public void BeginSession() => _sessionApprovals.Clear();

    public void RememberSessionApproval(string approvalKey)
    {
        if (_policy.AllowSessionRemember && !string.IsNullOrWhiteSpace(approvalKey))
        {
            _sessionApprovals.Add(approvalKey);
        }
    }

    public GateDecision Evaluate(AgentAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var assistantSelfBlock = TryDenyAssistantSelfAutomation(action);
        if (assistantSelfBlock is not null)
        {
            return assistantSelfBlock;
        }

        if (!KnownActions.Contains(action.Action))
        {
            return new GateDecision
            {
                Outcome = GateOutcome.Deny,
                Risk = ActionRisk.Sensitive,
                Reason = $"Desteklenmeyen action: '{action.Action}'.",
                Summary = $"Bilinmeyen eylem: {action.Action}"
            };
        }

        var risk = ClassifyRisk(action);
        var approvalKey = BuildApprovalKey(action, risk);
        var summary = BuildSummary(action, risk);

        if (risk == ActionRisk.Safe)
        {
            return Allow(risk, summary, approvalKey, "Guvenli eylem — otomatik.");
        }

        if (risk == ActionRisk.Destructive)
        {
            if (_sessionApprovals.Contains(approvalKey))
            {
                return Allow(risk, summary, approvalKey, "Bu oturumda daha once onaylandi.");
            }

            return new GateDecision
            {
                Outcome = GateOutcome.RequireApproval,
                Risk = risk,
                Reason = "Yikici veya geri alinmasi zor bir eylem — kullanici onayi gerekli.",
                Summary = summary,
                ApprovalKey = approvalKey
            };
        }

        var handling = risk switch
        {
            ActionRisk.Normal => _policy.Normal,
            ActionRisk.Sensitive => _policy.Sensitive,
            _ => RiskHandling.RequireApproval
        };

        if (handling == RiskHandling.Allow)
        {
            return Allow(risk, summary, approvalKey, "Politika: otomatik izin.");
        }

        if (handling == RiskHandling.Deny)
        {
            return new GateDecision
            {
                Outcome = GateOutcome.Deny,
                Risk = risk,
                Reason = $"Politika bu risk seviyesini engelliyor ({risk}).",
                Summary = summary,
                ApprovalKey = approvalKey
            };
        }

        if (_sessionApprovals.Contains(approvalKey))
        {
            return Allow(risk, summary, approvalKey, "Bu oturumda daha once onaylandi.");
        }

        return new GateDecision
        {
            Outcome = GateOutcome.RequireApproval,
            Risk = risk,
            Reason = $"Risk seviyesi {risk} — kullanici onayi gerekli.",
            Summary = summary,
            ApprovalKey = approvalKey
        };
    }

    private GateDecision? TryDenyAssistantSelfAutomation(AgentAction action)
    {
        if (!AssistantForegroundBlockedActions.Contains(action.Action))
        {
            return null;
        }

        var (_, processName, _) = _foregroundWindow.GetForegroundInfo();
        if (!AgentSelfWindow.IsAssistantProcess(processName))
        {
            return null;
        }

        return new GateDecision
        {
            Outcome = GateOutcome.Deny,
            Risk = ActionRisk.Normal,
            Reason =
                "Windows AI Assistant penceresine fiziksel/UI otomasyonu uygulanamaz. " +
                "Kullaniciya respond ile Turkce don.",
            Summary = $"{action.Action} engellendi (asistan penceresi odakta)"
        };
    }

    private static GateDecision Allow(ActionRisk risk, string summary, string approvalKey, string reason) =>
        new()
        {
            Outcome = GateOutcome.Allow,
            Risk = risk,
            Reason = reason,
            Summary = summary,
            ApprovalKey = approvalKey
        };

    private static ActionRisk ClassifyRisk(AgentAction action)
    {
        if (SafeActions.Contains(action.Action))
        {
            return ActionRisk.Safe;
        }

        return action.Action.ToLowerInvariant() switch
        {
            "window_state" => IsCloseState(action) ? ActionRisk.Destructive : ActionRisk.Normal,
            "set_value" => ActionRisk.Sensitive,
            "press_key" or "press_shortcut" => ActionRisk.Sensitive,
            "mouse_drag" => ActionRisk.Sensitive,
            "mouse_click" => UsesFreeCoordinates(action) ? ActionRisk.Sensitive : ActionRisk.Normal,
            "shell" => IsDestructiveShell(action) ? ActionRisk.Destructive : ActionRisk.Sensitive,
            "launch" => IsKnownLaunchTarget(action) ? ActionRisk.Normal : ActionRisk.Sensitive,
            "install_package" => IsPackageUninstall(action) ? ActionRisk.Destructive : ActionRisk.Sensitive,
            "registry_op" => IsRegistryWrite(action) ? ActionRisk.Sensitive : ActionRisk.Normal,
            "service_control" => IsServiceMutation(action) ? ActionRisk.Sensitive : ActionRisk.Normal,
            "open_app" or "open_url" or "type_text" or "click_element" or "focus_element" or
            "select_element" or "expand_collapse" or "invoke_toggle" or "scroll" or
            "focus_window" or "move_window" or "mouse_scroll" or "notify" or "jump_list" => ActionRisk.Normal,
            "schedule_task" or "com_invoke" or "global_hook" or
            "shell_session" or "file_watch" => ActionRisk.Sensitive,
            "clipboard" => IsClipboardWrite(action) ? ActionRisk.Sensitive : ActionRisk.Normal,
            "notification_listen" => IsNotificationRead(action) ? ActionRisk.Normal : ActionRisk.Sensitive,
            "credential_store" => IsCredentialMutation(action) ? ActionRisk.Sensitive : ActionRisk.Normal,
            "verify_user" => ActionRisk.Safe,
            _ => ActionRisk.Normal
        };
    }

    private static bool IsPackageUninstall(AgentAction action)
    {
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode");
        return mode?.Equals("uninstall", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsRegistryWrite(AgentAction action)
    {
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode");
        return mode?.Equals("write", StringComparison.OrdinalIgnoreCase) == true ||
               mode?.Equals("delete", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsServiceMutation(AgentAction action)
    {
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode");
        return mode is not null && (
            mode.Equals("start", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("restart", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsClipboardWrite(AgentAction action)
    {
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "read";
        return mode.Equals("write", StringComparison.OrdinalIgnoreCase) ||
               mode.Equals("set", StringComparison.OrdinalIgnoreCase) ||
               mode.Equals("clear", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNotificationRead(AgentAction action)
    {
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "peek";
        return mode.Equals("peek", StringComparison.OrdinalIgnoreCase) ||
               mode.Equals("read", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCredentialMutation(AgentAction action)
    {
        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "list";
        return mode.Equals("store", StringComparison.OrdinalIgnoreCase) ||
               mode.Equals("delete", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCloseState(AgentAction action)
    {
        var state = ActionParameterReader.GetTargetOrParameter(action, "state");
        return !string.IsNullOrWhiteSpace(state) &&
               state.Equals("close", StringComparison.OrdinalIgnoreCase);
    }

    private static bool UsesFreeCoordinates(AgentAction action)
    {
        var elementId = ActionParameterReader.GetTargetOrParameter(action, "elementId");
        if (!string.IsNullOrWhiteSpace(elementId))
        {
            return false;
        }

        return ActionParameterReader.TryGetInt(action, "x", out _) &&
               ActionParameterReader.TryGetInt(action, "y", out _);
    }

    private static bool IsDestructiveShell(AgentAction action)
    {
        var command = ActionParameterReader.GetTargetOrParameter(action, "command", "cmd", "script");
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var normalized = command.ToLowerInvariant();
        return DestructiveShellPatterns.Any(pattern =>
            normalized.Contains(pattern, StringComparison.Ordinal));
    }

    private static bool IsKnownLaunchTarget(AgentAction action)
    {
        var target = ActionParameterReader.GetTargetOrParameter(action, "app", "command", "uri");
        return AppLaunchCatalog.TryResolve(target, out _, out _);
    }

    private static string BuildApprovalKey(AgentAction action, ActionRisk risk)
    {
        var target = BuildApprovalTarget(action);
        return $"{action.Action}|{risk}|{target}".ToLowerInvariant();
    }

    private static string BuildApprovalTarget(AgentAction action)
    {
        return action.Action.ToLowerInvariant() switch
        {
            "shell" => ActionParameterReader.GetTargetOrParameter(action, "command", "cmd", "script") ?? string.Empty,
            "launch" => ActionParameterReader.GetTargetOrParameter(action, "app", "command", "uri") ?? string.Empty,
            "open_app" => ActionParameterReader.GetTargetOrParameter(action, "app") ?? string.Empty,
            "open_url" => ActionParameterReader.GetTargetOrParameter(action, "url") ?? string.Empty,
            "type_text" => ActionParameterReader.GetTargetOrParameter(action, "text") ?? string.Empty,
            "press_key" => ActionParameterReader.GetTargetOrParameter(action, "key") ?? string.Empty,
            "press_shortcut" => ActionParameterReader.GetTargetOrParameter(action, "shortcut", "keys") ?? string.Empty,
            "window_state" => $"{ActionParameterReader.GetTargetOrParameter(action, "windowId", "title")}|state={ActionParameterReader.GetTargetOrParameter(action, "state")}",
            "set_value" => $"{ActionParameterReader.GetTargetOrParameter(action, "elementId")}|value={ActionParameterReader.GetTargetOrParameter(action, "value", "text")}",
            "mouse_click" => ActionParameterReader.GetTargetOrParameter(action, "elementId") ??
                $"x={ActionParameterReader.GetTargetOrParameter(action, "x")},y={ActionParameterReader.GetTargetOrParameter(action, "y")}",
            "mouse_drag" =>
                $"from={ActionParameterReader.GetTargetOrParameter(action, "startX")},{ActionParameterReader.GetTargetOrParameter(action, "startY")};to={ActionParameterReader.GetTargetOrParameter(action, "endX")},{ActionParameterReader.GetTargetOrParameter(action, "endY")}",
            "move_window" =>
                $"{ActionParameterReader.GetTargetOrParameter(action, "windowId", "title")}|x={ActionParameterReader.GetTargetOrParameter(action, "x")},y={ActionParameterReader.GetTargetOrParameter(action, "y")},w={ActionParameterReader.GetTargetOrParameter(action, "width")},h={ActionParameterReader.GetTargetOrParameter(action, "height")}",
            _ => action.Target ?? string.Empty
        };
    }

    private static string BuildSummary(AgentAction action, ActionRisk risk)
    {
        var target = string.IsNullOrWhiteSpace(action.Target) ? "(hedef yok)" : action.Target;
        var extra = action.Action.ToLowerInvariant() switch
        {
            "window_state" => ActionParameterReader.GetTargetOrParameter(action, "state"),
            "set_value" => ActionParameterReader.GetTargetOrParameter(action, "value"),
            "type_text" => ActionParameterReader.GetTargetOrParameter(action, "text"),
            "shell" => ActionParameterReader.GetTargetOrParameter(action, "command", "cmd", "script"),
            "press_shortcut" or "press_key" => target,
            "mouse_click" when UsesFreeCoordinates(action) =>
                $"x={ActionParameterReader.GetTargetOrParameter(action, "x")}, y={ActionParameterReader.GetTargetOrParameter(action, "y")}",
            _ => string.Empty
        };

        var detail = string.IsNullOrWhiteSpace(extra) ? target : $"{target} ({extra})";
        return $"{action.Action} → {detail} [{risk}]";
    }
}
