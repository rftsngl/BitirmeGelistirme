using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Models;

public static class StepApprovalIdentityBuilder
{
    public static string? BuildForExecuteAction(ExecuteActionPayload? executeAction)
    {
        if (executeAction is null)
        {
            return null;
        }

        return BuildIdentity(
            semanticActionName: executeAction.ActionType.ToString(),
            targetReference: executeAction.Target.Reference,
            parameters: executeAction.Parameters.Values);
    }

    public static string? BuildForAgentAction(AgentAction? action)
    {
        if (action is null)
        {
            return null;
        }

        var semanticActionName = MapAgentActionToSemanticAction(action);
        return BuildIdentity(
            semanticActionName,
            action.Target?.NormalizedValue ??
            action.Target?.DisplayName ??
            action.Target?.OriginalText,
            action.Parameters);
    }

    private static string BuildIdentity(
        string semanticActionName,
        string? targetReference,
        IEnumerable<KeyValuePair<string, string>>? parameters)
    {
        var normalizedAction = Normalize(semanticActionName);
        var normalizedTarget = Normalize(targetReference);
        var parameterList = parameters?.ToList();
        var normalizedParameters = parameterList is null || parameterList.Count == 0
            ? string.Empty
            : string.Join(
                "&",
                parameterList
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => $"{Normalize(pair.Key)}={Normalize(pair.Value)}"));

        return $"{normalizedAction}|{normalizedTarget}|{normalizedParameters}";
    }

    private static string MapAgentActionToSemanticAction(AgentAction action)
    {
        if (action.ActionName.Equals("OpenApplication", StringComparison.OrdinalIgnoreCase))
        {
            return ActionType.Launch.ToString();
        }

        if (action.ActionName.Equals("FocusWindow", StringComparison.OrdinalIgnoreCase))
        {
            return ActionType.Focus.ToString();
        }

        if (action.ActionName.Equals("OpenExistingFile", StringComparison.OrdinalIgnoreCase))
        {
            return ActionType.OpenFile.ToString();
        }

        if (action.ActionName.StartsWith("Verify", StringComparison.OrdinalIgnoreCase))
        {
            return ActionType.Verify.ToString();
        }

        return string.IsNullOrWhiteSpace(action.ActionName)
            ? $"{Normalize(action.CapabilityName)}:unknown"
            : action.ActionName;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }
}
