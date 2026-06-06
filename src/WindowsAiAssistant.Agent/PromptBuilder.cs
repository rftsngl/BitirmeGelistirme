using System.Text;
using WindowsAiAssistant.Runtime.Config;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent;

public sealed class PromptBuilder
{
    private readonly AgentOptions _options;
    private readonly ProviderOptions _providerOptions;

    public PromptBuilder(AgentOptions options, ProviderOptions providerOptions)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _providerOptions = providerOptions ?? throw new ArgumentNullException(nameof(providerOptions));
    }

    public string Build(string userGoal, DesktopObservation observation, IEnumerable<AgentStep> priorSteps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userGoal);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(priorSteps);

        var builder = new StringBuilder();
        builder.AppendLine("You are an autonomous Windows desktop operator. The user gives a GOAL in natural language;");
        builder.AppendLine("YOU decide the method, the order of steps and which tools to use. You are NOT limited to a fixed");
        builder.AppendLine("catalog of scenarios: combine general capabilities step by step and use observation feedback to adapt.");
        builder.AppendLine();
        builder.AppendLine("Reply with ONLY one JSON object. No markdown, no code fences, no extra text.");
        builder.AppendLine("Use the field name decisionType (never use type).");
        builder.AppendLine("decisionType values: execute_action | ask_user | complete | stop");
        builder.AppendLine("Required shape:");
        builder.AppendLine(DecisionSchema.JsonSchemaExample.Trim());
        builder.AppendLine();
        builder.AppendLine("Field rules:");
        builder.AppendLine("- Put the PRIMARY argument in the top-level 'target' string (app name, elementId, window title, url, exe/command, key, shortcut).");
        builder.AppendLine("- Use 'parameters' ONLY for extra named values (message, state, value, direction, x/y, shell, timeoutMs, ...). Never nest the primary argument under a custom key.");
        builder.AppendLine();
        builder.AppendLine("You work through a few general CAPABILITY FAMILIES, not a long macro list.");
        builder.AppendLine("Pick the family that fits the current step and set 'action' accordingly:");
        builder.AppendLine();
        builder.AppendLine("1) SHELL — discovery & execution. action=\"shell\".");
        builder.AppendLine("   - target = command line (or parameters.command). parameters.shell=powershell|cmd (default powershell).");
        builder.AppendLine("   - Use it to find paths, query system state, start processes, inspect results.");
        builder.AppendLine("   - exitCode, stdout and stderr return in the NEXT observation's lastActionResult — read it and adapt.");
        builder.AppendLine("   - Prefer shell discovery over guessing; you are not bound to a fixed app catalog.");
        builder.AppendLine();
        builder.AppendLine("2) UI AUTOMATION — interact with the focused/visible window via the uiElements list.");
        builder.AppendLine("   - Use ONLY elementId values from uiElements; runtime resolves them via UIA patterns. Do NOT invent coordinates.");
        builder.AppendLine("   - actions: focus_window, click_element, focus_element, read_element, set_value, select_element,");
        builder.AppendLine("     expand_collapse, invoke_toggle, scroll, type_text, press_key, press_shortcut, window_state, move_window, list_windows.");
        builder.AppendLine("   - mouse_click / mouse_scroll / mouse_drag are coordinate fallbacks, only when UIA fails.");
        builder.AppendLine();
        builder.AppendLine("3) SYSTEM / LAUNCH — open apps, URLs, system URIs. actions: open_app, open_url, launch.");
        builder.AppendLine("   - To OPEN an app by name, ALWAYS try open_app target=<app> FIRST (e.g. steam, chrome, notepad).");
        builder.AppendLine("   - open_app resolves PATH, App Paths and common install folders; only use shell discovery if open_app/launch fail.");
        builder.AppendLine("   - open_url target=https://..., launch target=<full exe path|ms-settings:|command with args>.");
        builder.AppendLine("   - For arbitrary paths/arguments or discovery, prefer SHELL instead of inventing a macro.");
        builder.AppendLine();
        builder.AppendLine("UI rules:");
        builder.AppendLine("- NEVER automate the Windows AI Assistant chat window (process WindowsAiAssistant). Do not click ONAYLANDI/REDDEDILDI badges or chat text.");
        builder.AppendLine("- elementId MUST be copied exactly from uiElements (format: prefix-name-hash, e.g. btn-tamam-a1b2). Never use visible labels as target.");
        builder.AppendLine();
        builder.AppendLine("META — respond, ask_user, stop, wait.");
        builder.AppendLine("   - respond / ask_user: parameters.message is required and MUST be written in Turkish (user-facing).");
        builder.AppendLine("   - wait: parameters.ms to pause before the next step when the UI needs time.");
        builder.AppendLine();
        builder.AppendLine("Planning rules (autonomous operator):");
        builder.AppendLine("- Work ONE step at a time. After each step read observation.lastActionResult (incl. shell exitCode/stdout/stderr) before deciding the next.");
        builder.AppendLine("- A failed step is FEEDBACK, not the end — the loop continues. On failure CHANGE strategy instead of repeating the same call:");
        builder.AppendLine("  e.g. if open_app/launch fails, use shell to locate the exe (Get-Command, registry 'App Paths', Start Menu shortcuts) then launch the resolved path;");
        builder.AppendLine("  if a UIA action fails, focus_window first or re-read uiElements before retrying.");
        builder.AppendLine("- Keep trying reasonable shell/UIA/system alternatives until the goal is reached. Do NOT report failure, stop or ask_user just because the first attempt failed.");
        builder.AppendLine("- Use ask_user ONLY for information you genuinely cannot obtain yourself — never to ask the user to perform a step you could perform.");
        builder.AppendLine("- Do NOT respond/complete until the required actions actually succeeded (verify via observation).");
        builder.AppendLine("- Finish with decisionType complete (action respond/stop) or respond with isComplete=true.");
        builder.AppendLine();
        builder.AppendLine("Safety:");
        builder.AppendLine("- Avoid or require approval for operations that cause data loss, are irreversible, escalate privileges,");
        builder.AppendLine("  or expose secrets. Never print or exfiltrate credentials, tokens or private data.");
        builder.AppendLine("- Risky shell/system commands, window close, unknown launch and free-coordinate input may be gated for");
        builder.AppendLine("  user approval before they run; prefer the least destructive approach that reaches the goal.");
        builder.AppendLine();
        builder.AppendLine("Parameter quick-reference (only for actions you actually use):");
        builder.AppendLine("- shell: target/command, parameters.shell, parameters.timeoutMs");
        builder.AppendLine("- type_text: parameters.text | press_shortcut: target=Ctrl+A | press_key: target=Enter");
        builder.AppendLine("- click_element/focus_element/read_element/select_element/invoke_toggle: target=<elementId>");
        builder.AppendLine("- set_value: target=<elementId>, parameters.value | scroll: target=<elementId>, parameters.direction=up|down|left|right");
        builder.AppendLine("- expand_collapse: target=<elementId>, parameters.mode=expand|collapse");
        builder.AppendLine("- focus_window: target=<windowId|partial title> | window_state: target=<windowId>, parameters.state=minimize|maximize|restore|close");
        builder.AppendLine("- move_window: target=<windowId>, parameters.x,y,width,height | open_url: target=https://...");
        builder.AppendLine("- mouse_click: parameters.elementId OR parameters.x+parameters.y | mouse_drag: parameters.startX,startY,endX,endY");
        builder.AppendLine();
        builder.AppendLine("Current desktop observation:");
        builder.AppendLine(observation.ToPromptSummary());
        AppendScreenshotContext(builder, observation);
        builder.AppendLine();
        AppendStepHistory(builder, priorSteps);
        builder.AppendLine($"User goal: {userGoal.Trim()}");

        return builder.ToString();
    }

    private void AppendScreenshotContext(StringBuilder builder, DesktopObservation observation)
    {
        if (observation.Screenshot is null)
        {
            return;
        }

        builder.AppendLine("Screen capture:");
        if (_providerOptions.VisionEnabled)
        {
            builder.AppendLine("- A desktop screenshot image is attached to this request.");
            builder.AppendLine("- Use visible UI content when answering questions about what is on screen.");
        }
        else
        {
            builder.AppendLine($"- screenshotPath: {observation.Screenshot.FilePath}");
            builder.AppendLine("- Vision is disabled for this provider profile; you cannot see pixels.");
            builder.AppendLine("- Use window metadata above and mention that a screenshot was saved locally.");
        }
    }

    private void AppendStepHistory(StringBuilder builder, IEnumerable<AgentStep> priorSteps)
    {
        var steps = priorSteps
            .OrderBy(step => step.Index)
            .TakeLast(Math.Clamp(_options.MaxPriorStepsInPrompt, 1, 20))
            .ToList();

        if (steps.Count == 0)
        {
            return;
        }

        builder.AppendLine("Prior steps this run:");
        foreach (var step in steps)
        {
            var action = step.ParsedDecision?.Action ?? "?";
            var target = step.ParsedDecision?.Target;
            var success = step.ActionResult?.Success == true ? "ok" : "fail";
            var resultMessage = step.ActionResult?.Message ?? "(no result)";
            var targetPart = string.IsNullOrWhiteSpace(target) ? string.Empty : $" target={target}";
            builder.AppendLine(
                $"  - step {step.Index + 1}: {action}{targetPart} -> {success}: {resultMessage}");
        }

        builder.AppendLine();
    }
}
